using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Purview.Build.Schema;

/// <summary>
/// Generates the JSON Schema for <c>purview-build.json</c> from the settings types.
/// </summary>
/// <remarks>
/// The settings types are the single source of truth: a key the tool can bind is a key the schema
/// describes, and a key the tool cannot bind fails validation. <c>just schema</c> regenerates the
/// committed file, and <c>ConfigSchemaTests</c> fails the build when the two drift.
/// </remarks>
static class ConfigSchemaGenerator
{
	/// <summary>
	/// The canonical location of the schema, used both as the schema's <c>$id</c> and in the
	/// diagnostic the tool prints when a file does not validate.
	/// </summary>
	public const string SchemaId =
		"https://raw.githubusercontent.com/purview-dev/build/main/purview-build.schema.json";

	/// <summary>
	/// Every configuration section, in the order they appear in the schema. A section is a top-level
	/// object in <c>purview-build.json</c>; anything else reachable from it is nested.
	/// </summary>
	static readonly (string Section, Type Type)[] Sections =
	[
		(BuildSettings.SectionName, typeof(BuildSettings)),
		(PackValidationSettings.SectionName, typeof(PackValidationSettings)),
		(NuGetSettings.SectionName, typeof(NuGetSettings)),
		(PublishLocalNuGetSettings.SectionName, typeof(PublishLocalNuGetSettings)),
		(GitHubSettings.SectionName, typeof(GitHubSettings)),
		(VersionSettings.SectionName, typeof(VersionSettings)),
		(ReleaseSettings.SectionName, typeof(ReleaseSettings)),
	];

	static readonly JsonSerializerOptions ValueOptions = new()
	{
		Converters = { new JsonStringEnumConverter() },
	};

	static readonly JsonSerializerOptions RenderOptions = new()
	{
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1,
		// Pinned so the generated file is identical on every platform, like the other JSON here.
		NewLine = "\n",
	};

	/// <summary>
	/// Builds the schema, with <paramref name="shippedDefaults"/> (the tool's <c>appsettings.json</c>)
	/// overlaid on the C# initialisers so every <c>default</c> states the value the tool actually
	/// ships.
	/// </summary>
	public static JsonObject Build(JsonObject? shippedDefaults = null)
	{
		var documentation = SchemaDocumentation.Load();

		JsonObject properties = new();
		foreach (var (section, type) in Sections)
		{
			properties[section] = BuildObject(type, shippedDefaults?[section] as JsonObject, documentation);
		}

		return new JsonObject
		{
			["$schema"] = "https://json-schema.org/draft/2020-12/schema",
			["$id"] = SchemaId,
			["title"] = "Purview.Build configuration",
			["description"] =
				"Configuration for the Purview.Build shared pipeline (purview-build.json). "
				+ "Every section is optional; unset keys fall back to the tool's defaults.",
			["type"] = "object",
			["properties"] = properties,
			["additionalProperties"] = false,
			// "$" is reserved for metadata ($schema, $comment, ...). The tool ignores those keys, so
			// the schema accepts them while still rejecting a misspelled setting.
			["patternProperties"] = new JsonObject { ["^\\$"] = true },
		};
	}

	/// <summary>Renders the schema exactly as the committed file stores it.</summary>
	public static string Render(JsonObject? shippedDefaults = null) =>
		Build(shippedDefaults).ToJsonString(RenderOptions) + "\n";

	/// <summary>
	/// Loads the shipped <c>appsettings.json</c> so its values win over the C# initialisers, exactly
	/// as they do at runtime.
	/// </summary>
	public static JsonObject? LoadShippedDefaults(string path) =>
		File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;

	static JsonObject BuildObject(
		Type type,
		JsonObject? shippedDefaults,
		IReadOnlyDictionary<string, string> documentation
	)
	{
		var instance = Activator.CreateInstance(type);
		JsonObject properties = new();

		foreach (var property in SettingsProperties(type))
		{
			properties[property.Name] = BuildProperty(
				property,
				shippedDefaults?[property.Name],
				instance is null ? null : property.GetValue(instance),
				documentation
			);
		}

		JsonObject schema = new()
		{
			["type"] = "object",
			["properties"] = properties,
			["additionalProperties"] = false,
			["patternProperties"] = new JsonObject { ["^\\$"] = true },
		};

		if (documentation.TryGetValue($"T:{type.FullName}", out var summary))
			schema["description"] = summary;

		return schema;
	}

