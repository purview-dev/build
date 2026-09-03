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
}