namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG011 — a published package declares how it is imported.
/// </summary>
sealed class Pkg011EntryPointsRule : IPackageJsonRule
{
	static readonly string[] EntryPointFields =
	[
		"main",
		"module",
		"exports",
		"types",
		"typings",
		"bin",
	];

	public string Id => "PKG011";

	public string Description => "A published package declares at least one entry point";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.Profile != PackageJsonProfile.WebPackage)
		{
			return PackageJsonRuleResult.Skipped(
				Id,
				"Entry points only matter for a package that is published and then imported."
			);
		}

		var declared = EntryPointFields.Where(context.Document.HasNonEmpty).ToArray();

		return declared.Length > 0
			? PackageJsonRuleResult.Pass(
				Id,
				$"Entry point(s) declared: {string.Join(", ", declared)}."
			)
			: PackageJsonRuleResult.Warning(
				Id,
				"None of 'main', 'module', 'exports', 'types' or 'bin' is declared, so a consumer cannot "
					+ "import the package."
			);
	}
}
