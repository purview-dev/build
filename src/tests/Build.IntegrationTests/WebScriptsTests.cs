using Purview.Build.Helpers;

namespace Purview.Build;

public class WebScriptsTests
{
	static string CreateTempRepository(string packageJson)
	{
		var directory = Path.Combine(Path.GetTempPath(), "purview-build-webscripts-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, "package.json"), packageJson);
		return directory;
	}

	[Test]
	public async Task GetScriptName_ReturnsScriptForBareBunRunCommand()
	{
		await Assert.That(WebScripts.GetScriptName("bun run build")).IsEqualTo("build");
		await Assert.That(WebScripts.GetScriptName("bun run format:check")).IsEqualTo("format:check");
	}

	[Test]
	public async Task GetScriptName_ReturnsNullForNonScriptCommands()
	{
		await Assert.That(WebScripts.GetScriptName("bun install")).IsNull();
		await Assert.That(WebScripts.GetScriptName("bun run build --prod")).IsNull();
		await Assert.That(WebScripts.GetScriptName("npm run build")).IsNull();
	}

	[Test]
	public async Task ReadScripts_ParsesDeclaredScripts()
	{
		var repository = CreateTempRepository("""{"name":"site","scripts":{"build":"astro build","lint":"oxlint"}}""");

		try
		{
			var scripts = WebScripts.ReadScripts(repository);

			await Assert.That(scripts.Count).IsEqualTo(2);
			await Assert.That(scripts["build"]).IsEqualTo("astro build");
			await Assert.That(WebScripts.HasScript(repository, "lint")).IsTrue();
			await Assert.That(WebScripts.HasScript(repository, "test")).IsFalse();
		}
		finally
		{
			Directory.Delete(repository, true);
		}
	}

	[Test]
	public async Task ReadScripts_ReturnsEmptyWhenMissing()
	{
		var repository = Path.Combine(Path.GetTempPath(), "purview-build-webscripts-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(repository);

		try
		{
			var scripts = WebScripts.ReadScripts(repository);

			await Assert.That(scripts).IsEmpty();
			await Assert.That(WebScripts.HasScript(repository, "build")).IsFalse();
		}
		finally
		{
			Directory.Delete(repository, true);
		}
	}

	[Test]
	public async Task ReadPackageName_ReturnsNameField()
	{
		var repository = CreateTempRepository("""{"name":"purview-dev","version":"0.1.0"}""");

		try
		{
			await Assert.That(WebScripts.ReadPackageName(repository)).IsEqualTo("purview-dev");
		}
		finally
		{
			Directory.Delete(repository, true);
		}
	}

	[Test]
	public async Task FromCommand_StripsLeadingToolToken()
	{
		var install = BunCLIOptions.FromCommand("bun install");

		await Assert.That(install.Tool).IsEqualTo("bun");
		await Assert.That(install.CommandParts).IsEquivalentTo(["install"]);
	}

	[Test]
	public async Task FromCommand_PreservesScriptArguments()
	{
		var build = BunCLIOptions.FromCommand("bun run build");

		await Assert.That(build.Tool).IsEqualTo("bun");
		await Assert.That(build.CommandParts).IsEquivalentTo(["run", "build"]);
	}
}
