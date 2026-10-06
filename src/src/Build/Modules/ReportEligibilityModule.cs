using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Modules;
using Purview.Build.Release;
using System.Diagnostics.CodeAnalysis;

namespace Purview.Build.Modules;

/// <summary>
/// Evaluates the release-eligibility rules and reports the verdict, without acting on it.
/// </summary>
/// <remarks>
/// Deliberately category <c>Build</c>, not <c>Release</c>: it observes, and it never declines a
/// release. The workflow owns the decision — it runs <c>release-explain</c>, reads the verdict and
/// sets <c>Release__Mode</c> — and the tool must not silently refuse to release when it has been
/// asked to. This module exists so a real pipeline run still records WHY a release was or was not
/// eligible, which a workflow-only gate never surfaced in the tool's own log.
///
/// Every failure path here is a warning. A rule evaluation that throws (a misconfigured policy, an
/// unreadable simulated tag list) must not fail a build that would otherwise succeed.
/// </remarks>
[ModuleCategory("Build")]
[DependsOn<VersionModule>]
public sealed class ReportEligibilityModule(
	IOptions<ReleaseSettings> releaseSettings,
	IOptions<VersionSettings> versionSettings
) : Module<EligibilityDecision>
{
	protected override async Task<EligibilityDecision?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(ReportEligibilityModule));

		var versionResult = await context.GetModule<VersionModule>();
		var units = versionResult.ValueOrDefault;

		if (units is null)
		{
			context.Logger.LogWarning(
				"Release eligibility was not evaluated: the version module produced no release units."
			);

			return null;
		}

		try
		{
			return Report(context, units);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			context.Logger.LogWarning(
				exception,
				"Release eligibility could not be evaluated. This is reported, not enforced, so the run continues."
			);

			return null;
		}
	}

	EligibilityDecision Report(IModuleContext context, ReleaseUnitSet units)
	{
		var release = releaseSettings.Value;

		var releaseContext = ReleaseContextProvider.Resolve(release.Context, Environment.CurrentDirectory);
		var policy = EligibilityPolicies.Resolve(release.Eligibility, release.Eligibility.Policy);

		var input = EligibilityInput.Create(
			units.RawVersion,
			versionSettings.Value.Strictness,
			releaseContext,
			release.Channel,
			release.ResolveChannel(),
			units.Source
		);

		var decision = EligibilityEvaluator.Evaluate(input, policy);

		context.Logger.LogInformation(
			"Release eligibility ({Policy}): {Verdict} — {Message}",
			policy.Name,
			decision.Verdict,
			decision.Message
		);

		foreach (var result in decision.Results)
			context.Logger.LogInformation("  {Outcome,-4} {Message}", result.Outcome, result.Message);

		if (releaseContext.Simulated)
		{
			context.Logger.LogWarning(
				"Release:Context:* supplied a simulated release context, so this eligibility report is "
					+ "hypothetical."
			);
		}

		context.Summary.KeyValue("Release", "Eligibility policy", policy.Name);
		context.Summary.KeyValue("Release", "Eligibility verdict", decision.Verdict.ToString());

		if (decision.RuleId is not null)
			context.Summary.KeyValue("Release", "Decided by", decision.RuleId);

		return decision;
	}
}
