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
	/// When true, packages are pushed without an API key using NuGet Trusted Publishing
	/// (OIDC federation, e.g. via the NuGet/login GitHub Action). No API key is required.
	/// </summary>
	public bool TrustedPublishing { get; init; }

	public string? GetNuGetAPIKey() =>
		!string.IsNullOrWhiteSpace(APIKey) ? APIKey
		: !string.IsNullOrWhiteSpace(EnvAPIKey) ? EnvAPIKey
		: GetProcessAPIKey();

	static string? GetProcessAPIKey()
	{
		// GitHub Actions and other CI inject NUGET_APIKEY as a plain environment variable, which the
		// config binder does not map under the "NuGet" section. Read it directly as a fallback.
		foreach (var name in new[] { "NUGET_APIKEY", "NUGET_API_KEY" })
		{
			var value = Environment.GetEnvironmentVariable(name);
			if (!string.IsNullOrWhiteSpace(value))
				return value;
		}

		return null;
	}
}
