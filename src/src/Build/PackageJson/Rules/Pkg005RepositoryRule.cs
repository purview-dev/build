namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG005 — the source is discoverable, in the form registries understand.
/// </summary>
sealed class Pkg005RepositoryRule : IPackageJsonRule
{
	public string Id => "PKG005";

	public string Description => "Repository is declared as an object with a type and a url";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!context.Document.Has("repository"))
		{
			var advice =
				"Add a 'repository' object so consumers can find, inspect and contribute to the source.";

			return PackageJsonProfiles.Publishes(context.Profile)
				? PackageJsonRuleResult.Fail(Id, $"The 'repository' field is missing. {advice}")
				: PackageJsonRuleResult.Warning(Id, $"The 'repository' field is missing. {advice}");
		}

		if (context.Document.GetObject("repository") is null)
		{
			return PackageJsonRuleResult.Warning(
				Id,
				"Write 'repository' as an object with 'type' and 'url'; the shorthand string form omits "
					+ "the type registries link against."
			);
		}

		if (string.IsNullOrWhiteSpace(context.Document.GetNestedString("repository", "url")))
			return PackageJsonRuleResult.Warning(Id, "'repository.url' is missing or empty.");

		if (string.IsNullOrWhiteSpace(context.Document.GetNestedString("repository", "type")))
			return PackageJsonRuleResult.Warning(
				Id,
				"'repository.type' is missing. Declare it, e.g. \"type\": \"git\"."
			);

		return PackageJsonRuleResult.Pass(Id, "A repository type and url are declared.");
	}
}
