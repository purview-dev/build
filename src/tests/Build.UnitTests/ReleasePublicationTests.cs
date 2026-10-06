using NuGet.Versioning;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// The publication split: <c>Release:Mode</c> as a preset, with explicitly set
/// <c>Release:Publish</c>/<c>Release:GitHubRelease</c> and channel settings overriding it.
/// </summary>
public class ReleasePublicationTests
{
	[Test]
	[Arguments(ReleaseMode.None, false, false)]
	[Arguments(ReleaseMode.NuGet, true, true)]
	[Arguments(ReleaseMode.GitHubRelease, false, true)]
	[Arguments(ReleaseMode.LocalNuGet, false, false)]
	public async Task Mode_GivenNoExplicitSwitches_DerivesTodaysBehaviour(
		ReleaseMode mode,
		bool expectedPublish,
		bool expectedGitHubRelease
	)
	{
		// Arrange
		// These four rows are the pre-change skip conditions of PublishNuGetModule and
		// CreateGitHubReleaseModule, so the preset must keep deriving exactly them.
		ReleaseSettings settings = new() { Mode = mode };

		// Act
		var publish = settings.ShouldPublish();
		var githubRelease = settings.ShouldCreateGitHubRelease();

		// Assert
		await Assert.That(publish).IsEqualTo(expectedPublish);
		await Assert.That(githubRelease).IsEqualTo(expectedGitHubRelease);
	}

	[Test]
	public async Task Publish_GivenExplicitTrueAndModeNone_WinsOverThePreset()
	{
		// Arrange
		ReleaseSettings settings = new() { Mode = ReleaseMode.None, Publish = true };

		// Act
		var publish = settings.ShouldPublish();

		// Assert
		await Assert.That(publish).IsTrue();
	}

	[Test]
	public async Task Publish_GivenExplicitFalseAndModeNuGet_WinsOverThePreset()
	{
		// Arrange
		ReleaseSettings settings = new() { Mode = ReleaseMode.NuGet, Publish = false };

		// Act
		var publish = settings.ShouldPublish();

		// Assert
		await Assert.That(publish).IsFalse();
		await Assert.That(settings.ShouldCreateGitHubRelease()).IsTrue().Because("the two switches are independent.");
	}

	[Test]
	public async Task GitHubRelease_GivenExplicitFalseAndModeNuGet_PublishesWithoutReleasing()
	{
		// Arrange
		ReleaseSettings settings = new() { Mode = ReleaseMode.NuGet, GitHubRelease = false };

		// Act
		var githubRelease = settings.ShouldCreateGitHubRelease();

		// Assert
		await Assert.That(githubRelease).IsFalse();
		await Assert.That(settings.ShouldPublish()).IsTrue();
	}

	[Test]
	public async Task GitHubRelease_GivenChannelOptOut_SuppressesTheRelease()
	{
		// Arrange
		// A preview channel pointing at a different feed with GitHubRelease: false makes
		// dispatch-from-any-branch preview packages possible without cutting a GitHub release.
		ReleaseSettings settings = new()
		{
			Mode = ReleaseMode.NuGet,
			Channel = "preview",
			Channels = new()
			{
				["preview"] = new()
				{
					FeedUrl = "https://nuget.pkg.github.com/purview-dev/index.json",
					GitHubRelease = false,
				},
			},
		};

		// Act
		var githubRelease = settings.ShouldCreateGitHubRelease();

		// Assert
		await Assert.That(githubRelease).IsFalse();
		await Assert.That(settings.ShouldPublish()).IsTrue();
		await Assert
			.That(settings.ResolveChannel().FeedUrl)
			.IsEqualTo("https://nuget.pkg.github.com/purview-dev/index.json");
	}

	[Test]
	public async Task GitHubRelease_GivenExplicitKeyAndChannelOptOut_PrefersTheExplicitKey()
	{
		// Arrange
		// Explicitly set keys always win, including over a channel's own setting.
		ReleaseSettings settings = new()
		{
			Mode = ReleaseMode.None,
			GitHubRelease = true,
			Channel = "preview",
			Channels = new() { ["preview"] = new() { GitHubRelease = false } },
		};

		// Act
		var githubRelease = settings.ShouldCreateGitHubRelease();

		// Assert
		await Assert.That(githubRelease).IsTrue();
	}

	[Test]
	public async Task ResolveChannel_GivenAnUndeclaredChannel_FallsBackToStableDefaults()
	{
		// Arrange
		ReleaseSettings settings = new() { Channel = "does-not-exist" };

		// Act
		var channel = settings.ResolveChannel();

		// Assert
		await Assert.That(channel.FeedUrl).IsNull();
		await Assert.That(channel.GitHubRelease).IsNull();
		await Assert.That(channel.MarkPrerelease).IsNull();
	}

	[Test]
	public async Task ShouldMarkPrerelease_GivenChannelOverride_PrefersTheChannel()
	{
		// Arrange
		ReleaseSettings settings = new()
		{
			MarkPrerelease = true,
			Channel = "preview",
			Channels = new() { ["preview"] = new() { MarkPrerelease = false } },
		};

		// Act
		var marked = settings.ShouldMarkPrerelease(NuGetVersion.Parse("2.1.0-prerelease.1"));

		// Assert
		await Assert.That(marked).IsFalse();
	}

	[Test]
	public async Task ValidateSimulationInterlock_GivenSimulatedContextAndRealPublish_Fails()
	{
		// Arrange
		// Simulated tags selecting which versions are "already released" while really pushing
		// packages is the one combination that can do damage from a developer machine.
		ReleaseSettings settings = new()
		{
			Mode = ReleaseMode.NuGet,
			Context = new() { Ref = "refs/heads/release/2.0" },
		};

		// Act
		var act = settings.ValidateSimulationInterlock;

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("simulated release context");
		await Assert.That(exception!.Message).Contains("release-explain");
	}

	[Test]
	public async Task ValidateSimulationInterlock_GivenSimulatedContextAndLocalNuGet_Allows()
	{
		// Arrange
		// LocalNuGet never touches a remote feed, so simulating against it is safe and useful.
		ReleaseSettings settings = new()
		{
			Mode = ReleaseMode.LocalNuGet,
			Context = new() { Ref = "refs/heads/release/2.0" },
		};

		// Act
		settings.ValidateSimulationInterlock();

		// Assert
		await Assert.That(settings.ShouldPublish()).IsFalse();
	}

	[Test]
	public async Task ValidateSimulationInterlock_GivenNoSimulatedContext_Allows()
	{
		// Arrange
		ReleaseSettings settings = new() { Mode = ReleaseMode.NuGet };

		// Act
		settings.ValidateSimulationInterlock();

		// Assert
		await Assert.That(settings.Context.IsSimulated).IsFalse();
	}

	[Test]
	public async Task DryRun_GivenDefaults_IsOff()
	{
		// Arrange
		ReleaseSettings settings = new();

		// Act
		var dryRun = settings.DryRun;

		// Assert
		await Assert.That(dryRun).IsFalse();
	}

	[Test]
	public async Task Eligibility_GivenDefaults_SelectsReleaseOnMain()
	{
		// Arrange
		ReleaseSettings settings = new();

		// Act
		var policy = settings.Eligibility.Policy;

		// Assert
		await Assert.That(policy).IsEqualTo("ReleaseOnMain");
		await Assert.That(settings.Channel).IsEqualTo("stable");
	}
}
