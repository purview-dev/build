using NuGet.Versioning;
using Purview.Build.Release;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// Pins the pre-change release decision so the extracted evaluator cannot alter it.
/// </summary>
/// <remarks>
/// Before this change the decision was made entirely in YAML (<c>purview-release.yml</c>, step
/// "Check for version bump"): read <c>package.json</c>'s <c>version</c>, and skip the release when
/// <c>v{version}</c> already resolves as a git ref, otherwise release with <c>Release__Mode=NuGet</c>.
/// The version itself was validated only by <c>VersionModule</c>'s <see cref="NuGetVersion.TryParse"/>,
/// which accepts four-part ("legacy") versions — two live consumers depend on that (aspirec4 at
/// 13.5.3.10, build-sdk at 1.0.2.2). The evaluated ref was never consulted by the pipeline; the
/// caller's <c>on:</c> block was the only gate.
///
/// The expectations below are therefore the OLD behaviour, transcribed, not the new rules' output.
/// The <c>ReleaseOnMain</c> policy must reproduce every row.
/// </remarks>
public class ReleaseOnMainCharacterisationTests
{
	static readonly string[] ReleaseOnMainRules = ["REL001", "REL002", "REL003"];

	// The release design's sample JSON shows StableRefs as ["refs/heads/main"] only. That would
	// reject the documented main-as-head model, whose release head is `release` and which configures
	// no eligibility section to opt out with, so the shipped policy lists both heads.
	static readonly string[] ReleaseOnMainStableRefs = ["refs/heads/main", "refs/heads/release"];

	// Model A: every consuming repository releases from refs/heads/main.
	const string ModelARef = "refs/heads/main";

	// Model B: documented main-as-head/release-branch model. No repository uses it today, so the
	// expectation is the documented behaviour, which is identical bar the ref.
	const string ModelBRef = "refs/heads/release";

	static EligibilityDecision Decide(string version, string gitRef, params string[] existingTags) =>
		EligibilityEvaluator.Evaluate(
			EligibilityInput.ForCharacterisation(version, gitRef, existingTags),
			EligibilityPolicies.Resolve(EligibilitySettings.Default, EligibilityPolicies.ReleaseOnMainName)
		);

	[Test]
	[Arguments(ModelARef)]
	[Arguments(ModelBRef)]
	public async Task Evaluate_GivenUntaggedStableVersion_Releases(string gitRef)
	{
		// Arrange
		// Model A/B with a bumped version and no matching tag: the old YAML set should_release=true.

		// Act
		var decision = Decide("1.0.0", gitRef);

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Release);
		await Assert.That(decision.ExitCode).IsEqualTo(0);
	}

	[Test]
	[Arguments(ModelARef)]
	[Arguments(ModelBRef)]
	public async Task Evaluate_GivenAlreadyTaggedVersion_SkipsWithExitCodeZero(string gitRef)
	{
		// Arrange
		// The old YAML's `git rev-parse "$TAG"` succeeded, printed "already tagged. Skipping
		// release." and left the job green.

		// Act
		var decision = Decide("1.0.0", gitRef, "v1.0.0");

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Skip);
		await Assert.That(decision.ExitCode).IsEqualTo(0);
		await Assert.That(decision.RuleId).IsEqualTo("REL002");
	}

	[Test]
	public async Task Evaluate_GivenUnrelatedTags_Releases()
	{
		// Arrange
		// Only an exact v{version} match gated the old release.

		// Act
		var decision = Decide("0.3.7", ModelARef, "v0.3.5", "v0.3.6", "v0.2.1");

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Release);
	}

	[Test]
	public async Task Evaluate_GivenPrereleaseVersionOnMain_Releases()
	{
		// Arrange
		// sourcegenerator-framework (1.0.0-prerelease.54), value-objects and zodsharp all ship
		// prereleases from main. REL003 constrains stable versions only, so these must still pass.

		// Act
		var decision = Decide("1.0.0-prerelease.54", ModelARef);

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Release);
	}

	[Test]
	[Arguments("13.5.3.10")]
	[Arguments("1.0.2.2")]
	public async Task Evaluate_GivenFourPartVersionFromLiveConsumer_Releases(string version)
	{
		// Arrange
		// aspirec4 and build-sdk ship four-part versions today: NuGetVersion.TryParse accepts them
		// and REL006 is not in ReleaseOnMain, so they must keep releasing unchanged.

		// Act
		var decision = Decide(version, ModelARef);

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Release);
	}

	[Test]
	public async Task Evaluate_GivenNonZeroPatchOnMain_Releases()
	{
		// Arrange
		// REL004 (servicing refs) is NOT enabled in ReleaseOnMain, so a patch release from main —
		// which is how every repository ships fixes today — must not be blocked.

		// Act
		var decision = Decide("5.0.1", ModelARef);

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Release);
	}

	[Test]
	public async Task Evaluate_GivenVersionLowerThanAnExistingTag_Releases()
	{
		// Arrange
		// REL005 (monotonic versions) is NOT enabled in ReleaseOnMain. The old YAML only ever
		// compared for tag existence, so a regression released.

		// Act
		var decision = Decide("1.0.0", ModelARef, "v2.0.0");

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Release);
	}

	[Test]
	public async Task Evaluate_GivenUnparseableVersion_FailsWithExitCodeOne()
	{
		// Arrange
		// VersionModule threw "The version 'x' in package.json is not a valid SemVer." and the tool
		// exited 1.

		// Act
		var decision = Decide("not-a-version", ModelARef);

		// Assert
		await Assert.That(decision.Verdict).IsEqualTo(EligibilityVerdict.Fail);
		await Assert.That(decision.ExitCode).IsEqualTo(1);
		await Assert.That(decision.RuleId).IsEqualTo("REL001");
	}

	[Test]
	public async Task ReleaseOnMain_EnablesExactlyTheRulesThatReproduceTodaysBehaviour()
	{
		// Arrange
		var policy = EligibilityPolicies.Resolve(EligibilitySettings.Default, EligibilityPolicies.ReleaseOnMainName);

		// Act
		var ruleIds = policy.RuleIds;

		// Assert
		await Assert.That(ruleIds).IsEquivalentTo(ReleaseOnMainRules);
		await Assert.That(policy.StableRefs).IsEquivalentTo(ReleaseOnMainStableRefs);
	}

	[Test]
	public async Task ReleaseOnMain_IsTheDefaultPolicyWhenEligibilityIsAbsent()
	{
		// Arrange
		ReleaseSettings settings = new();

		// Act
		var policyName = settings.Eligibility.Policy;

		// Assert
		await Assert.That(policyName).IsEqualTo(EligibilityPolicies.ReleaseOnMainName);
	}
}
