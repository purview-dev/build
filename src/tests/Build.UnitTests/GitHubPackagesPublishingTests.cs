using Microsoft.Extensions.Configuration;
using Purview.Build.Configuration;
using Purview.Build.Helpers;
using Purview.Build.Modules;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// GitHub Packages as an optional second publication destination: it is off unless a repository opts
/// in, it never changes where NuGet.org publication goes, and it is gated by the same release decision.
/// </summary>
public class GitHubPackagesPublishingTests
{
	static readonly Func<string, string?> NoEnvironment = _ => null;

	static Func<string, string?> EnvironmentWith(params (string Name, string Value)[] values)
	{
		var map = values.ToDictionary(value => value.Name, value => (string?)value.Value, StringComparer.Ordinal);

		return name => map.GetValueOrDefault(name);
	}

	static NuGetSettings NuGet(bool enabled) => new() { GitHubPackages = new() { Enabled = enabled } };

	[Test]
	public async Task Defaults_GivenNoConfiguration_DisableGitHubPackages()
	{
		// Arrange
		NuGetSettings settings = new();

		// Act & Assert
		await Assert.That(settings.GitHubPackages.Enabled).IsFalse();
		await Assert
			.That(settings.GitHubPackages.ResolveFeedUrl(NoEnvironment))
			.IsEqualTo("https://nuget.pkg.github.com/purview-dev/index.json");
	}

	[Test]
	public async Task Binding_GivenTheReleaseWorkflowEnvironment_EnablesTheDestination()
	{
		// Arrange
		// This is exactly what purview-release.yml sets from the repository variable.
		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(
				new Dictionary<string, string?>
				{
					["NuGet:GitHubPackages:Enabled"] = "true",
					["NuGet:GitHubPackages:Owner"] = "purview-dev",
				}
			)
			.Build();

		// Act
		var settings = configuration.GetSection(NuGetSettings.SectionName).Get<NuGetSettings>()!;

		// Assert
		await Assert.That(settings.GitHubPackages.Enabled).IsTrue();
		await Assert
			.That(settings.GitHubPackages.ResolveFeedUrl(NoEnvironment))
			.IsEqualTo("https://nuget.pkg.github.com/purview-dev/index.json");
	}

	[Test]
	public async Task FeedUrl_GivenNoOwner_UsesTheRepositoryOwnerFromTheEnvironment()
	{
		// Arrange
		// The feed follows the repository rather than a hardcoded organisation; purview-dev is only the
		// local-run fallback.
		GitHubPackagesSettings settings = new();

		// Act
		var feed = settings.ResolveFeedUrl(EnvironmentWith(("GITHUB_REPOSITORY_OWNER", "contoso")));

		// Assert
		await Assert.That(feed).IsEqualTo("https://nuget.pkg.github.com/contoso/index.json");
	}

	[Test]
	public async Task Username_GivenNoConfiguration_PrefersTheWorkflowActor()
	{
		// Arrange
		GitHubPackagesSettings settings = new();

		// Act
		var username = settings.ResolveUsername(
			EnvironmentWith(("GITHUB_ACTOR", "octocat"), ("GITHUB_REPOSITORY_OWNER", "purview-dev"))
		);

		// Assert
		await Assert.That(username).IsEqualTo("octocat");
	}

	[Test]
	public async Task IsSameRegistry_GivenTheDerivedFeed_MatchesRegardlessOfCaseAndTrailingSlash()
	{
		// Arrange
		GitHubPackagesSettings settings = new();

		// Act
		var matches = settings.IsSameRegistry("https://NUGET.PKG.GITHUB.COM/purview-dev/index.json/", NoEnvironment);

		// Assert
		await Assert.That(matches).IsTrue();
	}

	[Test]
	public async Task DisabledReason_GivenOptedInAndPublishing_AllowsThePublish()
	{
		// Arrange
		ReleaseSettings release = new() { Mode = ReleaseMode.NuGet };

		// Act
		var reason = GitHubPackagesPublishing.DisabledReason(ProjectType.DotNet, release, NuGet(enabled: true));

		// Assert
		await Assert.That(reason).IsNull();
	}

