# Purview.Build

`Purview.Build` is the shared build/test/release system for the `purview-dev` organisation. It is a Generalized Modular Pipelines pipeline (based on the `PipelineCLI` originally developed in `sourcegeneratorframework`) packaged as a pinned .NET tool and exposed through a shared GitHub composite action and thin reusable workflows.

Consuming repositories own configuration; they do not own pipeline source code. Version, paths, feature switches, and release-mode selection are per-repository.

[![Release](https://github.com/purview-dev/build/actions/workflows/release.yml/badge.svg)](https://github.com/purview-dev/build/actions/workflows/release.yml)

## Delivery surfaces

The same implementation is available three ways:

1. **`Purview.Build` dotnet tool** — NuGet package published to the Purview-Dev GitHub Packages feed. Run anywhere a .NET SDK exists (locally via `just`, in GitHub Actions, or another CI service).
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
dotnet tool install Purview.Build --tool-path ./.tools --version 0.2.1
./.tools/purview-build
```

## Configuration

Add `purview-build.json` at the repository root. Everything is optional; defaults are baked into the tool. Configuration precedence is command line, environment variables, `purview-build.json`, then defaults. Nested environment keys use `__`, for example `Release__Mode=NuGet`.

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
    "RequiredContent": {
      "my.product": ["lib/netstandard2.0/My.Product.dll"]
    }
  },
  "Release": { "Mode": "None" }
}
```

Secrets must not be committed. They are supplied through `NUGET_APIKEY` (or `NuGet__ApiKey`), `GITHUB_TOKEN`, and `LOCAL_NUGET_FEED_PATH` (or `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH`).

See [architecture and configuration](docs/architecture.md) and [release strategy](docs/releasing.md).

## Pipeline

```text
Version ───────────────┐
Restore → Build → Test ├→ Pack → Validate → Publish → GitHub release
           └→ Lint     │
Version ───────────────┘
```

`Version` reads the SemVer `version` field from `package.json`. Lint restores local tools and runs CSharpier. Tests are discovered under `Build:TestRoot`/`Build:TestPatterns` and run with a TUnit tree-node filter (or an xUnit filter). Pack validation inspects each `.nupkg`/`.snupkg` against required/forbidden content rules. Publication and GitHub release steps are controlled by `Release:Mode` (`None`, `LocalNuGet`, `NuGet`, `GitHubRelease`) and independently by the `Build__Run*` switches.

## Repository CI/CD

This repository dogfoods the shared tool: CI builds and packs the tool from source, installs the generated package, then runs `purview-build` against this repository so the project builds and packs itself. Locked restore and warnings-as-errors compilation gate every pull request and merge.

On a push to `main`, the release workflow rebuilds and reinstalls the tool from the current source, then runs it with `Release__Mode=NuGet`, `NuGet__FeedUrl` pointing at nuget.org, and `Release__UploadArtifacts=true`. The tool therefore publishes the immutable package to `https://api.nuget.org/v3/index.json` and tags and releases itself (`v{Version}` + generated-notes GitHub release with the package attached) — exactly like every other purview-dev repository. Maintainers bump the `package.json` version and merge; they do not create release tags manually.

GitHub initially creates NuGet packages as private. To make sure every package is **Internal** (consumable by all Purview-Dev members), an organization owner should set the org default: Purview-Dev → Settings → Packages → **Package Creation** → **Internal**, and change any already-published package's visibility in its **Package settings** → **Danger Zone**. See [docs/releasing.md](docs/releasing.md) for the exact steps and the `gh api` alternative.
