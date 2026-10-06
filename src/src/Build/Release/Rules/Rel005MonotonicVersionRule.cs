namespace Purview.Build.Release.Rules;

/// <summary>
/// REL005 — the version is strictly greater than the highest existing version on the same line.
/// </summary>
/// <remarks>
/// Scoped to the line (MAJOR.MINOR) so a serviced stable line and an in-flight prerelease line can
/// advance independently without either blocking the other. Default-disabled, because the pipeline
/// has never enforced it.
/// </remarks>
sealed class Rel005MonotonicVersionRule : IEligibilityRule
{
	public string Id => "REL005";

	public string Description =>
		"Version is strictly greater than the highest existing version on the same line";

	public RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);

		var unit = input.Unit!;

		var highest = input
			.Context.KnownVersions()
			.Where(version => string.Equals(ReleaseUnit.LineOf(version), unit.Line, StringComparison.Ordinal))
			.Order()
			.LastOrDefault();

		if (highest is null)
			return RuleResult.Pass(
				Id,
				$"No existing version on line {unit.Line}; {unit.Version} is the first."
			);

		if (unit.Version > highest)
			return RuleResult.Pass(
				Id,
				$"Version {unit.Version} is greater than {highest} on line {unit.Line}."
			);

		// The version is not greater than the highest existing version on the same line.
		return RuleResult.Fail(
			Id,
			$"Version {unit.Version} is not greater than the highest existing version {highest} on line "
				+ $"{unit.Line}. Bump the version in package.json past {highest}."
		);
	}
}