	[Test]
	public async Task DisabledReason_GivenNoOptIn_NamesTheOptIn()
	{
		// Arrange
		ReleaseSettings release = new() { Mode = ReleaseMode.NuGet };

		// Act
		var reason = GitHubPackagesPublishing.DisabledReason(ProjectType.DotNet, release, NuGet(enabled: false));

		// Assert
		await Assert.That(reason).Contains("NuGet:GitHubPackages:Enabled");
		await Assert.That(reason).Contains("PURVIEW_PUBLISH_GITHUB_PACKAGES");
	}

	[Test]
	public async Task DisabledReason_GivenNoReleasePublication_DoesNotPublish()
	{
		// Arrange
		ReleaseSettings release = new() { Mode = ReleaseMode.None };

		// Act
		var reason = GitHubPackagesPublishing.DisabledReason(ProjectType.DotNet, release, NuGet(enabled: true));

		// Assert
		await Assert.That(reason).Contains("Release:Mode=NuGet");
	}

	[Test]
	public async Task DisabledReason_GivenAWebProject_Skips()
	{
		// Arrange
		ReleaseSettings release = new() { Mode = ReleaseMode.NuGet };

		// Act
		var reason = GitHubPackagesPublishing.DisabledReason(ProjectType.Web, release, NuGet(enabled: true));

		// Assert
		await Assert.That(reason).Contains("Web project");
	}

	[Test]
	public async Task DisabledReason_GivenAChannelAlreadyOnGitHubPackages_AvoidsADoublePush()
	{
		// Arrange
		ReleaseSettings release = new()
		{
			Mode = ReleaseMode.NuGet,
			Channel = "preview",
			Channels = new()
			{
				["preview"] = new()
				{
					FeedUrl = "https://nuget.pkg.github.com/purview-dev/index.json",
					GitHubRelease = false,
				},
			},
		};

		// Act
		var reason = GitHubPackagesPublishing.DisabledReason(ProjectType.DotNet, release, NuGet(enabled: true));

		// Assert
		await Assert.That(reason).Contains("already publishes to GitHub Packages");
	}

