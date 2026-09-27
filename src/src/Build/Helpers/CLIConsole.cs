using Spectre.Console;

namespace Purview.Build.Helpers;

/// <summary>
/// Writes the tool's own CLI output.
/// </summary>
/// <remarks>
/// The pipeline's analyzers forbid direct <see cref="Console"/> use (module and step output belongs to the
/// pipeline logger), so CLI-boundary output - the version banner, <c>--help</c>, and failure reports - goes
/// through the same console abstraction the pipeline itself renders with.
/// </remarks>
static class CLIConsole
{
	public static void WriteLine(string text) => AnsiConsole.WriteLine(text);

	public static void WriteLine() => AnsiConsole.WriteLine();
}