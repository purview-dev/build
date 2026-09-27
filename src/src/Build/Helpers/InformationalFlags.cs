namespace Purview.Build.Helpers;

/// <summary>
/// Informational command-line flags that print something about the tool and exit without running a pipeline.
/// </summary>
enum InformationalFlag
{
	None,

	Version,

	Help,
}

/// <summary>
/// Recognizes the tool's informational flags before the pipeline is created, so they never start a run.
/// </summary>
static class InformationalFlags
{
	static readonly string[] VersionFlags = ["-v", "--version"];

	static readonly string[] HelpFlags = ["-h", "--help", "-?"];

	public static InformationalFlag Parse(IReadOnlyList<string> args)
	{
		if (Matches(args, VersionFlags))
			return InformationalFlag.Version;

		return Matches(args, HelpFlags) ? InformationalFlag.Help : InformationalFlag.None;
	}

	static bool Matches(IReadOnlyList<string> args, string[] flags) =>
		args.Any(arg => flags.Contains(arg, StringComparer.OrdinalIgnoreCase));
}