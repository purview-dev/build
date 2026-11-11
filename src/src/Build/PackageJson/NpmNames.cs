namespace Purview.Build.PackageJson;

/// <summary>
/// npm's rules for a package name, as the registry enforces them.
/// </summary>
static class NpmNames
{
	const int MaxLength = 214;

	/// <summary>
	/// Describes why <paramref name="name"/> would be rejected by npm, or <see langword="null"/> when
	/// it is acceptable.
	/// </summary>
	public static string? DescribeProblem(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "it is empty";

		if (name.Length > MaxLength)
			return $"it is longer than {MaxLength} characters";

		if (name.Any(char.IsUpper))
			return "npm package names must be lowercase";

		if (name.Trim() != name)
			return "leading or trailing whitespace is not allowed";

		if (name is "node_modules" or "favicon.ico")
			return "it is a name npm reserves";

		var start = 0;

		if (name[0] == '@')
		{
			var separator = FindSeparator(name);
			if (separator < 0)
				return "a scoped name must be written '@scope/name'";

			var scope = name[1..separator];
			if (scope.Length == 0)
				return "the scope is empty";

			if (DescribePartProblem(scope) is { } scopeProblem)
				return $"the scope '{scope}' {scopeProblem}";

			start = separator + 1;
		}
		else if (FindSeparator(name) >= 0)
		{
			return "only a scoped name ('@scope/name') may contain '/'";
		}

		var leaf = name[start..];
		if (leaf.Length == 0)
			return "the name after the scope is empty";

		return DescribePartProblem(leaf);
	}

	static int FindSeparator(string value)
	{
		for (var index = 0; index < value.Length; index++)
		{
			if (value[index] == '/')
				return index;
		}

		return -1;
	}

	static string? DescribePartProblem(string part)
	{
		if (part.Length > 0 && part[0] is '.' or '_')
			return "must not start with '.' or '_'";

		foreach (var character in part)
		{
			if (!IsAllowed(character))
				return $"contains '{character}', which npm does not allow";
		}

		return null;
	}

	static bool IsAllowed(char character) =>
		character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_';
}
