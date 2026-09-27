namespace Purview.Build.Helpers;

static class PathHelpers
{
	public static string FindRepositoryRoot(string? startDirectory = null)
	{
		if (string.IsNullOrEmpty(startDirectory))
			startDirectory = Environment.CurrentDirectory;

		DirectoryInfo? directory = new(startDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "package.json")))
				return directory.FullName;

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			"Could not locate the repository root (no package.json found). Run the tool from within the repository."
		);
	}

	/// <summary>
	/// Deletes a directory and its contents when it exists and recreates it empty.
	/// </summary>
	/// <remarks>
	/// Used to reset the artifacts folder before a pipeline run produces anything, so a later validation,
	/// publish, or release upload can never observe output from an earlier run.
	/// </remarks>
	public static void ResetDirectory(string directory)
	{
		var fullPath = Path.GetFullPath(directory);

		if (Directory.Exists(fullPath))
			Directory.Delete(fullPath, recursive: true);

		Directory.CreateDirectory(fullPath);
	}
}
