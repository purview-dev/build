# Configuration Reference

Configuration is optional in a consuming repository; defaults are baked into the tool. Add `purview-build.json` at the repository root to override them.

## Precedence

Command line > environment variables > `purview-build.json` > baked-in defaults (`appsettings.json`) > code-level defaults.

- Environment variables use `__` for nesting, for example `Release__Mode=NuGet`.
- Command-line overrides use configuration syntax, for example `--Build:RunPack=false`.
- Secrets must not be committed; they are supplied at runtime through env vars / CI secrets. See [Secrets and Environment Variables](Secrets-and-Environment-Variables.md).

## `Build`

| Key | Default | Purpose |
| --- | --- | --- |
| `LogLevel` | `Warning` | `Trace`/`Debug`/`Information`/`Warning`/`Error`/`Critical`/`None`; used by the pipeline logger |
| `ProjectType` | `DotNet` | `DotNet` (dotnet restore/build/test/pack) or `Web` (Bun commands from the root `package.json` scripts) |
| `Solution` | `src/Product.slnx` | Solution, project, or directory passed to restore/build/pack (dotnet only) |
| `Configuration` | `Release` | .NET configuration |
| `ArtifactsFolder` | `artifacts` | Package output directory |
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
| `RequireSymbolPackage` | `true` | Every `.nupkg` must have a matching `.snupkg` and vice versa |
| `RequireSymbolFiles` | `true` | Every `.snupkg` must contain at least one `.pdb` |
| `RequireSourceLink` | `false` | Every `.dll`/`.exe` must have a matching portable PDB containing a Source Link record |
| `RequireDeterministic` | `false` | Every `.dll`/`.exe` must be built deterministically (the PE carries the Reproducible debug directory entry) |
| `RequiredCompilerFlags` | `[]` | Compiler-flag `key=value` entries that must appear in each assembly's PDB compiler-flags record (e.g. `optimization=release`) |
| `RequiredContent` | `{}` | Package-id glob → entry-path globs that must be present in the `.nupkg` (`"*"` matches every package) |
| `ForbiddenContent` | `{}` | Package-id glob → entry-path globs that must not be present in the `.nupkg` (`"*"` matches every package) |

Content entry paths and package-id keys are matched as globs (case-insensitive), e.g. `tools/**/Foo.dll` or `**/*.pdb`. Required content is satisfied when any package entry matches; forbidden content fails when any entry matches. The assembly checks (`RequireSourceLink`, `RequireDeterministic`, `RequiredCompilerFlags`) inspect each `.dll`/`.exe` in the `.nupkg` (PE header) and its sibling portable PDB in the `.snupkg` (custom debug info records); they only apply to assemblies the package ships symbols for. Determinism is detected via the PE's Reproducible debug directory entry, source link via the PDB's Source Link record, and compiler flags via the PDB's key/value compiler-flags record (matched case-insensitively, e.g. `optimization=release`).

> **Tool defaults vs code defaults.** The shipped `appsettings.json` sets `RequireSourceLink: false`, `RequireDeterministic: false`, and `RequiredCompilerFlags: []`. The C# property initializers in `PackValidationSettings` default those to `true`/`true`/`["optimization=release"]`, but because `appsettings.json` always loads and wins over code defaults, the effective shipped defaults are the `false`/`false`/`[]` values shown above.

## `NuGet`

| Key | Default | Purpose |
| --- | --- | --- |
| `FeedUrl` | nuget.org v3 | Remote package source |
| `TrustedPublishing` | `false` | Push without an API key (NuGet Trusted Publishing / OIDC) |
| `APIKey` | unset | Secret; use `NUGET_APIKEY` or `NuGet__ApiKey` |
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

## `Release`

| Key | Default | Purpose |
| --- | --- | --- |
| `Mode` | `None` | `None`, `LocalNuGet`, `NuGet`, or `GitHubRelease` |
| `UploadArtifacts` | `false` | Upload every file in `Build:ArtifactsFolder` as GitHub release assets |

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