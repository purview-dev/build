# Architecture and configuration

## Decision

The shared artifact is a .NET tool NuGet package, not a reusable workflow and not an MSBuild SDK. Modular Pipelines is an executable orchestration system, so a tool is its natural package boundary. A tool manifest gives each consumer deterministic version pinning and Renovate/Dependabot-compatible upgrades. It also keeps GitHub Actions as a thin host; the same command runs locally, in GitHub Actions, or in another CI service.

The implementation is the generalized `PipelineCLI` that originated in `sourcegeneratorframework` (its most advanced version, including pack validation). It supersedes the earlier `Purview.Build` modules.

The repository additionally exposes:

- a **composite action** (`.github/actions/purview-build`) that installs a pinned `Purview.Build` version and runs it, for repositories embedding the build in their own jobs, and
- two **reusable workflows** (`purview-build.yml`, `purview-release.yml`) that wrap that logic with structured inputs/secrets, reducing a consumer to one reusable-workflow job plus `purview-build.json`.

An MSBuild SDK remains a possible future companion for shared compile-time properties, analyzers, or package metadata. It should not own CI orchestration.

## Ownership boundary

The package owns module implementation, dependency ordering, safe defaults, secret lookup, NuGet/GitHub integration, and diagnostics. Each repository owns its tool-version pin, paths and discovery patterns, feature switches, and release-mode selection. A project needing truly custom behavior can invoke its own command before/after the shared tool; a generally useful variation should be added as a typed option here.

## Configuration reference

### `Build`

| Key | Default | Purpose |
|---|---|---|
| `Solution` | `src/Product.slnx` | Solution, project, or directory passed to restore/build/pack |
| `Configuration` | `Release` | .NET configuration |
| `ArtifactsFolder` | `artifacts` | Package output directory |
| `RunTests` | `true` | Enable discovered tests |
| `TestRoot` | `src/tests` | Test discovery root (relative to the repository root) |
| `TestPatterns` | `*Tests.csproj` | Comma-separated project search patterns applied under `TestRoot` |
| `TestProjects` | `*` | Comma-separated project names/globs to run; `*` runs all discovered |
| `TestFramework` | `TUnit` | `TUnit` (tree-node filter) or `xUnit` (VSTest filter) |
| `TestFilter` | `/*/*/*/*/` | TUnit tree-node filter or xUnit `--filter`; empty disables it |
| `RunLint` | `true` | Restore local tools and run CSharpier check |
| `RunPack` | `true` | Enable packing |
| `ValidatePack` | `true` | Enable pack validation |

### `PackValidation`

| Key | Default | Purpose |
|---|---|---|
| `RequireSymbolPackage` | `true` | Every `.nupkg` must have a matching `.snupkg` and vice versa |
| `RequireSymbolFiles` | `true` | Every `.snupkg` must contain at least one `.pdb` |
| `RequiredContent` | `{}` | Package id → entry paths that must be present in the `.nupkg` |
| `ForbiddenContent` | `{}` | Package id → entry paths that must not be present in the `.nupkg` |

### `NuGet`

| Key | Default | Purpose |
|---|---|---|
| `FeedUrl` | nuget.org v3 | Remote package source |
| `TrustedPublishing` | `false` | Push without an API key (NuGet Trusted Publishing / OIDC) |
| `APIKey` | unset | Secret; use `NUGET_APIKEY` or `NuGet__ApiKey` |
| `EnvAPIKey` | unset | Binds `NuGet__NUGET_APIKEY`; also falls back to process env `NUGET_APIKEY`/`NUGET_API_KEY` |

### `PublishLocalNuGet`

| Key | Default | Purpose |
|---|---|---|
| `LocalFeedPath` | unset | Absolute local package source |
| `EnvLocalFeedPath` | unset | Binds `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH`; also falls back to process env `LOCAL_NUGET_FEED_PATH` |
| `OverwriteExistingPackages` | `true` | Overwrite packages already in the local feed |
| `ShutdownDotnetBuilderServer` | `true` | Shut down the dotnet build server after publishing |
| `ClearPackageCache` | `true` | Clear the local NuGet package caches for the published packages |

### `GitHub`

| Key | Default | Purpose |
|---|---|---|
| `AccessToken` | unset | Secret; use `GITHUB_TOKEN` |
| `EnvAccessToken` | unset | Binds `GitHub__GITHUB_TOKEN`; also falls back to process env `GITHUB_TOKEN` |
| `ProductHeader` | `Purview.Build.Pipeline` | GitHub API product header |

### `Release`

| Key | Default | Purpose |
|---|---|---|
| `Mode` | `None` | `None`, `LocalNuGet`, `NuGet`, or `GitHubRelease` |
| `UploadArtifacts` | `false` | Upload every file in `Build:ArtifactsFolder` as GitHub release assets |

## Project, testing, and release support

- **Project types**: the pipeline is dotnet-first (libraries, source generators, analyzers, MSBuild SDKs, Aspire hosting extensions). Non-dotnet project types (`Web` for full-stack apps, `WebExtension` for JS/Azure DevOps extensions) are designed as future module additions gated by configuration.
- **Testing types**: TUnit on Microsoft.Testing.Platform (default) and xUnit, both configurable via `TestFramework`/`TestFilter`. Non-dotnet runners (Vitest, Playwright, Jest, Astro) are future modules.
- **Release types**: nuget.org (API key or Trusted Publishing), GitHub Packages internal feed, local NuGet feed, GitHub release (optionally with package/vsix assets), and future Aspire-deploy / Azure DevOps marketplace publishing.

## Release behavior

- `None`: build/test/pack may run, but nothing publishes.
- `LocalNuGet`: pushes packages to the resolved local feed for developer testing.
- `NuGet`: pushes packages to the configured feed and, by default, creates a GitHub release.
- `GitHubRelease`: creates a GitHub release (optionally uploading `ArtifactsFolder` assets) without publishing NuGet packages.

The workflow decides whether a version is eligible to release (for example, only an untagged version on `main` or `release`) and sets `Release__Mode`. Credentials remain CI secrets.

## Repository root resolution

The tool locates the repository root by walking up from the current working directory to the nearest `package.json`; `Environment.CurrentDirectory` is set to that root before modules run. `MODULAR_PIPELINES_DIRECTORY` can override the directory containing `appsettings.json` when the defaults do not apply.

Command-line overrides use configuration syntax, for example:

```shell
dotnet purview-build --Build:TestPatterns=*IntegrationTests.csproj --Build:RunPack=false
```