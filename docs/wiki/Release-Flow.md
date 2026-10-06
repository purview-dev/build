# Versioning and Release Flow

`Purview.Build` follows SemVer. The package version is the compatibility contract for configuration keys, defaults, module ordering, and tool behavior.

- Patch: fixes that preserve configuration and pipeline behavior.
- Minor: additive options or modules with backward-compatible defaults.
- Major: renamed/removed keys, changed defaults with material effects, or a required runtime upgrade.

The reusable workflows and composite action are referenced with an `@ref` suffix (required for cross-repository references), which selects the workflow/action *code*: `@main` always runs the latest code, while a release tag (e.g. `@v0.2.0`) pins it for reproducibility. `build-version` is an independent axis that selects the installed `Purview.Build` *tool*; it defaults to the latest stable release from nuget.org, and consumers that need reproducibility pin an exact version via the `build-version` input (and, for local use, `.config/dotnet-tools.json`). Automated dependency updates should open a pull request, where the consumer's normal build validates the new tool before merge. Keep the previous major supported while migrations are in progress.

The version is declared by the `version` field in the repository's root `package.json`. Releasing consists of bumping that field and merging the validated pull request into the release head.

## Branch models

Each repository is gated by a pull-request build. Three release trigger models are supported; the consuming repository's tiny caller workflow chooses. See [Release Models](Release-Models.md) for diagrams.

| Model | Trigger | Policy | Who uses it |
| --- | --- | --- | --- |
| **A** — release on `main` | `push: branches: [main]` | `ReleaseOnMain` (default) | Every repository today |
| **B** — main-as-head / release branch | `push: branches: [release]` | `ReleaseOnMain` (default) | Documented; no repository uses it yet |
| **C** — per-line release branches | `push: branches: ['release/**']` + `workflow_dispatch` | `TrunkReservesMinor` | New |

- **Model A**: development merges to `main`, and each merge may release.
- **Model B**: development merges to `main`, and merging `main` into a `release` branch performs the release.
- **Model C**: merging to `main` ships **nothing**. Work accumulates across many branches, and a release is cut by merging into (or dispatching from) a `release/<line>` branch. A serviced stable line (`2.0.1` → `2.0.2` from `release/2.0`) coexists with an in-flight prerelease line (`2.1.0-prerelease.N` from `release/2.1`); the two release concurrently without interfering, because the monotonic-version rule is scoped to the line.

Adopting Model C is a change to the caller workflow's `on:` block plus one setting:

```yaml
on:
  push:
    branches: ['release/**']
  workflow_dispatch:
```

```json
{ "Release": { "Eligibility": { "Policy": "TrunkReservesMinor" } } }
```

In every model the reusable `purview-release.yml` workflow asks the tool to evaluate eligibility (`purview-build release-explain --format=json`), reads the verdict, and sets `Release__Mode` accordingly:

- `Release` → run the pipeline with the caller's mode.
- `Skip` → the version is already released; the job finishes green and publishes nothing.
- `Fail` → a policy violation; the job fails with the rule's message.

Because publication is idempotent (`--skip-duplicate`) and publication precedes tagging, re-running after a failed release remains safe.

### The minor-reservation rule

Model C's `TrunkReservesMinor` policy enables `REL004`, which requires a non-zero PATCH component to originate from a ref matching `ServicingRefs`. The effect is that trunk only ever cuts `x.y.0`: once a line exists, `x.y.1` onwards is serviced from that line's own `release/x.y` branch. That is what makes a stable line serviceable while the next minor is still in prerelease.

`REL005` then requires the version to advance past the highest existing version **on the same line**, so `release/2.1` shipping `2.1.0-prerelease.4` never blocks `release/2.0` shipping `2.0.3`.

### Why hand-pushed tags stay forbidden

The tool owns tagging. `CreateGitHubReleaseModule` creates `v{version}`, and the tag must not already exist — which is also how `REL002` decides that a version is already released. A hand-pushed tag therefore makes the next legitimate release of that version look already-done and silently skip, and it decouples the tag from the publication that should accompany it. Model C does not change this: its decision point is a dispatch or a merge into the release head, never a hand-pushed tag.

Releasing is still: bump `version` in `package.json`, and merge.

### Rule reference

See [Configuration Reference](Configuration-Reference.md#releaseeligibility) for the full `REL0nn` table, the built-in policies, and the `Inherits` / `+REL0nn` / `-REL0nn` delta syntax. Rule IDs are permanent: never renumbered, never repurposed. A future release model should be expressible as a new named policy plus at most one new rule.

The reusable workflow does **not** define a concurrency group. GitHub Actions cancels a run as a deadlock when a caller workflow and the reusable workflow it calls share the same concurrency group (the caller's `purview-release-main` collided with the reusable workflow's `purview-release-${{ inputs.release-branch }}` resolving to the same value, producing *"Canceling since a deadlock was detected for concurrency group"*). Callers must own release serialization by defining their own `concurrency` block:

```yaml
concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false
```

The `release-branch` input is retained for backward compatibility only.

## This repository's CI/CD

This repository dogfoods the shared tool. CI performs restore, warnings-as-errors compilation, packing, installation from the generated package, then runs `purview-build` against this repository so the project builds and packs itself. See [Repository CI/CD](Repository-CI-CD.md).

On a push to `main`, the release workflow reads and validates the `package.json` version, skips when `v{version}` already exists, then builds and installs the tool from the current source and runs it with `Release__Mode=NuGet`, `NuGet__FeedUrl` set to nuget.org, and `Release__UploadArtifacts=true`. The tool performs the release build/pack steps, publishes the immutable package to `https://api.nuget.org/v3/index.json` using the `NUGET_APIKEY` secret, and creates `v{version}` plus a generated-notes GitHub release with the package attached — tagging itself exactly like every other purview-dev repository. The tool therefore owns tagging; maintainers must not push release tags manually.

## GitHub package visibility

GitHub creates NuGet packages as private on first publication. To make sure every package is **Internal** (visible to all Purview-Dev members), set both:

1. **Organization default (prevents future private packages)** — org owner:
   GitHub → purview-dev → Settings → Packages → **Package Creation** → select **Internal**.
   New NuGet packages published by organization members then default to Internal.
2. **Existing packages already published while private** — org owner, per package:
   `https://github.com/orgs/purview-dev/packages/nuget/package/<name>` → **Package settings** → **Danger Zone** → **Change visibility** → **Internal**.

   Or via the CLI/API for every package on the registry:

   ```shell
   gh api --method PATCH "/orgs/purview-dev/packages/nuget/Purview.Build" -f visibility=internal
   ```

   Public packages cannot be made private again; private → internal is safe.

NuGet versions are immutable; `--skip-duplicate` makes recovery safe if publication succeeded but tagging was interrupted.

## For local validation

```shell
dotnet pack src/src/Build/Build.csproj -c Release -o artifacts -p:Version=$(bun -p "require('./package.json').version") -p:PackageVersion=$(bun -p "require('./package.json').version")
dotnet tool install Purview.Build --tool-path ./.tools --add-source ./artifacts
./.tools/purview-build
```

To publish packages built by a consumer to a local feed for development:

```shell
LOCAL_NUGET_FEED_PATH=p:/_sync-projects/.local-nuget/ ./.tools/purview-build --Release:Mode=LocalNuGet
```

## See also

- [Getting Started](Getting-Started.md)
- [Repository CI/CD](Repository-CI-CD.md)