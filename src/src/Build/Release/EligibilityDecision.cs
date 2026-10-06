namespace Purview.Build.Release;

/// <summary>
/// The overall outcome of evaluating a release unit against a policy.
/// </summary>
public enum EligibilityVerdict
{
	/// <summary>Every enabled rule passed; the release should proceed.</summary>
	Release,

	/// <summary>Already released. Not an error: the run exits 0 and publishes nothing.</summary>
	Skip,

	/// <summary>A policy violation. The run exits 1.</summary>
	Fail,
}

/// <summary>
/// The outcome of one <c>REL0nn</c> rule.
/// </summary>
public enum RuleOutcome
{
	Pass,

	Skip,

	Fail,
}

/// <summary>
/// One rule's verdict and its message. The message always begins with the rule's ID so every
/// failure path names the rule that produced it.
/// </summary>
public sealed record RuleResult(string RuleId, RuleOutcome Outcome, string Message)
{
	public static RuleResult Pass(string ruleId, string message) =>
		new(ruleId, RuleOutcome.Pass, $"{ruleId}: {message}");

	public static RuleResult Skip(string ruleId, string message) =>
		new(ruleId, RuleOutcome.Skip, $"{ruleId}: {message}");

	public static RuleResult Fail(string ruleId, string message) =>
		new(ruleId, RuleOutcome.Fail, $"{ruleId}: {message}");
}

/// <summary>
/// The evaluated decision: the verdict, the rule that decided it, and every rule's result in
/// evaluation order up to the short-circuit point.
/// </summary>
public sealed record EligibilityDecision(
	EligibilityVerdict Verdict,
	string? RuleId,
	string Message,
	IReadOnlyList<RuleResult> Results,
	string? ShortCircuitRuleId
)
{
	/// <summary>
	/// The process exit code this decision implies. <see cref="EligibilityVerdict.Skip"/> is a
	/// success: the version is simply already released.
	/// </summary>
	public int ExitCode => Verdict == EligibilityVerdict.Fail ? 1 : 0;

	/// <summary>
	/// The <c>Release__Mode</c> a workflow should set for this decision, given the mode it intended
	/// to use. A non-releasing verdict yields <c>None</c>, so the publish and release modules skip.
	/// </summary>
	public ReleaseMode ResolveMode(ReleaseMode intendedMode) =>
		Verdict == EligibilityVerdict.Release ? intendedMode : ReleaseMode.None;
}
