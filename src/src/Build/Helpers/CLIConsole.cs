using Spectre.Console;

namespace Purview.Build.Helpers;

/// <summary>
/// Writes the tool's own CLI output.
/// </summary>
/// <remarks>
/// The pipeline's analyzers forbid direct <see cref="Console"/> use (module and step output belongs to the
/// pipeline logger), so CLI-boundary output - the version banner, <c>--help</c>, and failure reports - goes
/// through the same console abstraction the pipeline itself renders with.
///
/// Every write lifts the console profile width first. <see cref="AnsiConsole"/> hard-wraps at that
/// width, which is 80 columns whenever stdout is redirected - exactly the case when a workflow pipes
/// output into <c>jq</c> or greps a failure message. Wrapping inserts newlines inside JSON string
/// values and splits diagnostics mid-sentence, so the tool formats its own lines and the console
/// must not reflow them.
/// </remarks>
static class CLIConsole
{
	public static void WriteLine(string text) => WriteUnwrapped(text);

	public static void WriteLine() => AnsiConsole.WriteLine();

	/// <summary>
	/// Writes machine-readable output verbatim.
	/// </summary>
	public static void WriteRaw(string text) => WriteUnwrapped(text);

	static void WriteUnwrapped(string text)
	{
		var profile = AnsiConsole.Console.Profile;
		var previousWidth = profile.Width;

		try
		{
			profile.Width = int.MaxValue;
			AnsiConsole.WriteLine(text);
		}
		finally
		{
			profile.Width = previousWidth;
		}
	}
}
