using NuGet.Versioning;
using Purview.Build.Settings;

namespace Purview.Build;

public class ReleaseSettingsTests
{
	[Test]
	public async Task ShouldMarkPrerelease_GivenPrereleaseVersionAndDefaults_ReturnsTrue()
	{
		ReleaseSettings settings = new();

		await Assert.That(settings.ShouldMarkPrerelease(NuGetVersion.Parse("2.0.0-prerelease.25"))).IsTrue();
	}

	[Test]
	public async Task ShouldMarkPrerelease_GivenStableVersionAndDefaults_ReturnsFalse()
	{
		ReleaseSettings settings = new();

		await Assert.That(settings.ShouldMarkPrerelease(NuGetVersion.Parse("2.0.0"))).IsFalse();
	}

	[Test]
	public async Task ShouldMarkPrerelease_GivenMarkPrereleaseDisabled_ReturnsFalse()
	{
		ReleaseSettings settings = new() { MarkPrerelease = false };

		await Assert.That(settings.ShouldMarkPrerelease(NuGetVersion.Parse("2.0.0-prerelease.25"))).IsFalse();
	}

	[Test]
	public async Task MarkPrerelease_GivenDefaults_IsTrue()
	{
		ReleaseSettings settings = new();

		await Assert.That(settings.MarkPrerelease).IsTrue();
	}
}
