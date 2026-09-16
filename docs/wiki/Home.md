# Purview.Build Wiki

This wiki is the project documentation hub for **Purview.Build** — the shared build/test/release system for the `purview-dev` organisation. It is a Generalized Modular Pipelines pipeline (based on the `PipelineCLI` originally developed in `sourcegeneratorframework`) packaged as a pinned .NET tool and exposed through a shared GitHub composite action and thin reusable workflows.

Consuming repositories own configuration (`purview-build.json`); they do not own pipeline source code. Version, paths, feature switches, and release-mode selection are per-repository.

## Delivery surfaces

The same implementation is available three ways:

1. **`Purview.Build` dotnet tool** — NuGet package published to nuget.org. Run anywhere a .NET SDK exists (locally via `just`, in GitHub Actions, or another CI service).
2. **Composite action** `purview-dev/build/.github/actions/purview-build` — for repositories that want the action embedded directly in one of their own jobs.
3. **Reusable workflows** `purview-dev/build/.github/workflows/purview-build.yml` and `.../purview-release.yml` — thin `workflow_call` wrappers with structured inputs/secrets.

## Start here

- [Getting Started](Getting-Started.md)
- [Architecture](Architecture.md)
- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Pack Validation](Pack-Validation.md)
- [Release Flow](Release-Flow.md)
- [Local Development](Local-Development.md)
- [Secrets and Environment Variables](Secrets-and-Environment-Variables.md)
- [Repository CI/CD](Repository-CI-CD.md)
- [Migration: aspire-resourcekit](Migration-aspire-resourcekit.md)

## Pipeline

```text
Version ───────────────┐
Restore → Build → Test ├→ Pack → Validate → Publish → GitHub release
           └→ Lint     │
Version ───────────────┘
```

`Version` reads the SemVer `version` field from `package.json`. Lint restores local tools and runs CSharpier. Tests are discovered under `Build:TestRoot`/`Build:TestPatterns` and run with a TUnit tree-node filter (or an xUnit filter). Pack validation inspects each `.nupkg`/`.snupkg` against required/forbidden content rules (glob patterns) and can enforce source link, deterministic builds, and compiler flags on the packaged assemblies. Publication and GitHub release steps are controlled by `Release:Mode` (`None`, `LocalNuGet`, `NuGet`, `GitHubRelease`) and independently by the `Build__Run*` switches. `LocalNuGet` is only honoured when the tool runs locally; it is ignored in CI.

## Requirements

- .NET SDK 10.0 or later.
- `just` for local recipes (`just --list`).
- A repository root `package.json` whose `version` field is the single release version source.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/src/Build` | The packable `Purview.Build` tool: `Program.cs`, `Modules/`, `Settings/`, `Helpers/`, `appsettings.json` |
| `.github/actions/purview-build` | The shared composite action |
| `.github/workflows` | The reusable `purview-build.yml`/`purview-release.yml` and this repository's own `ci.yml`/`release.yml` |
| `docs/wiki` | This wiki |
| `purview-build.json` | This repository's own pipeline configuration (the tool dogfoods itself) |