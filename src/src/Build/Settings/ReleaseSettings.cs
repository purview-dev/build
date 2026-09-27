using NuGet.Versioning;

namespace Purview.Build.Settings;

public sealed record ReleaseSettings
{
	public const string SectionName = "Release";

	public ReleaseMode Mode { get; set; } = ReleaseMode.None;

	/// <summary>
	/// When true, the GitHub release module uploads every file in <c>Build:ArtifactsFolder</c>
	/// (for example .nupkg/.snupkg or .vsix) as release assets.
	/// </summary>
	public bool UploadArtifacts { get; init; }

	/// <summary>
	/// When true (the default), a GitHub release whose version is a prerelease (for example
	/// <c>2.0.0-prerelease.25</c>) is created as a prerelease, so it is not presented as the latest
	/// stable release. Set to false to publish prerelease versions as stable releases.
	/// </summary>
	public bool MarkPrerelease { get; init; } = true;

	/// <summary>
	/// Returns whether a release for <paramref name="version"/> should be created as a GitHub
	/// prerelease.
	/// </summary>
	public bool ShouldMarkPrerelease(NuGetVersion version)
	{
		ArgumentNullException.ThrowIfNull(version);

		return MarkPrerelease && version.IsPrerelease;
	}
}
