using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Purview.Build.Modules;

[ModuleCategory("Build")]
[DependsOn<RunTestsModule>]
[DependsOn<VersionModule>]
public sealed class PackModule(IOptions<BuildSettings> settings) : Module<CommandResult>
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

	protected override async Task<CommandResult?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		var versionResult = await context.GetModule<VersionModule>();
		var nugetVersion =
			versionResult.ValueOrDefault
			?? throw new InvalidOperationException(
				"The version was not produced by the version module."
			);

		Directory.CreateDirectory(settings.Value.ArtifactsFolder);

		if (settings.Value.ProjectType == ProjectType.Web)
			return PackWebArtifact(context, nugetVersion.ToString());

		var version = nugetVersion.ToString();
		return await context
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
	}

	CommandResult? PackWebArtifact(IModuleContext context, string version)
	{
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

			return null;
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

		return null;
	}
}
