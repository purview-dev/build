using System.Text;
using System.Text.RegularExpressions;

namespace Purview.Build.Helpers;

/// <summary>
/// Renders failures the way a CLI build tool reports them: the actual error, never a .NET stack trace.
/// </summary>
static partial class FailureReport
{
	/// <summary>
	/// Flattens an exception into its message chain (outermost first), skipping blank and repeated messages.
	/// </summary>
	public static IReadOnlyList<string> Describe(Exception exception)
	{
		List<string> messages = [];

		for (var current = (Exception?)exception; current is not null; current = current.InnerException)
		{
			if (
				!string.IsNullOrWhiteSpace(current.Message)
				&& !messages.Contains(current.Message, StringComparer.Ordinal)
			)
				messages.Add(current.Message);
		}

		return messages;
	}

	/// <summary>
	/// Formats a failure: a heading, the details indented beneath it, and - only when
	/// <c>PURVIEW_BUILD_STACKTRACE</c> asks for them - stack traces.
	/// </summary>
	public static string Format(string summary, Exception exception)
	{
		var messages = Describe(exception);

		return TryDescribeModuleFailure(messages, out var moduleName, out var detailLines)
			? Render($"{summary}: {moduleName}", detailLines, exception, inlineFirstLine: false)
			: Render(summary, SplitLines(messages), exception, inlineFirstLine: true);
	}

	/// <summary>
	/// Recognizes the pipeline's module-failure wrapper ("The module {Module} has failed." plus the failing
	/// command's output) and reports the module by name with its output beneath it.
	/// </summary>
	static bool TryDescribeModuleFailure(
		IReadOnlyList<string> messages,
		out string moduleName,
		out IReadOnlyList<string> detailLines
	)
	{
		moduleName = string.Empty;
		detailLines = [];

		if (messages.Count == 0)
			return false;

		var match = ModuleFailurePattern().Match(messages[0]);

		if (!match.Success)
			return false;

		moduleName = match.Groups["module"].Value;
		detailLines = SplitLines([messages[0][match.Length..], .. messages.Skip(1)]);

		return true;
	}

	static string Render(string heading, IReadOnlyList<string> lines, Exception exception, bool inlineFirstLine)
	{
		StringBuilder builder = new(heading);

		foreach (var (line, index) in lines.Select(static (line, index) => (line, index)))
		{
			if (inlineFirstLine && index == 0)
				builder.Append(": ").Append(line);
			else
				builder.AppendLine().Append("  ").Append(line);
		}

		if (Debugging.StackTraceRequested)
			builder.AppendLine().Append(exception);

		return builder.ToString();
	}

	/// <summary>
	/// Splits messages into trimmed, non-empty lines, dropping lines that a previous message already
	/// contributed (nested pipeline exceptions repeat the failing command's output).
	/// </summary>
	static string[] SplitLines(IEnumerable<string> messages)
	{
		List<string> lines = [];

		var candidates = messages.SelectMany(static message =>
			message.Split('\n').Select(static line => line.TrimEnd('\r').TrimEnd())
		);

		foreach (var line in candidates)
		{
			if (line.Length > 0 && !lines.Contains(line, StringComparer.Ordinal))
				lines.Add(line);
		}

		return [.. lines];
	}

	[GeneratedRegex(@"^The module (?<module>\S+) has failed\.")]
	private static partial Regex ModuleFailurePattern();
}