# .NET 11: supported runtimes and feature evaluation

.NET 11 reached general availability on **10 November 2026**. It is a Standard Term Support
release, supported from that date until **9 November 2028**. .NET 10 remains the current LTS, supported
until **14 November 2028**.

This page records which runtimes `Purview.Build` supports, what the pinned `Purview.BuildSdk` already
turns on, and the .NET 11 features that were evaluated but not adopted — with the reason, so the
decision is not re-litigated from scratch.

## Supported runtimes

| Target | Release type | Support ends | Status |
| --- | --- | --- | --- |
| `net8.0` | LTS | 10 November 2026 | **Dropped** |
| `net9.0` | STS | 10 November 2026 | **Dropped** |
| `net10.0` | LTS | 14 November 2028 | Supported |
| `net11.0` | STS | 9 November 2028 | Supported |

.NET 8 and .NET 9 reached end of support on the same day, so neither is a defensible baseline for a new
package. Dropping them here means:

- nothing in this repository targets, documents or exemplifies `net8.0`/`net9.0`;
- `System.Security.Cryptography.Pkcs` — a direct reference added to override the vulnerable 8.0.1
  package that `NuGet.Packaging` pulls transitively — is pinned to the runtime band
  (`$(DotNetRuntimeVersion)`), so no .NET 8-era asset is resolved;
- `netstandard2.0` remains in the examples it appears in, because Roslyn components still target it.
  That is a different axis from the .NET version and is not affected by this change.

The tool itself targets whatever `PurviewTargetFrameworkSet` resolves to, which the SDK defaults to
`Current` → `net10.0;net11.0`. Two consequences follow from that, and they matter more than any single
feature below:

1. **Shared code must compile against the oldest target's API surface.** A .NET 11-only API needs a
   `#if NET11_0_OR_GREATER` guard with a documented fallback, not an unconditional call. See
   `PublishLocalNuGetModule` for the pattern.
2. **The published tool must stay installable on .NET 10.** `PackAsTool` ships a
   `tools/<tfm>/any/` folder per target, and NuGet selects the best one for the consumer's runtime. A
   single `net11.0` tool would remove every consumer running on the LTS, so `Current` is the right set
   even though it costs the guard above.

## What the SDK already enables

`Purview.BuildSdk` 2.0.0 opts a `net11.0` target into the rest of .NET 11 without any per-repository
configuration:

| Switch | Effect |
| --- | --- |
| `Features=runtime-async=on` | Runtime Async V2 — the runtime tracks suspension instead of the compiler emitting state machines |
| `Features=updated-memory-safety-rules`, `MemorySafetyRules=2` | The C# 15 memory-safety model |
| `LangVersion` | 15.0 on `net11.0` |
| `AnalysisLevel=latest` | The .NET 11 analyzer set, per target |

`PurviewRuntimeAsync=false` or `UseRuntimeAsync=false` opts a project out; `MemorySafetyRules` selects
the tier.

## Per-target analyzer divergence

The .NET 11 analyzer set is applied to the `net11.0` leg only, and it now suggests APIs that do not
exist on `net10.0`. Two rules are in direct conflict:

- **CA1307** (net11) asks for `string.StartsWith(char, StringComparison)` and friends, which .NET 11
  added. Those overloads do not exist on `net10.0`.
- **CA1865** (both) forbids the portable workaround — passing a single-character *string* to
  `StartsWith`/`EndsWith`/`LastIndexOf`.

There is no expression that satisfies both while compiling for both targets. Because a `char`
comparison is ordinal by definition, CA1307's suggestion buys clarity of intent and nothing else, so it
is suppressed on the newest target only (`Build.csproj`), keeping the rule live on the older one. The
suppression is conditioned on `$(PurviewTargetFrameworksLatest)` rather than a literal `net11.0`, so it
follows the SDK when `Latest` moves to the next major.

If the tool ever collapses to a single target, delete the suppression and adopt the overloads.

## Evaluation by area

Legend: **Adopted** — in this change. **Candidate** — plausible, not done. **Rejected** — deliberately
not done, with the reason. **N/A** — does not apply to this tool.

### Runtime

