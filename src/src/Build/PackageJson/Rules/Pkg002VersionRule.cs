using NuGet.Versioning;

namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG002 — the version is present, and acceptable to every registry the file serves.
/// </summary>
/// <remarks>
/// The pipeline's own gate is <see cref="VersionStrictness"/>, which defaults to <c>NuGet</c> and so
/// accepts four-part versions such as <c>13.5.3.10</c>. npm accepts only SemVer 2.0, which is why
/// the same value is fatal for a published npm package and merely advisory for anything else.
/// </remarks>
sealed class Pkg002VersionRule : IPackageJsonRule
{
	public string Id => "PKG002";

	public string Description => "Version is present and valid for every registry the file serves";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var raw = context.Document.GetString("version");

		if (string.IsNullOrWhiteSpace(raw))
			return PackageJsonRuleResult.Fail(
				Id,
				"The 'version' field is missing or empty. It is the pipeline's release version."
			);

		if (SemanticVersion.TryParse(raw, out _))
		{
			if (raw is "0.0.0")
			{
				return PackageJsonRuleResult.Warning(
					Id,
					"The version is the '0.0.0' placeholder. Set a real version before releasing."
				);
			}

			return PackageJsonRuleResult.Pass(
				Id,
				context.ResolvedVersion is null
					? $"Version '{raw}' is valid SemVer 2.0."
					: $"Version '{raw}' is valid SemVer 2.0 and is the version the pipeline resolved."
			);
		}

		if (NuGetVersion.TryParse(raw, out _))
		{
			return context.Profile == PackageJsonProfile.WebPackage
				? PackageJsonRuleResult.Fail(
					Id,
					$"Version '{raw}' is not valid SemVer 2.0, and npm rejects anything else. "
						+ "Use a three-part version."
				)
				: PackageJsonRuleResult.Warning(
					Id,
					$"Version '{raw}' is accepted by NuGet (Version:Strictness={context.Strictness}) but "
						+ "is not SemVer 2.0, so npm and other strict consumers reject it."
				);
		}

		return PackageJsonRuleResult.Fail(Id, $"Version '{raw}' is not a valid version.");
	}
}
