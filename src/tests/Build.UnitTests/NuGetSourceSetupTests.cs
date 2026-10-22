namespace Purview.Build;

/// <summary>
/// Pins the GitHub Packages wiring: one shared source configuration, used by every entry point, and
/// the publish opt-in evaluated only where a release can happen.
/// </summary>
/// <remarks>
/// These are text assertions because the behaviour they protect lives in YAML and bash, where a typo
/// is silent. The same reason <see cref="WorkflowParityTests"/> exists. The invariants matter:
/// restoring from the organisation feed must need no per-repository configuration, and a PR must never
/// gain a publication path from the opt-in variable.
/// </remarks>
public class NuGetSourceSetupTests
{
	const string SourceAction = "actions/nuget-sources/action.yml";
	const string SourceScript = "actions/nuget-sources/nuget-sources.sh";
	const string PruneScript = "actions/prune-github-packages/prune.sh";
	const string BuildWorkflow = ".github/workflows/purview-build.yml";
	const string ReleaseWorkflow = ".github/workflows/purview-release.yml";
	const string SelfHostedBuild = ".github/workflows/ci.yml";
	const string SelfHostedRelease = ".github/workflows/release.yml";
	const string CompositeAction = ".github/actions/purview-build/action.yml";

	[Test]
	public async Task SourceAction_RegistersNuGetOrgAndTheOrganisationFeed()
	{
		// Arrange
		var script = Read(SourceScript);

		// Act & Assert
		await Assert.That(script).Contains("https://api.nuget.org/v3/index.json");
		await Assert.That(script).Contains("https://nuget.pkg.github.com/${NAMESPACE}/index.json");
		await Assert.That(script).Contains("PURVIEW_NUGET_NAMESPACE");
	}

	[Test]
	public async Task SourceAction_AuthenticatesWithAMacroOnly()
	{
		// Arrange
		var script = Read(SourceScript);

		// Act & Assert
		// The password stored in the user configuration is a macro NuGet expands at restore time. If
		// the token itself were written, a self-hosted runner would keep it on disk between jobs.
		await Assert.That(script).Contains("CREDENTIAL_MACRO='%GITHUB_TOKEN%'");
		await Assert.That(script).Contains("CREDENTIAL_MACRO='%PURVIEW_NUGET_TOKEN%'");
		await Assert.That(script).Contains("--password \"$CREDENTIAL_MACRO\"");
		await Assert.That(script).Contains("--store-password-in-clear-text");
		await Assert
			.That(script)
			.DoesNotContain("--password \"$TOKEN\"")
			.Because("the token must never be written to the NuGet configuration.");
		await Assert.That(script).DoesNotContain("echo \"$TOKEN\"").Because("the token must never be logged.");
	}

	[Test]
	public async Task SourceConfiguration_RunsBeforeTheToolInstall()
	{
		// Arrange
		// A build-version pinned to a version only GitHub Packages has (NuGet.org not indexed yet) is
		// resolved by `dotnet tool install`, so the feed must exist before that step.
		var build = Read(BuildWorkflow);
		var release = Read(ReleaseWorkflow);
		var composite = Read(CompositeAction);

		// Act & Assert
		foreach (
			var (name, content) in new[]
			{
				(BuildWorkflow, build),
				(ReleaseWorkflow, release),
				(CompositeAction, composite),
			}
		)
		{
			var feed = content.IndexOf("nuget-sources", StringComparison.Ordinal);
			var install = content.IndexOf("Install shared build", StringComparison.Ordinal);

			await Assert
				.That(feed)
				.IsLessThan(install)
				.Because($"{name} must register the mirror before installing the tool.");
		}
	}

	[Test]
	public async Task SourceAction_UsesAnOrganisationReadTokenWhenProvided()
	{
		// Arrange
		var action = Read(SourceAction);
		var script = Read(SourceScript);
		var build = Read(BuildWorkflow);
		var release = Read(ReleaseWorkflow);
		var composite = Read(CompositeAction);

		// Act & Assert
		await Assert
			.That(action)
			.Contains("read:packages")
			.Because("the optional token exists to read packages without per-package Actions grants.");
		await Assert.That(script).Contains("PURVIEW_NUGET_TOKEN=$TOKEN");
		await Assert.That(script).Contains("PURVIEW_NUGET_TOKEN=");
		await Assert.That(build).Contains("secrets.PURVIEW_PACKAGES_READ_TOKEN || github.token");
		await Assert.That(release).Contains("secrets.PURVIEW_PACKAGES_READ_TOKEN || github.token");
		await Assert.That(composite).Contains("packages-read-token");
		await Assert.That(composite).Contains("inputs.packages-read-token || github.token");
	}

