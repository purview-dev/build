using System.Globalization;
using System.Text;

namespace Purview.Build.PackageJson;

/// <summary>
/// Renders a <see cref="PackageJsonValidationReport"/> for a human.
/// </summary>
static class PackageJsonValidationReportText
{
	public static string Render(PackageJsonValidationReport report)
	{
		ArgumentNullException.ThrowIfNull(report);

		StringBuilder builder = new();

		builder.AppendLine(CultureInfo.InvariantCulture, $"package.json: {report.File}");
		builder.AppendLine(CultureInfo.InvariantCulture, $"  profile: {report.Profile}");
		builder.AppendLine(
			CultureInfo.InvariantCulture,
			$"  verdict: {(report.Succeeded ? "pass" : "FAIL")} (exit code {report.ExitCode})"
		);
		builder.AppendLine();

		builder.AppendLine("Rules, in evaluation order:");
		foreach (var result in report.Results)
		{
			builder.AppendLine(
				CultureInfo.InvariantCulture,
				$"  {result.Outcome, -7}  {result.RuleId}  {result.Message}"
			);
		}

		builder.AppendLine();

		if (report.ErrorCount > 0)
		{
			builder.AppendLine(
				CultureInfo.InvariantCulture,
				$"{report.ErrorCount} error(s), {report.WarningCount} warning(s)."
			);
		}
		else if (report.WarningCount > 0)
		{
			builder.AppendLine(
				CultureInfo.InvariantCulture,
				$"{report.WarningCount} warning(s), no errors."
			);
		}

		return builder.ToString().TrimEnd();
	}
}
