using ModularPipelines.Context;

namespace Purview.Build.Helpers;

/// <summary>
/// Emits the per-module progress lines a CLI build tool is expected to show.
/// </summary>
/// <remarks>
/// The pipeline only logs a module when it completes, so a long-running step stays silent until it finishes,
/// which reads as "nothing is happening" in CI job logs. Modules log this line as they start, and the
/// framework logs the completion and duration.
/// </remarks>
static class ModuleProgress
{
	public static void Starting(IModuleContext context, string moduleName) =>
		context.Logger.LogInformation("Running {Module}...", moduleName);
}

/// <summary>
/// Switches for diagnosing the tool itself, read from environment variables so they can be enabled in CI
/// without changing repository configuration.
/// </summary>
static class Debugging
{
	const string StackTraceVariable = "PURVIEW_BUILD_STACKTRACE";

	/// <summary>
	/// True when <c>PURVIEW_BUILD_STACKTRACE</c> is set to <c>1</c> or <c>true</c>.
	/// </summary>
	public static bool StackTraceRequested { get; } = IsEnabled(StackTraceVariable);

	static bool IsEnabled(string variable)
	{
		var value = Environment.GetEnvironmentVariable(variable);

		return !string.IsNullOrWhiteSpace(value)
			&& (value.Trim() == "1" || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
	}
}