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
	/// The release implementation. It is a composite action rather than a reusable workflow because
	/// Trusted Publishing cannot be satisfied from a called workflow — see the test below.
	/// </summary>
	const string ReleaseAction = "actions/purview-release/action.yml";

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
		"actions/purview-release/action.yml",
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
						|| WithMapping(input).IsMatch(content)
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
	public async Task ReleaseAction_DelegatesTheEligibilityDecisionToTheTool()
	{
		// Arrange
		// The gate used to be bash: read package.json, `git rev-parse` the tag, set should_release.
		// It now has to be the tool's evaluation, or the logic is untestable again.
		var content = ReadWorkflow(ReleaseAction);

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
			.Because("the release must set Release__Mode from the evaluated decision.");
	}

	[Test]
	public async Task ReleaseAction_IsTheOnlyPlaceThatMintsATrustedPublishingKey()
	{
		// Arrange
		// nuget.org validates the OIDC `job_workflow_ref` claim against the registered policy. Inside a
		// reusable workflow that claim names this repository, never the caller, so the exchange fails
		// with "No matching trust policy" — NuGet/NuGetGallery#11000, still open. A composite action
		// runs in the caller's job, which keeps the claim on the caller's own workflow file.
		var action = ReadWorkflow(ReleaseAction);
		var workflow = ReadWorkflow(".github/workflows/purview-release.yml");

		// Act
		var mintsInAction = action.Contains("NuGet/login@", StringComparison.Ordinal);
		var mintsInWorkflow = workflow.Contains("NuGet/login@", StringComparison.Ordinal);

		// Assert
		await Assert
			.That(mintsInAction)
			.IsTrue()
			.Because("the composite action is the only entry point that can satisfy a Trusted Publishing policy.");
		await Assert
			.That(mintsInWorkflow)
			.IsFalse()
			.Because(
				"a reusable workflow's OIDC claim names the called workflow, so a key minted there can "
					+ "never match a policy registered for the caller (NuGet/NuGetGallery#11000). Move the "
					+ "login into the composite action instead of advertising an input that always 401s."
			);
		await Assert
			.That(workflow)
			.DoesNotContain("trusted-publishing:")
			.Because("offering the input on the reusable workflow invites a migration that cannot work.");
		await Assert.That(workflow).DoesNotContain("nuget-user:");
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
	public async Task ReleaseWorkflow_DeclaresNoPermissionsBlock()
	{
		// Arrange
		// GitHub validates a reusable workflow's permissions against the caller statically, before an
		// input is read, and a reusable workflow can never elevate its caller — so a block here grants
		// nothing and can only fail runs. `id-token` is the trap: it is `none` by default whatever the
		// repository's default-token setting, so declaring `id-token: write` for the optional
		// Trusted Publishing path failed every caller at parse time.
		var content = ReadWorkflow(".github/workflows/purview-release.yml");

		// Act
		var hasPermissions = PermissionsBlock().IsMatch(content);

		// Assert
		await Assert
			.That(hasPermissions)
			.IsFalse()
			.Because(
				"a workflow-level permissions block in purview-release.yml makes every caller that "
					+ "does not grant the same scopes fail validation, including callers of optional "
					+ "features they never enabled. Callers own their token."
			);
	}

	[Test]
	[MethodDataSource(nameof(DocumentedCallerPermissions))]
	public async Task Documentation_CallerPermissionBlockGrantsEveryScopeTheReleaseNeeds(
		string file,
		int line,
		string block
	)
	{
		// Arrange
		// Every permissions block in the documentation is a release caller's: the build caller needs
		// none. A partial block is the failure mode this guards — one example granted contents and
		// id-token but not packages, so a repository that followed it published to nuget.org and then
		// failed on the GitHub Packages push.
		string[] required = ["contents: write", "packages: write"];

		// A caller that grants `packages: write` is publishing to NuGet, and every NuGet publisher now
		// mints its nuget.org key from the job's OIDC token. Scoping the requirement to that block
		// keeps a GitHubRelease-only caller's snippet minimal: it needs neither scope.
		if (block.Contains("packages: write", StringComparison.Ordinal))
			required = [.. required, "id-token: write"];

		// Act
		var missing = required.Where(scope => !block.Contains(scope, StringComparison.Ordinal)).ToList();

		// Assert
		await Assert
			.That(missing)
			.IsEmpty()
			.Because(
				$"the caller permissions block at {file}:{line} omits {string.Join(", ", missing)}. A "
					+ "documented release caller must grant contents: write to tag and create the "
					+ "release, packages: write for the GitHub Packages mirror, and id-token: write for "
					+ "the Trusted Publishing exchange that replaced the stored nuget.org key."
			);
	}

	public static IEnumerable<Func<(string, int, string)>> DocumentedCallerPermissions()
	{
		var root = RepositoryRoot();

		string[] files =
		[
			.. Directory.EnumerateFiles(Path.Combine(root, "docs", "wiki"), "*.md", SearchOption.AllDirectories),
			.. Directory.EnumerateFiles(Path.Combine(root, "skills"), "SKILL.md", SearchOption.AllDirectories),
			Path.Combine(root, "README.md"),
		];

		foreach (var file in files)
		{
			var content = File.ReadAllText(file);
			var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

			foreach (Match match in DocumentedPermissions().Matches(content))
			{
				var block = match.Groups["body"].Value;

				// Read-only blocks belong to a build caller, which needs nothing from this rule.
				if (!block.Contains(": write", StringComparison.Ordinal))
					continue;

				var line = content.Take(match.Index).Count(character => character == '\n') + 1;

				yield return () => (relative, line, block);
			}
		}
	}

	[Test]
	public async Task DocumentationChecks_ActuallyFindSomethingToCheck()
	{
		// Arrange
		// Guards the guard: a regex matching no documented block would make the parity test above pass
		// without reading anything.
		var blocks = DocumentedCallerPermissions().Select(source => source()).ToList();

		// Act
		var files = blocks.Select(block => block.Item1).Distinct(StringComparer.Ordinal).ToList();

		// Assert
		await Assert.That(blocks).IsNotEmpty();
		await Assert.That(files).Contains("README.md");
	}

	[Test]
	public async Task ReleaseEntryPoints_DeclareEveryNewInput()
	{
		// Arrange
		string[] expected = ["config-path", "eligibility-policy", "release-channel", "version-source"];

		// Act & Assert
		// The delegator and the implementation must agree: an input the workflow accepts but the
		// action never declares is silently dropped.
		foreach (var file in new[] { ".github/workflows/purview-release.yml", ReleaseAction })
		{
			var content = ReadWorkflow(file);
			var declared = expected.Where(input => content.Contains($"{input}:", StringComparison.Ordinal));

			await Assert.That(declared).IsEquivalentTo(expected).Because($"{file} must declare every release input.");
		}
	}

	[Test]
	public async Task ParityChecks_ActuallyFindSomethingToCheck()
	{
		// Arrange
		// Guards the guards: a regex that silently matches nothing would make every parity
		// assertion above pass vacuously. Optional inputs are declared by the delegator; the settings
		// keys are forwarded by the implementation.
		var content = ReadWorkflow(".github/workflows/purview-release.yml");
		var action = ReadWorkflow(ReleaseAction);

		// Act
		var optionalInputs = OptionalInputs(content);
		var settingsKeys = SettingsKeys(action);

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

	[Test]
	public async Task ReleaseAction_GuardsEveryOptionalInputItForwards()
	{
		// Arrange
		// The same hazard the workflow checks cover, in a different shape: a composite action declares
		// its inputs at two spaces of indentation instead of six, so OptionalInputs cannot see them.
		// An omitted input forwarded as the empty string still overrides the repository's
		// purview-build.json, because environment variables outrank the file.
		var content = ReadWorkflow(ReleaseAction);

		// Act
		// Only inputs that become an INPUT_* shell variable can do that; an input consumed directly in
		// an expression cannot reach the tool's configuration.
		var forwarded = ActionInputs(content)
			.Where(input => IsEmptyDefault(input.Default))
			.Select(input => input.Name)
			.Where(input => content.Contains(EnvironmentName(input), StringComparison.Ordinal))
			.ToList();

		var unguarded = forwarded.Where(input => !EnvironmentGuard(input).IsMatch(content)).ToList();

		// Assert
		await Assert.That(forwarded).IsNotEmpty().Because("the guard below would otherwise be vacuous.");
		await Assert
			.That(unguarded)
			.IsEmpty()
			.Because(
				$"{ReleaseAction} forwards optional input(s) {string.Join(", ", unguarded)} without "
					+ "guarding them against being empty. Apply them with "
					+ "`[ -z \"$INPUT_<NAME>\" ] || export ...` instead of exporting the empty string."
			);
	}

	[Test]
	public async Task ReleaseActionChecks_ActuallyFindSomethingToCheck()
	{
		// Arrange
		// Guards the guard: a malformed inputs block would make the test above pass without reading
		// anything.
		var inputs = ActionInputs(ReadWorkflow(ReleaseAction));

		// Act
		var optional = inputs.Where(input => IsEmptyDefault(input.Default)).Select(input => input.Name).ToList();

		// Assert
		await Assert.That(inputs).IsNotEmpty();
		await Assert.That(optional).Contains("config-path");
		await Assert.That(optional).Contains("test-filter");
		await Assert.That(optional).DoesNotContain("run-tests").Because("run-tests declares a default.");
		await Assert.That(inputs.Select(input => input.Name)).Contains("trusted-publishing");
	}

	/// <summary>
	/// The inputs a composite action declares, with the raw text of each <c>default:</c>.
	/// </summary>
	/// <remarks>
	/// The block is read from the top-level <c>inputs:</c> key to the next key at column zero, which is
	/// what stops <c>runs:</c> from being mistaken for an input.
	/// </remarks>
	static List<(string Name, string? Default)> ActionInputs(string content)
	{
		var block = ActionInputsBlock().Match(content);
		if (!block.Success)
			return [];

		List<(string, string?)> inputs = [];
		foreach (Match match in ActionInputDeclaration().Matches(block.Groups["body"].Value))
		{
			var declared = ActionInputDefault().Match(match.Groups["body"].Value);
			inputs.Add((match.Groups["name"].Value, declared.Success ? declared.Groups["value"].Value.Trim() : null));
		}

		return inputs;
	}

	/// <summary>
	/// True when an input's default is absent or an empty string, which makes it optional in practice.
	/// </summary>
	static bool IsEmptyDefault(string? value) => value is null || value.Trim('"', '\'').Length == 0;

	[GeneratedRegex(@"^inputs:[ \t]*$(?<body>(?:\n(?![a-z])[^\n]*)*)", RegexOptions.Multiline)]
	private static partial Regex ActionInputsBlock();

	[GeneratedRegex(
		@"^  (?<name>[a-z][a-z0-9-]*):[ \t]*$(?<body>(?:\n(?![ ]{0,2}[a-z])[^\n]*)*)",
		RegexOptions.Multiline
	)]
	private static partial Regex ActionInputDeclaration();

	[GeneratedRegex(@"^[ \t]+default:[ \t]*(?<value>.*)$", RegexOptions.Multiline)]
	private static partial Regex ActionInputDefault();

	[Test]
	public async Task ReleaseAction_BlanksTheLegacyKeyWhenTrustedPublishingIsOn()
	{
		// Arrange
		// GitHub evaluates `''` as falsy, so the obvious `trusted && '' || legacyKey` yields the legacy
		// key, not the empty string — the tool prefers NUGET_APIKEY, so a leftover secret would keep
		// publishing the old way while the configuration looked migrated. The choice is made in bash
		// instead.
		var content = ReadWorkflow(ReleaseAction);

		// Act
		var blanksLegacy = content.Contains("export NUGET_APIKEY=\"\"", StringComparison.Ordinal);
		var usesFalsyExpression = content.Contains("&& '' ||", StringComparison.Ordinal);

		// Assert
		await Assert.That(blanksLegacy).IsTrue().Because("a leftover long-lived key must never shadow the minted one.");
		await Assert
			.That(usesFalsyExpression)
			.IsFalse()
			.Because(
				"`condition && '' || value` returns `value`, because the empty string is falsy in a "
					+ "GitHub expression. Select the credential in the shell script instead."
			);
	}

	[Test]
	public async Task CompositeActions_UseOnlyContextsGitHubAllowsThem()
	{
		// Arrange
		// A composite action has no `vars` or `secrets` context. Referencing either makes GitHub reject
		// the manifest — "Unrecognized named-value: 'vars'" — while loading the action, before any step
		// runs. actionlint only parses workflow files, so nothing else catches it, and the failure only
		// appears when a consumer's release actually loads the action.
		var root = RepositoryRoot();
		List<string> actions =
		[
			.. Directory.EnumerateFiles(Path.Combine(root, "actions"), "action.yml", SearchOption.AllDirectories),
			.. Directory.EnumerateFiles(
				Path.Combine(root, ".github", "actions"),
				"action.yml",
				SearchOption.AllDirectories
			),
		];

		// Act
		var offenders = actions
			.Where(path => ForbiddenActionContext().IsMatch(File.ReadAllText(path)))
			.Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
			.ToList();

		// Assert
		await Assert.That(actions).IsNotEmpty().Because("the guard below would otherwise be vacuous.");
		await Assert
			.That(offenders)
			.IsEmpty()
			.Because(
				$"{string.Join(", ", offenders)} reference `vars` or `secrets` from a composite action. "
					+ "GitHub has no such context there and fails to load the manifest. Pass the value in as "
					+ "an input from the calling workflow."
			);
	}

	[GeneratedRegex(@"\$\{\{[^}]*\b(?:vars|secrets)\.")]
	private static partial Regex ForbiddenActionContext();

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

	/// <summary>
	/// An optional input handed straight to another action's <c>with:</c>.
	/// </summary>
	/// <remarks>
	/// An action input is not an environment variable, so an empty value cannot override a consuming
	/// repository's configuration: the called action owns that decision and applies its own non-empty
	/// guards. This is the shape a delegating workflow forwards through.
	/// </remarks>
	static Regex WithMapping(string input) =>
		new(
			@"^ {2,}[a-z][a-z0-9-]*:[ \t]*\$\{\{\s*inputs\." + Regex.Escape(input) + @"\s*\}\}[ \t]*$",
			RegexOptions.Multiline,
			TimeSpan.FromSeconds(5)
		);

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

	/// <summary>
	/// A <c>permissions:</c> key at any level — workflow or job. Both are validated against the
	/// caller, so both break callers in the same way.
	/// </summary>
	[GeneratedRegex(@"^[ \t]*permissions:", RegexOptions.Multiline)]
	private static partial Regex PermissionsBlock();

	/// <summary>
	/// A documented <c>permissions:</c> block, in either the block form used inside fenced YAML or
	/// the inline flow form used in prose.
	/// </summary>
	[GeneratedRegex(
		@"permissions:[ \t]*(?:\{(?<body>[^}]*)\}|\r?\n(?<body>(?:[ \t]*#[^\n]*\r?\n|[ \t]+[a-z][a-z-]*:[ \t]*[A-Za-z]+[ \t]*\r?\n?)+))"
	)]
	private static partial Regex DocumentedPermissions();
}
