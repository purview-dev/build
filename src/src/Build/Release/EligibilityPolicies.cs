namespace Purview.Build.Release;

/// <summary>
/// The built-in eligibility policies, and resolution of a configured policy name into a
/// <see cref="ResolvedPolicy"/>.
/// </summary>
/// <remarks>
/// The three policies named in the release design ship built in rather than having to be declared in
/// every repository, so adopting a different release model is a single
/// <c>Release:Eligibility:Policy</c> value plus the caller's <c>on:</c> block. A repository may add
/// its own policies, or override a built-in by declaring the same name.
/// </remarks>
static class EligibilityPolicies
{
	public const string ReleaseOnMainName = "ReleaseOnMain";

	public const string TrunkReservesMinorName = "TrunkReservesMinor";

	public const string FourPartServicingName = "FourPartServicing";

	/// <summary>
	/// Policies shipped with the tool.
	/// </summary>
	/// <remarks>
	/// <see cref="ReleaseOnMainName"/> is the default and reproduces the behaviour the pipeline had
	/// before eligibility rules existed: the version must parse, must not already be released, and a
	/// stable version comes from <c>main</c>. It deliberately omits REL004-REL007 so patch releases,
	/// version regressions and four-part versions keep behaving exactly as they do today.
	/// </remarks>
	public static IReadOnlyDictionary<string, EligibilityPolicySettings> BuiltIn { get; } =
		new Dictionary<string, EligibilityPolicySettings>(StringComparer.OrdinalIgnoreCase)
		{
			[ReleaseOnMainName] = new()
			{
				Rules = ["REL001", "REL002", "REL003"],
				// Both documented pre-eligibility release heads. Model A releases from main; the
				// main-as-head model merges main into `release` and releases from there. Listing only
				// main would make REL003 reject a Model B release, which no repository configures
				// its way out of because it configures no eligibility section at all.
				StableRefs = ["refs/heads/main", "refs/heads/release"],
				TrunkRefs = ["refs/heads/main"],
			},
			[TrunkReservesMinorName] = new()
			{
				Rules = ["REL001", "REL002", "REL003", "REL004", "REL005"],
				StableRefs = ["refs/heads/release/*"],
				ServicingRefs = ["refs/heads/release/*"],
				TrunkRefs = ["refs/heads/main"],
			},
			[FourPartServicingName] = new()
			{
				Inherits = TrunkReservesMinorName,
				Rules = ["+REL006"],
				AllowFourPart = true,
				FourPartRefs = ["refs/heads/release/*"],
			},
		};

	/// <summary>
	/// Resolves <paramref name="policyName"/> against the built-in policies merged with the
	/// repository's own.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The policy, or a policy it inherits from, does not exist; the inheritance chain is cyclic; or
	/// a rule list mixes absolute entries with <c>+</c>/<c>-</c> deltas.
	/// </exception>
	public static ResolvedPolicy Resolve(EligibilitySettings settings, string policyName)
	{
		ArgumentNullException.ThrowIfNull(settings);

		var all = Merge(settings.Policies);

		List<string> chain = [];
		var resolved = Resolve(all, policyName, chain);

		return resolved;
	}

	static Dictionary<string, EligibilityPolicySettings> Merge(
		Dictionary<string, EligibilityPolicySettings> configured
	)
	{
		Dictionary<string, EligibilityPolicySettings> all = new(BuiltIn, StringComparer.OrdinalIgnoreCase);

		foreach (var (name, policy) in configured)
			all[name] = policy;

		return all;
	}

