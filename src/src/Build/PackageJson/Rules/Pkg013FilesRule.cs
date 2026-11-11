namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG013 — the published tarball is deliberate rather than "everything that is not ignored".
/// </summary>
sealed class Pkg013FilesRule : IPackageJsonRule
{
	public string Id => "PKG013";

	public string Description => "A published package declares a files allow-list";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.Profile != PackageJsonProfile.WebPackage)
		{
			return PackageJsonRuleResult.Skipped(
				Id,
				"A publish allow-list only matters for a package published to npm."
			);
		}

		var files = context.Document.GetStrings("files");

		return files.Count > 0
			? PackageJsonRuleResult.Pass(Id, $"'files' declares {files.Count} path(s).")
			: PackageJsonRuleResult.Warning(
				Id,
				"No 'files' allow-list is declared, so npm publishes everything that is not ignored. "
					+ "Declare 'files' so the tarball is deliberate."
			);
	}
}
