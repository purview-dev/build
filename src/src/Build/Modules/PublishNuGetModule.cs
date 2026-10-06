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
				|| (
					!nugetSettings.Value.TrustedPublishing
					&& string.IsNullOrWhiteSpace(nugetSettings.Value.GetNuGetAPIKey())
				)
					? SkipDecision.Skip(
						"NuGet publishing is disabled. Set Release__Mode=NuGet (or Release__Publish=true) and either NuGet__ApiKey (or NUGET_APIKEY) or NuGet__TrustedPublishing=true to publish packages."
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

		var artifactsFolder = buildSettings.Value.ArtifactsFolder;
		if (!Directory.Exists(artifactsFolder))
		{
			throw new InvalidOperationException(
				$"The artifacts folder '{artifactsFolder}' does not exist. "
					+ "Ensure the pack step ran (Release__Mode must not be None) before publishing."
			);
		}

		var packages = Directory
			.EnumerateFiles(artifactsFolder, "*.nupkg", SearchOption.TopDirectoryOnly)
			.ToList();

		if (packages.Count == 0)
		{
			throw new InvalidOperationException(
				$"No NuGet packages found in {buildSettings.Value.ArtifactsFolder}."
			);
		}

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

		var tasks = packages.Select(package =>
			context
				.DotNet()
				.Nuget.Push(
					new()
					{
						Path = package,
						Source = feedUrl,
						ApiKey = nugetSettings.Value.TrustedPublishing
							? null
							: nugetSettings.Value.GetNuGetAPIKey(),
						SkipDuplicate = true,
					},
					cancellationToken: cancellationToken
				)
		);

		return await Task.WhenAll(tasks);
	}
}
