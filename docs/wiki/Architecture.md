# Architecture

## Decision

The shared artifact is a .NET tool NuGet package, not a reusable workflow and not an MSBuild SDK. Modular Pipelines is an executable orchestration system, so a tool is its natural package boundary. A tool manifest gives each consumer deterministic version pinning and Renovate/Dependabot-compatible upgrades. It also keeps GitHub Actions as a thin host; the same command runs locally, in GitHub Actions, or in another CI service.

The implementation is the generalized `PipelineCLI` that originated in `sourcegeneratorframework` (its most advanced version, including pack validation). It supersedes the earlier `Purview.Build` modules.

The repository additionally exposes:

- a **composite action** (`.github/actions/purview-build`) that installs a pinned `Purview.Build` version and runs it, for repositories embedding the build in their own jobs, and
- two **reusable workflows** (`purview-build.yml`, `purview-release.yml`) that wrap that logic with structured inputs/secrets, reducing a consumer to one reusable-workflow job plus `purview-build.json`.

An MSBuild SDK remains a possible future companion for shared compile-time properties, analyzers, or package metadata. It should not own CI orchestration.

## Ownership boundary

The package owns module implementation, dependency ordering, safe defaults, secret lookup, NuGet/GitHub integration, and diagnostics. Each repository owns its tool-version pin, paths and discovery patterns, feature switches, and release-mode selection. A project needing truly custom behavior can invoke its own command before/after the shared tool; a generally useful variation should be added as a typed option here.

## Module ordering

The pipeline is registered in `Program.cs` in this order, with explicit `[DependsOn]` edges defining the graph:

```text
VersionModule ──────────────┐
RestoreModule → BuildModule ├→ RunTestsModule → PackModule → ValidatePackModule
LintModule (independent)    │
VersionModule ──────────────┘
```

Explicit `[DependsOn]` edges:

- `BuildModule` depends on `RestoreModule`.
- `RunTestsModule` depends on `BuildModule`.
- `PackModule` depends on `RunTestsModule` and `VersionModule`.
- `ValidatePackModule` depends on `PackModule`.
- `PublishNuGetModule` depends on `PackModule`, `ValidatePackModule`, and `RunTestsModule`.
- `PublishLocalNuGetModule` depends on `PackModule` and `ValidatePackModule`.
- `CreateGitHubReleaseModule` depends on `PublishNuGetModule`, `ValidatePackModule`, and `VersionModule`.

See [Pipeline Modules](Pipeline-Modules.md) for per-module behavior and skip conditions.

## Repository root resolution

The tool locates the repository root by walking up from the current working directory to the nearest `package.json` (`PathHelpers.FindRepositoryRoot`); `Environment.CurrentDirectory` is set to that root before modules run. `MODULAR_PIPELINES_DIRECTORY` can override the directory containing `appsettings.json` when the defaults do not apply.

Command-line overrides use configuration syntax, for example:

```shell
dotnet purview-build --Build:TestPatterns=*IntegrationTests.csproj --Build:RunPack=false
```

## Project, testing, and release support

- **Project types**: the pipeline is dotnet-first (libraries, source generators, analyzers, MSBuild SDKs, Aspire hosting extensions), gated by `Build:ProjectType`. Non-dotnet project types are implemented as configuration-gated module branches:
  - `Web` (Bun/JS/TS sites such as the Astro/Starlight purview.dev portal) runs the repository's root `package.json` scripts: `bun install` (restore), `bun run build` (build, after an automatic `data:sync` when one is declared and the command is left at the default), `bun run format:check` + `bun run lint` (lint), and `bun run test` (tests). The Web pack step zips `Build:WebBuildOutput` into `Build:ArtifactsFolder` as `<package-name>-<version>.zip`. Every Web command is overridable via the `Web*` settings.
  - `WebExtension` (JS/Azure DevOps extensions) remains future work.
- **Testing types**: TUnit on Microsoft.Testing.Platform (default) and xUnit, both configurable via `TestFramework`/`TestFilter`. Web projects run the `WebTestCommand` (default `bun run test`); other non-dotnet runners (Vitest, Playwright, Jest) can be targeted by overriding that command.
- **Release types**: nuget.org (API key or Trusted Publishing), GitHub Packages internal feed, local NuGet feed, GitHub release (optionally with package/vsix assets), and future Aspire-deploy / Azure DevOps marketplace publishing.

## Release behavior

- `None`: build/test/pack may run, but nothing publishes.
- `LocalNuGet`: pushes packages to the resolved local feed for developer testing. Only honoured when the tool runs **locally**; it is ignored in CI, so it cannot be driven through the reusable workflows.
- `NuGet`: pushes packages to the configured feed and, by default, creates a GitHub release.
- `GitHubRelease`: creates a GitHub release (optionally uploading `ArtifactsFolder` assets) without publishing NuGet packages.

The workflow decides whether a version is eligible to release (for example, only an untagged version on `main` or `release`) and sets `Release__Mode`. Credentials remain CI secrets.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Release Flow](Release-Flow.md)