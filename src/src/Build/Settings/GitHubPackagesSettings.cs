namespace Purview.Build.Settings;

/// <summary>
/// The optional second destination for a release: the organization's GitHub Packages NuGet feed.
/// </summary>
/// <remarks>
/// Publication is additional, never a substitute: NuGet.org stays the primary feed, and enabling this
/// merely replicates the same immutable package artifacts there. It is off unless a repository opts in
/// through <c>NuGet:GitHubPackages:Enabled</c>, which the shared release workflow maps from the
/// <c>PURVIEW_PUBLISH_GITHUB_PACKAGES</c> repository variable.
/// <para>
/// The owner defaults to <c>GITHUB_REPOSITORY_OWNER</c> so the feed follows the repository rather than a
/// hardcoded organization, and to <c>purview-dev</c> only when no environment is available (a local run).
/// </para>
/// </remarks>
public sealed record GitHubPackagesSettings
{
	/// <summary>
	/// The organization the packages belong to when neither configuration nor the environment name one.
	/// </summary>
	public const string DefaultOwner = "purview-dev";

	/// <summary>
	/// Whether an eligible release also publishes to GitHub Packages. Defaults to false: a repository
	/// that configures nothing continues to publish to NuGet.org only.
	/// </summary>
	public bool Enabled { get; init; }

	/// <summary>
	/// Feed owner (organization or user). Falls back to <c>GITHUB_REPOSITORY_OWNER</c>, then
	/// <see cref="DefaultOwner"/>.
	/// </summary>
	public string? Owner { get; init; }

	/// <summary>
	/// Explicit feed URL, for a namespace that is not the one the workflow runs under. Falls back to
	/// <c>https://nuget.pkg.github.com/{owner}/index.json</c>.
	/// </summary>
	public string? FeedUrl { get; init; }

	/// <summary>
	/// Basic-auth username for the feed. Falls back to <c>GITHUB_ACTOR</c>, then the owner.
	/// </summary>
	public string? Username { get; init; }

	/// <summary>
	/// The owner the feed URL is derived from.
	/// </summary>
	public string ResolveOwner(Func<string, string?>? readEnvironment = null) =>
		!string.IsNullOrWhiteSpace(Owner)
			? Owner.Trim()
			: FirstNonEmpty(
				(readEnvironment ?? Environment.GetEnvironmentVariable)("GITHUB_REPOSITORY_OWNER"),
				DefaultOwner
			);

	/// <summary>
	/// The username sent with the token. GitHub accepts the workflow actor; a local run falls back to
	/// the owner so a manual push is still attributable.
	/// </summary>
	public string ResolveUsername(Func<string, string?>? readEnvironment = null) =>
		!string.IsNullOrWhiteSpace(Username)
			? Username.Trim()
			: FirstNonEmpty(
				(readEnvironment ?? Environment.GetEnvironmentVariable)("GITHUB_ACTOR"),
				ResolveOwner(readEnvironment)
			);

	/// <summary>
	/// The full feed URL packages are pushed to.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Design",
		"CA1055:URI return values should not be strings",
		Justification = "The value is a NuGet source address, passed verbatim to dotnet nuget push and compared with NuGet:FeedUrl, which is a string setting."
	)]
	public string ResolveFeedUrl(Func<string, string?>? readEnvironment = null) =>
		!string.IsNullOrWhiteSpace(FeedUrl)
			? FeedUrl.Trim()
			: $"https://nuget.pkg.github.com/{ResolveOwner(readEnvironment)}/index.json";

	/// <summary>
	/// True when <paramref name="feedUrl"/> addresses this same registry, so pushing to both would send
	/// the package twice.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Design",
		"CA1054:URI parameters should not be strings",
		Justification = "The parameter comes from NuGet:FeedUrl / the channel FeedUrl, which are string settings."
	)]
	public bool IsSameRegistry(string? feedUrl, Func<string, string?>? readEnvironment = null) =>
		!string.IsNullOrWhiteSpace(feedUrl)
		&& string.Equals(
			Normalise(feedUrl),
			Normalise(ResolveFeedUrl(readEnvironment)),
			StringComparison.OrdinalIgnoreCase
		);

	static string Normalise(string url) => url.Trim().TrimEnd('/');

	static string FirstNonEmpty(params string?[] values) =>
		values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
		?? DefaultOwner;
}
