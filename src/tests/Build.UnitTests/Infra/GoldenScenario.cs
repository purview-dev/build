using Purview.Build.Configuration;
using Purview.Build.Release;

namespace Purview.Build.Infra;

/// <summary>
/// The one fixed scenario the <c>release-explain</c> golden file records: a Model C servicing
/// release of <c>2.0.2</c> from <c>release/2.0</c>, evaluated against a simulated tag list.
/// </summary>
/// <remarks>
/// Chosen because it exercises the whole report — a non-default policy, an inherited rule set, a
/// simulated context, a prerelease-free stable line, and every rule passing — so a change anywhere
/// in the contract moves the golden file.
/// </remarks>
static class GoldenScenario
{
	public const string Version = "2.0.2";

	public const string Ref = "refs/heads/release/2.0";

	public static async Task<string> RenderAsync(CancellationToken cancellationToken)
	{
		var root = Path.Combine(Path.GetTempPath(), "purview-build-golden-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			var explanation = await BuildAsync(root, cancellationToken);

			return explanation.ToJson();
		}
		finally
		{
			if (Directory.Exists(root))
				Directory.Delete(root, recursive: true);
		}
	}

	static async Task<ReleaseExplanation> BuildAsync(string root, CancellationToken cancellationToken)
	{
		await File.WriteAllTextAsync(
			Path.Combine(root, "package.json"),
			$$"""{ "name": "golden", "version": "{{Version}}" }""",
			cancellationToken
		);

		var tagsPath = Path.Combine(root, "tags.txt");
		await File.WriteAllLinesAsync(tagsPath, ["v2.0.0", "v2.0.1", "v2.1.0-prerelease.1"], cancellationToken);

		var configPath = Path.Combine(root, "purview-build.json");
		await File.WriteAllTextAsync(
			configPath,
			$$"""
			{
				"Build": { "Solution": "src/Product.slnx" },
				"Release": {
					"Mode": "NuGet",
					"Eligibility": { "Policy": "TrunkReservesMinor" },
					"Context": {
						"Ref": "{{Ref}}",
						"ExistingTags": "{{tagsPath.Replace('\\', '/')}}"
					}
				}
			}
			""",
			cancellationToken
		);

		PipelineStartup startup = new(
			PipelineDirectory: AppSettings.Directory,
			RepositoryRoot: root,
			Resolution: ConfigFileLocator.Resolve(root)
		);

		var configuration = ConfigurationChain.Build(startup, []);

		return await ReleaseExplainer.ExplainAsync(startup, configuration, cancellationToken);
	}
}

/// <summary>
/// Locates the shipped <c>appsettings.json</c> directory for tests that build a configuration chain
/// without running the tool.
/// </summary>
static class AppSettings
{
	public static string Directory { get; } = Find();

	static string Find()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			var candidate = Path.Combine(directory.FullName, "src", "src", "Build");
			if (File.Exists(Path.Combine(candidate, "appsettings.json")))
				return candidate;
		}

		throw new InvalidOperationException(
			$"Could not locate src/src/Build/appsettings.json walking up from '{AppContext.BaseDirectory}'."
		);
	}
}
