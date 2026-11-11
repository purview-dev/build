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

	public static PackageJsonScenarioFile PackageJson { get; } =
		Load<PackageJsonScenarioFile>("package-json-scenarios.json");

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

	/// <summary>
	/// Relative path of a listed file to write as valid JSON that violates the configuration schema.
	/// </summary>
	public string? Invalid { get; init; }

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

public sealed record PackageJsonScenarioFile
{
	public IReadOnlyList<PackageJsonScenario> Scenarios { get; init; } = [];
}

/// <summary>
/// One <c>package.json</c> validation case: the file's contents, the configuration the run resolves,
/// and the findings the run must produce.
/// </summary>
/// <remarks>
/// Both the TUnit suite and the <c>just package-matrix</c> recipe build the same settings and write
/// the same file, so a mismatch means the shipped CLI and the in-process runner disagree.
/// </remarks>
public sealed record PackageJsonScenario
{
	public string Name { get; init; } = string.Empty;

	public string Why { get; init; } = string.Empty;

	public string Profile { get; init; } = "Auto";

	public string ProjectType { get; init; } = "DotNet";

	public bool PacksNuGetPackages { get; init; }

	public bool RunTests { get; init; } = true;

	public bool RunLint { get; init; } = true;

	public IReadOnlyList<string> AdditionalRequiredFields { get; init; } = [];

	public IReadOnlyList<string> DisabledRules { get; init; } = [];

	public bool TreatWarningsAsErrors { get; init; }

	/// <summary>The file's contents, used verbatim (an array is deliberately unreadable).</summary>
	public JsonElement PackageJson { get; init; }

	public ExpectedPackageJson Expected { get; init; } = new();

	public override string ToString() => Name;
}

/// <summary>
/// What a scenario must produce. The rule lists are rule IDs, not messages, so rewording a finding
/// is not a fixture change.
/// </summary>
public sealed record ExpectedPackageJson
{
	public bool Succeeded { get; init; }

	public int ExitCode { get; init; }

	public string Profile { get; init; } = string.Empty;

	public IReadOnlyList<string> Errors { get; init; } = [];

	public IReadOnlyList<string> Warnings { get; init; } = [];
}
