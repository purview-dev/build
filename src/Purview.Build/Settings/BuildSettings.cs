using System.ComponentModel.DataAnnotations;

namespace Purview.Build.Settings;

public enum TestFramework
{
	TUnit,

	xUnit,
}

public sealed class BuildSettings
{
	public const string SectionName = "Build";

	public LogLevel LogLevel { get; init; } = LogLevel.Warning;

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
}