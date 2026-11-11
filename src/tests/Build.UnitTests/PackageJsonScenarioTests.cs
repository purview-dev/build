using Purview.Build.Infra;
using Purview.Build.PackageJson;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// Every case in <c>src/tests/fixtures/package-json-scenarios.json</c>, driven from the file.
/// </summary>
/// <remarks>
/// Adding a rule means appending cases to that file; no test code changes. The same file drives
/// <c>just package-matrix</c>, which runs each case through the real CLI, so the in-process runner
/// and the shipped binary are held to one specification.
/// </remarks>
public class PackageJsonScenarioTests
{
	public static IEnumerable<Func<PackageJsonScenario>> AllScenarios() =>
		Scenarios.PackageJson.Scenarios.Select<PackageJsonScenario, Func<PackageJsonScenario>>(scenario =>
			() => scenario
		);

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Validate_GivenScenario_MatchesTheExpectedProfileAndVerdict(PackageJsonScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		var report = await RunAsync(scenario);

		await Assert.That(report.Profile).IsEqualTo(scenario.Expected.Profile).Because(Describe(scenario, report));

		await Assert.That(report.Succeeded).IsEqualTo(scenario.Expected.Succeeded).Because(Describe(scenario, report));

		await Assert.That(report.ExitCode).IsEqualTo(scenario.Expected.ExitCode).Because(Describe(scenario, report));
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Validate_GivenScenario_FailsTheExpectedRules(PackageJsonScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		var report = await RunAsync(scenario);

		await Assert
			.That(RuleIds(report, "fail"))
			.IsEquivalentTo(scenario.Expected.Errors)
			.Because(Describe(scenario, report));
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Validate_GivenScenario_WarnsOnTheExpectedRules(PackageJsonScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		var report = await RunAsync(scenario);

		await Assert
			.That(RuleIds(report, "warning"))
			.IsEquivalentTo(scenario.Expected.Warnings)
			.Because(Describe(scenario, report));
	}

	[Test]
	public async Task AllScenarios_NameOnlyKnownRuleIds()
	{
		// Guards the fixture itself: a typo in an expected rule ID would otherwise silently assert
		// against a rule that does not exist.
		var referenced = Scenarios
			.PackageJson.Scenarios.SelectMany(scenario => scenario.Expected.Errors.Concat(scenario.Expected.Warnings))
			.Distinct();

		foreach (var ruleId in referenced)
		{
			await Assert
				.That(PackageJsonRules.IsKnown(ruleId))
				.IsTrue()
				.Because($"the fixture references '{ruleId}', which is not a known rule.");
		}
	}

	[Test]
	public async Task AllScenarios_ExerciseEveryRule()
	{
		var referenced = Scenarios
			.PackageJson.Scenarios.SelectMany(scenario => scenario.Expected.Errors.Concat(scenario.Expected.Warnings))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var unexercised = PackageJsonRules
			.All.Select(rule => rule.Id)
			.Where(ruleId => !referenced.Contains(ruleId))
			.ToArray();

		await Assert
			.That(unexercised)
			.IsEmpty()
			.Because("a rule with no scenario is a rule nobody has specified: " + string.Join(", ", unexercised));
	}

	static async Task<PackageJsonValidationReport> RunAsync(PackageJsonScenario scenario)
	{
		var directory = Path.Combine(Path.GetTempPath(), "purview-build-packagejson-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			await File.WriteAllTextAsync(Path.Combine(directory, "package.json"), scenario.PackageJson.GetRawText());

			BuildSettings build = new()
			{
				ProjectType = Enum.Parse<ProjectType>(scenario.ProjectType, ignoreCase: true),
				RunPack = scenario.PacksNuGetPackages,
				RunTests = scenario.RunTests,
				RunLint = scenario.RunLint,
			};

			PackageJsonValidationSettings settings = new()
			{
				Enabled = true,
				Profile = Enum.Parse<PackageJsonProfile>(scenario.Profile, ignoreCase: true),
				DisabledRules = [.. scenario.DisabledRules],
				AdditionalRequiredFields = [.. scenario.AdditionalRequiredFields],
				TreatWarningsAsErrors = scenario.TreatWarningsAsErrors,
			};

			return PackageJsonValidationRunner.Run(settings, build, new VersionSettings(), directory);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	static IReadOnlyList<string> RuleIds(PackageJsonValidationReport report, string outcome) =>
		[.. report.Results.Where(result => result.Outcome == outcome).Select(result => result.RuleId)];

	static string Describe(PackageJsonScenario scenario, PackageJsonValidationReport report) =>
		$"{scenario.Name}: {scenario.Why}{Environment.NewLine}{PackageJsonValidationReportText.Render(report)}";
}
