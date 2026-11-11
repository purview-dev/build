using System.Text.Json;
using Purview.Build.PackageJson;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// The parts of <c>package.json</c> validation that the scenario fixture does not drive: the npm
/// name rules, profile resolution, the unreadable-file path, and the JSON contract.
/// </summary>
public class PackageJsonValidationTests
{
	// A file that satisfies every rule for the profile it is read as.
	const string CompleteDotNetDocument = /*lang=json,strict*/
		"""
		{
			"name": "contoso.widgets",
			"version": "1.2.3",
			"description": "Widget primitives for Contoso applications.",
			"license": "MIT",
			"author": { "name": "Contoso" },
			"homepage": "https://contoso.example/widgets",
			"bugs": { "url": "https://contoso.example/widgets/issues" },
			"repository": { "type": "git", "url": "git+https://contoso.example/widgets.git" },
			"keywords": ["widgets"],
			"private": true
		}
		""";

	[Test]
	public async Task Validate_GivenACompleteDocument_PassesEveryRule()
	{
		var report = Validate(CompleteDotNetDocument, PackageJsonProfile.DotNetNuGet);

		await Assert.That(report.Succeeded).IsTrue().Because(PackageJsonValidationReportText.Render(report));
		await Assert.That(report.ErrorCount).IsEqualTo(0);
		await Assert.That(report.WarningCount).IsEqualTo(0);
		await Assert.That(report.ExitCode).IsEqualTo(0);
	}

	[Test]
	public async Task Validate_GivenAnEmptyDocument_StillReportsEveryRule()
	{
		// One missing field must not hide the rest of the findings; the report is the whole picture.
		var report = Validate("{}", PackageJsonProfile.DotNetNuGet);

		await Assert
			.That(report.Results.Select(result => result.RuleId))
			.IsEquivalentTo(PackageJsonRules.All.Select(rule => rule.Id));
	}

	[Test]
	public async Task Validate_GivenARuleDisabledInConfiguration_SkipsItRatherThanFailingIt()
	{
		const string document = /*lang=json,strict*/
			"""
			{
				"name": "contoso.widgets",
				"version": "1.2.3",
				"description": "Widget primitives for Contoso applications.",
				"license": "MIT",
				"author": { "name": "Contoso" },
				"homepage": "https://contoso.example/widgets",
				"repository": { "type": "git", "url": "git+https://contoso.example/widgets.git" },
				"private": true
			}
			""";

		var report = Validate(document, PackageJsonProfile.DotNetNuGet, disabledRules: ["PKG008"]);

		await Assert.That(report.Succeeded).IsTrue().Because(PackageJsonValidationReportText.Render(report));
		await Assert.That(report.Results.Single(result => result.RuleId == "PKG008").Outcome).IsEqualTo("skipped");
	}

	[Test]
	public async Task Validate_GivenAnUnparseableFile_ReportsPkg000WithoutThrowing()
	{
		var report = Validate("this is not JSON", PackageJsonProfile.DotNetNuGet);

		await Assert.That(report.Succeeded).IsFalse();
		await Assert.That(report.ExitCode).IsEqualTo(1);
		await Assert.That(report.Results).Count().IsEqualTo(1);
		await Assert.That(report.Results[0].RuleId).IsEqualTo(PackageJsonRules.ReadFailureRuleId);
	}

