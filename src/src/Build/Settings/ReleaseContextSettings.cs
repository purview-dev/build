namespace Purview.Build.Settings;

/// <summary>
/// Simulated release context, so eligibility can be evaluated on a developer machine without being
/// on the branch or having the tags and feed state the rules judge against.
/// </summary>
/// <remarks>
/// When any member is set, evaluation performs no network calls and every report states that
/// simulated context was used — a simulated verdict must never be mistakable for a real one. A
/// simulated context can therefore never drive a real publish; see
/// <c>ReleaseSettings.ValidateSimulationInterlock</c>.
/// </remarks>
public sealed record ReleaseContextSettings
{
	public const string SectionName = "Context";

	/// <summary>
	/// Overrides the evaluated ref. Defaults to <c>GITHUB_REF</c>, then the current git branch.
	/// </summary>
	public string? Ref { get; init; }

	/// <summary>
	/// Path to a newline- or JSON-delimited tag list, used instead of querying git.
	/// </summary>
	public string? ExistingTags { get; init; }

	/// <summary>
	/// Path to a newline- or JSON-delimited version list treated as already on the feed, used
	/// instead of querying it.
	/// </summary>
	public string? PublishedVersions { get; init; }

	/// <summary>
	/// True when any simulated value is present.
	/// </summary>
	public bool IsSimulated =>
		!string.IsNullOrWhiteSpace(Ref)
		|| !string.IsNullOrWhiteSpace(ExistingTags)
		|| !string.IsNullOrWhiteSpace(PublishedVersions);
}
