namespace Purview.Build.Release.Rules;

/// <summary>
/// REL004 — a non-zero PATCH component originates only from a ref matching <c>ServicingRefs</c>.
/// </summary>
/// <remarks>
/// This is the minor-reservation rule: trunk cuts <c>x.y.0</c>, and <c>x.y.1</c> onwards is serviced
/// from the line's own release branch. Default-disabled, so Model A repositories keep shipping
/// patches from <c>main</c>.
/// </remarks>
sealed class Rel004ServicingPatchRule : IEligibilityRule
{
	public string Id => "REL004";

	public string Description =>
		"A non-zero PATCH component originates only from a ref matching ServicingRefs";

	public RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(policy);

		var unit = input.Unit!;

		if (unit.Version.Patch == 0)
			return RuleResult.Pass(
				Id,
				$"Version {unit.Version} has a zero PATCH component, which this rule does not constrain."
			);

		if (RefMatcher.Matches(input.Context.Ref, policy.ServicingRefs))
			return RuleResult.Pass(
				Id,
				$"Servicing version {unit.Version} is released from '{input.Context.Ref}'."
			);

		return RuleResult.Fail(
			Id,
			$"Version {unit.Version} has a non-zero PATCH component and cannot be released from "
				+ $"'{input.Context.Ref}'. Policy '{policy.Name}' allows servicing releases only from: "
				+ $"{RefMatcher.Describe(policy.ServicingRefs)}. Cut the patch from the "
				+ $"release/{unit.Line} branch instead."
		);
	}
}
