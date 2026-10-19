---
name: purview-build-pipeline
description: >-
  Use when reasoning about what a Purview.Build run does or why a module was skipped - the module
  graph and its dependency edges, each module's behaviour and skip condition, the DotNet and Web
  paths, and how the resolved version flows from VersionModule to pack and release.
metadata:
  product: Purview.Build
  repository: purview-dev/build
---

# The pipeline modules

A Modular Pipelines orchestration. Modules are registered in `BuildPipeline.cs`; `Program.cs` owns
the CLI — informational options, the version banner, failure reporting. Explicit `[DependsOn]` edges
define ordering, and `ModuleConfiguration` skip conditions gate opt-in behaviour. Categories are
`Build` and `Release`.

```text
CleanArtifacts → { Version, Restore → Build → Test, Restore → Lint } → Pack → ValidatePack → Publish → GitHub release
                   └→ ReportEligibility
```

**`Lint` and `ReportEligibility` are gates and reports, not inputs.** Nothing depends on either, so
neither feeds `Pack`.

Every module logs a line when it starts (`Running BuildModule...`) before its command output, so a
long run still reports progress in CI logs where the live progress display is disabled.

## CleanArtifacts

Deletes `Build:ArtifactsFolder` when it exists and recreates it empty, **before any other module
runs**. `VersionModule` and `RestoreModule` depend on it, so the reset completes first.

The folder is shared output — pack writes it, validation inspects every package in it, publishing
moves packages out of it, the release step can upload its contents. A leftover package from an
earlier or differently-configured run would otherwise be validated, published or uploaded as if it
belonged to this run.

*Skipped when* `Build:CleanArtifacts` is false, **or** `Build:RunPack` is false — nothing will be
packed, so an existing folder (one being inspected ahead of a manual publish, say) is left alone.

## Version

Resolves the release units **once**, through `Version:Source`, and exposes them as an immutable
module result. Every downstream module reads that result; **none recomputes a version.**

The result is an ordered **set**, not a scalar, even though every current source yields exactly one.
Each unit carries its id, version, prerelease flag and label, line (`MAJOR.MINOR`), channel, tag
(`v{version}`) and source. Modelling it as a set means adding a second unit needs no change in
`PackModule` or `CreateGitHubReleaseModule` — both enumerate.

`PackageJson` reads the `version` field of the repository-root `package.json` and validates it
against `Version:Strictness`. It fails when the file is missing, the field is missing or empty, or
the value does not satisfy the strictness.

The tag preserves the version **exactly as written**: a normalised tag would drop a trailing `.0`
revision and desynchronise from the declared version.

## ReportEligibility

Category `Build`. Depends on `Version`. No skip condition.

Evaluates the `Release:Eligibility` rules and **reports** the verdict — the policy name, every
rule's outcome and message in evaluation order, and the short-circuit point — to the log and run
summary. **It never acts on the verdict.**

It is deliberately *not* category `Release`: it observes, and must never decline a release. The
workflow owns the decision (it runs `release-explain`, reads the verdict, sets `Release__Mode`).
This module exists so a real run still records *why* a release was or was not eligible — which a
workflow-only gate never surfaced in the tool's own log.

Every failure path inside it is a **warning**. A misconfigured policy or an unreadable simulated tag
list must not fail a build that would otherwise succeed. When `Release:Context:*` supplied a
simulated context, the report says so.

## Restore

`Build:WebInstallCommand` (default `bun install`) when `Build:ProjectType` is `Web`; otherwise
`dotnet restore` against `Build:Solution`.

## Build

Depends on `Restore`.

- **DotNet** — `dotnet build` against `Build:Solution`, with `Build:Configuration` and
  `--no-restore`.
- **Web** — when `WebBuildCommand` is left at its default **and** the repository declares a
  `data:sync` script, it runs `bun run data:sync` first, so a fresh checkout can build offline.
  Overriding `WebBuildCommand` takes full control, and the automatic data sync is then skipped.

## Lint

Depends on `Restore` — Web lint invokes tooling that `bun install` put there. *Skipped when*
`Build:RunLint` is false.

- **DotNet** — `dotnet tool restore` (retried up to 3 times with a 2-second backoff), then
  `dotnet tool run csharpier check <repository root>`. The root is resolved by walking up to the
  nearest `package.json`. When `Build:CSharpierPath` is set, the manifest tool is bypassed: the module
  runs `dotnet <path> check <repository root>` against the vendored assembly and restores nothing.
