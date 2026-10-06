namespace Purview.Build.Release.Rules;

/// <summary>
/// REL006 — a four-part version is permitted only when <c>AllowFourPart</c> is set and the ref
/// matches <c>FourPartRefs</c>.
/// </summary>
/// <remarks>
/// An escape hatch, not a routine flow: MinVer, release-please and changesets cannot produce a
/// four-part version, so this path is only ever reached by a hand-edited <c>package.json</c>.
/// Default-disabled, so the two repositories that ship four-part versions today are unaffected.
/// </remarks>
sealed class Rel006FourPartVersionRule : IEligibilityRule
{
	public string Id => "REL006";

	public string Description =>
		"A four-part version is permitted only when AllowFourPart and the ref matches FourPartRefs";

	public RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(policy);

		var unit = input.Unit!;

		if (!input.IsFourPart)
			return RuleResult.Pass(
				Id,
				$"Version {unit.Version} is not a four-part version, which this rule does not constrain."
			);

		if (!policy.AllowFourPart)
			return RuleResult.Fail(
				Id,
				$"Version {input.RawVersion} is a four-part version, which policy '{policy.Name}' does not "
					+ "permit. Set AllowFourPart on the policy, or use a three-part version."
			);

		if (RefMatcher.Matches(input.Context.Ref, policy.FourPartRefs))
			return RuleResult.Pass(
				Id,
				$"Four-part version {input.RawVersion} is released from '{input.Context.Ref}'."
			);

		// The ref does not match the policy's allowed refs for four-part releases.
		return RuleResult.Fail(
			Id,
			$"Four-part version {input.RawVersion} cannot be released from '{input.Context.Ref}'. "
				+ $"Policy '{policy.Name}' allows four-part releases only from: "
				+ $"{RefMatcher.Describe(policy.FourPartRefs)}."
		);
	}
}
