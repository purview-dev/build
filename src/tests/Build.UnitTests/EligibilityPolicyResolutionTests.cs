using Purview.Build.Release;
using Purview.Build.Settings;

namespace Purview.Build;

public class EligibilityPolicyResolutionTests
{
	static readonly string[] TrunkReservesMinorRules = ["REL001", "REL002", "REL003", "REL004", "REL005"];

	static readonly string[] FourPartServicingRules = ["REL001", "REL002", "REL003", "REL004", "REL005", "REL006"];

	static readonly string[] ParseAndTagRulesOnly = ["REL001", "REL002"];

	static readonly string[] ParseRuleOnly = ["REL001"];

	static readonly string[] NarrowedStableRefs = ["refs/heads/release/2.0"];

	static readonly string[] InheritedServicingRefs = ["refs/heads/release/*"];

	static readonly string[] OverriddenStableRefs = ["refs/heads/trunk"];

	static ResolvedPolicy Resolve(string name, params (string Name, EligibilityPolicySettings Policy)[] policies)
	{
		EligibilitySettings settings = new()
		{
			Policy = name,
			Policies = policies.ToDictionary(entry => entry.Name, entry => entry.Policy),
		};

		return EligibilityPolicies.Resolve(settings, name);
	}

	[Test]
	public async Task Resolve_GivenBuiltInTrunkReservesMinor_EnablesTheDocumentedRules()
	{
		// Arrange
		// TrunkReservesMinor ships built in so adopting Model C is a policy name, not a config block.

		// Act
		var policy = EligibilityPolicies.Resolve(
			EligibilitySettings.Default,
			EligibilityPolicies.TrunkReservesMinorName
		);

		// Assert
		await Assert.That(policy.RuleIds).IsEquivalentTo(TrunkReservesMinorRules);
		await Assert.That(policy.TrunkRefs).Contains("refs/heads/main");
	}

	[Test]
	public async Task Resolve_GivenInheritsWithAdditiveDelta_AddsToTheParentRules()
	{
		// Arrange
		// FourPartServicing is defined as "TrunkReservesMinor plus REL006".

		// Act
		var policy = EligibilityPolicies.Resolve(
			EligibilitySettings.Default,
			EligibilityPolicies.FourPartServicingName
		);

		// Assert
		await Assert.That(policy.RuleIds).IsEquivalentTo(FourPartServicingRules);
		await Assert.That(policy.AllowFourPart).IsTrue();
		await Assert
			.That(policy.InheritanceChain)
			.IsEquivalentTo(
				new[] { EligibilityPolicies.FourPartServicingName, EligibilityPolicies.TrunkReservesMinorName }
			);
	}

	[Test]
	public async Task Resolve_GivenInheritsWithSubtractiveDelta_RemovesTheParentRule()
	{
		// Arrange
		EligibilityPolicySettings relaxed = new()
		{
			Inherits = EligibilityPolicies.TrunkReservesMinorName,
			Rules = ["-REL005"],
		};

		// Act
		var policy = Resolve("Relaxed", ("Relaxed", relaxed));

		// Assert
		await Assert.That(policy.Enables("REL005")).IsFalse();
		await Assert.That(policy.Enables("REL004")).IsTrue();
	}

	[Test]
	public async Task Resolve_GivenInheritsWithNoRules_InheritsTheParentRulesUnchanged()
	{
		// Arrange
		EligibilityPolicySettings renamed = new() { Inherits = EligibilityPolicies.TrunkReservesMinorName };

		// Act
		var policy = Resolve("Renamed", ("Renamed", renamed));

		// Assert
		await Assert.That(policy.RuleIds).IsEquivalentTo(TrunkReservesMinorRules);
	}

	[Test]
	public async Task Resolve_GivenAbsoluteRuleList_ReplacesTheParentRules()
	{
		// Arrange
		EligibilityPolicySettings absolute = new()
		{
			Inherits = EligibilityPolicies.TrunkReservesMinorName,
			Rules = ["REL001", "REL002"],
		};

		// Act
		var policy = Resolve("Absolute", ("Absolute", absolute));

		// Assert
		await Assert.That(policy.RuleIds).IsEquivalentTo(ParseAndTagRulesOnly);
	}

