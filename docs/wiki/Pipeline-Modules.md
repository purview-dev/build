# Pipeline Modules

The pipeline is a Modular Pipelines orchestration. Modules are registered in `BuildPipeline.cs` (`Program.cs` handles the CLI: informational options, the version banner, and failure reporting); explicit `[DependsOn]` edges define ordering, while `ModuleConfiguration` skip conditions gate opt-in behavior. Module categories are `Build` and `Release`.

Each module logs a line when it starts (`Running BuildModule...`) before its command output, so long runs report progress in CI logs where the live progress display is disabled.

```text
CleanArtifacts → { Version, Restore → Build → Test, Restore → Lint } → Pack → ValidatePack → Publish → GitHub release
                   └→ ReportEligibility
```

`Lint` and `ReportEligibility` are gates and reports, not inputs: nothing depends on either, so
neither feeds `Pack`.

## CleanArtifactsModule

Deletes `Build:ArtifactsFolder` (when it exists) and recreates it empty, before any other module runs. The folder is shared output: pack writes it, validation inspects every package in it, publishing moves packages out of it, and the release step can upload its contents — so a leftover package from an earlier (or differently configured) run would otherwise be validated, published, or uploaded as if it belonged to this run.

`VersionModule` and `RestoreModule` depend on this module, so the reset completes before any other module starts. Skip conditions: skipped when `Build:CleanArtifacts` is false, or when `Build:RunPack` is false (nothing will be packed, so existing artifacts — for example a folder being inspected ahead of a manual publish — are left untouched).

## VersionModule

Resolves the release units **once**, through the provider selected by `Version:Source` (`PackageJson` is the default and the only implemented source), and exposes them as an immutable module result. Every downstream module reads that result; none recomputes a version.

The result is an ordered **set** of release units, not a scalar, even though every current source yields exactly one. Each unit carries its id, version, prerelease flag and label, line (`MAJOR.MINOR`), channel, tag (`v{version}`) and source. Modelling it as a set means adding a second unit requires no change in `PackModule` or `CreateGitHubReleaseModule`, both of which enumerate the set.

`PackageJson` reads the `version` field from the repository root `package.json` and validates it against `Version:Strictness`. The default, `NuGet`, accepts everything `NuGetVersion` does — including four-part versions such as `13.5.3.10`, which two consuming repositories ship. `SemVer2` is the stricter opt-in. Fails when the file is missing, the field is missing/empty, or the value does not satisfy the configured strictness.

The tag preserves the version exactly as written in `package.json`, rather than its normalised form: a normalised tag would drop a trailing `.0` revision and desynchronise the tag from the declared version.

## ReportEligibilityModule

Category `Build`. Depends on `VersionModule`. No skip condition.

Evaluates the `Release:Eligibility` rules and **reports** the verdict — the policy name, every rule's outcome and message in evaluation order, and the short-circuit point — to the log and the run summary. It never acts on the verdict.

Deliberately not category `Release`: it observes, and it must never decline a release. The workflow owns the decision (it runs `release-explain`, reads the verdict and sets `Release__Mode`); this module exists so a real pipeline run still records *why* a release was or was not eligible, which a workflow-only gate never surfaced in the tool's own log.

Every failure path inside it is a warning. A misconfigured policy or an unreadable simulated tag list must not fail a build that would otherwise succeed. When `Release:Context:*` supplied a simulated context, the report says so.

## RestoreModule

Runs `Build:WebInstallCommand` (default `bun install`) when `Build:ProjectType` is `Web`, otherwise `dotnet restore` against `Build:Solution`.

## BuildModule

Depends on `RestoreModule`.

- **DotNet**: runs `dotnet build` against `Build:Solution` with `Build:Configuration` and `--no-restore`.
- **Web**: when `Build:WebBuildCommand` is left at the default (`bun run build`) and the repository declares a `data:sync` script, first runs `bun run data:sync` (so a fresh checkout can build offline), then runs the build command. Overriding `WebBuildCommand` takes full control of the build (for example a chain of validation scripts); the automatic data sync is then skipped.

## LintModule

Depends on `RestoreModule` (Web lint invokes tooling installed by the restore step, so it must wait for `bun install`; dotnet lint is unaffected beyond running after restore). Skip condition: skipped when `Build:RunLint` is false.

- **DotNet**: restores the repository's local tools (`dotnet tool restore` against `.config/dotnet-tools.json`, retried up to 3 times with a 2-second backoff on failure) and then runs `dotnet tool run csharpier check <repository root>`. The repository root is resolved by walking up to the nearest `package.json`.
- **Web**: runs `Build:WebFormatCheckCommand` (default `bun run format:check`) then `Build:WebLintCommand` (default `bun run lint`). Each step is skipped when the corresponding script is not declared in the root `package.json`.

## RunTestsModule

Depends on `BuildModule`. Skip condition: skipped when `Build:RunTests` is false.

- **DotNet**: discovers test projects by enumerating `*.csproj` recursively under `Build:TestRoot`, matching file names against `Build:TestPatterns` (comma-separated glob/name patterns, case-insensitive), then restricting the run list with `Build:TestProjects` (default `*`). If no projects match, it logs a warning and returns an empty result. Runs each test project with `dotnet test --no-build --no-restore` in parallel, using `Build:Configuration`:
  - **TUnit** (default): passes `--ignore-exit-code 8` (Microsoft.Testing.Platform exits with code 8 when no tests are selected; treated as success) and, when `Build:TestFilter` is non-empty, `--treenode-filter <filter>`.
  - **xUnit**: when `Build:TestFilter` is non-empty, passes `--filter <filter>`.
