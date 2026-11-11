namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG001 — the package has a name, and npm will accept it when the file is published there.
/// </summary>
sealed class Pkg001NameRule : IPackageJsonRule
{
	public string Id => "PKG001";

	public string Description => "Package name is present and valid for the target registry";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var name = context.Document.GetString("name");

		if (string.IsNullOrWhiteSpace(name))
			return PackageJsonRuleResult.Fail(Id, "The 'name' field is missing or empty.");

		if (!PackageJsonProfiles.IsWeb(context.Profile))
			return PackageJsonRuleResult.Pass(Id, $"Name '{name}' is present.");

		if (NpmNames.DescribeProblem(name) is not { } problem)
			return PackageJsonRuleResult.Pass(Id, $"Name '{name}' is a valid npm package name.");

		// A published package is rejected outright; an application's name is never sent to npm, so
		// the same defect is only advisory there.
		return context.Profile == PackageJsonProfile.WebPackage
			? PackageJsonRuleResult.Fail(
				Id,
				$"Name '{name}' is not a valid npm package name: {problem}."
			)
			: PackageJsonRuleResult.Warning(
				Id,
				$"Name '{name}' would be rejected by npm: {problem}."
			);
	}
}
