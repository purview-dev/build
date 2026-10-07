using System.Text.Json.Nodes;
using Purview.Build.Configuration;
using Purview.Build.Schema;

namespace Purview.Build;

/// <summary>
/// Pins the generated configuration schema against the settings types, and pins the validation the
/// tool applies when it loads <c>purview-build.json</c>.
/// </summary>
/// <remarks>
/// The schema is generated from the settings types, so it cannot describe a key the tool does not
/// bind. Regenerate it with <c>just schema</c> after deliberately changing the settings; a drift
/// fails here rather than shipping a schema that disagrees with the tool.
/// </remarks>
public class ConfigSchemaTests
{
	const string SchemaFileName = "purview-build.schema.json";

	[Test]
	public async Task Schema_MatchesTheGeneratedFile()
	{
		// Arrange
		var path = Path.Combine(RepositoryRoot, SchemaFileName);
		var generated = ConfigSchemaGenerator.Render(ShippedDefaults());

		// Act
		// Regeneration is deliberately opt-in, exactly as for the release-explain golden file: a
		// file that rewrites itself on mismatch records the change instead of reporting it.
		if (Environment.GetEnvironmentVariable("PURVIEW_BUILD_UPDATE_SCHEMA") == "1")
			await File.WriteAllTextAsync(path, generated);

		// Assert
		await Assert
			.That(File.Exists(path))
			.IsTrue()
			.Because($"the schema '{path}' must exist; regenerate it with `just schema` if it was removed.");

		await Assert
			.That(Normalise(await File.ReadAllTextAsync(path)))
			.IsEqualTo(Normalise(generated))
			.Because("the schema is generated from the settings types; regenerate it with `just schema`.");
	}

	[Test]
	public async Task Schema_GivenShippedDefaults_StatesTheEffectiveDefaults()
	{
		// Arrange
		var schema = ConfigSchemaGenerator.Build(ShippedDefaults());

		// Assert
		// The shipped appsettings.json wins over the C# initialisers, so the stated default is the
		// value a consumer actually gets, not the one the property initialiser suggests.
		await Assert.That(Property(schema, "Build", "ProjectType")["default"]!.GetValue<string>()).IsEqualTo("DotNet");
		await Assert
			.That(Property(schema, "PackValidation", "RequireSourceLink")["default"]!.GetValue<bool>())
			.IsFalse();
		await Assert
			.That(Property(schema, "PackValidation", "RequireDeterministic")["default"]!.GetValue<bool>())
			.IsFalse();
	}

	[Test]
	public async Task Schema_DescribesEveryNestedSettingsType()
	{
		// Arrange
		var release = Section(schema: ConfigSchemaGenerator.Build(ShippedDefaults()), "Release")[
			"properties"
		]!.AsObject();

		// Assert
		await Assert.That(release.ContainsKey("Eligibility")).IsTrue();
		await Assert.That(release.ContainsKey("Context")).IsTrue();
		await Assert.That(release.ContainsKey("Channels")).IsTrue();

		var channels = release["Channels"]!.AsObject();
		await Assert.That(channels["additionalProperties"]!["type"]!.GetValue<string>()).IsEqualTo("object");

		var policies = release["Eligibility"]!["properties"]!["Policies"]!.AsObject();
		await Assert.That(policies["additionalProperties"]!["properties"]!.AsObject().ContainsKey("Rules")).IsTrue();
	}

	[Test]
	public void Validate_GivenTheRepositoryConfiguration_Succeeds() =>
		AssertValid(File.ReadAllText(Path.Combine(RepositoryRoot, "purview-build.json")));

	[Test]
	public void Validate_GivenCommentsAndTrailingCommas_Succeeds() =>
		// Consuming repositories annotate their configuration with // comments; the binder tolerates
		// them, so validation must too.
		AssertValid(
			"""
			{
				// The solution to build.
				"Build": { "Solution": "src/Product.slnx" },
			}
			"""
		);

