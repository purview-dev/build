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
	/// The <c>--help</c> text. Treated like any other CLI build tool: usage and options first, then the
	/// configuration keys the tool accepts.
	/// </summary>
	public static string HelpText =>
		$$"""
		{{VersionLine}} - shared build, test, pack, and release pipeline.

		Usage:
		  purview-build [configuration overrides]

		Options:
		  -v, --version   Print the {{Name}} version and exit.
		  -h, --help      Print this help and exit.

		Configuration precedence: command line > environment variables > purview-build.json > defaults.
		  --Build:RunPack=false                        command-line override
		  Build__RunPack=false                         environment variable (nested keys use "__")
		  { "Build": { "RunPack": false } }            purview-build.json at the repository root

		Modules run in dependency order: CleanArtifacts, Version, Restore, Build, Lint, RunTests, Pack,
		ValidatePack, PublishNuGet, PublishLocalNuGet, CreateGitHubRelease. Each logs a line when it starts and
		when it finishes; a failing run reports the failed module's error and exits with code 1.

		Set PURVIEW_BUILD_STACKTRACE=1 to include stack traces when an unexpected failure is reported.
		Documentation: https://purview.dev/docs/build/
		""";

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