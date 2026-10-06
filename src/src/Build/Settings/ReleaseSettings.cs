using NuGet.Versioning;

namespace Purview.Build.Settings;

public sealed record ReleaseSettings
{
	public const string SectionName = "Release";

	/// <summary>
	/// Preset that derives <see cref="Publish"/> and <see cref="GitHubRelease"/>. Retained as the
	/// primary switch; an explicitly set <see cref="Publish"/>/<see cref="GitHubRelease"/> wins over
	/// whatever the preset implies.
	/// </summary>
	public ReleaseMode Mode { get; set; } = ReleaseMode.None;

	/// <summary>
	/// Whether packages are pushed. Null means "derive from <see cref="Mode"/>".
	/// </summary>
	public bool? Publish { get; init; }

	/// <summary>
	/// Whether the tag and GitHub release are created. Null means "derive from the channel, then
	/// from <see cref="Mode"/>".
	/// </summary>
	public bool? GitHubRelease { get; init; }

	/// <summary>
	/// Named channel, selecting the feed and the label policy.
	/// </summary>
	public string Channel { get; init; } = ReleaseChannelSettings.StableChannelName;

	public Dictionary<string, ReleaseChannelSettings> Channels { get; init; } = [];

	/// <summary>
	/// Runs the full pipeline but skips publishing and the GitHub release, logging what each would
	/// have done.
	/// </summary>
	public bool DryRun { get; init; }

	/// <summary>
	/// When true, the GitHub release module uploads every file in <c>Build:ArtifactsFolder</c>
	/// (for example .nupkg/.snupkg or .vsix) as release assets.
	/// </summary>
	public bool UploadArtifacts { get; init; }

	/// <summary>
	/// When true (the default), a GitHub release whose version is a prerelease (for example
	/// <c>2.0.0-prerelease.25</c>) is created as a prerelease, so it is not presented as the latest
	/// stable release. Set to false to publish prerelease versions as stable releases.
	/// </summary>
	public bool MarkPrerelease { get; init; } = true;

	public EligibilitySettings Eligibility { get; init; } = new();

	public ReleaseContextSettings Context { get; init; } = new();

	/// <summary>
	/// The resolved settings for <see cref="Channel"/>, or the stable defaults when the channel is
	/// not declared.
	/// </summary>
	public ReleaseChannelSettings ResolveChannel() =>
		Channels.TryGetValue(Channel, out var channel) ? channel : ReleaseChannelSettings.Stable;

	/// <summary>
	/// Whether packages should be pushed to a remote feed.
	/// </summary>
	public bool ShouldPublish() => Publish ?? (Mode == ReleaseMode.NuGet);

	/// <summary>
	/// Whether the tag and GitHub release should be created.
	/// </summary>
	public bool ShouldCreateGitHubRelease() =>
		GitHubRelease
		?? ResolveChannel().GitHubRelease
		?? (Mode is ReleaseMode.NuGet or ReleaseMode.GitHubRelease);

	/// <summary>
	/// Returns whether a release for <paramref name="version"/> should be created as a GitHub
	/// prerelease.
	/// </summary>
	public bool ShouldMarkPrerelease(NuGetVersion version)
	{
		ArgumentNullException.ThrowIfNull(version);

		var markPrerelease = ResolveChannel().MarkPrerelease ?? MarkPrerelease;

		return markPrerelease && version.IsPrerelease;
	}

	/// <summary>
	/// Fails when a simulated release context would drive a real publish.
	/// </summary>
	/// <remarks>
	/// Simulated tags and feed state exist to answer "what would happen"; letting them select which
	/// versions are already released while actually pushing packages and cutting tags is the one
	/// combination that can do real damage from a developer machine.
	/// </remarks>
	public void ValidateSimulationInterlock()
	{
		if (!Context.IsSimulated)
			return;

		if (Mode != ReleaseMode.NuGet && !ShouldPublish())
			return;

		throw new InvalidOperationException(
			"Release:Context:* is set (simulated release context) while the release would really publish "
				+ $"(Release:Mode={Mode}, Release:Publish={ShouldPublish()}). Simulated context must never "
				+ "drive a real publish. Drop the Release:Context:* keys, or use 'release-explain' / "
				+ "Release:DryRun=true to evaluate without publishing."
		);
	}
}
