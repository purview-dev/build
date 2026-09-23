using Microsoft.Extensions.FileSystemGlobbing;
using NuGet.Packaging;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Purview.Build.Helpers;

static class PackageInspector
{
	// Portable PDB custom debug info record identifiers (see the portable PDB spec).
	static readonly Guid SourceLinkDebugInfoGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
	static readonly Guid CompilerFlagsDebugInfoGuid = new("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");

	public static bool MatchesAny(string pattern, IEnumerable<string> paths)
	{
		Matcher matcher = new(StringComparison.OrdinalIgnoreCase);
		matcher.AddInclude(pattern);
		return matcher.Match(paths).HasMatches;
	}

	public static bool RequiresAssemblyInspection(PackValidationSettings settings) =>
		settings.RequireSourceLink
		|| settings.RequireDeterministic
		|| settings.RequiredCompilerFlags.Length > 0;

	public static bool IsPdbAllowedInNupkg(string path) =>
		path.StartsWith("tools/", StringComparison.OrdinalIgnoreCase)
		|| path.StartsWith("analyzers/dotnet/", StringComparison.OrdinalIgnoreCase);

	public static bool HasEmbeddedAnalyzerSymbols(IEnumerable<string> paths) =>
		paths.Any(path =>
			IsPdbFile(path) && path.StartsWith("analyzers/dotnet/", StringComparison.OrdinalIgnoreCase)
		);

	public static void ValidateContentRules(
		IReadOnlyList<string> files,
		string packageId,
		Dictionary<string, string[]> requiredRules,
		Dictionary<string, string[]> forbiddenRules,
		List<string> errors
	)
	{
		var required = GetContentRule(requiredRules, packageId);
		if (required is not null)
		{
			foreach (var entry in required)
			{
				if (!MatchesAny(entry, files))
					errors.Add($"Required content '{entry}' is missing from the package.");
			}
		}

		var forbidden = GetContentRule(forbiddenRules, packageId);
		if (forbidden is not null)
		{
			foreach (var entry in forbidden)
			{
				if (MatchesAny(entry, files))
					errors.Add($"Forbidden content '{entry}' must not be in the package.");
			}
		}
	}

