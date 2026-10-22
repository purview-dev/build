---
name: purview-build-configuration
description: >-
  Use when configuring Purview.Build - where purview-build.json is found and how it is anchored,
  the precedence order, every Build/PackValidation/NuGet/GitHub/Version/Release setting, the secrets
  and environment variables, and why a configured value appears to be ignored.
metadata:
  product: Purview.Build
  repository: purview-dev/build
---

# Configuring Purview.Build

## Where the file lives

Explicitly:

| Form | Resolution |
| --- | --- |
| `--config <path>` / `-c <path>` | absolute, or relative to the current working directory |
| `PURVIEW_BUILD_CONFIG=<path>` | the same resolution; `--config` wins |

An explicit path that does not exist is an **error**, never a silent fallback.

Otherwise the tool probes for `purview-build.json`, conventionally at `.config/purview-build.json`.
Relative paths inside the file are anchored to the repository root, not to the file's own folder, so
moving the file does not change what `src/tests` means.

`PURVIEW_BUILD_USER_CONFIG=1` additionally reads machine-local user configuration — ignored when not
running locally.

`MODULAR_PIPELINES_DIRECTORY` is **unrelated**: it is where the tool's own shipped `appsettings.json`
lives, not part of `purview-build.json` discovery.

## Precedence

**Command line > environment variables > `purview-build.json` > baked-in defaults.**

Nested environment keys use `__`: `Release__Mode=NuGet`, `Build__TestFilter=…`. Configuration keys
are case-insensitive.

The consequence worth internalising: because environment variables outrank the file, an **empty**
forwarded variable silently overrides a configured value. If a setting appears to be ignored, look
for an environment variable of that name before anything else.

## `Build`

| Key | Default | Purpose |
| --- | --- | --- |
| `LogLevel` | `Information` | `Information` reports every module's command output and progress; `Warning` keeps CI logs quiet |
| `ProjectType` | `DotNet` | `DotNet`, or `Web` (Bun commands from the root `package.json` scripts) |
| `Solution` | `src/Product.slnx` | solution, project or directory for restore/build/pack |
| `Configuration` | `Release` | .NET configuration |
| `ArtifactsFolder` | `artifacts` | package output directory |
| `CleanArtifacts` | `true` | delete and recreate `ArtifactsFolder` before anything is produced. Ignored when `RunPack` is `false` |
| `RunTests` / `RunLint` / `RunPack` / `ValidatePack` | `true` | the per-stage switches |
| `CSharpierPath` | unset | path (relative to the repository root, or absolute) to a CSharpier CLI `.dll` run with `dotnet` instead of the `dotnet-tools.json` manifest tool, for an unreleased CSharpier build (dotnet only) |
| `TestRoot` | `src/tests` | discovery root, relative to the repository root |
| `TestPatterns` | `*Tests.csproj` | comma-separated search patterns under `TestRoot` |
| `TestProjects` | `*` | comma-separated names/globs to actually run |
| `TestFramework` | `TUnit` | `TUnit` (tree-node filter) or `xUnit` (VSTest filter) |
| `TestFilter` | `/*/*/*/*/` | the filter; empty disables it |
| `Web*Command`, `WebBuildOutput` | Bun defaults | only for `ProjectType=Web` |

## `PackValidation`

| Key | Default | Purpose |
| --- | --- | --- |
| `RequireSymbolPackage` | `true` | every `.nupkg` needs a matching `.snupkg` and vice versa, except analyzer-only packages embedding PDBs under `analyzers/dotnet/` |
| `RequireSymbolFiles` | `true` | every `.snupkg` holds at least one `.pdb` |
| `RequireSourceLink` | `false` | every shipped assembly's PDB carries a Source Link record |
| `RequireDeterministic` | `false` | every shipped assembly carries the Reproducible debug directory entry |
| `RequiredCompilerFlags` | `[]` | flags that must appear in the PDB compiler-flags record, e.g. `optimization=release` |
| `RequiredContent` | `{}` | package-id glob → entry-path globs that must be present. Supports the `$(TFM)` token |
| `ForbiddenContent` | `{}` | package-id glob → entry-path globs that must not be present |
| `RequireExplicitContent` | `true` | makes `RequiredContent` **exhaustive** |

`RequireExplicitContent` defaulting to `true` is the one that surprises people: an undeclared
package, or an undeclared entry in a declared package, is an error. See the
`purview-build-pack-validation` skill.

## `NuGet`, `PublishLocalNuGet`, `GitHub`

| Key | Default | Purpose |
| --- | --- | --- |
| `NuGet:FeedUrl` | nuget.org v3 | remote source |
| `NuGet:TrustedPublishing` | `false` | push without an API key (OIDC federation) |
| `NuGet:APIKey` / `EnvAPIKey` | unset | secret |
| `NuGet:GitHubPackages:Enabled` | `false` | also push the same packages to the organisation's GitHub Packages feed. The shared release workflow maps `PURVIEW_PUBLISH_GITHUB_PACKAGES` onto it |
| `NuGet:GitHubPackages:Owner` | `GITHUB_REPOSITORY_OWNER`, then `purview-dev` | feed owner |
| `NuGet:GitHubPackages:FeedUrl` | derived | `https://nuget.pkg.github.com/{Owner}/index.json` |
| `NuGet:GitHubPackages:Username` | `GITHUB_ACTOR`, then the owner | basic-auth username sent with `GITHUB_TOKEN` |
| `PublishLocalNuGet:LocalFeedPath` | unset | **absolute** local source; a drive-relative path such as `p:foo` is rejected with a remediation message |
| `PublishLocalNuGet:OverwriteExistingPackages` / `ShutdownDotnetBuilderServer` / `ClearPackageCache` | `true` | local publish behaviour |
| `GitHub:AccessToken` / `EnvAccessToken` | unset | secret |
| `GitHub:ProductHeader` | `Purview.Build.Pipeline` | API product header |

