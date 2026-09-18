using System.Text.Json;

namespace Purview.Build.Helpers;

static class WebScripts
{
	public static IReadOnlyDictionary<string, string> ReadScripts(string repositoryRoot)
	{
		var packageJsonPath = Path.Combine(repositoryRoot, "package.json");
		if (!File.Exists(packageJsonPath))
			return new Dictionary<string, string>();

		using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
		if (!document.RootElement.TryGetProperty("scripts", out var scripts))
			return new Dictionary<string, string>();

		Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
		foreach (var property in scripts.EnumerateObject())
			result[property.Name] = property.Value.GetString() ?? string.Empty;

		return result;
	}

	public static bool HasScript(string repositoryRoot, string scriptName) =>
		ReadScripts(repositoryRoot).ContainsKey(scriptName);

	/// <summary>
	/// Reads the <c>name</c> field from the root package.json (used to name the Web release artifact).
	/// </summary>
	public static string ReadPackageName(string repositoryRoot)
	{
		var packageJsonPath = Path.Combine(repositoryRoot, "package.json");
		if (!File.Exists(packageJsonPath))
			throw new FileNotFoundException($"Could not find package.json at {packageJsonPath}");

		using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
		return document.RootElement.GetProperty("name").GetString() ?? "package";
	}

	/// <summary>
	/// Extracts the package.json script name from a default <c>bun run &lt;script&gt;</c> command,
	/// or null when the command is not a bare script invocation.
	/// </summary>
	public static string? GetScriptName(string command)
	{
		const string prefix = "bun run ";
		if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return null;

		var remainder = command[prefix.Length..].Trim();
		return remainder.Length > 0 && !remainder.Contains(' ', StringComparison.Ordinal)
			? remainder
			: null;
	}
}