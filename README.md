# Purview.Build

`Purview.Build` is the shared build CLI for the `purview-dev` organisation. It packages the existing Modular Pipelines implementation as a pinned .NET tool: consuming repositories own configuration, but not pipeline source code.

## Minimal repository setup

The recommended GitHub Actions setup is a single reusable-workflow job. Pin both the workflow ref and package version:

```yaml
name: Build
on: [pull_request, push]
permissions:
  contents: write
  packages: read
jobs:
  build:
    uses: purview-dev/build/.github/workflows/purview-build.yml@v0.1.0
    with:
      build-version: 0.1.0
    secrets: inherit
```

The reusable workflow authenticates to the internal Purview-Dev NuGet registry, installs the exact CLI version, and runs it. The consuming repository only adds `purview-build.json`; it does not need a copied pipeline project, package-source credentials, or setup steps.

For local use, authenticate once to the organization feed with a classic PAT carrying `read:packages`, then install a released version into the repository:

```shell
dotnet new tool-manifest
dotnet tool install Purview.Build --version 0.1.0 --add-source https://nuget.pkg.github.com/purview-dev/index.json
dotnet tool restore
dotnet purview-build
```

Add `purview-build.json` at the repository root:

```json
{
  "Build": {
    "Solution": "src/MyProduct.slnx",
    "TestRoot": "src/tests",
    "TestPatterns": ["*Tests.csproj"],
    "TestFilter": "/*/*/*/*[Category=Unit]",
    "PackTarget": "src/MyProduct.slnx"
  },
  "Release": { "Mode": "None" }
}
```

Configuration precedence is command line, environment variables, `purview-build.json`, then defaults. Nested environment keys use `__`, for example `Release__Mode=NuGet`. Secrets should only be supplied through `NUGET_API_KEY` and `GITHUB_TOKEN`.

## Pipeline

The tool runs these Modular Pipelines modules and dependencies:

```text
Version ───────────────┐
Restore → Build → Test ├→ Pack → Publish → GitHub release
           └→ Lint     │
Version ───────────────┘
```

`Version` reads the SemVer `version` field from `package.json` by default. Lint restores the repository's local tools and runs CSharpier. Tests are discovered rather than hard-coded. Pack, publication, and GitHub release steps are independently controlled by configuration and release mode.

See [architecture and configuration](docs/architecture.md), [release strategy](docs/releasing.md), and the [aspire-resourcekit migration](docs/migrations/aspire-resourcekit.md).

## Repository CI/CD

This repository gates every pull request with locked restore, warnings-as-errors compilation, package creation, installation of the generated package, and an end-to-end CLI smoke run. A successful CI run on `main` triggers CD.

CD reads the project `Version`, publishes that immutable package to `https://nuget.pkg.github.com/purview-dev/index.json`, then creates the matching `v{Version}` tag and GitHub release. Maintainers bump the project version and merge; they do not create release tags manually.

After the first publication, an organization owner must set `Purview.Build` to **Internal** under Purview-Dev → Packages → Purview.Build → Package settings. GitHub initially creates NuGet packages as private. Also enable internal package creation under the organization's package settings if it is disabled.
