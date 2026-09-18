# Pipeline Modules

The pipeline is a Modular Pipelines orchestration. Modules are registered in `Program.cs`; explicit `[DependsOn]` edges define ordering, while `ModuleConfiguration` skip conditions gate opt-in behavior. Module categories are `Build` and `Release`.

```text
Version ───────────────┐
Restore → Build → Test ├→ Pack → Validate → Publish → GitHub release
   └→ Lint             │
Version ───────────────┘
```

## VersionModule

Reads the SemVer `version` field from the repository root `package.json` and produces a `NuGetVersion`. Fails when the file is missing, the field is missing/empty, or the value is not valid SemVer. The version feeds `PackModule` (via `Version`/`PackageVersion`) and `CreateGitHubReleaseModule` (via the `v{version}` tag).

## RestoreModule

Runs `Build:WebInstallCommand` (default `bun install`) when `Build:ProjectType` is `Web`, otherwise `dotnet restore` against `Build:Solution`.

## BuildModule

Depends on `RestoreModule`.

- **DotNet**: runs `dotnet build` against `Build:Solution` with `Build:Configuration` and `--no-restore`.
- **Web**: when `Build:WebBuildCommand` is left at the default (`bun run build`) and the repository declares a `data:sync` script, first runs `bun run data:sync` (so a fresh checkout can build offline), then runs the build command. Overriding `WebBuildCommand` takes full control of the build (for example a chain of validation scripts); the automatic data sync is then skipped.

## LintModule

Depends on `RestoreModule` (Web lint invokes tooling from `node_modules`, so it must wait for `bun install`; dotnet lint is unaffected beyond running after restore). Skip condition: skipped when `Build:RunLint` is false.

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

- **DotNet**: creates `Build:ArtifactsFolder` and runs `dotnet pack` against `Build:Solution` with `Build:Configuration`, `--output <ArtifactsFolder>`, and `-p:PackageVersion=<version> -p:Version=<version>` where the version comes from `VersionModule`.
- **Web**: creates `Build:ArtifactsFolder` and zips `Build:WebBuildOutput` (default `src/dist`) into `<package-name>-<version>.zip` (name from the root `package.json` `name` field, version from `VersionModule`). Logs a warning and produces no artifact when the build output directory does not exist.

## ValidatePackModule

Depends on `PackModule`. Skip condition: skipped when `Build:ValidatePack` is false **or** `Build:ProjectType` is `Web` (Web projects produce no `.nupkg`).

Inspects every `.nupkg`/`.snupkg` in `Build:ArtifactsFolder`. Fails the run if any package has errors. Produces a summary of valid/invalid package counts. See [Pack Validation](Pack-Validation.md) for the full rule set.

## PublishNuGetModule

Category `Release`. Depends on `PackModule`, `ValidatePackModule`, and `RunTestsModule`. Skip condition: skipped when `Build:ProjectType` is `Web` **or** `Release:Mode` is not `NuGet` **or** (`NuGet:TrustedPublishing` is false and no API key resolves via `NuGet:GetNuGetAPIKey()`).

Pushes every `*.nupkg` in `Build:ArtifactsFolder` to `NuGet:FeedUrl` with `--skip-duplicate`. When `NuGet:TrustedPublishing` is true, pushes without an API key (NuGet Trusted Publishing / OIDC federation).

## PublishLocalNuGetModule

Depends on `PackModule` and `ValidatePackModule`. Skip condition: skipped when `Build:ProjectType` is `Web` **or** the tool is not running **locally** (`ctx.IsRunningLocally()`) **or** `Release:Mode` is not `LocalNuGet`. This mode is intentionally ignored in CI.

Validates `PublishLocalNuGet:LocalFeedPath` (resolved via `GetLocalFeedPath()`, falling back to `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH` and then process env `LOCAL_NUGET_FEED_PATH`). The path must be absolute; drive-relative paths such as `p:foo` (backslashes stripped by a sh-style shell) are rejected with a remediation message. See [Local Development](Local-Development.md).

Moves the `.nupkg`/`.snupkg` files from `Build:ArtifactsFolder` into the local feed (skipping existing files unless `OverwriteExistingPackages` is true), optionally clears the NuGet global-packages and HTTP caches for the published packages (`ClearPackageCache`), and optionally shuts down the dotnet build server (`ShutdownDotnetBuilderServer`).

## CreateGitHubReleaseModule

Category `Release`. Depends on `PublishNuGetModule`, `ValidatePackModule`, and `VersionModule`. Skip condition: skipped unless `Release:Mode` is `NuGet` or `GitHubRelease` **and** a GitHub token resolves via `GitHub:GetGitHubToken()`.

Creates a GitHub release with tag `v{version}` and `GenerateReleaseNotes = true`. When `Release:UploadArtifacts` is true, uploads every file in `Build:ArtifactsFolder` as a release asset — for Web projects this is the `<package-name>-<version>.zip` produced by `PackModule`. The tag must not already exist; callers gate release eligibility (the tool does not skip an existing tag itself).

## See also

- [Architecture](Architecture.md)
- [Configuration Reference](Configuration-Reference.md)
- [Pack Validation](Pack-Validation.md)
- [Release Flow](Release-Flow.md)