using System.Globalization;
using System.Text;

namespace Purview.Build.Release;

/// <summary>
/// Renders a <see cref="ReleaseExplanation"/> for a human.
/// </summary>
static class ReleaseExplanationText
{
	static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	public static string Render(ReleaseExplanation explanation)
	{
		ArgumentNullException.ThrowIfNull(explanation);

		StringBuilder builder = new();

		if (explanation.Simulated)
		{
			builder.AppendLine(
				"SIMULATED: release context was supplied through Release:Context:*. This verdict describes a"
			);
			builder.AppendLine("hypothetical release, not the real state of this repository.");
			builder.AppendLine();
		}

		builder.AppendLine(Culture, $"Verdict: {explanation.Verdict} (exit code {explanation.ExitCode})");
		builder.AppendLine(Culture, $"  {explanation.Message}");
		builder.AppendLine();

		AppendConfiguration(builder, explanation.Configuration);
		AppendVersion(builder, explanation.Version);
		AppendPolicy(builder, explanation.Policy, explanation.Ref, explanation.RefSource);
		AppendRules(builder, explanation);
		AppendPublication(builder, explanation.Publication);

		if (explanation.Warnings.Count > 0)
		{
			builder.AppendLine("Warnings:");
			foreach (var warning in explanation.Warnings)
				builder.AppendLine(Culture, $"  {warning}");

			builder.AppendLine();
		}

		return builder.ToString().TrimEnd();
	}

	static void AppendConfiguration(StringBuilder builder, ConfigurationReport configuration)
	{
		builder.AppendLine("Configuration:");
		builder.AppendLine(
			configuration.ResolvedPath is null
				? "  resolved: none found (using built-in defaults)"
				: $"  resolved: {configuration.ResolvedPath} (via {configuration.Source})"
		);

		if (configuration.ProbeTrail.Count > 0)
		{
			builder.AppendLine("  probe trail:");
			foreach (var probe in configuration.ProbeTrail)
			{
				builder.AppendLine(
					Culture,
					$"    [{(probe.Exists ? "found" : "     ")}] {probe.RelativePath}"
				);
			}
		}

		if (configuration.ShadowedPaths.Count > 0)
		{
			builder.AppendLine("  shadowed (NOT read):");
			foreach (var path in configuration.ShadowedPaths)
				builder.AppendLine(Culture, $"    {path}");
		}

		builder.AppendLine(
			configuration.UserConfigActive
				? $"  user config: {configuration.UserConfigPath} (active)"
				: "  user config: not active"
		);
		builder.AppendLine();
	}

	static void AppendVersion(StringBuilder builder, VersionReport version)
	{
		builder.AppendLine(
			Culture,
			$"Version: '{version.Raw}' from {version.Source} ({version.Strictness} strictness)"
		);

		if (version.Units.Count == 0)
		{
			builder.AppendLine("  no release units (the version could not be parsed)");
		}
		else
		{
			builder.AppendLine("  release units:");
			foreach (var unit in version.Units)
			{
				var label = unit.IsPrerelease
					? $"  prerelease label={unit.PrereleaseLabel}"
					: string.Empty;

				builder.AppendLine(
					Culture,
					$"    {unit.Id}  version={unit.Version}  line={unit.Line}  channel={unit.Channel}  tag={unit.Tag}  source={unit.Source}{label}"
				);
			}
		}

		builder.AppendLine();
	}

	static void AppendPolicy(
		StringBuilder builder,
		PolicyReport policy,
		string gitRef,
		string refSource
	)
	{
		builder.AppendLine(Culture, $"Policy: {policy.Name}");

		if (policy.InheritanceChain.Count > 1)
			builder.AppendLine(Culture, $"  inherits: {string.Join(" <- ", policy.InheritanceChain)}");

		builder.AppendLine(Culture, $"  rules: {string.Join(", ", policy.ResolvedRules)}");
		builder.AppendLine(Culture, $"  stable refs: {RefMatcher.Describe(policy.StableRefs)}");

		if (policy.ServicingRefs.Count > 0)
			builder.AppendLine(Culture, $"  servicing refs: {RefMatcher.Describe(policy.ServicingRefs)}");

		if (policy.AllowFourPart)
			builder.AppendLine(Culture, $"  four-part refs: {RefMatcher.Describe(policy.FourPartRefs)}");

		builder.AppendLine(
			Culture,
			$"  evaluated ref: {(string.IsNullOrEmpty(gitRef) ? "(none)" : gitRef)} (from {refSource})"
		);
		builder.AppendLine();
	}

	static void AppendRules(StringBuilder builder, ReleaseExplanation explanation)
	{
		builder.AppendLine("Rules, in evaluation order:");

		foreach (var rule in explanation.Rules)
			builder.AppendLine(Culture, $"  {rule.Verdict,-4}  {rule.Message}");

		if (explanation.ShortCircuitedAt is not null)
		{
			builder.AppendLine(
				Culture,
				$"  short-circuited at {explanation.ShortCircuitedAt}; later rules were not evaluated."
			);
		}

		builder.AppendLine();
	}

	static void AppendPublication(StringBuilder builder, PublicationReport publication)
	{
		builder.AppendLine("Publication that would result:");
		builder.AppendLine(Culture, $"  Release__Mode:         {publication.Mode}");
		builder.AppendLine(Culture, $"  Release:Publish:       {publication.Publish}");
		builder.AppendLine(Culture, $"  Release:GitHubRelease: {publication.GitHubRelease}");
		builder.AppendLine(Culture, $"  Release:Channel:       {publication.Channel}");
		builder.AppendLine(Culture, $"  feed URL:              {publication.FeedUrl}");

		if (publication.DryRun)
			builder.AppendLine("  Release:DryRun is set, so nothing would actually be pushed or tagged.");

		builder.AppendLine();
	}
}
