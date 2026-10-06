using NuGet.Versioning;

namespace Purview.Build.Release;

/// <summary>
/// The outside world an eligibility evaluation is judged against: which ref is being released, and
/// which versions already exist.
/// </summary>
/// <param name="Ref">The git ref being evaluated.</param>
/// <param name="RefSource">Where <paramref name="Ref"/> came from, for reporting.</param>
/// <param name="ExistingTags">Every tag that already exists.</param>
/// <param name="PublishedVersions">Versions already on the target feed.</param>
/// <param name="PublishedVersionsKnown">
/// False when the feed was not consulted (no network, or no simulated list supplied). Rules that
/// depend on feed state then report why they could not judge rather than passing silently.
/// </param>
/// <param name="Simulated">
/// True when any <c>Release:Context:*</c> key supplied this context. A simulated verdict must never
/// be mistakable for a real one, so this is surfaced in every report.
/// </param>
public sealed record ReleaseContext(
	string Ref,
	string RefSource,
	IReadOnlyList<string> ExistingTags,
	IReadOnlyList<NuGetVersion> PublishedVersions,
	bool PublishedVersionsKnown,
	bool Simulated
)
{
	public static ReleaseContext ForRef(string gitRef, string refSource, params string[] existingTags) =>
		new(gitRef, refSource, existingTags, [], PublishedVersionsKnown: false, Simulated: false);

	/// <summary>
	/// Whether a tag already exists, compared ordinally — the same comparison
	/// <c>git rev-parse "$TAG"</c> effectively performed in the workflow this replaces.
	/// </summary>
	public bool HasTag(string tag) => ExistingTags.Contains(tag, StringComparer.Ordinal);

	/// <summary>
	/// Every known version, from tags that look like <c>v{version}</c> plus the feed when known.
	/// Used by the monotonic-version rule.
	/// </summary>
	public IReadOnlyList<NuGetVersion> KnownVersions()
	{
		List<NuGetVersion> versions = [.. PublishedVersions];

		foreach (var tag in ExistingTags)
		{
			if (!tag.StartsWith('v'))
				continue;

			if (NuGetVersion.TryParse(tag[1..], out var version))
				versions.Add(version);
		}

		return versions;
	}
}
