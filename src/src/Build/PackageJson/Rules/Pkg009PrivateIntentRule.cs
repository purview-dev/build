namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG009 — <c>private</c> agrees with what the repository does with the file.
/// </summary>
/// <remarks>
/// In npm terms the absence of <c>private</c> means "publishable". For a .NET repository the file
/// only carries the release version, so a missing flag is a warning; for a web application it is the
/// difference between a private app and an accidental publish, which is an error.
/// </remarks>
sealed class Pkg009PrivateIntentRule : IPackageJsonRule
{
	public string Id => "PKG009";

	public string Description => "The private flag matches whether the file is published";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var isPrivate = context.Document.IsPrivate;

		return context.Profile switch
		{
			PackageJsonProfile.WebPackage when isPrivate => PackageJsonRuleResult.Fail(
				Id,
				"'private' is true, so npm refuses to publish this package. Remove 'private', or set "
					+ "PackageJson:Profile=WebApp if this is an application."
			),
			PackageJsonProfile.WebApp when !isPrivate => PackageJsonRuleResult.Fail(
				Id,
				"This file describes an application, so it must not be publishable. Set 'private' to "
					+ "true, or set PackageJson:Profile=WebPackage if it really is published."
			),
			PackageJsonProfile.DotNetNuGet or PackageJsonProfile.DotNetSolution when !isPrivate =>
				PackageJsonRuleResult.Warning(
					Id,
					"The file is not 'private', which in npm terms means publishable. A .NET repository's "
						+ "package.json only carries the release version, so set 'private' to true."
				),
			_ => PackageJsonRuleResult.Pass(Id, "The 'private' flag matches how this file is used."),
		};
	}
}
