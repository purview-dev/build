using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

/// <summary>
/// Publishes the release's packages to the organization's GitHub Packages feed, in addition to NuGet.org.
/// </summary>
/// <remarks>
/// Deliberately independent of <see cref="PublishNuGetModule"/>: both depend on the validated pack
/// output and run concurrently, so a slow NuGet.org indexing does not hold the mirror back, and one
/// registry's failure is reported against that registry rather than hidden behind the other.
/// <see cref="CreateGitHubReleaseModule"/> depends on both, so the tag and GitHub release are only
/// created once every requested destination accepted the artifacts.
/// <para>
/// The push goes through <c>dotnet nuget push</c> with a throwaway configuration holding the feed
/// credentials, because <c>dotnet nuget push</c> has no username/password switches. That keeps the
/// credential out of the repository, out of the command line and out of logs; the file is deleted
/// whether the push succeeds, fails or is cancelled.
/// </para>
/// </remarks>
[ModuleCategory("Release")]
[DependsOn<PackModule>]
[DependsOn<ValidatePackModule>]
[DependsOn<RunTestsModule>]
public sealed class PublishGitHubPackagesModule(
	IOptions<BuildSettings> buildSettings,
	IOptions<NuGetSettings> nugetSettings,
	IOptions<ReleaseSettings> releaseSettings,
	IOptions<GitHubSettings> githubSettings
) : Module<CommandResult[]>
{
	/// <summary>
	/// The source name the throwaway configuration declares. It is never added to a repository or user
	/// configuration; it exists only inside this module's temp file.
	/// </summary>
	const string SourceName = "purview-github-packages";

	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				GitHubPackagesPublishing.DisabledReason(
					buildSettings.Value.ProjectType,
					releaseSettings.Value,
					nugetSettings.Value
				) is { } reason
					? SkipDecision.Skip(reason)
					: SkipDecision.DoNotSkip
			)
			.Build();

	protected override async Task<CommandResult[]?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(PublishGitHubPackagesModule));

		var packages = NuGetPackages.Discover(buildSettings.Value.ArtifactsFolder);
		var settings = nugetSettings.Value.GitHubPackages;
		var feedUrl = settings.ResolveFeedUrl();
		var username = settings.ResolveUsername();

		if (releaseSettings.Value.DryRun)
		{
			foreach (var package in packages)
			{
				context.Logger.LogInformation(
					"Release:DryRun is set. Would have pushed {Package} to GitHub Packages ({FeedUrl}).",
					Path.GetFileName(package),
					feedUrl
				);
			}

			return [];
		}

		var token = githubSettings.Value.GetGitHubToken();
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException(
				"GitHub Packages publishing is enabled, but GITHUB_TOKEN is not set. "
					+ "The shared release workflow provides it automatically; a manual run must export it."
			);
		}

		var configPath = GitHubPackagesPublishing.WritePushConfig(feedUrl, username, token);

		try
		{
			List<CommandResult> results = [];
			List<string> failures = [];

			foreach (var package in packages)
			{
				var result = await PushAsync(context, package, configPath, cancellationToken);
				results.Add(result);

				var fileName = Path.GetFileName(package);

				if (result.ExitCode == 0)
				{
					context.Logger.LogInformation(
						"Pushed {Package} to GitHub Packages ({FeedUrl}).",
						fileName,
						feedUrl
					);
					continue;
				}

				failures.Add(DescribeFailure(fileName, result));
			}

			context.Summary.KeyValue(
				"Publish",
				"GitHub Packages",
				$"{packages.Count - failures.Count}/{packages.Count} pushed"
			);

			if (failures.Count > 0)
			{
				throw new InvalidOperationException(
					$"GitHub Packages publishing failed for {failures.Count} of {packages.Count} package(s). "
						+ "NuGet.org publication is reported by its own module and is not affected by this failure."
						+ Environment.NewLine
						+ string.Join(Environment.NewLine, failures)
				);
			}

			// The module's result is the raw command results, so a caller can inspect them for details if desired.
			return [.. results];
		}
		finally
		{
			DeleteConfig(configPath);
		}
	}

	static Task<CommandResult> PushAsync(
		IModuleContext context,
		string package,
		string configPath,
		CancellationToken cancellationToken
	) =>
		context.Shell.Command.ExecuteCommandLineTool(
			DotNetCLIOptions.Create(
				"nuget",
				"push",
				package,
				"--source",
				SourceName,
				"--configfile",
				configPath,
				"--skip-duplicate",
				// GitHub Packages has no symbol server: the .snupkg stays a NuGet.org artifact.
				"--no-symbols"
			),
			GitHubPackagesPublishing.CreatePushExecutionOptions(),
			cancellationToken
		);

	static string DescribeFailure(string fileName, CommandResult result)
	{
		var output = string.Join(
			Environment.NewLine,
			new[] { result.StandardError, result.StandardOutput }
				.Where(text => !string.IsNullOrWhiteSpace(text))
				.Select(text => text.Trim())
		);

		var remediation = IsAuthenticationFailure(output)
			? " The token needs `packages: write` (granted by the caller workflow's permissions block), and "
				+ "the package must grant this repository write access (Package settings -> Manage Actions access)."
			: string.Empty;

		return $"  {fileName}: exit code {result.ExitCode}.{remediation}"
			+ (output.Length == 0 ? string.Empty : Environment.NewLine + Indent(output));
	}

	static bool IsAuthenticationFailure(string output) =>
		output.Contains("401", StringComparison.Ordinal)
		|| output.Contains("403", StringComparison.Ordinal)
		|| output.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
		|| output.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);

	static string Indent(string text) =>
		string.Join(
			Environment.NewLine,
			text.Split('\n').Select(line => "    " + line.TrimEnd('\r'))
		);

	static void DeleteConfig(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// A temp file that cannot be deleted is not worth failing an otherwise finished publish.
		}
	}
}
