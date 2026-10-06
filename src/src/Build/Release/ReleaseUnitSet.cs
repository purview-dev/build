using NuGet.Versioning;

namespace Purview.Build.Release;

/// <summary>
/// The ordered set of release units a run produces, resolved once and read by every downstream
/// module.
/// </summary>
/// <remarks>
/// Every current version source yields exactly one unit, but the set is the contract so adding a
/// second requires no change in <c>PackModule</c> or <c>CreateGitHubReleaseModule</c>.
/// </remarks>
public sealed record ReleaseUnitSet(string RawVersion, IReadOnlyList<ReleaseUnit> Units, string Source)
{
	/// <summary>
	/// The first unit. Modules that pack or tag a single version use this; modules that must handle
	/// every unit enumerate <see cref="Units"/>.
	/// </summary>
	public ReleaseUnit Primary =>
		Units.Count > 0
			? Units[0]
			: throw new InvalidOperationException("The version source produced no release units.");

	/// <summary>
	/// The primary unit's version, for the many call sites that only need the version.
	/// </summary>
	public NuGetVersion Version => Primary.Version;
}
