using Purview.Build.Helpers;

namespace Purview.Build;

public class ArtifactsCleanupTests
{
	static string CreateTempArtifactsFolder(params string[] relativeFiles)
	{
		var directory = Path.Combine(Path.GetTempPath(), "purview-build-artifacts-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		foreach (var relativeFile in relativeFiles)
		{
			var filePath = Path.Combine(directory, relativeFile);
			Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
			File.WriteAllText(filePath, "stale");
		}

		return directory;
	}

	[Test]
	public async Task ResetDirectory_RemovesStaleArtifactsAndRecreatesTheFolder()
	{
		var artifactsFolder = CreateTempArtifactsFolder(
			"Purview.ValueObjects.1.0.0-prerelease.6.nupkg",
			"Purview.ValueObjects.1.0.0-prerelease.6.snupkg",
			"nested/Purview.ValueObjects.1.0.0-prerelease.7.nupkg"
		);

		try
		{
			PathHelpers.ResetDirectory(artifactsFolder);

			await Assert.That(Directory.Exists(artifactsFolder)).IsTrue();
			await Assert.That(Directory.EnumerateFileSystemEntries(artifactsFolder).Any()).IsFalse();
		}
		finally
		{
			if (Directory.Exists(artifactsFolder))
				Directory.Delete(artifactsFolder, recursive: true);
		}
	}

	[Test]
	public async Task ResetDirectory_GivenMissingFolder_CreatesIt()
	{
		var artifactsFolder = Path.Combine(
			Path.GetTempPath(),
			"purview-build-artifacts-" + Guid.NewGuid().ToString("N")
		);

		try
		{
			PathHelpers.ResetDirectory(artifactsFolder);

			await Assert.That(Directory.Exists(artifactsFolder)).IsTrue();
			await Assert.That(Directory.EnumerateFileSystemEntries(artifactsFolder).Any()).IsFalse();
		}
		finally
		{
			if (Directory.Exists(artifactsFolder))
				Directory.Delete(artifactsFolder, recursive: true);
		}
	}
}
