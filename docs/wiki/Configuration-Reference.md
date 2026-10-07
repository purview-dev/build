# Configuration Reference

Configuration is optional in a consuming repository; defaults are baked into the tool. Add `purview-build.json` and override what you need.

## Where the configuration file lives

### Explicit location

The file cannot select itself, so an explicit location is command line or environment only.

| Form | Notes |
| --- | --- |
| `--config <path>` / `-c <path>` | Absolute, or relative to the current working directory |
| `PURVIEW_BUILD_CONFIG=<path>` | Same resolution; consistent with `PURVIEW_BUILD_STACKTRACE` naming |

`--config` beats `PURVIEW_BUILD_CONFIG`. When either is set, **probing is skipped entirely**. A path naming a directory resolves to `purview-build.json` inside it. An explicit path that does not exist **fails with exit code 1** — never a silent fallback to probing or to defaults, because an explicit location that quietly does nothing is worse than a failure.

### Default probe order

Relative to the resolved repository root. First match wins; probing stops there.

| Order | Path | Rationale |
| --- | --- | --- |
| 1 | `purview-build.json` | The original location, so no existing repository moves |
| 2 | `.config/purview-build.json` | Beside `.config/dotnet-tools.json` and `.config/lefthook.yml` |
| 3 | `.build/purview-build.json` | Build-tooling folder |
| 4 | `build/purview-build.json` | Historic home of the vendored `build/PipelineCLI` |
| 5 | `.purview/purview-build.json` | Tool-namespaced, beside `.agents/` |
| 6 | `.github/purview-build.json` | Keeps CI configuration with the workflows that drive it |

The list is data, not a hard-coded chain: adding a location is a single entry, and `--help` prints the order with no further change. No configuration file at all remains entirely valid — every key is optional, and absence does not warn.

### Shadowing

When more than one probe path exists, the first is used and a **warning** names every shadowed file with its full path. The run does not fail, and files are **never merged**: silent first-match-wins is how someone spends an afternoon editing a file the tool never reads, and merging would create invisible layering that the precedence chain below already handles more predictably.

When nothing is found but a probe directory contains a filename within a small edit distance of `purview-build.json`, a single warning names it. It is not loaded.

### Path anchoring

`Build:Solution`, `Build:TestRoot`, `Build:ArtifactsFolder`, `Build:WebBuildOutput` and the `PackValidation` globs are relative to the **repository root**, wherever the configuration file was found. Anchoring to the configuration file's own directory would silently change the meaning of every existing path the moment a repository moved its config into `.config/`.

### `MODULAR_PIPELINES_DIRECTORY` is unrelated

`MODULAR_PIPELINES_DIRECTORY` controls **only** the directory containing the tool's shipped `appsettings.json`. It has nothing to do with locating `purview-build.json`. The two are orthogonal, and confusing them is easy: one points at the tool's own defaults, the other at a repository's overrides.

### User-level configuration — opt-in, local-only

A `$HOME`-level file is a convenience for machine-specific values, chiefly `PublishLocalNuGet:LocalFeedPath`. Locations, in order:

1. `$XDG_CONFIG_HOME/purview-build/purview-build.json`
2. `~/.config/purview-build/purview-build.json`
3. `%APPDATA%\purview-build\purview-build.json` (Windows)

It is also a reproducibility hazard — a machine-local file silently altering a build is the same class of problem as an empty forwarded environment variable — so:

- **Off by default.** Enable with `--user-config` or `PURVIEW_BUILD_USER_CONFIG=1`.
- **Ignored when not running locally**, exactly as `Release:Mode=LocalNuGet` is. It cannot influence a CI build.
- **Lower precedence than the repository configuration**, higher than `appsettings.json`.
- When active, the resolved path is logged at `Information`.

## Validation

`purview-build.json` is validated against a JSON Schema when it is loaded, before any module runs. The schema is generated from the tool's settings types, so it describes exactly the keys the tool can bind, and it ships with the tool (embedded for validation, and alongside `appsettings.json` in the package).

- A key the tool cannot bind — a typo, or a setting that has been renamed — is an **error**, not a silently ignored line. So is a value of the wrong type and an enum value outside its allowed set.
- `$`-prefixed keys are reserved for metadata and ignored, so `$schema` and `$comment` are accepted.
- Property names use the canonical casing shown in this reference (`"Build"`, not `"build"`).
- `//` comments and trailing commas are tolerated, as they are by the configuration binder.

