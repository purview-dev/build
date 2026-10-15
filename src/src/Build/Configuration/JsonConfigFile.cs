using Json.Schema;
using Purview.Build.Schema;
using System.Text.Json;

namespace Purview.Build.Configuration;

/// <summary>
/// Parse-checks and schema-validates a configuration file before it reaches the configuration binder.
/// </summary>
/// <remarks>
/// The JSON configuration provider reports a malformed file as a bare deserialisation failure with
/// no path, which is useless when six probe locations are possible; it also silently ignores a key
/// it cannot bind, so a typo behaves exactly like an unset setting. Parsing first names the file and
/// the position, and validating against the schema the tool ships turns "silently ignored" into a
/// failure that names the offending key.
/// </remarks>
static class JsonConfigFile
{
	const string SchemaResourceName = "Purview.Build.ConfigSchema.json";

	static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

	/// <exception cref="InvalidOperationException">
	/// The file exists but is not valid JSON, or does not match the configuration schema.
	/// </exception>
	public static void Validate(string path)
	{
		if (!File.Exists(path))
			return;

		using var document = Parse(path);

		var results = Schema.Value.Evaluate(
			document.RootElement,
			// The default output format reports only valid/invalid; Hierarchical carries the failing
			// keyword and instance location that make a diagnostic actionable.
			new EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.Hierarchical }
		);
		if (results.IsValid)
			return;

		throw new InvalidOperationException(Describe(path, results));
	}

	static JsonDocument Parse(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			return JsonDocument.Parse(
				stream,
				new JsonDocumentOptions
				{
					// Consuming repositories annotate their configuration with // comments and trailing
					// commas; the configuration provider tolerates both, so validation must too.
					CommentHandling = JsonCommentHandling.Skip,
					AllowTrailingCommas = true,
				}
			);
		}
		catch (JsonException exception)
		{
			var position =
				exception.LineNumber is { } line && exception.BytePositionInLine is { } bytePosition
					// JsonException counts lines from zero; editors count from one.
					? $" at line {line + 1}, position {bytePosition + 1}"
					: string.Empty;

			throw new InvalidOperationException(
				$"'{path}' is not valid JSON{position}: {exception.Message}",
				exception
			);
		}
	}

	static JsonSchema LoadSchema()
	{
		using var stream =
			typeof(JsonConfigFile).Assembly.GetManifestResourceStream(SchemaResourceName)
			?? throw new InvalidOperationException(
				$"The embedded configuration schema '{SchemaResourceName}' was not found."
			);
		using StreamReader reader = new(stream);

		return JsonSchema.FromText(reader.ReadToEnd());
	}

	static string Describe(string path, EvaluationResults results)
	{
		var violations = Leaves(results)
			.SelectMany(
				result =>
					result.Errors?.Select(error => $"{Location(result)}: {DescribeError(result, error.Value)}") ?? []
			)
			.ToList();

		return $"'{path}' does not match the Purview.Build configuration schema:"
			+ Environment.NewLine
			+ string.Join(Environment.NewLine, violations.Select(violation => $"  - {violation}"))
			+ Environment.NewLine
			+ $"Add \"$schema\": \"{ConfigSchemaGenerator.SchemaId}\" to the file for editor validation.";
	}

	/// <summary>
	/// The failing results that have no failing children, so a container failure
	/// (<c>additionalProperties</c>, <c>properties</c>) reports the key that caused it rather than a
	/// generic message about the object.
	/// </summary>
	static IEnumerable<EvaluationResults> Leaves(EvaluationResults results)
	{
		var children = (results.Details ?? []).Where(detail => !detail.IsValid).ToList();

		if (children.Count == 0)
		{
			if (!results.IsValid)
				yield return results;

			yield break;
		}

		foreach (var child in children)
		{
			foreach (var leaf in Leaves(child))
				yield return leaf;
		}
	}

	static string Location(EvaluationResults result)
	{
		var pointer = result.InstanceLocation.ToString();

		return string.IsNullOrEmpty(pointer) ? "(root)" : pointer.TrimStart('/').Replace('/', '.');
	}

	/// <summary>
	/// A failure whose evaluation path ends at <c>additionalProperties</c> is a key the schema does
	/// not know, which is the typo the schema exists to catch; the library's own wording for it
	/// ("all values fail against the false schema") says nothing useful to the reader.
	/// </summary>
	static string DescribeError(EvaluationResults result, string message) =>
		result.EvaluationPath.ToString().EndsWith("/additionalProperties", StringComparison.Ordinal)
			? "unknown property (not a Purview.Build setting)"
			: message;
}
