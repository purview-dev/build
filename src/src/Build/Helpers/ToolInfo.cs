using Purview.Build.Configuration;
using System.Reflection;

namespace Purview.Build.Helpers;

/// <summary>
/// Identity of the build tool itself, as opposed to the repository it operates on.
/// </summary>
static class ToolInfo
{
	public const string Name = "Purview.Build";

	/// <summary>
	/// The package version of the running tool, without any build metadata (<c>+sha</c>) suffix.
	/// </summary>
	public static string Version { get; } = ResolveVersion();

	public static string VersionLine => $"{Name} {Version}";

	/// <summary>
	/// The <c>--help</c> text, without configuration resolution. Kept for callers that have not
	/// resolved configuration yet; <see cref="BuildHelpText"/> is what the CLI prints.
	/// </summary>
	public static string HelpText => BuildHelpText(resolution: null);

	/// <summary>
	/// The <c>--help</c> text. Treated like any other CLI build tool: usage and options first, then the
	/// configuration keys the tool accepts, then where configuration was actually found.
	/// </summary>
	public static string BuildHelpText(ConfigResolution? resolution) =>
		$$"""
		{{VersionLine}} - shared build, test, pack, and release pipeline.

		Usage:
		  purview-build [configuration overrides]
		  purview-build release-explain [--format=json]

		Commands:
		  release-explain  Explain the release decision and exit, without running any module.
		                   --format=json emits the stable contract the reusable workflow consumes.

		Options:
		  -v, --version        Print the {{Name}} version and exit.
		  -h, --help           Print this help and exit.
		  -c, --config <path>  Use this configuration file. Absolute, or relative to the current
		                       directory; a directory resolves to purview-build.json inside it.
		                       Skips probing entirely. A path that does not exist is an error.
		      --user-config    Also read machine-local user configuration (local runs only).

		Configuration precedence: command line > environment variables > purview-build.json
		  > user config (opt-in, local only) > defaults.
		  --Build:RunPack=false                        command-line override
		  Build__RunPack=false                         environment variable (nested keys use "__")
		  { "Build": { "RunPack": false } }            purview-build.json

		  PURVIEW_BUILD_CONFIG=<path>       selects WHICH configuration file (--config wins over it)
		  PURVIEW_BUILD_USER_CONFIG=1       same as --user-config
		  MODULAR_PIPELINES_DIRECTORY       unrelated: the directory holding the shipped
		                                    appsettings.json, not repository configuration

		{{DescribeProbeOrder()}}
		{{DescribeResolvedConfig(resolution)}}
		Relative paths in the configuration file (Build:Solution, Build:TestRoot, Build:ArtifactsFolder,
		Build:WebBuildOutput, PackValidation globs) always resolve against the repository root, wherever
		the configuration file itself was found.

		Modules run in dependency order: CleanArtifacts, Version, ReportEligibility, Restore, Build, Lint,
		RunTests, Pack, ValidatePack, PublishNuGet, PublishLocalNuGet, CreateGitHubRelease. Each logs a line
		when it starts and when it finishes; a failing run reports the failed module's error and exits 1.

		Set PURVIEW_BUILD_STACKTRACE=1 to include stack traces when an unexpected failure is reported.
		Documentation: https://purview.dev/docs/build/
		""";

	static string DescribeProbeOrder()
	{
		var lines = ConfigProbePaths.All.Select(
			(probe, index) => $"  {index + 1}. {probe.RelativePath,-32} {probe.Rationale}"
		);

		return $"""
			Default probe order, relative to the repository root (first match wins, and any lower-priority
			file that also exists is reported as shadowed, never merged):
			{string.Join(Environment.NewLine, lines)}
			""";
	}

	static string DescribeResolvedConfig(ConfigResolution? resolution)
	{
		if (resolution is null)
			return string.Empty;

		// The resolution is known, so report it. The tool's own help text is not a substitute for the
		return $"""

			Resolved configuration for this directory:
			{string.Join(Environment.NewLine, ConfigurationChain.DescribeResolution(resolution).Select(line => "  " + line))}

			""";
	}

	static string ResolveVersion()
	{
		var assembly = typeof(ToolInfo).Assembly;

		var informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;

		if (!string.IsNullOrWhiteSpace(informationalVersion))
		{
			var buildMetadata = informationalVersion.IndexOf('+', StringComparison.Ordinal);
			return buildMetadata < 0 ? informationalVersion : informationalVersion[..buildMetadata];
		}

		return assembly.GetName().Version?.ToString() ?? "unknown";
	}
}
