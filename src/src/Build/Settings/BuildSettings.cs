using System.ComponentModel.DataAnnotations;

namespace Purview.Build.Settings;

public sealed class BuildSettings
{
	public const string SectionName = "Build";

	public LogLevel LogLevel { get; init; } = LogLevel.Warning;

	/// <summary>
	/// The kind of repository the pipeline operates on. <see cref="ProjectType.DotNet"/> runs the
	/// dotnet-based modules (restore/build/test/pack/lint); <see cref="ProjectType.Web"/> runs the
	/// corresponding Bun-based commands from the repository's root package.json scripts.
	/// </summary>
	public ProjectType ProjectType { get; init; } = ProjectType.DotNet;

	[Required(AllowEmptyStrings = false)]
	public string Solution { get; init; } = "src/Product.slnx";

	[Required(AllowEmptyStrings = false)]
	public string Configuration { get; init; } = "Release";

	[Required(AllowEmptyStrings = false)]
	public string ArtifactsFolder { get; init; } = "artifacts";

	public bool RunTests { get; init; } = true;

	/// <summary>
	/// Root directory (relative to the repository root) under which test projects are discovered.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string TestRoot { get; init; } = "src/tests";

	/// <summary>
	/// Comma-separated project search patterns, recursively applied under <see cref="TestRoot"/>.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string TestPatterns { get; init; } = "*Tests.csproj";

	/// <summary>
	/// Comma-separated list of test project file names (or glob patterns) to run.
	/// Empty or "*" runs every discovered test project.
	/// </summary>
	public string TestProjects { get; init; } = "*";

	public TestFramework TestFramework { get; init; } = TestFramework.TUnit;

	/// <summary>
	/// Test filter. For TUnit this is a Microsoft.Testing.Platform tree-node filter
	/// (e.g. "/*/*/*/*[Category=Unit]"); for xUnit it is a VSTest filter (e.g. "Category=Unit").
	/// Empty disables the filter.
	/// </summary>
	public string TestFilter { get; init; } = "/*/*/*/*/";

	public bool RunLint { get; init; } = true;

	public bool RunPack { get; init; } = true;

	public bool ValidatePack { get; init; } = true;

	/// <summary>
	/// Install command used by <see cref="RestoreModule"/> for <see cref="ProjectType.Web"/> projects.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string WebInstallCommand { get; init; } = "bun install";

	/// <summary>
	/// Build command used by <see cref="BuildModule"/> for <see cref="ProjectType.Web"/> projects.
	/// When left at the default, the module first runs a <c>data:sync</c> script if one is declared.
	/// Override to take full control of the build (for example a chain of validation scripts).
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string WebBuildCommand { get; init; } = "bun run build";

	/// <summary>
	/// Lint command used by <see cref="LintModule"/> for <see cref="ProjectType.Web"/> projects.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string WebLintCommand { get; init; } = "bun run lint";

	/// <summary>
	/// Format-check command used by <see cref="LintModule"/> for <see cref="ProjectType.Web"/> projects.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string WebFormatCheckCommand { get; init; } = "bun run format:check";

	/// <summary>
	/// Test command used by <see cref="RunTestsModule"/> for <see cref="ProjectType.Web"/> projects.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string WebTestCommand { get; init; } = "bun run test";

	/// <summary>
	/// Directory (relative to the repository root) whose contents <see cref="PackModule"/> zips into
	/// <c>Build:ArtifactsFolder</c> for <see cref="ProjectType.Web"/> projects.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	public string WebBuildOutput { get; init; } = "src/dist";
}