| Feature | Decision | Notes |
| --- | --- | --- |
| Runtime Async V2 | Adopted (SDK) | On for `net11.0`. Lower overhead in async-heavy code; the runtime libraries are themselves compiled with it, so the whole dependency graph benefits. |
| x86-64-v2 minimum, R2R targeting x86-64-v3 | **Rollout risk** | .NET 11 refuses to start on pre-2013 CPUs ("The current CPU is missing one or more of the baseline instruction sets"). Self-hosted runners and long-lived build agents need auditing before consumers move. Windows Arm64 also moves to an `armv8.2-a + RCPC` R2R baseline. |
| More than 1024 logical processors | N/A | Fixes a startup failure on very large servers; no action. |
| In-process crash reports | Candidate | `DOTNET_EnableCrashReport=1` captures a managed stack trace from inside the dying process. Useful for diagnosing a tool crash on a CI runner where an out-of-process dump is not collected. |

### Async

| Feature | Decision | Notes |
| --- | --- | --- |
| Runtime Async V2 | Adopted (SDK) | See above. |
| Continuations skip `ExecutionContext` capture when there is nothing to restore | Adopted (runtime) | Free; benefits `Task`/`ValueTask` paths this tool uses heavily. |
| Async DataAnnotations validation | Adopted | `Validator.TryValidateObjectAsync` in `PublishLocalNuGetModule`, guarded by `#if NET11_0_OR_GREATER` with the synchronous fallback on `net10.0`. It also honours the module's `CancellationToken`. |

### Performance

| Feature | Decision | Notes |
| --- | --- | --- |
| NativeAOT `dotnet` CLI host | Adopted (runtime) | `dotnet --version/--info/--help`, `dotnet <cmd> --help`, `dotnet sln list` and tool invocations run without starting the managed CLI, saving roughly 600–700 ms per invocation. A pipeline that shells out dozens of times gains the most of any single item here. Opt out with `DOTNET_CLI_ENABLEAOT=false`. |
| MSBuild server enabled by default | **Review the shutdown step** | .NET 11 keeps a warm MSBuild worker between invocations. `PublishLocalNuGet:ShutdownDotnetBuilderServer=true` tears that down after a local publish; with .NET 11 that discards a resource the next run would otherwise reuse. Worth measuring before changing, since a stale server during feed replacement was the original reason for it. |
| JIT: devirtualization, bounds-check elimination, constant-folding `SequenceEqual`, exact-type `Activator.CreateInstance<T>` | Adopted (runtime) | Free and unconditional on `net11.0`. |
| `ReadyToRun` for the tool | Rejected | A dotnet tool ships portable IL; `PublishReadyToRun` would pin it to a platform and remove the single-package cross-platform property. |
| `Comparer<T>.Default` specialised in R2R (up to 20×) | N/A | Only relevant to R2R images. |
| Collection expression arguments (`[with(capacity: …), .. items]`) | Candidate | Micro-optimisation for the several list-building sites in the release and schema code. Low value, zero risk. |

### Testing

| Feature | Decision | Notes |
| --- | --- | --- |
| `dotnet test --no-dependencies` | **Rejected for now** | Would skip the redundant project-to-project builds `RunTestsModule` currently repeats per test project after `BuildModule` has already built the solution. It is an .NET 11-only CLI option, and the tool must keep working on a consumer's .NET 10 SDK — the shared workflows document `10.0.x` as their fallback. Gate it on a probed SDK version before adopting. |
| `--results-directory-layout per-module` | Candidate | Prevents same-named reports from overwriting each other across multi-targeted test projects. Default is `flat`. |
| `--artifacts-path` in MTP mode | Candidate | Aligns test output with the SDK artifacts layout. |
| Run-level policies: `--timeout`, `--maximum-failed-tests` | Candidate | `--timeout` counts only while a test application is running and returns exit code 3; `--maximum-failed-tests` returns 13. Both are .NET 11-only, so the same SDK-version gate as `--no-dependencies` applies. |
| Two-stage Ctrl+C cancellation | Adopted (runtime) | First press stops scheduling, second force-kills. Behaviour change with no code needed. |
| Live in-flight test display, protocol 1.1.0 output forwarding | Adopted (runtime) | Interactive terminals only; no effect on CI output. |
| `DOTNET_TEST_RUNNER` environment variable | Rejected | `global.json` already pins `test.runner` for this repository, and the environment variable would let an ambient value override it silently. |

### Build

