using ModularPipelines.Models;

namespace Purview.Build.Helpers;

/// <summary>
/// Builds and runs the tool's pipeline: configuration binding, shared services, module registration, and
/// failure collection.
/// </summary>
/// <remarks>
/// Module failures are reported by the caller (CLI style) rather than thrown, which is why the pipeline is
/// configured with <c>PipelineOptions.ThrowOnPipelineFailure</c> disabled.
/// </remarks>
static class BuildPipeline
{
	/// <summary>
	/// Runs the pipeline and returns the failed module results (empty when every module succeeded or skipped).
	/// </summary>
	public static async Task<IReadOnlyList<IModuleResult>> RunAsync(string[] args)
	{
		var pipelineDirectory = PipelineProjectDirectory.Find();
		var repositoryRoot = PathHelpers.FindRepositoryRoot(Environment.CurrentDirectory);

		var builder = Pipeline.CreateBuilder(args);

		AddConfiguration(builder, args, pipelineDirectory, repositoryRoot);
		ApplyLogLevel(builder);
		BindSettings(builder);
		AddGitHubClient(builder);
		AddModules(builder);

		builder.ConfigurePipelineOptions(options => options.ThrowOnPipelineFailure = false);

		// Modules resolve every configured path relative to the repository root.
		Environment.CurrentDirectory = repositoryRoot;

		await using var pipeline = await builder.BuildAsync();

		var summary = await pipeline.RunAsync();

		return summary.GetFailedModuleResults();
	}

	static void AddConfiguration(
		PipelineBuilder builder,
		string[] args,
		string pipelineDirectory,
		string repositoryRoot
	) =>
		builder
			.Configuration.AddJsonFile(Path.Combine(pipelineDirectory, "appsettings.json"), optional: false)
			.AddJsonFile(Path.Combine(repositoryRoot, "purview-build.json"), optional: true)
			.AddEnvironmentVariables()
			.AddCommandLine(args);

	/// <summary>
	/// Applies <c>Build:LogLevel</c> to the pipeline logger, defaulting to <see cref="LogLevel.Information"/> so
	/// a run reports each module's command output and progress (set it to <c>Warning</c> for quiet output).
	/// </summary>
	static void ApplyLogLevel(PipelineBuilder builder)
	{
		var configured = builder.Configuration[$"{BuildSettings.SectionName}:{nameof(BuildSettings.LogLevel)}"];

		builder.SetLogLevel(
			Enum.TryParse<LogLevel>(configured, ignoreCase: true, out var logLevel)
				? logLevel
				: LogLevel.Information
		);
	}

	static void BindSettings(PipelineBuilder builder)
	{
		builder.Services.Configure<BuildSettings>(builder.Configuration.GetSection(BuildSettings.SectionName));
		builder.Services.Configure<NuGetSettings>(builder.Configuration.GetSection(NuGetSettings.SectionName));
		builder.Services.Configure<PackValidationSettings>(
			builder.Configuration.GetSection(PackValidationSettings.SectionName)
		);
		builder.Services.Configure<PublishLocalNuGetSettings>(
			builder.Configuration.GetSection(PublishLocalNuGetSettings.SectionName)
		);
		builder.Services.Configure<GitHubSettings>(builder.Configuration.GetSection(GitHubSettings.SectionName));
		builder.Services.Configure<ReleaseSettings>(
			builder.Configuration.GetSection(ReleaseSettings.SectionName)
		);
	}

	static void AddGitHubClient(PipelineBuilder builder) =>
		builder.Services.AddSingleton<IGitHubClient>(serviceProvider =>
		{
			var settings = serviceProvider.GetRequiredService<IOptions<GitHubSettings>>();

			return new GitHubClient(
				new(settings.Value.ProductHeader),
				new InMemoryCredentialStore(new(settings.Value.GetGitHubToken()))
			);
		});

	static void AddModules(PipelineBuilder builder) =>
		builder
			.AddModule<CleanArtifactsModule>()
			.AddModule<VersionModule>()
			.AddModule<RestoreModule>()
			.AddModule<BuildModule>()
			.AddModule<LintModule>()
			.AddModule<RunTestsModule>()
			.AddModule<PackModule>()
			.AddModule<ValidatePackModule>()
			.AddModule<PublishNuGetModule>()
			.AddModule<PublishLocalNuGetModule>()
			.AddModule<CreateGitHubReleaseModule>();
}