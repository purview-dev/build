var informationalFlag = InformationalFlags.Parse(args);

if (informationalFlag == InformationalFlag.Version)
{
	CLIConsole.WriteLine(ToolInfo.VersionLine);
	return 0;
}

if (informationalFlag == InformationalFlag.Help)
{
	CLIConsole.WriteLine(ToolInfo.HelpText);
	return 0;
}

// Identify the tool up front, so CI job logs and local runs show which build tool executed the pipeline.
CLIConsole.WriteLine(ToolInfo.VersionLine);
CLIConsole.WriteLine();

try
{
	var failures = await BuildPipeline.RunAsync(args);

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