	[Test]
	public async Task Resolve_GivenChildRefList_NarrowsRatherThanInherits()
	{
		// Arrange
		// A non-empty child list replaces the parent's, so a policy can narrow a ref set.
		EligibilityPolicySettings narrowed = new()
		{
			Inherits = EligibilityPolicies.TrunkReservesMinorName,
			StableRefs = ["refs/heads/release/2.0"],
		};

		// Act
		var policy = Resolve("Narrowed", ("Narrowed", narrowed));

		// Assert
		await Assert.That(policy.StableRefs).IsEquivalentTo(NarrowedStableRefs);
		await Assert.That(policy.ServicingRefs).IsEquivalentTo(InheritedServicingRefs);
	}

	[Test]
	public async Task Resolve_GivenMissingParent_FailsNamingTheUnresolvedPolicy()
	{
		// Arrange
		EligibilityPolicySettings orphan = new() { Inherits = "DoesNotExist", Rules = ["+REL006"] };

		// Act
		var act = () => Resolve("Orphan", ("Orphan", orphan));

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("DoesNotExist");
		await Assert.That(exception!.Message).Contains("Orphan");
	}

	[Test]
	public async Task Resolve_GivenUnknownPolicyName_FailsListingTheAvailablePolicies()
	{
		// Arrange

		// Act
		var act = () => EligibilityPolicies.Resolve(EligibilitySettings.Default, "Nonsense");

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("Nonsense");
		await Assert.That(exception!.Message).Contains(EligibilityPolicies.ReleaseOnMainName);
	}

	[Test]
	public async Task Resolve_GivenCyclicInheritance_FailsRatherThanRecursing()
	{
		// Arrange
		EligibilityPolicySettings left = new() { Inherits = "Right", Rules = ["+REL006"] };
		EligibilityPolicySettings right = new() { Inherits = "Left", Rules = ["+REL007"] };

		// Act
		var act = () => Resolve("Left", ("Left", left), ("Right", right));

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("inherits from itself");
	}

	[Test]
	public async Task Resolve_GivenMixedAbsoluteAndDeltaRules_FailsRatherThanGuessing()
	{
		// Arrange
		// The intended precedence of a mixed list is not obvious, so it is rejected outright.
		EligibilityPolicySettings mixed = new()
		{
			Inherits = EligibilityPolicies.TrunkReservesMinorName,
			Rules = ["REL001", "+REL006"],
		};

		// Act
		var act = () => Resolve("Mixed", ("Mixed", mixed));

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("mixes absolute rule entries");
	}

	[Test]
	public async Task Resolve_GivenUnknownRuleId_FailsListingTheKnownRules()
	{
		// Arrange
		EligibilityPolicySettings bogus = new() { Rules = ["REL999"] };

		// Act
		var act = () => Resolve("Bogus", ("Bogus", bogus));

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("REL999");
		await Assert.That(exception!.Message).Contains("REL001");
	}

	[Test]
	public async Task Resolve_GivenARepositoryPolicyNamedLikeABuiltIn_PrefersTheRepositoryPolicy()
	{
		// Arrange
		// A repository must be able to override a built-in policy by declaring the same name.
		EligibilityPolicySettings overridden = new() { Rules = ["REL001"], StableRefs = ["refs/heads/trunk"] };

		// Act
		var policy = Resolve(
			EligibilityPolicies.ReleaseOnMainName,
			(EligibilityPolicies.ReleaseOnMainName, overridden)
		);

		// Assert
		await Assert.That(policy.RuleIds).IsEquivalentTo(ParseRuleOnly);
		await Assert.That(policy.StableRefs).IsEquivalentTo(OverriddenStableRefs);
	}

	[Test]
	public async Task Resolve_GivenEmptyPolicyName_FailsWithActionableGuidance()
	{
		// Arrange
		EligibilitySettings settings = new() { Policy = "" };

		// Act
		var act = () => EligibilityPolicies.Resolve(settings, settings.Policy);

		// Assert
		var exception = await Assert.That(act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains(EligibilityPolicies.ReleaseOnMainName);
	}

	[Test]
	public async Task Resolve_GivenEveryBuiltInPolicy_OrdersRulesById()
	{
		// Arrange
		// Evaluation order is rule-ID order, so resolution must sort rather than preserve
		// declaration or delta-application order.
		string[] names =
		[
			EligibilityPolicies.ReleaseOnMainName,
			EligibilityPolicies.TrunkReservesMinorName,
			EligibilityPolicies.FourPartServicingName,
		];

		// Act
		var policies = names.Select(name => EligibilityPolicies.Resolve(EligibilitySettings.Default, name));

		// Assert
		foreach (var policy in policies)
		{
			await Assert.That(policy.RuleIds).IsEquivalentTo(policy.RuleIds.Order(StringComparer.Ordinal).ToList());
		}
	}
}
