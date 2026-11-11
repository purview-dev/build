using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Purview.Build.Release;

namespace Purview.Build.PackageJson;

/// <summary>
/// Everything a <c>package.json</c> validation run found, in one shape rendered either for a human or
/// as the JSON contract <c>package-validate --format=json</c> emits.
/// </summary>
/// <remarks>
/// The JSON property names are a contract: a caller or workflow parses <c>succeeded</c>,
/// <c>exitCode</c>, <c>profile</c> and the per-rule <c>results</c>. Renaming or removing a property is
/// a breaking change, which is why the shape is pinned by the test suite.
/// </remarks>
public sealed record PackageJsonValidationReport
{
	[JsonPropertyName("file")]
	public string File { get; init; } = string.Empty;

	[JsonPropertyName("profile")]
	public string Profile { get; init; } = string.Empty;

	[JsonPropertyName("succeeded")]
	public bool Succeeded { get; init; }

	[JsonPropertyName("exitCode")]
	public int ExitCode { get; init; }

	[JsonPropertyName("errorCount")]
	public int ErrorCount { get; init; }

	[JsonPropertyName("warningCount")]
	public int WarningCount { get; init; }

	[JsonPropertyName("results")]
	public IReadOnlyList<PackageJsonRuleReport> Results { get; init; } = [];

	[JsonPropertyName("errors")]
	public IReadOnlyList<string> Errors { get; init; } = [];

	[JsonPropertyName("warnings")]
	public IReadOnlyList<string> Warnings { get; init; } = [];

	/// <summary>
	/// The report for a file that could not be read at all, reported as a single
	/// <see cref="PackageJsonRules.ReadFailureRuleId"/> finding so the shape never varies.
	/// </summary>
	public static PackageJsonValidationReport Unreadable(string file, string error) =>
		new()
		{
			File = file,
			Profile = PackageJsonProfile.Auto.ToString(),
			Succeeded = false,
			ExitCode = 1,
			ErrorCount = 1,
			Results =
			[
				new PackageJsonRuleReport
				{
					RuleId = PackageJsonRules.ReadFailureRuleId,
					Outcome = PackageJsonRuleReport.Describe(PackageJsonRuleOutcome.Fail),
					Message = error,
				},
			],
			Errors = [$"{PackageJsonRules.ReadFailureRuleId}: {error}"],
		};

	internal static PackageJsonValidationReport From(
		PackageJsonValidationContext context,
		IReadOnlyList<PackageJsonRuleResult> results,
		bool treatWarningsAsErrors
	)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(results);

		var errors = results
			.Where(result => result.Outcome == PackageJsonRuleOutcome.Fail)
			.Select(Describe)
			.ToArray();
		var warnings = results
			.Where(result => result.Outcome == PackageJsonRuleOutcome.Warning)
			.Select(Describe)
			.ToArray();

		var failed = errors.Length > 0 || (treatWarningsAsErrors && warnings.Length > 0);

		return new()
		{
			File = context.Document.Path,
			Profile = context.Profile.ToString(),
			Succeeded = !failed,
			ExitCode = failed ? 1 : 0,
			ErrorCount = errors.Length,
			WarningCount = warnings.Length,
			Results =
			[
				.. results.Select(result => new PackageJsonRuleReport
				{
					RuleId = result.RuleId,
					Outcome = PackageJsonRuleReport.Describe(result.Outcome),
					Message = result.Message,
				}),
			],
			Errors = errors,
			Warnings = warnings,
		};
	}

	static string Describe(PackageJsonRuleResult result) => $"{result.RuleId}: {result.Message}";

	static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		// Findings quote field names and versions with apostrophes. The default encoder escapes
		// those to ', which is valid JSON but unreadable in a log. This output goes to a console and
		// to jq, never into HTML.
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		// Attaching the source-generated resolver keeps every option above while giving the
		// serializer metadata it can see at compile time, which is the only form an AOT publish
		// supports.
		TypeInfoResolver = BuildJsonContext.Default,
	};

	/// <summary>
	/// Renders the report as the JSON contract.
	/// </summary>
	/// <remarks>
	/// The type info comes from the source-generated resolver, so this works in a NativeAOT publish,
	/// where reflection-based serialization throws. .NET 11 added the generic
	/// <c>GetTypeInfo&lt;T&gt;()</c>; the net10.0 leg casts the non-generic result instead.
	/// </remarks>
	public string ToJson()
	{
#if NET11_0_OR_GREATER
		var typeInfo = JsonOptions.GetTypeInfo<PackageJsonValidationReport>();
#else
		var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<PackageJsonValidationReport>)
			JsonOptions.GetTypeInfo(typeof(PackageJsonValidationReport));
#endif
		return JsonSerializer.Serialize(this, typeInfo);
	}
}

/// <summary>
/// One rule's outcome, as it appears in the JSON contract.
/// </summary>
public sealed record PackageJsonRuleReport
{
	[JsonPropertyName("ruleId")]
	public string RuleId { get; init; } = string.Empty;

	[JsonPropertyName("outcome")]
	public string Outcome { get; init; } = string.Empty;

	[JsonPropertyName("message")]
	public string Message { get; init; } = string.Empty;

	/// <summary>
	/// The outcome strings of the JSON contract. Spelled out rather than lower-cased from the enum
	/// name, so the contract cannot drift if the enum is renamed.
	/// </summary>
	public static string Describe(PackageJsonRuleOutcome outcome) =>
		outcome switch
		{
			PackageJsonRuleOutcome.Pass => "pass",
			PackageJsonRuleOutcome.Warning => "warning",
			PackageJsonRuleOutcome.Fail => "fail",
			PackageJsonRuleOutcome.Skipped => "skipped",
			_ => "unknown",
		};
}
