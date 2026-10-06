using Purview.Build.Configuration;
using Purview.Build.Infra;

namespace Purview.Build;

/// <summary>
/// Every case in <c>src/tests/fixtures/config-resolution-scenarios.json</c>, driven from the file,
/// against a real filesystem.
/// </summary>
/// <remarks>
/// Marked <see cref="NotInParallelAttribute"/> because the scenarios set <c>PURVIEW_BUILD_CONFIG</c>
/// and the build-agent variables that decide locality, which are process-wide.
/// </remarks>
[NotInParallel]
public class ConfigResolutionScenarioTests
{
	public static IEnumerable<Func<ConfigScenario>> AllScenarios() =>
		Scenarios.ConfigResolution.Scenarios.Select<ConfigScenario, Func<ConfigScenario>>(scenario => () => scenario);

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Resolve_GivenScenario_ResolvesTheExpectedFile(ConfigScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		using ScenarioRepository repository = new(scenario);

		// Act
		var outcome = repository.Resolve();

		// Assert
		if (scenario.Expected.ExitCode != 0)
		{
			await Assert
				.That(outcome.Error)
				.IsNotNull()
				.Because($"{scenario.Name}: expected a failure, got {outcome.Resolution?.Path}");

			if (scenario.Expected.ErrorContains is not null)
				await Assert.That(outcome.Error!.Message).Contains(scenario.Expected.ErrorContains);

			return;
		}

		await Assert.That(outcome.Error).IsNull().Because($"{scenario.Name}: {outcome.Error?.Message}");

		var expectedPath = scenario.Expected.ResolvedPath is null
			? null
			: repository.FullPath(scenario.Expected.ResolvedPath);

		await Assert.That(outcome.Resolution!.Path).IsEqualTo(expectedPath).Because($"{scenario.Name}: {scenario.Why}");
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Resolve_GivenScenario_ReportsTheExpectedShadowedFiles(ConfigScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		using ScenarioRepository repository = new(scenario);

		// Act
		var outcome = repository.Resolve();

		// Assert
		if (scenario.Expected.ExitCode != 0)
			return;

		var expected = scenario
			.Expected.ShadowWarnings.Select(repository.FullPath)
			.Order(StringComparer.Ordinal)
			.ToList();
		var actual = outcome.Resolution!.ShadowedPaths.Order(StringComparer.Ordinal).ToList();

		await Assert
			.That(actual)
			.IsEquivalentTo(expected)
			.Because($"{scenario.Name}: every shadowed file must be named, and nothing else.");
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Resolve_GivenScenario_ReportsUserConfigActivationCorrectly(ConfigScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		if (scenario.Expected.UserConfigActive is null)
			return;

		using ScenarioRepository repository = new(scenario);

		// Act
		var outcome = repository.Resolve();

		// Assert
		await Assert
			.That(outcome.Resolution!.UserConfigActive)
			.IsEqualTo(scenario.Expected.UserConfigActive!.Value)
			.Because($"{scenario.Name}: {scenario.Why}");
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Resolve_GivenScenario_WarnsAboutANearMissFilename(ConfigScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		using ScenarioRepository repository = new(scenario);

		// Act
		var outcome = repository.Resolve();

		// Assert
		if (scenario.Expected.NearMissContains is null)
		{
			if (outcome.Resolution is not null)
				await Assert.That(outcome.Resolution.NearMissPath).IsNull();

			return;
		}

		await Assert
			.That(outcome.Resolution!.NearMissPath)
			.IsNotNull()
			.Because($"{scenario.Name}: a near-miss filename must be reported, not silently ignored.");
		await Assert.That(outcome.Resolution!.NearMissPath!).Contains(scenario.Expected.NearMissContains);
	}

	[Test]
	public async Task Resolve_GivenNoConfig_ProducesNoWarnings()
	{
		// Arrange
		// Absence of configuration is entirely valid and must not warn.
		ConfigScenario scenario = new() { Name = "no-config", Files = [] };
		using ScenarioRepository repository = new(scenario);

		// Act
		var outcome = repository.Resolve();

		// Assert
		await Assert.That(outcome.Resolution!.Found).IsFalse();
		await Assert.That(ConfigurationChain.DescribeWarnings(outcome.Resolution!)).IsEmpty();
	}

	[Test]
	[MethodDataSource(nameof(AllScenarios))]
	public async Task Resolve_GivenScenario_AnchorsRelativePathsToTheRepositoryRoot(ConfigScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		// Arrange
		// Wherever the configuration file is found, a relative Build:Solution must resolve to the
		// same absolute file. Anchoring to the config file's own directory would silently change
		// the meaning of every existing path the moment a repository moved its config.
		if (scenario.Expected.ExitCode != 0 || scenario.Expected.ResolvedPath is null)
			return;

		using ScenarioRepository repository = new(scenario);

		// Act
		var outcome = repository.Resolve();
		var solution = Path.GetFullPath(
			Path.Combine(repository.Root, repository.ReadSolution(outcome.Resolution!.Path!))
		);

		// Assert
		await Assert
			.That(solution)
			.IsEqualTo(repository.FullPath("src/Product.slnx"))
			.Because($"{scenario.Name}: relative paths anchor to the repository root.");
	}
}
