using NuGet.Versioning;

namespace Purview.Build.Release.Rules;

/// <summary>
/// REL001 — the version parses as valid for the configured strictness.
/// </summary>
sealed class Rel001VersionParsesRule : IEligibilityRule
{
	public string Id => "REL001";

	public string Description => "Version parses as valid for the configured strictness";

	public RuleResult Evaluate(EligibilityInput input, ResolvedPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(input);

		if (string.IsNullOrWhiteSpace(input.RawVersion))
			return RuleResult.Fail(Id, "The version in package.json is missing or empty.");

		if (input.Unit is null)
			return RuleResult.Fail(
				Id,
				$"The version '{input.RawVersion}' is not a valid version. "
					+ "Set the 'version' field in the repository root package.json to a valid SemVer value."
			);

		if (input.Strictness == VersionStrictness.SemVer2 && !SemanticVersion.TryParse(input.RawVersion, out _))
			return RuleResult.Fail(
				Id,
				$"The version '{input.RawVersion}' is not valid SemVer 2.0, and Version:Strictness is "
					+ "SemVer2. Use a three-part version, or set Version:Strictness=NuGet to allow "
					+ "four-part versions."
			);

		// The version is known and satisfies the configured strictness.
		return RuleResult.Pass(Id, $"Version '{input.RawVersion}' is valid for {input.Strictness} strictness.");
	}
}
