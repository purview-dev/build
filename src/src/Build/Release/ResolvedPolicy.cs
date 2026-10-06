namespace Purview.Build.Release;

/// <summary>
/// A policy with its <see cref="RuleIds"/> fully resolved: inheritance chains followed and
/// <c>+</c>/<c>-</c> deltas applied, ordered by rule ID so evaluation order is the documented one.
/// </summary>
public sealed record ResolvedPolicy(
	string Name,
	IReadOnlyList<string> RuleIds,
	IReadOnlyList<string> StableRefs,
	IReadOnlyList<string> ServicingRefs,
	IReadOnlyList<string> TrunkRefs,
	IReadOnlyList<string> FourPartRefs,
	bool AllowFourPart,
	IReadOnlyList<string> InheritanceChain
)
{
	public bool Enables(string ruleId) => RuleIds.Contains(ruleId, StringComparer.Ordinal);
}
