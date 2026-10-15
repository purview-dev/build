using ModularPipelines.Attributes;

namespace Purview.Build.Settings;

public sealed record GitHubSettings
{
	public const string SectionName = "GitHub";

	/// <summary>
	/// Secret: the GitHub token used to create releases. Prefer the <c>GITHUB_TOKEN</c> environment
	/// variable over committing it here.
	/// </summary>
	[SecretValue]
	public string? AccessToken { get; init; }

	/// <summary>
	/// Secret: binds <c>GitHub__GITHUB_TOKEN</c>. Prefer the plain <c>GITHUB_TOKEN</c> environment
	/// variable.
	/// </summary>
	[SecretValue]
	[ConfigurationKeyName("GITHUB_TOKEN")]
	public string? EnvAccessToken { get; init; }

	public string ProductHeader { get; init; } = "Purview.Build.Pipeline";

	public string? GetGitHubToken()
	{
		if (!string.IsNullOrWhiteSpace(AccessToken))
			return AccessToken;

		if (!string.IsNullOrWhiteSpace(EnvAccessToken))
			return EnvAccessToken;

		// GitHub Actions provisions the automatic GITHUB_TOKEN as a plain environment variable.
		// The config binder keys it under the "GitHub" section (GitHub:GITHUB_TOKEN), which the
		// standard GITHUB_TOKEN env var does not map to, so read it directly as a fallback.
		var processToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
		return string.IsNullOrWhiteSpace(processToken) ? null : processToken;
	}
}
