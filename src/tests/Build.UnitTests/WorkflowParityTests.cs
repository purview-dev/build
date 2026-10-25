using System.Reflection;
using System.Text.RegularExpressions;
using Purview.Build.Configuration;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// Holds the YAML surface honest: every configuration key the reusable workflows and the composite
/// action forward must exist on a settings class, and every optional input must be forwarded only
/// when the caller actually provided it.
/// </summary>
/// <remarks>
/// The second half guards the empty-env-var hazard fixed in commit <c>4d72bf7</c>: environment
/// variables outrank <c>purview-build.json</c>, so forwarding an unset input as the empty string
/// silently erases a consuming repository's configured value. A typo'd key, meanwhile, binds to
/// nothing at all and fails silently — nothing in the pipeline warns about an unrecognised
/// environment variable.
/// </remarks>
public partial class WorkflowParityTests
{
	/// <summary>
	/// The settings sections an environment key may address, by configuration section name.
	/// </summary>
	/// <remarks>
	/// Compared case-insensitively, because .NET configuration keys are: the environment provider
	/// maps <c>NUGET__APIKEY</c> onto <c>NuGet:APIKey</c> regardless of casing, and the historical
	/// secret name consuming repositories use differs in case from the section name.
	/// </remarks>
	static readonly Dictionary<string, Type> Sections = new(StringComparer.OrdinalIgnoreCase)
	{
		[BuildSettings.SectionName] = typeof(BuildSettings),
		[PackValidationSettings.SectionName] = typeof(PackValidationSettings),
		[NuGetSettings.SectionName] = typeof(NuGetSettings),
		[PublishLocalNuGetSettings.SectionName] = typeof(PublishLocalNuGetSettings),
		[GitHubSettings.SectionName] = typeof(GitHubSettings),
		[ReleaseSettings.SectionName] = typeof(ReleaseSettings),
		[VersionSettings.SectionName] = typeof(VersionSettings),
	};

	/// <summary>
	/// Environment variables the tool reads directly rather than through the configuration binder.
	/// </summary>
	static readonly string[] ToolEnvironmentVariables =
	[
		ConfigFileLocator.EnvironmentVariableName,
		ConfigFileLocator.UserConfigEnvironmentVariableName,
		"GITHUB_TOKEN",
		"NUGET_APIKEY",
		"NUGET_API_KEY",
		"LOCAL_NUGET_FEED_PATH",
		"PURVIEW_BUILD_STACKTRACE",
		"MODULAR_PIPELINES_DIRECTORY",
	];

	static readonly string[] WorkflowFiles =
	[
		".github/workflows/purview-build.yml",
		".github/workflows/purview-release.yml",
		".github/workflows/release.yml",
		".github/actions/purview-build/action.yml",
	];

	public static IEnumerable<Func<string>> Workflows() =>
		WorkflowFiles.Select<string, Func<string>>(file => () => file);

	[Test]
	[MethodDataSource(nameof(Workflows))]
	public async Task Workflow_ForwardsOnlyKeysThatExistInTheSettingsClasses(string workflowFile)
	{
		// Arrange
		var content = ReadWorkflow(workflowFile);

		// Act
		var keys = SettingsKeys(content);

		// Assert
		foreach (var key in keys)
		{
			await Assert
				.That(Resolves(key))
				.IsTrue()
				.Because(
					$"{workflowFile} forwards '{key}', which binds to no settings property. An "
						+ "unrecognised environment variable is silently ignored by the configuration "
						+ "binder, so the setting would never take effect."
				);
		}
	}

	[Test]
	[MethodDataSource(nameof(Workflows))]
	public async Task Workflow_ForwardsEveryOptionalInputOnlyWhenNonEmpty(string workflowFile)
	{
		// Arrange
		var content = ReadWorkflow(workflowFile);
		var optionalInputs = OptionalInputs(content);

		// Act
		var directlyMapped = optionalInputs.Where(input => DirectEnvMapping(input).IsMatch(content)).ToList();

		// Assert
		await Assert
			.That(directlyMapped)
			.IsEmpty()
			.Because(
				$"{workflowFile} maps optional input(s) {string.Join(", ", directlyMapped)} straight into "
					+ "env:. An input the caller omitted is forwarded as the empty string, which overrides "
					+ "the repository's purview-build.json because env vars win. Guard it with "
					+ "`if [ -n \"${{ inputs.<name> }}\" ]` instead."
			);
	}

