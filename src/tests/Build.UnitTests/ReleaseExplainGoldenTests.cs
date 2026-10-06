using System.Text.RegularExpressions;
using Purview.Build.Infra;

namespace Purview.Build;

/// <summary>
/// Pins the <c>release-explain --format=json</c> contract against a golden file.
/// </summary>
/// <remarks>
/// The reusable release workflow parses <c>verdict</c>, <c>releaseMode</c>, <c>decidedByRule</c> and
/// <c>message</c> from this output, and consuming repositories pin the workflow by ref. A change in
/// shape or in a rule message is therefore a change to a published contract, and has to surface in
/// review rather than in a consumer's release.
///
/// Regenerate with <c>just release-explain-golden</c> after deliberately changing the contract.
/// </remarks>
[NotInParallel]
public partial class ReleaseExplainGoldenTests
{
	const string GoldenFileName = "release-explain.golden.json";

	[Test]
	public async Task Explain_GivenTheGoldenScenario_MatchesTheRecordedContract(CancellationToken cancellationToken)
	{
		// Arrange
		var expectedPath = Path.Combine(Scenarios.FixturesDirectory, GoldenFileName);

		// Act
		var actual = await GoldenScenario.RenderAsync(cancellationToken);

		// Assert
		// Regeneration is deliberately opt-in: a golden file that rewrites itself on mismatch
		// records the change instead of reporting it.
		if (Environment.GetEnvironmentVariable("PURVIEW_BUILD_UPDATE_GOLDEN") == "1")
		{
			// Written normalised: the scenario runs in a throwaway temp directory, so the raw output
			// carries a one-off absolute path that would be pure noise in the committed file.
			await File.WriteAllTextAsync(expectedPath, Normalise(actual) + Environment.NewLine, cancellationToken);
		}

		await Assert
			.That(File.Exists(expectedPath))
			.IsTrue()
			.Because($"the golden file '{expectedPath}' must exist; regenerate it if it was removed.");

		var expected = Normalise(await File.ReadAllTextAsync(expectedPath, cancellationToken));

		await Assert
			.That(Normalise(actual))
			.IsEqualTo(expected)
			.Because(
				"release-explain --format=json is a published contract. If this change is intentional, "
					+ "regenerate the golden file and review the diff."
			);
	}

	[Test]
	public async Task Explain_GivenTheGoldenScenario_ExposesEveryFieldTheWorkflowParses(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		// Guards the specific property names the YAML reads with jq, independently of the golden
		// file's whole-document comparison.
		var json = await GoldenScenario.RenderAsync(cancellationToken);

		// Act
		using var document = System.Text.Json.JsonDocument.Parse(json);
		var root = document.RootElement;

		// Assert
		await Assert.That(root.TryGetProperty("verdict", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("exitCode", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("releaseMode", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("message", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("decidedByRule", out _)).IsTrue();
		await Assert.That(root.TryGetProperty("simulated", out _)).IsTrue();
	}

	/// <summary>
	/// Replaces machine-specific text so the golden file is stable across machines: absolute paths
	/// become a placeholder and line endings are normalised.
	/// </summary>
	static string Normalise(string json) =>
		AbsolutePaths()
			// Quoted, so the golden file stays valid JSON and can be reviewed and parsed as such.
			.Replace(json.Replace("\r\n", "\n", StringComparison.Ordinal), "\"<root>\"")
			.Trim();

	[GeneratedRegex(@"""[A-Za-z]:\\\\[^""]*""|""/(?:tmp|var|home)/[^""]*""")]
	private static partial Regex AbsolutePaths();
}
