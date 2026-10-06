namespace Purview.Build.Helpers;

/// <summary>
/// Whether the tool is running on a developer machine or on a build agent, decided before any
/// pipeline context exists.
/// </summary>
/// <remarks>
/// Modules use <c>ctx.IsRunningLocally()</c>, but configuration is composed before a context exists
/// and ModularPipelines' concrete build-system detector is internal to that assembly, so the
/// locality check that gates machine-local configuration has to live here. The variables below are
/// the ones the known build systems set; an empty value counts as unset, because a workflow that
/// forwards an undefined variable sets it to the empty string.
/// </remarks>
static class ExecutionEnvironment
{
	/// <summary>
	/// The variables that mark a build agent. Exposed so tests emulating a local run neutralise
	/// every one of them rather than only the variable they happen to know about.
	/// </summary>
	public static IReadOnlyList<string> BuildAgentVariables { get; } =
	[
		// Generic, set by most hosted CI including GitHub Actions.
		"CI",
		"GITHUB_ACTIONS",
		"TF_BUILD",
		"TEAMCITY_VERSION",
		"JENKINS_URL",
		"JENKINS_HOME",
		"GITLAB_CI",
		"BITBUCKET_BUILD_NUMBER",
		"TRAVIS",
		"APPVEYOR",
	];

	/// <summary>
	/// True when no known build-agent variable is set.
	/// </summary>
	public static bool IsRunningLocally() => !IsRunningOnBuildAgent();

	public static bool IsRunningOnBuildAgent() =>
		BuildAgentVariables.Any(variable =>
			!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
		);

	/// <summary>
	/// Names the detected build agent variable, for diagnostics. Null when running locally.
	/// </summary>
	public static string? DetectedBuildAgentVariable() =>
		BuildAgentVariables.FirstOrDefault(variable =>
			!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
		);
}
