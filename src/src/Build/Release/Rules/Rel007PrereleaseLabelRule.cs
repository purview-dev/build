namespace Purview.Build.Release.Rules;

/// <summary>
/// REL007 — the prerelease label matches the channel's allowed pattern.
/// </summary>
/// <remarks>
/// Keeps a channel's labels consistent, so a <c>preview</c> channel cannot accidentally ship
/// <c>-alpha.1</c>. Default-disabled, and a no-op when the channel declares no
/// <c>LabelPattern</c>.
/// </remarks>
sealed class Rel007PrereleaseLabelRule : IEligibilityRule
{
	public string Id => "REL007";

	public string Description => "The prerelease label matches the channel's allowed pattern";

	public RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);

		var unit = input.Unit!;
		var pattern = input.Channel.LabelPattern;

		if (!unit.IsPrerelease)
			return RuleResult.Pass(
				Id,
				$"Version {unit.Version} is stable, so it carries no prerelease label."
			);

		if (string.IsNullOrWhiteSpace(pattern))
			return RuleResult.Pass(
				Id,
				$"Channel '{unit.Channel}' declares no LabelPattern, so any label is allowed."
			);

		var label = unit.PrereleaseLabel ?? string.Empty;
		var matches = pattern.EndsWith('*')
			? label.StartsWith(pattern[..^1], StringComparison.Ordinal)
			: string.Equals(label, pattern, StringComparison.Ordinal);

		return matches
			? RuleResult.Pass(
				Id,
				$"Prerelease label '{label}' matches channel '{unit.Channel}' pattern '{pattern}'."
			)
			: RuleResult.Fail(
				Id,
				$"Prerelease label '{label}' does not match the '{unit.Channel}' channel's LabelPattern "
					+ $"'{pattern}'. Rename the prerelease label in package.json, or release on a channel "
					+ "that allows it."
			);
	}
}