## `Version`

| Key | Default | Purpose |
| --- | --- | --- |
| `Source` | `PackageJson` | the only implemented source: the `version` field of the root `package.json` |
| `Strictness` | `NuGet` | accepts everything `NuGetVersion` does, including four-part (`13.5.3.10`) and one/two-part (`1.0`) versions. `SemVer2` is the stricter opt-in |

The tag preserves the version **exactly as written**, not its normalised form — normalising would
drop a trailing `.0` revision and desynchronise the tag from the declared version.

## `Release`

| Key | Default | Purpose |
| --- | --- | --- |
| `Mode` | `None` | preset: `None`, `LocalNuGet`, `NuGet`, `GitHubRelease`. Derives `Publish` and `GitHubRelease` |
| `Publish` | derived | push packages; setting it explicitly wins over the preset |
| `GitHubRelease` | derived from `Mode`, then the channel | create the tag and release; explicit wins over both |
| `Channel` / `Channels` | `stable` / `{}` | a channel selects the feed and the label policy: `{ FeedUrl, GitHubRelease, MarkPrerelease, LabelPattern }` |
| `DryRun` | `false` | run everything, publish nothing, log what each step would have done |
| `UploadArtifacts` | `false` | upload `ArtifactsFolder` as GitHub release assets |
| `MarkPrerelease` | `true` | a prerelease **version** becomes a GitHub prerelease. GitHub metadata only — NuGet publication is unaffected |

| `Mode` | `Publish` | `GitHubRelease` |
| --- | --- | --- |
| `None` | false | false |
| `NuGet` | true | true |
| `GitHubRelease` | false | true |
| `LocalNuGet` | false | false |

`Release:Eligibility` (`Policy`, `Policies`, `Inherits`, `Rules`, the ref sets, `AllowFourPart`) and
`Release:Context` (`Ref`, `ExistingTags`, `PublishedVersions`, for simulated evaluation) are covered
by the `purview-build-release-eligibility` skill.

## Secrets

Never committed; supplied at runtime.

| Secret | Used for | Bound alias |
| --- | --- | --- |
| `NUGET_APIKEY` | NuGet push | `NuGet__NUGET_APIKEY`; also read from process env `NUGET_APIKEY`/`NUGET_API_KEY` |
| `NuGet__APIKey` | NuGet push | `APIKey`; the historical `NUGET__APIKEY` name binds here too, keys being case-insensitive |
| `GITHUB_TOKEN` | the GitHub release | `GitHub__GITHUB_TOKEN`; also read from process env |
| `LOCAL_NUGET_FEED_PATH` | local publishing | `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH`; also read from process env |

The config binder does not map the plain process variables under their settings sections, which is
why the settings classes read the process environment directly as a fallback.

## Fail-fast preflight

Before the pipeline is built, `ConfigurationPreflight` checks the dependencies of the capabilities
the run intends to use, and reports **every** missing requirement at once:

| When | Required |
| --- | --- |
| `Build:ProjectType=DotNet` | `Build:Solution` exists |
| `Build:RunLint=true` and `Build:CSharpierPath` is set | that file exists |
| `Release:Mode=LocalNuGet`, running locally, not Web | `PublishLocalNuGet:LocalFeedPath` / `LOCAL_NUGET_FEED_PATH`, absolute |
| `Release:Publish` true, not Web, `Release:DryRun=false` | a feed, and a NuGet API key or `NuGet:TrustedPublishing=true` |
| `Release:GitHubRelease` true, `Release:DryRun=false` | `GITHUB_TOKEN` and `GITHUB_REPOSITORY_ID` |

`Release:DryRun=true` waives the remote credentials. A requested release with no credential now
fails immediately instead of skipping the publish or release module after the build.

## Diagnostics variables

| Variable | Purpose |
| --- | --- |
| `PURVIEW_BUILD_CONFIG` | which config file to read |
| `PURVIEW_BUILD_USER_CONFIG` | also read machine-local user config; local runs only |
| `PURVIEW_BUILD_STACKTRACE` | include stack traces. Unset, a failure prints only the failing module and its output, then exits 1 |
| `GITHUB_REF` | the ref the eligibility rules evaluate, unless `Release:Context:Ref` overrides it |
| `Build__LogLevel` | pipeline verbosity |

## Informational commands

| Option | Effect |
| --- | --- |
| `-v`, `--version` | print the tool version and exit |
| `-h`, `--help`, `-?` | usage, options, the probe order, and the **resolved configuration path** (or "none found") |
| `release-explain` | print the release decision and exit, running no module. `--format=json` emits the stable contract the reusable workflow consumes |

`--help` printing the resolved path is the fastest answer to "which config is it actually reading?".

## Related skills

`purview-build-adoption` for wiring it up, `purview-build-pipeline` for what each module does,
`purview-build-pack-validation` for the content rules, and `purview-build-release-eligibility` for
the `REL0nn` policies.
