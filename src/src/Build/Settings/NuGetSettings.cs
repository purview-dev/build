using ModularPipelines.Attributes;

namespace Purview.Build.Settings;

public sealed record NuGetSettings
{
	public const string SectionName = "NuGet";

	/// <summary>
	/// Secret: the NuGet API key used to push packages. Prefer the <c>NUGET_APIKEY</c> environment
	/// variable over committing it here.
	/// </summary>
	[SecretValue]
	public string? APIKey { get; set; }

	/// <summary>
	/// Secret: binds <c>NuGet__NUGET_APIKEY</c>. Prefer the plain <c>NUGET_APIKEY</c> environment
	/// variable.
	/// </summary>
	[SecretValue]
	[ConfigurationKeyName("NUGET_APIKEY")]
	public string? EnvAPIKey { get; set; }

	public string FeedUrl { get; init; } = "https://api.nuget.org/v3/index.json";

	/// <summary>
	/// When true, the API key is expected to arrive at run time from NuGet Trusted Publishing
	/// (OIDC federation, via the <c>NuGet/login</c> GitHub Action) rather than from configuration
	/// or a stored secret.
	/// </summary>
	/// <remarks>
	/// This does not mean "push without a key". The OIDC exchange mints a short-lived key, which
	/// the login action exports as <c>NUGET_API_KEY</c>; the push uses it like any other. The flag
	/// only tells the preflight not to demand a key up front, because none is configured and none
	/// should be. A publish that finds no key with this set is a misconfigured workflow, and is
	/// reported rather than attempted unauthenticated.
	/// </remarks>
	public bool TrustedPublishing { get; init; }

	/// <summary>
	/// The optional GitHub Packages destination. Disabled unless a repository opts in, so existing
	/// configurations publish exactly where they did before.
	/// </summary>
	public GitHubPackagesSettings GitHubPackages { get; init; } = new();

	public string? GetNuGetAPIKey(Func<string, string?>? readEnvironment = null) =>
		!string.IsNullOrWhiteSpace(APIKey) ? APIKey
		: !string.IsNullOrWhiteSpace(EnvAPIKey) ? EnvAPIKey
		: GetProcessAPIKey(readEnvironment ?? Environment.GetEnvironmentVariable);

	static string? GetProcessAPIKey(Func<string, string?> readEnvironment)
	{
		// GitHub Actions and other CI inject NUGET_APIKEY as a plain environment variable, which the
		// config binder does not map under the "NuGet" section. Read it directly as a fallback.
		foreach (var name in new[] { "NUGET_APIKEY", "NUGET_API_KEY" })
		{
			var value = readEnvironment(name);
			if (!string.IsNullOrWhiteSpace(value))
				return value;
		}

		return null;
	}
}
