using Purview.Build.PackageJson.Rules;

namespace Purview.Build.PackageJson;

/// <summary>
/// The rule registry. Adding a rule means adding one implementation and one entry here.
/// </summary>
static class PackageJsonRules
{
	/// <summary>
	/// The rule ID reported when the file could not be read at all. Not a rule: it exists so a
	/// missing or malformed file is reported in the same shape as any other finding.
	/// </summary>
	public const string ReadFailureRuleId = "PKG000";

	/// <summary>
	/// Every rule, ordered by ID. Evaluation order is ID order, so the registry's order is the
	/// documented order.
	/// </summary>
	public static IReadOnlyList<IPackageJsonRule> All { get; } =
	[
		new Pkg001NameRule(),
		new Pkg002VersionRule(),
		new Pkg003DescriptionRule(),
		new Pkg004LicenseRule(),
		new Pkg005RepositoryRule(),
		new Pkg006AuthorRule(),
		new Pkg007LinksRule(),
		new Pkg008KeywordsRule(),
		new Pkg009PrivateIntentRule(),
		new Pkg010EnginesRule(),
		new Pkg011EntryPointsRule(),
		new Pkg012ScriptsRule(),
		new Pkg013FilesRule(),
		new Pkg014AdditionalRequiredFieldsRule(),
	];

	public static IReadOnlyList<string> KnownIds { get; } =
		[ReadFailureRuleId, .. All.Select(rule => rule.Id)];

	public static bool IsKnown(string ruleId) =>
		KnownIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase);

	public static IPackageJsonRule ById(string ruleId) =>
		All.First(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
}
