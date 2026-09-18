using ModularPipelines.Options;

namespace Purview.Build.Helpers;

public sealed record BunCLIOptions : CommandLineToolOptions
{
	public static BunCLIOptions Create(params string[] commandParts) =>
		new() { Tool = "bun", CommandParts = commandParts };

	/// <summary>
	/// Builds options from a full command-line string (e.g. <c>bun run build</c>). The leading
	/// <c>bun</c> tool token is stripped so it is not duplicated when the tool is executed.
	/// </summary>
	public static BunCLIOptions FromCommand(string command)
	{
		ArgumentNullException.ThrowIfNull(command);

		var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var commandParts = parts[0].Equals("bun", StringComparison.OrdinalIgnoreCase)
			? parts.Skip(1).ToArray()
			: parts;

		return new() { Tool = "bun", CommandParts = commandParts };
	}
}