using Microsoft.Extensions.Configuration;
using Purview.Build.Configuration;
using Purview.Build.Infra;
using Purview.Build.Release;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// A fixture per consuming-repository shape, each asserting the pre-change release decision is
/// reproduced exactly, with the configuration file at the repository root as it is today.
/// </summary>
/// <remarks>
/// The shapes are taken from the real repositories surveyed before this change: Model A on
/// <c>main</c> with a stable version (aspire-resourcekit, telemetry-sourcegenerator), Model A with a
/// prerelease version (sourcegenerator-framework, value-objects, zodsharp), Model A with a four-part
/// version (aspirec4, build-sdk), the documented Model B release-branch shape, and a
/// <c>ProjectType=Web</c> repository.
///
/// The Web shape is reconstructed from the documented configuration rather than from the real
/// repository, which was not available locally when this was written.
/// </remarks>
[NotInParallel]
public class ConsumerCompatibilityTests
{
	/// <summary>
	/// Every consuming repository's configuration sets Release:Mode to None and relies on the
	/// workflow's Release__Mode environment variable to outrank it.
	/// </summary>
	const string ModelAConfig = """
		{
			"Build": {
				"Solution": "src/Product.slnx",
				"TestRoot": "src/tests",
				"TestPatterns": "*Tests.csproj",
				"TestFilter": "/*/*/*/*[Category=Unit]"
			},
			"Release": { "Mode": "None" }
		}
		""";

	const string WebConfig = """
		{
			"Build": {
				"ProjectType": "Web",
				"WebBuildOutput": "src/dist"
			},
			"Release": { "Mode": "None" }
		}
		""";

	public static IEnumerable<Func<ConsumerShape>> Shapes() =>
		new ConsumerShape[]
		{
			new("aspire-resourcekit (Model A, stable)", "1.0.0", "main", ModelAConfig),
			new("telemetry-sourcegenerator (Model A, stable)", "5.0.0", "main", ModelAConfig),
			new("sourcegenerator-framework (Model A, prerelease)", "1.0.0-prerelease.54", "main", ModelAConfig),
			new("value-objects (Model A, prerelease)", "1.0.0-prerelease.12", "main", ModelAConfig),
			new("zodsharp (Model A, prerelease)", "2.0.1-prerelease.1", "main", ModelAConfig),
			new("aspirec4 (Model A, four-part)", "13.5.3.10", "main", ModelAConfig),
			new("build-sdk (Model A, four-part)", "1.0.2.2", "main", ModelAConfig),
			new("documented Model B (release branch)", "2.0.0", "release", ModelAConfig),
			new("purview.dev portal shape (Web)", "1.4.0", "main", WebConfig),
		}.Select<ConsumerShape, Func<ConsumerShape>>(shape => () => shape);

	[Test]
	[MethodDataSource(nameof(Shapes))]
	public async Task Evaluate_GivenConsumerShapeWithUntaggedVersion_Releases(ConsumerShape shape)
	{
		ArgumentNullException.ThrowIfNull(shape);

		// Arrange
		using var fixture = GitFixture.Create(shape.Version, shape.Branch, tags: ["v0.0.1"], configJson: shape.Config);

		// Act
		var decision = Evaluate(fixture);

		// Assert
		// Before this change the workflow released whenever v{version} did not exist. Every shape
		// must still reach that outcome with no configuration added.
		await Assert
			.That(decision.Verdict)
			.IsEqualTo(EligibilityVerdict.Release)
			.Because($"{shape.Name}: {Describe(decision)}");
	}

