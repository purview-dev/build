namespace Purview.Build.Settings;

public enum ReleaseMode
{
	None,

	NuGet,

	GitHubRelease,

	LocalNuGet,
}

public sealed record ReleaseSettings
{
	public const string SectionName = "Release";

	public ReleaseMode Mode { get; set; } = ReleaseMode.None;

	/// <summary>
	/// When true, the GitHub release module uploads every file in <c>Build:ArtifactsFolder</c>
	/// (for example .nupkg/.snupkg or .vsix) as release assets.
	/// </summary>
	public bool UploadArtifacts { get; init; }
}