- **Web** — `WebFormatCheckCommand` then `WebLintCommand`, each skipped when the corresponding
  script is not declared in the root `package.json`.

## RunTests

Depends on `Build`. *Skipped when* `Build:RunTests` is false.

- **DotNet** — enumerates `*.csproj` recursively under `Build:TestRoot`, matching names against
  `Build:TestPatterns` (comma-separated, case-insensitive globs), then narrows to `Build:TestProjects`
  (default `*`). No matches logs a **warning** and returns empty. Each project runs with
  `dotnet test --no-build --no-restore`, in parallel.
  - **TUnit** (default) — passes `--ignore-exit-code 8` (Microsoft.Testing.Platform exits 8 when no
    tests are selected; treated as success) and `--treenode-filter <filter>` when `TestFilter` is
    non-empty.
  - **xUnit** — `--filter <filter>` when non-empty.
- **Web** — `WebTestCommand` from the repository root.

Per-project timings are logged, ordered by elapsed time.

## Pack

Depends on `RunTests` and `Version`. *Skipped when* `Build:RunPack` is false.

Packs **once per release unit**, so a multi-unit version source needs no change here.

- **DotNet** — `dotnet pack` with `--output <ArtifactsFolder>` and
  `-p:PackageVersion=<version> -p:Version=<version>` from the resolved release unit.
- **Web** — zips `Build:WebBuildOutput` into `<package-name>-<version>.zip`. Logs a warning and
  produces nothing when the build output directory does not exist.

## ValidatePack

Depends on `Pack`. *Skipped when* `Build:ValidatePack` is false **or** `ProjectType` is `Web` (no
`.nupkg` to inspect).

Inspects every `.nupkg`/`.snupkg` in `ArtifactsFolder` and fails the run if any package has errors.
Because `CleanArtifacts` cleared the folder, those are exactly the packages this run packed.

## PublishNuGet

Category `Release`. Depends on `Pack`, `ValidatePack`, `RunTests`. *Skipped when* `ProjectType` is
`Web`, **or** the resolved `Release:Publish` is false.

Credentials are a startup concern: with publishing requested and `Release:DryRun` false, a missing
NuGet API key (when `NuGet:TrustedPublishing` is false) fails the run before any module.

Pushes every `*.nupkg` with `--skip-duplicate`, to the channel's `FeedUrl` when `Release:Channel`
declares one and `NuGet:FeedUrl` otherwise. With `TrustedPublishing`, pushes without an API key.
With `Release:DryRun`, logs the push and performs none.

## PublishLocalNuGet

Category **`Build`** — it never reaches a remote feed. Depends on `Pack` and `ValidatePack`.
*Skipped when* `ProjectType` is `Web`, **or** the tool is not running locally, **or** `Release:Mode`
is not `LocalNuGet`. The mode is intentionally ignored in CI.

The feed path is validated by the startup preflight before any module runs, so a missing or invalid
path fails immediately. It must be **absolute**; a drive-relative path such as `p:foo`, left by a
sh-style shell stripping backslashes, is rejected with a remediation message. The module then moves
the packages into the feed, and optionally clears the NuGet caches and shuts down the build server.

## CreateGitHubRelease

Category `Release`. Depends on `PublishNuGet`, `ValidatePack`, `Version`. *Skipped unless* the
resolved `Release:GitHubRelease` is true.

With a real (non-dry-run) release requested, `GITHUB_TOKEN` and `GITHUB_REPOSITORY_ID` are required
before any module runs; a missing one fails the run rather than silently skipping the release.

One release per release unit, with that unit's tag and `GenerateReleaseNotes = true`. A prerelease
**version** becomes a GitHub **prerelease** unless `Release:MarkPrerelease` — or the channel's — is
false, so a prerelease build is never presented as the latest stable. That changes GitHub metadata
only: prerelease versions are still published to the feed.

With `Release:UploadArtifacts`, every file in `ArtifactsFolder` is uploaded as an asset. With
`DryRun`, it logs and creates nothing.

**The tag must not already exist.** The workflow gates eligibility; the tool evaluates and reports
it but does not skip an existing tag itself.

## Related skills

`purview-build-configuration` for every setting named above, `purview-build-pack-validation` for
what `ValidatePack` actually checks, and `purview-build-release-eligibility` for the verdict
`ReportEligibility` prints.
