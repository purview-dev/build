namespace Purview.Build.Release.Rules;

/// <summary>
/// REL003 — a stable (non-prerelease) version originates only from a ref in <c>StableRefs</c>.
/// </summary>
/// <remarks>
/// Prerelease versions are unconstrained by this rule, which is what keeps repositories that ship
/// prereleases from <c>main</c> (sourcegenerator-framework, value-objects, zodsharp) unaffected.
/// </remarks>
sealed class Rel003StableFromStableRefRule : IEligibilityRule
{
	public string Id => "REL003";

	public string Description =>
		"A stable (non-prerelease) version originates only from a ref in StableRefs";

	public RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(policy);

		var unit = input.Unit!;

		if (unit.IsPrerelease)
			return RuleResult.Pass(
				Id,
				$"Version {unit.Version} is a prerelease, which this rule does not constrain."
			);

		if (RefMatcher.Matches(input.Context.Ref, policy.StableRefs))
			return RuleResult.Pass(
				Id,
				$"Stable version {unit.Version} is released from '{input.Context.Ref}'."
			);

		return RuleResult.Fail(
			Id,
			$"Stable version {unit.Version} cannot be released from '{input.Context.Ref}'. "
				+ $"Policy '{policy.Name}' allows stable releases only from: "
				+ $"{RefMatcher.Describe(policy.StableRefs)}. Release a prerelease version from this ref, "
				+ "or merge into an allowed ref."
		);
	}
}
