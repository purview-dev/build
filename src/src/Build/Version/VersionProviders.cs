namespace Purview.Build.Version;

/// <summary>
/// Selects the provider for a configured <see cref="VersionSource"/>.
/// </summary>
/// <remarks>
/// Adding a source means adding one provider and one entry here. <c>Tag</c>, <c>MinVer</c> and
/// <c>External</c> are deliberately not implemented yet; the abstraction exists so they can be,
/// without touching the modules that consume the resolved units.
/// </remarks>
static class VersionProviders
{
	static readonly IVersionProvider[] All = [new PackageJsonVersionProvider()];

	public static IVersionProvider For(VersionSource source) =>
		All.FirstOrDefault(provider => provider.Source == source)
		?? throw new InvalidOperationException(
			$"Version:Source={source} is not implemented. Supported sources: "
				+ $"{string.Join(", ", All.Select(provider => provider.Source))}."
		);
}
