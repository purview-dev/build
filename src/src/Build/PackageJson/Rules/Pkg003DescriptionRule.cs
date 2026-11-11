namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG003 — the package says what it is.
/// </summary>
/// <remarks>
/// The description is what a registry search result, a directory listing and a feed entry show, so an
/// absent one is an error for every profile; a token one is advisory.
/// </remarks>
sealed class Pkg003DescriptionRule : IPackageJsonRule
{
	const int MinimumSubstantiveLength = 10;

	public string Id => "PKG003";

	public string Description => "Description is present and says what the package does";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var description = context.Document.GetString("description");

		if (string.IsNullOrWhiteSpace(description))
			return PackageJsonRuleResult.Fail(
				Id,
				"The 'description' field is missing or empty. It is what a registry search result and a "
					+ "directory listing show."
			);

		var trimmed = description.Trim();

		if (
			trimmed.Length < MinimumSubstantiveLength
			|| string.Equals(trimmed, context.Document.GetString("name"), StringComparison.Ordinal)
		)
		{
			return PackageJsonRuleResult.Warning(
				Id,
				$"The description '{trimmed}' says too little. Describe what the package does and who "
					+ "it is for."
			);
		}

		return PackageJsonRuleResult.Pass(Id, "A description is present.");
	}
}