A failure names the file and every offending key, then exits 1 — exactly as malformed JSON does.

### Editor support

Add the schema to the file and editors (VS Code, Rider, Visual Studio) complete keys and flag unknown ones as you type:

```json
{
  "$schema": "https://raw.githubusercontent.com/purview-dev/build/main/purview-build.schema.json",
  "Build": { "Solution": "src/MyProduct.slnx" }
}
```

The schema is generated from the settings types: `just schema` regenerates it after a deliberate settings change, and the test suite fails when the committed file drifts from the types.

## Precedence

```text
--config / PURVIEW_BUILD_CONFIG   selects WHICH file

command line
  > environment variables (__ nesting)
    > <the resolved config file>
      > user config, when opted in and running locally
        > appsettings.json (shipped)
          > C# property initialisers
```

- Environment variables use `__` for nesting, for example `Release__Mode=NuGet`.
- Command-line overrides use configuration syntax, for example `--Build:RunPack=false`.
- Secrets must not be committed; they are supplied at runtime through env vars / CI secrets. See [Secrets and Environment Variables](Secrets-and-Environment-Variables.md).

### Informational options

| Option | Behaviour |
| --- | --- |
| `-v`, `--version` | Print the tool version and exit without running the pipeline. |
| `-h`, `--help`, `-?` | Print usage, options, the probe order, and the resolved configuration path (or "none found"), then exit. |
| `release-explain` | Print the release decision and exit without running any module. `--format=json` emits the stable contract the reusable workflow consumes. See [Local Development](Local-Development.md). |

Failures are reported the way a CLI build tool reports them: the tool prints the failing module and that module's
output, then exits with code 1. Set `PURVIEW_BUILD_STACKTRACE=1` to add stack traces when diagnosing the tool itself.

Malformed JSON in a configuration file fails with the file path and, where the parser supplies them, the line and position — never a bare deserialisation exception.

## `Build`

| Key | Default | Purpose |
| --- | --- | --- |
| `LogLevel` | `Information` | `Trace`/`Debug`/`Information`/`Warning`/`Error`/`Critical`/`None`; applied to the pipeline logger. `Information` reports every module's command output, progress, and completion; `Warning` keeps CI logs quiet |
| `ProjectType` | `DotNet` | `DotNet` (dotnet restore/build/test/pack) or `Web` (Bun commands from the root `package.json` scripts) |
| `Solution` | `src/Product.slnx` | Solution, project, or directory passed to restore/build/pack (dotnet only) |
| `Configuration` | `Release` | .NET configuration |
| `ArtifactsFolder` | `artifacts` | Package output directory |
| `CleanArtifacts` | `true` | Delete and recreate `ArtifactsFolder` before the run produces anything, so validation, publishing, and release uploads only see the current run's packages. Ignored when `RunPack` is `false` |
| `RunTests` | `true` | Enable discovered tests |
| `TestRoot` | `src/tests` | Test discovery root (relative to the repository root) |
| `TestPatterns` | `*Tests.csproj` | Comma-separated project search patterns applied under `TestRoot` |
| `TestProjects` | `*` | Comma-separated project names/globs to run; `*` runs all discovered |
| `TestFramework` | `TUnit` | `TUnit` (tree-node filter) or `xUnit` (VSTest filter) |
| `TestFilter` | `/*/*/*/*/` | TUnit tree-node filter or xUnit `--filter`; empty disables it |
| `RunLint` | `true` | Restore local tools and run CSharpier check (dotnet) or `format:check` + `lint` scripts (Web) |
| `RunPack` | `true` | Enable packing |
| `ValidatePack` | `true` | Enable pack validation (dotnet only; always skipped for Web) |
| `WebInstallCommand` | `bun install` | Install command for `ProjectType=Web` |
| `WebBuildCommand` | `bun run build` | Build command for `ProjectType=Web`; when left at the default the module first runs a `data:sync` script if one is declared |
| `WebLintCommand` | `bun run lint` | Lint command for `ProjectType=Web` |
| `WebFormatCheckCommand` | `bun run format:check` | Format-check command for `ProjectType=Web` |
| `WebTestCommand` | `bun run test` | Test command for `ProjectType=Web` |
| `WebBuildOutput` | `src/dist` | Directory zipped into `ArtifactsFolder` by the Web pack step |

## `PackValidation`

