namespace Purview.Build.Release;

/// <summary>
/// One release-eligibility rule. Rule IDs are permanent identifiers: never renumbered, never
/// repurposed.
/// </summary>
interface IEligibilityRule
{
	/// <summary>The rule's permanent <c>REL0nn</c> identifier.</summary>
	string Id { get; }

	/// <summary>One-line description, used by <c>release-explain</c> and the documentation.</summary>
	string Description { get; }

	RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy);
}
