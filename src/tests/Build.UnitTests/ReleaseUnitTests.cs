using NuGet.Versioning;
using Purview.Build.Release;
using Purview.Build.Settings;

namespace Purview.Build;

public class ReleaseUnitTests
{
	static readonly string[] ExpectedConcurrentLines = ["2.0", "2.1"];

	static ReleaseUnit Unit(string version) =>
		ReleaseUnitFactory.TryCreate(
			version,
			ReleaseChannelSettings.StableChannelName,
			nameof(VersionSource.PackageJson)
		)!;

	[Test]
	[Arguments("2.0.2", "2.0")]
	[Arguments("2.1.0-prerelease.1", "2.1")]
	[Arguments("13.5.3.10", "13.5")]
	[Arguments("1.0.0", "1.0")]
	public async Task LineOf_GivenVersion_IsTheMajorMinorPair(string version, string expectedLine)
	{
		// Arrange
		// The line is what REL005 scopes monotonicity to, so a serviced stable line and an
		// in-flight prerelease line never block each other.

		// Act
		var line = Unit(version).Line;

		// Assert
		await Assert.That(line).IsEqualTo(expectedLine);
	}

	[Test]
	[Arguments("2.1.0-prerelease.1", "prerelease")]
	[Arguments("2.1.0-preview.4", "preview")]
	[Arguments("2.1.0-rc1", "rc1")]
	public async Task LabelOf_GivenPrerelease_DropsTheNumericCounter(string version, string expectedLabel)
	{
		// Arrange

		// Act
		var label = Unit(version).PrereleaseLabel;

		// Assert
		await Assert.That(label).IsEqualTo(expectedLabel);
	}

	[Test]
	public async Task LabelOf_GivenStableVersion_IsNull()
	{
		// Arrange

		// Act
		var label = Unit("2.0.0").PrereleaseLabel;

		// Assert
		await Assert.That(label).IsNull();
	}

	[Test]
	[Arguments("13.5.3.10", "v13.5.3.10")]
	[Arguments("1.0.0.0", "v1.0.0.0")]
	[Arguments("2.0.2", "v2.0.2")]
	public async Task Tag_PreservesTheVersionExactlyAsWritten(string version, string expectedTag)
	{
		// Arrange
		// The workflow's old gate computed TAG="v$VERSION" from the raw package.json text. A
		// normalised tag would drop a trailing ".0" revision and desynchronise the two.

		// Act
		var tag = Unit(version).Tag;

		// Assert
		await Assert.That(tag).IsEqualTo(expectedTag);
	}

	[Test]
	[Arguments("2.0.1.1", true)]
	[Arguments("13.5.3.10", true)]
	[Arguments("1.0.0", false)]
	[Arguments("2.1.0-prerelease.1", false)]
	[Arguments("1.0.0+abc", false)]
	public async Task IsFourPart_GivenVersion_DetectsFourNumericComponents(string version, bool expected)
	{
		// Arrange

		// Act
		var fourPart = ReleaseUnitFactory.IsFourPart(version);

		// Assert
		await Assert.That(fourPart).IsEqualTo(expected);
	}

	[Test]
	[Arguments("13.5.3.10", VersionStrictness.NuGet, true)]
	[Arguments("13.5.3.10", VersionStrictness.SemVer2, false)]
	[Arguments("1.0.0", VersionStrictness.SemVer2, true)]
	[Arguments("1.0", VersionStrictness.NuGet, true)]
	[Arguments("1.0", VersionStrictness.SemVer2, false)]
	[Arguments("not-a-version", VersionStrictness.NuGet, false)]
	public async Task SatisfiesStrictness_GivenVersionAndStrictness_JudgesTheText(
		string version,
		VersionStrictness strictness,
		bool expected
	)
	{
		// Arrange
		// NuGet strictness is the default because it is what the pipeline has always accepted.

		// Act
		var satisfies = ReleaseUnitFactory.SatisfiesStrictness(version, strictness);

		// Assert
		await Assert.That(satisfies).IsEqualTo(expected);
	}

	[Test]
	public async Task TryCreate_GivenUnparseableVersion_ReturnsNull()
	{
		// Arrange

		// Act
		var unit = ReleaseUnitFactory.TryCreate(
			"nonsense",
			ReleaseChannelSettings.StableChannelName,
			nameof(VersionSource.PackageJson)
		);

		// Assert
		await Assert.That(unit).IsNull();
	}

	[Test]
	public async Task ReleaseUnitSet_GivenMultipleUnits_ExposesEveryUnitInOrder()
	{
		// Arrange
		// Every version source yields one unit today. The set is the contract so a future
		// multi-unit source needs no change in PackModule or CreateGitHubReleaseModule, both of
		// which enumerate Units rather than reading a scalar version.
		ReleaseUnitSet set = new(
			"2.0.2",
			[Unit("2.0.2"), Unit("2.1.0-prerelease.1")],
			nameof(VersionSource.PackageJson)
		);

		// Act
		var tags = set.Units.Select(unit => unit.Tag).ToList();

		// Assert
		await Assert.That(set.Units).Count().IsEqualTo(2);
		await Assert.That(tags[0]).IsEqualTo("v2.0.2");
		await Assert.That(tags[1]).IsEqualTo("v2.1.0-prerelease.1");
		await Assert.That(set.Primary.Tag).IsEqualTo("v2.0.2");
		await Assert.That(set.Version).IsEqualTo(NuGetVersion.Parse("2.0.2"));
	}

	[Test]
	public async Task ReleaseUnitSet_GivenMultipleUnits_KeepsLinesAndChannelsIndependent()
	{
		// Arrange
		ReleaseUnitSet set = new(
			"2.0.2",
			[Unit("2.0.2"), Unit("2.1.0-prerelease.1")],
			nameof(VersionSource.PackageJson)
		);

		// Act
		var lines = set.Units.Select(unit => unit.Line).ToList();

		// Assert
		await Assert.That(lines).IsEquivalentTo(ExpectedConcurrentLines);
		await Assert.That(set.Units[0].IsPrerelease).IsFalse();
		await Assert.That(set.Units[1].IsPrerelease).IsTrue();
	}

	[Test]
	public async Task ReleaseUnitSet_GivenNoUnits_FailsWhenThePrimaryIsRead()
	{
		// Arrange
		ReleaseUnitSet set = new("1.0.0", [], nameof(VersionSource.PackageJson));

		// Act
		ReleaseUnit Act() => set.Primary;

		// Assert
		await Assert.That(Act).Throws<InvalidOperationException>();
	}
}
