namespace Purview.Build.Helpers;

/// <summary>
/// What the tool was asked to do.
/// </summary>
enum ToolCommand
{
	/// <summary>Run the pipeline (the default).</summary>
	RunPipeline,

	/// <summary>Print the version and exit.</summary>
	Version,

	/// <summary>Print the help and exit.</summary>
	Help,

	/// <summary>Explain the release decision and exit, without running any module.</summary>
	ReleaseExplain,

	/// <summary>Validate the root package.json and exit, without running any module.</summary>
	PackageValidate,
}

/// <summary>
/// Output shape for <c>release-explain</c>.
/// </summary>
enum OutputFormat
{
	/// <summary>Human-readable, through the tool's console.</summary>
	Text,

	/// <summary>The stable JSON contract the reusable workflow consumes.</summary>
	Json,
}

/// <summary>
/// The tool's own command line, separated from the configuration overrides the pipeline binds.
/// </summary>
/// <remarks>
/// The configuration command-line provider accepts only <c>--key=value</c> / <c>--key value</c>
/// forms, so a bare verb (<c>release-explain</c>) or a single-dash option (<c>-c path</c>) would
/// make it throw. The tool's own arguments are therefore removed here and only the remainder is
/// handed to the pipeline.
/// </remarks>
sealed record ToolCommandLine(
	ToolCommand Command,
	string? ConfigPath,
	bool UserConfig,
	OutputFormat Format,
	string[] PipelineArguments
)
{
	const string ReleaseExplainVerb = "release-explain";

	const string PackageValidateVerb = "package-validate";

	static readonly string[] ConfigFlags = ["--config", "-c"];

	public static ToolCommandLine Parse(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);

		// Informational flags win, and are recognised before anything else can fail to parse.
		var informational = InformationalFlags.Parse(args);
		var command = informational switch
		{
			InformationalFlag.Version => ToolCommand.Version,
			InformationalFlag.Help => ToolCommand.Help,
			InformationalFlag.None or _ => ToolCommand.RunPipeline,
		};

		string? configPath = null;
		var userConfig = false;
		var format = OutputFormat.Text;
		List<string> remaining = [];

		for (var index = 0; index < args.Length; index++)
		{
			var arg = args[index];

			if (TryReadVerb(arg, ReleaseExplainVerb, command, ToolCommand.ReleaseExplain, out var explained))
			{
				command = explained;
				continue;
			}

			if (
				TryReadVerb(
					arg,
					PackageValidateVerb,
					command,
					ToolCommand.PackageValidate,
					out var validating
				)
			)
			{
				command = validating;
				continue;
			}

			if (TryReadValue(args, ref index, ConfigFlags, out var config))
			{
				configPath = config;
				continue;
			}

			if (TryReadValue(args, ref index, ["--format"], out var formatValue))
			{
				format = string.Equals(formatValue, "json", StringComparison.OrdinalIgnoreCase)
					? OutputFormat.Json
					: OutputFormat.Text;
				continue;
			}

			if (string.Equals(arg, "--user-config", StringComparison.OrdinalIgnoreCase))
			{
				userConfig = true;
				continue;
			}

			remaining.Add(arg);
		}

		return new(command, configPath, userConfig, format, [.. remaining]);
	}

	/// <summary>
	/// Consumes one of the tool's own verbs, which is never a configuration override: recognising it
	/// here keeps the bare word away from the configuration command-line provider, which accepts only
	/// <c>--key=value</c> forms. An informational command (<c>--help</c>/<c>--version</c>) wins over
	/// any verb.
	/// </summary>
	static bool TryReadVerb(
		string arg,
		string verb,
		ToolCommand current,
		ToolCommand target,
		out ToolCommand command
	)
	{
		command = current;

		if (!string.Equals(arg, verb, StringComparison.OrdinalIgnoreCase))
			return false;

		if (current == ToolCommand.RunPipeline)
			command = target;

		return true;
	}

	/// <summary>
	/// Reads <c>--flag=value</c> or <c>--flag value</c>, advancing past the value in the latter case.
	/// </summary>
	static bool TryReadValue(
		string[] args,
		ref int index,
		IReadOnlyList<string> flags,
		out string? value
	)
	{
		var arg = args[index];
		value = null;

		foreach (var flag in flags)
		{
			if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Length)
					throw new InvalidOperationException($"'{flag}' requires a value.");

				value = args[++index];
				return true;
			}

			var inline = flag + "=";
			if (arg.StartsWith(inline, StringComparison.OrdinalIgnoreCase))
			{
				value = arg[inline.Length..];

				return string.IsNullOrWhiteSpace(value)
					? throw new InvalidOperationException($"'{flag}' requires a value.")
					: true;
			}
		}

		return false;
	}
}
