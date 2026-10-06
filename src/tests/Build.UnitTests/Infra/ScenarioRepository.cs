using System.Text.Json;
using Purview.Build.Configuration;
using Purview.Build.Helpers;

namespace Purview.Build.Infra;

/// <summary>
/// A throwaway repository on disk for one configuration-resolution scenario: a root
/// <c>package.json</c>, the scenario's configuration files, and the environment the scenario
/// describes.
/// </summary>
/// <remarks>
/// Resolution is exercised against a real filesystem rather than a mock, because the behaviour under
/// test is entirely about what exists where.
/// </remarks>
sealed class ScenarioRepository : IDisposable
{
	readonly ConfigScenario _scenario;
	readonly string? _previousConfigVariable;
	readonly string? _previousUserConfigVariable;
	readonly Dictionary<string, string?> _previousBuildAgentVariables;
	readonly string? _previousXdgVariable;
	readonly string _userConfigHome;

	public ScenarioRepository(ConfigScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		_scenario = scenario;

		Root = Path.Combine(Path.GetTempPath(), "purview-build-config-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);

		// Every probe path is relative to the repository root, which the tool finds by walking up
		// to the nearest package.json.
		File.WriteAllText(
			Path.Combine(Root, "package.json"),
			/*lang=json,strict*/
			"""{ "name": "scenario", "version": "1.0.0" }"""
		);

		foreach (var relativePath in scenario.Files)
			WriteConfigFile(relativePath, malformed: relativePath == scenario.Malformed);

		_userConfigHome = Path.Combine(Root, ".user-config-home");

		_previousConfigVariable = Environment.GetEnvironmentVariable(ConfigFileLocator.EnvironmentVariableName);
		_previousUserConfigVariable = Environment.GetEnvironmentVariable(
			ConfigFileLocator.UserConfigEnvironmentVariableName
		);
		_previousBuildAgentVariables = ExecutionEnvironment.BuildAgentVariables.ToDictionary(
			variable => variable,
			Environment.GetEnvironmentVariable
		);
		_previousXdgVariable = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

		ApplyEnvironment();
	}

	public string Root { get; }

	public string FullPath(string relativePath) =>
		Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

	/// <summary>
	/// Resolves configuration the way the tool does, capturing a failure rather than throwing so a
	/// scenario can assert on an expected error.
	/// </summary>
	public ResolutionOutcome Resolve()
	{
		try
		{
			var resolution = ConfigFileLocator.Resolve(
				Root,
				_scenario.Config is null ? null : FullPath(_scenario.Config),
				_scenario.UserConfig
			);

			if (resolution.Path is not null)
				JsonConfigFile.Validate(resolution.Path);

			return new(resolution, null);
		}
		catch (InvalidOperationException exception)
		{
			return new(null, exception);
		}
	}

	/// <summary>
	/// Reads <c>Build:Solution</c> out of the resolved file, to prove relative-path anchoring.
	/// </summary>
	public string ReadSolution(string configPath)
	{
		using var document = JsonDocument.Parse(File.ReadAllText(configPath));

		return document.RootElement.GetProperty("Build").GetProperty("Solution").GetString()!;
	}

	void WriteConfigFile(string relativePath, bool malformed)
	{
		var path = FullPath(relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		if (malformed)
		{
			// A truncated object: valid-looking enough to be selected, invalid enough to fail parsing.
			File.WriteAllText(path, "{ \"Build\": { \"Solution\": \"src/Product.slnx\" ");
			return;
		}

		// The marker is the relative path, so a test can tell WHICH file was loaded, and
		// Build:Solution is identical everywhere so anchoring can be asserted.
		var json = $$"""
			{
				"Build": { "Solution": "src/Product.slnx" },
				"$marker": "{{relativePath}}"
			}
			""";

		File.WriteAllText(path, json);
	}

	void ApplyEnvironment()
	{
		Environment.SetEnvironmentVariable(
			ConfigFileLocator.EnvironmentVariableName,
			_scenario.Env is null ? null : FullPath(_scenario.Env)
		);

		// The opt-in flag is passed explicitly to Resolve, so the variable is always cleared here to
		// keep the two paths independent.
		Environment.SetEnvironmentVariable(ConfigFileLocator.UserConfigEnvironmentVariableName, null);

		// Locality decides whether user configuration applies at all. Every known build-agent
		// variable must be neutralised, not just GITHUB_ACTIONS: a CI runner also sets CI, and
		// leaving it set makes a "local" scenario look like a build agent.
		foreach (var variable in ExecutionEnvironment.BuildAgentVariables)
			Environment.SetEnvironmentVariable(variable, _scenario.IsLocal ? null : "true");

		if (_scenario.UserConfigFile)
		{
			var userConfig = Path.Combine(_userConfigHome, "purview-build", "purview-build.json");
			Directory.CreateDirectory(Path.GetDirectoryName(userConfig)!);
			File.WriteAllText(
				userConfig,
				/*lang=json,strict*/
				"""{ "PublishLocalNuGet": { "LocalFeedPath": "/tmp/scenario-feed" } }"""
			);

			Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _userConfigHome);
		}
		else
		{
			Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _userConfigHome);
		}
	}

	public void Dispose()
	{
		Environment.SetEnvironmentVariable(ConfigFileLocator.EnvironmentVariableName, _previousConfigVariable);
		Environment.SetEnvironmentVariable(
			ConfigFileLocator.UserConfigEnvironmentVariableName,
			_previousUserConfigVariable
		);
		foreach (var (variable, value) in _previousBuildAgentVariables)
			Environment.SetEnvironmentVariable(variable, value);
		Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousXdgVariable);

		if (Directory.Exists(Root))
			Directory.Delete(Root, recursive: true);
	}
}

/// <summary>
/// A resolution attempt: either a resolution, or the failure it produced.
/// </summary>
sealed record ResolutionOutcome(ConfigResolution? Resolution, InvalidOperationException? Error);
