namespace Purview.Build.PackageJson;

/// <summary>What one rule concluded.</summary>
public enum PackageJsonRuleOutcome
{
	/// <summary>The requirement is met.</summary>
	Pass,

	/// <summary>The requirement is not met, but the run does not fail on it.</summary>
	Warning,

	/// <summary>The requirement is not met and the run fails.</summary>
	Fail,

	/// <summary>The rule does not apply to this repository's profile, or was disabled.</summary>
	Skipped,
}

/// <summary>
/// The outcome of one rule, in evaluation order.
/// </summary>
sealed record PackageJsonRuleResult(string RuleId, PackageJsonRuleOutcome Outcome, string Message)
{
	public static PackageJsonRuleResult Pass(string ruleId, string message) =>
		new(ruleId, PackageJsonRuleOutcome.Pass, message);

	public static PackageJsonRuleResult Warning(string ruleId, string message) =>
		new(ruleId, PackageJsonRuleOutcome.Warning, message);

	public static PackageJsonRuleResult Fail(string ruleId, string message) =>
		new(ruleId, PackageJsonRuleOutcome.Fail, message);

	public static PackageJsonRuleResult Skipped(string ruleId, string message) =>
		new(ruleId, PackageJsonRuleOutcome.Skipped, message);
}
