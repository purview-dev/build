using System.Text.RegularExpressions;

namespace Purview.Build;

/// <summary>
/// This repository builds itself: every entry point that runs the pipeline against this working tree
/// must run the tool compiled from this commit, never a <c>Purview.Build</c> resolved from a feed.
/// </summary>
/// <remarks>
/// A released build standing in for the local one is silent and plausible — the pipeline runs, the
/// output looks right, and the change under test was never exercised. On a release run it is worse:
/// the previous version would produce and publish the new one.
/// <para>
/// The reusable workflows (<c>purview-build.yml</c>, <c>purview-release.yml</c>) and the composite
/// action are deliberately excluded. They exist to install the published tool for *consuming*
/// repositories, which is the opposite requirement.
/// </para>
/// </remarks>
public class SelfHostingTests
{
	/// <summary>
	/// This repository's own CI/CD, as opposed to the reusable workflows it publishes for consumers.
	/// </summary>
	static readonly string[] SelfHostedWorkflows = [".github/workflows/ci.yml", ".github/workflows/release.yml"];

	public static IEnumerable<Func<string>> Workflows() =>
		SelfHostedWorkflows.Select<string, Func<string>>(file => () => file);

	[Test]
	[MethodDataSource(nameof(Workflows))]
	public async Task Workflow_InstallsTheToolOnlyFromTheLocallyPackedArtifacts(string workflowFile)
	{
		// Arrange
		var content = ReadRepositoryFile(workflowFile);

		// Act
		var installs = ToolInstallCommands(content);

		// Assert
		await Assert
			.That(installs)
			.IsNotEmpty()
			.Because($"{workflowFile} is expected to install the tool it packed from source.");

		foreach (var install in installs)
		{
			await Assert
				.That(install)
				.DoesNotContain("--add-source")
				.Because(
					$"{workflowFile} installs Purview.Build with --add-source, which leaves nuget.org in the "
						+ "source list. Once a version has shipped, NuGet may satisfy the install from the feed "
						+ "instead of the package just packed from this commit. Use --source."
				);

			await Assert
				.That(install)
				.Contains("--source artifacts")
				.Because(
					$"{workflowFile} must restrict the install to the artifacts packed from this commit, so a "
						+ "released Purview.Build can never stand in for the build under test."
				);
		}
	}

	[Test]
	public async Task Justfile_RunsThePipelineFromTheLocalBuild()
	{
		// Arrange
		var content = ReadRepositoryFile("Justfile");

		// Act
		var pipelineTool = Assignment("pipeline_tool").Match(content);

		// Assert
		await Assert.That(pipelineTool.Success).IsTrue().Because("Justfile no longer defines 'pipeline_tool'.");

		await Assert
			.That(pipelineTool.Groups["value"].Value.Trim())
			.IsEqualTo("local_tool")
			.Because(
				"the pipeline recipes must run the binary built from the working tree. 'dotnet purview-build' "
					+ "resolves a tool from the manifest or a feed, so a local pipeline run would exercise a "
					+ "released version rather than the change being made."
			);
	}

	[Test]
	public async Task ToolManifest_DoesNotPinThisRepositorysOwnTool()
	{
		// Arrange
		var content = ReadRepositoryFile(".config/dotnet-tools.json");

		// Act & Assert
		await Assert
			.That(content)
			.DoesNotContain("purview.build", StringComparison.OrdinalIgnoreCase)
			.Because(
				"pinning Purview.Build in this repository's own tool manifest makes `dotnet tool restore` fetch "
					+ "a released version of the tool this repository produces. It builds itself instead — see "
					+ "the 'build-local-tool' recipe."
			);
	}

	static string ReadRepositoryFile(string relativePath)
	{
		var root = RepositoryRoot();
		var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

		return File.ReadAllText(path);
	}

	static string RepositoryRoot()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			if (File.Exists(Path.Combine(directory.FullName, "package.json")))
				return directory.FullName;
		}

		throw new InvalidOperationException($"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
	}

	/// <summary>
	/// Every <c>dotnet tool install Purview.Build</c> invocation, with its arguments.
	/// </summary>
	/// <remarks>
	/// The workflows write the command in both the block-scalar (<c>run: |</c>, one line) and folded
	/// (<c>run: &gt;-</c>, one argument per line) forms, so the arguments are collected by reading on
	/// to the end of the step rather than by matching a command shape.
	/// </remarks>
	static List<string> ToolInstallCommands(string content)
	{
		List<string> commands = [];
		var lines = content.ReplaceLineEndings("\n").Split('\n');

		for (var index = 0; index < lines.Length; index++)
		{
			if (!lines[index].Contains("dotnet tool install Purview.Build", StringComparison.OrdinalIgnoreCase))
				continue;

			List<string> command = [lines[index]];
			for (var next = index + 1; next < lines.Length && lines[next].Trim().Length > 0; next++)
				command.Add(lines[next]);

			commands.Add(string.Join('\n', command));
		}

		return commands;
	}

	static Regex Assignment(string name) => new($@"^{Regex.Escape(name)}\s*:=\s*(?<value>.+)$", RegexOptions.Multiline);
}
