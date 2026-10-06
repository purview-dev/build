namespace Purview.Build.Configuration;

/// <summary>
/// Where the tool looks for <c>purview-build.json</c> when no explicit location is given.
/// </summary>
/// <remarks>
/// Held as data rather than a hard-coded chain, so adding a location is a single entry, the order is
/// printable by <c>--help</c>, and the probe trail can be reported verbatim.
/// </remarks>
static class ConfigProbePaths
{
	public const string FileName = "purview-build.json";

	/// <summary>
	/// Probe paths relative to the repository root, in order. First match wins.
	/// </summary>
	public static IReadOnlyList<ConfigProbePath> All { get; } =
		[
			new(FileName, "Repository root — the original location, so no existing repository moves."),
			new(
				$".config/{FileName}",
				"Beside .config/dotnet-tools.json and .config/lefthook.yml."
			),
			new($".build/{FileName}", "Build-tooling folder."),
			new($"build/{FileName}", "Historic home of the vendored build/PipelineCLI."),
			new($".purview/{FileName}", "Tool-namespaced, beside .agents/."),
			new($".github/{FileName}", "Keeps CI configuration with the workflows that drive it."),
		];
}

/// <summary>
/// One probe location and why it is on the list.
/// </summary>
sealed record ConfigProbePath(string RelativePath, string Rationale);
