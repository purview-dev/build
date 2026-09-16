# Getting Started

`Purview.Build` is consumed from a repository that owns its own configuration. The pipeline code lives here; consumers reference the composite action or one of the reusable workflows (or run the tool locally) and configure it with `purview-build.json`.

## Two independent version axes

- The `@ref` suffix on a reusable-workflow or composite-action reference selects the workflow/action **code**: `@main` always runs the latest code, while a release tag (e.g. `@v0.2.0`) pins it for reproducibility. There is no `@latest`; the `@ref` is required for cross-repository references.
- The `build-version` input selects the installed `Purview.Build` **tool**. Omit it to always install the latest stable tool from nuget.org, or pin an exact version (e.g. `build-version: 0.2.0`) for reproducibility.

Mixing a pinned old `@ref` with a floating `build-version` runs newer tool code through older workflow inputs.

## Minimal repository setup (reusable workflow)

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
  # Serialize releases; callers own concurrency (see Release Flow).
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

The reusable release workflow checks whether `v{version}` (read from `package.json`) is already tagged and skips if so, so merging `main` into `release` releases exactly once.

The reusable workflows install the pinned CLI version (or the latest stable when `build-version` is omitted) from nuget.org; the consuming repository adds `purview-build.json` and a root `package.json` version. It does not need a copied pipeline project or package-source credentials.

## Minimal repository setup (composite action)

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

## Local use

```shell
dotnet tool install Purview.Build --tool-path ./.tools
./.tools/purview-build
```

Omit `--version` to install the latest stable release.

## Release a consuming repository

Releasing consists of bumping the `version` field in the repository's root `package.json` and merging the validated pull request into the release head. The pipeline owns tagging (`v{version}`) and the GitHub release; maintainers must not push release tags manually. See [Release Flow](Release-Flow.md).

## See also

- [Architecture](Architecture.md)
- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Secrets and Environment Variables](Secrets-and-Environment-Variables.md)