using Purview.Build.Release;
using System.Text.Json;

namespace Purview.Build.Version;

/// <summary>
/// Reads the <c>version</c> field from the repository root <c>package.json</c>.
/// </summary>
/// <remarks>
/// The default and, today, the only source. Behaviour is deliberately unchanged from the original
/// <c>VersionModule</c>: the same file location, the same failure messages, and the same
/// <c>NuGetVersion</c> parsing — which accepts four-part versions, as two consuming repositories
/// require.
/// </remarks>
sealed class PackageJsonVersionProvider : IVersionProvider
{
	public VersionSource Source => VersionSource.PackageJson;

	public async Task<ReleaseUnitSet> ResolveAsync(
		VersionProviderContext context,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(context);

		var rawVersion = await ReadRawVersionAsync(context.RepositoryRoot, cancellationToken);

		if (!ReleaseUnitFactory.SatisfiesStrictness(rawVersion, context.Strictness))
		{
			throw new InvalidOperationException(
				context.Strictness == VersionStrictness.SemVer2
					? $"The version '{rawVersion}' in package.json is not valid SemVer 2.0 "
						+ "(Version:Strictness=SemVer2). Use a three-part version, or set "
						+ "Version:Strictness=NuGet to allow four-part versions."
					: $"The version '{rawVersion}' in package.json is not a valid SemVer."
			);
		}

		var unit =
			ReleaseUnitFactory.TryCreate(rawVersion, context.ChannelName, nameof(VersionSource.PackageJson))
			?? throw new InvalidOperationException(
				$"The version '{rawVersion}' in package.json is not a valid SemVer."
			);

		return new ReleaseUnitSet(rawVersion, [unit], nameof(VersionSource.PackageJson));
	}

	/// <summary>
	/// Reads the raw <c>version</c> text, without validating it, so eligibility can report on a
	/// version the pipeline would reject.
	/// </summary>
	public static async Task<string> ReadRawVersionAsync(
		string repositoryRoot,
		CancellationToken cancellationToken
	)
	{
		var packageJsonPath = Path.Combine(repositoryRoot, "package.json");

		if (!File.Exists(packageJsonPath))
			throw new FileNotFoundException($"Could not find package.json at {packageJsonPath}");

		var packageJson = await File.ReadAllTextAsync(packageJsonPath, cancellationToken);

		using var document = JsonDocument.Parse(packageJson);
		var version = document.RootElement.GetProperty("version").GetString();

		return string.IsNullOrWhiteSpace(version)
			? throw new InvalidOperationException(
				"The version field in package.json is missing or empty."
			)
			: version;
	}
}
