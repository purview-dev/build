using NuGet.Versioning;
using Purview.Build.Infra;
using Purview.Build.Release;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// Every case in <c>src/tests/fixtures/eligibility-scenarios.json</c>, driven from the file.
/// </summary>
/// <remarks>
/// Adding a rule or policy means appending cases to that file; no test code changes. The same file
/// drives <c>just release-matrix</c>, which runs each case through the real CLI, so the in-process
/// evaluator and the shipped binary are held to one specification.
/// </remarks>
public class EligibilityScenarioTests
{
	public static IEnumerable<Func<EligibilityScenario>> AllScenarios() =>
		Scenarios.Eligibility.Scenarios.Select<EligibilityScenario, Func<EligibilityScenario>>(scenario =>
			() => scenario
		);

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Evaluate_GivenScenario_MatchesExpectedVerdict(EligibilityScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		var decision = Evaluate(scenario);

		// Act
		var verdict = decision.Verdict.ToString();

		// Assert
		await Assert
			.That(verdict)
			.IsEqualTo(scenario.Expected.Verdict)
			.Because($"{scenario.Name}: {scenario.Why}{Environment.NewLine}{Describe(decision)}");
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Evaluate_GivenScenario_MatchesExpectedDecidingRule(EligibilityScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		var decision = Evaluate(scenario);

		// Act
		var ruleId = decision.RuleId;

		// Assert
		await Assert
			.That(ruleId)
			.IsEqualTo(scenario.Expected.RuleId)
			.Because($"{scenario.Name}: {scenario.Why}{Environment.NewLine}{Describe(decision)}");
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Evaluate_GivenScenario_MatchesExpectedExitCode(EligibilityScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		var decision = Evaluate(scenario);

		// Act
		var exitCode = decision.ExitCode;

		// Assert
		await Assert
			.That(exitCode)
			.IsEqualTo(scenario.Expected.ExitCode)
			.Because($"{scenario.Name}: a skip must stay exit 0 and a policy violation exit 1.");
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Evaluate_GivenScenario_ShortCircuitsAtTheDecidingRule(EligibilityScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		var decision = Evaluate(scenario);

		// Act
		var lastEvaluated = decision.Results.Count > 0 ? decision.Results[^1].RuleId : null;

		// Assert
		// A non-passing verdict must stop at the rule that decided it, so no rule after the
		// short-circuit point is ever evaluated.
		if (scenario.Expected.RuleId is null)
			await Assert.That(decision.ShortCircuitRuleId).IsNull();
		else
			await Assert.That(lastEvaluated).IsEqualTo(scenario.Expected.RuleId);
	}

	[Test]
	public async Task AllScenarios_NameEveryRuleIdThatCanDecide()
	{
		// Arrange
		// Guards the fixture itself: a typo in an expected rule ID would otherwise silently assert
		// against a rule that does not exist.
		var known = EligibilityRules.KnownIds;

		// Act
		var referenced = Scenarios
			.Eligibility.Scenarios.Select(scenario => scenario.Expected.RuleId)
			.Where(ruleId => ruleId is not null)
			.Distinct()
			.ToList();

		// Assert
		foreach (var ruleId in referenced)
			await Assert.That(known).Contains(ruleId!);
	}

	static EligibilityDecision Evaluate(EligibilityScenario scenario)
	{
		EligibilitySettings settings = new() { Policy = scenario.Policy, Policies = Scenarios.Eligibility.Policies };

		var policy = EligibilityPolicies.Resolve(settings, scenario.Policy);

		List<NuGetVersion> published = [];
		foreach (var version in scenario.PublishedVersions ?? [])
		{
			if (NuGetVersion.TryParse(version, out var parsed))
				published.Add(parsed);
		}

		ReleaseContext context = new(
			Ref: scenario.Ref,
			RefSource: "scenario fixture",
			ExistingTags: scenario.ExistingTags,
			PublishedVersions: published,
			PublishedVersionsKnown: scenario.PublishedVersions is not null,
			Simulated: true
		);

		var strictness = Enum.Parse<VersionStrictness>(scenario.Strictness, ignoreCase: true);

		var input = EligibilityInput.Create(
			scenario.Version,
			strictness,
			context,
			ReleaseChannelSettings.StableChannelName,
			ReleaseChannelSettings.Stable,
			nameof(VersionSource.PackageJson)
		);

		return EligibilityEvaluator.Evaluate(input, policy);
	}

	static string Describe(EligibilityDecision decision) =>
		string.Join(Environment.NewLine, decision.Results.Select(result => $"  {result.Outcome, -4} {result.Message}"));
}
