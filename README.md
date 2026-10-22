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
dotnet new tool-manifest   # only if the repository has no .config/dotnet-tools.json
dotnet tool install Purview.Build
dotnet purview-build
```

`Purview.Build` is an ordinary .NET local tool: the manifest pins the version and `dotnet tool restore`
installs it. Pass `--version` to pin explicitly; omit it to take the latest stable release.

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

`purview-build.json` is validated against a JSON Schema as it loads, so an unknown key, a wrong type or an invalid enum value fails the run instead of being silently ignored. Add `"$schema": "https://raw.githubusercontent.com/purview-dev/build/main/purview-build.schema.json"` to the file for editor completion and validation; see [validation](docs/wiki/Configuration-Reference.md#validation).

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

Secrets must not be committed. They are supplied through `NUGET_APIKEY` (or `NuGet__APIKey`), `GITHUB_TOKEN`, and `LOCAL_NUGET_FEED_PATH` (or `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH`). Before any module runs, the tool checks the dependencies of the configured capabilities — the solution and vendored CSharpier paths, the local feed path, NuGet credentials or Trusted Publishing, and the GitHub token/repository id — and fails immediately, reporting every missing requirement at once.

Release eligibility is rule-based and selected per repository with `Release:Eligibility:Policy`: `ReleaseOnMain` (the default, reproducing the pipeline's long-standing behaviour), `TrunkReservesMinor` (per-line release branches), or `FourPartServicing`. Run `purview-build release-explain` to see the decision, the rule trail and the publication that would result, without running anything. See [release models](docs/wiki/Release-Models.md).

See the [Documentation](#documentation) section below for the architecture, configuration reference, and release strategy.

## GitHub Packages

Consumption and publication are deliberately different defaults: every repository using the shared
workflows can **restore** from the organisation's GitHub Packages feed automatically, while publishing
to it is **off** until a repository opts in.

| | Restoring from GitHub Packages | Publishing to GitHub Packages |
| --- | --- | --- |
| Default | automatic | off |
| Configured by | the shared action/workflow (nothing in the repository) | the repository variable `PURVIEW_PUBLISH_GITHUB_PACKAGES=true` |
| Local development | unchanged — no source is added locally | unchanged — opt in explicitly if you want it |
| `purview-build.json` | not read | not read; the tool's `NuGet:GitHubPackages` settings exist for command-line use |

### Restoring from the organisation feed

The shared composite action and both reusable workflows register, for the duration of the job:

```text
https://api.nuget.org/v3/index.json
https://nuget.pkg.github.com/<repository owner>/index.json
```

They write the source into the runner's **user-level** NuGet configuration (never the repository's
`nuget.config`), authenticate it with `GITHUB_TOKEN` and a `%GITHUB_TOKEN%` credential macro so no
token is written to disk, and remove the source again at the end of the job. `packages: read` is
already declared by both reusable workflows. A repository that depends only on NuGet.org packages
keeps restoring normally: both sources stay enabled, and NuGet picks the highest version that
satisfies the reference.

No repository configuration is required. A repository whose own `nuget.config` contains `<clear />`
hides lower-priority sources; the setup step emits a warning naming that file.

### Making every repository able to read everything

GitHub's per-package **Manage Actions access** grant is the only way `GITHUB_TOKEN` can read another
repository's package. On a Team plan, **Internal visibility does not remove that step**: Internal gives
every organisation member read access as a user, but only an Enterprise-owned organisation extends that
to all workflows. If you would rather not add every consuming repository to every package:

1. Create one classic PAT with **`read:packages`** (owned by an organisation owner) and store it as the
   organisation secret **`PURVIEW_PACKAGES_READ_TOKEN`**:

   ```shell
   gh secret set PURVIEW_PACKAGES_READ_TOKEN --org purview-dev --visibility all \
     --body "<classic PAT with read:packages>"
   ```

   `--visibility all` makes it available to every repository; a selected set of repositories works too.

2. That is all. Visibility is independent: GitHub creates a package **Private** on first publication,
   and the read token can read private packages too. If people should also see the package in the
   organisation UI, change that package to **Internal** afterwards (Package settings → Danger Zone →
   Change visibility). The organisation's **Package Creation** checkboxes only govern which visibility
   types members may *create*; they do not change what a published package inherits, and Private often
   cannot be unchecked because the default creation type must remain allowed.

The shared workflows use that secret for the feed when it exists and fall back to `GITHUB_TOKEN`
otherwise. Publication still uses `GITHUB_TOKEN` with `packages: write`; the read token never
publishes, is never written to a configuration file, and is only exported for the duration of the job.
Callers of the reusable workflows already use `secrets: inherit`; composite-action users pass it as
`packages-read-token`.

If you need a package to resolve **only** from GitHub Packages (for example to consume a version
before NuGet.org has indexed it), set the repository variable:

```text
PURVIEW_NUGET_SOURCE_MAPPING=Purview.ValueObjects
```

Semicolon-separate several patterns. This enables NuGet package source mapping for exactly those
patterns and leaves `*` on NuGet.org, and clears the HTTP cache so a just-published version is visible.
Do not map a prefix whose packages are not all mirrored yet; unmapped packages continue to come from
NuGet.org.

### Opting a repository in to publishing

Publication is controlled only by a repository-level GitHub Actions **variable**, evaluated by the
shared release workflow. Nothing in `.config/purview-build.json`, `package.json`, the `.csproj` files
or the repository's `nuget.config` needs to change.

1. **Let the release workflow write packages.** A reusable workflow can never grant more than its
   caller. In the repository's `.github/workflows/release.yml`:

   ```yaml
   permissions:
     contents: write
     packages: write
   ```

   (The job also needs `contents: write` to create the tag and GitHub release.)

2. **Set the variable.** Repository → **Settings** → **Secrets and variables** → **Actions** →
   **Variables** → **New repository variable**:

   ```text
   PURVIEW_PUBLISH_GITHUB_PACKAGES = true
   ```

   Or with the GitHub CLI (requires repository admin):

   ```shell
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/build-sdk
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/zodsharp
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/value-objects
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/build
   ```

   Those four are the repositories expected to adopt the mirror first. They are **not** an allowlist:
   the variable is the only switch, so any Purview repository can opt in the same way without a
   change to the central pipeline.

   Absent or `false` means NuGet.org only — that is the default, and it is what every repository has
   today. An ordinary PR build never reads the variable. To stop mirroring later, set the variable to
   `false` (or delete it); existing versions on the mirror are left alone until the next release's
   cleanup, and no NuGet.org version is affected.

3. **Consumers need no access grant when the org read token is configured.** The repository that
   publishes a package is its admin. Without `PURVIEW_PACKAGES_READ_TOKEN`, every other repository that
   must restore it needs read access: open the package under
   `https://github.com/orgs/purview-dev/packages/nuget/<package>`, then **Package settings** →
   **Manage Actions access** → **Add repository** (role **Read**). With the token, skip this step. Do
   not disable "Inherit access from source repository": the publishing repository needs its inherited
   admin permission.

