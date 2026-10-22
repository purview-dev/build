# Secrets and Environment Variables

Secrets must never be committed. They are supplied at runtime via environment variables / CI secrets and read by the pipeline through the settings' lookup helpers.

## Precedence recap

Command line > environment variables > `purview-build.json` > baked-in defaults. Nested environment keys use `__`, for example `Release__Mode=NuGet`. Because env vars take precedence over `purview-build.json`, an empty forwarded env var can silently override a configured value — the reusable workflows only forward optional test settings when the caller actually provides them.

## Secrets

| Secret | Where it is used | Environment-var bound alias |
| --- | --- | --- |
| `NUGET_APIKEY` | NuGet push | `NuGet__NUGET_APIKEY` (binds `EnvAPIKey`); also read directly from process env `NUGET_APIKEY`/`NUGET_API_KEY` |
| `NuGet__APIKey` | NuGet push | `APIKey`. Configuration keys are case-insensitive, so the historical `NUGET__APIKEY` secret name binds here too |
| `GITHUB_TOKEN` | GitHub release creation; GitHub Packages push | `GitHub__GITHUB_TOKEN` (binds `EnvAccessToken`); also read directly from process env `GITHUB_TOKEN` |
| `LOCAL_NUGET_FEED_PATH` | Local NuGet publishing | `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH` (binds `EnvLocalFeedPath`); also read directly from process env `LOCAL_NUGET_FEED_PATH` |
| `PURVIEW_PACKAGES_READ_TOKEN` | Restoring from GitHub Packages across repositories | Optional organisation secret; a classic PAT with `read:packages`. Prefer it over per-package Actions-access grants. Never used for publishing. |

GitHub Packages uses the same `GITHUB_TOKEN`, not a separate PAT. The token needs `packages: write`
to publish and `packages: read` to restore. The shared source configuration stores only the
`%GITHUB_TOKEN%` macro in the user-level NuGet configuration, so no token is written to disk.

The config binder does not map plain `NUGET_APIKEY`/`GITHUB_TOKEN`/`LOCAL_NUGET_FEED_PATH` process env vars under their settings sections, so the settings classes fall back to reading the process environment directly.

## Required before any module runs

Credentials and paths are checked by the startup preflight, before the pipeline is built. A missing requirement fails the run immediately and names every problem at once — it is never discovered by a module after clean, restore, build, test, and pack have already run.

| Requirement | Needed when | Fix |
| --- | --- | --- |
| `LOCAL_NUGET_FEED_PATH` (or `PublishLocalNuGet:LocalFeedPath`) | `Release:Mode=LocalNuGet`, running locally | Set an absolute feed path |
| `NUGET_APIKEY` / `NUGET_API_KEY` (or `NuGet:APIKey`) | `Release:Publish` is enabled and `NuGet:TrustedPublishing` is false | Provide the key, or opt into Trusted Publishing |
| `GITHUB_TOKEN` | a real (non-dry-run) GitHub release is requested, or `NuGet:GitHubPackages:Enabled=true` and publication is requested | Provide a token that can create releases / write packages |
| `GITHUB_REPOSITORY_ID` | a real (non-dry-run) GitHub release is requested | Supplied automatically by GitHub Actions; set it for a local run |
| `NuGet:GitHubPackages:FeedUrl` | `NuGet:GitHubPackages:Enabled=true` and publication is requested | Must be an absolute `https` URL. Leave it unset: it derives from `GitHubPackages:Owner`, `GITHUB_REPOSITORY_OWNER`, then `purview-dev` |

`Release:DryRun=true` waives the remote credentials. `Release:Mode=NuGet` in a workflow therefore fails rather than silently skipping publication when neither a key nor Trusted Publishing is configured.

## Optional-input forwarding

The reusable workflows (`purview-build.yml`, `purview-release.yml`) forward every **optional** input only when it is non-empty: `test-filter`, `test-projects`, `config-path`, `eligibility-policy`, `release-channel`, and `version-source`. An empty forwarded value would override a consuming repository's `purview-build.json` (env vars take precedence over JSON) and silently erase a configured value — see commit `4d72bf7`.

Inputs that declare a `default:` (for example `release-mode`, `run-tests`) are never empty and are mapped directly into `env:`.

This is enforced by a test (`WorkflowParityTests`), which asserts that every `Section__Key` the workflows set resolves to a real settings property, and that no optional input is mapped straight into `env:` without an `if [ -n … ]` guard.

## Configuration and diagnostics variables

| Variable | Purpose |
| --- | --- |
| `PURVIEW_BUILD_CONFIG` | Selects **which** `purview-build.json` to read (`--config` wins over it). An explicit path that does not exist is an error, never a silent fallback. |
| `PURVIEW_BUILD_USER_CONFIG` | Set to `1` (or `true`) to also read machine-local user configuration. Ignored when not running locally. |
| `PURVIEW_BUILD_STACKTRACE` | Set to `1` (or `true`) to include stack traces in failure reports. Unset, a failing run prints only the failing module and that module's output, then exits with code 1. |
| `MODULAR_PIPELINES_DIRECTORY` | The directory containing the tool's shipped `appsettings.json`. Unrelated to `purview-build.json` discovery. |
| `PURVIEW_PUBLISH_GITHUB_PACKAGES` | Repository Actions **variable**, read only by the release workflows: `true` adds GitHub Packages as a publication destination. Absent or `false` means NuGet.org only. |
| `PURVIEW_GITHUB_PACKAGES_KEEP_VERSIONS` | Repository Actions variable read by the release workflows: the newest N versions of each package are always kept by the mirror cleanup (default `1`; `0` removes the floor). |
| `PURVIEW_GITHUB_PACKAGES_RETENTION_HOURS` | Repository Actions variable read by the release workflows: how long a mirrored version survives after publication (default `24`; `1` for an hourly window; `0` prunes by count only). |
| `PURVIEW_NUGET_SOURCE_MAPPING` | Repository Actions variable read by the source setup: semicolon-separated package ID patterns that must resolve only from GitHub Packages. Empty keeps both sources available. |
| `GITHUB_REF` | Read by the eligibility rules as the evaluated ref, unless `Release:Context:Ref` overrides it. |

Pipeline verbosity is configured with `Build__LogLevel` (default `Information`, which reports each module's command output and progress); set `Build__LogLevel=Warning` for quiet CI logs.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Release Flow](Release-Flow.md)