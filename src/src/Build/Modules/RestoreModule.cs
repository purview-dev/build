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
		return await context
			.DotNet()
			.Restore(
				new DotNetRestoreOptions { ProjectSolution = settings.Value.Solution },
				cancellationToken: cancellationToken
			);
	}
}