	[Test]
	public async Task PushConfig_GivenTheEnvironmentToken_StoresOnlyTheMacro()
	{
		// Arrange
		var environment = EnvironmentWith(("GITHUB_TOKEN", "secret-token"));

		// Act
		var path = GitHubPackagesPublishing.WritePushConfig(
			"https://nuget.pkg.github.com/purview-dev/index.json",
			"github-actions",
			"secret-token",
			environment
		);

		try
		{
			var content = await File.ReadAllTextAsync(path);

			// Assert
			await Assert.That(content).Contains("purview-github-packages");
			await Assert.That(content).Contains("%GITHUB_TOKEN%");
			await Assert.That(content).DoesNotContain("secret-token");
			await Assert.That(content).Contains("<clear />");
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task PushConfig_GivenATokenFromConfiguration_WritesItToASingleUseFile()
	{
		// Arrange
		// A local run may supply GitHub:AccessToken instead of the environment, and has no macro to
		// reference. The caller deletes the file.
		var path = GitHubPackagesPublishing.WritePushConfig(
			"https://nuget.pkg.github.com/purview-dev/index.json",
			"github-actions",
			"configured-token",
			NoEnvironment
		);

		try
		{
			var content = await File.ReadAllTextAsync(path);

			// Assert
			await Assert.That(content).Contains("configured-token");
			await Assert.That(Path.GetFileName(path)).StartsWith("purview-github-packages-");
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task PushOptions_ClearTheNuGetOrgKey()
	{
		// Arrange
		// Consumer release workflows export the NuGet.org key as NUGET_API_KEY. NuGet 7.6+ sends that
		// as the push API key as well as the source credentials, and GitHub Packages rejects the
		// combination. The child must not inherit it.
		var options = GitHubPackagesPublishing.CreatePushExecutionOptions();

		// Act
		var key = options.EnvironmentVariables?["NUGET_API_KEY"];

		// Assert
		await Assert.That(options.ThrowOnNonZeroExitCode).IsFalse();
		await Assert.That(key).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Discover_GivenAMissingArtifactsFolder_FailsWithThePackRemediation()
	{
		// Arrange
		var missing = Path.Combine(Path.GetTempPath(), "purview-missing-" + Guid.NewGuid().ToString("N"));

		// Act
		void Act() => NuGetPackages.Discover(missing);

		// Assert
		var exception = await Assert.That(Act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("artifacts folder");
		await Assert.That(exception.Message).Contains("pack step");
	}

	[Test]
	public async Task Discover_GivenAnEmptyArtifactsFolder_Fails()
	{
		// Arrange
		var folder = Path.Combine(Path.GetTempPath(), "purview-empty-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);

		try
		{
			// Act
			void Act() => NuGetPackages.Discover(folder);

			// Assert
			var exception = await Assert.That(Act).Throws<InvalidOperationException>();
			await Assert.That(exception!.Message).Contains("No NuGet packages found");
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Test]
	public async Task Preflight_GivenOptInAndNoToken_ReportsTheMissingGitHubToken()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[
				("Release:Mode", "NuGet"),
				("Release:GitHubRelease", "false"),
				("NuGet:APIKey", "nuget-key"),
				("NuGet:GitHubPackages:Enabled", "true"),
			]
		);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("GITHUB_TOKEN");
		await Assert.That(problems[0]).Contains("GitHubPackages");
	}

	[Test]
	public async Task Preflight_GivenOptInAndToken_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[
				("Release:Mode", "NuGet"),
				("Release:GitHubRelease", "false"),
				("NuGet:APIKey", "nuget-key"),
				("NuGet:GitHubPackages:Enabled", "true"),
			],
			environment: EnvironmentWith(("GITHUB_TOKEN", "token"))
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task Preflight_GivenOptInButNoReleasePublication_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(repository, [("Release:Mode", "None"), ("NuGet:GitHubPackages:Enabled", "true")]);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task Preflight_GivenOptInAndDryRun_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[
				("Release:Mode", "NuGet"),
				("Release:GitHubRelease", "false"),
				("NuGet:APIKey", "nuget-key"),
				("NuGet:GitHubPackages:Enabled", "true"),
				("Release:DryRun", "true"),
			]
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task Preflight_GivenAnInsecureFeedUrl_ReportsIt()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[
				("Release:Mode", "NuGet"),
				("Release:GitHubRelease", "false"),
				("NuGet:APIKey", "nuget-key"),
				("NuGet:GitHubPackages:Enabled", "true"),
				("NuGet:GitHubPackages:FeedUrl", "http://insecure.example/index.json"),
			],
			environment: EnvironmentWith(("GITHUB_TOKEN", "token"))
		);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("not an absolute https URL");
	}

	[Test]
	public async Task Release_GivenAnEligibleRelease_RequiresBothRegistriesBeforeTagging()
	{
		// Arrange
		// The tag and GitHub release must not appear when either registry rejected the artifacts.
		var dependencies = Dependencies(typeof(CreateGitHubReleaseModule));

		// Assert
		await Assert.That(dependencies).Contains(typeof(PublishNuGetModule));
		await Assert.That(dependencies).Contains(typeof(PublishGitHubPackagesModule));
	}

	static IReadOnlyList<Type> Dependencies(Type module) =>
		[
			.. module
				.GetCustomAttributes(inherit: false)
				.Select(attribute => attribute.GetType())
				.Where(type =>
					type.IsGenericType && type.Name.StartsWith("DependsOnAttribute", StringComparison.Ordinal)
				)
				.Select(type => type.GetGenericArguments()[0]),
		];

	static IReadOnlyList<string> FindProblems(
		PreflightRepository repository,
		(string Key, string? Value)[] values,
		Func<string, string?>? environment = null
	) =>
		ConfigurationPreflight.FindProblems(
			Configuration(values),
			repository.Root,
			isRunningLocally: true,
			environment ?? NoEnvironment
		);

	static IConfiguration Configuration(IEnumerable<(string Key, string? Value)> values) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
			.Build();
}
