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
		"purview-logo.png",
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
}
