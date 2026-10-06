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

## The five layers

A release passes through five layers. Which side of the ownership boundary each sits on is the thing to keep straight:

| Layer | Owned by | What it decides |
| --- | --- | --- |
| **Trigger** | the **workflow** | Whether a release attempt happens at all: the caller's `on:` block and its concurrency group |
| **Version** | the **tool** | What is being released: `Version:Source` resolves an ordered set of release units (version, line, channel, tag) |
| **Eligibility** | **evaluated by the tool, decided by the workflow** | Whether this version may be released from this ref: the `REL0nn` rules produce a verdict and a message; the workflow reads it and sets `Release__Mode` |
| **Execution** | the **tool** | Clean, restore, build, test, lint, pack, validate |
| **Publication** | the **tool** | Push packages to the channel's feed, create `v{version}` and the GitHub release |

The eligibility layer is the one worth being precise about. Before this split, the decision lived entirely in the reusable workflow as bash — unreachable from a unit test and unrunnable locally without `act`. Moving the *evaluation* into the tool (`release-explain`, plus `ReportEligibilityModule` for observability during a real run) makes it testable and locally runnable while leaving the *decision* with the workflow. The tool never silently declines to release when it has been asked to; it reports, and the workflow acts.

`release-explain` is an informational command in the same family as `-v` and `--help`: it prints and exits without running any module, so it adds no node to the execution graph. `ReportEligibilityModule` is category `Build`, not `Release`, and every failure path inside it is a warning — a misconfigured policy must not fail a build that would otherwise succeed.

## Configuration discovery

`purview-build.json` is resolved before the pipeline is built: an explicit `--config` / `PURVIEW_BUILD_CONFIG` location, or the first hit from a documented probe list held as data. Relative paths inside the file always anchor to the repository root, never to the file's own directory, so moving the file changes nothing else. See [Configuration Reference](Configuration-Reference.md#where-the-configuration-file-lives).

Because configuration is composed before any module (and therefore any pipeline context) exists, the locality check that gates opt-in machine-local user configuration is a first-party helper rather than `ctx.IsRunningLocally()`.

## Module ordering

The pipeline is registered in `BuildPipeline.cs` (`Program.cs` is the CLI boundary: informational options, the version banner, configuration binding, and failure reporting) in this order, with explicit `[DependsOn]` edges defining the graph:

```text
CleanArtifactsModule → RestoreModule → BuildModule → RunTestsModule ──┬→ PackModule → ValidatePackModule
CleanArtifactsModule → RestoreModule → LintModule                     │
CleanArtifactsModule → VersionModule ─────────────────────────────────┤
                       VersionModule → ReportEligibilityModule        │
                                                                      │
                                  (LintModule and ReportEligibilityModule
                                   are not inputs to PackModule)
```

Explicit `[DependsOn]` edges:

- `VersionModule` and `RestoreModule` depend on `CleanArtifactsModule`, so the artifacts folder is reset before any other module starts.
- `BuildModule` depends on `RestoreModule`.
- `LintModule` depends on `RestoreModule` (Web lint needs the dependencies installed by `bun install`; dotnet lint is unaffected beyond running after restore). **Nothing depends on `LintModule`** — it is a gate, not an input.
- `RunTestsModule` depends on `BuildModule`.
- `ReportEligibilityModule` depends on `VersionModule`. Nothing depends on it; it only reports.
- `PackModule` depends on `RunTestsModule` and `VersionModule`.
- `ValidatePackModule` depends on `PackModule`.
- `PublishNuGetModule` depends on `PackModule`, `ValidatePackModule`, and `RunTestsModule`.
- `PublishLocalNuGetModule` depends on `PackModule` and `ValidatePackModule`.
- `CreateGitHubReleaseModule` depends on `PublishNuGetModule`, `ValidatePackModule`, and `VersionModule`.

Module categories are `Build` and `Release`. `PublishNuGetModule` and `CreateGitHubReleaseModule` are `Release`; everything else — including `PublishLocalNuGetModule`, which never reaches a remote feed, and `ReportEligibilityModule` — is `Build`.

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

`Release:Mode` is a preset over two independent switches, `Release:Publish` and `Release:GitHubRelease`, either of which may be set explicitly to override what the preset implies:

- `None`: build/test/pack may run, but nothing publishes.
- `LocalNuGet`: pushes packages to the resolved local feed for developer testing. Only honoured when the tool runs **locally**; it is ignored in CI, so it cannot be driven through the reusable workflows.
- `NuGet`: pushes packages to the channel's feed and, by default, creates a GitHub release.
- `GitHubRelease`: creates a GitHub release (optionally uploading `ArtifactsFolder` assets) without publishing NuGet packages.

`Release:Channel` selects the feed and label policy, so a `preview` channel can publish to GitHub Packages with `GitHubRelease: false` — preview packages without a GitHub release. `Release:DryRun` runs everything and skips both publish steps, logging what each would have done.

The workflow decides whether a version is eligible to release and sets `Release__Mode`; the tool evaluates and reports that eligibility (see [the five layers](#the-five-layers)). Credentials remain CI secrets.

Publication precedes tagging, and publication is idempotent (`--skip-duplicate`), so re-running after a failed release is safe.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Release Flow](Release-Flow.md)
- [Release Models](Release-Models.md)