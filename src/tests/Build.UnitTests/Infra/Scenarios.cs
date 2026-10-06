using System.Text.Json;
using System.Text.Json.Serialization;
using Purview.Build.Settings;

namespace Purview.Build.Infra;

/// <summary>
/// Loads the declarative scenario files that both the TUnit suites and the <c>just</c> CLI matrices
/// read, so a new case is appended to one JSON file rather than written as test code.
/// </summary>
public static class Scenarios
{
	static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	/// <summary>
	/// The repository's <c>src/tests/fixtures</c> directory, found by walking up to the repository
	/// root the same way the tool does.
	/// </summary>
	public static string FixturesDirectory { get; } = FindFixturesDirectory();

	public static EligibilityScenarioFile Eligibility { get; } =
		Load<EligibilityScenarioFile>("eligibility-scenarios.json");

	public static ConfigScenarioFile ConfigResolution { get; } =
		Load<ConfigScenarioFile>("config-resolution-scenarios.json");

	static T Load<T>(string fileName)
	{
		var path = Path.Combine(FixturesDirectory, fileName);
		var json = File.ReadAllText(path);

		return JsonSerializer.Deserialize<T>(json, Options)
			?? throw new InvalidOperationException($"'{path}' deserialised to null.");
	}

	static string FindFixturesDirectory()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			var candidate = Path.Combine(directory.FullName, "src", "tests", "fixtures");
			if (Directory.Exists(candidate))
				return candidate;
		}

		throw new InvalidOperationException(
			$"Could not locate src/tests/fixtures walking up from '{AppContext.BaseDirectory}'."
		);
	}
}

public sealed record EligibilityScenarioFile
{
	public IReadOnlyList<EligibilityScenario> Scenarios { get; init; } = [];

	public Dictionary<string, EligibilityPolicySettings> Policies { get; init; } = [];
}

public sealed record EligibilityScenario
{
	public string Name { get; init; } = string.Empty;

	public string Why { get; init; } = string.Empty;

	public string Version { get; init; } = string.Empty;

	[JsonPropertyName("ref")]
	public string Ref { get; init; } = string.Empty;

	public string Policy { get; init; } = string.Empty;

	public string Strictness { get; init; } = "NuGet";

	public IReadOnlyList<string> ExistingTags { get; init; } = [];

	/// <summary>Null means the feed was not consulted.</summary>
	public IReadOnlyList<string>? PublishedVersions { get; init; }

	public ExpectedEligibility Expected { get; init; } = new();

	public override string ToString() => Name;
}

public sealed record ExpectedEligibility
{
	public string Verdict { get; init; } = string.Empty;

	public string? RuleId { get; init; }

	public int ExitCode { get; init; }
}

public sealed record ConfigScenarioFile
{
	public IReadOnlyList<ConfigScenario> Scenarios { get; init; } = [];
}

public sealed record ConfigScenario
{
	public string Name { get; init; } = string.Empty;

	public string Why { get; init; } = string.Empty;

	public IReadOnlyList<string> Files { get; init; } = [];

	/// <summary>Relative path of a listed file to write as malformed JSON.</summary>
	public string? Malformed { get; init; }

	public string? Config { get; init; }

	public string? Env { get; init; }

	public bool UserConfig { get; init; }

	public bool IsLocal { get; init; } = true;

	public bool UserConfigFile { get; init; }

	public ExpectedConfig Expected { get; init; } = new();

	public override string ToString() => Name;
}

public sealed record ExpectedConfig
{
	public string? ResolvedPath { get; init; }

	public IReadOnlyList<string> ShadowWarnings { get; init; } = [];

	public int ExitCode { get; init; }

	public string? ErrorContains { get; init; }

	public string? NearMissContains { get; init; }

	public bool? UserConfigActive { get; init; }
}
