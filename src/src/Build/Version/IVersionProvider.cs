using Purview.Build.Release;

namespace Purview.Build.Version;

/// <summary>
/// Produces the release units for a run.
/// </summary>
/// <remarks>
/// An abstraction so <c>Tag</c>, <c>MinVer</c> and <c>External</c> sources can be added later without
/// touching <c>PackModule</c> or <c>CreateGitHubReleaseModule</c>: they read the resolved
/// <see cref="ReleaseUnitSet"/> and never recompute a version.
/// </remarks>
interface IVersionProvider
{
	VersionSource Source { get; }

	/// <summary>
	/// Resolves the raw version text and the units it produces.
	/// </summary>
	Task<ReleaseUnitSet> ResolveAsync(
		VersionProviderContext context,
		CancellationToken cancellationToken
	);
}

/// <summary>
/// What a version provider is given.
/// </summary>
/// <param name="RepositoryRoot">The repository root; relative paths resolve against it.</param>
/// <param name="ChannelName">The channel the resolved units are released on.</param>
/// <param name="Strictness">How strictly the resolved version text is validated.</param>
sealed record VersionProviderContext(
	string RepositoryRoot,
	string ChannelName,
	VersionStrictness Strictness
);
