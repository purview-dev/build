using Purview.Build;

var root = Directory.GetCurrentDirectory();
var builder = Pipeline.CreateBuilder(args);
builder.Configuration
    .AddJsonFile(Path.Combine(root, "purview-build.json"), optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.Configure<BuildOptions>(builder.Configuration.GetSection("Build"));
builder.Services.Configure<ReleaseOptions>(builder.Configuration.GetSection("Release"));
builder.Services.AddSingleton<IGitHubClient>(services =>
{
    var token = services.GetRequiredService<IOptions<ReleaseOptions>>().Value.GitHubToken
        ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty;
    return new GitHubClient(new ProductHeaderValue("Purview.Build"), new InMemoryCredentialStore(new Credentials(token)));
});

builder.AddModule<VersionModule>().AddModule<RestoreModule>().AddModule<BuildModule>()
    .AddModule<LintModule>().AddModule<TestModule>().AddModule<PackModule>()
    .AddModule<PublishModule>().AddModule<GitHubReleaseModule>();

await using var pipeline = await builder.BuildAsync();
await pipeline.RunAsync();