	[Test]
	public async Task SourceAction_LeavesTheRepositoryConfigurationAlone()
	{
		// Arrange
		var script = Read(SourceScript);

		// Act & Assert
		await Assert
			.That(script)
			.Contains(".nuget/NuGet/NuGet.Config")
			.Because("the source belongs in the user-level configuration, not the checkout.");
		await Assert
			.That(script)
			.DoesNotContain("--configfile nuget.config")
			.Because("a tracked repository file must never be rewritten by CI.");
	}

	[Test]
	public async Task SourceAction_SupportsCleanup()
	{
		// Arrange
		var action = Read(SourceAction);
		var script = Read(SourceScript);

		// Act & Assert
		await Assert.That(action).Contains("mode:");
		await Assert.That(action).Contains("remove");
		await Assert.That(script).Contains("cp \"$BACKUP\" \"$USER_CONFIG\"");
	}

	[Test]
	public async Task SourceAction_SupportsOptionalPackageSourceMapping()
	{
		// Arrange
		var action = Read(SourceAction);
		var script = Read(SourceScript);

		// Act & Assert
		await Assert.That(action).Contains("source-mapping:");
		await Assert.That(script).Contains("packageSourceMapping");
		await Assert
			.That(script)
			.Contains("<package pattern=\"*\" />")
			.Because("the safe default leaves every other package resolvable from nuget.org.");
		await Assert
			.That(script)
			.Contains("dotnet nuget locals http-cache --clear")
			.Because("a cached negative result must not defeat an explicit source requirement.");
	}

	[Test]
	public async Task EveryEntryPoint_UsesTheSameSourceAction()
	{
		// Arrange
		var build = Read(BuildWorkflow);
		var release = Read(ReleaseWorkflow);
		var selfBuild = Read(SelfHostedBuild);
		var selfRelease = Read(SelfHostedRelease);
		var composite = Read(CompositeAction);

		// Act & Assert
		await Assert.That(build).Contains("purview-dev/build/actions/nuget-sources@");
		await Assert.That(release).Contains("purview-dev/build/actions/nuget-sources@");
		await Assert.That(composite).Contains("purview-dev/build/actions/nuget-sources@");
		await Assert
			.That(selfBuild)
			.Contains("uses: ./actions/nuget-sources")
			.Because("this repository dogfoods the same action from the working tree.");
		await Assert.That(selfRelease).Contains("uses: ./actions/nuget-sources");
	}

	[Test]
	public async Task SourceConfiguration_RunsBeforeTheRepositoryRestore()
	{
		// Arrange
		var build = Read(BuildWorkflow);
		var release = Read(ReleaseWorkflow);
		var selfBuild = Read(SelfHostedBuild);
		var selfRelease = Read(SelfHostedRelease);

		// Act & Assert
		foreach (
			var (name, content, later) in new[]
			{
				(BuildWorkflow, build, "Run shared build"),
				(ReleaseWorkflow, release, "Run release pipeline"),
				(SelfHostedBuild, selfBuild, "Restore"),
				(SelfHostedRelease, selfRelease, "Restore"),
			}
		)
		{
			await Assert
				.That(content.IndexOf("nuget-sources", StringComparison.Ordinal))
				.IsLessThan(content.IndexOf(later, StringComparison.Ordinal))
				.Because($"{name} must register the feed before the repository is restored.");
		}
	}

	[Test]
	public async Task PublishOptIn_IsEvaluatedOnlyWhereAReleaseCanHappen()
	{
		// Arrange
		var release = Read(ReleaseWorkflow);
		var selfRelease = Read(SelfHostedRelease);
		var build = Read(BuildWorkflow);
		var selfBuild = Read(SelfHostedBuild);

		// Act & Assert
		await Assert.That(release).Contains("vars.PURVIEW_PUBLISH_GITHUB_PACKAGES");
		await Assert.That(release).Contains("NuGet__GitHubPackages__Enabled");
		await Assert.That(selfRelease).Contains("vars.PURVIEW_PUBLISH_GITHUB_PACKAGES");
		await Assert.That(selfRelease).Contains("NuGet__GitHubPackages__Enabled");
		await Assert
			.That(build)
			.DoesNotContain("PURVIEW_PUBLISH_GITHUB_PACKAGES")
			.Because("a PR build must not even read the publish switch.");
		await Assert
			.That(selfBuild)
			.DoesNotContain("PURVIEW_PUBLISH_GITHUB_PACKAGES")
			.Because("this repository's PR build must not read the publish switch.");
	}

