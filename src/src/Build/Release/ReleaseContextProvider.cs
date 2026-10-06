using NuGet.Versioning;
using System.Diagnostics;
using System.Text.Json;

namespace Purview.Build.Release;

/// <summary>
/// Builds the <see cref="ReleaseContext"/> an evaluation is judged against.
/// </summary>
/// <remarks>
/// Every input can be supplied through <c>Release:Context:*</c> so eligibility is answerable on a
/// developer machine, offline, without being on the branch. When any of them is supplied, no process
/// is launched and no network call is made, and the context is marked simulated so a report can
/// never present a simulated verdict as a real one.
///
/// The feed half of REL002 is only evaluated from a supplied
/// <c>Release:Context:PublishedVersions</c> list. Querying the feed is deliberately not done: the
/// gate this replaces only ever checked the tag, publication is already idempotent
/// (<c>--skip-duplicate</c>), and a mandatory feed query would make the offline scenario matrices
/// impossible.
/// </remarks>
static class ReleaseContextProvider
{
	public static ReleaseContext Resolve(ReleaseContextSettings settings, string repositoryRoot)
	{
		ArgumentNullException.ThrowIfNull(settings);

		var simulated = settings.IsSimulated;

		var (gitRef, refSource) = ResolveRef(settings, repositoryRoot, simulated);
		var tags = ResolveTags(settings, repositoryRoot, simulated);
		var (publishedVersions, publishedKnown) = ResolvePublishedVersions(settings);

		return new ReleaseContext(
			Ref: gitRef,
			RefSource: refSource,
			ExistingTags: tags,
			PublishedVersions: publishedVersions,
			PublishedVersionsKnown: publishedKnown,
			Simulated: simulated
		);
	}

	static (string Ref, string Source) ResolveRef(
		ReleaseContextSettings settings,
		string repositoryRoot,
		bool simulated
	)
	{
		if (!string.IsNullOrWhiteSpace(settings.Ref))
			return (Normalize(settings.Ref), "Release:Context:Ref (simulated)");

		var githubRef = Environment.GetEnvironmentVariable("GITHUB_REF");
		if (!string.IsNullOrWhiteSpace(githubRef))
			return (githubRef, "GITHUB_REF");

		// A simulated evaluation must not shell out, so an unspecified ref stays unspecified
		// rather than silently picking up the developer's current branch.
		if (simulated)
			return (string.Empty, "unspecified (simulated context supplied no ref)");

		var branch = RunGit(repositoryRoot, "rev-parse", "--abbrev-ref", "HEAD");

		return string.IsNullOrWhiteSpace(branch)
			? (string.Empty, "unknown (not a git repository, and GITHUB_REF is unset)")
			: (Normalize(branch.Trim()), "the current git branch");
	}

	static string[] ResolveTags(
		ReleaseContextSettings settings,
		string repositoryRoot,
		bool simulated
	)
	{
		if (!string.IsNullOrWhiteSpace(settings.ExistingTags))
			return ReadList(settings.ExistingTags, "Release:Context:ExistingTags");

		if (simulated)
			return [];

		var output = RunGit(repositoryRoot, "tag", "--list");

		return string.IsNullOrWhiteSpace(output)
			? []
			: [
				.. output.Split(
					['\r', '\n'],
					StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
				),
			];
	}

	static (IReadOnlyList<NuGetVersion> Versions, bool Known) ResolvePublishedVersions(
		ReleaseContextSettings settings
	)
	{
		if (string.IsNullOrWhiteSpace(settings.PublishedVersions))
			return ([], false);

		var entries = ReadList(settings.PublishedVersions, "Release:Context:PublishedVersions");

		List<NuGetVersion> versions = [];
		foreach (var entry in entries)
		{
			if (NuGetVersion.TryParse(entry, out var version))
				versions.Add(version);
		}

		return (versions, true);
	}

	/// <summary>
	/// Reads a newline- or JSON-delimited list from a file.
	/// </summary>
	static string[] ReadList(string path, string origin)
	{
		var fullPath = Path.GetFullPath(path);

		if (!File.Exists(fullPath))
			throw new InvalidOperationException(
				$"{origin} points at '{path}', which does not exist (resolved to '{fullPath}')."
			);

		var content = File.ReadAllText(fullPath).Trim();
		if (content.Length == 0)
			return [];

		if (content.StartsWith('['))
		{
			try
			{
				return JsonSerializer.Deserialize<string[]>(content) ?? [];
			}
			catch (JsonException exception)
			{
				throw new InvalidOperationException(
					$"{origin} points at '{fullPath}', which starts with '[' but is not a valid JSON array "
						+ $"of strings: {exception.Message}",
					exception
				);
			}
		}

		return [
			.. content.Split(
				['\r', '\n'],
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
			),
		];
	}

	static string Normalize(string gitRef) =>
		gitRef.StartsWith("refs/", StringComparison.Ordinal) ? gitRef : $"refs/heads/{gitRef}";

	/// <summary>
	/// Runs git and returns stdout, or null when git is unavailable or the command failed.
	/// </summary>
	/// <remarks>
	/// Failure is not an error: eligibility is answerable without git (every input can be supplied),
	/// and the rules report what they could not determine rather than guessing.
	/// </remarks>
	static string? RunGit(string workingDirectory, params string[] arguments)
	{
		try
		{
			ProcessStartInfo startInfo = new("git")
			{
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			foreach (var argument in arguments)
				startInfo.ArgumentList.Add(argument);

			using var process = Process.Start(startInfo);
			if (process is null)
				return null;

			var output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			return process.ExitCode == 0 ? output : null;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return null;
		}
	}
}
