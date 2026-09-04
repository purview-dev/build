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

		var restoreResult = await RestoreWithRetryAsync(Restore, maxAttempts, context, cancellationToken);

		// Restore worked, now run the linter
		return await context.Shell.Command.ExecuteCommandLineTool(
			DotNetCLIOptions.Create("tool", "run", "csharpier", "check", repositoryRoot),
			new() { WorkingDirectory = repositoryRoot },
			cancellationToken: cancellationToken
		);
	}

	static async Task<CommandResult> RestoreWithRetryAsync(
		Func<Task<CommandResult>> restore,
		int maxAttempts,
		IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		Exception? lastException = null;
		CommandResult? lastResult = null;

		for (var attempt = 1; attempt <= maxAttempts; attempt++)
		{
			try
			{
				var result = await restore();
				if (result.ExitCode == 0)
					return result;

				lastException = null;
				lastResult = result;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				lastException = ex;
				lastResult = null;
			}

			if (attempt < maxAttempts)
			{
				context.Logger.LogWarning(
					lastException,
					"dotnet tool restore failed (attempt {Attempt} of {MaxAttempts}). Retrying...",
					attempt,
					maxAttempts
				);

				await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
			}
		}

		if (lastException is not null)
			throw lastException;

		if (lastResult is not null)
			throw new InvalidOperationException($"dotnet tool restore failed with exit code {lastResult.ExitCode}.");

		throw new InvalidOperationException("dotnet tool restore failed.");
	}
}