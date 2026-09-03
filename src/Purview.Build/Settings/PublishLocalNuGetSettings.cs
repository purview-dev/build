using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Purview.Build.Settings;

public sealed record PublishLocalNuGetSettings : IValidatableObject
{
	public const string SectionName = "PublishLocalNuGet";

	public string LocalFeedPath { get; init; } = string.Empty;

	/// <summary>
	/// Env-var bound alias for <see cref="LocalFeedPath"/> via
	/// <c>PublishLocalNuGet__LOCAL_NUGET_FEED_PATH</c>.
	/// </summary>
	[ConfigurationKeyName("LOCAL_NUGET_FEED_PATH")]
	public string? EnvLocalFeedPath { get; init; }

	public bool OverwriteExistingPackages { get; init; } = true;

	public bool ShutdownDotnetBuilderServer { get; init; } = true;

	public bool ClearPackageCache { get; init; } = true;

	/// <summary>
	/// Resolves the configured local feed path, falling back to the env-bound value
	/// (<c>PublishLocalNuGet__LOCAL_NUGET_FEED_PATH</c>) and then to the plain
	/// <c>LOCAL_NUGET_FEED_PATH</c> process environment variable.
	/// </summary>
	public string? GetLocalFeedPath()
	{
		if (!string.IsNullOrWhiteSpace(LocalFeedPath))
			return LocalFeedPath;

		if (!string.IsNullOrWhiteSpace(EnvLocalFeedPath))
			return EnvLocalFeedPath;

		var processValue = Environment.GetEnvironmentVariable("LOCAL_NUGET_FEED_PATH");
		return string.IsNullOrWhiteSpace(processValue) ? null : processValue;
	}

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		var localFeedPath = GetLocalFeedPath();
		if (string.IsNullOrWhiteSpace(localFeedPath))
		{
			yield return new ValidationResult(
				"LocalFeedPath (or LOCAL_NUGET_FEED_PATH) is required.",
				[nameof(LocalFeedPath)]
			);
			yield break;
		}

		// Path.IsPathRooted("p:foo") returns true, but a drive-relative path like "p:foo" is NOT an
		// absolute path: Path.GetFullPath resolves it against the current directory and can silently
		// copy packages to an unintended location. This is the classic signature of a Windows path whose
		// backslashes were stripped by a sh-style shell, e.g. 'p:\_sync-projects\.local-nuget\'.
		if (localFeedPath.Length >= 2 && localFeedPath[1] == ':')
		{
			var hasSeparatorAfterDrive =
				localFeedPath.Length >= 3
				&& (
					localFeedPath[2] == Path.DirectorySeparatorChar
					|| localFeedPath[2] == Path.AltDirectorySeparatorChar
				);
			if (!hasSeparatorAfterDrive)
			{
				yield return new ValidationResult(
					$"LocalFeedPath '{localFeedPath}' is drive-relative, not an absolute path. "
						+ "This is usually caused by the shell stripping backslashes from a Windows path such as "
						+ $"'p:\\_sync-projects\\.local-nuget\\'. Use forward slashes instead, e.g. "
						+ "'p:/_sync-projects/.local-nuget/'.",
					[nameof(LocalFeedPath)]
				);
				yield break;
			}
		}

		if (!Path.IsPathRooted(localFeedPath))
		{
			yield return new ValidationResult(
				$"LocalFeedPath must be an absolute path. Received: '{localFeedPath}'.",
				[nameof(LocalFeedPath)]
			);
			yield break;
		}

		var root = Path.GetPathRoot(localFeedPath);
		if (string.IsNullOrEmpty(root))
		{
			yield return new ValidationResult(
				$"LocalFeedPath could not be parsed. Received: '{localFeedPath}'.",
				[nameof(LocalFeedPath)]
			);
			yield break;
		}

		var lastChar = root[^1];
		if (lastChar == Path.DirectorySeparatorChar || lastChar == Path.AltDirectorySeparatorChar)
			yield break;

		if (root.StartsWith(@"\\", StringComparison.Ordinal) || root.StartsWith("//", StringComparison.Ordinal))
			yield break;

		yield return new ValidationResult(
			$"LocalFeedPath must be an absolute path (e.g. 'C:\\folder' or '\\\\server\\share'). Received: '{localFeedPath}'.",
			[nameof(LocalFeedPath)]
		);
	}
}