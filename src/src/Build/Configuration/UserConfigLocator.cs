namespace Purview.Build.Configuration;

/// <summary>
/// Locates the opt-in, machine-local user configuration file.
/// </summary>
/// <remarks>
/// Exists chiefly for <c>PublishLocalNuGet:LocalFeedPath</c>, which is inherently machine-specific.
/// It is also a reproducibility hazard — a machine-local file silently altering a build is the same
/// class of problem as an empty forwarded environment variable — so it is off by default and ignored
/// whenever the tool is not running locally, exactly as <c>Release:Mode=LocalNuGet</c> is.
/// </remarks>
static class UserConfigLocator
{
	const string DirectoryName = "purview-build";

	/// <summary>
	/// The candidate paths, in order, for the current platform.
	/// </summary>
	public static IReadOnlyList<string> Candidates()
	{
		List<string> candidates = [];

		var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		if (!string.IsNullOrWhiteSpace(xdg))
			candidates.Add(Path.Combine(xdg, DirectoryName, ConfigProbePaths.FileName));

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrWhiteSpace(home))
			candidates.Add(Path.Combine(home, ".config", DirectoryName, ConfigProbePaths.FileName));

		var appData = Environment.GetEnvironmentVariable("APPDATA");
		if (!string.IsNullOrWhiteSpace(appData))
			candidates.Add(Path.Combine(appData, DirectoryName, ConfigProbePaths.FileName));

		return candidates;
	}

	/// <summary>
	/// The first candidate that exists, or null.
	/// </summary>
	public static string? Find() =>
		Candidates().FirstOrDefault(File.Exists) is { } found ? Path.GetFullPath(found) : null;
}
