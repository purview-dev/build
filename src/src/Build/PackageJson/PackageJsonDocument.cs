using System.Text.Json;
using System.Text.Json.Nodes;

namespace Purview.Build.PackageJson;

/// <summary>
/// A parsed root <c>package.json</c>, read tolerantly.
/// </summary>
/// <remarks>
/// Only parsing can fail. Beyond that every accessor answers "absent" rather than throwing, so a
/// rule — not an exception — reports a missing or mistyped field, and one bad field never hides the
/// rest of the findings.
/// </remarks>
sealed class PackageJsonDocument
{
	PackageJsonDocument(JsonObject root, string path)
	{
		Root = root;
		Path = path;
	}

	/// <summary>The parsed document.</summary>
	public JsonObject Root { get; }

	/// <summary>Where the document was read from, for diagnostics.</summary>
	public string Path { get; }

	/// <summary>
	/// Reads and parses a <c>package.json</c>, capturing either failure as a message so a caller can
	/// report it as a finding instead of throwing.
	/// </summary>
	public static bool TryLoad(string path, out PackageJsonDocument? document, out string? error)
	{
		document = null;

		if (!File.Exists(path))
		{
			error = $"No package.json was found at '{path}'.";
			return false;
		}

		return TryParse(File.ReadAllText(path), path, out document, out error);
	}

	/// <summary>
	/// Parses <paramref name="json"/> as a <c>package.json</c>, capturing either failure as a
	/// message.
	/// </summary>
	public static bool TryParse(
		string json,
		string path,
		out PackageJsonDocument? document,
		out string? error
	)
	{
		document = null;
		error = null;

		try
		{
			if (JsonNode.Parse(json) is not JsonObject root)
			{
				error = $"'{path}' is not a JSON object.";
				return false;
			}

			document = new(root, path);
			return true;
		}
		catch (JsonException exception)
		{
			error = $"'{path}' is not valid JSON: {exception.Message}";
			return false;
		}
	}

	/// <summary>
	/// Parses a document that is known to be valid, for callers that own their input (the scenario
	/// fixtures).
	/// </summary>
	public static PackageJsonDocument FromJson(string json, string path = "package.json")
	{
		if (!TryParse(json, path, out var document, out var error))
			throw new InvalidOperationException(error);

		return document!;
	}

	/// <summary>Whether the document declares <c>"private": true</c>.</summary>
	public bool IsPrivate => GetBool("private");

	public bool Has(string name) => Root.ContainsKey(name);

	public bool HasNonEmpty(string name) =>
		Root.TryGetPropertyValue(name, out var node) && !IsBlank(node);

	public string? GetString(string name) =>
		Root.TryGetPropertyValue(name, out var node)
		&& node is JsonValue value
		&& value.TryGetValue<string>(out var text)
			? text
			: null;

	public bool GetBool(string name) =>
		Root.TryGetPropertyValue(name, out var node)
		&& node is JsonValue value
		&& value.TryGetValue<bool>(out var flag)
		&& flag;

	public JsonObject? GetObject(string name) =>
		Root.TryGetPropertyValue(name, out var node) && node is JsonObject obj ? obj : null;

	public string? GetNestedString(string name, string property) =>
		GetObject(name) is { } obj
		&& obj.TryGetPropertyValue(property, out var node)
		&& node is JsonValue value
		&& value.TryGetValue<string>(out var text)
			? text
			: null;

	/// <summary>The non-empty string members of an array-valued field, or empty when it is absent or of another shape.</summary>
	public IReadOnlyList<string> GetStrings(string name)
	{
		if (Root.TryGetPropertyValue(name, out var node) is false || node is not JsonArray array)
			return [];

		List<string> values = [with(array.Count)];
		foreach (var item in array)
		{
			if (
				item is JsonValue value
				&& value.TryGetValue<string>(out var text)
				&& !string.IsNullOrWhiteSpace(text)
			)
			{
				values.Add(text);
			}
		}

		return values;
	}

	/// <summary>
	/// True when a node carries no value: absent, <c>null</c>, or a whitespace-only string or empty
	/// array.
	/// </summary>
	public static bool IsBlank(JsonNode? node) =>
		node switch
		{
			null => true,
			JsonArray array => array.Count == 0,
			JsonValue value when value.TryGetValue<string>(out var text) =>
				string.IsNullOrWhiteSpace(text),
			_ => false,
		};
}
