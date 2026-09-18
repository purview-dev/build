using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

[ModuleCategory("Build")]
[DependsOn<RestoreModule>]
public sealed class BuildModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
	protected override async Task<CommandResult?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		if (settings.Value.ProjectType == ProjectType.Web)
		{
			var repositoryRoot = PathHelpers.FindRepositoryRoot();

			// When the repo declares a data:sync script and the build command is left at the
			// default, refresh data (docs mirrors, release metadata) before building so a fresh
			// checkout can build offline afterwards. Overriding WebBuildCommand takes full control.
			if (
				settings.Value.WebBuildCommand == "bun run build"
				&& WebScripts.HasScript(repositoryRoot, "data:sync")
			)
			{
				var syncResult = await context.Shell.Command.ExecuteCommandLineTool(
					BunCLIOptions.Create("run", "data:sync"),
					new() { WorkingDirectory = repositoryRoot },
					cancellationToken: cancellationToken
				);
				if (syncResult.ExitCode != 0)
					return syncResult;
			}

			return await context.Shell.Command.ExecuteCommandLineTool(
				BunCLIOptions.FromCommand(settings.Value.WebBuildCommand),
				new() { WorkingDirectory = repositoryRoot },
				cancellationToken: cancellationToken
			);
		}

		return await context
			.DotNet()
			.Build(
				new()
				{
					ProjectSolution = settings.Value.Solution,
					Configuration = settings.Value.Configuration,
					NoRestore = true,
				},
				cancellationToken: cancellationToken
			);
	}
}
