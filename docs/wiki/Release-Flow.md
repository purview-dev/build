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

On a push to `main`, the release workflow reads and validates the `package.json` version, skips when `v{version}` already exists, then builds and installs the tool from the current source and runs it with `Release__Mode=NuGet`, `NuGet__FeedUrl` set to nuget.org, and `Release__UploadArtifacts=true`. The tool performs the release build/pack steps, publishes the immutable package to `https://api.nuget.org/v3/index.json` using the `NUGET_APIKEY` secret, and creates `v{version}` plus a generated-notes GitHub release with the package attached — tagging itself exactly like every other purview-dev repository. When this repository's `PURVIEW_PUBLISH_GITHUB_PACKAGES` variable is `true`, the same packages also go to GitHub Packages, and older mirror versions are pruned afterwards. The tool therefore owns tagging; maintainers must not push release tags manually.

## GitHub Packages

GitHub Packages is a **secondary** NuGet feed. NuGet.org remains the durable archive; the
organisation feed (`https://nuget.pkg.github.com/purview-dev/index.json`) exists so a downstream
repository can consume a version before NuGet.org has indexed it, and so the organisation has a
mirror it controls.

### Publishing is opt-in

The shared release workflow reads one repository-level Actions variable:

```text
PURVIEW_PUBLISH_GITHUB_PACKAGES=true
```

| Variable | Existing NuGet release | Result |
| --- | --- | --- |
| absent | yes | NuGet.org only |
| `false` | yes | NuGet.org only |
| `true` | yes | NuGet.org and GitHub Packages |
| `true` | no (PR, `Release:Mode=None`) | nothing publishes |
| `true` | `Release:DryRun=true` | neither registry is pushed; both are logged |

To opt a repository in:

1. Grant the caller's release job the permission the reusable workflow declares. Reusable workflows
   cannot elevate a caller, so add to the repository's `.github/workflows/release.yml`:

   ```yaml
   permissions:
     contents: write
     packages: write
   ```

2. Create the repository variable (Settings → Secrets and variables → Actions → Variables):
   `PURVIEW_PUBLISH_GITHUB_PACKAGES = true`.

   ```shell
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/build
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/build-sdk
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/zodsharp
   gh variable set PURVIEW_PUBLISH_GITHUB_PACKAGES --body true --repo purview-dev/value-objects
   ```

   Those are the expected first adopters, not an allowlist. The variable is the only switch, so any
   Purview repository opts in the same way without touching the central pipeline. Set it to `false`
   (or delete it) to stop mirroring; NuGet.org publication is unaffected either way.

3. Release by bumping `package.json` and merging, exactly as before. No configuration file, project
   file, workflow input, or `nuget.config` changes.

Absent or `false` is the default and reproduces today's behaviour exactly. The PR build workflow never
reads the variable, and the setting is still gated by the release eligibility decision:
`Release:Publish` must be true for the module to run. `NuGet:GitHubPackages:Enabled` (the setting the
workflow maps the variable onto) is the command-line equivalent for a manual run.

An older Purview.Build ignores the unknown setting. Set the variable only after a release containing
this feature has shipped.

### Consuming is automatic

The shared composite action and both reusable workflows register nuget.org and the organisation feed
in the runner's **user-level** NuGet configuration (never the repository's `nuget.config`),
authenticated with `GITHUB_TOKEN` through a `%GITHUB_TOKEN%` credential macro. Both sources stay
enabled, so a package that is only on NuGet.org keeps restoring, and a version that exists only on
GitHub Packages is found while NuGet.org catches up. When NuGet has cached source metadata from before
a version was published, the cache can hide it until it expires; `PURVIEW_NUGET_SOURCE_MAPPING`
(comma/semicolon-separated package patterns) enables package source mapping for those patterns and
clears the HTTP cache, at the cost of forbidding fallback for them.

No repository opts in to consumption. A repository whose own `nuget.config` contains `<clear />`
disables lower-priority sources and hides the feed; the setup step warns with that file's path.

### Access and permissions

- Publishing needs `GITHUB_TOKEN` with `packages: write`, which the caller must grant.
- The publishing repository gets admin permission on the package it creates by inheritance. Do not
  disable "Inherit access from source repository" in the organisation's default package settings.
- A repository that restores another repository's package needs read access to it: package page →
  **Package settings** → **Manage Actions access** → **Add repository** → **Read**. This is per
  package and must be repeated for each consuming repository. There is no organisation-wide
  auto-grant for NuGet packages.