4. **Release as usual.** Bump `package.json` and merge. An eligible release pushes the same immutable
   package files to both feeds. No new workflow input is involved.

Optionally, for human visibility, change each published package to **Internal** (Package settings →
Danger Zone). It is a per-package UI action with no REST endpoint, and it is not needed for workflow
access.

Other repositories can opt in later with only steps 1–2; nothing in Purview.Build is repository-specific.

### What a dual publish does, and what it does not

- It packs **once** and pushes the same artifacts, so the package ID and version are identical.
- NuGet.org publication is unchanged, including Trusted Publishing and `--skip-duplicate`.
- GitHub Packages gets the `.nupkg` only; it has no symbol server, so the `.snupkg` remains a
  NuGet.org artifact.
- The two pushes run independently after validation. If either registry fails, the run fails and the
  tag and GitHub release are not created, so a rerun completes the missing side (already-published
  versions are skipped).
- A duplicate version is reported as already present and is not an error.
- `Release:DryRun` logs both destinations and pushes nothing.
- After a successful release, the mirror is pruned to versions published in the last 24 hours
  (`PURVIEW_GITHUB_PACKAGES_RETENTION_HOURS`, default `24`; `0` prunes by count instead), always keeping
  the newest `PURVIEW_GITHUB_PACKAGES_KEEP_VERSIONS` (default `1`) and never deleting the version just
  released. NuGet.org keeps every version, so this window exists purely for build acceleration and
  cleanup never fails a release.