	[Test]
	[MethodDataSource(nameof(Workflows))]
	public async Task Workflow_GuardsEveryOptionalInputItForwards(string workflowFile)
	{
		// Arrange
		var content = ReadWorkflow(workflowFile);
		var optionalInputs = OptionalInputs(content);

		// Act
		// An optional input that is referenced outside its own declaration must be referenced
		// inside a non-empty guard.
		var referenced = optionalInputs.Where(input => InputReference(input).Count(content) > 0).ToList();

		// Assert
		foreach (var input in referenced)
		{
			await Assert
				.That(
					NonEmptyGuard(input).IsMatch(content)
						|| EnvironmentGuard(input).IsMatch(content)
						|| ConditionGuard(input).IsMatch(content)
				)
				.IsTrue()
				.Because(
					$"{workflowFile} references optional input '{input}' without guarding it against "
						+ $"being empty. Map it to {EnvironmentName(input)} in `env:` and test that variable, "
						+ $"guard a step with `if: ... inputs.{input} == ''`, or guard it inline with "
						+ $"`if [ -n \"${{{{ inputs.{input} }}}}\" ]`."
				);
		}
	}

	[Test]
	public async Task ReleaseWorkflow_DelegatesTheEligibilityDecisionToTheTool()
	{
		// Arrange
		// The gate used to be bash: read package.json, `git rev-parse` the tag, set should_release.
		// It now has to be the tool's evaluation, or the logic is untestable again.
		var content = ReadWorkflow(".github/workflows/purview-release.yml");

		// Act
		var usesReleaseExplain = content.Contains("release-explain --format=json", StringComparison.Ordinal);

		// Assert
		await Assert.That(usesReleaseExplain).IsTrue();
		await Assert
			.That(content)
			.DoesNotContain("git rev-parse")
			.Because("tag arithmetic in YAML cannot be unit-tested or run locally.");
		await Assert
			.That(content)
			.Contains("steps.eligibility.outputs.release_mode")
			.Because("the workflow must set Release__Mode from the evaluated decision.");
	}

	[Test]
	public async Task ReleaseWorkflow_KeepsTheDeprecatedReleaseBranchInput()
	{
		// Arrange
		// All seven consuming repositories still pass release-branch. Removing it breaks them all.
		var content = ReadWorkflow(".github/workflows/purview-release.yml");

		// Act
		var declared = content.Contains("release-branch:", StringComparison.Ordinal);

		// Assert
		await Assert.That(declared).IsTrue();
	}

	[Test]
	public async Task ReleaseWorkflow_DefinesNoConcurrencyGroup()
	{
		// Arrange
		// A shared group between a caller and the reusable workflow it calls makes GitHub cancel the
		// run as a deadlock. Callers own release serialization.
		var content = ReadWorkflow(".github/workflows/purview-release.yml");

		// Act
		var hasConcurrency = ConcurrencyBlock().IsMatch(content);

		// Assert
		await Assert.That(hasConcurrency).IsFalse();
	}

	[Test]
	public async Task ReleaseWorkflow_DeclaresEveryNewInput()
	{
		// Arrange
		var content = ReadWorkflow(".github/workflows/purview-release.yml");
		string[] expected = ["config-path", "eligibility-policy", "release-channel", "version-source"];

		// Act
		var declared = expected.Where(input => content.Contains($"{input}:", StringComparison.Ordinal));

		// Assert
		await Assert.That(declared).IsEquivalentTo(expected);
	}

	[Test]
	public async Task ParityChecks_ActuallyFindSomethingToCheck()
	{
		// Arrange
		// Guards the guards: a regex that silently matches nothing would make every parity
		// assertion above pass vacuously.
		var content = ReadWorkflow(".github/workflows/purview-release.yml");

		// Act
		var optionalInputs = OptionalInputs(content);
		var settingsKeys = SettingsKeys(content);

		// Assert
		await Assert.That(optionalInputs).Contains("test-filter");
		await Assert.That(optionalInputs).Contains("config-path");
		await Assert.That(optionalInputs).Contains("eligibility-policy");
		await Assert
			.That(optionalInputs)
			.DoesNotContain("release-mode")
			.Because("release-mode declares a default, so it is never forwarded empty.");
		await Assert.That(settingsKeys).Contains("Release__Mode");
		await Assert.That(settingsKeys).Contains("Build__RunTests");
		await Assert.That(settingsKeys).Contains("Release__Eligibility__Policy");
	}

	static string ReadWorkflow(string relativePath)
	{
		var root = RepositoryRoot();
		var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

		return File.ReadAllText(path);
	}

