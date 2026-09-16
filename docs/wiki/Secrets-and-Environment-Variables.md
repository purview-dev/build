# Secrets and Environment Variables

Secrets must never be committed. They are supplied at runtime via environment variables / CI secrets and read by the pipeline through the settings' lookup helpers.

## Precedence recap

Command line > environment variables > `purview-build.json` > baked-in defaults. Nested environment keys use `__`, for example `Release__Mode=NuGet`. Because env vars take precedence over `purview-build.json`, an empty forwarded env var can silently override a configured value — the reusable workflows only forward optional test settings when the caller actually provides them.

## Secrets

| Secret | Where it is used | Environment-var bound alias |
| --- | --- | --- |
| `NUGET_APIKEY` | NuGet push | `NuGet__NUGET_APIKEY` (binds `EnvAPIKey`); also read directly from process env `NUGET_APIKEY`/`NUGET_API_KEY` |
| `NuGet__ApiKey` | NuGet push | `APIKey` |
| `GITHUB_TOKEN` | GitHub release creation | `GitHub__GITHUB_TOKEN` (binds `EnvAccessToken`); also read directly from process env `GITHUB_TOKEN` |
| `LOCAL_NUGET_FEED_PATH` | Local NuGet publishing | `PublishLocalNuGet__LOCAL_NUGET_FEED_PATH` (binds `EnvLocalFeedPath`); also read directly from process env `LOCAL_NUGET_FEED_PATH` |

The config binder does not map plain `NUGET_APIKEY`/`GITHUB_TOKEN`/`LOCAL_NUGET_FEED_PATH` process env vars under their settings sections, so the settings classes fall back to reading the process environment directly.

## Test filter forwarding

The reusable workflows (`purview-build.yml`, `purview-release.yml`) forward the caller's `test-filter` and `test-projects` inputs as `Build__TestFilter`/`Build__TestProjects` **only when they are non-empty**. An empty forwarded value would override a consuming repository's `purview-build.json` (env vars take precedence over JSON) and silently disable the filter — see commit `4d72bf7`.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Release Flow](Release-Flow.md)