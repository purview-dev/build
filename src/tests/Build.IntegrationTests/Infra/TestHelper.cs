using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Purview.Build.Infra;

static class TestHelper
{
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
	public static (byte[] Assembly, byte[] Pdb) EmitAssembly(bool deterministic, CancellationToken cancellationToken)
	{
		const string source = """
			namespace Sample
			{
			    public static class Greeter
			    {
			        public static string Hello() => "Hello";
			    }
			}
			""";

		var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
			.WithDeterministic(deterministic)
			.WithOptimizationLevel(OptimizationLevel.Release);

		var compilation = CSharpCompilation.Create(
			"Sample",
			[CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)],
			[MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
			options
		);

		using MemoryStream peStream = new();
		using MemoryStream pdbStream = new();
		var result = compilation.Emit(
			peStream,
			pdbStream,
			options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
			cancellationToken: cancellationToken
		);
		if (!result.Success)
			throw new InvalidOperationException(string.Join("; ", result.Diagnostics));

		// Return the emitted assembly and PDB as byte arrays.
		return (peStream.ToArray(), pdbStream.ToArray());
	}

	public static byte[] BuildPortablePdb(params (Guid Kind, byte[] Value)[] debugInfo)
	{
		MetadataBuilder builder = new();
		builder.AddModule(0, builder.GetOrAddString("Sample"), builder.GetOrAddGuid(Guid.NewGuid()), default, default);

		foreach (var (kind, value) in debugInfo)
		{
			builder.AddCustomDebugInformation(
				MetadataTokens.EntityHandle(TableIndex.Module, 1),
				builder.GetOrAddGuid(kind),
				builder.GetOrAddBlob(value)
			);
		}

		PortablePdbBuilder pdbBuilder = new(
			builder,
			new int[64].ToImmutableArray(),
			default,
			_ => new BlobContentId(Guid.NewGuid(), 0)
		);

		BlobBuilder blob = new();
		pdbBuilder.Serialize(blob);
		return blob.ToArray();
	}

	public static byte[] CompilerFlagsBlob(string keyValuePairs)
	{
		// Roslyn stores the record as NUL-separated key/value pairs, e.g. "optimization\0release\0".
		return Encoding.UTF8.GetBytes(keyValuePairs);
	}
}
