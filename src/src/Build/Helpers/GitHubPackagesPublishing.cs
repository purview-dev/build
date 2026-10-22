namespace Purview.Build.Helpers;

/// <summary>
/// Whether a run publishes packages to the organization's GitHub Packages feed.
/// </summary>
/// <remarks>
/// <c>ConfigurationPreflight</c> and the publish module both have to answer this, and they must agree:
/// preflight only demands a token and a feed URL when the module is going to push. The same split
/// (<see cref="LocalNuGetPublishing"/>) exists for the local feed for the same reason.
/// <para>
/// GitHub Packages is an additional destination, not a replacement. Every clause below either does not
/// apply or is a consequence of an ordinary release switch, so no repository's existing publication
/// changes unless it sets <c>NuGet:GitHubPackages:Enabled</c>.
/// </para>
/// </remarks>
static class GitHubPackagesPublishing
{
	/// <summary>
	/// The reason the GitHub Packages publish will not happen, or <see langword="null"/> when it will.
	/// </summary>
	public static string? DisabledReason(
		ProjectType projectType,
		ReleaseSettings release,
		NuGetSettings nuget,
		Func<string, string?>? readEnvironment = null
	)
	{
		if (projectType == ProjectType.Web)
			return "GitHub Packages publishing does not apply to a Web project, which produces no packages.";

		if (!release.ShouldPublish())
		{
			return "GitHub Packages publishing is disabled. Set Release:Mode=NuGet (or Release:Publish=true) "
				+ "to publish packages.";
		}

		var settings = nuget.GitHubPackages;

		if (!settings.Enabled)
		{
			return "GitHub Packages publishing is disabled for this repository. Set "
				+ "NuGet:GitHubPackages:Enabled=true, or PURVIEW_PUBLISH_GITHUB_PACKAGES=true in GitHub Actions, "
				+ "to publish the same packages to the organization feed as well as NuGet.org.";
		}

		// A channel whose own feed is the GitHub Packages registry is already publishing there; running
		// both modules would push the same immutable artifact twice.
		var primaryFeed = release.ResolveChannel().FeedUrl ?? nuget.FeedUrl;
		if (settings.IsSameRegistry(primaryFeed, readEnvironment))
		{
			return $"The selected release channel already publishes to GitHub Packages "
				+ $"({settings.ResolveFeedUrl(readEnvironment)}), so the additional destination would duplicate "
				+ "the push.";
		}

		// The module will push to the feed, so it must have a token. The token is not required for a local
		return null;
	}

	/// <summary>
	/// Writes the throwaway NuGet configuration the push uses, so the tool needs no repository-level
	/// credentials and no ambient source registration.
	/// </summary>
	/// <remarks>
	/// The password is the <c>%GITHUB_TOKEN%</c> macro whenever the token came from the environment,
	/// which is the CI path: the credential is resolved by NuGet at push time and never written to disk.
	/// A token supplied through configuration (a local run) has no environment macro to reference, so it
	/// is written to a single-use file that the caller deletes in a <c>finally</c>.
	/// </remarks>
	public static string WritePushConfig(
		string feedUrl,
		string username,
		string token,
		Func<string, string?>? readEnvironment = null
	)
	{
		ArgumentNullException.ThrowIfNull(feedUrl);
		ArgumentNullException.ThrowIfNull(username);
		ArgumentNullException.ThrowIfNull(token);

		var environmentToken = (readEnvironment ?? Environment.GetEnvironmentVariable)("GITHUB_TOKEN");
		var password = string.Equals(token, environmentToken, StringComparison.Ordinal)
			? "%GITHUB_TOKEN%"
			: token;

		var path = Path.Combine(
			Path.GetTempPath(),
			$"purview-github-packages-{Guid.NewGuid():N}.config"
		);

		var content =
			$"""
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
			  <packageSources>
			    <clear />
			    <add key="purview-github-packages" value="{Escape(feedUrl)}" />
			  </packageSources>
			  <packageSourceCredentials>
			    <purview-github-packages>
			      <add key="Username" value="{Escape(username)}" />
			      <add key="ClearTextPassword" value="{Escape(password)}" />
			    </purview-github-packages>
			  </packageSourceCredentials>
			</configuration>
			""";

		File.WriteAllText(path, content);

		if (!OperatingSystem.IsWindows())
		{
			try
			{
				File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Best effort: the file is already private to the user's temp directory, and failing the
				// push because a permission tightening was refused would be worse than proceeding.
			}
		}

		return path;
	}

	static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
