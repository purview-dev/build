namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG010 — the supported runtime is stated where a runtime is the thing that runs it.
/// </summary>
sealed class Pkg010EnginesRule : IPackageJsonRule
{
	public string Id => "PKG010";

	public string Description => "engines.node declares the supported Node version range";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!PackageJsonProfiles.IsWeb(context.Profile))
		{
			return PackageJsonRuleResult.Skipped(
				Id,
				"Runtime engines only apply where npm is the consumer, so a .NET repository's file "
					+ "does not need them."
			);
		}

		var node = context.Document.GetNestedString("engines", "node");

		if (!string.IsNullOrWhiteSpace(node))
			return PackageJsonRuleResult.Pass(Id, $"'engines.node' declares '{node}'.");

		return context.Profile == PackageJsonProfile.WebPackage
			? PackageJsonRuleResult.Fail(
				Id,
				"No 'engines.node' range is declared. A consumer installing the package needs to know "
					+ "which Node versions are supported."
			)
			: PackageJsonRuleResult.Warning(
				Id,
				"No 'engines.node' range is declared. State the Node version the project supports."
			);
	}
}
