namespace Purview.Build.Configuration;

/// <summary>
/// The single definition of the configuration chain, so the pipeline and <c>release-explain</c> can
/// never disagree about what a run is configured with.
/// </summary>
/// <remarks>
/// Order, lowest priority first:
/// <list type="number">
/// <item>the shipped <c>appsettings.json</c> (located by <c>MODULAR_PIPELINES_DIRECTORY</c>, which
/// controls only that file and nothing about repository configuration discovery),</item>
/// <item>machine-local user configuration, when opted in and running locally,</item>
/// <item>the resolved repository configuration file, wherever it was found,</item>
/// <item>environment variables (<c>__</c> for nesting),</item>
/// <item>command line.</item>
/// </list>
/// </remarks>
static class ConfigurationChain
{
	/// <summary>
	/// Resolves everything a run needs before any configuration is read.
	/// </summary>
	public static PipelineStartup ResolveStartup(ToolCommandLine commandLine)
	{
		ArgumentNullException.ThrowIfNull(commandLine);

		var pipelineDirectory = PipelineProjectDirectory.Find();
		var repositoryRoot = PathHelpers.FindRepositoryRoot(Environment.CurrentDirectory);

		var resolution = ConfigFileLocator.Resolve(
			repositoryRoot,
			commandLine.ConfigPath,
			commandLine.UserConfig
		);

		if (resolution.Path is not null)
			JsonConfigFile.Validate(resolution.Path);

		if (resolution.UserConfigActive && resolution.UserConfigPath is not null)
			JsonConfigFile.Validate(resolution.UserConfigPath);

		return new(pipelineDirectory, repositoryRoot, resolution);
	}

	/// <summary>
	/// Applies the chain to a configuration builder.
	/// </summary>
	public static void Apply(
		IConfigurationBuilder configuration,
		PipelineStartup startup,
		string[] args
	)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(startup);

		configuration.AddJsonFile(
			Path.Combine(startup.PipelineDirectory, "appsettings.json"),
			optional: false
		);

		if (startup.Resolution.UserConfigActive && startup.Resolution.UserConfigPath is not null)
			configuration.AddJsonFile(startup.Resolution.UserConfigPath, optional: true);

		if (startup.Resolution.Path is not null)
			configuration.AddJsonFile(startup.Resolution.Path, optional: true);

		configuration.AddEnvironmentVariables().AddCommandLine(args);
	}

	/// <summary>
	/// Builds a standalone configuration root for <c>release-explain</c>, which never creates a
	/// pipeline.
	/// </summary>
	public static IConfigurationRoot Build(PipelineStartup startup, string[] args)
	{
		ConfigurationBuilder configuration = new();
		Apply(configuration, startup, args);

		return configuration.Build();
	}

	/// <summary>
	/// The lines a run reports about where its configuration came from.
	/// </summary>
	public static IReadOnlyList<string> DescribeResolution(ConfigResolution resolution)
	{
		ArgumentNullException.ThrowIfNull(resolution);

		List<string> lines =
		[
			resolution.Found
				? $"Configuration: {resolution.Path} (selected by {Describe(resolution.Source)})"
				: "Configuration: none found; using the tool's built-in defaults.",
		];

		if (resolution.UserConfigActive && resolution.UserConfigPath is not null)
			lines.Add($"User configuration: {resolution.UserConfigPath} (active)");
		else if (resolution.UserConfigSkipReason is not null)
			lines.Add($"User configuration: not applied — {resolution.UserConfigSkipReason}.");

		return lines;
	}

	/// <summary>
	/// The warnings a run reports: shadowed files, and a near-miss filename when nothing was found.
	/// </summary>
	public static IReadOnlyList<string> DescribeWarnings(ConfigResolution resolution)
	{
		ArgumentNullException.ThrowIfNull(resolution);

		List<string> warnings = [];

		if (resolution.ShadowedPaths.Count > 0)
		{
			warnings.Add(
				$"warning: {resolution.ShadowedPaths.Count} other {ConfigProbePaths.FileName} "
					+ $"file(s) exist and are NOT being read, because '{resolution.Path}' comes first in the "
					+ "probe order:"
			);
			warnings.AddRange(resolution.ShadowedPaths.Select(path => $"  {path}"));
			warnings.Add(
					"  Configuration files are not merged. Delete or consolidate the shadowed file(s)."
			);
		}

		if (resolution.NearMissPath is not null)
		{
			warnings.Add(
				$"warning: no {ConfigProbePaths.FileName} was found, but '{resolution.NearMissPath}' "
					+ "has a very similar name. It was NOT loaded; rename it if it was meant to configure "
					+ "the pipeline."
			);
		}

		return warnings;
	}

	static string Describe(ConfigSource source) =>
		source switch
		{
			ConfigSource.CommandLine => "--config",
			ConfigSource.EnvironmentVariable => ConfigFileLocator.EnvironmentVariableName,
			ConfigSource.Probe => "the default probe order",
			_ => "nothing",
		};
}

/// <summary>
/// The directories and configuration resolution a run starts from.
/// </summary>
sealed record PipelineStartup(
	string PipelineDirectory,
	string RepositoryRoot,
	ConfigResolution Resolution
);
