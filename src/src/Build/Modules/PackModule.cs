using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using Purview.Build.Release;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Purview.Build.Modules;

[ModuleCategory("Build")]
[DependsOn<RunTestsModule>]
[DependsOn<VersionModule>]
public sealed class PackModule(IOptions<BuildSettings> settings) : Module<CommandResult[]>
{
	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				!settings.Value.RunPack
					? SkipDecision.Skip(
						"Packing is disabled. Set Build__RunPack=true to enable it."
					)
					: SkipDecision.DoNotSkip
			)
			.Build();

	protected override async Task<CommandResult[]?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(PackModule));

		var versionResult = await context.GetModule<VersionModule>();
		var units =
			versionResult.ValueOrDefault
			?? throw new InvalidOperationException(
				"The version was not produced by the version module."
			);

		Directory.CreateDirectory(settings.Value.ArtifactsFolder);

		List<CommandResult> results = [];

		// Every version source yields a single unit today; packing per unit means a future
		// multi-unit source needs no change here.
		foreach (var unit in units.Units)
		{
			if (settings.Value.ProjectType == ProjectType.Web)
			{
				PackWebArtifact(context, unit);
				continue;
			}

			results.Add(await PackDotNetAsync(context, unit, cancellationToken));
		}

		return [.. results];
	}

	async Task<CommandResult> PackDotNetAsync(
		IModuleContext context,
		ReleaseUnit unit,
		CancellationToken cancellationToken
	)
	{
		var version = unit.Version.ToString();

		var result = await context
			.DotNet()
			.Pack(
				new DotNetPackOptions
				{
					ProjectSolution = settings.Value.Solution,
					Configuration = settings.Value.Configuration,
					Output = settings.Value.ArtifactsFolder,
					Properties = [("PackageVersion", version), ("Version", version)],
				},
				cancellationToken: cancellationToken
			);

		context.Logger.LogInformation("Packed version {Version}.", version);

		return result;
	}

	void PackWebArtifact(IModuleContext context, ReleaseUnit unit)
	{
		var version = unit.Version.ToString();
		var repositoryRoot = PathHelpers.FindRepositoryRoot();
		var packageName = WebScripts.ReadPackageName(repositoryRoot);

		var outputDirectory = Path.GetFullPath(
			Path.Combine(repositoryRoot, settings.Value.WebBuildOutput)
		);
		if (!Directory.Exists(outputDirectory))
		{
			context.Logger.LogWarning(
				"The Web build output '{WebBuildOutput}' does not exist. Nothing was packed.",
				settings.Value.WebBuildOutput
			);

			return;
		}

		var zipPath = Path.Combine(
			Path.GetFullPath(settings.Value.ArtifactsFolder),
			$"{packageName}-{version}.zip"
		);
		ZipFile.CreateFromDirectory(
			outputDirectory,
			zipPath,
			CompressionLevel.Optimal,
			includeBaseDirectory: false
		);

		context.Logger.LogInformation(
			"Packed Web build output into {ZipPath}.",
			Path.GetFileName(zipPath)
		);
		context.Logger.LogInformation("Packed version {Version}.", version);
	}
}
