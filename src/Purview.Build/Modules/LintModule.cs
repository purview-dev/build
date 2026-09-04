using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;

namespace Purview.Build.Modules;

[ModuleCategory("Build")]
public sealed class LintModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				settings.Value.RunLint
					? SkipDecision.DoNotSkip
					: SkipDecision.Skip("Linting is disabled. Set Build__RunLint=true to enable it.")
			)
			.Build();

	protected override async Task<CommandResult?> ExecuteAsync(
		IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		var repositoryRoot = PathHelpers.FindRepositoryRoot();
		var dotnet = context.DotNet();

		const int maxAttempts = 3;
		Task<CommandResult> Restore() =>
			dotnet.Tool.Restore(
				new() { Interactive = false, ToolManifest = Path.Combine(repositoryRoot, ".config", "dotnet-tools.json") },
				new() { WorkingDirectory = repositoryRoot },
				cancellationToken
			);

		var restoreResult = await Restore();
		for (var attempt = 1; restoreResult.ExitCode != 0 && attempt < maxAttempts; attempt++)
		{
			context.Logger.LogWarning(
				"dotnet tool restore failed (attempt {Attempt} of {MaxAttempts}). Retrying...",
				attempt,
				maxAttempts
			);

			await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

			restoreResult = await Restore();
		}
		if (restoreResult.ExitCode != 0)
			return restoreResult;

		// Restore worked, now run the linter
		return await context.Shell.Command.ExecuteCommandLineTool(
			DotNetCLIOptions.Create("tool", "run", "csharpier", "check", repositoryRoot),
			new() { WorkingDirectory = repositoryRoot },
			cancellationToken: cancellationToken
		);
	}
}