using Purview.Build.Release.Rules;

namespace Purview.Build.Release;

/// <summary>
/// The rule registry. Adding a rule means adding one implementation and one entry here; policies
/// then select it by ID.
/// </summary>
static class EligibilityRules
{
	/// <summary>
	/// Every rule, ordered by ID. Evaluation order is ID order, so the registry's order is the
	/// documented order.
	/// </summary>
	public static IReadOnlyList<IEligibilityRule> All { get; } =
		[
			new Rel001VersionParsesRule(),
			new Rel002NotAlreadyReleasedRule(),
			new Rel003StableFromStableRefRule(),
			new Rel004ServicingPatchRule(),
			new Rel005MonotonicVersionRule(),
			new Rel006FourPartVersionRule(),
			new Rel007PrereleaseLabelRule(),
		];

	public static IReadOnlyList<string> KnownIds { get; } = [.. All.Select(rule => rule.Id)];

	public static bool IsKnown(string ruleId) =>
		KnownIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase);

	public static IEligibilityRule ById(string ruleId) =>
		All.First(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
}