| Feature | Decision | Notes |
| --- | --- | --- |
| Fast wildcard matching during evaluation | Adopted (SDK 11.0.100) | 10–13% faster project evaluation and 16–34% less allocation where include/exclude patterns overlap — the common case in SDK-style projects. |
| SDK resolved from `global.json` in CI | Adopted | `ci.yml` and `release.yml` now derive `dotnet-version` from `global.json` instead of hardcoding a band, matching the shared workflows. A hardcoded band and a pinned `global.json` disagree the moment the pin moves, and the job fails at the first `dotnet` command. |
| `dotnet sln` support for solution filters (`.slnf`) | Candidate | Useful for very large consumer repositories; no effect on this one. |
| `Configuration` environment variable feeds `-c` | **Risk noted** | The `dotnet` CLI now uses `Configuration` as the default for the shared `--configuration` option. Every module passes `Configuration` explicitly, so the pipeline is unaffected, but an ambient `Configuration` value in a consumer's CI can now change a bare `dotnet` invocation. |
| `DOTNET_CLI_USE_MSBUILD_SERVER` no longer forced to `0` | Adopted (runtime) | The CLI stops overriding a user-set `MSBUILDUSESERVER`. |

### Debug

| Feature | Decision | Notes |
| --- | --- | --- |
| Runtime Async live stack traces | Adopted (SDK) | `new StackTrace()`, profilers and the debugger see the real call chain (5 frames instead of 13) with no state-machine frames. |
| Breakpoints bind inside runtime-async methods | Adopted (SDK) | Stepping over `await` no longer jumps into compiler infrastructure. |
| `Process` API expansion | **Candidate, highest value** | `ReleaseContextProvider.RunGit` redirects stdout *and* stderr and then calls `StandardOutput.ReadToEnd()` followed by `WaitForExit()`. If `git` fills the stderr pipe buffer the call deadlocks, and neither pipe is drained concurrently. `Process.RunAndCaptureText` (or `ReadAllLinesAsync`, which distinguishes stdout from stderr without parsing) removes the whole class of problem, and `ProcessRunAndCaptureTextAsync` accepts a timeout. Requires a `#if NET11_0_OR_GREATER` guard. |
| `ProcessStartInfo.KillOnParentExit`, `TryGetProcessById`, suspended start | Candidate | Relevant to a pipeline that spawns child processes which must not outlive it. |
| `Console` `FORCE_COLOR` support | Adopted (runtime) | Does **not** change wrapping, so the `CLIConsole` width invariant — output must not be reflowed for `jq`/`grep` — is unaffected. `NO_COLOR` still wins. |

### Packaging

| Feature | Decision | Notes |
| --- | --- | --- |
| NETSDK1235: `PackAsTool` with a custom `NuspecFile` | **Unaffected** | This package rewrites the `.nupkg` after packing with an inline MSBuild task; it does not set `NuspecFile`. The new warning correctly does not fire. |
| `ZipArchive` CRC32 validation on read | Adopted (runtime) | The pack task rewrites the package zip; a corrupt or truncated archive now throws `InvalidDataException` instead of being silently accepted. |
| `ZipArchiveEntry.Open(FileAccess)` / async overloads | Candidate | Would let the PDB-stripping task stream entries with explicit access instead of re-creating every entry. |
| `dotnet nuget add/update source --min-publish-age-hours` | Candidate | Supply-chain hardening for floating references. This repository pins exact versions, so it is only worth adopting as a repository-wide policy. |
| Reproducible container publishing (`SOURCE_DATE_EPOCH`) | N/A | No containers are published. |
| Multi-arch container builds with Podman | N/A | As above. |

### Language features (C# 15)

| Feature | Decision | Notes |
| --- | --- | --- |
| Union types | Rejected for now | Tempting for the eligibility verdict shape, but the verdict is part of the `release-explain` JSON contract and the golden file. A shape change needs its own decision, not a language upgrade. |
| Closed hierarchies (+ inferred `System.Text.Json` polymorphism) | Rejected for now | Changes the serialized contract in the same way. |
| Labelled `break`/`continue` | Candidate | `EligibilityPolicies.ApplyRules` and the test-project filters both use flag-and-`goto`-style control flow that this would simplify. |
| Extension indexers, static interface members on older runtimes | N/A | No site in this codebase would get clearer. |
| Memory safety preview | Adopted (SDK) | `updated-memory-safety-rules` at tier 2, via the SDK. |

## Adopting anything new: the checklist

1. Does the API exist on `net10.0`? If not, guard it with `#if NET11_0_OR_GREATER` and a documented
   fallback — or reject it until the tool is single-target.
2. Is it a `dotnet` CLI option? Then it is a function of the **consumer's** SDK, not the tool's own
   build. Probe or gate it; never assume .NET 11.
3. Run `just release-matrix` and `just config-matrix` as well as `just test` — several of the
   candidates above (process capture, test reporting) touch release eligibility.
