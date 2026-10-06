using NuGet.Versioning;

namespace Purview.Build.Release;

/// <summary>
/// Turns a raw version string into a <see cref="ReleaseUnit"/>.
/// </summary>
/// <remarks>
/// Shared by the version providers and by eligibility evaluation, so the tag, line, channel and
/// label a rule judges are exactly the ones the pipeline would act on.
/// </remarks>
static class ReleaseUnitFactory
{
	/// <summary>
	/// Builds a unit, or null when <paramref name="rawVersion"/> does not parse at all.
	/// </summary>
	public static ReleaseUnit? TryCreate(string? rawVersion, string channelName, string versionSource)
	{
		if (string.IsNullOrWhiteSpace(rawVersion) || !NuGetVersion.TryParse(rawVersion, out var version))
			return null;

		return new ReleaseUnit(
			Id: version.ToNormalizedString(),
			Version: version,
			IsPrerelease: version.IsPrerelease,
			PrereleaseLabel: ReleaseUnit.LabelOf(version),
			Line: ReleaseUnit.LineOf(version),
			Channel: channelName,
			// ToString() preserves the version exactly as written in package.json, which is what the
			// workflow's `TAG="v$VERSION"` produced. ToNormalizedString() would drop a trailing ".0"
			// revision and desynchronise the tag from the declared version.
			Tag: $"v{version}",
			Source: versionSource
		);
	}

	/// <summary>
	/// Whether a raw version string has four numeric components, for example <c>2.0.1.1</c>.
	/// </summary>
	public static bool IsFourPart(string? rawVersion) =>
		!string.IsNullOrWhiteSpace(rawVersion)
		&& rawVersion.Split('-', '+')[0].Count(character => character == '.') == 3;

	/// <summary>
	/// Whether a raw version string satisfies the configured strictness.
	/// </summary>
	public static bool SatisfiesStrictness(string? rawVersion, VersionStrictness strictness)
	{
		if (string.IsNullOrWhiteSpace(rawVersion))
			return false;

		return strictness == VersionStrictness.SemVer2
			? SemanticVersion.TryParse(rawVersion, out _)
			: NuGetVersion.TryParse(rawVersion, out _);
	}
}
