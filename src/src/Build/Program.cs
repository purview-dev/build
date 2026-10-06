using Purview.Build.Configuration;
using Purview.Build.Release;

var commandLine = ToolCommandLine.Parse(args);

if (commandLine.Command == ToolCommand.Version)
{
	CLIConsole.WriteLine(ToolInfo.VersionLine);
	return 0;
}

try
{
	// Both --help and release-explain report the resolved configuration, so resolution happens
	// before either is rendered. It is read-only and mutates nothing.
	var startup = ConfigurationChain.ResolveStartup(commandLine);

	if (commandLine.Command == ToolCommand.Help)
	{
		CLIConsole.WriteLine(ToolInfo.BuildHelpText(startup.Resolution));
		return 0;
	}

	if (commandLine.Command == ToolCommand.ReleaseExplain)
	{
		var configuration = ConfigurationChain.Build(startup, commandLine.PipelineArguments);
		var explanation = await ReleaseExplainer.ExplainAsync(
			startup,
			configuration,
			CancellationToken.None
		);

		// Both forms are written raw: the JSON is a contract a workflow pipes into jq, and the text
		// report is already wrapped deliberately, so console re-wrapping only mangles it.
		CLIConsole.WriteRaw(
			commandLine.Format == OutputFormat.Json
				? explanation.ToJson()
				: ReleaseExplanationText.Render(explanation)
		);

		// release-explain always exits 0: it reports a decision, it does not act on one. The
		// decision's own exit code is in the report, for the caller to act on.
		return 0;
	}

	// Identify the tool up front, so CI job logs and local runs show which build tool executed the
	// pipeline, and which configuration it read.
	CLIConsole.WriteLine(ToolInfo.VersionLine);

	foreach (var line in ConfigurationChain.DescribeResolution(startup.Resolution))
		CLIConsole.WriteLine(line);

	foreach (var warning in ConfigurationChain.DescribeWarnings(startup.Resolution))
		CLIConsole.WriteLine(warning);

	CLIConsole.WriteLine();

	var failures = await BuildPipeline.RunAsync(commandLine, startup);

	if (failures.Count == 0)
		return 0;

	CLIConsole.WriteLine();
	CLIConsole.WriteLine($"{ToolInfo.Name} failed: {failures.Count} module(s) failed.");

	foreach (var failure in failures)
	{
		CLIConsole.WriteLine();
		CLIConsole.WriteLine(FailureReport.Format(failure.ModuleName, failure.ExceptionOrDefault!));
	}

	return 1;
}
catch (Exception exception)
{
	CLIConsole.WriteLine();
	CLIConsole.WriteLine(FailureReport.Format($"{ToolInfo.Name} failed", exception));
	return 1;
}
