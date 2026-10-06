using Purview.Build.Configuration;
using Purview.Build.Version;

namespace Purview.Build.Release;

/// <summary>
/// Composes a <see cref="ReleaseExplanation"/> from a run's configuration, without running any
/// module or mutating anything.
/// </summary>
static class ReleaseExplainer
{
	public static async Task<ReleaseExplanation> ExplainAsync(
		PipelineStartup startup,
		IConfiguration configuration,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(startup);
		ArgumentNullException.ThrowIfNull(configuration);

		var release =
			configuration.GetSection(ReleaseSettings.SectionName).Get<ReleaseSettings>() ?? new();
		var version =
			configuration.GetSection(VersionSettings.SectionName).Get<VersionSettings>() ?? new();
		var nuget = configuration.GetSection(NuGetSettings.SectionName).Get<NuGetSettings>() ?? new();

		var context = ReleaseContextProvider.Resolve(release.Context, startup.RepositoryRoot);
		var policy = EligibilityPolicies.Resolve(release.Eligibility, release.Eligibility.Policy);

		var rawVersion = await ReadRawVersionAsync(
			version,
			startup.RepositoryRoot,
			cancellationToken
		);

		var channel = release.ResolveChannel();
		var input = EligibilityInput.Create(
			rawVersion,
			version.Strictness,
			context,
			release.Channel,
			channel,
			version.Source.ToString()
		);

		var decision = EligibilityEvaluator.Evaluate(input, policy);

		return Compose(startup, release, version, nuget, context, policy, input, decision);
	}

	/// <summary>
	/// Reads the version without validating it, so an invalid version is explained by REL001 rather
	/// than crashing the explanation.
	/// </summary>
	static async Task<string> ReadRawVersionAsync(
		VersionSettings version,
		string repositoryRoot,
		CancellationToken cancellationToken
	)
	{
		if (version.Source != VersionSource.PackageJson)
			return string.Empty;

		try
		{
			return await PackageJsonVersionProvider.ReadRawVersionAsync(
				repositoryRoot,
				cancellationToken
			);
		}
		catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
		{
			return string.Empty;
		}
	}

	static ReleaseExplanation Compose(
		PipelineStartup startup,
		ReleaseSettings release,
		VersionSettings version,
		NuGetSettings nuget,
		ReleaseContext context,
		ResolvedPolicy policy,
		EligibilityInput input,
		EligibilityDecision decision
	)
	{
		var mode = decision.ResolveMode(release.Mode);

		return new ReleaseExplanation
		{
			Verdict = decision.Verdict.ToString(),
			ExitCode = decision.ExitCode,
			ReleaseMode = mode.ToString(),
			Message = decision.Message,
			DecidedByRule = decision.RuleId,
			ShortCircuitedAt = decision.ShortCircuitRuleId,
			Simulated = context.Simulated,
			Ref = context.Ref,
			RefSource = context.RefSource,
			Configuration = new ConfigurationReport
			{
				ResolvedPath = startup.Resolution.Path,
				Source = startup.Resolution.Source.ToString(),
				ProbeTrail =
				[
					.. startup.Resolution.ProbeTrail.Select(probe => new ProbeReport
					{
						RelativePath = probe.RelativePath,
						FullPath = probe.FullPath,
						Exists = probe.Exists,
					}),
				],
				ShadowedPaths = startup.Resolution.ShadowedPaths,
				UserConfigPath = startup.Resolution.UserConfigPath,
				UserConfigActive = startup.Resolution.UserConfigActive,
			},
			Version = new VersionReport
			{
				Raw = input.RawVersion,
				Source = version.Source.ToString(),
				Strictness = version.Strictness.ToString(),
				Units = input.Unit is null
					? []
					: [ToReport(input.Unit)],
			},
			Policy = new PolicyReport
			{
				Name = policy.Name,
				InheritanceChain = policy.InheritanceChain,
				ResolvedRules = policy.RuleIds,
				StableRefs = policy.StableRefs,
				ServicingRefs = policy.ServicingRefs,
				TrunkRefs = policy.TrunkRefs,
				FourPartRefs = policy.FourPartRefs,
				AllowFourPart = policy.AllowFourPart,
			},
			Rules =
			[
				.. decision.Results.Select(result => new RuleReport
				{
					Id = result.RuleId,
					Verdict = DescribeOutcome(result.Outcome),
					Message = result.Message,
				}),
			],
			Publication = new PublicationReport
			{
				Mode = mode.ToString(),
				// Reported as the publication that WOULD result: a non-releasing verdict yields
				// Release__Mode=None, so nothing publishes regardless of the configured preset.
				Publish = mode == ReleaseMode.None ? false : release.ShouldPublish(),
				GitHubRelease = mode == ReleaseMode.None ? false : release.ShouldCreateGitHubRelease(),
				Channel = release.Channel,
				FeedUrl = release.ResolveChannel().FeedUrl ?? nuget.FeedUrl,
				DryRun = release.DryRun,
			},
			Warnings = ConfigurationChain.DescribeWarnings(startup.Resolution),
		};
	}

	/// <summary>
	/// The per-rule verdict strings in the JSON contract. Spelled out rather than lower-cased from
	/// the enum name, so the contract cannot drift if the enum is renamed.
	/// </summary>
	static string DescribeOutcome(RuleOutcome outcome) =>
		outcome switch
		{
			RuleOutcome.Pass => "pass",
			RuleOutcome.Skip => "skip",
			RuleOutcome.Fail => "fail",
			_ => "unknown",
		};

	static UnitReport ToReport(ReleaseUnit unit) =>
		new()
		{
			Id = unit.Id,
			Version = unit.Version.ToString(),
			IsPrerelease = unit.IsPrerelease,
			PrereleaseLabel = unit.PrereleaseLabel,
			Line = unit.Line,
			Channel = unit.Channel,
			Tag = unit.Tag,
			Source = unit.Source,
		};
}
