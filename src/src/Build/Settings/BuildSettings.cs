using System.ComponentModel.DataAnnotations;

namespace Purview.Build.Settings;

public sealed class BuildSettings
{
	public const string SectionName = "Build";

	public LogLevel LogLevel { get; init; } = LogLevel.Information;

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

	/// <summary>
	/// When true, <see cref="CleanArtifactsModule"/> deletes and recreates <see cref="ArtifactsFolder"/> before
	/// the pipeline produces any output, so validation, publishing, and release uploads only ever see the
	/// artifacts from the current run. Ignored when <see cref="RunPack"/> is false (nothing will be packed, so
	/// existing artifacts, such as a folder being inspected ahead of a publish, are left untouched).
	/// </summary>
	public bool CleanArtifacts { get; init; } = true;

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
	/// Maximum number of test projects run concurrently. <c>0</c> (the default) uses
	/// <see cref="Environment.ProcessorCount"/>; <c>1</c> runs them one at a time.
	/// </summary>
	/// <remarks>
	/// Every test project is its own <c>dotnet test</c> process, and each test runner parallelises
	/// internally on top of that. Launching all of them at once oversubscribes the machine on a large
	/// solution: the projects compete for the same cores, the slowest one finishes later than it would
	/// have alone, and memory pressure rises with the count rather than with the bound.
	/// </remarks>
	[Range(0, 1024)]
	public int MaxTestParallelism { get; init; }

	/// <summary>
	/// How many tests a single test project runs concurrently (TUnit's
	/// <c>--maximum-parallel-tests</c>). <c>0</c> (the default) derives three quarters of
	/// <see cref="Environment.ProcessorCount"/>; a negative value hands the decision back to the runner.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the second of two parallelism bounds and they multiply: <see cref="MaxTestParallelism"/>
	/// governs how many test <em>projects</em> run at once, this one how many tests run inside each. A
	/// suite whose tests spawn processes — an MSBuild harness, say — saturates the machine well before
	/// either bound is reached, which is why both default to less than "as much as possible".
	/// </para>
	/// <para>
	/// The runner's own auto-detection assumes cheap, CPU-bound tests. On a 416-test harness suite it was
	/// the slowest setting measured, 36% behind three quarters of the core count.
	/// </para>
	/// </remarks>
	[Range(-1, 1024)]
	public int MaxParallelTestsPerProject { get; init; }

	/// <summary>
	/// Test filter. For TUnit this is a Microsoft.Testing.Platform tree-node filter
	/// (e.g. "/*/*/*/*[Category=Unit]"); for xUnit it is a VSTest filter (e.g. "Category=Unit").
	/// Empty disables the filter.
	/// </summary>
	public string TestFilter { get; init; } = "/*/*/*/*/";

	public bool RunLint { get; init; } = true;

	/// <summary>
	/// Path (relative to the repository root, or absolute) to a CSharpier CLI assembly (a <c>.dll</c>)
	/// to run with <c>dotnet</c> instead of the repository's <c>dotnet-tools.json</c> manifest tool.
	/// Use when a repository needs an unreleased CSharpier build (for example C# 15 union support);
	/// leave unset to run the manifest tool.
	/// </summary>
	public string? CSharpierPath { get; init; }

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
