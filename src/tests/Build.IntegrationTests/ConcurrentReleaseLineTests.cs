using NuGet.Versioning;
using Purview.Build.Infra;
using Purview.Build.Release;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// The concurrent-line scenario Model C exists for: a serviced stable line and an in-flight
/// prerelease line releasing independently, neither affecting the other's verdict.
/// </summary>
[NotInParallel]
public class ConcurrentReleaseLineTests
{
	// Both lines see the same tag history; only the ref and the version differ.
	static readonly string[] SharedTags = ["v2.0.0", "v2.0.1", "v2.1.0-prerelease.1"];

	static EligibilityDecision Evaluate(GitFixture fixture, string gitRef, string version)
	{
		var tagsPath = fixture.WriteList("tags.txt", SharedTags);

		ReleaseContextSettings contextSettings = new() { Ref = gitRef, ExistingTags = tagsPath };
		var context = fixture.InWorkingDirectory(() => ReleaseContextProvider.Resolve(contextSettings, fixture.Root));

		var policy = EligibilityPolicies.Resolve(
			EligibilitySettings.Default,
			EligibilityPolicies.TrunkReservesMinorName
		);

		var input = EligibilityInput.Create(
			version,
			VersionStrictness.NuGet,
			context,
			ReleaseChannelSettings.StableChannelName,
			ReleaseChannelSettings.Stable,
			nameof(VersionSource.PackageJson)
		);

		return EligibilityEvaluator.Evaluate(input, policy);
	}

	[Test]
	public async Task Evaluate_GivenServicedStableLine_IsEligible()
	{
		// Arrange
		using var fixture = GitFixture.Create("2.0.2", branch: "release/2.0", tags: SharedTags);

		// Act
		var decision = Evaluate(fixture, "refs/heads/release/2.0", "2.0.2");

		// Assert
		await Assert
			.That(decision.Verdict)
			.IsEqualTo(EligibilityVerdict.Release)
			.Because(string.Join(Environment.NewLine, decision.Results.Select(result => result.Message)));
	}

	[Test]
	public async Task Evaluate_GivenInFlightPrereleaseLine_IsEligible()
	{
		// Arrange
		using var fixture = GitFixture.Create("2.1.0-prerelease.2", branch: "release/2.1", tags: SharedTags);

		// Act
		var decision = Evaluate(fixture, "refs/heads/release/2.1", "2.1.0-prerelease.2");

		// Assert
		await Assert
			.That(decision.Verdict)
			.IsEqualTo(EligibilityVerdict.Release)
			.Because(string.Join(Environment.NewLine, decision.Results.Select(result => result.Message)));
	}

	[Test]
	public async Task Evaluate_GivenBothLines_NeitherAffectsTheOthersVerdict()
	{
		// Arrange
		// REL005 is scoped to the line, so the 2.1 prerelease ahead of 2.0.2 must not block the
		// 2.0 patch, and the serviced 2.0 line must not block 2.1 advancing.
		using var fixture = GitFixture.Create("2.0.2", branch: "release/2.0", tags: SharedTags);

		// Act
		var stable = Evaluate(fixture, "refs/heads/release/2.0", "2.0.2");
		var prerelease = Evaluate(fixture, "refs/heads/release/2.1", "2.1.0-prerelease.2");

		// Assert
		await Assert.That(stable.Verdict).IsEqualTo(EligibilityVerdict.Release);
		await Assert.That(prerelease.Verdict).IsEqualTo(EligibilityVerdict.Release);
	}

	[Test]
	public async Task ReleaseUnits_GivenBothLines_CarryDistinctLinesAndTags()
	{
		// Arrange
		using var fixture = GitFixture.Create("2.0.2", branch: "release/2.0", tags: SharedTags);

		// Act
		var stable = ReleaseUnitFactory.TryCreate(
			"2.0.2",
			ReleaseChannelSettings.StableChannelName,
			nameof(VersionSource.PackageJson)
		)!;
		var prerelease = ReleaseUnitFactory.TryCreate(
			"2.1.0-prerelease.2",
			ReleaseChannelSettings.StableChannelName,
			nameof(VersionSource.PackageJson)
		)!;

		// Assert
		await Assert.That(stable.Line).IsEqualTo("2.0");
		await Assert.That(prerelease.Line).IsEqualTo("2.1");
		await Assert.That(stable.Tag).IsEqualTo("v2.0.2");
		await Assert.That(prerelease.Tag).IsEqualTo("v2.1.0-prerelease.2");
		await Assert.That(fixture.Root).IsNotEmpty();
	}

	[Test]
	public async Task ShouldMarkPrerelease_GivenBothLines_MarksOnlyThePrerelease()
	{
		// Arrange
		// The prerelease must not be presented as the latest stable release on GitHub, while the
		// serviced stable release must be.
		ReleaseSettings settings = new() { Mode = ReleaseMode.NuGet };

		// Act
		var stableMarked = settings.ShouldMarkPrerelease(NuGetVersion.Parse("2.0.2"));
		var prereleaseMarked = settings.ShouldMarkPrerelease(NuGetVersion.Parse("2.1.0-prerelease.2"));

		// Assert
		await Assert.That(stableMarked).IsFalse();
		await Assert.That(prereleaseMarked).IsTrue();
	}
}