	[Test]
	public async Task Validate_GivenAMissingFile_ReportsPkg000WithoutThrowing()
	{
		var directory = NewTemporaryDirectory();

		try
		{
			var report = PackageJsonValidationRunner.Run(
				new PackageJsonValidationSettings(),
				new BuildSettings(),
				new VersionSettings(),
				directory
			);

			await Assert.That(report.Succeeded).IsFalse();
			await Assert.That(report.Results[0].RuleId).IsEqualTo(PackageJsonRules.ReadFailureRuleId);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task ToJson_ExposesTheContractACallerReads()
	{
		var report = Validate(CompleteDotNetDocument, PackageJsonProfile.DotNetNuGet);

		using var document = JsonDocument.Parse(report.ToJson());
		var root = document.RootElement;

		await Assert.That(root.TryGetProperty("file", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("profile", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("succeeded", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("exitCode", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("errorCount", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("warningCount", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("results", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("errors", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("warnings", out _)).IsTrue();

		await Assert.That(root.GetProperty("profile").GetString()).IsEqualTo("DotNetNuGet");
		await Assert.That(root.GetProperty("results").GetArrayLength()).IsEqualTo(PackageJsonRules.All.Count);
	}

	[Test]
	public async Task Rules_HaveUniquePermanentIds()
	{
		var ids = PackageJsonRules.All.Select(rule => rule.Id).ToArray();

		await Assert.That(ids.Distinct(StringComparer.Ordinal)).Count().IsEqualTo(ids.Length);
		await Assert.That(PackageJsonRules.IsKnown(PackageJsonRules.ReadFailureRuleId)).IsTrue();
	}

	[Test]
	public async Task Profile_AutoResolvesFromTheProjectShape()
	{
		await Assert
			.That(
				PackageJsonProfiles.Resolve(
					PackageJsonProfile.Auto,
					ProjectType.DotNet,
					packsNuGetPackages: true,
					isPrivate: true
				)
			)
			.IsEqualTo(PackageJsonProfile.DotNetNuGet);

		await Assert
			.That(
				PackageJsonProfiles.Resolve(
					PackageJsonProfile.Auto,
					ProjectType.DotNet,
					packsNuGetPackages: false,
					isPrivate: true
				)
			)
			.IsEqualTo(PackageJsonProfile.DotNetSolution);

		await Assert
			.That(
				PackageJsonProfiles.Resolve(
					PackageJsonProfile.Auto,
					ProjectType.Web,
					packsNuGetPackages: false,
					isPrivate: true
				)
			)
			.IsEqualTo(PackageJsonProfile.WebApp);

		await Assert
			.That(
				PackageJsonProfiles.Resolve(
					PackageJsonProfile.Auto,
					ProjectType.Web,
					packsNuGetPackages: false,
					isPrivate: false
				)
			)
			.IsEqualTo(PackageJsonProfile.WebPackage);
	}

	[Test]
	public async Task Profile_GivenAnExplicitProfile_IsNotDerived()
	{
		var resolved = PackageJsonProfiles.Resolve(
			PackageJsonProfile.WebApp,
			ProjectType.DotNet,
			packsNuGetPackages: true,
			isPrivate: true
		);

		await Assert.That(resolved).IsEqualTo(PackageJsonProfile.WebApp);
		await Assert.That(PackageJsonProfiles.IsWeb(resolved)).IsTrue();
		await Assert.That(PackageJsonProfiles.Publishes(resolved)).IsFalse();
	}

	[Test]
	public async Task NpmNames_AcceptEveryValidShape()
	{
		foreach (var name in new[] { "build", "purview-build", "@purview/build", "a", "b2.c_d" })
		{
			await Assert
				.That(NpmNames.DescribeProblem(name))
				.IsNull()
				.Because($"'{name}' is a valid npm package name.");
		}
	}

	[Test]
	public async Task NpmNames_RejectEveryInvalidShape()
	{
		string[] invalid =
		[
			string.Empty,
			"Build",
			".hidden",
			"_private",
			"has space",
			"node_modules",
			"favicon.ico",
			"a/b",
			"@scope",
			"@scope/",
			"@/name",
			"ünicode",
		];

		foreach (var name in invalid)
		{
			await Assert
				.That(NpmNames.DescribeProblem(name))
				.IsNotNull()
				.Because($"'{name}' is not a valid npm package name.");
		}
	}

	static PackageJsonValidationReport Validate(
		string packageJson,
		PackageJsonProfile profile,
		ProjectType projectType = ProjectType.DotNet,
		IReadOnlyList<string>? disabledRules = null,
		bool treatWarningsAsErrors = false
	)
	{
		var directory = NewTemporaryDirectory();

		try
		{
			File.WriteAllText(Path.Combine(directory, "package.json"), packageJson);

			PackageJsonValidationSettings settings = new()
			{
				Enabled = true,
				Profile = profile,
				DisabledRules = disabledRules is null ? [] : [.. disabledRules],
				TreatWarningsAsErrors = treatWarningsAsErrors,
			};

			BuildSettings build = new() { ProjectType = projectType, RunPack = true };

			return PackageJsonValidationRunner.Run(settings, build, new VersionSettings(), directory);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	static string NewTemporaryDirectory()
	{
		var directory = Path.Combine(Path.GetTempPath(), "purview-build-packagejson-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		return directory;
	}
}
