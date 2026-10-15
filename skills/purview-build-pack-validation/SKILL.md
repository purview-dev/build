---
name: purview-build-pack-validation
description: >-
  Use when a Purview.Build pack validation fails, or when writing the RequiredContent and
  ForbiddenContent rules for a package - symbol pairing, PDB placement, the $(TFM) token, exhaustive
  content with RequireExplicitContent, source link, determinism and compiler-flag inspection.
metadata:
  product: Purview.Build
  repository: purview-dev/build
---

# Pack validation

`ValidatePackModule` inspects every `.nupkg` and `.snupkg` in `Build:ArtifactsFolder` and fails the
pipeline when any package has errors. Each package is reported valid or invalid in the summary.

`CleanArtifactsModule` resets that folder before the run produces anything, so validation only ever
inspects **this run's** packages — a leftover from an earlier or differently-configured build cannot
fail (or pass) validation. `Build:CleanArtifacts=false` keeps existing artifacts.

## Symbol packages

- **`RequireSymbolPackage`** — every `.nupkg` must have a matching `.snupkg` (same id and version)
  and vice versa. A package without its sibling is an error.
- **`RequireSymbolFiles`** — every `.snupkg` must contain at least one `.pdb`, and must contain no
  non-symbol files other than OPC metadata (`[Content_Types].xml`, `_rels/`,
  `package/services/metadata/`) and the `.nuspec`.

## PDB placement

A `.nupkg` must not contain `.pdb` files **outside `tools/`** — PDBs are delivered through the
`.snupkg`. The exception is a tool package (`PackAsTool`), whose runtime PDBs legitimately live under
`tools/`. `Purview.Build`'s own csproj strips those PDBs from the `.nupkg` after `GenerateNuspec`, so
the `.snupkg` keeps them for source-link validation.

## Content rules

`RequiredContent` and `ForbiddenContent` are both keyed by **package-id glob** (case-insensitive;
`"*"` matches every package) and hold entry-path glob lists with forward slashes:

```json
"RequiredContent": {
  "purview.build": [
    "tools/**/Purview.Build.dll",
    "tools/**/appsettings.json",
    "README.md"
  ]
},
"ForbiddenContent": { "*": ["**/*.pdb"] }
```

- **Required** — satisfied when any package entry matches; a missing match is an error.
- **Forbidden** — any matching entry is an error.

### The `$(TFM)` token

An entry may contain the literal `$(TFM)`, e.g. `lib/$(TFM)/Purview.Telemetry.dll`. It expands into
one entry **per target framework the package actually ships** — the short folder name (`net8.0`,
`net48`, `netstandard2.0`), discovered from the package's own `lib`/`ref`/`build`/`tools`/
`frameworkAssemblies` groups — so the rule must be satisfied **independently for every one of them**.

If the package has no detectable target frameworks, a `$(TFM)` entry is an **error**, not a silent
skip. That is deliberate: a token that quietly matched nothing would turn a required rule into no
rule at all.

A package that ships a TFM-less folder is the case where `$(TFM)` does not work — the token expands
to include `any`, and the rule could never be met. Write the literal paths instead.

### `RequireExplicitContent` — exhaustive, and on by default

When `true` (**the default**), `RequiredContent` stops being a "must contain at least" list and
becomes the precise definition of every package's contents:

- every produced `.nupkg`'s id must match a `RequiredContent` key — **a generated package with no
  matching rule is an error**;
- every entry in a matched package, after `$(TFM)` expansion, must match one of that package's globs
  — **any undeclared entry is an error**.

Standard NuGet/OPC metadata is excluded from that check: `.nuspec`, `[Content_Types].xml`, `_rels/`,
`package/services/metadata/`, `.signature.p7s`.

`ForbiddenContent` is unaffected and continues to apply as a plain deny-list.

This is the setting behind most first-run failures after adding a package or an asset: the
manifest is now wrong, not the package. Add the entry rather than turning the check off.

## Assembly inspection

Triggered when **any** of `RequireSourceLink`, `RequireDeterministic` or `RequiredCompilerFlags` is
enabled. Each `.dll`/`.exe` in the `.nupkg` **that the package ships symbols for** — a sibling PDB
exists in the `.snupkg` or `.nupkg` — is inspected:

| Check | What is read |
| --- | --- |
| `RequireDeterministic` | the PE must carry the `Reproducible` debug directory entry. Bundled third-party binaries without symbols are not judged. |
| `RequireSourceLink` | the matching portable PDB must contain a Source Link record (custom debug info GUID `CC110556-A091-4D38-9FEC-25AB9A351A6A`) |
| `RequiredCompilerFlags` | the PDB's compiler-flags record (GUID `B5FEEC05-8CD0-4A83-96DA-466284BB4BD8`, NUL-separated `key=value` pairs) must contain each required flag, matched case-insensitively — e.g. `optimization=release` |

Unreadable packages and invalid PE/PDB files are errors.

A practical consequence: an **analyzer-only** package that ships no PDB is never inspected, so these
checks neither pass nor fail for it. An analyzer package that *does* embed a PDB under
`analyzers/dotnet/` is inspected — and if a merge pass strips the compiler-flags record, the
`RequiredCompilerFlags` check fails. Either stop embedding the PDB, or relax the check with a
comment saying why.

## Filename checks

Package file names must match the nuspec id and version: `<id>.<version>.nupkg` and
`<id>.<version>.snupkg`.

## Related skills

`purview-build-configuration` for the `PackValidation` keys, and `purview-build-pipeline` for where
`ValidatePackModule` sits in the graph.