	[Test]
	public void Validate_GivenMetadataKeys_Succeeds() =>
		// "$schema" drives editor completion; other "$"-prefixed keys are ignored metadata. Both must
		// be accepted so the schema can be referenced from the file it validates.
		AssertValid(
			"""
			{
				"$schema": "https://raw.githubusercontent.com/purview-dev/build/main/purview-build.schema.json",
				"$comment": "Annotated for the next reader.",
				"Build": { "Solution": "src/Product.slnx" },
				"$marker": "used by the configuration-resolution scenarios"
			}
			"""
		);

	[Test]
	public async Task Validate_GivenUnknownProperty_FailsNamingTheKey()
	{
		var error = AssertInvalid("""{ "Build": { "RunPackk": true } }""");

		await Assert.That(error).Contains("Build.RunPackk");
		await Assert.That(error).Contains("unknown property");
	}

	[Test]
	public async Task Validate_GivenUnknownSection_FailsNamingTheKey()
	{
		var error = AssertInvalid("""{ "Releases": { "Mode": "None" } }""");

		await Assert.That(error).Contains("Releases");
		await Assert.That(error).Contains("unknown property");
	}

	[Test]
	public async Task Validate_GivenWrongType_FailsNamingTheKey()
	{
		var error = AssertInvalid("""{ "Build": { "RunPack": "yes" } }""");

		await Assert.That(error).Contains("Build.RunPack");
	}

	[Test]
	public async Task Validate_GivenInvalidEnumValue_FailsNamingTheKey()
	{
		var error = AssertInvalid("""{ "Release": { "Mode": "Nugget" } }""");

		await Assert.That(error).Contains("Release.Mode");
	}

	[Test]
	public async Task Validate_GivenMalformedJson_FailsNamingTheFile()
	{
		var error = AssertInvalid("{ \"Build\": { \"Solution\": ");

		await Assert.That(error).Contains("is not valid JSON");
	}

	static void AssertValid(string json)
	{
		var path = WriteTemporary(json);
		try
		{
			JsonConfigFile.Validate(path);
		}
		catch (InvalidOperationException exception)
		{
			throw new InvalidOperationException(
				$"Expected the configuration to be valid, but validation failed: {exception.Message}",
				exception
			);
		}
		finally
		{
			File.Delete(path);
		}
	}

	static string AssertInvalid(string json)
	{
		var path = WriteTemporary(json);
		try
		{
			try
			{
				JsonConfigFile.Validate(path);
			}
			catch (InvalidOperationException exception)
			{
				return exception.Message;
			}

			throw new InvalidOperationException(
				$"Expected the configuration to fail validation, but it was accepted:{Environment.NewLine}{json}"
			);
		}
		finally
		{
			File.Delete(path);
		}
	}

	static string WriteTemporary(string json)
	{
		var path = Path.Combine(Path.GetTempPath(), $"purview-build-schema-{Guid.NewGuid():N}.json");
		File.WriteAllText(path, json);
		return path;
	}

	static JsonObject ShippedDefaults() =>
		ConfigSchemaGenerator.LoadShippedDefaults(
			Path.Combine(RepositoryRoot, "src", "src", "Build", "appsettings.json")
		) ?? [];

	static JsonObject Section(JsonObject schema, string name) => schema["properties"]![name]!.AsObject();

	static JsonObject Property(JsonObject schema, string section, string property) =>
		Section(schema, section)["properties"]![property]!.AsObject();

	static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

	static string RepositoryRoot { get; } = FindRepositoryRoot();

	static string FindRepositoryRoot()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			if (
				File.Exists(Path.Combine(directory.FullName, SchemaFileName))
				&& File.Exists(Path.Combine(directory.FullName, "package.json"))
			)
				return directory.FullName;
		}

		throw new InvalidOperationException(
			$"Could not locate the repository root walking up from '{AppContext.BaseDirectory}'."
		);
	}
}