- **Web**: runs `Build:WebTestCommand` (default `bun run test`) from the repository root.

Per-project timings are logged, ordered by elapsed time.

## PackModule

Depends on `RunTestsModule` and `VersionModule`. Skip condition: skipped when `Build:RunPack` is false.

`CleanArtifactsModule` resets `Build:ArtifactsFolder` before the run produces anything, so the folder only contains packages from the current run.

Packs once per release unit, so a multi-unit version source needs no change here.

- **DotNet**: creates `Build:ArtifactsFolder` and runs `dotnet pack` against `Build:Solution` with `Build:Configuration`, `--output <ArtifactsFolder>`, and `-p:PackageVersion=<version> -p:Version=<version>` where the version comes from the release unit resolved by `VersionModule`.
- **Web**: creates `Build:ArtifactsFolder` and zips `Build:WebBuildOutput` (default `src/dist`) into `<package-name>-<version>.zip` (name from the root `package.json` `name` field, version from the release unit). Logs a warning and produces no artifact when the build output directory does not exist.

## ValidatePackModule

Depends on `PackModule`. Skip condition: skipped when `Build:ValidatePack` is false **or** `Build:ProjectType` is `Web` (Web projects produce no `.nupkg`).

Inspects every `.nupkg`/`.snupkg` in `Build:ArtifactsFolder`. Because `CleanArtifactsModule` cleared the folder at the start of the run, the packages inspected are exactly those the current run packed. Fails the run if any package has errors. Produces a summary of valid/invalid package counts. See [Pack Validation](Pack-Validation.md) for the full rule set.

## PublishNuGetModule

Category `Release`. Depends on `PackModule`, `ValidatePackModule`, and `RunTestsModule`. Skip condition: skipped when `Build:ProjectType` is `Web` **or** the resolved `Release:Publish` is false **or** (`NuGet:TrustedPublishing` is false and no API key resolves via `NuGet:GetNuGetAPIKey()`).

`Release:Publish` is derived from `Release:Mode` unless set explicitly, so the default skip behaviour is unchanged: it publishes when `Release:Mode=NuGet`.

Pushes every `*.nupkg` in `Build:ArtifactsFolder` with `--skip-duplicate`, to the channel's `FeedUrl` when `Release:Channel` declares one and `NuGet:FeedUrl` otherwise. When `NuGet:TrustedPublishing` is true, pushes without an API key (NuGet Trusted Publishing / OIDC federation). When `Release:DryRun` is true, logs the push it would have made and performs none.

## PublishLocalNuGetModule

Category `Build` — it never reaches a remote feed. Depends on `PackModule` and `ValidatePackModule`. Skip condition: skipped when `Build:ProjectType` is `Web` **or** the tool is not running **locally** (`ctx.IsRunningLocally()`) **or** `Release:Mode` is not `LocalNuGet`. This mode is intentionally ignored in CI.

Validates `PublishLocalNuGet:LocalFeedPath` (resolved via `GetLocalFeedPath()`, falling back to `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH` and then process env `LOCAL_NUGET_FEED_PATH`). The path must be absolute; drive-relative paths such as `p:foo` (backslashes stripped by a sh-style shell) are rejected with a remediation message. See [Local Development](Local-Development.md).

Moves the `.nupkg`/`.snupkg` files from `Build:ArtifactsFolder` into the local feed (skipping existing files unless `OverwriteExistingPackages` is true), optionally clears the NuGet global-packages and HTTP caches for the published packages (`ClearPackageCache`), and optionally shuts down the dotnet build server (`ShutdownDotnetBuilderServer`).

## CreateGitHubReleaseModule

Category `Release`. Depends on `PublishNuGetModule`, `ValidatePackModule`, and `VersionModule`. Skip condition: skipped unless the resolved `Release:GitHubRelease` is true **and** a GitHub token resolves via `GitHub:GetGitHubToken()`.

`Release:GitHubRelease` is derived from the channel and then from `Release:Mode` unless set explicitly, so the default skip behaviour is unchanged: it releases when `Release:Mode` is `NuGet` or `GitHubRelease`.

Creates one GitHub release per release unit, with that unit's tag and `GenerateReleaseNotes = true`. Releases whose version is a prerelease (for example `2.0.0-prerelease.25`) are created as GitHub prereleases unless `Release:MarkPrerelease` — or the channel's `MarkPrerelease` — is false, so prerelease builds are not presented as the latest stable release. This changes GitHub release metadata only: prerelease versions are still published to the NuGet feed — `PublishNuGetModule` is unaffected. When `Release:UploadArtifacts` is true, uploads every file in `Build:ArtifactsFolder` as a release asset — for Web projects this is the `<package-name>-<version>.zip` produced by `PackModule`. When `Release:DryRun` is true, logs the release it would have created and creates none.

The tag must not already exist. The workflow gates release eligibility — the tool evaluates and reports it (`release-explain`, `ReportEligibilityModule`) but does not skip an existing tag itself.

## See also

- [Architecture](Architecture.md)
- [Configuration Reference](Configuration-Reference.md)
- [Pack Validation](Pack-Validation.md)
- [Release Flow](Release-Flow.md)