| Key | Default | Purpose |
| --- | --- | --- |
| `RequireSymbolPackage` | `true` | Every `.nupkg` must have a matching `.snupkg` and vice versa, except analyzer-only packages that embed their PDBs under `analyzers/dotnet/` |
| `RequireSymbolFiles` | `true` | Every `.snupkg` must contain at least one `.pdb` |
| `RequireSourceLink` | `false` | Every `.dll`/`.exe` must have a matching portable PDB containing a Source Link record |
| `RequireDeterministic` | `false` | Every `.dll`/`.exe` must be built deterministically (the PE carries the Reproducible debug directory entry) |
| `RequiredCompilerFlags` | `[]` | Compiler-flag `key=value` entries that must appear in each assembly's PDB compiler-flags record (e.g. `optimization=release`) |
| `RequiredContent` | `{}` | Package-id glob → entry-path globs that must be present in the `.nupkg` (`"*"` matches every package). Entries may use the `$(TFM)` target-framework partial token, e.g. `lib/$(TFM)/Foo.dll` |
| `ForbiddenContent` | `{}` | Package-id glob → entry-path globs that must not be present in the `.nupkg` (`"*"` matches every package). Also supports the `$(TFM)` partial token |
| `RequireExplicitContent` | `true` | Makes `RequiredContent` exhaustive: every generated package must match a rule, and every non-metadata entry must match a declared glob; undeclared packages/entries are errors |

