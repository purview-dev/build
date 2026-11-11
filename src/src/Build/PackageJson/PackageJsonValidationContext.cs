namespace Purview.Build.PackageJson;

/// <summary>
/// Everything a rule needs to judge one <c>package.json</c>: the document, the profile whose rules
/// apply, and the run-specific inputs a rule cannot infer from the file alone.
/// </summary>
sealed record PackageJsonValidationContext(
	PackageJsonDocument Document,
	PackageJsonProfile Profile,
	VersionStrictness Strictness,
	string? ResolvedVersion,
	IReadOnlyList<string> RequiredScripts,
	IReadOnlyList<string> RecommendedScripts,
	IReadOnlyList<string> AdditionalRequiredFields
);
