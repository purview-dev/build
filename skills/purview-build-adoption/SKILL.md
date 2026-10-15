---
name: purview-build-adoption
description: >-
  Use when wiring Purview.Build into a repository - choosing the reusable workflow or the composite
  action, pinning the workflow ref versus the tool version, running the pipeline locally as a dotnet
  tool, and what a consuming repository has to add before anything will run.
metadata:
  product: Purview.Build
  repository: purview-dev/build
---

# Adopting Purview.Build

`Purview.Build` is a pipeline tool consumed from a repository that owns its own configuration. The
pipeline code lives in `purview-dev/build`; a consumer references a reusable workflow or the
composite action — or runs the tool locally — and configures it with `.config/purview-build.json`.

A consuming repository needs exactly three things: a `purview-build.json`, a `version` field in the
root `package.json`, and a workflow that calls one of the entry points below. No copied pipeline
project, no package-source credentials.

## Two independent version axes

Get this wrong and you run new tool code through old workflow inputs.

| Axis | What it selects | How |
| --- | --- | --- |
| `@ref` on the workflow or action reference | the **workflow/action code** | `@main` for latest, a release tag (`@v0.2.0`) for reproducibility. There is no `@latest`, and the `@ref` is **required** for a cross-repository reference. |
| the `build-version` input | the installed **tool** | omit it for the latest stable from nuget.org; pin an exact version (`build-version: 0.2.0`) for reproducibility. |

Pin both, or neither. Pinning one and floating the other is the mismatch.

## Reusable workflow — the usual choice

```yaml
# .github/workflows/pr.yml
name: PR
on:
  pull_request:
    branches: [main]
jobs:
  build:
    uses: purview-dev/build/.github/workflows/purview-build.yml@main
    secrets: inherit
```

```yaml
# .github/workflows/release.yml
name: Release
on:
  push:
    branches: [main]
concurrency:
  # Callers own release serialisation; the reusable workflow defines no concurrency group.
  group: release-${{ github.ref }}
  cancel-in-progress: false
jobs:
  release:
    uses: purview-dev/build/.github/workflows/purview-release.yml@main
    with:
      release-mode: NuGet
    secrets: inherit
```

The release caller's **trigger** is what picks your release model:

| Model | Trigger | Policy |
| --- | --- | --- |
| A — release on `main` | `push: branches: [main]` | `ReleaseOnMain` (the default) |
| B — main-as-head, release branch | `push: branches: [release]` | `ReleaseOnMain` |
| C — per-line release branches | `push: branches: ['release/**']` plus `workflow_dispatch` | `TrunkReservesMinor` |

```yaml
jobs:
  release:
    uses: purview-dev/build/.github/workflows/purview-release.yml@main
    with:
      release-mode: NuGet
      eligibility-policy: TrunkReservesMinor
    secrets: inherit
```

The reusable release workflow asks the tool to evaluate eligibility and reads the verdict: an
already-released version **skips** and the job finishes green; a policy violation **fails** the job
naming the rule. Merging `main` into `release` therefore releases exactly once.

## Composite action — when you need surrounding steps

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

`build-version` is optional here too.

## Locally

```shell
dotnet new tool-manifest       # only if there is no .config/dotnet-tools.json
dotnet tool install Purview.Build
dotnet purview-build
```

It is an ordinary .NET local tool: the manifest pins the version, and `dotnet tool restore` installs
it. Most repositories wrap it in `just` recipes:

```just
pipeline-pr:            dotnet purview-build
pipeline-pack-validate: dotnet purview-build --Build:RunPack=true --Build:ValidatePack=true --Release:Mode=None
```

Before running anything destructive, two commands tell you what *would* happen:

```shell
dotnet purview-build --help              # usage, probe order, and the resolved config path
dotnet purview-build release-explain     # the release decision, without running a module
```

## Releasing a consuming repository

Bump the `version` field in the root `package.json`, and merge the validated pull request into the
release head. **The pipeline owns tagging (`v{version}`) and the GitHub release** — never push a
release tag by hand.

## A setting that will not take effect

Precedence is **command line > environment variables > `purview-build.json` > defaults**. Because
environment variables outrank the JSON file, an *empty* forwarded variable silently erases a
configured value. That is why the reusable workflows forward every optional input only when the
caller actually provided it, and why `WorkflowParityTests` asserts that no optional input is mapped
straight into `env:` without an `if [ -n … ]` guard.

If a `purview-build.json` setting appears to be ignored, check for an environment variable of the
same name first.

## Related skills

`purview-build-configuration` for the full `purview-build.json` reference and the secrets,
`purview-build-pipeline` for the modules and their order, `purview-build-pack-validation` for the
package rules, and `purview-build-release-eligibility` for the `REL0nn` rules and the three models.
