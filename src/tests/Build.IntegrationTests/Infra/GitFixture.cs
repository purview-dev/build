using System.Diagnostics;

namespace Purview.Build.Infra;

/// <summary>
/// A throwaway git repository in a temp directory: a root <c>package.json</c> with a chosen version,
/// a chosen branch name, and a chosen set of tags.
/// </summary>
/// <remarks>
/// Repository-root resolution and ref/tag discovery are asserted against a real <c>.git</c> rather
/// than a mock, because what is being tested is precisely the interaction with git and the
/// filesystem. No network and no container: <c>git init</c> plus local commits only.
/// </remarks>
sealed class GitFixture : IDisposable
{
	GitFixture(string root)
	{
		Root = root;
	}

	public string Root { get; }

	/// <summary>
	/// Creates a repository with <paramref name="version"/> in <c>package.json</c>, checked out on
	/// <paramref name="branch"/>, with <paramref name="tags"/> already created.
	/// </summary>
	public static GitFixture Create(
		string version,
		string branch = "main",
		IEnumerable<string>? tags = null,
		string? configJson = null,
		string? configRelativePath = null
	)
	{
		var root = Path.Combine(Path.GetTempPath(), "purview-build-git-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		GitFixture fixture = new(root);

		try
		{
			File.WriteAllText(
				Path.Combine(root, "package.json"),
				$$"""{ "name": "fixture", "version": "{{version}}" }"""
			);

			if (configJson is not null)
			{
				var relative = configRelativePath ?? "purview-build.json";
				var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				File.WriteAllText(path, configJson);
			}

			fixture.Git("init", "--initial-branch", branch);
			// A fixture must not depend on the developer's global git identity.
			fixture.Git("config", "user.email", "fixture@purview.dev");
			fixture.Git("config", "user.name", "Purview Fixture");
			fixture.Git("config", "commit.gpgsign", "false");
			fixture.Git("add", ".");
			fixture.Git("commit", "-m", "chore: fixture");

			foreach (var tag in tags ?? [])
				fixture.Git("tag", tag);

			return fixture;
		}
		catch
		{
			fixture.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Writes a newline-delimited list into the fixture and returns its absolute path, for the
	/// <c>Release:Context:*</c> keys.
	/// </summary>
	public string WriteList(string fileName, IEnumerable<string> entries)
	{
		var path = Path.Combine(Root, fileName);
		File.WriteAllLines(path, entries);

		return path;
	}

	public string Git(params string[] arguments)
	{
		ProcessStartInfo startInfo = new("git")
		{
			WorkingDirectory = Root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		using var process =
			Process.Start(startInfo) ?? throw new InvalidOperationException("git could not be started.");

		var output = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();

		return process.ExitCode == 0
			? output
			: throw new InvalidOperationException(
				$"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {error}"
			);
	}

	/// <summary>
	/// Runs <paramref name="action"/> with the process working directory inside the fixture.
	/// </summary>
	/// <remarks>
	/// The tool resolves the repository root from the current directory, so a test that exercises
	/// that resolution has to actually change it. Restored afterwards.
	/// </remarks>
	public T InWorkingDirectory<T>(Func<T> action)
	{
		ArgumentNullException.ThrowIfNull(action);

		var previous = Environment.CurrentDirectory;

		try
		{
			Environment.CurrentDirectory = Root;

			return action();
		}
		finally
		{
			Environment.CurrentDirectory = previous;
		}
	}

	public void Dispose()
	{
		if (!Directory.Exists(Root))
			return;

		try
		{
			// git marks objects read-only, which blocks a plain recursive delete on Windows.
			foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
				File.SetAttributes(file, FileAttributes.Normal);

			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
			// A leftover temp directory is not worth failing a test over.
		}
		catch (UnauthorizedAccessException)
		{
			// As above.
		}
	}
}
