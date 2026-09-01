using System.Text.Json;
using ModularPipelines.GitHub.Extensions;

namespace Purview.Build.Modules;

[ModuleCategory("Build")]
public sealed class VersionModule(IOptions<BuildOptions> options) : Module<NuGetVersion>
{
    protected override async Task<NuGetVersion?> ExecuteAsync(IModuleContext context, CancellationToken token)
    {
        var path = Path.GetFullPath(options.Value.VersionFile);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
        var value = json.RootElement.GetProperty("version").GetString();
        if (!NuGetVersion.TryParse(value, out var version))
            throw new InvalidOperationException($"'{value}' in {path} is not a valid semantic version.");
        context.Summary.KeyValue("Version", "Package version", version.ToFullString());
        return version;
    }
}

[ModuleCategory("Build")]
public sealed class RestoreModule(IOptions<BuildOptions> options) : Module<CommandResult>
{
    protected override Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken token) =>
        context.DotNet().Restore(new DotNetRestoreOptions { ProjectSolution = options.Value.Solution }, cancellationToken: token);
}

[ModuleCategory("Build"), DependsOn<RestoreModule>]
public sealed class BuildModule(IOptions<BuildOptions> options) : Module<CommandResult>
{
    protected override Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken token) =>
        context.DotNet().Build(new DotNetBuildOptions {
            ProjectSolution = options.Value.Solution, Configuration = options.Value.Configuration, NoRestore = true
        }, cancellationToken: token);
}

[ModuleCategory("Build")]
public sealed class LintModule(IOptions<BuildOptions> options) : Module<CommandResult>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration.Create()
        .WithSkipWhen(_ => options.Value.Lint ? SkipDecision.DoNotSkip : SkipDecision.Skip("Lint is disabled.")).Build();
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken token)
    {
        await context.DotNet().Tool.Restore(new() { Interactive = false }, new(), token);
        return await context.Shell.Command.ExecuteCommandLineTool(
            new DotNetCommand { Tool = "dotnet", CommandParts = ["tool", "run", "csharpier", "check", Directory.GetCurrentDirectory()] },
            cancellationToken: token);
    }
}

[ModuleCategory("Build"), DependsOn<BuildModule>]
public sealed class TestModule(IOptions<BuildOptions> options) : Module<CommandResult[]>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration.Create()
        .WithSkipWhen(_ => options.Value.Test ? SkipDecision.DoNotSkip : SkipDecision.Skip("Tests are disabled.")).Build();
    protected override async Task<CommandResult[]?> ExecuteAsync(IModuleContext context, CancellationToken token)
    {
        var settings = options.Value;
        if (!Directory.Exists(settings.TestRoot)) return [];
        var projects = settings.TestPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(settings.TestRoot, pattern, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var arguments = settings.TestArguments.Concat(string.IsNullOrWhiteSpace(settings.TestFilter)
            ? [] : new[] { "--treenode-filter", settings.TestFilter }).ToArray();
        return await Task.WhenAll(projects.Select(project => context.DotNet().Test(new DotNetTestOptions {
            Project = project, Configuration = settings.Configuration, NoBuild = true, NoRestore = true, Arguments = arguments
        }, cancellationToken: token)));
    }
}

[ModuleCategory("Build"), DependsOn<BuildModule>, DependsOn<TestModule>, DependsOn<VersionModule>]
public sealed class PackModule(IOptions<BuildOptions> options) : Module<CommandResult>
{
    protected override ModuleConfiguration Configure() => ModuleConfiguration.Create()
        .WithSkipWhen(_ => options.Value.Pack ? SkipDecision.DoNotSkip : SkipDecision.Skip("Packing is disabled.")).Build();
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken token)
    {
        var version = (await context.GetModule<VersionModule>()).ValueOrDefault!;
        Directory.CreateDirectory(options.Value.ArtifactsDirectory);
        return await context.DotNet().Pack(new DotNetPackOptions {
            ProjectSolution = options.Value.PackTarget, Configuration = options.Value.Configuration,
            Output = options.Value.ArtifactsDirectory, NoBuild = true,
            Properties = [("PackageVersion", version.ToFullString()), ("Version", version.ToFullString())]
        }, cancellationToken: token);
    }
}

public sealed record DotNetCommand : ModularPipelines.Options.CommandLineToolOptions;