	[Test]
	public async Task ReleaseWorkflows_GrantPackageWrite()
	{
		// Arrange
		var release = Read(ReleaseWorkflow);
		var selfRelease = Read(SelfHostedRelease);

		// Act & Assert
		await Assert.That(release).Contains("packages: write");
		await Assert.That(selfRelease).Contains("packages: write");
	}

	[Test]
	public async Task BuildWorkflows_GrantOnlyPackageRead()
	{
		// Arrange
		var build = Read(BuildWorkflow);
		var selfBuild = Read(SelfHostedBuild);

		// Act & Assert
		await Assert.That(build).Contains("packages: read");
		await Assert.That(build).DoesNotContain("packages: write");
		await Assert.That(selfBuild).Contains("packages: read");
		await Assert.That(selfBuild).DoesNotContain("packages: write");
	}

	[Test]
	public async Task Prune_OnlyRunsAfterASuccessfulReleaseWithTheOptIn()
	{
		// Arrange
		var release = Read(ReleaseWorkflow);
		var selfRelease = Read(SelfHostedRelease);
		var build = Read(BuildWorkflow);
		var composite = Read(CompositeAction);

		// Act & Assert
		await Assert.That(release).Contains("prune-github-packages");
		await Assert.That(release).Contains("vars.PURVIEW_PUBLISH_GITHUB_PACKAGES == 'true'");
		await Assert.That(selfRelease).Contains("prune-github-packages");
		await Assert.That(build).DoesNotContain("prune-github-packages");
		await Assert.That(composite).DoesNotContain("prune-github-packages");
	}

	[Test]
	public async Task Prune_KeepsTheRetentionWindowTheFloorAndTheJustPublishedVersion()
	{
		// Arrange
		var script = Read(PruneScript);

		// Act & Assert
		await Assert.That(script).Contains("CURRENT_VERSION");
		await Assert.That(script).Contains("it is the version just published");
		await Assert.That(script).Contains("RETENTION_HOURS");
		await Assert.That(script).Contains("cutoff=\"$((now - RETENTION_HOURS * 3600))\"");
		await Assert.That(script).Contains("floor=$((count - KEEP))");
		await Assert
			.That(script)
			.Contains("published within the ${RETENTION_HOURS}-hour retention window")
			.Because("a version published today must remain available for downstream acceleration.");
		await Assert
			.That(script)
			.Contains("is not on the mirror, so this release did not publish it")
			.Because("a package the release did not publish must keep its history.");
		await Assert
			.That(script)
			.Contains("select(.repository.full_name")
			.Because("only packages linked to this repository may be pruned.");
		await Assert.That(script).Contains("::warning").Because("cleanup is housekeeping and must not fail a release.");
	}

	[Test]
	public async Task Prune_IsDisabledWhenBothRetentionPoliciesAreOff()
	{
		// Arrange
		var script = Read(PruneScript);

		// Act & Assert
		await Assert.That(script).Contains("[ \"$KEEP\" -le 0 ] && [ \"$RETENTION_HOURS\" -le 0 ]");
		await Assert.That(script).Contains("pruning is disabled");
	}

	[Test]
	public async Task Prune_ReadsTheRetentionWindowFromTheRepositoryVariable()
	{
		// Arrange
		var release = Read(ReleaseWorkflow);
		var selfRelease = Read(SelfHostedRelease);

		// Act & Assert
		await Assert
			.That(release)
			.Contains("PURVIEW_GITHUB_PACKAGES_RETENTION_HOURS")
			.Because("a repository must be able to shorten or disable the mirror retention.");
		await Assert.That(selfRelease).Contains("PURVIEW_GITHUB_PACKAGES_RETENTION_HOURS");
		await Assert.That(release).Contains("retention-hours:");
	}

	[Test]
	public async Task SourceConfiguration_SkipsForkPullRequests()
	{
		// Arrange
		// A fork PR receives no organisation secrets, so the private feed would deny every restore.
		var build = Read(BuildWorkflow);
		var selfBuild = Read(SelfHostedBuild);
		var release = Read(ReleaseWorkflow);
		var composite = Read(CompositeAction);

		// Act & Assert
		foreach (var content in new[] { build, selfBuild, release, composite })
		{
			await Assert
				.That(content)
				.Contains("github.event.pull_request.head.repo.fork == false")
				.Because("outside contributors must fall back to NuGet.org rather than a feed they cannot read.");
		}
	}

	static string Read(string relativePath) =>
		File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

	static string RepositoryRoot()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			if (File.Exists(Path.Combine(directory.FullName, "package.json")))
				return directory.FullName;
		}

		throw new InvalidOperationException($"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
	}
}
