namespace Purview.Build.PackageJson;

/// <summary>
/// Resolves the effective <see cref="PackageJsonProfile"/>, so which rules apply is a property of
/// the repository rather than something the tool guesses.
/// </summary>
static class PackageJsonProfiles
{
	/// <summary>
	/// Resolves the profile: an explicit configuration wins, otherwise it is derived from the project
	/// type, whether the repository packs NuGet packages, and the file's own <c>private</c> field.
	/// </summary>
	public static PackageJsonProfile Resolve(
		PackageJsonProfile configured,
		ProjectType projectType,
		bool packsNuGetPackages,
		bool isPrivate
	) =>
		configured switch
		{
			PackageJsonProfile.Auto when projectType == ProjectType.Web =>
				isPrivate ? PackageJsonProfile.WebApp : PackageJsonProfile.WebPackage,
			PackageJsonProfile.Auto =>
				packsNuGetPackages
					? PackageJsonProfile.DotNetNuGet
					: PackageJsonProfile.DotNetSolution,
			_ => configured,
		};

	/// <summary>
	/// True for profiles whose <c>package.json</c> is published to a package registry, where metadata
	/// completeness stops being advisory.
	/// </summary>
	public static bool Publishes(PackageJsonProfile profile) =>
		profile is PackageJsonProfile.DotNetNuGet or PackageJsonProfile.WebPackage;

	/// <summary>
	/// True for the npm world, where npm's own naming and engine rules apply.
	/// </summary>
	public static bool IsWeb(PackageJsonProfile profile) =>
		profile is PackageJsonProfile.WebPackage or PackageJsonProfile.WebApp;

	public static string Describe(PackageJsonProfile profile) => profile.ToString();
}