	static JsonObject BuildProperty(
		PropertyInfo property,
		JsonNode? shippedDefault,
		object? csharpValue,
		IReadOnlyDictionary<string, string> documentation
	)
	{
		var declared = property.PropertyType;
		var type = Nullable.GetUnderlyingType(declared) ?? declared;
		var nullable = type != declared;

		var schema = BuildType(type, shippedDefault as JsonObject, documentation);

		if (nullable && schema["type"] is JsonValue value && value.TryGetValue<string>(out var typeName))
			schema["type"] = new JsonArray(typeName, "null");

		if (documentation.TryGetValue($"P:{property.DeclaringType!.FullName}.{property.Name}", out var summary))
			schema["description"] = summary;

		// A JSON null means "unset", so it is not worth stating as a default. A nested object's
		// default comes only from the shipped appsettings.json, because serialising the C# instance
		// would also emit computed members such as Release:Context:IsSimulated.
		var @default = Prune(shippedDefault);
		if (@default is null && !IsNestedSettings(type))
			@default = Prune(ToJsonValue(csharpValue));

		if (@default is not null)
			schema["default"] = @default;

		return schema;
	}

	/// <summary>
	/// Whether a property's type is a nested settings object rather than a scalar, array or map.
	/// </summary>
	static bool IsNestedSettings(Type type) =>
		!type.IsEnum
		&& type != typeof(string)
		&& type != typeof(bool)
		&& type != typeof(int)
		&& type != typeof(long)
		&& !type.IsArray
		&& DictionaryValueType(type) is null;

	static JsonObject BuildType(
		Type type,
		JsonObject? shippedDefaults,
		IReadOnlyDictionary<string, string> documentation
	)
	{
		if (type.IsEnum)
		{
			JsonArray values = new();
			foreach (var name in Enum.GetNames(type))
				values.Add(name);
			return new JsonObject { ["type"] = "string", ["enum"] = values };
		}

		if (type == typeof(bool))
			return new JsonObject { ["type"] = "boolean" };

		if (type == typeof(int) || type == typeof(long))
			return new JsonObject { ["type"] = "integer" };

		if (type == typeof(string))
			return new JsonObject { ["type"] = "string" };

		if (type.IsArray)
			return new JsonObject
			{
				["type"] = "array",
				["items"] = BuildType(type.GetElementType()!, shippedDefaults: null, documentation),
			};

		var dictionaryValue = DictionaryValueType(type);
		if (dictionaryValue is not null)
			return new JsonObject
			{
				["type"] = "object",
				["additionalProperties"] = BuildType(dictionaryValue, shippedDefaults: null, documentation),
			};

		// Any remaining settings type is a nested object with its own known keys.
		return BuildObject(type, shippedDefaults, documentation);
	}

	static Type? DictionaryValueType(Type type)
	{
		if (
			type.IsGenericType
			&& type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
			&& type.GetGenericArguments()[0] == typeof(string)
		)
			return type.GetGenericArguments()[1];

		return null;
	}

	/// <summary>
	/// The bindable properties of a settings type, in declaration order. Excludes the static
	/// <c>SectionName</c>/<c>Default</c>/<c>Stable</c> helpers and computed properties such as
	/// <c>IsSimulated</c>, neither of which the configuration binder can set.
	/// </summary>
	static IEnumerable<PropertyInfo> SettingsProperties(Type type) =>
		type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.CanWrite)
			.OrderBy(property => property.MetadataToken);

	static JsonNode? ToJsonValue(object? value) =>
		value is null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), ValueOptions);

	/// <summary>
	/// Drops nulls from a default value: a nested object whose members are all null carries no
	/// information, and <c>null</c> is not a useful stated default for an optional key.
	/// </summary>
	static JsonNode? Prune(JsonNode? node)
	{
		switch (node)
		{
			case null:
				return null;
			case JsonObject obj:
			{
				JsonObject pruned = new();
				foreach (var (key, value) in obj)
				{
					if (Prune(value) is { } kept)
						pruned[key] = kept;
				}
				return pruned;
			}
			case JsonArray array:
			{
				JsonArray pruned = new();
				foreach (var item in array)
				{
					if (Prune(item) is { } kept)
						pruned.Add(kept);
				}
				return pruned;
			}
			default:
				return node.DeepClone();
		}
	}
}
