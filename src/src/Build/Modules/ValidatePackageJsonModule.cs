using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using Purview.Build.PackageJson;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Purview.Build.Modules;

/// <summary>
/// Validates the repository root <c>package.json</c> against the content a best-in-class package of
/// the repository's kind is expected to declare.
/// </summary>
/// <remarks>
/// Opt-in through <c>PackageJson:Enabled</c>: the file already exists everywhere, so holding it to a
/// standard is a deliberate decision. The same checks are always available on their own through
/// <c>purview-build package-validate</c>, which needs no configuration at all.
/// </remarks>
[ModuleCategory("Build")]
[DependsOn<VersionModule>]
public sealed class ValidatePackageJsonModule(
	IOptions<BuildSettings> buildSettings,
	IOptions<VersionSettings> versionSettings,
	IOptions<PackageJsonValidationSettings> packageJsonSettings
) : Module<PackageJsonValidationReport>
{
	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				packageJsonSettings.Value.Enabled
					? SkipDecision.DoNotSkip
					: SkipDecision.Skip(
						"package.json validation is opt-in. Set PackageJson__Enabled=true to enable it, "
							+ "or run 'purview-build package-validate' on its own."
					)
			)
			.Build();

	protected override async Task<PackageJsonValidationReport?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(ValidatePackageJsonModule));

		var versionResult = await context.GetModule<VersionModule>();
		var resolvedVersion = versionResult.ValueOrDefault?.RawVersion;

		var report = PackageJsonValidationRunner.Run(
			packageJsonSettings.Value,
			buildSettings.Value,
			versionSettings.Value,
			PathHelpers.FindRepositoryRoot(),
			resolvedVersion
		);

		foreach (var result in report.Results)
		{
			context.Logger.LogInformation(
				"  {Outcome,-7} {RuleId} {Message}",
				result.Outcome,
				result.RuleId,
				result.Message
			);
		}

		context.Summary.KeyValue("package.json", "Profile", report.Profile);
		context.Summary.KeyValue(
			"package.json",
			"Errors",
			report.ErrorCount.ToString(CultureInfo.InvariantCulture)
		);
		context.Summary.KeyValue(
			"package.json",
			"Warnings",
			report.WarningCount.ToString(CultureInfo.InvariantCulture)
		);

		if (!report.Succeeded)
			throw new InvalidOperationException(PackageJsonValidationReportText.Render(report));

		return report;
	}
}
