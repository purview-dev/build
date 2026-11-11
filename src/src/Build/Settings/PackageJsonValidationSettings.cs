namespace Purview.Build.Settings;

/// <summary>
/// Which registry the repository root <c>package.json</c> serves, and therefore which set of content
/// rules applies to it.
/// </summary>
public enum PackageJsonProfile
{
	/// <summary>
	/// Derived from <see cref="BuildSettings.ProjectType"/>, whether the repository packs, and the
	/// file's own <c>private</c> field.
	/// </summary>
	Auto,

	/// <summary>
	/// A .NET repository that packs NuGet package(s). The file carries the release version and
	/// metadata; it is never published to npm.
	/// </summary>
	DotNetNuGet,

	/// <summary>A .NET repository that packs nothing. The file carries the release version only.</summary>
	DotNetSolution,

	/// <summary>A Web repository whose file is published to npm as a package.</summary>
	WebPackage,

	/// <summary>A Web application. The file is private and is never published.</summary>
	WebApp,
}

/// <summary>
/// Opt-in validation of the repository root <c>package.json</c> against the content a
/// best-in-class package of the repository's kind is expected to declare.
/// </summary>
/// <remarks>
/// Off by default: the file already exists in every consuming repository, so enabling this is a
/// deliberate decision to hold it to a standard. The <c>package-validate</c> command runs the same
/// checks on their own, whether or not this section enables the pipeline step.
/// </remarks>
public sealed record PackageJsonValidationSettings
{
	public const string SectionName = "PackageJson";

	/// <summary>
	/// When true, <see cref="Modules.ValidatePackageJsonModule"/> runs as part of the pipeline.
	/// </summary>
	/// <remarks>Defaults to <see langword="false"/>: the check is opt-in.</remarks>
	public bool Enabled { get; init; }

	/// <summary>
	/// The registry profile whose rules apply, or <see cref="PackageJsonProfile.Auto"/> to derive it
	/// from the project type and the file itself.
	/// </summary>
	public PackageJsonProfile Profile { get; init; } = PackageJsonProfile.Auto;

	/// <summary>
	/// Rule IDs to skip, e.g. <c>["PKG008"]</c> for a repository that deliberately declares no
	/// keywords. Unknown IDs are ignored.
	/// </summary>
	public string[] DisabledRules { get; init; } = [];

	/// <summary>
	/// Extra top-level fields the repository requires on top of the built-in rules, e.g.
	/// <c>["funding"]</c>. Any that is missing or empty is reported by <c>PKG014</c>.
	/// </summary>
	public string[] AdditionalRequiredFields { get; init; } = [];

	/// <summary>
	/// When true, a warning-level finding fails the run exactly as an error-level one does. Useful
	/// once the file is clean, to stop it drifting back.
	/// </summary>
	public bool TreatWarningsAsErrors { get; init; }
}