	[Test]
	[MethodDataSource(nameof(Shapes))]
	public async Task Evaluate_GivenConsumerShapeWithTaggedVersion_SkipsWithExitCodeZero(ConsumerShape shape)
	{
		ArgumentNullException.ThrowIfNull(shape);

		// Arrange
		using var fixture = GitFixture.Create(
			shape.Version,
			shape.Branch,
			tags: [$"v{shape.Version}"],
			configJson: shape.Config
		);

		// Act
		var decision = Evaluate(fixture);

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Skip);
		await Assert.That(decision.ExitCode).IsEqualTo(0);
		await Assert.That(decision.RuleId).IsEqualTo("REL002");
	}

	[Test]
	[MethodDataSource(nameof(Shapes))]
	public async Task Resolve_GivenConsumerShape_FindsTheRootConfigAtProbePositionOne(ConsumerShape shape)
	{
		ArgumentNullException.ThrowIfNull(shape);

		// Arrange
		// A repository with purview-build.json at its root must resolve exactly as it does today:
		// probe position 1, no shadow warnings, no user config.
		using var fixture = GitFixture.Create(shape.Version, shape.Branch, configJson: shape.Config);

		// Act
		var resolution = ConfigFileLocator.Resolve(fixture.Root);

		// Assert
		await Assert
			.That(resolution.Path)
			.IsEqualTo(Path.GetFullPath(Path.Combine(fixture.Root, "purview-build.json")));
		await Assert.That(resolution.Source).IsEqualTo(ConfigSource.Probe);
		await Assert.That(resolution.ShadowedPaths).IsEmpty();
		await Assert.That(resolution.UserConfigActive).IsFalse();
		await Assert.That(ConfigurationChain.DescribeWarnings(resolution)).IsEmpty();
	}

	[Test]
	[MethodDataSource(nameof(Shapes))]
	public async Task Publication_GivenConsumerShapeAndWorkflowReleaseMode_PublishesAndReleases(ConsumerShape shape)
	{
		ArgumentNullException.ThrowIfNull(shape);

		// Arrange
		// The caller workflow sets Release__Mode=NuGet, which outranks the repository's
		// "Mode": "None". The derived switches must match the pre-change skip conditions.
		using var fixture = GitFixture.Create(shape.Version, shape.Branch, configJson: shape.Config);

		var configuration = BuildConfiguration(fixture, ("Release__Mode", "NuGet"));
		var release = configuration.GetSection(ReleaseSettings.SectionName).Get<ReleaseSettings>()!;

		// Act
		var publish = release.ShouldPublish();
		var githubRelease = release.ShouldCreateGitHubRelease();

		// Assert
		await Assert.That(publish).IsTrue().Because($"{shape.Name} publishes to NuGet today.");
		await Assert.That(githubRelease).IsTrue().Because($"{shape.Name} cuts a GitHub release today.");
		await Assert.That(release.DryRun).IsFalse();
	}

	[Test]
	[MethodDataSource(nameof(Shapes))]
	public async Task Configuration_GivenConsumerShape_PreservesEveryConfiguredValue(ConsumerShape shape)
	{
		ArgumentNullException.ThrowIfNull(shape);

		// Arrange
		using var fixture = GitFixture.Create(shape.Version, shape.Branch, configJson: shape.Config);

		var configuration = BuildConfiguration(fixture);

		// Act
		var build = configuration.GetSection(BuildSettings.SectionName).Get<BuildSettings>()!;
		var version = configuration.GetSection(VersionSettings.SectionName).Get<VersionSettings>()!;

		// Assert
		// The new Version section must default to today's behaviour, and nothing in the new
		// configuration surface may disturb what the repository already declares.
		await Assert.That(version.Source).IsEqualTo(VersionSource.PackageJson);
		await Assert
			.That(version.Strictness)
			.IsEqualTo(VersionStrictness.NuGet)
			.Because("four-part versions must keep parsing, as aspirec4 and build-sdk require.");

		if (shape.Config == WebConfig)
		{
			await Assert.That(build.ProjectType).IsEqualTo(ProjectType.Web);
			await Assert.That(build.WebBuildOutput).IsEqualTo("src/dist");
		}
		else
		{
			await Assert.That(build.Solution).IsEqualTo("src/Product.slnx");
			await Assert.That(build.TestFilter).IsEqualTo("/*/*/*/*[Category=Unit]");
		}
	}

	[Test]
	public async Task Resolve_GivenConfigMovedToDotConfig_AnchorsPathsIdentically()
	{
		// Arrange
		// Moving the configuration file must require no other edit: every relative path in it still
		// resolves against the repository root.
		using var atRoot = GitFixture.Create("1.0.0", configJson: ModelAConfig);
		using var inDotConfig = GitFixture.Create(
			"1.0.0",
			configJson: ModelAConfig,
			configRelativePath: ".config/purview-build.json"
		);

		// Act
		var rootSolution = ResolveSolutionPath(atRoot);
		var movedSolution = ResolveSolutionPath(inDotConfig);

		// Assert
		await Assert
			.That(Path.GetRelativePath(atRoot.Root, rootSolution))
			.IsEqualTo(Path.GetRelativePath(inDotConfig.Root, movedSolution))
			.Because("Build:Solution anchors to the repository root, not the config file's directory.");
	}

	static string ResolveSolutionPath(GitFixture fixture)
	{
		var configuration = BuildConfiguration(fixture);
		var build = configuration.GetSection(BuildSettings.SectionName).Get<BuildSettings>()!;

		return Path.GetFullPath(Path.Combine(fixture.Root, build.Solution));
	}

	static IConfigurationRoot BuildConfiguration(GitFixture fixture, params (string Key, string Value)[] environment)
	{
		var resolution = ConfigFileLocator.Resolve(fixture.Root);

		PipelineStartup startup = new(AppSettingsDirectory.Path, fixture.Root, resolution);

		ConfigurationBuilder builder = new();
		builder.AddJsonFile(Path.Combine(startup.PipelineDirectory, "appsettings.json"), optional: false);

		if (resolution.Path is not null)
			builder.AddJsonFile(resolution.Path, optional: true);

		// The reusable workflow's environment layer, which outranks the repository's JSON.
		if (environment.Length > 0)
		{
			builder.AddInMemoryCollection(
				environment.Select(entry => new KeyValuePair<string, string?>(
					entry.Key.Replace("__", ":", StringComparison.Ordinal),
					entry.Value
				))
			);
		}

		return builder.Build();
	}

	static string Describe(EligibilityDecision decision) =>
		string.Join(Environment.NewLine, decision.Results.Select(result => result.Message));

	static EligibilityDecision Evaluate(GitFixture fixture)
	{
		var configuration = BuildConfiguration(fixture);
		var release = configuration.GetSection(ReleaseSettings.SectionName).Get<ReleaseSettings>() ?? new();
		var version = configuration.GetSection(VersionSettings.SectionName).Get<VersionSettings>() ?? new();

		var context = fixture.InWorkingDirectory(() => ReleaseContextProvider.Resolve(release.Context, fixture.Root));

		var policy = EligibilityPolicies.Resolve(release.Eligibility, release.Eligibility.Policy);

		var rawVersion = fixture.InWorkingDirectory(() =>
			System
				.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Root, "package.json")))
				.RootElement.GetProperty("version")
				.GetString()!
		);

		var input = EligibilityInput.Create(
			rawVersion,
			version.Strictness,
			context,
			release.Channel,
			release.ResolveChannel(),
			version.Source.ToString()
		);

		return EligibilityEvaluator.Evaluate(input, policy);
	}
}

/// <summary>
/// One consuming-repository shape: the version it ships, the branch it releases from, and the
/// configuration file it has at its root.
/// </summary>
public sealed record ConsumerShape(string Name, string Version, string Branch, string Config)
{
	public override string ToString() => Name;
}

/// <summary>
/// Locates the shipped <c>appsettings.json</c> directory.
/// </summary>
static class AppSettingsDirectory
{
	public static string Path { get; } = Find();

	static string Find()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			var candidate = System.IO.Path.Combine(directory.FullName, "src", "src", "Build");
			if (File.Exists(System.IO.Path.Combine(candidate, "appsettings.json")))
				return candidate;
		}

		throw new InvalidOperationException(
			$"Could not locate src/src/Build/appsettings.json from '{AppContext.BaseDirectory}'."
		);
	}
}
