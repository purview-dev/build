namespace Purview.Build.Release;

/// <summary>
/// Matches a git ref against the patterns in an eligibility policy.
/// </summary>
/// <remarks>
/// Deliberately narrow: an exact match, or a single trailing <c>*</c> wildcard covering the rest of
/// the ref (so <c>refs/heads/release/*</c> matches <c>refs/heads/release/2.0</c> and
/// <c>refs/heads/release/2.0/hotfix</c>). Comparison is ordinal and case-sensitive, because git refs
/// are.
/// </remarks>
static class RefMatcher
{
	public static bool Matches(string? gitRef, IReadOnlyList<string> patterns)
	{
		if (string.IsNullOrWhiteSpace(gitRef) || patterns.Count == 0)
			return false;

		foreach (var pattern in patterns)
		{
			if (MatchesPattern(gitRef, pattern))
				return true;
		}

		return false;
	}

	static bool MatchesPattern(string gitRef, string pattern)
	{
		if (string.IsNullOrWhiteSpace(pattern))
			return false;

		if (!pattern.EndsWith('*'))
			return string.Equals(gitRef, pattern, StringComparison.Ordinal);

		var prefix = pattern[..^1];
		return gitRef.StartsWith(prefix, StringComparison.Ordinal);
	}

	/// <summary>
	/// Renders a pattern list for a rule message, so a failure says what was expected.
	/// </summary>
	public static string Describe(IReadOnlyList<string> patterns) =>
		patterns.Count == 0 ? "(none configured)" : string.Join(", ", patterns);
}
