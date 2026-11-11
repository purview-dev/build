namespace Purview.Build.PackageJson.Rules;

/// <summary>
/// PKG004 — the package declares a licence a consumer can act on.
/// </summary>
/// <remarks>
/// The identifier is checked against the common SPDX short forms rather than the full licence list:
/// an unrecognised value is advisory, so a correct but unlisted identifier never fails a build.
/// </remarks>
sealed class Pkg004LicenseRule : IPackageJsonRule
{
	static readonly HashSet<string> KnownSpdxIdentifiers = new(StringComparer.OrdinalIgnoreCase)
	{
		"0BSD",
		"AGPL-3.0-only",
		"AGPL-3.0-or-later",
		"Apache-2.0",
		"Artistic-2.0",
		"BSD-2-Clause",
		"BSD-3-Clause",
		"BSL-1.0",
		"CC0-1.0",
		"CDDL-1.0",
		"Elastic-2.0",
		"EPL-2.0",
		"GPL-2.0-only",
		"GPL-2.0-or-later",
		"GPL-3.0-only",
		"GPL-3.0-or-later",
		"ISC",
		"LGPL-2.1-only",
		"LGPL-2.1-or-later",
		"LGPL-3.0-only",
		"LGPL-3.0-or-later",
		"MIT",
		"MIT-0",
		"MPL-2.0",
		"MS-PL",
		"MS-RL",
		"NCSA",
		"PostgreSQL",
		"Python-2.0",
		"Unlicense",
		"WTFPL",
		"Zlib",
	};

	public string Id => "PKG004";

	public string Description => "License is present and is an SPDX identifier";

	public PackageJsonRuleResult Evaluate(PackageJsonValidationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var license = context.Document.GetString("license");

		if (string.IsNullOrWhiteSpace(license))
			return PackageJsonRuleResult.Fail(
				Id,
				"The 'license' field is missing. Declare an SPDX identifier such as 'MIT', or "
					+ "'SEE LICENSE IN LICENSE.md'."
			);

		if (license.StartsWith("SEE LICENSE IN ", StringComparison.OrdinalIgnoreCase))
			return PackageJsonRuleResult.Pass(Id, $"License is declared as '{license}'.");

		if (string.Equals(license, "UNLICENSED", StringComparison.OrdinalIgnoreCase))
		{
			return PackageJsonProfiles.Publishes(context.Profile)
				? PackageJsonRuleResult.Warning(
					Id,
					"The package is published but licensed 'UNLICENSED'. Declare a real licence, or stop "
						+ "publishing it."
				)
				: PackageJsonRuleResult.Pass(
					Id,
					"The package is not published, so 'UNLICENSED' is appropriate."
				);
		}

		return KnownSpdxIdentifiers.Contains(license)
			? PackageJsonRuleResult.Pass(Id, $"License '{license}' is a recognised SPDX identifier.")
			: PackageJsonRuleResult.Warning(
				Id,
				$"License '{license}' is not a recognised SPDX identifier. Use an SPDX expression such "
					+ "as 'MIT' or 'Apache-2.0', or 'SEE LICENSE IN <file>'."
			);
	}
}
