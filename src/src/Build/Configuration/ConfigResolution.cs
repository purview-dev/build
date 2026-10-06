namespace Purview.Build.Configuration;

/// <summary>
/// How the repository configuration file was chosen.
/// </summary>
enum ConfigSource
{
	/// <summary>No configuration file was found. Entirely valid: every key is optional.</summary>
	None,

	/// <summary><c>--config</c> / <c>-c</c>.</summary>
	CommandLine,

	/// <summary><c>PURVIEW_BUILD_CONFIG</c>.</summary>
	EnvironmentVariable,

	/// <summary>Found by probing the documented locations.</summary>
	Probe,
}

/// <summary>
/// The outcome of locating configuration: which file won, how it was chosen, what was shadowed, and
/// whether machine-local user configuration took part.
/// </summary>
sealed record ConfigResolution
{
	/// <summary>Absolute path of the repository configuration file, or null when none was found.</summary>
	public string? Path { get; init; }

	public ConfigSource Source { get; init; } = ConfigSource.None;

	/// <summary>
	/// Every path considered, in order. Empty when an explicit location skipped probing.
	/// </summary>
	public IReadOnlyList<ConfigProbeResult> ProbeTrail { get; init; } = [];

	/// <summary>
	/// Existing files at lower-priority probe locations, which are NOT loaded. Reported as a
	/// warning: silent first-match-wins is how someone edits a file the tool never reads.
	/// </summary>
	public IReadOnlyList<string> ShadowedPaths { get; init; } = [];

	/// <summary>Resolved machine-local configuration file, when opted in, local, and present.</summary>
	public string? UserConfigPath { get; init; }

	/// <summary>True when <see cref="UserConfigPath"/> actually took part in the configuration chain.</summary>
	public bool UserConfigActive { get; init; }

	/// <summary>Why user configuration was not applied, when it was asked for but skipped.</summary>
	public string? UserConfigSkipReason { get; init; }

	/// <summary>
	/// A file in a probe directory whose name is close to <c>purview-build.json</c>, when nothing was
	/// found. Reported so an obvious typo is not mistaken for "no configuration".
	/// </summary>
	public string? NearMissPath { get; init; }

	public bool Found => Path is not null;
}

/// <summary>
/// One probe location and whether a file existed there.
/// </summary>
sealed record ConfigProbeResult(string RelativePath, string FullPath, bool Exists);
