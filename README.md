# Build

[![NuGet version](https://img.shields.io/nuget/v/Purview.Build.svg)](https://www.nuget.org/packages/Purview.Build)
[![Release](https://github.com/purview-dev/build/actions/workflows/release.yml/badge.svg)](https://github.com/purview-dev/build/actions/workflows/release.yml)

`Purview.Build` is the shared build/test/release system for the `purview-dev` organisation. It is a Generalized Modular Pipelines pipeline (based on the `PipelineCLI` originally developed in `sourcegeneratorframework`) packaged as a pinned .NET tool and exposed through a shared GitHub composite action and thin reusable workflows.

Consuming repositories own configuration; they do not own pipeline source code. Version, paths, feature switches, and release-mode selection are per-repository.

## Delivery surfaces

The same implementation is available three ways:

1. **`Purview.Build` dotnet tool** — NuGet package published to nuget.org. Run anywhere a .NET SDK exists (locally via `just`, in GitHub Actions, or another CI service).
2. **Composite action** `purview-dev/build/.github/actions/purview-build` — for repositories that want the action embedded directly in one of their own jobs.
3. **Reusable workflows** `purview-dev/build/.github/workflows/purview-build.yml` and `.../purview-release.yml` — thin `workflow_call` wrappers with structured inputs/secrets.

## Minimal repository setup (reusable workflow)

The examples below reference the workflows at `@main`, so they always run the latest version of the workflow/action code. The `@ref` suffix is required for cross-repository references and resolves the workflow/action to a specific commit; there is no `@latest`. Pin a release tag (e.g. `@v0.2.0`) instead if you want reproducible workflow code.

> **Two independent version axes.** The `@ref` selects the workflow/action *code*, while the
> `build-version` input selects the installed `Purview.Build` *tool*. Omit `build-version` to
> always install the latest stable tool, or pin it (e.g. `build-version: 0.2.0`) for
> reproducibility. Mixing a pinned old `@ref` with a floating `build-version` runs newer tool
> code through older workflow inputs.

```yaml
# .github/workflows/pr.yml
name: PR
on:
  pull_request:
    branches: [main]
jobs:
  build:
    uses: purview-dev/build/.github/workflows/purview-build.yml@main
    # `build-version` is optional; when omitted, the latest stable Purview.Build
    # from nuget.org is installed. Pin it (e.g. `build-version: 0.2.0`) for
    # reproducible builds.
    secrets: inherit
```

```yaml
# .github/workflows/release.yml — release on main
name: Release
on:
  push:
    branches: [main]
concurrency:
  # Serialize releases; callers own concurrency (see docs/wiki/Release-Flow.md).
  group: release-${{ github.ref }}
  cancel-in-progress: false
jobs:
  release:
    uses: purview-dev/build/.github/workflows/purview-release.yml@main
    with:
      release-mode: NuGet
    secrets: inherit
```

For the **main-as-head / release-branch model**, point the release caller at the release branch instead:

```yaml
on:
  push:
    branches: [release]
```

The reusable workflow checks whether `v{version}` (read from `package.json`) is already tagged and skips if so, so merging `main` into `release` releases exactly once.

The reusable workflows install the pinned CLI version (or the latest stable when `build-version` is omitted) from nuget.org; the consuming repository adds `purview-build.json` and a root `package.json` version. It does not need a copied pipeline project or package-source credentials.

### Minimal repository setup (composite action)

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: purview-dev/build/.github/actions/purview-build@main
        env:
          Build__TestFilter: "/*/*/*/*[Category=Unit]"
```

The action's `build-version` input is optional. When omitted, `dotnet tool install` resolves the latest stable `Purview.Build` from nuget.org; pass an exact version to pin the build.

### Local use

```shell
dotnet tool install Purview.Build --tool-path ./.tools
./.tools/purview-build
```

Omit `--version` to install the latest stable release.

### Command line

The tool behaves like any other CLI build tool:

```shell
purview-build --version   # print the tool version and exit
purview-build -v          # same
purview-build --help      # usage, options, and configuration keys
```

Every run prints the tool version first, then a line for each module as it starts (`Running BuildModule...`) followed by
its completion and duration. A failing run reports the failed module and that module's output, then exits with code 1
instead of dumping a .NET stack trace; set `PURVIEW_BUILD_STACKTRACE=1` when you need the stack trace for diagnosing the
tool itself. Verbosity is controlled by `Build:LogLevel` (default `Information`, which includes each module's command
output and progress).

## Configuration

Add `purview-build.json`. Everything is optional; defaults are baked into the tool. Configuration precedence is command line, environment variables, `purview-build.json`, opt-in machine-local user config, then defaults. Nested environment keys use `__`, for example `Release__Mode=NuGet`.

The file is found at the repository root, or at `.config/`, `.build/`, `build/`, `.purview/` or `.github/` beneath it — first match wins, and any lower-priority file that also exists is reported as shadowed rather than merged. Select one explicitly with `--config <path>` or `PURVIEW_BUILD_CONFIG`. Relative paths inside the file always anchor to the repository root, wherever the file itself lives. Run `purview-build --help` to print the probe order and the path that resolved. See the [configuration reference](docs/wiki/Configuration-Reference.md#where-the-configuration-file-lives).

The pipeline is dotnet-first but supports **Web** projects (Bun/JS/TS, e.g. the Astro/Starlight `purview-dev` portal) by setting `Build:ProjectType=Web`: restore/build/lint/test then run the repository's root `package.json` scripts (`bun install`, `bun run build`, `bun run format:check`/`bun run lint`, `bun run test`), and the pack step zips `Build:WebBuildOutput` (default `src/dist`) into `Build:ArtifactsFolder` for the GitHub release. Every Web command is overridable via the `Web*` settings below.

```json
{
  "Build": {
    "ProjectType": "Web",
    "WebBuildCommand": "bun run build",
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

Secrets must not be committed. They are supplied through `NUGET_APIKEY` (or `NuGet__APIKey`), `GITHUB_TOKEN`, and `LOCAL_NUGET_FEED_PATH` (or `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH`).

Release eligibility is rule-based and selected per repository with `Release:Eligibility:Policy`: `ReleaseOnMain` (the default, reproducing the pipeline's long-standing behaviour), `TrunkReservesMinor` (per-line release branches), or `FourPartServicing`. Run `purview-build release-explain` to see the decision, the rule trail and the publication that would result, without running anything. See [release models](docs/wiki/Release-Models.md).

See the [Documentation](#documentation) section below for the architecture, configuration reference, and release strategy.

## Pipeline

```text
CleanArtifacts → { Version, Restore → Build → Test, Restore → Lint } → Pack → Validate → Publish → GitHub release
                    └→ ReportEligibility
```

`Lint` and `ReportEligibility` are gates and reports, not inputs: nothing depends on either.

`CleanArtifacts` deletes and recreates `Build:ArtifactsFolder` before anything else runs, so pack, validation, publishing, and release uploads only ever see the packages from the current run (set `Build:CleanArtifacts=false` to keep existing artifacts). `Version` resolves the release units from `Version:Source` (default: the `version` field of the root `package.json`); `Version:Strictness` defaults to `NuGet`, which accepts four-part versions. `ReportEligibility` evaluates the release rules and reports the verdict without acting on it. Lint restores local tools and runs CSharpier. Tests are discovered under `Build:TestRoot`/`Build:TestPatterns` and run with a TUnit tree-node filter (or an xUnit filter). Pack validation inspects each `.nupkg`/`.snupkg` against required/forbidden content rules (glob patterns) and can enforce source link, deterministic builds, and compiler flags on the packaged assemblies. Analyzer-only packages can embed portable PDBs under `analyzers/dotnet/` without requiring a `.snupkg`. Publication and GitHub release steps are controlled by `Release:Mode` (`None`, `LocalNuGet`, `NuGet`, `GitHubRelease`) — a preset over the independent `Release:Publish` and `Release:GitHubRelease` switches — and by the `Build__Run*` switches. `Release:DryRun` runs everything and skips both publish steps. `LocalNuGet` is only honoured when the tool runs locally; it is ignored in CI (for example via a reusable workflow).

## Repository CI/CD

This repository dogfoods the shared tool: CI builds and packs the tool from source, installs the generated package, then runs `purview-build` against this repository so the project builds and packs itself. Restore and warnings-as-errors compilation gate every pull request and merge.

On a push to `main`, the release workflow rebuilds and reinstalls the tool from the current source, asks it to evaluate release eligibility (`release-explain --format=json`), and — when the verdict is `Release` — runs it with `Release__Mode=NuGet`, `NuGet__FeedUrl` pointing at nuget.org, and `Release__UploadArtifacts=true`. The tool therefore publishes the immutable package to `https://api.nuget.org/v3/index.json` and tags and releases itself (`v{Version}` + generated-notes GitHub release with the package attached) — exactly like every other purview-dev repository. Maintainers bump the `package.json` version and merge; they do not create release tags manually.

GitHub initially creates NuGet packages as private. To make sure every package is **Internal** (consumable by all Purview-Dev members), an organization owner should set the org default: Purview-Dev → Settings → Packages → **Package Creation** → **Internal**, and change any already-published package's visibility in its **Package settings** → **Danger Zone**. See [docs/wiki/Release-Flow.md](docs/wiki/Release-Flow.md) for the exact steps and the `gh api` alternative.

## Documentation

- [Homepage](https://purview.dev/projects/build/)
- [Documentation](https://purview.dev/docs/build/)
- [Architecture](docs/wiki/Architecture.md)
- [Configuration reference](docs/wiki/Configuration-Reference.md)
- [Release flow](docs/wiki/Release-Flow.md)
- [Release models](docs/wiki/Release-Models.md)
