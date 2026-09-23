using Purview.Build.Helpers;

namespace Purview.Build;

public class GlobTests
{
	static readonly string[] Files =
	[
		"tools/net10.0/any/Purview.Build.dll",
		"lib/netstandard2.0/Foo.dll",
		"lib/netstandard2.0/Foo.pdb",
		"README.md",
		"LICENSE.md",
		"purview-logo-light.png",
	];

	[Test]
	public async Task DoubleStar_MatchesNestedPath()
	{
		var result = PackageInspector.MatchesAny("tools/**/Purview.Build.dll", Files);
		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task DoubleStar_DoesNotMatchOutsideRoot()
	{
		var result = PackageInspector.MatchesAny("tools/**/Purview.Build.dll", ["lib/Purview.Build.dll"]);
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task DoubleStar_MatchesZeroDirectories()
	{
		var result = PackageInspector.MatchesAny("tools/**/Purview.Build.dll", ["tools/Purview.Build.dll"]);
		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task Star_DoesNotCrossDirectories()
	{
		var result = PackageInspector.MatchesAny("*.pdb", ["lib/net6.0/Foo.pdb"]);
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task DoubleStarAny_MatchesNestedFiles()
	{
		var result = PackageInspector.MatchesAny("**/*.pdb", Files);
		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task ExactPattern_MatchesOnlyExactPath_IgnoringCase()
	{
		await Assert.That(PackageInspector.MatchesAny("README.md", Files)).IsTrue();
		await Assert.That(PackageInspector.MatchesAny("ReadMe.MD", Files)).IsTrue();
		await Assert.That(PackageInspector.MatchesAny("readme.txt", Files)).IsFalse();
	}

	[Test]
	public async Task AnalyzerPdb_IsAllowedInNupkg()
	{
		await Assert.That(PackageInspector.IsPdbAllowedInNupkg("analyzers/dotnet/cs/Sample.pdb")).IsTrue();
		await Assert
			.That(
				PackageInspector.HasEmbeddedAnalyzerSymbols([
					"analyzers/dotnet/cs/Sample.dll",
					"analyzers/dotnet/cs/Sample.pdb",
				])
			)
			.IsTrue();
	}

	[Test]
	public async Task RequiredContent_GlobSatisfied_Passes()
	{
		List<string> errors = [];
		PackageInspector.ValidateContentRules(
			Files,
			"purview.build",
			new() { ["purview.build"] = ["tools/**/Purview.Build.dll", "README.md"] },
			[],
			errors
		);
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task RequiredContent_GlobMissing_ReportsError()
	{
		List<string> errors = [];
		PackageInspector.ValidateContentRules(
			Files,
			"purview.build",
			new() { ["purview.build"] = ["tools/**/Missing.dll"] },
			[],
			errors
		);
		await Assert.That(errors).Count().IsEqualTo(1);
	}

	[Test]
	public async Task ForbiddenContent_GlobMatch_ReportsError()
	{
		List<string> errors = [];
		PackageInspector.ValidateContentRules(Files, "purview.build", [], new() { ["*"] = ["**/*.pdb"] }, errors);
		await Assert.That(errors).Count().IsEqualTo(1);
	}

	[Test]
	public async Task WildcardPackageId_AppliesToEveryPackage()
	{
		List<string> errors = [];
		PackageInspector.ValidateContentRules(Files, "some.other.package", [], new() { ["*"] = ["**/*.pdb"] }, errors);
		await Assert.That(errors).Count().IsEqualTo(1);
	}

	[Test]
	public async Task GlobPackageId_MatchesSpecificPackages()
	{
		List<string> errors = [];
		PackageInspector.ValidateContentRules(
			Files,
			"purview.build",
			new() { ["purview.*"] = ["README.md"] },
			[],
			errors
		);
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task ExactPackageId_WinsOverWildcard()
	{
		List<string> errors = [];
		PackageInspector.ValidateContentRules(
			Files,
			"purview.build",
			new() { ["*"] = ["tools/**/Missing.dll"], ["purview.build"] = ["README.md"] },
			[],
			errors
		);
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task TfmToken_ExpandsAndSatisfiesEachTargetFramework()
	{
		List<string> errors = [];
		string[] files = ["lib/net8.0/Foo.dll", "lib/netstandard2.0/Foo.dll"];

		PackageInspector.ValidateContentRules(
			files,
			"purview.build",
			new() { ["purview.build"] = ["lib/$(TFM)/Foo.dll"] },
			[],
			errors,
			targetFrameworkFolderNames: ["net8.0", "netstandard2.0"]
		);

		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task TfmToken_MissingForOneFramework_ReportsError()
	{
		List<string> errors = [];
		string[] files = ["lib/net8.0/Foo.dll"];

		PackageInspector.ValidateContentRules(
			files,
			"purview.build",
			new() { ["purview.build"] = ["lib/$(TFM)/Foo.dll"] },
			[],
			errors,
			targetFrameworkFolderNames: ["net8.0", "netstandard2.0"]
		);

		await Assert.That(errors).Count().IsEqualTo(1);
	}

	[Test]
	public async Task TfmToken_NoFrameworksDetected_ReportsError()
	{
		List<string> errors = [];

		PackageInspector.ValidateContentRules(
			Files,
			"purview.build",
			new() { ["purview.build"] = ["lib/$(TFM)/Foo.dll"] },
			[],
			errors
		);

		await Assert.That(errors).Count().IsEqualTo(1);
	}

	[Test]
	public async Task ExplicitContent_AllDeclared_Passes()
	{
		List<string> errors = [];
		string[] files = ["README.md", "lib/netstandard2.0/Foo.dll"];

		PackageInspector.ValidateContentRules(
			files,
			"purview.build",
			new() { ["purview.build"] = ["README.md", "lib/netstandard2.0/Foo.dll"] },
			[],
			errors,
			requireExplicitContent: true
		);

		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task ExplicitContent_UndeclaredFile_ReportsError()
	{
		List<string> errors = [];

		PackageInspector.ValidateContentRules(
			Files,
			"purview.build",
			new() { ["purview.build"] = ["README.md"] },
			[],
			errors,
			requireExplicitContent: true
		);

		// README.md is declared; every other non-metadata file in `Files` is undeclared.
		await Assert.That(errors).Count().IsEqualTo(Files.Length - 1);
	}

	[Test]
	public async Task ExplicitContent_NoRuleForPackage_ReportsError()
	{
		List<string> errors = [];

		PackageInspector.ValidateContentRules(
			Files,
			"some.other.package",
			new() { ["purview.build"] = ["README.md"] },
			[],
			errors,
			requireExplicitContent: true
		);

		await Assert.That(errors).Count().IsEqualTo(1);
	}

	[Test]
	public async Task IsPackageMetadata_DetectsOpcAndNuspecEntries()
	{
		await Assert.That(PackageInspector.IsPackageMetadata("[Content_Types].xml")).IsTrue();
		await Assert.That(PackageInspector.IsPackageMetadata("_rels/.rels")).IsTrue();
		await Assert
			.That(PackageInspector.IsPackageMetadata("package/services/metadata/core-properties/abc.psmdcp"))
			.IsTrue();
		await Assert.That(PackageInspector.IsPackageMetadata("purview.build.nuspec")).IsTrue();
		await Assert.That(PackageInspector.IsPackageMetadata(".signature.p7s")).IsTrue();
		await Assert.That(PackageInspector.IsPackageMetadata("README.md")).IsFalse();
	}
}
