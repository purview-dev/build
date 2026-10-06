namespace Purview.Build.Configuration;

/// <summary>
/// Resolves which <c>purview-build.json</c> the run uses: an explicitly selected file, or the first
/// hit from the documented probe list.
/// </summary>
/// <remarks>
/// Paths inside the file stay anchored to the repository root wherever the file is found. Anchoring
/// to the config file's own directory would silently change the meaning of every existing relative
/// path the moment a repository moved its config into <c>.config/</c>.
/// </remarks>
static class ConfigFileLocator
{
	public const string EnvironmentVariableName = "PURVIEW_BUILD_CONFIG";

	public const string UserConfigEnvironmentVariableName = "PURVIEW_BUILD_USER_CONFIG";

	/// <summary>
	/// Resolves configuration for a run.
	/// </summary>
	/// <param name="repositoryRoot">The repository root, which every probe path is relative to.</param>
	/// <param name="explicitPath">The <c>--config</c> value, if any. Beats the environment variable.</param>
	/// <param name="userConfigRequested">Whether <c>--user-config</c> was passed.</param>
	/// <exception cref="InvalidOperationException">
	/// An explicitly selected path does not exist. Never falls back to probing or to defaults: an
	/// explicit location that silently does nothing is worse than a failure.
	/// </exception>
	public static ConfigResolution Resolve(
		string repositoryRoot,
		string? explicitPath = null,
		bool userConfigRequested = false
	)
	{
		var (userConfigPath, userConfigActive, userConfigSkipReason) = ResolveUserConfig(
			userConfigRequested
		);

		var environmentPath = Environment.GetEnvironmentVariable(EnvironmentVariableName);

		if (!string.IsNullOrWhiteSpace(explicitPath))
		{
			return new ConfigResolution
			{
				Path = ResolveExplicit(explicitPath, "--config"),
				Source = ConfigSource.CommandLine,
				UserConfigPath = userConfigPath,
				UserConfigActive = userConfigActive,
				UserConfigSkipReason = userConfigSkipReason,
			};
		}

		if (!string.IsNullOrWhiteSpace(environmentPath))
		{
			return new ConfigResolution
			{
				Path = ResolveExplicit(environmentPath, EnvironmentVariableName),
				Source = ConfigSource.EnvironmentVariable,
				UserConfigPath = userConfigPath,
				UserConfigActive = userConfigActive,
				UserConfigSkipReason = userConfigSkipReason,
			};
		}

		// No explicit path was supplied, so probe the repository for a config file.
		return Probe(repositoryRoot, userConfigPath, userConfigActive, userConfigSkipReason);
	}

	/// <summary>
	/// Resolves an explicitly supplied path, which may name a directory.
	/// </summary>
	static string ResolveExplicit(string path, string origin)
	{
		var fullPath = Path.GetFullPath(path);

		if (Directory.Exists(fullPath))
		{
			var inDirectory = Path.Combine(fullPath, ConfigProbePaths.FileName);

			return File.Exists(inDirectory)
				? inDirectory
				: throw new InvalidOperationException(
					$"{origin} names the directory '{fullPath}', which contains no "
						+ $"{ConfigProbePaths.FileName}."
				);
		}

		return File.Exists(fullPath)
			? fullPath
			: throw new InvalidOperationException(
				$"{origin} was set to '{path}', which does not exist (resolved to '{fullPath}'). "
					+ "Correct the path, or remove the setting to use the default probe order."
			);
	}

	static ConfigResolution Probe(
		string repositoryRoot,
		string? userConfigPath,
		bool userConfigActive,
		string? userConfigSkipReason
	)
	{
		List<ConfigProbeResult> trail = [];

		foreach (var probe in ConfigProbePaths.All)
		{
			var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, probe.RelativePath));
			trail.Add(new(probe.RelativePath, fullPath, File.Exists(fullPath)));
		}

		var winner = trail.FirstOrDefault(result => result.Exists);

		return new ConfigResolution
		{
			Path = winner?.FullPath,
			Source = winner is null ? ConfigSource.None : ConfigSource.Probe,
			ProbeTrail = trail,
			ShadowedPaths = winner is null
				? []
				: [.. trail.Where(result => result.Exists && result != winner).Select(result => result.FullPath)],
			NearMissPath = winner is null ? FindNearMiss(trail) : null,
			UserConfigPath = userConfigPath,
			UserConfigActive = userConfigActive,
			UserConfigSkipReason = userConfigSkipReason,
		};
	}

	static (string? Path, bool Active, string? SkipReason) ResolveUserConfig(bool requested)
	{
		var fromEnvironment = Environment.GetEnvironmentVariable(UserConfigEnvironmentVariableName);
		var optedIn =
			requested
			|| (
				!string.IsNullOrWhiteSpace(fromEnvironment)
				&& (
					fromEnvironment.Trim() == "1"
					|| string.Equals(fromEnvironment.Trim(), "true", StringComparison.OrdinalIgnoreCase)
				)
			);

		if (!optedIn)
			return (null, false, null);

		var path = UserConfigLocator.Find();
		if (path is null)
		{
			return (
				null,
				false,
				"user configuration was requested but no file exists at any candidate location"
			);
		}

		if (!ExecutionEnvironment.IsRunningLocally())
		{
			return (
				path,
				false,
				"user configuration is ignored when not running locally "
					+ $"({ExecutionEnvironment.DetectedBuildAgentVariable()} is set)"
			);
		}

		return (path, true, null);
	}

	/// <summary>
	/// Looks for a file in any probe directory whose name is within a small edit distance of
	/// <c>purview-build.json</c>, so an obvious typo is reported rather than read as "no config".
	/// </summary>
	static string? FindNearMiss(IReadOnlyList<ConfigProbeResult> trail)
	{
		const int maxDistance = 3;

		foreach (var probe in trail)
		{
			var directory = Path.GetDirectoryName(probe.FullPath);
			if (directory is null || !Directory.Exists(directory))
				continue;

			foreach (var candidate in Directory.EnumerateFiles(directory, "*.json"))
			{
				var name = Path.GetFileName(candidate);
				if (string.Equals(name, ConfigProbePaths.FileName, StringComparison.OrdinalIgnoreCase))
					continue;

				var distance = EditDistance(name, ConfigProbePaths.FileName);
				if (distance is > 0 and <= maxDistance)
					return Path.GetFullPath(candidate);
			}
		}

		return null;
	}

	/// <summary>
	/// Case-insensitive Levenshtein distance, two rows at a time.
	/// </summary>
	static int EditDistance(string left, string right)
	{
		var previous = new int[right.Length + 1];
		var current = new int[right.Length + 1];

		for (var column = 0; column <= right.Length; column++)
			previous[column] = column;

		for (var row = 1; row <= left.Length; row++)
		{
			current[0] = row;

			for (var column = 1; column <= right.Length; column++)
			{
				var same = char.ToUpperInvariant(left[row - 1]) == char.ToUpperInvariant(right[column - 1]);
				var substitution = previous[column - 1] + (same ? 0 : 1);
				current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), substitution);
			}

			(previous, current) = (current, previous);
		}

		return previous[right.Length];
	}
}
