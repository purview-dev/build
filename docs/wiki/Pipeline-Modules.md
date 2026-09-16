# Pipeline Modules

The pipeline is a Modular Pipelines orchestration. Modules are registered in `Program.cs`; explicit `[DependsOn]` edges define ordering, while `ModuleConfiguration` skip conditions gate opt-in behavior. Module categories are `Build` and `Release`.

```text
Version ───────────────┐
Restore → Build → Test ├→ Pack → Validate → Publish → GitHub release
           └→ Lint     │
Version ───────────────┘
```

## VersionModule

Reads the SemVer `version` field from the repository root `package.json` and produces a `NuGetVersion`. Fails when the file is missing, the field is missing/empty, or the value is not valid SemVer. The version feeds `PackModule` (via `Version`/`PackageVersion`) and `CreateGitHubReleaseModule` (via the `v{version}` tag).

## RestoreModule

Runs `dotnet restore` against `Build:Solution`.

## BuildModule

Depends on `RestoreModule`. Runs `dotnet build` against `Build:Solution` with `Build:Configuration` and `--no-restore`.

## LintModule

Skip condition: skipped when `Build:RunLint` is false.

Restores the repository's local tools (`dotnet tool restore` against `.config/dotnet-tools.json`, retried up to 3 times with a 2-second backoff on failure) and then runs `dotnet tool run csharpier check <repository root>`. The repository root is resolved by walking up to the nearest `package.json`.

## RunTestsModule

Depends on `BuildModule`. Skip condition: skipped when `Build:RunTests` is false.

Discovers test projects by enumerating `*.csproj` recursively under `Build:TestRoot`, matching file names against `Build:TestPatterns` (comma-separated glob/name patterns, case-insensitive), then restricting the run list with `Build:TestProjects` (default `*`). If no projects match, it logs a warning and returns an empty result.

Runs each test project with `dotnet test --no-build --no-restore` in parallel, using `Build:Configuration`:

- **TUnit** (default): passes `--ignore-exit-code 8` (Microsoft.Testing.Platform exits with code 8 when no tests are selected; treated as success) and, when `Build:TestFilter` is non-empty, `--treenode-filter <filter>`.
- **xUnit**: when `Build:TestFilter` is non-empty, passes `--filter <filter>`.

Per-project timings are logged, ordered by elapsed time.

## PackModule

Depends on `RunTestsModule` and `VersionModule`. Skip condition: skipped when `Build:RunPack` is false.

Creates `Build:ArtifactsFolder` and runs `dotnet pack` against `Build:Solution` with `Build:Configuration`, `--output <ArtifactsFolder>`, and `-p:PackageVersion=<version> -p:Version=<version>` where the version comes from `VersionModule`.

## ValidatePackModule

Depends on `PackModule`. Skip condition: skipped when `Build:ValidatePack` is false.

Inspects every `.nupkg`/`.snupkg` in `Build:ArtifactsFolder`. Fails the run if any package has errors. Produces a summary of valid/invalid package counts. See [Pack Validation](Pack-Validation.md) for the full rule set.

## PublishNuGetModule

Category `Release`. Depends on `PackModule`, `ValidatePackModule`, and `RunTestsModule`. Skip condition: skipped unless `Release:Mode` is `NuGet` **and** either `NuGet:TrustedPublishing` is true or an API key resolves via `NuGet:GetNuGetAPIKey()`.

Pushes every `*.nupkg` in `Build:ArtifactsFolder` to `NuGet:FeedUrl` with `--skip-duplicate`. When `NuGet:TrustedPublishing` is true, pushes without an API key (NuGet Trusted Publishing / OIDC federation).

## PublishLocalNuGetModule

Depends on `PackModule` and `ValidatePackModule`. Skip condition: skipped unless the tool is running **locally** (`ctx.IsRunningLocally()`) and `Release:Mode` is `LocalNuGet`. This mode is intentionally ignored in CI.

Validates `PublishLocalNuGet:LocalFeedPath` (resolved via `GetLocalFeedPath()`, falling back to `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH` and then process env `LOCAL_NUGET_FEED_PATH`). The path must be absolute; drive-relative paths such as `p:foo` (backslashes stripped by a sh-style shell) are rejected with a remediation message. See [Local Development](Local-Development.md).

Moves the `.nupkg`/`.snupkg` files from `Build:ArtifactsFolder` into the local feed (skipping existing files unless `OverwriteExistingPackages` is true), optionally clears the NuGet global-packages and HTTP caches for the published packages (`ClearPackageCache`), and optionally shuts down the dotnet build server (`ShutdownDotnetBuilderServer`).

## CreateGitHubReleaseModule

Category `Release`. Depends on `PublishNuGetModule`, `ValidatePackModule`, and `VersionModule`. Skip condition: skipped unless `Release:Mode` is `NuGet` or `GitHubRelease` **and** a GitHub token resolves via `GitHub:GetGitHubToken()`.

Creates a GitHub release with tag `v{version}` and `GenerateReleaseNotes = true`. When `Release:UploadArtifacts` is true, uploads every file in `Build:ArtifactsFolder` as a release asset. The tag must not already exist; callers gate release eligibility (the tool does not skip an existing tag itself).

## See also

- [Architecture](Architecture.md)
- [Configuration Reference](Configuration-Reference.md)
- [Pack Validation](Pack-Validation.md)
- [Release Flow](Release-Flow.md)