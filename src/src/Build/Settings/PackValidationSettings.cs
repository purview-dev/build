namespace Purview.Build.Settings;

public sealed record PackValidationSettings
{
	public const string SectionName = "PackValidation";

	/// <summary>
	/// Every .nupkg must have a matching .snupkg (same id/version) and vice versa.
	/// </summary>
	/// <remarks>Defaults to <see langword="true"/>.</remarks>
	public bool RequireSymbolPackage { get; init; } = true;

	/// <summary>
	/// Every .snupkg must contain at least one .pdb file.
	/// </summary>
	/// <remarks>Defaults to <see langword="true"/>.</remarks>
	public bool RequireSymbolFiles { get; init; } = true;

	/// <summary>
	/// Every .dll/.exe in the .nupkg must have a matching portable PDB (in the .snupkg)
	/// that contains a Source Link record.
	/// </summary>
	/// <remarks>Defaults to <see langword="true"/>.</remarks>
	public bool RequireSourceLink { get; init; } = true;

	/// <summary>
	/// Every .dll/.exe in the .nupkg must be built deterministically (the PE must carry
	/// the Reproducible debug directory entry emitted by deterministic compiler builds).
	/// </summary>
	/// <remarks>Defaults to <see langword="true"/>.</remarks>
	public bool RequireDeterministic { get; init; } = true;

	/// <summary>
	/// Compiler-flag key=value entries that must appear in each assembly's PDB
	/// compiler-flags record (case-insensitive), e.g. "optimization=release".
	/// </summary>
	/// <remarks>Defaults to 'optimization=release'.</remarks>
	public string[] RequiredCompilerFlags { get; init; } = ["optimization=release"];

	/// <summary>
	/// Package id (glob, case-insensitive; "*" matches every package) to entry path globs
	/// that MUST be present in the .nupkg. Paths use forward slashes, e.g.
	/// "tools/**/Foo.dll" or "lib/netstandard2.0/Foo.dll".
	/// </summary>
	/// <remarks>
	/// An entry may contain the literal token <c>$(TFM)</c> as a target-framework partial, e.g.
	/// "lib/$(TFM)/Foo.dll". The token is expanded into one required entry per target framework
	/// the package actually supports (short folder name, e.g. "net8.0", "net48", "netstandard2.0"),
	/// so every one of the package's target frameworks must independently satisfy the entry.
	/// </remarks>
	public Dictionary<string, string[]> RequiredContent { get; init; } = [];

	/// <summary>
	/// Package id (glob, case-insensitive; "*" matches every package) to entry path globs
	/// that MUST NOT be present in the .nupkg. Paths use forward slashes, e.g. "**/*.pdb".
	/// </summary>
	/// <remarks>
	/// Entries may also use the <c>$(TFM)</c> target-framework partial token; see
	/// <see cref="RequiredContent"/>.
	/// </remarks>
	public Dictionary<string, string[]> ForbiddenContent { get; init; } = [];

	/// <summary>
	/// When <see langword="true"/>, <see cref="RequiredContent"/> becomes the exhaustive, exact
	/// definition of every package's contents:
	/// <list type="bullet">
	/// <item>Every produced .nupkg's id must match a <see cref="RequiredContent"/> key; a package
	/// with no matching rule is an error.</item>
	/// <item>Every entry in the .nupkg (excluding standard NuGet/OPC metadata such as the .nuspec,
	/// "[Content_Types].xml", "_rels/", "package/services/metadata/", and ".signature.p7s") must
	/// match one of that package's (TFM-expanded) <see cref="RequiredContent"/> globs; any
	/// undeclared entry is an error.</item>
	/// </list>
	/// </summary>
	/// <remarks>Defaults to <see langword="true"/>.</remarks>
	public bool RequireExplicitContent { get; init; } = true;
}
