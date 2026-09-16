# Local Development

Local work uses the `just` recipes in the `Justfile` (which delegate to plain `dotnet`/`bun` commands) or the shared pipeline tool directly.

## Tool installation

```shell
dotnet tool install Purview.Build --tool-path ./.tools
./.tools/purview-build
```

Omit `--version` to install the latest stable release. For a pinned local tool manifest, add it to `.config/dotnet-tools.json` and run `dotnet tool restore`.

## `just` recipes

| Recipe | Purpose |
| --- | --- |
| `just build` | Build `src/Build.slnx` (Debug) |
| `just test` / `just test-unit` | Run tests with a TUnit tree-node filter (unit filter: `/*/*/*/*[Category=Unit]`) |
| `just lint-check` / `just lint-fix` | CSharpier check / format |
| `just pack` | `dotnet pack` with the current `package.json` version |
| `just pipeline-pr` | Run the shared tool (restore, build, lint, tests) |
| `just pipeline-build` | Run the shared tool without tests |
| `just pipeline-release` | Run the shared tool with `Release:Mode=NuGet` |
| `just pipeline-tests` | Run the shared tool with tests enabled |
| `just pipeline-local-release` | Lint-fix, then run the shared tool with `Release:Mode=LocalNuGet` |
| `just clean-all` / `just scrub` | Clean build outputs |

`just pipeline-*` installs the `Purview.Build` tool to `.tools/purview-build` from nuget.org when it is not already present.

## Local NuGet publishing

`Release:Mode=LocalNuGet` pushes packages to a local feed and is only honoured when the tool runs **locally** — it is ignored in CI.

```shell
LOCAL_NUGET_FEED_PATH=C:/local/nuget-feed/ ./.tools/purview-build --Release:Mode=LocalNuGet
```

The local feed path must be an absolute path. `just` runs recipes through the shell, which strips backslashes from unquoted arguments, so a backslash-based Windows path is mangled before the tool sees it and is rejected. Use the `LOCAL_NUGET_FEED_PATH` environment variable, or forward slashes:

```shell
just pipeline-local-release --PublishLocalNuGet:LocalFeedPath=C:/local/nuget-feed/
```

By default the module overwrites existing packages, clears the NuGet global-packages and HTTP caches for the published packages, and shuts down the dotnet build server afterwards (all configurable under `PublishLocalNuGet`).

## Repository root resolution

The tool locates the repository root by walking up from the current working directory to the nearest `package.json`. Run `purview-build` from within the repository. `MODULAR_PIPELINES_DIRECTORY` can override the directory containing `appsettings.json`.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Release Flow](Release-Flow.md)