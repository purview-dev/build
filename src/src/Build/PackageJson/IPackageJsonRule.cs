namespace Purview.Build.PackageJson;

/// <summary>
/// One <c>package.json</c> content rule.
/// </summary>
/// <remarks>
/// Rule IDs are permanent identifiers: never renumbered, never repurposed.
/// </remarks>
interface IPackageJsonRule
{
	/// <summary>The rule's permanent <c>PKG0nn</c> identifier.</summary>
	string Id { get; }

	/// <summary>One-line description, used by the report and the documentation.</summary>
	string Description { get; }

	PackageJsonRuleResult Evaluate(PackageJsonValidationContext context);
}
