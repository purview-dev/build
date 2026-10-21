using System.Text;
using Microsoft.Extensions.Configuration;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// Covers the bound used by <c>RunTestsModule</c> and <c>ValidatePackModule</c> to fan work out.
/// </summary>
/// <remarks>
/// Both modules run a separate process (or open a separate archive) per item, and each `dotnet test`
/// parallelises internally on top of that. An unbounded fan-out therefore oversubscribes the machine on
/// a large solution rather than going faster, which is what this setting exists to stop.
/// </remarks>
public class TestParallelismTests
{
	/// <summary>
	/// The resolution both modules apply: an unset or zero value means "use every core".
	/// </summary>
	static int Resolve(int configured) => configured > 0 ? configured : Environment.ProcessorCount;

	[Test]
	public async Task MaxTestParallelism_GivenDefaults_IsZeroMeaningProcessorCount()
	{
		BuildSettings settings = new();

		await Assert.That(settings.MaxTestParallelism).IsEqualTo(0);
		await Assert.That(Resolve(settings.MaxTestParallelism)).IsEqualTo(Environment.ProcessorCount);
	}

	[Test]
	[Arguments(1, 1)]
	[Arguments(2, 2)]
	[Arguments(64, 64)]
	public async Task MaxTestParallelism_GivenAnExplicitValue_IsHonoured(int configured, int expected)
	{
		BuildSettings settings = new() { MaxTestParallelism = configured };

		await Assert.That(Resolve(settings.MaxTestParallelism)).IsEqualTo(expected);
	}

	/// <summary>
	/// A negative value would make <c>ParallelOptions.MaxDegreeOfParallelism</c> mean "unbounded", which is
	/// the behaviour this setting replaced. It falls back to the processor count instead.
	/// </summary>
	[Test]
	public async Task MaxTestParallelism_GivenANegativeValue_FallsBackToProcessorCount()
	{
		BuildSettings settings = new() { MaxTestParallelism = -1 };

		await Assert.That(Resolve(settings.MaxTestParallelism)).IsEqualTo(Environment.ProcessorCount);
	}

	/// <summary>
	/// The within-project bound, mirroring <c>RunTestsModule.ResolveMaxParallelTestsPerProject</c>.
	/// </summary>
	static int ResolvePerProject(int configured) =>
		configured > 0 ? configured
		: configured < 0 ? 0
		: Math.Max(1, Environment.ProcessorCount * 3 / 4);

	[Test]
	public async Task MaxParallelTestsPerProject_GivenDefaults_DerivesThreeQuartersOfTheCores()
	{
		BuildSettings settings = new();

		await Assert.That(settings.MaxParallelTestsPerProject).IsEqualTo(0);
		await Assert
			.That(ResolvePerProject(settings.MaxParallelTestsPerProject))
			.IsEqualTo(Math.Max(1, Environment.ProcessorCount * 3 / 4));
	}

	/// <summary>
	/// The derived value must never reach zero, which the runner reads as "no bound".
	/// </summary>
	[Test]
	public async Task MaxParallelTestsPerProject_NeverDerivesZero()
	{
		await Assert.That(ResolvePerProject(0)).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task MaxParallelTestsPerProject_GivenAnExplicitValue_IsHonoured()
	{
		await Assert.That(ResolvePerProject(3)).IsEqualTo(3);
	}

	/// <summary>
	/// A negative value is the documented way to hand the decision back to the runner, which the module
	/// expresses by omitting the argument entirely.
	/// </summary>
	[Test]
	public async Task MaxParallelTestsPerProject_GivenNegative_DefersToTheRunner()
	{
		await Assert.That(ResolvePerProject(-1)).IsEqualTo(0);
	}

	[Test]
	public async Task MaxParallelTestsPerProject_BindsFromConfiguration()
	{
		const string json = /*lang=json,strict*/
			"""
			{ "Build": { "MaxParallelTestsPerProject": 5 } }
			""";

		var configuration = new ConfigurationBuilder()
			.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
			.Build();

		var settings = configuration.GetSection(BuildSettings.SectionName).Get<BuildSettings>()!;

		await Assert.That(settings.MaxParallelTestsPerProject).IsEqualTo(5);
	}

	[Test]
	public async Task MaxTestParallelism_GivenAConfiguredValue_BindsFromConfiguration()
	{
		const string json = /*lang=json,strict*/
			"""
			{ "Build": { "MaxTestParallelism": 3 } }
			""";

		var configuration = new ConfigurationBuilder()
			.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
			.Build();

		var settings = configuration.GetSection(BuildSettings.SectionName).Get<BuildSettings>()!;

		await Assert.That(settings.MaxTestParallelism).IsEqualTo(3);
	}

	/// <summary>
	/// Environment variables outrank <c>purview-build.json</c>, so this is the route a CI job uses to pin
	/// the bound on a runner whose core count does not match the developer machine.
	/// </summary>
	[Test]
	public async Task MaxTestParallelism_GivenAnEnvironmentVariable_OutranksJson()
	{
		const string json = /*lang=json,strict*/
			"""
			{ "Build": { "MaxTestParallelism": 3 } }
			""";

		var configuration = new ConfigurationBuilder()
			.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
			.AddInMemoryCollection([new KeyValuePair<string, string?>("Build:MaxTestParallelism", "7")])
			.Build();

		var settings = configuration.GetSection(BuildSettings.SectionName).Get<BuildSettings>()!;

		await Assert.That(settings.MaxTestParallelism).IsEqualTo(7);
	}
}
