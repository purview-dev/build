using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Purview.Build.Release;

/// <summary>
/// Everything <c>release-explain</c> reports, in one shape rendered either as text or as the JSON
/// contract the reusable workflow consumes.
/// </summary>
/// <remarks>
/// The JSON property names are a published contract: the reusable release workflow parses
/// <c>verdict</c>, <c>exitCode</c> and <c>releaseMode</c>. Renaming or removing a property is a
/// breaking change to every consumer pinned to a workflow ref, which is why the shape is pinned by a
/// golden file in the test suite.
/// </remarks>
sealed record ReleaseExplanation
{
	[JsonPropertyName("verdict")]
	public string Verdict { get; init; } = string.Empty;

	[JsonPropertyName("exitCode")]
	public int ExitCode { get; init; }

	/// <summary>
	/// The <c>Release__Mode</c> the workflow should set. <c>None</c> for a skip or a failure, so the
	/// publish and release modules skip without the workflow having to reason about it.
	/// </summary>
	[JsonPropertyName("releaseMode")]
	public string ReleaseMode { get; init; } = string.Empty;

	[JsonPropertyName("message")]
	public string Message { get; init; } = string.Empty;

	[JsonPropertyName("decidedByRule")]
	public string? DecidedByRule { get; init; }

	[JsonPropertyName("shortCircuitedAt")]
	public string? ShortCircuitedAt { get; init; }

	[JsonPropertyName("simulated")]
	public bool Simulated { get; init; }

	[JsonPropertyName("ref")]
	public string Ref { get; init; } = string.Empty;

	[JsonPropertyName("refSource")]
	public string RefSource { get; init; } = string.Empty;

	[JsonPropertyName("configuration")]
	public ConfigurationReport Configuration { get; init; } = new();

	[JsonPropertyName("version")]
	public VersionReport Version { get; init; } = new();

	[JsonPropertyName("policy")]
	public PolicyReport Policy { get; init; } = new();

	[JsonPropertyName("rules")]
	public IReadOnlyList<RuleReport> Rules { get; init; } = [];

	[JsonPropertyName("publication")]
	public PublicationReport Publication { get; init; } = new();

	[JsonPropertyName("warnings")]
	public IReadOnlyList<string> Warnings { get; init; } = [];

	static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		// Rule messages quote versions and refs with apostrophes. The default encoder escapes those
		// to ', which is valid JSON but makes the contract unreadable in a workflow log and in
		// the golden file that pins it. This output goes to a console and to jq, never into HTML.
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

sealed record ConfigurationReport
{
	[JsonPropertyName("resolvedPath")]
	public string? ResolvedPath { get; init; }

	[JsonPropertyName("source")]
	public string Source { get; init; } = "None";

	[JsonPropertyName("probeTrail")]
	public IReadOnlyList<ProbeReport> ProbeTrail { get; init; } = [];

	[JsonPropertyName("shadowedPaths")]
	public IReadOnlyList<string> ShadowedPaths { get; init; } = [];

	[JsonPropertyName("userConfigPath")]
	public string? UserConfigPath { get; init; }

	[JsonPropertyName("userConfigActive")]
	public bool UserConfigActive { get; init; }
}

sealed record ProbeReport
{
	[JsonPropertyName("relativePath")]
	public string RelativePath { get; init; } = string.Empty;

	[JsonPropertyName("fullPath")]
	public string FullPath { get; init; } = string.Empty;

	[JsonPropertyName("exists")]
	public bool Exists { get; init; }
}

sealed record VersionReport
{
	[JsonPropertyName("raw")]
	public string Raw { get; init; } = string.Empty;

	[JsonPropertyName("source")]
	public string Source { get; init; } = string.Empty;

	[JsonPropertyName("strictness")]
	public string Strictness { get; init; } = string.Empty;

	[JsonPropertyName("units")]
	public IReadOnlyList<UnitReport> Units { get; init; } = [];
}

sealed record UnitReport
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = string.Empty;

	[JsonPropertyName("version")]
	public string Version { get; init; } = string.Empty;

	[JsonPropertyName("isPrerelease")]
	public bool IsPrerelease { get; init; }

	[JsonPropertyName("prereleaseLabel")]
	public string? PrereleaseLabel { get; init; }

	[JsonPropertyName("line")]
	public string Line { get; init; } = string.Empty;

	[JsonPropertyName("channel")]
	public string Channel { get; init; } = string.Empty;

	[JsonPropertyName("tag")]
	public string Tag { get; init; } = string.Empty;

	[JsonPropertyName("source")]
	public string Source { get; init; } = string.Empty;
}

sealed record PolicyReport
{
	[JsonPropertyName("name")]
	public string Name { get; init; } = string.Empty;

	[JsonPropertyName("inheritanceChain")]
	public IReadOnlyList<string> InheritanceChain { get; init; } = [];

	[JsonPropertyName("resolvedRules")]
	public IReadOnlyList<string> ResolvedRules { get; init; } = [];

	[JsonPropertyName("stableRefs")]
	public IReadOnlyList<string> StableRefs { get; init; } = [];

	[JsonPropertyName("servicingRefs")]
	public IReadOnlyList<string> ServicingRefs { get; init; } = [];

	[JsonPropertyName("trunkRefs")]
	public IReadOnlyList<string> TrunkRefs { get; init; } = [];

	[JsonPropertyName("fourPartRefs")]
	public IReadOnlyList<string> FourPartRefs { get; init; } = [];

	[JsonPropertyName("allowFourPart")]
	public bool AllowFourPart { get; init; }
}

sealed record RuleReport
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = string.Empty;

	[JsonPropertyName("verdict")]
	public string Verdict { get; init; } = string.Empty;

	[JsonPropertyName("message")]
	public string Message { get; init; } = string.Empty;
}

sealed record PublicationReport
{
	[JsonPropertyName("mode")]
	public string Mode { get; init; } = string.Empty;

	[JsonPropertyName("publish")]
	public bool Publish { get; init; }

	[JsonPropertyName("githubRelease")]
	public bool GitHubRelease { get; init; }

	[JsonPropertyName("channel")]
	public string Channel { get; init; } = string.Empty;

	[JsonPropertyName("feedUrl")]
	public string FeedUrl { get; init; } = string.Empty;

	[JsonPropertyName("dryRun")]
	public bool DryRun { get; init; }
}
