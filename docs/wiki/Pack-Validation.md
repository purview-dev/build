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