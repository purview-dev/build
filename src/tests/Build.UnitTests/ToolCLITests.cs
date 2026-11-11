using Purview.Build.Helpers;

namespace Purview.Build;

public class ToolCLITests
{
	[Test]
	public async Task ToolInfo_ReportsVersionWithoutBuildMetadata()
	{
		await Assert.That(ToolInfo.Version).IsNotEmpty();
		await Assert.That(ToolInfo.Version).DoesNotContain("+");
		await Assert.That(ToolInfo.VersionLine).IsEqualTo($"{ToolInfo.Name} {ToolInfo.Version}");
	}

	[Test]
	public async Task ToolInfo_HelpTextDocumentsOptionsAndConfiguration()
	{
		await Assert.That(ToolInfo.HelpText).Contains("--version");
		await Assert.That(ToolInfo.HelpText).Contains("--help");
		await Assert.That(ToolInfo.HelpText).Contains("Build__RunPack=false");
		await Assert.That(ToolInfo.HelpText).Contains("PURVIEW_BUILD_STACKTRACE");
		await Assert.That(ToolInfo.HelpText).Contains("release-explain");
		await Assert.That(ToolInfo.HelpText).Contains("package-validate");
	}

	[Test]
	public async Task ToolCommandLine_GivenThePackageValidateVerb_SelectsTheCommand()
	{
		var commandLine = ToolCommandLine.Parse(["package-validate", "--format=json"]);

		await Assert.That(commandLine.Command).IsEqualTo(ToolCommand.PackageValidate);
		await Assert.That(commandLine.Format).IsEqualTo(OutputFormat.Json);
	}

	[Test]
	public async Task ToolCommandLine_GivenTheReleaseExplainVerb_SelectsTheCommand()
	{
		var commandLine = ToolCommandLine.Parse(["release-explain"]);

		await Assert.That(commandLine.Command).IsEqualTo(ToolCommand.ReleaseExplain);
	}

	[Test]
	public async Task ToolCommandLine_GivenAVerb_KeepsItOutOfThePipelineArguments()
	{
		// The configuration command-line provider accepts only --key=value forms, so a bare verb
		// must never reach it.
		var commandLine = ToolCommandLine.Parse(["package-validate", "--Build:RunPack=false"]);

		await Assert.That(commandLine.PipelineArguments).IsEquivalentTo(["--Build:RunPack=false"]);
	}

	[Test]
	public async Task InformationalFlags_GivenVersionFlags_ReturnsVersion()
	{
		await Assert.That(InformationalFlags.Parse(["--version"])).IsEqualTo(InformationalFlag.Version);
		await Assert.That(InformationalFlags.Parse(["-v"])).IsEqualTo(InformationalFlag.Version);
		await Assert.That(InformationalFlags.Parse(["--VERSION"])).IsEqualTo(InformationalFlag.Version);
	}

	[Test]
	public async Task InformationalFlags_GivenHelpFlags_ReturnsHelp()
	{
		await Assert.That(InformationalFlags.Parse(["--help"])).IsEqualTo(InformationalFlag.Help);
		await Assert.That(InformationalFlags.Parse(["-h"])).IsEqualTo(InformationalFlag.Help);
		await Assert.That(InformationalFlags.Parse(["-?"])).IsEqualTo(InformationalFlag.Help);
	}

	[Test]
	public async Task InformationalFlags_GivenConfigurationOverrides_ReturnsNone()
	{
		var flag = InformationalFlags.Parse(["--Build:RunPack=false", "--Release:Mode=NuGet"]);

		await Assert.That(flag).IsEqualTo(InformationalFlag.None);
	}

	[Test]
	public async Task InformationalFlags_GivenVersionAndHelp_PrefersVersion()
	{
		await Assert.That(InformationalFlags.Parse(["--help", "--version"])).IsEqualTo(InformationalFlag.Version);
	}

	[Test]
	public async Task FailureReport_Describe_FlattensMessagesOutermostFirst()
	{
		Exception exception = new InvalidOperationException(
			"Pack validation failed.",
			new IOException("Stale package.")
		);

		var messages = FailureReport.Describe(exception);

		await Assert.That(messages).Count().IsEqualTo(2);
		await Assert.That(messages[0]).IsEqualTo("Pack validation failed.");
		await Assert.That(messages[1]).IsEqualTo("Stale package.");
	}

	[Test]
	public async Task FailureReport_Describe_SkipsRepeatedMessages()
	{
		Exception exception = new InvalidOperationException(
			"Same message.",
			new InvalidOperationException("Same message.")
		);

		await Assert.That(FailureReport.Describe(exception)).Count().IsEqualTo(1);
	}

	[Test]
	public async Task FailureReport_Format_IndentsNestedMessagesUnderTheModuleName()
	{
		Exception exception = new InvalidOperationException(
			"Pack validation failed.",
			new IOException("Stale package.")
		);

		var formatted = FailureReport.Format("ValidatePackModule", exception);

		await Assert.That(formatted).Contains("ValidatePackModule: Pack validation failed.");
		await Assert.That(formatted).Contains("  Stale package.");
		await Assert.That(formatted.Split('\n')).Count().IsEqualTo(2);
	}

	[Test]
	public async Task FailureReport_Format_ReportsPipelineModuleFailuresByModuleName()
	{
		Exception exception = new InvalidOperationException(
			"""
			The module RestoreModule has failed.

			Input: dotnet restore src/DoesNotExist.slnx

			Error: MSBUILD : error MSB1009: Project file does not exist.
			Exit Code: 1
			"""
		);

		var formatted = FailureReport.Format("Purview.Build failed", exception);

		await Assert.That(formatted).Contains("Purview.Build failed: RestoreModule");
		await Assert.That(formatted).Contains("  Input: dotnet restore src/DoesNotExist.slnx");
		await Assert.That(formatted).Contains("  Error: MSBUILD : error MSB1009: Project file does not exist.");
		await Assert.That(formatted).DoesNotContain("has failed.");
	}

	[Test]
	public async Task FailureReport_Format_DeduplicatesRepeatedDetailLines()
	{
		Exception exception = new InvalidOperationException(
			"The module RestoreModule has failed.\n\nInput: dotnet restore src/DoesNotExist.slnx\nExit Code: 1",
			new InvalidOperationException("Input: dotnet restore src/DoesNotExist.slnx\nExit Code: 1")
		);

		var formatted = FailureReport.Format("Purview.Build failed", exception);

		await Assert.That(formatted.Split('\n')).Count().IsEqualTo(3);
	}
}
