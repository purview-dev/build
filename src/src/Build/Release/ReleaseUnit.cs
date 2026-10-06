using NuGet.Versioning;

namespace Purview.Build.Release;

/// <summary>
/// One thing that can be released: a version, the line it belongs to, the channel it goes to, and
/// the tag it would carry.
/// </summary>
/// <remarks>
/// Modelled as an ordered set even though every current version source yields exactly one unit, so a
/// future multi-unit source needs no change in <c>PackModule</c> or <c>CreateGitHubReleaseModule</c>.
/// A reference type (not a <c>readonly record struct</c>) because it is passed around as an
/// immutable module result and compared by value in reporting.
/// </remarks>
public sealed record ReleaseUnit(
	string Id,
	NuGetVersion Version,
	bool IsPrerelease,
	string? PrereleaseLabel,
	string Line,
	string Channel,
	string Tag,
	string Source
)
{
	/// <summary>
	/// The release line a version belongs to: its MAJOR.MINOR pair, for example <c>2.0</c> for
	/// <c>2.0.2</c>. Two versions on the same line are serviced together.
	/// </summary>
	public static string LineOf(NuGetVersion version)
	{
		ArgumentNullException.ThrowIfNull(version);

		return $"{version.Major}.{version.Minor}";
	}

	/// <summary>
	/// The prerelease label without its numeric counter, for example <c>prerelease</c> for
	/// <c>2.1.0-prerelease.4</c>. Null for a stable version.
	/// </summary>
	public static string? LabelOf(NuGetVersion version)
	{
		ArgumentNullException.ThrowIfNull(version);

		if (!version.IsPrerelease || string.IsNullOrEmpty(version.Release))
			return null;

		var firstPart = version.Release.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
		return firstPart;
	}
}
