using ModularPipelines.Attributes;
using ModularPipelines.Configuration;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Purview.Build.Modules;

[ModuleCategory("Build")]
[DependsOn<BuildModule>]
public sealed class RunTestsModule(IOptions<BuildSettings> settings) : Module<CommandResult[]>
{
	protected override ModuleConfiguration Configure() =>
		ModuleConfiguration
			.Create()
			.WithSkipWhen(_ =>
				settings.Value.RunTests
					? SkipDecision.DoNotSkip
					: SkipDecision.Skip("Tests are disabled. Set Build__RunTests=true to run them.")
			)
			.Build();

	protected override async Task<CommandResult[]?> ExecuteAsync(
		[NotNull] IModuleContext context,
		CancellationToken cancellationToken
	)
	{
		ModuleProgress.Starting(context, nameof(RunTestsModule));

		if (settings.Value.ProjectType == ProjectType.Web)
		{
			var repositoryRoot = PathHelpers.FindRepositoryRoot();
			var result = await context.Shell.Command.ExecuteCommandLineTool(
				BunCLIOptions.FromCommand(settings.Value.WebTestCommand),
				new() { WorkingDirectory = repositoryRoot },
				cancellationToken: cancellationToken
			);

			return [result];
		}

		var testRoot = settings.Value.TestRoot;

		// Enumerate with an absolute root so `dotnet test` receives an absolute `--project`:
		// the Microsoft.Testing.Platform runner in .NET 11 RC1 duplicates the directory
		// segments of a relative project path for multi-targeted projects (dotnet/sdk#56196).
		var testProjects = FilterTestProjects(
			Directory
				.EnumerateFiles(Path.GetFullPath(testRoot), "*.csproj", SearchOption.AllDirectories)
				.Where(project => IsTestProject(project, settings.Value.TestPatterns))
				.ToList(),
			settings.Value.TestProjects
		);
		if (testProjects.Count == 0)
		{
			context.Logger.LogWarning(
				"No test projects matched '{TestRoot}' (patterns: {TestPatterns}, filter: {TestProjects}), despite tests being enabled. Skipping test execution.",
				testRoot,
				settings.Value.TestPatterns,
				settings.Value.TestProjects
			);

			return [];
		}

		List<(string Project, TimeSpan Elapsed, int ExitCode)> timings = [];

		// Indexed rather than appended, so the returned order matches discovery order regardless of which
		// project finishes first.
		var results = new CommandResult[testProjects.Count];

		// Bounded rather than a bare Task.WhenAll over every project: each one is a separate `dotnet test`
		// process that parallelises internally, so an unbounded fan-out oversubscribes the machine on a
		// large solution and makes every project slower than it would have been on its own.
		var maxParallelism =
			settings.Value.MaxTestParallelism > 0 ? settings.Value.MaxTestParallelism : Environment.ProcessorCount;

		context.Logger.LogInformation(
			"Running {ProjectCount} test project(s), up to {MaxParallelism} at a time.",
			testProjects.Count,
			maxParallelism
		);

		await Parallel.ForEachAsync(
			Enumerable.Range(0, testProjects.Count),
			new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = cancellationToken },
			async (index, token) =>
			{
				var project = testProjects[index];
				var stopwatch = Stopwatch.StartNew();
				var result = await context
					.DotNet()
					.Test(
						new DotNetTestOptions
						{
							Project = project,
							Configuration = settings.Value.Configuration,
							NoBuild = true,
							NoRestore = true,
							Arguments = BuildTestArguments(settings.Value),
						},
						cancellationToken: token
					);
				stopwatch.Stop();

				results[index] = result;

				lock (timings)
					timings.Add((project, stopwatch.Elapsed, result.ExitCode));
			}
		);

		context.Logger.LogInformation(
			"Test run timings:{NewLine}{Timings}",
			Environment.NewLine,
			string.Join(
				Environment.NewLine,
				timings
					.OrderByDescending(t => t.Elapsed)
					.Select(t =>
						$"  {Path.GetFileName(t.Project)}: {t.Elapsed.TotalSeconds:F1}s (exit {t.ExitCode})"
					)
			)
		);

		return results;
	}

	/// <summary>
	/// Resolves how many tests a single project runs concurrently.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Measured on a 22-core machine against a 416-test integration suite whose tests each spawn
	/// <c>dotnet msbuild</c>: the runner's own auto-detected value took 183s, 8 took 138s, 16 took 135s and
	/// 32 took 154s. Auto is tuned for tests that are cheap and CPU-bound; a test that spawns a process is
	/// neither, and the curve is flat enough between half and three-quarters of the core count that
	/// three-quarters is a safe default rather than a tuned constant.
	/// </para>
	/// <para>
	/// A fixed number would be wrong on any machine but the one it was measured on, which is why this is
	/// derived. <c>0</c> hands the decision back to the runner.
	/// </para>
	/// </remarks>
	static int ResolveMaxParallelTestsPerProject(BuildSettings buildSettings)
	{
		if (buildSettings.MaxParallelTestsPerProject > 0)
			return buildSettings.MaxParallelTestsPerProject;

		if (buildSettings.MaxParallelTestsPerProject < 0)
			return 0;

		// Three-quarters of the core count is a safe default for tests that spawn processes, and at least 1 so that a single-core machine can run tests.
		return Math.Max(1, Environment.ProcessorCount * 3 / 4);
	}

	static List<string> BuildTestArguments(BuildSettings buildSettings)
	{
		List<string> arguments = [];

		if (buildSettings.TestFramework == TestFramework.TUnit)
		{
			// Microsoft.Testing.Platform exits with code 8 when no tests are selected; treat it as success.
			arguments.Add("--ignore-exit-code");
			arguments.Add("8");

			if (!string.IsNullOrWhiteSpace(buildSettings.TestFilter))
			{
				arguments.Add("--treenode-filter");
				arguments.Add(buildSettings.TestFilter);
			}

			var parallelTests = ResolveMaxParallelTestsPerProject(buildSettings);
			if (parallelTests > 0)
			{
				arguments.Add("--maximum-parallel-tests");
				arguments.Add(parallelTests.ToString(CultureInfo.InvariantCulture));
			}
		}
		else if (!string.IsNullOrWhiteSpace(buildSettings.TestFilter))
		{
			arguments.Add("--filter");
			arguments.Add(buildSettings.TestFilter);
		}

		return arguments;
	}

	static bool IsTestProject(string project, string patterns)
	{
		if (string.IsNullOrWhiteSpace(patterns) || patterns.Trim() == "*")
			return true;

		var fileName = Path.GetFileName(project);
		return patterns
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(ToRegexPattern)
			.Any(pattern => Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
	}

	static IReadOnlyList<string> FilterTestProjects(IReadOnlyList<string> projects, string filter)
	{
		if (string.IsNullOrWhiteSpace(filter) || filter.Trim() == "*")
			return projects;

		var patterns = filter
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(ToRegexPattern)
			.ToArray();

		return projects
			.Where(project =>
			{
				var fileName = Path.GetFileName(project);
				return patterns.Any(pattern =>
					Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase)
				);
			})
			.ToList();
	}

	static string ToRegexPattern(string entry)
	{
		if (entry.Contains('*', StringComparison.Ordinal))
		{
			var escaped = Regex.Escape(entry);
			return "^" + escaped.Replace("\\*", ".*", StringComparison.Ordinal) + "$";
		}

		return "^" + Regex.Escape(entry) + "$";
	}
}
