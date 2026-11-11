namespace Purview.Build.PackageJson;

/// <summary>
/// Evaluates every applicable rule against one <c>package.json</c> and composes the report.
/// </summary>
/// <remarks>
/// Unlike the release-eligibility evaluator this is a gate: an error-level finding fails whatever
/// asked for the check, because "is this file good enough to publish" is the question being
/// answered. Warning-level findings are reported and do not fail unless
/// <see cref="PackageJsonValidationSettings.TreatWarningsAsErrors"/> says otherwise.
/// </remarks>
static class PackageJsonValidator
{
	public static PackageJsonValidationReport Validate(
		PackageJsonValidationSettings settings,
		PackageJsonValidationContext context
	)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(context);

		HashSet<string> disabled = [with(StringComparer.OrdinalIgnoreCase), .. settings.DisabledRules];

		List<PackageJsonRuleResult> results = [with(PackageJsonRules.All.Count)];
		foreach (var rule in PackageJsonRules.All)
		{
			results.Add(
				disabled.Contains(rule.Id)
					? PackageJsonRuleResult.Skipped(rule.Id, "Disabled by PackageJson:DisabledRules.")
					: rule.Evaluate(context)
			);
		}

		return PackageJsonValidationReport.From(context, results, settings.TreatWarningsAsErrors);
	}
}
