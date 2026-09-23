using System.IO.Compression;
using System.Text;
using NuGet.Packaging;
using Purview.Build.Helpers;
using Purview.Build.Infra;
using Purview.Build.Settings;

namespace Purview.Build;

public class PackageValidationTests
{
	static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
	static readonly Guid CompilerFlagsGuid = new("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");
	static readonly byte[] SourceLinkJson = Encoding.UTF8.GetBytes(
		/*lang=json,strict*/
		"""{"documents":{"C:\\repo\\*":"https://github.com/purview-dev/build/*"}}"""
	);

	static byte[] BuildValidPdb() =>
		TestHelper.BuildPortablePdb(
			(SourceLinkGuid, SourceLinkJson),
			(CompilerFlagsGuid, TestHelper.CompilerFlagsBlob("optimization\0release\0"))
		);

	[Test]
	public async Task ValidateAssemblies_DeterministicSourceLinkAndFlags_Passes(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: true, cancellationToken);
		var pdb = BuildValidPdb();

		await ValidateInPackagesAsync(
			[("lib/net10.0/Sample.dll", dll)],
			[("lib/net10.0/Sample.pdb", pdb)],
			new PackValidationSettings
			{
				RequireSourceLink = true,
				RequireDeterministic = true,
				RequiredCompilerFlags = ["optimization=release"],
			},
			expectedErrors: 0,
			cancellationToken: cancellationToken
		);
	}

	[Test]
	public async Task ValidateAssemblies_MissingCompilerFlag_ReportsError(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: true, cancellationToken);
		var pdb = BuildValidPdb();

		await ValidateInPackagesAsync(
			[("lib/net10.0/Sample.dll", dll)],
			[("lib/net10.0/Sample.pdb", pdb)],
			new PackValidationSettings
			{
				RequireSourceLink = true,
				RequireDeterministic = true,
				RequiredCompilerFlags = ["nonexistent-flag"],
			},
			expectedErrors: 1,
			cancellationToken: cancellationToken
		);
	}

	[Test]
	public async Task ValidateAssemblies_NoSourceLink_ReportsError(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: true, cancellationToken);
		var pdb = TestHelper.BuildPortablePdb();

		await ValidateInPackagesAsync(
			[("lib/net10.0/Sample.dll", dll)],
			[("lib/net10.0/Sample.pdb", pdb)],
			new PackValidationSettings
			{
				RequireSourceLink = true,
				RequireSymbolPackage = false,
				RequiredCompilerFlags = [],
			},
			expectedErrors: 1,
			cancellationToken: cancellationToken
		);
	}

	[Test]
	public async Task ValidateAssemblies_AssemblyWithoutSymbols_IsSkipped(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: true, cancellationToken);

		await ValidateInPackagesAsync(
			[("lib/net10.0/Sample.dll", dll)],
			[],
			new PackValidationSettings { RequireSourceLink = true },
			expectedErrors: 0,
			cancellationToken: cancellationToken
		);
	}

	[Test]
	public async Task ValidateAssemblies_ChecksOff_DoesNothing(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: false, cancellationToken);

		await ValidateInPackagesAsync(
			[("lib/net10.0/Sample.dll", dll)],
			[],
			new PackValidationSettings(),
			expectedErrors: 0,
			cancellationToken: cancellationToken
		);
	}

	[Test]
	public async Task ValidateAssemblies_AnalyzerPdbInNupkg_IsInspected(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: true, cancellationToken);
		var pdb = BuildValidPdb();

		await ValidateInPackagesAsync(
			[("analyzers/dotnet/cs/Sample.dll", dll), ("analyzers/dotnet/cs/Sample.pdb", pdb)],
			[],
			new PackValidationSettings
			{
				RequireSourceLink = true,
				RequireDeterministic = true,
				RequiredCompilerFlags = ["optimization=release"],
			},
			expectedErrors: 0,
			cancellationToken: cancellationToken
		);
	}

	static async Task ValidateInPackagesAsync(
		(string Entry, byte[] Content)[] nupkgEntries,
		(string Entry, byte[] Content)[] snupkgEntries,
		PackValidationSettings settings,
		int expectedErrors,
		CancellationToken cancellationToken
	)
	{
		var directory = Directory.CreateTempSubdirectory("purview-build-tests").FullName;
		try
		{
			var nupkgPath = Path.Combine(directory, "Sample.1.0.0.nupkg");
			var snupkgPath = Path.Combine(directory, "Sample.1.0.0.snupkg");
			await CreateZipAsync(nupkgPath, nupkgEntries, cancellationToken);
			await CreateZipAsync(snupkgPath, snupkgEntries, cancellationToken);

			using PackageArchiveReader nupkgReader = new(nupkgPath);
			using PackageArchiveReader snupkgReader = new(snupkgPath);
			List<string> errors = [];

			await PackageInspector.ValidateAssembliesAsync(
				nupkgReader,
				snupkgReader,
				settings,
				errors,
				cancellationToken
			);

			await Assert.That(errors).Count().IsEqualTo(expectedErrors);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	static async Task CreateZipAsync(
		string path,
		(string Entry, byte[] Content)[] entries,
		CancellationToken cancellationToken
	)
	{
		using var file = File.Create(path);
		using ZipArchive zip = new(file, ZipArchiveMode.Create);
		foreach (var (entry, content) in entries)
		{
			var archiveEntry = zip.CreateEntry(entry);
			using var stream = await archiveEntry.OpenAsync(cancellationToken);
			await stream.WriteAsync(content, cancellationToken);
		}
	}
}
