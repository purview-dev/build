namespace Purview.Build.Release;

/// <summary>
/// Evaluates a release unit against a resolved policy, short-circuiting in rule-ID order.
/// </summary>
/// <remarks>
/// This is evaluation only. The decision of what to do with the verdict belongs to the workflow,
/// which reads it and sets <c>Release__Mode</c>; the tool never silently declines to release when it
/// has been asked to.
/// </remarks>
static class EligibilityEvaluator
{
	public static EligibilityDecision Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(policy);

		List<RuleResult> results = [];

		foreach (var ruleId in policy.RuleIds)
		{
			var rule = EligibilityRules.ById(ruleId);

			// REL001 is what establishes that a version exists at all. Any later rule would have to
			// re-check, so a parse failure stops evaluation regardless of rule order.
			if (input.Unit is null && !string.Equals(rule.Id, "REL001", StringComparison.Ordinal))
			{
				var blocked = RuleResult.Fail(
					rule.Id,
					$"Not evaluated: the version '{input.RawVersion}' could not be parsed."
				);
				results.Add(blocked);

				return Decide(EligibilityVerdict.Fail, blocked, results);
			}

			var result = rule.Evaluate(input, policy);
			results.Add(result);

			if (result.Outcome == RuleOutcome.Skip)
				return Decide(EligibilityVerdict.Skip, result, results);

			if (result.Outcome == RuleOutcome.Fail)
				return Decide(EligibilityVerdict.Fail, result, results);
		}

		return new EligibilityDecision(
			Verdict: EligibilityVerdict.Release,
			RuleId: null,
			Message: policy.RuleIds.Count == 0
				? $"Policy '{policy.Name}' enables no rules; the release is eligible by default."
				: $"Every rule passed ({string.Join(", ", policy.RuleIds)}). The release is eligible.",
			Results: results,
			ShortCircuitRuleId: null
		);
	}

	static EligibilityDecision Decide(
		EligibilityVerdict verdict,
		RuleResult deciding,
		List<RuleResult> results
	) =>
		new(
			Verdict: verdict,
			RuleId: deciding.RuleId,
			Message: deciding.Message,
			Results: results,
			ShortCircuitRuleId: deciding.RuleId
		);
}
