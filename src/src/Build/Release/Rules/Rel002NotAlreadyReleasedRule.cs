namespace Purview.Build.Release.Rules;

/// <summary>
/// REL002 — <c>v{version}</c> is not already tagged, and the version is not already on the target
/// feed.
/// </summary>
/// <remarks>
/// This is the rule that replaces the workflow's <c>git rev-parse "$TAG"</c> gate, so a hit is a
/// <see cref="RuleOutcome.Skip"/> (already released; exit 0), never a failure. The feed half is only
/// judged when the feed state is known: offline evaluation reports that it could not be checked
/// rather than passing silently.
/// </remarks>
sealed class Rel002NotAlreadyReleasedRule : IEligibilityRule
{
	public string Id => "REL002";

	public string Description =>
		"v{version} is not already tagged, and the version is not already on the target feed";

	public RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);

		var unit = input.Unit!;

		if (input.Context.HasTag(unit.Tag))
			return RuleResult.Skip(
				Id,
				$"Version {unit.Version} is already released as {unit.Tag}. Nothing to do."
			);

		if (input.Context.PublishedVersionsKnown)
		{
			var published = input.Context.PublishedVersions.Any(version =>
				version.Equals(unit.Version)
			);

			if (published)
				return RuleResult.Skip(
					Id,
					$"Version {unit.Version} is already on the target feed. Nothing to do."
				);

			return RuleResult.Pass(
				Id,
				$"{unit.Tag} does not exist and {unit.Version} is not on the target feed."
			);
		}

		return RuleResult.Pass(
			Id,
			$"{unit.Tag} does not exist. The feed was not consulted, so only the tag was checked."
		);
	}
}
