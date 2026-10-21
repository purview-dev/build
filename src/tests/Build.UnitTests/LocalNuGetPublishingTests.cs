using Purview.Build.Helpers;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// The single predicate behind "will this run publish to the local feed?".
/// </summary>
/// <remarks>
/// <c>ConfigurationPreflight</c> demands a local feed path exactly when
/// <c>PublishLocalNuGetModule</c> is going to use one. While each evaluated the three clauses for
/// itself, against different locality checks, they could disagree — and a disagreement is silent
/// until the module fails late, after the whole build has run.
/// </remarks>
public class LocalNuGetPublishingTests
{
	[Test]
	public async Task GivenLocalNuGetRunningLocally_IsEnabled()
	{
		// Act
		var reason = LocalNuGetPublishing.DisabledReason(
			ProjectType.DotNet,
			ReleaseMode.LocalNuGet,
			isRunningLocally: true
		);

		// Assert
		await Assert.That(reason).IsNull();
	}

	[Test]
	[Arguments(ReleaseMode.None)]
	[Arguments(ReleaseMode.NuGet)]
	[Arguments(ReleaseMode.GitHubRelease)]
	public async Task GivenAnotherMode_NamesTheModeThatIsSet(ReleaseMode mode)
	{
		// Act
		var reason = LocalNuGetPublishing.DisabledReason(ProjectType.DotNet, mode, isRunningLocally: true);

		// Assert
		await Assert
			.That(reason)
			.IsNotNull()
			.And.Contains(mode.ToString())
			.Because("all three clauses produce the same skip, so the message has to say which one applied.");
	}

	[Test]
	public async Task GivenABuildAgent_IsDisabledWhateverTheMode()
	{
		// Act
		var reason = LocalNuGetPublishing.DisabledReason(
			ProjectType.DotNet,
			ReleaseMode.LocalNuGet,
			isRunningLocally: false
		);

		// Assert
		await Assert.That(reason).IsNotNull().And.Contains("not running locally");
	}

	[Test]
	public async Task GivenAWebProject_IsDisabled()
	{
		// Act
		var reason = LocalNuGetPublishing.DisabledReason(
			ProjectType.Web,
			ReleaseMode.LocalNuGet,
			isRunningLocally: true
		);

		// Assert
		await Assert.That(reason).IsNotNull().And.Contains("Web project");
	}

	/// <summary>
	/// The clauses, in the order the predicate reports them. A Web project on a build agent is
	/// disabled for both reasons; which one is named does not matter, that it is disabled does.
	/// </summary>
	public static IEnumerable<Func<(ProjectType, ReleaseMode, bool)>> Combinations()
	{
		foreach (var projectType in Enum.GetValues<ProjectType>())
		{
			foreach (var mode in Enum.GetValues<ReleaseMode>())
			{
				foreach (var isRunningLocally in new[] { true, false })
				{
					var combination = (projectType, mode, isRunningLocally);

					yield return () => combination;
				}
			}
		}
	}

	[Test]
	[MethodDataSource(nameof(Combinations))]
	public async Task IsEnabled_AgreesWithDisabledReason_ForEveryCombination(
		ProjectType projectType,
		ReleaseMode mode,
		bool isRunningLocally
	)
	{
		// Act
		var enabled = LocalNuGetPublishing.IsEnabled(projectType, mode, isRunningLocally);
		var reason = LocalNuGetPublishing.DisabledReason(projectType, mode, isRunningLocally);

		// Assert
		await Assert
			.That(enabled)
			.IsEqualTo(reason is null)
			.Because(
				"preflight gates on IsEnabled and the module gates on DisabledReason. If they ever "
					+ "disagreed, preflight would waive the feed path for a run that goes on to need it."
			);

		// The only combination that publishes.
		var expected = projectType != ProjectType.Web && isRunningLocally && mode == ReleaseMode.LocalNuGet;

		await Assert.That(enabled).IsEqualTo(expected);
	}
}
