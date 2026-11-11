namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG008 — the package is discoverable in a registry search.
/// </summary>
/// <remarks>
/// Only meaningful where the file is published: a private application's keywords are never indexed.
/// </remarks>
sealed class Pkg008KeywordsRule : IPackageJsonRule
{
	public string Id => "PKG008";

	public string Description => "Keywords are declared for a published package";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!PackageJsonProfiles.Publishes(context.Profile))
		{
			return PackageJsonRuleResult.Skipped(
				Id,
				"Keywords only affect registry discovery, and this file is not published to a registry."
			);
		}

		var keywords = context.Document.GetStrings("keywords");

		return keywords.Count > 0
			? PackageJsonRuleResult.Pass(Id, $"{keywords.Count} keyword(s) are declared.")
			: PackageJsonRuleResult.Warning(
				Id,
				"No 'keywords' are declared, so the package is hard to find in a registry search."
			);
	}
}
