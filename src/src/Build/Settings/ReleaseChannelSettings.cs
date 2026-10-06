namespace Purview.Build.Settings;

/// <summary>
/// A named release channel: where packages go, whether a GitHub release is cut, and which
/// prerelease labels are allowed.
/// </summary>
/// <remarks>
/// A channel exists so a <c>preview</c> line can publish to a different feed with
/// <c>GitHubRelease: false</c> — dispatch-from-any-branch preview packages without cutting a GitHub
/// release. Every member is nullable so "not configured for this channel" is distinguishable from
/// "configured to false", and the top-level <c>Release</c> setting is used as the fallback.
/// </remarks>
public sealed record ReleaseChannelSettings
{
	public const string StableChannelName = "stable";

	/// <summary>
	/// Feed this channel publishes to. Falls back to <c>NuGet:FeedUrl</c>.
	/// </summary>
	public string? FeedUrl { get; init; }

	/// <summary>
	/// Whether a GitHub release is created for this channel. Falls back to the resolved
	/// <c>Release:GitHubRelease</c>.
	/// </summary>
	public bool? GitHubRelease { get; init; }

	/// <summary>
	/// Whether a prerelease version is marked as a GitHub prerelease. Falls back to
	/// <c>Release:MarkPrerelease</c>.
	/// </summary>
	public bool? MarkPrerelease { get; init; }

	/// <summary>
	/// Pattern the prerelease label must match (<c>REL007</c>), for example <c>prerelease</c> or
	/// <c>preview</c>. A trailing <c>*</c> is a prefix match. Null disables the check even when
	/// <c>REL007</c> is enabled.
	/// </summary>
	public string? LabelPattern { get; init; }

	/// <summary>
	/// The channel a repository that configures nothing releases on.
	/// </summary>
	public static ReleaseChannelSettings Stable { get; } = new();
}
