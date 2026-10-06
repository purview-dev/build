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
| `just pipeline-dogfood` | Pack the tool from source, install it to a temp tool path, and run it against this repository |
| `just test-eligibility` | The eligibility and config-resolution suites only |
| `just lint-yaml` | `actionlint` over the workflow files (skipped with a note when not installed) |
| `just clean-all` / `just scrub` | Clean build outputs |

`just pipeline-*` installs the `Purview.Build` tool to `.tools/purview-build` from nuget.org when it is not already present.

## Answering "did I break anything?"

| Recipe | Purpose |
| --- | --- |
| `just release-explain *args` | Explain the release decision for the working tree |
| `just release-simulate ref version *args` | Explain the decision for a simulated ref and version |
| `just release-matrix` | Run every eligibility scenario through the real CLI; non-zero on any mismatch |
| `just config-matrix` | Run every config-resolution scenario through the real CLI |
| `just release-explain-golden` | Regenerate the `release-explain` JSON golden file and show the diff |

`just release-matrix` and `just config-matrix` are the two commands to run before opening a pull
request that touches release behaviour. Both need **no secrets and no network**: every case supplies
its own ref and tag list through `Release:Context:*`, which suppresses all process and network
lookups. Both build the tool first (a restore is needed on a genuinely fresh clone) and then run the
real binary, so the shipped CLI and the in-process evaluator are held to one specification — the same
fixture files drive the TUnit suites:

- `src/tests/fixtures/eligibility-scenarios.json`
- `src/tests/fixtures/config-resolution-scenarios.json`

Adding a rule or a probe location means appending cases to those files, not writing test code.

Each run creates throwaway repositories under `.scenario-runs/` (gitignored) and leaves the working
tree untouched.

## `release-explain`

Prints the release decision and exits **0** without running any module and without mutating
anything. The decision's own exit code is reported inside the output, for a caller to act on.

```shell
just release-explain
just release-explain --config .config/purview-build.json
./.tools/purview-build/purview-build release-explain --format=json
```

It reports the resolved configuration path, the probe trail, any shadowed files, whether user
configuration was active, the resolved release units, the selected policy and its fully resolved rule
list, every rule in evaluation order with its verdict and message, the short-circuit point, the
`Release__Mode` / `Release:Publish` / `Release:GitHubRelease` / feed URL that would result, and the
ref it evaluated against with where that ref came from.

`--format=json` is the contract the reusable release workflow consumes. It is pinned by a golden file
(`src/tests/fixtures/release-explain.golden.json`), because consuming repositories pin the workflow
by ref: a change in shape or in a rule message is a change to a published contract.

### Simulating a ref and version

```shell
just release-simulate refs/heads/main 1.1.0
just release-simulate refs/heads/release/2.0 2.0.2 --Release:Eligibility:Policy=TrunkReservesMinor
just release-simulate refs/heads/release/2.1 2.1.0-prerelease.1 --Release:Eligibility:Policy=TrunkReservesMinor
```

The output is clearly marked `SIMULATED`, and the real repository's branch and tags do not leak in —
a simulated verdict must never be mistakable for a real one.

## Safety interlocks

A local run cannot perform a real publish or create a tag:

- `release-explain` never mutates anything.
- If any `Release:Context:*` key is set and the run would really publish, the pipeline **fails before
  any module runs**, naming the conflict. Simulated context must never drive a real publish.
- `Release:DryRun=true` runs the full pipeline but skips publishing and the GitHub release, logging
  what each would have done.
- `Release:Mode=LocalNuGet` is honoured only when running locally and is ignored in CI.

## End-to-end local rehearsal

`Release:Mode=LocalNuGet` is the natural rehearsal path. Install the tool to a separate tool path
first — running the Release-configuration build against the binary that is executing it will fail on
a locked file:

```shell
dotnet pack src/Build.slnx -c Release -o ./artifacts/dogfood \
    -p:Version=$(bun -p "require('./package.json').version") \
    -p:PackageVersion=$(bun -p "require('./package.json').version")
dotnet tool install Purview.Build --tool-path ./.tools/dogfood \
    --add-source ./artifacts/dogfood --version $(bun -p "require('./package.json').version")

LOCAL_NUGET_FEED_PATH=/tmp/purview-local-feed ./.tools/dogfood/purview-build --Release:Mode=LocalNuGet
```

`just pipeline-dogfood` does the pack-and-install part for you.

The rehearsal exercises Clean → Version → ReportEligibility → Restore → Build → Test → Lint → Pack →
ValidatePack → local publish, and provably does **not** create a tag, create a GitHub release, or
touch a remote feed: `PublishNuGetModule` and `CreateGitHubReleaseModule` both report `Skipped`, even
when `NUGET_APIKEY` and `GITHUB_TOKEN` are present in the environment.

For a dry run against the real publication path instead:

```shell
./.tools/dogfood/purview-build --Release:Mode=NuGet --Release:DryRun=true
```

Both publish steps then log `Would have …` and perform no action.

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

The tool locates the repository root by walking up from the current working directory to the nearest `package.json`. Run `purview-build` from within the repository. `MODULAR_PIPELINES_DIRECTORY` can override the directory containing the tool's shipped `appsettings.json` — it has nothing to do with locating `purview-build.json`, which has its own [probe order](Configuration-Reference.md#default-probe-order).

## Windows paths and `just`

`just` runs recipes through a shell that strips backslashes from unquoted arguments, so a
backslash-based Windows path is mangled before the tool sees it. Use forward slashes, or the
`LOCAL_NUGET_FEED_PATH` environment variable:

```shell
just pipeline-local-release --PublishLocalNuGet:LocalFeedPath=p:/_sync-projects/.local-nuget/
```

The local feed path must be absolute. A drive-relative path such as `p:foo` — the classic signature
of stripped backslashes — is rejected with a remediation message rather than silently resolving
against the current directory.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Release Flow](Release-Flow.md)