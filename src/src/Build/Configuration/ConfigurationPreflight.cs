using System.ComponentModel.DataAnnotations;

namespace Purview.Build.Configuration;

/// <summary>
/// Verifies, before the pipeline is built, that the capabilities a run intends to use have everything
/// they require.
/// </summary>
/// <remarks>
/// A missing local feed path, NuGet credential or GitHub token used to surface only when the owning
/// module executed - after clean, restore, build, test and pack had already run. Reporting every
/// problem here makes the failure immediate, and keeps a requested release from being silently
/// declined.
/// </remarks>
static class ConfigurationPreflight
{
	/// <summary>
	/// Fails the run when the resolved configuration cannot support what it asks the pipeline to do.
	/// </summary>
	public static void Validate(IConfiguration configuration, string repositoryRoot) =>
		Validate(
			configuration,
			repositoryRoot,
			ExecutionEnvironment.IsRunningLocally(),
			Environment.GetEnvironmentVariable
		);

	internal static void Validate(
		IConfiguration configuration,
		string repositoryRoot,
		bool isRunningLocally,
		Func<string, string?> environment
	)
	{
		var problems = FindProblems(configuration, repositoryRoot, isRunningLocally, environment);

		if (problems.Count == 0)
			return;

		throw new InvalidOperationException(
			"The pipeline cannot start because required configuration is missing or invalid:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem))
		);
	}

	/// <summary>
	/// The requirements the resolved configuration does not satisfy, in reporting order.
	/// </summary>
	internal static IReadOnlyList<string> FindProblems(
		IConfiguration configuration,
		string repositoryRoot,
		bool isRunningLocally,
		Func<string, string?> environment
	)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(repositoryRoot);
		ArgumentNullException.ThrowIfNull(environment);

		var build = configuration.GetSection(BuildSettings.SectionName).Get<BuildSettings>() ?? new();
		var release =
			configuration.GetSection(ReleaseSettings.SectionName).Get<ReleaseSettings>() ?? new();
		var nuget = configuration.GetSection(NuGetSettings.SectionName).Get<NuGetSettings>() ?? new();
		var localNuGet =
			configuration
				.GetSection(PublishLocalNuGetSettings.SectionName)
				.Get<PublishLocalNuGetSettings>() ?? new();
		var github =
			configuration.GetSection(GitHubSettings.SectionName).Get<GitHubSettings>() ?? new();

		List<string> problems = [];

		ValidateBuildInputs(build, repositoryRoot, problems);
		ValidateLocalNuGet(build, release, localNuGet, isRunningLocally, environment, problems);
		ValidateRemoteNuGetPublish(build, release, nuget, environment, problems);
		ValidateGitHubRelease(release, github, environment, problems);

