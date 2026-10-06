using NuGet.Versioning;

namespace Purview.Build.Release;

/// <summary>
/// Everything one evaluation needs: the raw version as written, the release unit it produced (null
/// when it would not parse), the strictness it is judged by, and the context it is judged against.
/// </summary>
/// <remarks>
/// The raw string is carried separately from <see cref="Unit"/> because <c>REL001</c> judges the
/// text — four-part and one/two-part forms parse as <see cref="NuGetVersion"/> but are not SemVer 2,
/// and the distinction is lost once parsed.
/// </remarks>
public sealed record EligibilityInput(
	string RawVersion,
	ReleaseUnit? Unit,
	VersionStrictness Strictness,
	ReleaseContext Context,
	ReleaseChannelSettings Channel
)
{
	/// <summary>
	/// Builds an input the way the pipeline does, deriving the unit from the raw version when it
	/// parses at all.
	/// </summary>
	public static EligibilityInput Create(
		string rawVersion,
		VersionStrictness strictness,
		ReleaseContext context,
		string channelName,
		ReleaseChannelSettings channel,
		string versionSource
	)
	{
		ArgumentNullException.ThrowIfNull(context);

		var unit = ReleaseUnitFactory.TryCreate(rawVersion, channelName, versionSource);

		return new(rawVersion, unit, strictness, context, channel);
	}

	/// <summary>
	/// The minimal input the characterisation tests use: default strictness, default channel, and a
	/// ref with a set of existing tags.
	/// </summary>
	public static EligibilityInput ForCharacterisation(
		string rawVersion,
		string gitRef,
		params string[] existingTags
	) =>
		Create(
			rawVersion,
			VersionStrictness.NuGet,
			ReleaseContext.ForRef(gitRef, "test", existingTags),
			ReleaseChannelSettings.StableChannelName,
			ReleaseChannelSettings.Stable,
			nameof(VersionSource.PackageJson)
		);

	/// <summary>
	/// True when the raw version has four numeric components, for example <c>2.0.1.1</c>.
	/// </summary>
	public bool IsFourPart => ReleaseUnitFactory.IsFourPart(RawVersion);
}
