namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG007 — users have somewhere to read the docs and report a problem.
/// </summary>
sealed class Pkg007LinksRule : IPackageJsonRule
{
	public string Id => "PKG007";

	public string Description => "Homepage or a bug tracker is declared";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var homepage = context.Document.GetString("homepage");
		var bugs = context.Document.GetNestedString("bugs", "url");

		if (!string.IsNullOrWhiteSpace(homepage) || !string.IsNullOrWhiteSpace(bugs))
			return PackageJsonRuleResult.Pass(Id, "A homepage or bug tracker is declared.");

		return PackageJsonRuleResult.Warning(
			Id,
			"Neither 'homepage' nor 'bugs.url' is set. Users need somewhere to read the docs and "
				+ "report a problem."
		);
	}
}
