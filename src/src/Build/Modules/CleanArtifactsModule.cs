using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

/// <summary>
/// Resets <see cref="BuildSettings.ArtifactsFolder"/> before any other module runs.
/// </summary>
/// <remarks>
/// The artifacts folder is shared output: the pack step writes it, validation inspects every package in it,
/// publishing moves packages out of it, and the release step can upload its contents. Clearing it first keeps
/// a run deterministic: a stale package from an earlier (or differently configured) build can no longer be
/// validated, published, or uploaded. Skipped when <see cref="BuildSettings.CleanArtifacts"/> is false or when
/// packing is disabled, so a run that only inspects existing artifacts keeps them.
/// </remarks>
[ModuleCategory("Build")]
public sealed class CleanArtifactsModule(IOptions<BuildSettings> settings) : Module<CommandResult>
{
	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				!settings.Value.CleanArtifacts || !settings.Value.RunPack
					? SkipDecision.Skip(
						"Clearing the artifacts folder is disabled. Set Build__CleanArtifacts=true and Build__RunPack=true to enable it."
					)
					: SkipDecision.DoNotSkip
			)
			.Build();

	protected override Task<CommandResult?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(CleanArtifactsModule));

		var artifactsFolder = Path.GetFullPath(settings.Value.ArtifactsFolder);

		if (Directory.Exists(artifactsFolder))
			context.Logger.LogInformation(
				"Clearing the artifacts folder '{ArtifactsFolder}'.",
				settings.Value.ArtifactsFolder
			);

		PathHelpers.ResetDirectory(artifactsFolder);

		return Task.FromResult<CommandResult?>(null);
	}
}