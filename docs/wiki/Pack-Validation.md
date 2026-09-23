# Pack Validation

`ValidatePackModule` inspects every `.nupkg`/`.snupkg` produced in `Build:ArtifactsFolder` and fails the pipeline when any package has validation errors. Each package is reported as valid/invalid in the summary.

## Symbol package pairing (`RequireSymbolPackage`)

Every `.nupkg` must have a matching `.snupkg` (same id/version) and vice versa. A package without its symbol sibling is an error.

## Symbol package contents (`RequireSymbolFiles`)

Every `.snupkg` must contain at least one `.pdb`. Symbol packages must not contain non-symbol files other than OPC metadata (`[Content_Types].xml`, `_rels/`, `package/services/metadata/`) and the `.nuspec`.

## PDB placement in `.nupkg`

The `.nupkg` must not contain `.pdb` files outside `tools/` — PDBs are delivered through the `.snupkg`. The exception is tool packages (`PackAsTool`), whose runtime PDBs legitimately live under `tools/`; the `Purview.Build` csproj strips those PDBs from the `.nupkg` after `GenerateNuspec` so the `.snupkg` keeps them for source-link validation.

## Content rules (`RequiredContent` / `ForbiddenContent`)

Both maps are keyed by package-id glob (case-insensitive; `"*"` matches every package) and contain entry-path glob lists (forward slashes, e.g. `tools/**/Purview.Build.dll` or `**/*.pdb`).

- **Required**: the rule is satisfied when any package entry matches the glob; a missing match is an error.
- **Forbidden**: any matching entry is an error.

### Target-framework partials (`$(TFM)`)

An entry may contain the literal token `$(TFM)`, e.g. `lib/$(TFM)/Purview.Telemetry.dll`. The token is expanded into one entry per target framework the package actually ships (short folder name, e.g. `net8.0`, `net48`, `netstandard2.0`, discovered via the package's own `lib`/`ref`/`build`/`tools`/`frameworkAssemblies` groups), so the rule must be satisfied independently for every one of the package's target frameworks. If the package has no detectable target frameworks, a `$(TFM)` entry is reported as an error rather than silently skipped.

### Explicit/exhaustive content (`RequireExplicitContent`)

When `RequireExplicitContent` is `true`, `RequiredContent` becomes the precise, exhaustive definition of every package's contents instead of a "must contain at least" list:

- Every produced `.nupkg`'s package id must match a `RequiredContent` key; a generated package with no matching rule is an error.
- Every entry in a matched package (after `$(TFM)` expansion) — excluding standard NuGet/OPC metadata (`.nuspec`, `[Content_Types].xml`, `_rels/`, `package/services/metadata/`, `.signature.p7s`) — must match one of that package's `RequiredContent` globs; any undeclared entry is reported as an error.

`ForbiddenContent` is unaffected by `RequireExplicitContent` and continues to apply as a simple deny-list.

## Assembly inspection

When any of `RequireSourceLink`, `RequireDeterministic`, or `RequiredCompilerFlags` is enabled, each `.dll`/`.exe` in the `.nupkg` that the package ships symbols for (a sibling PDB exists in the `.snupkg` or `.nupkg`) is inspected:

- **Deterministic (`RequireDeterministic`)**: the PE must carry the Reproducible debug directory entry (`PEReader.ReadDebugDirectory` with type `Reproducible`). Bundled third-party binaries without symbols are not judged.
- **Source link (`RequireSourceLink`)**: the matching portable PDB must contain a Source Link record (custom debug info GUID `CC110556-A091-4D38-9FEC-25AB9A351A6A`).
- **Compiler flags (`RequiredCompilerFlags`)**: the PDB's compiler-flags record (custom debug info GUID `B5FEEC05-8CD0-4A83-96DA-466284BB4BD8`, stored as NUL-separated `key=value` pairs) must contain each required flag, matched case-insensitively (e.g. `optimization=release`).

Unreadable packages and invalid PE/PDB files are reported as errors.

## Filename checks

Package file names must match the nuspec id/version, i.e. `<id>.<version>.nupkg` / `<id>.<version>.snupkg`.

## See also

- [Pipeline Modules](Pipeline-Modules.md)
- [Configuration Reference](Configuration-Reference.md)