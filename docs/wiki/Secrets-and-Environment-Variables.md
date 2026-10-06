# Secrets and Environment Variables

Secrets must never be committed. They are supplied at runtime via environment variables / CI secrets and read by the pipeline through the settings' lookup helpers.

## Precedence recap

Command line > environment variables > `purview-build.json` > baked-in defaults. Nested environment keys use `__`, for example `Release__Mode=NuGet`. Because env vars take precedence over `purview-build.json`, an empty forwarded env var can silently override a configured value — the reusable workflows only forward optional test settings when the caller actually provides them.

## Secrets

| Secret | Where it is used | Environment-var bound alias |
| --- | --- | --- |
| `NUGET_APIKEY` | NuGet push | `NuGet__NUGET_APIKEY` (binds `EnvAPIKey`); also read directly from process env `NUGET_APIKEY`/`NUGET_API_KEY` |
| `NuGet__APIKey` | NuGet push | `APIKey`. Configuration keys are case-insensitive, so the historical `NUGET__APIKEY` secret name binds here too |
| `GITHUB_TOKEN` | GitHub release creation | `GitHub__GITHUB_TOKEN` (binds `EnvAccessToken`); also read directly from process env `GITHUB_TOKEN` |
| `LOCAL_NUGET_FEED_PATH` | Local NuGet publishing | `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH` (binds `EnvLocalFeedPath`); also read directly from process env `LOCAL_NUGET_FEED_PATH` |

The config binder does not map plain `NUGET_APIKEY`/`GITHUB_TOKEN`/`LOCAL_NUGET_FEED_PATH` process env vars under their settings sections, so the settings classes fall back to reading the process environment directly.

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
| `GITHUB_REF` | Read by the eligibility rules as the evaluated ref, unless `Release:Context:Ref` overrides it. |

Pipeline verbosity is configured with `Build__LogLevel` (default `Information`, which reports each module's command output and progress); set `Build__LogLevel=Warning` for quiet CI logs.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Release Flow](Release-Flow.md)