Content entry paths and package-id keys are matched as globs (case-insensitive), e.g. `tools/**/Foo.dll` or `**/*.pdb`. Entries may also contain the `$(TFM)` partial token (e.g. `lib/$(TFM)/Foo.dll`), which expands to one entry per target framework the package actually ships (short folder name, discovered from the package's own content groups); each expanded entry is checked independently. Required content is satisfied when any package entry matches; forbidden content fails when any entry matches. When `RequireExplicitContent` is `true`, `RequiredContent` becomes exhaustive: every generated package must match a rule, and every non-metadata entry in a matched package must match a declared (and `$(TFM)`-expanded) glob — undeclared packages or entries are errors. PDBs are normally delivered through `.snupkg`, but analyzer-only packages may embed them under `analyzers/dotnet/` in the `.nupkg`; those packages do not require a sibling `.snupkg`. The assembly checks (`RequireSourceLink`, `RequireDeterministic`, `RequiredCompilerFlags`) inspect each `.dll`/`.exe` in the `.nupkg` (PE header) and its sibling portable PDB in the `.snupkg` or analyzer-slot PDB in the `.nupkg`; they only apply to assemblies the package ships symbols for. Determinism is detected via the PE's Reproducible debug directory entry, source link via the PDB's Source Link record, and compiler flags via the PDB's key/value compiler-flags record (matched case-insensitively, e.g. `optimization=release`).

> **Tool defaults vs code defaults.** The shipped `appsettings.json` sets `RequireSourceLink: false`, `RequireDeterministic: false`, and `RequiredCompilerFlags: []`. The C# property initializers in `PackValidationSettings` default those to `true`/`true`/`["optimization=release"]`, but because `appsettings.json` always loads and wins over code defaults, the effective shipped defaults are the `false`/`false`/`[]` values shown above.
>
> `RequireExplicitContent` is the opposite case and was previously documented incorrectly as `false`. It was **absent** from `appsettings.json`, so the C# initialiser's `true` survived and the effective default has always been `true`. It is now stated explicitly in `appsettings.json` so the shipped value is visible rather than inferred. Behaviour is unchanged. Note what `true` means with an empty `RequiredContent`: every produced package must declare its content, so a repository that packs anything without a matching `RequiredContent` rule gets an error. Set `"RequireExplicitContent": false` to opt out, as this repository's own `purview-build.json` does.

## `NuGet`

| Key | Default | Purpose |
| --- | --- | --- |
| `FeedUrl` | nuget.org v3 | Remote package source |
| `TrustedPublishing` | `false` | Push without an API key (NuGet Trusted Publishing / OIDC) |
| `APIKey` | unset | Secret; use `NUGET_APIKEY` or `NuGet__APIKey` (configuration keys are case-insensitive, so `NUGET__APIKEY` also binds here) |
| `EnvAPIKey` | unset | Binds `NuGet__NUGET_APIKEY`; also falls back to process env `NUGET_APIKEY`/`NUGET_API_KEY` |

## `PublishLocalNuGet`

| Key | Default | Purpose |
| --- | --- | --- |
| `LocalFeedPath` | unset | Absolute local package source |
| `EnvLocalFeedPath` | unset | Binds `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH`; also falls back to process env `LOCAL_NUGET_FEED_PATH` |
| `OverwriteExistingPackages` | `true` | Overwrite packages already in the local feed |
| `ShutdownDotnetBuilderServer` | `true` | Shut down the dotnet build server after publishing |
| `ClearPackageCache` | `true` | Clear the local NuGet package caches for the published packages |

## `GitHub`

| Key | Default | Purpose |
| --- | --- | --- |
| `AccessToken` | unset | Secret; use `GITHUB_TOKEN` |
| `EnvAccessToken` | unset | Binds `GitHub__GITHUB_TOKEN`; also falls back to process env `GITHUB_TOKEN` |
| `ProductHeader` | `Purview.Build.Pipeline` | GitHub API product header |

## `Version`

| Key | Default | Purpose |
| --- | --- | --- |
| `Source` | `PackageJson` | Where the version comes from. `PackageJson` reads the `version` field of the repository root `package.json` — the only implemented source |
| `Strictness` | `NuGet` | `NuGet` accepts everything `NuGetVersion` does, including four-part ("legacy") versions such as `13.5.3.10` and one/two-part versions such as `1.0`. `SemVer2` is the stricter opt-in: three numeric components with optional prerelease and build metadata |

> **Why `NuGet` is the default.** It is exactly what the pipeline has always accepted. Two consuming repositories ship four-part versions (`aspirec4` at `13.5.3.10`, `build-sdk` at `1.0.2.2`), so defaulting to `SemVer2` would stop them releasing. Set `Version:Strictness=SemVer2` per repository to tighten it.

`MinVer`, `release-please` and changesets cannot produce a four-part version, so that path is manual-only — an escape hatch, not a routine flow. `REL006` gates where such a version may be released from.

## `Release`

| Key | Default | Purpose |
| --- | --- | --- |
| `Mode` | `None` | Preset: `None`, `LocalNuGet`, `NuGet`, or `GitHubRelease`. Derives `Publish` and `GitHubRelease` |
| `Publish` | derived from `Mode` | Push packages. Explicitly setting it wins over the `Mode` preset |
| `GitHubRelease` | derived from `Mode`, then the channel | Create the tag and GitHub release. Explicitly setting it wins over both |
| `Channel` | `stable` | Named channel, selecting the feed and the label policy |
| `Channels` | `{}` | Channel name → `{ FeedUrl, GitHubRelease, MarkPrerelease, LabelPattern }` |
| `DryRun` | `false` | Run the full pipeline but skip publishing and the GitHub release, logging what each would have done |
| `UploadArtifacts` | `false` | Upload every file in `Build:ArtifactsFolder` as GitHub release assets |
| `MarkPrerelease` | `true` | Create the GitHub release as a prerelease when the package version is a prerelease (for example `2.0.0-prerelease.25`). Affects GitHub release metadata only — NuGet publication is unaffected |

`Mode` remains the primary switch, and the derived values reproduce the pipeline's long-standing behaviour exactly:

| `Mode` | `Publish` | `GitHubRelease` |
| --- | --- | --- |
| `None` | `false` | `false` |
| `NuGet` | `true` | `true` |
| `GitHubRelease` | `false` | `true` |
| `LocalNuGet` | `false` | `false` |

### `Release:Channels`

A channel lets a preview line publish elsewhere without cutting a GitHub release:

```json
{
  "Release": {
    "Mode": "NuGet",
    "Channel": "preview",
    "Channels": {
      "preview": {
        "FeedUrl": "https://nuget.pkg.github.com/purview-dev/index.json",
        "GitHubRelease": false,
        "MarkPrerelease": true,
        "LabelPattern": "preview"
      }
    }
  }
}
```

An unset channel member falls back to the top-level `Release`/`NuGet` setting. An explicitly set `Release:Publish`/`Release:GitHubRelease` wins over the channel.

### `Release:Eligibility`

Rules carry permanent `REL0nn` identifiers: never renumbered, never repurposed. Evaluation short-circuits in ID order, and **skip** (already released; exit 0) is distinct from **fail** (policy violation; exit 1).

| ID | Rule | Enabled by default |
| --- | --- | --- |
| `REL001` | Version parses as valid for the configured strictness | yes |
| `REL002` | `v{version}` is not already tagged, and the version is not already on the target feed | yes |
| `REL003` | A stable (non-prerelease) version originates only from a ref in `StableRefs` | yes |
| `REL004` | A non-zero PATCH component originates only from a ref matching `ServicingRefs` | no |
| `REL005` | Version is strictly greater than the highest existing version on the same line | no |
| `REL006` | A four-part version is permitted only when `AllowFourPart` and the ref matches `FourPartRefs` | no |
| `REL007` | The prerelease label matches the channel's allowed pattern | no |

`REL002` checks the tag always. The feed half is only judged when `Release:Context:PublishedVersions` supplies the feed state; otherwise the rule reports that the feed was not consulted. That matches the gate this replaces, which only ever checked the tag, and keeps publication idempotent through `--skip-duplicate`.

Three policies ship built in, so adopting a different release model is one setting plus the caller's `on:` block — no policy block to copy:

| Policy | Rules | Refs |
| --- | --- | --- |
| `ReleaseOnMain` (default) | `REL001`, `REL002`, `REL003` | `StableRefs`: `refs/heads/main`, `refs/heads/release` |
| `TrunkReservesMinor` | `REL001`–`REL005` | `StableRefs`/`ServicingRefs`: `refs/heads/release/*`; `TrunkRefs`: `refs/heads/main` |
| `FourPartServicing` | `TrunkReservesMinor` + `REL006` | as above, plus `AllowFourPart` and `FourPartRefs`: `refs/heads/release/*` |

| Key | Default | Purpose |
| --- | --- | --- |
| `Policy` | `ReleaseOnMain` | The selected policy name |
| `Policies` | `{}` | Additional policies, merged over the built-ins. Declaring a built-in's name overrides it |

Each policy accepts:

| Key | Purpose |
| --- | --- |
| `Inherits` | Name of the policy this one is a diff from. A missing parent is an error naming the unresolved policy |
| `Rules` | Either an absolute list (`["REL001","REL002"]`) or deltas against the parent (`["+REL006","-REL005"]`). Mixing the two forms is an error |
| `StableRefs`, `ServicingRefs`, `TrunkRefs`, `FourPartRefs` | Ref patterns. A single trailing `*` is a prefix match (`refs/heads/release/*`). A non-empty child list replaces the parent's, so a policy can narrow a ref set |
| `AllowFourPart` | Whether four-part versions are permitted at all (`REL006`) |

```json
{
  "Release": {
    "Eligibility": {
      "Policy": "TrunkReservesMinor"
    }
  }
}
```

> **The ownership boundary.** The tool *evaluates* eligibility and reports it; the workflow *decides* and sets `Release__Mode`. The tool never silently declines to release when it has been asked to.

### `Release:Context` — simulated evaluation

All optional, all honoured by `release-explain`. Used to answer "what would happen" on a developer machine.

| Key | Purpose |
| --- | --- |
| `Ref` | Override the evaluated ref (defaults to `GITHUB_REF`, else the current git branch) |
| `ExistingTags` | Path to a newline- or JSON-delimited tag list, used instead of querying git |
| `PublishedVersions` | Path to a version list treated as already on the feed, instead of querying it |

When any of these is set, evaluation performs **no process or network lookups** and every report states that simulated context was used — a simulated verdict must never be mistakable for a real one. The real repository's branch and tags do not leak in.

**Interlock:** if any `Release:Context:*` key is set and the run would really publish, the pipeline fails before any module runs. Use `release-explain` or `Release:DryRun=true` to evaluate without publishing.

## Example

```json
{
  "Build": {
    "Solution": "src/MyProduct.slnx",
    "TestRoot": "src/tests",
    "TestPatterns": "*Tests.csproj",
    "TestFilter": "/*/*/*/*[Category=Unit]"
  },
  "PackValidation": {
    "RequireSymbolPackage": true,
    "RequireSourceLink": true,
    "RequireDeterministic": true,
    "RequiredCompilerFlags": ["optimization=release"],
    "RequiredContent": {
      "my.product": ["lib/netstandard2.0/My.Product.dll"]
    }
  },
  "Release": { "Mode": "None" }
}
```

## See also

- [Pipeline Modules](Pipeline-Modules.md)
- [Secrets and Environment Variables](Secrets-and-Environment-Variables.md)