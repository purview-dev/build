using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

[ModuleCategory("Build")]
public sealed class RestoreModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
	protected override async Task<CommandResult?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		if (settings.Value.ProjectType == ProjectType.Web)
		{
			return await context
				.Shell.Command.ExecuteCommandLineTool(
					BunCLIOptions.FromCommand(settings.Value.WebInstallCommand),
					cancellationToken: cancellationToken
				);
		}

		return await context
			.DotNet()
			.Restore(
				new DotNetRestoreOptions { ProjectSolution = settings.Value.Solution },
				cancellationToken: cancellationToken
			);
	}
}
