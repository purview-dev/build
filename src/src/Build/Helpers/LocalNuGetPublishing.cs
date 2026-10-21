namespace Purview.Build.Helpers;

/// <summary>
/// Whether a run publishes packages to the local feed.
/// </summary>
/// <remarks>
/// <c>ConfigurationPreflight</c> and <see cref="PublishLocalNuGetModule"/> both have to answer this,
/// and they must agree: preflight only demands a local feed path when the module is going to use one.
/// They previously evaluated the same three clauses separately, against <em>different</em> locality
/// checks — preflight against <see cref="ExecutionEnvironment"/>, the module against ModularPipelines'
/// own build-system detector, which does not treat <c>CI</c> as a build agent. Where the two
/// disagreed, preflight waived the feed path and the module then ran without it, turning a fast
/// configuration failure into a <c>ModuleFailedException</c> after clean, restore, build, test and
/// pack had already run.
/// <para>
/// Locality is a parameter rather than a call into <see cref="ExecutionEnvironment"/>, because
/// preflight runs before a pipeline context exists and its tests inject the value.
/// </para>
/// </remarks>
static class LocalNuGetPublishing
{
	/// <summary>
	/// The reason the local publish will not happen, or <see langword="null"/> when it will.
	/// </summary>
	/// <remarks>
	/// Naming the clause that actually applies matters: all three produce the same outcome, and a
	/// developer already running locally with <c>Release__Mode=LocalNuGet</c> cannot act on a message
	/// that tells them to do what they just did.
	/// </remarks>
	public static string? DisabledReason(
		ProjectType projectType,
		ReleaseMode mode,
		bool isRunningLocally
	)
	{
		if (projectType == ProjectType.Web)
			return "Local NuGet feed publishing does not apply to a Web project, which produces no packages.";

		if (!isRunningLocally)
		{
			var detected = ExecutionEnvironment.DetectedBuildAgentVariable();

			return "Local NuGet feed publishing is ignored when not running locally"
				+ (detected is null ? "." : $" ({detected} is set). Unset it to publish to the local feed.");
		}

		if (mode != ReleaseMode.LocalNuGet)
			return $"Local NuGet feed publishing is disabled. Release:Mode is {mode}; set it to LocalNuGet to enable it.";

		return null;
	}

	/// <summary>
	/// True when the run will publish to the local feed, and so requires a local feed path.
	/// </summary>
	public static bool IsEnabled(ProjectType projectType, ReleaseMode mode, bool isRunningLocally) =>
		DisabledReason(projectType, mode, isRunningLocally) is null;
}
