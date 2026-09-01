namespace Purview.Build;

public sealed class BuildOptions
{
    public string Solution { get; init; } = "src";
    public string Configuration { get; init; } = "Release";
    public string ArtifactsDirectory { get; init; } = "artifacts";
    public bool Lint { get; init; } = true;
    public bool Test { get; init; } = true;
    public string TestRoot { get; init; } = "src/tests";
    public string[] TestPatterns { get; init; } = ["*Tests.csproj"];
    public string TestFilter { get; init; } = "/*/*/*/*/";
    public string[] TestArguments { get; init; } = ["--ignore-exit-code", "8"];
    public bool Pack { get; init; } = true;
    public string PackTarget { get; init; } = "src";
    public string VersionFile { get; init; } = "package.json";
}

public enum ReleaseMode { None, LocalNuGet, NuGet, GitHubRelease }

public sealed class ReleaseOptions
{
    public ReleaseMode Mode { get; init; }
    public string NuGetFeed { get; init; } = "https://api.nuget.org/v3/index.json";
    public string? NuGetApiKey { get; init; }
    public string? GitHubToken { get; init; }
    public string? LocalFeed { get; init; }
    public bool CreateGitHubRelease { get; init; } = true;
}
