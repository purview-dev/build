namespace Purview.Build.Helpers;

/// <summary>
/// Discovers the package artifacts a publication step pushes.
/// </summary>
/// <remarks>
/// NuGet.org and GitHub Packages publish the exact same files: packing happens once, and both modules
/// read the same folder. Sharing discovery keeps that guarantee visible and keeps their diagnostics in
/// step — a missing folder is a pack-step problem, not a registry problem.
/// </remarks>
static class NuGetPackages
{
	/// <summary>
	/// The <c>.nupkg</c> files in <paramref name="artifactsFolder"/>, in enumeration order.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The folder does not exist, or contains no package.
	/// </exception>
	public static IReadOnlyList<string> Discover(string artifactsFolder)
	{
		if (!Directory.Exists(artifactsFolder))
		{
			throw new InvalidOperationException(
				$"The artifacts folder '{artifactsFolder}' does not exist. "
					+ "Ensure the pack step ran (Release__Mode must not be None) before publishing."
			);
		}

		var packages = Directory
			.EnumerateFiles(artifactsFolder, "*.nupkg", SearchOption.TopDirectoryOnly)
			.ToList();

		if (packages.Count == 0)
		{
			throw new InvalidOperationException($"No NuGet packages found in {artifactsFolder}.");
		}

		// The module logs the files it pushes, so the caller does not need to log them here.
		return packages;
	}
}
