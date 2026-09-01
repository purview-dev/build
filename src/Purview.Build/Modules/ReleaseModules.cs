using ModularPipelines.GitHub.Extensions;

namespace Purview.Build.Modules;

[ModuleCategory("Release"), DependsOn<PackModule>]
public sealed class PublishModule(IOptions<BuildOptions> build, IOptions<ReleaseOptions> release) : Module<CommandResult[]>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration.Create().WithSkipWhen(_ =>
        release.Value.Mode is ReleaseMode.NuGet or ReleaseMode.LocalNuGet ? SkipDecision.DoNotSkip : SkipDecision.Skip("Package publishing is disabled.")).Build();
    protected override async Task<CommandResult[]?> ExecuteAsync(IModuleContext context, CancellationToken token)
    {
        var settings = release.Value;
        var source = settings.Mode == ReleaseMode.LocalNuGet ? settings.LocalFeed : settings.NuGetFeed;
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("The package feed is not configured.");
        Directory.CreateDirectory(settings.Mode == ReleaseMode.LocalNuGet ? Path.GetFullPath(source) : build.Value.ArtifactsDirectory);
        var key = settings.NuGetApiKey ?? Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? "local";
        var packages = Directory.EnumerateFiles(build.Value.ArtifactsDirectory, "*.nupkg", SearchOption.TopDirectoryOnly);
        return await Task.WhenAll(packages.Select(package => context.DotNet().Nuget.Push(new() {
            Path = package, Source = source, ApiKey = key, SkipDuplicate = true
        }, cancellationToken: token)));
    }
}

[ModuleCategory("Release"), DependsOn<PublishModule>, DependsOn<VersionModule>]
public sealed class GitHubReleaseModule(IOptions<ReleaseOptions> release) : Module<Release?>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration.Create().WithSkipWhen(_ =>
        release.Value.CreateGitHubRelease && release.Value.Mode is ReleaseMode.NuGet or ReleaseMode.GitHubRelease
            ? SkipDecision.DoNotSkip : SkipDecision.Skip("GitHub release creation is disabled.")).Build();
    protected override async Task<Release?> ExecuteAsync(IModuleContext context, CancellationToken token)
    {
        var version = (await context.GetModule<VersionModule>()).ValueOrDefault!;
        if (!long.TryParse(context.GitHub().EnvironmentVariables.RepositoryId, out var repositoryId))
            throw new InvalidOperationException("GITHUB_REPOSITORY_ID is missing or invalid.");
        var tag = $"v{version}";
        return await context.GitHub().Client.Repository.Release.Create(repositoryId,
            new NewRelease(tag) { Name = tag, GenerateReleaseNotes = true });
    }
}
