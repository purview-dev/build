using System.Text;
using Purview.Build.Helpers;
using Purview.Build.Infra;

namespace Purview.Build;

public class AssemblyInspectorTests
{
	static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
	static readonly Guid CompilerFlagsGuid = new("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");

	[Test]
	public async Task DeterministicEmit_IsDeterministic(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: true, cancellationToken);
		using MemoryStream stream = new(dll);

		var deterministic = PackageInspector.IsDeterministic(stream);

		await Assert.That(deterministic).IsTrue();
	}

	[Test]
	public async Task NonDeterministicEmit_IsNotDeterministic(CancellationToken cancellationToken)
	{
		var (dll, _) = TestHelper.EmitAssembly(deterministic: false, cancellationToken);
		using MemoryStream stream = new(dll);

		var deterministic = PackageInspector.IsDeterministic(stream);

		await Assert.That(deterministic).IsFalse();
	}

	[Test]
	public async Task SourceLink_IsDetected()
	{
		var json = Encoding.UTF8.GetBytes( /*lang=json,strict*/
			"""{"documents":{"C:\\repo\\*":"https://github.com/purview-dev/build/*"}}"""
		);
		var pdb = TestHelper.BuildPortablePdb((SourceLinkGuid, json));
		using MemoryStream stream = new(pdb);

		var hasSourceLink = PackageInspector.HasSourceLink(stream);

		await Assert.That(hasSourceLink).IsTrue();
	}

	[Test]
	public async Task NoSourceLink_IsNotDetected()
	{
		var pdb = TestHelper.BuildPortablePdb();
		using MemoryStream stream = new(pdb);

		var hasSourceLink = PackageInspector.HasSourceLink(stream);

		await Assert.That(hasSourceLink).IsFalse();
	}

	[Test]
	public async Task CompilerFlags_AreDecoded()
	{
		const string raw = "version\02\0optimization\0release\0nullable\0Enable\0";
		var pdb = TestHelper.BuildPortablePdb((CompilerFlagsGuid, TestHelper.CompilerFlagsBlob(raw)));
		using MemoryStream stream = new(pdb);

		var decoded = PackageInspector.GetCompilerFlags(stream);

		await Assert.That(decoded).IsEqualTo("version=2;optimization=release;nullable=Enable");
	}

	[Test]
	public async Task CompilerFlags_FlagLookupIsCaseInsensitive()
	{
		var pdb = TestHelper.BuildPortablePdb(
			(CompilerFlagsGuid, TestHelper.CompilerFlagsBlob("optimization\0release\0"))
		);
		using MemoryStream stream = new(pdb);

		var decoded = PackageInspector.GetCompilerFlags(stream);

		await Assert.That(decoded!.Contains("OPTIMIZATION=RELEASE", StringComparison.OrdinalIgnoreCase)).IsTrue();
	}

	[Test]
	public async Task NoCompilerFlags_ReturnsNull()
	{
		var pdb = TestHelper.BuildPortablePdb();
		using MemoryStream stream = new(pdb);

		var decoded = PackageInspector.GetCompilerFlags(stream);

		await Assert.That(decoded).IsNull();
	}

	[Test]
	public async Task SourceLinkGuid_MatchesPortablePdbSpec()
	{
		// These GUIDs are mandated by the portable PDB specification; do not change them.
		await Assert.That(SourceLinkGuid).IsEqualTo(new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A"));
		await Assert.That(CompilerFlagsGuid).IsEqualTo(new Guid("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8"));
	}
}
