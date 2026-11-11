namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG012 — the scripts the pipeline drives are declared in the file that defines them.
/// </summary>
/// <remarks>
/// A Web run executes <c>Build:Web*Command</c> through <c>package.json</c> scripts. A script the
/// build step needs and does not find fails the run later anyway; naming it here says why, before
/// anything is built.
/// </remarks>
sealed class Pkg012ScriptsRule : IPackageJsonRule
{
	public string Id => "PKG012";

	public string Description => "The package.json scripts this run drives are declared";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.RequiredScripts.Count == 0 && context.RecommendedScripts.Count == 0)
		{
			return PackageJsonRuleResult.Skipped(
				Id,
				"This run drives no package.json scripts."
			);
		}

		var scripts = context.Document.GetObject("scripts");

		var required = Missing(scripts, context.RequiredScripts);
		if (required.Count > 0)
		{
			return PackageJsonRuleResult.Fail(
				Id,
				$"The scripts this run executes are not declared: {string.Join(", ", required)}. Add them "
					+ "to 'scripts', or point Build:Web*Command at scripts that exist."
			);
		}

		var recommended = Missing(scripts, context.RecommendedScripts);
		if (recommended.Count > 0)
		{
			return PackageJsonRuleResult.Warning(
				Id,
				$"Consider declaring {string.Join(", ", recommended)} in 'scripts', so the pipeline and "
					+ "contributors run the same commands."
			);
		}

		return PackageJsonRuleResult.Pass(Id, "Every script this run drives is declared.");
	}

	static List<string> Missing(System.Text.Json.Nodes.JsonObject? scripts, IReadOnlyList<string> names)
	{
		List<string> missing = [];

		foreach (var name in names)
		{
			if (scripts is null || !scripts.ContainsKey(name))
				missing.Add(name);
		}

		return missing;
	}
}
