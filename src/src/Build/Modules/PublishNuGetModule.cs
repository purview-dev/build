using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

[ModuleCategory("Release")]
[DependsOn<PackModule>]
[DependsOn<ValidatePackModule>]
[DependsOn<RunTestsModule>]
public sealed class PublishNuGetModule(
	IOptions<BuildSettings> buildSettings,
	IOptions<NuGetSettings> nugetSettings,
	IOptions<ReleaseSettings> releaseSettings
) : Module<CommandResult[]>
{
	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				buildSettings.Value.ProjectType == ProjectType.Web
				|| !releaseSettings.Value.ShouldPublish()
					? SkipDecision.Skip(
						"NuGet publishing is disabled. Set Release__Mode=NuGet (or Release__Publish=true) to publish packages."
					)
					: SkipDecision.DoNotSkip
			)
			.Build();

	protected override async Task<CommandResult[]?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(PublishNuGetModule));

		var packages = NuGetPackages.Discover(buildSettings.Value.ArtifactsFolder);

		// The channel's feed wins over NuGet:FeedUrl, so a preview channel can target a different
		// feed without the caller rewriting NuGet:FeedUrl for every run.
		var feedUrl = releaseSettings.Value.ResolveChannel().FeedUrl ?? nugetSettings.Value.FeedUrl;

		if (releaseSettings.Value.DryRun)
		{
			foreach (var package in packages)
			{
				context.Logger.LogInformation(
					"Release:DryRun is set. Would have pushed {Package} to {FeedUrl}.",
					Path.GetFileName(package),
					feedUrl
				);
			}

			return [];
		}

		// Trusted Publishing does not mean "no API key": the OIDC exchange produces a short-lived
		// one, which NuGet/login puts in the environment for this step to find. Pushing with no key
		// at all is simply an unauthenticated push, and nuget.org rejects it.
		var apiKey = nugetSettings.Value.GetNuGetAPIKey();

		if (nugetSettings.Value.TrustedPublishing && string.IsNullOrWhiteSpace(apiKey))
		{
			throw new InvalidOperationException(
				"NuGet:TrustedPublishing is enabled, but no API key reached the pipeline. The OIDC "
					+ "exchange produces a short-lived key, so a publishing job must grant `id-token: write` "
					+ "and run the NuGet/login action before this step, exporting its output as NUGET_API_KEY."
			);
		}

		var tasks = packages.Select(package =>
			context
				.DotNet()
				.Nuget.Push(
					new()
					{
						Path = package,
						Source = feedUrl,
						ApiKey = apiKey,
						SkipDuplicate = true,
					},
					cancellationToken: cancellationToken
				)
		);

		return await Task.WhenAll(tasks);
	}
}