- Set the variable only **after** a Purview.Build release containing this feature has shipped: an older
  tool ignores the unknown setting and would publish to NuGet.org only.

## Pipeline

```text
CleanArtifacts → { Version, Restore → Build → Test, Restore → Lint } → Pack → Validate → Publish ─┬→ NuGet.org
                    └→ ReportEligibility                                                          └→ GitHub Packages (opt-in)
                                                                                      → GitHub release
```

`Lint` and `ReportEligibility` are gates and reports, not inputs: nothing depends on either.

`CleanArtifacts` deletes and recreates `Build:ArtifactsFolder` before anything else runs, so pack, validation, publishing, and release uploads only ever see the packages from the current run (set `Build:CleanArtifacts=false` to keep existing artifacts). `Version` resolves the release units from `Version:Source` (default: the `version` field of the root `package.json`); `Version:Strictness` defaults to `NuGet`, which accepts four-part versions. `ReportEligibility` evaluates the release rules and reports the verdict without acting on it. Lint restores local tools and runs CSharpier (`Build:CSharpierPath` runs a vendored CSharpier assembly instead, for an unreleased build). Tests are discovered under `Build:TestRoot`/`Build:TestPatterns` and run with a TUnit tree-node filter (or an xUnit filter). Pack validation inspects each `.nupkg`/`.snupkg` against required/forbidden content rules (glob patterns) and can enforce source link, deterministic builds, and compiler flags on the packaged assemblies. Analyzer-only packages can embed portable PDBs under `analyzers/dotnet/` without requiring a `.snupkg`. Publication and GitHub release steps are controlled by `Release:Mode` (`None`, `LocalNuGet`, `NuGet`, `GitHubRelease`) — a preset over the independent `Release:Publish` and `Release:GitHubRelease` switches — and by the `Build__Run*` switches. `Release:DryRun` runs everything and skips both publish steps. `LocalNuGet` is only honoured when the tool runs locally; it is ignored in CI (for example via a reusable workflow).

## Repository CI/CD

This repository dogfoods the shared tool: CI builds and packs the tool from source, installs the generated package, then runs `purview-build` against this repository so the project builds and packs itself. Restore and warnings-as-errors compilation gate every pull request and merge.

On a push to `main`, the release workflow rebuilds and reinstalls the tool from the current source, asks it to evaluate release eligibility (`release-explain --format=json`), and — when the verdict is `Release` — runs it with `Release__Mode=NuGet`, `NuGet__FeedUrl` pointing at nuget.org, and `Release__UploadArtifacts=true`. The tool therefore publishes the immutable package to `https://api.nuget.org/v3/index.json` and tags and releases itself (`v{Version}` + generated-notes GitHub release with the package attached) — exactly like every other purview-dev repository. Maintainers bump the `package.json` version and merge; they do not create release tags manually.

GitHub creates NuGet packages as **private** on first publication. Workflow access does not depend on visibility: the organisation-level `PURVIEW_PACKAGES_READ_TOKEN` reads private packages from every repository. For people, change a package to **Internal** in its **Package settings** → **Danger Zone**; that is a per-package UI action, not an API. See [docs/wiki/Release-Flow.md](docs/wiki/Release-Flow.md#visibility).

## Documentation

- [Homepage](https://purview.dev/projects/build/)
- [Documentation](https://purview.dev/docs/build/)
- [Architecture](docs/wiki/Architecture.md)
- [Configuration reference](docs/wiki/Configuration-Reference.md)
- [Release flow](docs/wiki/Release-Flow.md)
- [Release models](docs/wiki/Release-Models.md)