- **Internal visibility does not remove that step on a Team plan.** Internal gives every organisation
  member read access as a user; GitHub's permissions table extends it to all workflows only for an
  Enterprise-owned organisation. To avoid per-package grants entirely, provide one organisation secret,
  `PURVIEW_PACKAGES_READ_TOKEN`: a classic PAT with `read:packages`, owned by an organisation owner. The
  shared source setup prefers it when present and falls back to `GITHUB_TOKEN`. It is read-only and is
  never used for publication. Visibility is independent: GitHub creates a package private, and the token
  reads private packages too.

  ```shell
  gh secret set PURVIEW_PACKAGES_READ_TOKEN --org purview-dev --visibility all \
    --body "<classic PAT with read:packages>"
  ```
- GitHub Packages has no symbol server: `.snupkg` files go to NuGet.org only.
- The workflow fails (rather than silently publishing to one registry) when either push fails; the tag
  and GitHub release are created only after both succeed.

### Partial failures and reruns

The two publish modules run independently after pack validation. If one registry fails, the run fails
and no tag is created. Because `REL002` keys off the tag rather than feed state, the version is still
eligible on a rerun; `--skip-duplicate` makes the registry that succeeded a no-op, and the rerun
completes the missing side. A duplicate version on GitHub Packages is treated as already published,
not as an error.

### Retention

The mirror exists for build acceleration, not as an archive: it keeps versions published in the last
**24 hours** so a downstream repository can restore a version before NuGet.org has indexed it. After a
successful release, the shared workflow deletes versions of the packages linked to the publishing
repository that fall outside that window. It always keeps:

- the version just released, even if the version listing is out of order;
- the newest `PURVIEW_GITHUB_PACKAGES_KEEP_VERSIONS` versions (default `1`), so the feed is never
  emptied by a long gap between releases;
- every version published within `PURVIEW_GITHUB_PACKAGES_RETENTION_HOURS` (default `24`; set it to `1`
  for an hourly window, the practical minimum for a release job).

`PURVIEW_GITHUB_PACKAGES_RETENTION_HOURS=0` disables the age window and falls back to count-only
pruning; setting both variables to `0` disables pruning entirely. This is housekeeping on the mirror:
NuGet.org holds every version, a cleanup failure only warns, and a package the release did not publish
is skipped. Deleted versions can be restored through the REST API within 30 days if a window proves
too aggressive.

### Visibility

GitHub creates NuGet packages as **private** on first publication. The organisation's **Package
Creation** settings control *which visibility types members may use* (`Public`, `Private`,
`Internal`), not the visibility of a package that gets published — so leaving **Private** checked there
is harmless, and it often cannot be cleared because the default creation type must remain allowed.

Two facts keep this from being a problem:

- **Workflow access no longer depends on visibility.** Every repository reads the feed with the
  organisation-level `PURVIEW_PACKAGES_READ_TOKEN`, which can read private packages too.
- **Internal visibility is for people, not builds.** It makes a package visible to all organisation
  members in the UI. Change it per package after publication:
  `https://github.com/orgs/purview-dev/packages/nuget/package/<name>` → **Package settings** →
  **Danger Zone** → **Change visibility** → **Internal**.

There is **no REST or GraphQL endpoint for package visibility or Actions access**; both are UI-only.
A `PATCH` against `/orgs/{org}/packages/nuget/{name}` returns 404, so scripts cannot set visibility.
Public packages cannot be made private again; private → internal is safe.

NuGet versions are immutable; `--skip-duplicate` makes recovery safe if publication succeeded but tagging was interrupted.

## For local validation

```shell
dotnet pack src/src/Build/Build.csproj -c Release -o artifacts -p:Version=$(bun -p "require('./package.json').version") -p:PackageVersion=$(bun -p "require('./package.json').version")
dotnet tool update Purview.Build --add-source ./artifacts --version $(bun -p "require('./package.json').version")
dotnet purview-build
```

To publish packages built by a consumer to a local feed for development:

```shell
LOCAL_NUGET_FEED_PATH=p:/_sync-projects/.local-nuget/ dotnet purview-build --Release:Mode=LocalNuGet
```

## See also

- [Getting Started](Getting-Started.md)
- [Repository CI/CD](Repository-CI-CD.md)