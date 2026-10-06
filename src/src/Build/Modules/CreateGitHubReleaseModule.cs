using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.GitHub.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using Purview.Build.Release;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

[ModuleCategory("Release")]
[DependsOn<PublishNuGetModule>]
[DependsOn<ValidatePackModule>]
[DependsOn<VersionModule>]
public sealed class CreateGitHubReleaseModule(
	IOptions<ReleaseSettings> releaseSettings,
	IOptions<GitHubSettings> gitSettings,
	IOptions<BuildSettings> buildSettings
	// Octokit.Release is qualified throughout: Purview.Build.Release is a namespace in this
	// assembly, so the unqualified name is ambiguous here.
) : Module<Octokit.Release[]>
{
	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				!releaseSettings.Value.ShouldCreateGitHubRelease()
				|| string.IsNullOrWhiteSpace(gitSettings.Value.GetGitHubToken())
					? SkipDecision.Skip(
						"GitHub release creation is disabled. Set Release__Mode=NuGet (or GitHubRelease), or "
							+ "Release__GitHubRelease=true, and GITHUB_TOKEN to create a GitHub release."
					)
					: SkipDecision.DoNotSkip
			)
			.Build();

	protected override async Task<Octokit.Release[]?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(CreateGitHubReleaseModule));

		var versionResult = await context.GetModule<VersionModule>();
		var units =
			versionResult.ValueOrDefault
			?? throw new InvalidOperationException(
				"The version was not produced by the version module."
			);

		if (releaseSettings.Value.DryRun)
		{
			foreach (var unit in units.Units)
			{
				context.Logger.LogInformation(
					"Release:DryRun is set. Would have created GitHub release {Tag}{Prerelease}.",
					unit.Tag,
					releaseSettings.Value.ShouldMarkPrerelease(unit.Version) ? " as a prerelease" : string.Empty
				);
			}

			return [];
		}

		var repositoryIdString = context.GitHub().EnvironmentVariables.RepositoryId;
		if (!long.TryParse(repositoryIdString, out var repositoryId))
		{
			throw new InvalidOperationException(
				$"Failed to parse RepositoryId '{repositoryIdString}' as a valid long integer."
			);
		}

		List<Octokit.Release> releases = [];

		foreach (var unit in units.Units)
			releases.Add(await CreateReleaseAsync(context, repositoryId, unit, cancellationToken));

		return [.. releases];
	}

	async Task<Octokit.Release> CreateReleaseAsync(
		IModuleContext context,
		long repositoryId,
		ReleaseUnit unit,
		CancellationToken cancellationToken
	)
	{
		// Create a new release on GitHub with the specified tag and generate release notes.
		// Prerelease versions are published as prereleases so they are not presented as the
		// latest stable release.
		var isPrerelease = releaseSettings.Value.ShouldMarkPrerelease(unit.Version);

		var release = await context
			.GitHub()
			.Client.Repository.Release.Create(
				repositoryId,
				new NewRelease(unit.Tag)
				{
					Name = unit.Tag,
					GenerateReleaseNotes = true,
					Prerelease = isPrerelease,
				}
			);

		context.Logger.LogInformation(
			"Created GitHub release {Tag}{Prerelease}.",
			unit.Tag,
			isPrerelease ? " as a prerelease" : string.Empty
		);

		if (releaseSettings.Value.UploadArtifacts)
			await UploadArtifactsAsync(context, release, cancellationToken);

		return release;
	}

	async Task UploadArtifactsAsync(
		IModuleContext context,
		Octokit.Release release,
		CancellationToken cancellationToken
	)
	{
		var artifactsFolder = buildSettings.Value.ArtifactsFolder;
		if (!Directory.Exists(artifactsFolder))
			return;

		foreach (
			var file in Directory.EnumerateFiles(artifactsFolder, "*.*", SearchOption.TopDirectoryOnly)
		)
		{
			await using var stream = File.OpenRead(file);
			await context
				.GitHub()
				.Client.Repository.Release.UploadAsset(
					release,
					new ReleaseAssetUpload
					{
						FileName = Path.GetFileName(file),
						ContentType = "application/octet-stream",
						RawData = stream,
					},
					cancellationToken
				);

			context.Logger.LogInformation("Uploaded release asset {File}.", Path.GetFileName(file));
		}
	}
}
