namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG014 — the repository's own extra requirements are met.
/// </summary>
/// <remarks>
/// The escape hatch for a field that matters to one organisation and not to the ecosystem:
/// <c>PackageJson:AdditionalRequiredFields</c> names top-level fields that must be present and
/// non-empty.
/// </remarks>
sealed class Pkg014AdditionalRequiredFieldsRule : IPackageJsonRule
{
	public string Id => "PKG014";

	public string Description => "Every field named by PackageJson:AdditionalRequiredFields is present";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.AdditionalRequiredFields.Count == 0)
		{
			return PackageJsonRuleResult.Skipped(
				Id,
				"PackageJson:AdditionalRequiredFields is empty."
			);
		}

		var missing = context.AdditionalRequiredFields.Where(field => !context.Document.HasNonEmpty(field));

		return missing.Any()
			? PackageJsonRuleResult.Fail(
				Id,
				$"PackageJson:AdditionalRequiredFields requires {string.Join(", ", missing)}, which "
					+ "is missing or empty."
			)
			: PackageJsonRuleResult.Pass(
				Id,
				$"Every configured additional field is present "
					+ $"({string.Join(", ", context.AdditionalRequiredFields)})."
			);
	}
}
