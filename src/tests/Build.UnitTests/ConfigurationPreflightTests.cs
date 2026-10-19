using Microsoft.Extensions.Configuration;
using Purview.Build.Configuration;

namespace Purview.Build;

/// <summary>
/// The startup requirements: a run must fail before any module executes when its configuration cannot
/// support what it was asked to do.
/// </summary>
public class ConfigurationPreflightTests
{
	static readonly Func<string, string?> NoEnvironment = _ => null;

	static IReadOnlyList<string> FindProblems(
		PreflightRepository repository,
		(string Key, string? Value)[] values,
		bool isRunningLocally = true,
		Func<string, string?>? environment = null
	) =>
		ConfigurationPreflight.FindProblems(
			Configuration(values),
			repository.Root,
			isRunningLocally,
			environment ?? NoEnvironment
		);

	static IConfiguration Configuration(IEnumerable<(string Key, string? Value)> values) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
			.Build();

	static Func<string, string?> EnvironmentWith(params (string Name, string Value)[] values)
	{
		var map = values.ToDictionary(value => value.Name, value => (string?)value.Value, StringComparer.Ordinal);

		return name => map.GetValueOrDefault(name);
	}

	[Test]
	public async Task LocalNuGet_GivenNoFeedPath_ReportsTheMissingPath()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(repository, [("Release:Mode", "LocalNuGet")]);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("PublishLocalNuGet");
		await Assert.That(problems[0]).Contains("LOCAL_NUGET_FEED_PATH");
	}

	[Test]
	public async Task LocalNuGet_GivenConfiguredFeedPath_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "LocalNuGet"), ("PublishLocalNuGet:LocalFeedPath", "/tmp/purview-preflight-feed")]
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task LocalNuGet_GivenFeedPathFromTheEnvironment_ReportsNothing()
	{
		// Arrange
		// The preflight and the module must resolve the plain process variable the same way.
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "LocalNuGet")],
			environment: EnvironmentWith(("LOCAL_NUGET_FEED_PATH", "/tmp/purview-preflight-feed"))
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task LocalNuGet_GivenBuildAgent_ReportsNothing()
	{
		// Arrange
		// LocalNuGet is intentionally ignored in CI, so the feed path is not a CI requirement.
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(repository, [("Release:Mode", "LocalNuGet")], isRunningLocally: false);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task LocalNuGet_GivenDriveRelativePath_ReportsTheRemediationMessage()
	{
		// Arrange
		// 'p:foo' is the signature of a Windows path whose backslashes a shell stripped.
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "LocalNuGet"), ("PublishLocalNuGet:LocalFeedPath", "p:foo")]
		);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("drive-relative");
		await Assert.That(problems[0]).Contains("forward slashes");
	}

	[Test]
	public async Task NuGetPublish_GivenNoCredential_ReportsTheMissingAPIKey()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(repository, [("Release:Mode", "NuGet"), ("Release:GitHubRelease", "false")]);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("NuGet API key");
		await Assert.That(problems[0]).Contains("NUGET_APIKEY");
	}

	[Test]
	public async Task NuGetPublish_GivenAPIKey_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "NuGet"), ("Release:GitHubRelease", "false"), ("NuGet:APIKey", "secret")]
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task NuGetPublish_GivenAPIKeyFromTheEnvironment_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "NuGet"), ("Release:GitHubRelease", "false")],
			environment: EnvironmentWith(("NUGET_APIKEY", "secret"))
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task NuGetPublish_GivenTrustedPublishing_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "NuGet"), ("Release:GitHubRelease", "false"), ("NuGet:TrustedPublishing", "true")]
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task NuGetPublish_GivenDryRun_ReportsNothing()
	{
		// Arrange
		// A dry run performs no push, so it needs no credential.
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "NuGet"), ("Release:GitHubRelease", "false"), ("Release:DryRun", "true")]
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task GitHubRelease_GivenNoCredentials_ReportsTokenAndRepositoryId()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(repository, [("Release:Mode", "GitHubRelease")]);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(2);
		await Assert.That(problems[0]).Contains("GITHUB_TOKEN");
		await Assert.That(problems[1]).Contains("GITHUB_REPOSITORY_ID");
	}

	[Test]
	public async Task GitHubRelease_GivenTokenButNoRepositoryId_ReportsTheRepositoryId()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(repository, [("Release:Mode", "GitHubRelease"), ("GitHub:AccessToken", "token")]);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("GITHUB_REPOSITORY_ID");
	}

	[Test]
	public async Task GitHubRelease_GivenTokenAndRepositoryId_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Release:Mode", "GitHubRelease")],
			environment: EnvironmentWith(("GITHUB_TOKEN", "token"), ("GITHUB_REPOSITORY_ID", "123456"))
		);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task GitHubRelease_GivenDryRun_ReportsNothing()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(repository, [("Release:Mode", "GitHubRelease"), ("Release:DryRun", "true")]);

		// Assert
		await Assert.That(problems).IsEmpty();
	}

	[Test]
	public async Task BuildInputs_GivenMissingSolution_ReportsIt()
	{
		// Arrange
		using PreflightRepository repository = new();

		// Act
		var problems = FindProblems(repository, [("Build:Solution", "src/Missing.slnx"), ("Release:Mode", "None")]);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("Build:Solution");
		await Assert.That(problems[0]).Contains("does not exist");
	}

	[Test]
	public async Task BuildInputs_GivenMissingCSharpierPath_ReportsIt()
	{
		// Arrange
		using PreflightRepository repository = new("src/Product.slnx");

		// Act
		var problems = FindProblems(
			repository,
			[("Build:CSharpierPath", "tools/missing-csharpier.dll"), ("Release:Mode", "None")]
		);

		// Assert
		await Assert.That(problems).Count().IsEqualTo(1);
		await Assert.That(problems[0]).Contains("Build:CSharpierPath");
	}

	[Test]
	public async Task Validate_GivenSeveralProblems_ReportsThemAllAtOnce()
	{
		// Arrange
		using PreflightRepository repository = new();
		var configuration = Configuration([("Release:Mode", "LocalNuGet")]);

		// Act
		var act = () => ConfigurationPreflight.Validate(configuration, repository.Root, true, NoEnvironment);

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("Build:Solution");
		await Assert.That(exception.Message).Contains("PublishLocalNuGet");
	}
}

/// <summary>
/// A throwaway repository root for one preflight case, containing only the files the case needs.
/// </summary>
sealed class PreflightRepository : IDisposable
{
	public PreflightRepository(params string[] relativeFiles)
	{
		Root = Path.Combine(Path.GetTempPath(), "purview-build-preflight-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);

		foreach (var relativeFile in relativeFiles)
		{
			var path = Path.Combine(Root, relativeFile.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, string.Empty);
		}
	}

	public string Root { get; }

	public void Dispose()
	{
		if (Directory.Exists(Root))
			Directory.Delete(Root, recursive: true);
	}
}