		return problems;
	}

	static void ValidateBuildInputs(
		BuildSettings build,
		string repositoryRoot,
		List<string> problems
	)
	{
		if (build.ProjectType != ProjectType.DotNet)
			return;

		if (string.IsNullOrWhiteSpace(build.Solution))
		{
			problems.Add("Build:Solution is required for a DotNet project.");
		}
		else
		{
			var solution = ResolvePath(repositoryRoot, build.Solution);
			if (!File.Exists(solution))
			{
				problems.Add(
					$"Build:Solution '{build.Solution}' does not exist (resolved to '{solution}')."
				);
			}
		}

		if (!build.RunLint || string.IsNullOrWhiteSpace(build.CSharpierPath))
			return;

		var csharpier = ResolvePath(repositoryRoot, build.CSharpierPath);
		if (!File.Exists(csharpier))
		{
			problems.Add(
				$"Build:CSharpierPath '{build.CSharpierPath}' does not exist (resolved to '{csharpier}')."
			);
		}
	}

	static void ValidateLocalNuGet(
		BuildSettings build,
		ReleaseSettings release,
		PublishLocalNuGetSettings settings,
		bool isRunningLocally,
		Func<string, string?> environment,
		List<string> problems
	)
	{
		// The module is intentionally ignored in CI and for Web projects, so demanding the feed path
		// there would fail a run that never touches it. Shared with the module so the two cannot
		// disagree about whether the path is needed.
		if (!LocalNuGetPublishing.IsEnabled(build.ProjectType, release.Mode, isRunningLocally))
			return;

		// Bind the resolved path into the instance being validated, so the same environment lookup
		// the module uses is the one the validator sees.
		var effective = settings with
		{
			LocalFeedPath = settings.GetLocalFeedPath(environment) ?? string.Empty,
		};

		// The lookup travels with the context so the validator resolves the feed path exactly as the
		// module would. Without it the validator reaches for the real process environment, and a
		// developer machine with LOCAL_NUGET_FEED_PATH set would quietly satisfy a requirement this
		// preflight was asked to check as absent.
		ValidationContext validationContext = new(
			effective,
			serviceProvider: null,
			items: new Dictionary<object, object?>
			{
				[PublishLocalNuGetSettings.EnvironmentLookupKey] = environment,
			}
		);

		List<ValidationResult> results = [];
		if (Validator.TryValidateObject(effective, validationContext, results, validateAllProperties: true))
			return;

		problems.AddRange(
			results
				.Select(result => result.ErrorMessage)
				.Where(message => !string.IsNullOrWhiteSpace(message))
				.Select(message => $"PublishLocalNuGet: {message}")
		);
	}

	static void ValidateRemoteNuGetPublish(
		BuildSettings build,
		ReleaseSettings release,
		NuGetSettings nuget,
		Func<string, string?> environment,
		List<string> problems
	)
	{
		// Web projects produce no .nupkg, and a dry run performs no push.
		if (build.ProjectType == ProjectType.Web || !release.ShouldPublish() || release.DryRun)
			return;

		var feedUrl = release.ResolveChannel().FeedUrl ?? nuget.FeedUrl;
		if (string.IsNullOrWhiteSpace(feedUrl))
		{
			problems.Add(
				"Release:Publish is enabled, but no NuGet feed is configured. "
					+ "Set NuGet:FeedUrl or the channel's FeedUrl."
			);
		}

		if (
			nuget.TrustedPublishing
			|| !string.IsNullOrWhiteSpace(nuget.GetNuGetAPIKey(environment))
		)
			return;

		problems.Add(
			"Release:Publish is enabled, but no NuGet API key is configured. "
				+ "Set NUGET_APIKEY (or NuGet:APIKey), or set NuGet:TrustedPublishing=true."
		);
	}

	static void ValidateGitHubRelease(
		ReleaseSettings release,
		GitHubSettings github,
		Func<string, string?> environment,
		List<string> problems
	)
	{
		if (!release.ShouldCreateGitHubRelease() || release.DryRun)
			return;

		if (string.IsNullOrWhiteSpace(github.GetGitHubToken(environment)))
		{
			problems.Add(
				"Release:GitHubRelease is enabled, but GITHUB_TOKEN is not set. "
					+ "Provide a token that can create releases."
			);
		}

		if (string.IsNullOrWhiteSpace(environment("GITHUB_REPOSITORY_ID")))
		{
			problems.Add(
				"Release:GitHubRelease is enabled, but GITHUB_REPOSITORY_ID is not set. "
					+ "Run the release from GitHub Actions, or set it to the repository's numeric id."
			);
		}
	}

	static string ResolvePath(string repositoryRoot, string path)
	{
		var combined = Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path);

		try
		{
			return Path.GetFullPath(combined);
		}
		catch (Exception exception)
			when (exception is ArgumentException or NotSupportedException or PathTooLongException)
		{
			// Let File.Exists report the malformed path as missing rather than masking it with an
			// unrelated exception from the preflight itself.
			return combined;
		}
	}
}
