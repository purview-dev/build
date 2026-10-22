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
project, and no package-source credentials: restoring from the organisation's GitHub Packages feed is
configured automatically by the shared entry points, while publishing to it is opt-in (see below).

## Two independent version axes

Get this wrong and you run new tool code through old workflow inputs.

| Axis | What it selects | How |
| --- | --- | --- |
| `@ref` on the workflow or action reference | the **workflow/action code** | `@main` for latest, a release tag (`@v0.2.0`) for reproducibility. There is no `@latest`, and the `@ref` is **required** for a cross-repository reference. |
| the `build-version` input | the installed **tool** | omit it for the latest stable from nuget.org or the organisation mirror (registered first); pin an exact version (`build-version: 0.2.0`) for reproducibility, including one only the mirror has. |

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

## GitHub Packages

Two different defaults, and the distinction matters:

| | Restoring from the organisation feed | Publishing to it |
| --- | --- | --- |
| Default | **automatic** | **off** |
| Where it is configured | the shared action/workflow | the repository variable `PURVIEW_PUBLISH_GITHUB_PACKAGES` |
| Files to change | none | `release.yml` permissions only |
| Local development | unchanged | unchanged unless you opt in explicitly |

Consumption needs nothing. The shared action and both reusable workflows register
`https://api.nuget.org/v3/index.json` **and**
`https://nuget.pkg.github.com/<owner>/index.json` in the runner's user-level NuGet configuration,
authenticated with `GITHUB_TOKEN` via a `%GITHUB_TOKEN%` macro, before the repository's first restore.
A package that only exists on NuGet.org keeps restoring: both sources stay enabled.

**If you want every repository to read every package without per-package grants** (Internal does not
do that on a Team plan), add one organisation secret, `PURVIEW_PACKAGES_READ_TOKEN`, a classic PAT with
`read:packages`. The shared workflows use it for the feed when present and fall back to `GITHUB_TOKEN`;
it never publishes. Callers already use `secrets: inherit`; the composite action takes it as
`packages-read-token`. Visibility is independent: new packages are private, the token reads them, and
changing a package to Internal is a per-package UI action (no REST endpoint).

Publication is additive — the same packed artifacts go to both feeds. To opt a repository in:

1. In `.github/workflows/release.yml`, grant what the reusable release workflow declares (a reusable
   workflow cannot elevate its caller):

   ```yaml
   permissions:
     contents: write
     packages: write
   ```

2. Create the repository variable (Settings → Secrets and variables → Actions → Variables):
   `PURVIEW_PUBLISH_GITHUB_PACKAGES = true`.

3. Release as usual. No `purview-build.json`, `package.json`, project file or `nuget.config` change.

Then grant consumers read access per package: package page → **Package settings** →
**Manage Actions access** → **Add repository** (Read). There is no organisation-wide auto-grant for
NuGet packages; Enterprise-owned organisations may read **Internal** packages automatically. Set the
variable only after a Purview.Build release containing the feature has shipped — an older tool ignores
the unknown setting.

Optional repository variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `PURVIEW_GITHUB_PACKAGES_KEEP_VERSIONS` | `1` | newest versions always kept by the post-release mirror cleanup; `0` removes the floor |
| `PURVIEW_GITHUB_PACKAGES_RETENTION_HOURS` | `24` | how long a mirrored version survives; `1` for an hourly window, `0` for count-only pruning |
| `PURVIEW_NUGET_SOURCE_MAPPING` | empty | semicolon-separated package patterns pinned **only** to GitHub Packages; mapping itself is always written (`*` to both by default), and setting this also clears the HTTP cache |

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
