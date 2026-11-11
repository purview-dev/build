namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG006 — the maintainer is named.
/// </summary>
sealed class Pkg006AuthorRule : IPackageJsonRule
{
	public string Id => "PKG006";

	public string Description => "Author is declared, as a string or an object with a name";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!context.Document.Has("author"))
		{
			var advice = "Add 'author' so the package has a named maintainer.";

			return PackageJsonProfiles.Publishes(context.Profile)
				? PackageJsonRuleResult.Fail(Id, $"The 'author' field is missing. {advice}")
				: PackageJsonRuleResult.Warning(Id, $"The 'author' field is missing. {advice}");
		}

		// npm accepts the "Name <email> (url)" string form as well as the object form.
		if (context.Document.GetString("author") is { } text)
		{
			return string.IsNullOrWhiteSpace(text)
				? PackageJsonRuleResult.Fail(Id, "The 'author' field is empty.")
				: PackageJsonRuleResult.Pass(Id, $"Author '{text}' is declared.");
		}

		if (context.Document.GetObject("author") is not { } author)
		{
			return PackageJsonRuleResult.Warning(
				Id,
				"Write 'author' as a string or an object; other shapes are not read by registries."
			);
		}

		var name = context.Document.GetNestedString("author", "name");

		if (string.IsNullOrWhiteSpace(name))
			return PackageJsonRuleResult.Fail(Id, "'author.name' is missing or empty.");

		return PackageJsonRuleResult.Pass(
			Id,
			author.ContainsKey("email") || author.ContainsKey("url")
				? $"Author '{name}' is declared with contact details."
				: $"Author '{name}' is declared."
		);
	}
}
