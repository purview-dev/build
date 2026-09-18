using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics.CodeAnalysis;

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
					: SkipDecision.Skip(
						"Linting is disabled. Set Build__RunLint=true to enable it."
					)
			)
			.Build();

	protected override async Task<CommandResult?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		var repositoryRoot = PathHelpers.FindRepositoryRoot();

		if (settings.Value.ProjectType == ProjectType.Web)
			return await RunWebLintAsync(context, repositoryRoot, cancellationToken);

		var dotnet = context.DotNet();

		const int maxAttempts = 3;
		Task<CommandResult> Restore() =>
			dotnet.Tool.Restore(
				new()
				{
					Interactive = false,
					ToolManifest = Path.Combine(repositoryRoot, ".config", "dotnet-tools.json"),
				},
				new() { WorkingDirectory = repositoryRoot },
				cancellationToken
			);

		var restoreResult = await RestoreWithRetryAsync(
			Restore,
			maxAttempts,
			context,
			cancellationToken
		);

		// Restore worked, now run the linter
		return await context.Shell.Command.ExecuteCommandLineTool(
			DotNetCLIOptions.Create("tool", "run", "csharpier", "check", repositoryRoot),
			new() { WorkingDirectory = repositoryRoot },
			cancellationToken: cancellationToken
		);
	}

	async Task<CommandResult?> RunWebLintAsync(
		IModuleContext context,
		string repositoryRoot,
		CancellationToken cancellationToken
	)
	{
		var scripts = WebScripts.ReadScripts(repositoryRoot);

		async Task<CommandResult?> RunIfDeclaredAsync(
			string command,
			string description
		)
		{
			var scriptName = WebScripts.GetScriptName(command);
			if (scriptName is null || !scripts.ContainsKey(scriptName))
			{
				context.Logger.LogInformation(
					"Skipping Web {Description}: the repository declares no '{Script}' script.",
					description,
					scriptName ?? command
				);

				return null;
			}

			var result = await context.Shell.Command.ExecuteCommandLineTool(
				BunCLIOptions.FromCommand(command),
				new() { WorkingDirectory = repositoryRoot },
				cancellationToken: cancellationToken
			);

			return result.ExitCode == 0 ? null : result;
		}

		var formatResult = await RunIfDeclaredAsync(
			settings.Value.WebFormatCheckCommand,
			"format check"
		);
		if (formatResult is not null)
			return formatResult;

		return await RunIfDeclaredAsync(settings.Value.WebLintCommand, "lint");
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
			throw new InvalidOperationException(
				$"dotnet tool restore failed with exit code {lastResult.ExitCode}."
			);

		throw new InvalidOperationException("dotnet tool restore failed.");
	}
}