	static string RepositoryRoot()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			if (File.Exists(Path.Combine(directory.FullName, "package.json")))
				return directory.FullName;
		}

		throw new InvalidOperationException($"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
	}

	/// <summary>
	/// Every <c>Section__Key</c> style environment variable the workflow sets, excluding the tool's
	/// own direct-read variables.
	/// </summary>
	/// <remarks>
	/// Keys reach the environment two ways: written literally as <c>Key: value</c> under
	/// <c>env:</c>, or handed to the <c>forward</c> helper, which appends them to
	/// <c>$GITHUB_ENV</c>. Both are collected, or moving a key to the helper would quietly remove
	/// it from the parity check that proves it binds to a real settings property.
	/// </remarks>
	static IReadOnlyList<string> SettingsKeys(string content) =>
		[
			.. EnvironmentKey()
				.Matches(content)
				.Select(match => match.Groups["key"].Value)
				.Concat(ForwardedKey().Matches(content).Select(match => match.Groups["key"].Value))
				.Where(key => !ToolEnvironmentVariables.Contains(key, StringComparer.Ordinal))
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

	/// <summary>
	/// Inputs declared without a <c>default:</c>, which are therefore empty when the caller omits
	/// them.
	/// </summary>
	static IReadOnlyList<string> OptionalInputs(string content) =>
		[
			.. InputDeclaration()
				.Matches(content)
				.Where(match => !match.Groups["body"].Value.Contains("default:", StringComparison.Ordinal))
				.Select(match => match.Groups["name"].Value)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

	static bool Resolves(string environmentKey)
	{
		var segments = environmentKey.Split("__", StringSplitOptions.RemoveEmptyEntries);

		if (segments.Length < 2 || !Sections.TryGetValue(segments[0], out var current))
			return false;

		foreach (var segment in segments.Skip(1))
		{
			var property = current
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.FirstOrDefault(candidate =>
					string.Equals(candidate.Name, segment, StringComparison.OrdinalIgnoreCase)
				);

			if (property is null)
				return false;

			current = property.PropertyType;
		}

		return true;
	}

	static Regex DirectEnvMapping(string input) =>
		new(
			@"^\s{2,}[A-Za-z_][A-Za-z0-9_]*__[A-Za-z0-9_]+\s*:\s*\$\{\{\s*inputs\." + Regex.Escape(input) + @"\s*\}\}",
			RegexOptions.Multiline,
			TimeSpan.FromSeconds(5)
		);

	static Regex InputReference(string input) =>
		new(@"\$\{\{\s*inputs\." + Regex.Escape(input) + @"\s*\}\}", RegexOptions.None, TimeSpan.FromSeconds(5));

	static Regex NonEmptyGuard(string input) =>
		new(
			@"if\s+\[\s+-n\s+""\$\{\{\s*inputs\." + Regex.Escape(input) + @"\s*\}\}""\s+\]",
			RegexOptions.None,
			TimeSpan.FromSeconds(5)
		);

	/// <summary>
	/// The environment variable an input is mapped to, by convention: <c>build-version</c> becomes
	/// <c>INPUT_BUILD_VERSION</c>.
	/// </summary>
	static string EnvironmentName(string input) => "INPUT_" + input.Replace('-', '_').ToUpperInvariant();

	/// <summary>
	/// The preferred guard: the input is mapped into <c>env:</c> and the resulting shell variable
	/// is tested, rather than interpolated into the script text.
	/// </summary>
	/// <remarks>
	/// Interpolating <c>${{ inputs.x }}</c> inside a <c>run:</c> body substitutes a caller-supplied
	/// value into the shell source before bash parses it, so an inline guard protects the
	/// configuration while leaving the script injectable. Guarding the environment variable gives
	/// the same empty-value protection without that.
	/// </remarks>
	/// <summary>
	/// A step-level condition that tests the input for emptiness, such as a step that fails the
	/// run when a required-in-context input was not supplied.
	/// </summary>
	/// <remarks>
	/// This covers an input that never reaches a shell or an environment variable at all - one
	/// passed straight to another action's <c>with:</c> - where the hazard is not an overridden
	/// configuration value but a silently useless run. Failing loudly is a stronger guard than
	/// skipping, so it counts.
	/// </remarks>
	static Regex ConditionGuard(string input) =>
		new(@"if:.*inputs\." + Regex.Escape(input) + @"\s*(?:==|!=)\s*''", RegexOptions.None, TimeSpan.FromSeconds(5));

	static Regex EnvironmentGuard(string input)
	{
		var name = Regex.Escape(EnvironmentName(input));

		return new(
			// `[ -n "$X" ]`, `[ -z "$X" ] ||`, or handed to the forward helper, which guards it.
			$@"\[\s+-[nz]\s+""\${name}""\s+\]|forward\s+\S+\s+""\${name}""",
			RegexOptions.None,
			TimeSpan.FromSeconds(5)
		);
	}

	[GeneratedRegex(@"(?<key>[A-Za-z_][A-Za-z0-9_]*__[A-Za-z0-9_]+(?:__[A-Za-z0-9_]+)*)\s*[:=]")]
	private static partial Regex EnvironmentKey();

	[GeneratedRegex(@"forward\s+(?<key>[A-Za-z_][A-Za-z0-9_]*__[A-Za-z0-9_]+(?:__[A-Za-z0-9_]+)*)\s")]
	private static partial Regex ForwardedKey();

	[GeneratedRegex(
		@"^      (?<name>[a-z][a-z0-9-]*):\s*$(?<body>(?:\n(?:        .*|\s*))*?)(?=^      [a-z]|^    [a-z]|^[a-z])",
		RegexOptions.Multiline
	)]
	private static partial Regex InputDeclaration();

	[GeneratedRegex(@"^concurrency:", RegexOptions.Multiline)]
	private static partial Regex ConcurrencyBlock();
}
