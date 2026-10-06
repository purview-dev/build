using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Modules;
using Purview.Build.Release;
using Purview.Build.Version;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

/// <summary>
/// Resolves the release units once, so every downstream module reads the same version rather than
/// recomputing it.
/// </summary>
[ModuleCategory("Build")]
[DependsOn<CleanArtifactsModule>]
public sealed class VersionModule(
	IOptions<VersionSettings> versionSettings,
	IOptions<ReleaseSettings> releaseSettings
) : Module<ReleaseUnitSet>
{
	protected override async Task<ReleaseUnitSet?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(VersionModule));

		var provider = VersionProviders.For(versionSettings.Value.Source);

		var units = await provider.ResolveAsync(
			new VersionProviderContext(
				RepositoryRoot: Environment.CurrentDirectory,
				ChannelName: releaseSettings.Value.Channel,
				Strictness: versionSettings.Value.Strictness
			),
			cancellationToken
		);

		context.Summary.KeyValue("Version", "Package version", units.RawVersion);

		if (units.Units.Count > 1)
		{
			context.Summary.KeyValue(
				"Version",
				"Release units",
				string.Join(", ", units.Units.Select(unit => unit.Id))
			);
		}

		return units;
	}
}
