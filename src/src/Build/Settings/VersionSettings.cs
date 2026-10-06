namespace Purview.Build.Settings;

/// <summary>
/// Where the release version comes from.
/// </summary>
public enum VersionSource
{
	/// <summary>
	/// The <c>version</c> field of the repository root <c>package.json</c>.
	/// </summary>
	PackageJson,
}

/// <summary>
/// How strictly a version string is validated.
/// </summary>
public enum VersionStrictness
{
	/// <summary>
	/// <see cref="NuGet.Versioning.NuGetVersion"/> parsing, which additionally accepts "legacy"
	/// four-part versions such as <c>13.5.3.10</c>, and one- and two-part versions such as <c>1.0</c>.
	/// </summary>
	/// <remarks>
	/// The default, because it is exactly what the pipeline has always accepted. Two consuming
	/// repositories ship four-part versions (aspirec4, build-sdk), so defaulting to
	/// <see cref="SemVer2"/> would stop them releasing.
	/// </remarks>
	NuGet,

	/// <summary>
	/// Strict SemVer 2.0 parsing: three numeric components with optional prerelease and build
	/// metadata. Rejects four-part versions.
	/// </summary>
	SemVer2,
}

public sealed record VersionSettings
{
	public const string SectionName = "Version";

	public VersionSource Source { get; init; } = VersionSource.PackageJson;

	public VersionStrictness Strictness { get; init; } = VersionStrictness.NuGet;
}