	static ResolvedPolicy Resolve(
		Dictionary<string, EligibilityPolicySettings> all,
		string policyName,
		List<string> chain
	)
	{
		if (string.IsNullOrWhiteSpace(policyName))
			throw new InvalidOperationException(
				"Release:Eligibility:Policy is empty. Name a policy, or remove the key to use the default "
					+ $"'{ReleaseOnMainName}' policy."
			);

		if (chain.Contains(policyName, StringComparer.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				$"The eligibility policy '{policyName}' inherits from itself: "
					+ $"{string.Join(" -> ", chain)} -> {policyName}."
			);

		if (!all.TryGetValue(policyName, out var policy))
			throw new InvalidOperationException(
				$"The eligibility policy '{policyName}' is not defined. Available policies: "
					+ $"{string.Join(", ", all.Keys.Order(StringComparer.Ordinal))}."
			);

		chain.Add(policyName);

		ResolvedPolicy? parent = null;
		if (!string.IsNullOrWhiteSpace(policy.Inherits))
		{
			if (!all.ContainsKey(policy.Inherits))
				throw new InvalidOperationException(
					$"The eligibility policy '{policyName}' inherits from '{policy.Inherits}', which is not "
						+ $"defined. Available policies: {string.Join(", ", all.Keys.Order(StringComparer.Ordinal))}."
				);

			parent = Resolve(all, policy.Inherits, chain);
		}

		var ruleIds = ApplyRules(policyName, policy.Rules, parent?.RuleIds ?? []);

		return new ResolvedPolicy(
			Name: policyName,
			RuleIds: ruleIds,
			StableRefs: Inherit(policy.StableRefs, parent?.StableRefs),
			ServicingRefs: Inherit(policy.ServicingRefs, parent?.ServicingRefs),
			TrunkRefs: Inherit(policy.TrunkRefs, parent?.TrunkRefs),
			FourPartRefs: Inherit(policy.FourPartRefs, parent?.FourPartRefs),
			AllowFourPart: policy.AllowFourPart || (parent?.AllowFourPart ?? false),
			InheritanceChain: [.. chain]
		);
	}

	/// <summary>
	/// An empty list on the child means "inherit"; a non-empty list replaces the parent's, so a
	/// child can narrow a ref set rather than only widening it.
	/// </summary>
	static IReadOnlyList<string> Inherit(string[] own, IReadOnlyList<string>? parent) =>
		own.Length > 0 ? own : parent ?? [];

	static IReadOnlyList<string> ApplyRules(
		string policyName,
		string[] rules,
		IReadOnlyList<string> inherited
	)
	{
		if (rules.Length == 0)
			return [.. inherited.Order(StringComparer.Ordinal)];

		var deltas = rules.Count(IsDelta);
		if (deltas != 0 && deltas != rules.Length)
			throw new InvalidOperationException(
				$"The eligibility policy '{policyName}' mixes absolute rule entries with '+'/'-' deltas "
					+ $"({string.Join(", ", rules)}). Use either an absolute list or deltas, not both."
			);

		SortedSet<string> resolved = deltas == 0
			? new(StringComparer.Ordinal)
			: new(inherited, StringComparer.Ordinal);

		foreach (var rule in rules)
		{
			var trimmed = rule.Trim();
			if (trimmed.Length == 0)
				continue;

			if (trimmed.StartsWith('-'))
				resolved.Remove(Normalize(policyName, trimmed[1..]));
			else if (trimmed.StartsWith('+'))
				resolved.Add(Normalize(policyName, trimmed[1..]));
			else
				resolved.Add(Normalize(policyName, trimmed));
		}

		return [.. resolved];
	}

	static bool IsDelta(string rule)
	{
		var trimmed = rule.Trim();
		return trimmed.StartsWith('+') || trimmed.StartsWith('-');
	}

	static string Normalize(string policyName, string ruleId)
	{
		var trimmed = ruleId.Trim();

		return EligibilityRules.IsKnown(trimmed)
			? trimmed.ToUpperInvariant()
			: throw new InvalidOperationException(
				$"The eligibility policy '{policyName}' names an unknown rule '{trimmed}'. Known rules: "
					+ $"{string.Join(", ", EligibilityRules.KnownIds)}."
			);
	}
}