	public static async Task ValidateAssembliesAsync(
		PackageArchiveReader nupkgReader,
		PackageArchiveReader? snupkgReader,
		PackValidationSettings settings,
		List<string> errors,
		CancellationToken cancellationToken
	)
	{
		if (!RequiresAssemblyInspection(settings))
			return;

		var nupkgFiles = (await nupkgReader.GetFilesAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var snupkgFiles = snupkgReader is null
			? new(StringComparer.OrdinalIgnoreCase)
			: (await snupkgReader.GetFilesAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var entry in nupkgFiles)
		{
			if (!IsAssembly(entry))
				continue;

			var pdbPath = Path.ChangeExtension(entry, ".pdb");
			var pdbIsInSnupkg = snupkgFiles.Contains(pdbPath);
			var pdbIsInNupkg = nupkgFiles.Contains(pdbPath);

			// Only assemblies the package ships symbols for are judged by the package's
			// quality bar; bundled third-party binaries without symbols are not ours to check.
			if (!pdbIsInSnupkg && !pdbIsInNupkg)
				continue;

			if (settings.RequireDeterministic)
				await ValidateDeterministicAsync(nupkgReader, entry, errors, cancellationToken);

			if (!settings.RequireSourceLink && settings.RequiredCompilerFlags.Length == 0)
				continue;

			var pdbReader = pdbIsInSnupkg ? snupkgReader : nupkgReader;
			await ValidatePdbAsync(pdbReader!, pdbPath, settings, entry, errors, cancellationToken);
		}
	}

	internal static bool IsDeterministic(Stream peStream)
	{
		using PEReader peReader = new(peStream);
		return peReader
			.ReadDebugDirectory()
			.Any(entry => entry.Type == DebugDirectoryEntryType.Reproducible);
	}

	internal static bool HasSourceLink(Stream pdbStream)
	{
		using var provider = MetadataReaderProvider.FromPortablePdbStream(
			pdbStream,
			MetadataStreamOptions.LeaveOpen
		);
		return HasDebugInfo(provider.GetMetadataReader(), SourceLinkDebugInfoGuid);
	}

	internal static string? GetCompilerFlags(Stream pdbStream)
	{
		using var provider = MetadataReaderProvider.FromPortablePdbStream(
			pdbStream,
			MetadataStreamOptions.LeaveOpen
		);
		return GetCompilerFlags(provider.GetMetadataReader());
	}

	static async Task ValidateDeterministicAsync(
		PackageArchiveReader reader,
		string entry,
		List<string> errors,
		CancellationToken cancellationToken
	)
	{
		bool deterministic;
		using (var stream = await OpenEntryAsync(reader, entry, cancellationToken))
		{
			try
			{
				deterministic = IsDeterministic(stream);
			}
			catch (BadImageFormatException ex)
			{
				errors.Add($"Assembly '{entry}' is not a valid PE file: {ex.Message}");
				return;
			}
		}

		if (!deterministic)
		{
			errors.Add(
				$"Assembly '{entry}' is not deterministic (missing the Reproducible debug directory entry)."
			);
		}
	}

	static async Task ValidatePdbAsync(
		PackageArchiveReader reader,
		string pdbEntry,
		PackValidationSettings settings,
		string assemblyEntry,
		List<string> errors,
		CancellationToken cancellationToken
	)
	{
		bool hasSourceLink;
		string? flags;
		using (var stream = await OpenEntryAsync(reader, pdbEntry, cancellationToken))
		{
			try
			{
				hasSourceLink = HasSourceLink(stream);
				stream.Position = 0;
				flags = GetCompilerFlags(stream);
			}
			catch (BadImageFormatException ex)
			{
				errors.Add(
					$"PDB '{pdbEntry}' for assembly '{assemblyEntry}' is not a portable PDB: {ex.Message}"
				);
				return;
			}
		}

		if (settings.RequireSourceLink && !hasSourceLink)
			errors.Add($"Assembly '{assemblyEntry}' has no Source Link record in its PDB.");

		if (settings.RequiredCompilerFlags.Length == 0)
			return;

		if (flags is null)
		{
			errors.Add($"Assembly '{assemblyEntry}' has no compiler-flags record in its PDB.");
			return;
		}

		foreach (var flag in settings.RequiredCompilerFlags)
		{
			if (!flags.Contains(flag, StringComparison.OrdinalIgnoreCase))
			{
				errors.Add(
					$"Assembly '{assemblyEntry}' is missing required compiler flag '{flag}' (found: '{flags}')."
				);
			}
		}
	}

	static bool HasDebugInfo(MetadataReader reader, Guid kind)
	{
		foreach (var handle in reader.CustomDebugInformation)
		{
			var record = reader.GetCustomDebugInformation(handle);
			if (reader.GetGuid(record.Kind) == kind)
				return true;
		}

		return false;
	}

	static string? GetCompilerFlags(MetadataReader reader)
	{
		foreach (var handle in reader.CustomDebugInformation)
		{
			var record = reader.GetCustomDebugInformation(handle);
			if (reader.GetGuid(record.Kind) != CompilerFlagsDebugInfoGuid)
				continue;

			var bytes = reader.GetBlobBytes(record.Value);
			if (bytes.Length == 0)
				return null;

			// The compiler-flags record is stored as a NUL-separated list of key/value pairs.
			return FormatCompilerFlags(Encoding.UTF8.GetString(bytes));
		}

		return null;
	}

	// Roslyn stores the compiler-flags record as NUL-separated key/value pairs.
	static string FormatCompilerFlags(string text)
	{
		var segments = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
		StringBuilder builder = new();
		for (var i = 0; i + 1 < segments.Length; i += 2)
		{
			if (builder.Length > 0)
				builder.Append(';');

			builder.Append(segments[i]).Append('=').Append(segments[i + 1]);
		}

		return builder.ToString();
	}

	static async Task<MemoryStream> OpenEntryAsync(PackageArchiveReader reader, string entry, CancellationToken cancellationToken)
	{
		using var stream = await reader.GetStreamAsync(entry, cancellationToken);
		MemoryStream memory = new();
		await stream.CopyToAsync(memory, cancellationToken);
		memory.Position = 0;

		return memory;
	}

	static string[]? GetContentRule(Dictionary<string, string[]> rules, string packageId)
	{
		if (rules.Count == 0)
			return null;

		foreach (var rule in rules)
		{
			if (string.Equals(rule.Key, packageId, StringComparison.OrdinalIgnoreCase))
				return rule.Value;
		}

		foreach (var rule in rules)
		{
			if (MatchesAny(rule.Key, [packageId]))
				return rule.Value;
		}

		return null;
	}

	static bool IsAssembly(string path)
	{
		var extension = Path.GetExtension(path);
		return string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase);
	}

	static bool IsPdbFile(string path) =>
		string.Equals(Path.GetExtension(path), ".pdb", StringComparison.OrdinalIgnoreCase);
}
