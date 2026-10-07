# AGENTS.md

This file is the primary instruction set for human and AI agents working in this repository. It is authoritative; other files referenced below document specifics.

## What this repository is

`Purview.Build` is the shared build/test/release system for the `purview-dev` organisation: a Generalized Modular Pipelines pipeline, packaged as a pinned .NET tool (`src/Purview.Build`) and exposed through a GitHub composite action (`.github/actions/purview-build`) and thin reusable workflows (`.github/workflows/purview-build.yml`, `purview-release.yml`). Consuming repositories own configuration (`purview-build.json`); they do not own pipeline code.

## Repository layout

- `src/src/Build/` — the dotnet tool (the pipeline, package `Purview.Build`): `Program.cs`, `Modules/`, `Settings/`, `Helpers/`, `Configuration/`, `Release/`, `Version/`, `appsettings.json` (tool defaults).
- `src/tests/Build.UnitTests/` and `src/tests/Build.IntegrationTests/` — the test suites. TUnit categories come from the project-name suffix (the Purview SDK emits an assembly-level `[Category]`), so **never write a `[Category]` attribute by hand**: put a test in the project whose name carries the category you want.
- `src/tests/fixtures/` — the declarative scenario matrices (`eligibility-scenarios.json`, `config-resolution-scenarios.json`) and the `release-explain` golden file. These drive both the TUnit suites and the `just` CLI matrices; a new rule or probe location means appending cases here, not writing test code.
- `src/tests/scenarios/` — the shell runners behind `just release-matrix` / `just config-matrix`.
- `.github/actions/purview-build/` and `.github/workflows/` — the shared action and reusable workflows.
- `.github/workflows/ci.yml` and `release.yml` — this repository's own CI/CD, which dogfoods the tool.
- `docs/wiki/` — authoritative documentation:
  - `docs/wiki/Architecture.md` — architecture and design decisions.
  - `docs/wiki/Configuration-Reference.md` — the full configuration reference (settings keys, defaults, modules, release behavior).
  - `docs/wiki/Release-Flow.md` — versioning, branch models, and release strategy.
  - `docs/wiki/Release-Models.md` — the three release trigger models, as raw Mermaid source.
  - `docs/wiki/Local-Development.md` — `just` recipes, `release-explain`, simulation, and the local rehearsal.
  - `docs/wiki/Migration-*.md` — per-consumer migration guides.
- `README.md` — user-facing overview and minimal consumer setup.
- `.config/purview-build.json` — this repository's own pipeline configuration.
- `Justfile` — developer recipes (`just --list`).

## Build, test, lint

The developer entry points are the `just` recipes; the .NET SDK and `just` are required.

- `just build` — compile `src/Purview.Build` (Debug).
- `just test` / `just test-unit` — run tests under `src/tests`. CI runs the unit filter only (`Build:TestFilter` in `purview-build.json`); `just test` runs everything.
- `just test-eligibility` — the eligibility and config-resolution suites only.
- `just release-matrix` / `just config-matrix` — run every scenario through the real CLI. **Run both before any change to release or configuration behaviour**; they need no secrets and no network.
- `just release-explain` / `just release-simulate <ref> <version>` — explain the release decision, mutating nothing.
- `just lint-yaml` — `actionlint` over the workflow files (skipped with a note when not installed).
- `just lint-check` / `just lint-fix` — CSharpier (via local tool manifest `.config/dotnet-tools.json`).
- `just pipeline-pr` / `just pipeline-build` / `just pipeline-release` / `just pipeline-tests` — run the shared tool itself.
- `just pipeline-dogfood` — pack the tool from source, install it to a temp tool path, and run it against this repository. Use this rather than running the `bin/` binary against a Release build of itself, which fails on a locked file.
- `just clean-all` / `just scrub` — clean build outputs.

CI builds with `--warnaserror`.

## Code conventions

- C# is formatted with **CSharpier** (pinned in `.config/dotnet-tools.json`); `just lint-check` must pass. `.pre-commit` (Lefthook, `.config/lefthook.yml`) runs it.
- Target `net10.0`; the codebase uses C# modern idioms (primary constructors, collection expressions, file-scoped namespaces).
- Commits must follow [Conventional Commits](https://www.conventionalcommits.org) (enforced by commitlint via Lefthook). Examples: `fix:`, `feat:`, `build:`, `docs:`, `chore:`.
- Do not add code comments unless they explain non-obvious rationale (see existing comments in `Settings/` and `Modules/` for the style).
- Do not commit secrets or modify secret-handling settings without review.

## Rules and invariants

- **Versioning**: the single source of truth is the `version` field in `package.json`. `VersionModule` resolves it once into an ordered set of release units; every downstream module reads that result and **never recomputes a version**. `Version:Strictness` defaults to `NuGet`, which accepts four-part versions — two consuming repositories ship them, so do not tighten this default.
- **Eligibility ownership**: the tool *evaluates* release eligibility and reports it; the workflow *decides* and sets `Release__Mode`. The tool must never silently decline to release when asked to. `ReportEligibilityModule` is category `Build` and every failure path in it is a warning.
- **Rule IDs are permanent**: never renumber or repurpose a `REL0nn`.
- **Do not add a `concurrency` block to `purview-release.yml`**: a shared group between caller and reusable workflow makes GitHub cancel the run as a deadlock. Callers own release serialization.
- **Do not remove the `release-branch` input** from `purview-release.yml`: all seven consuming repositories still pass it.
- **Optional workflow inputs are forwarded only when non-empty.** Environment variables outrank `purview-build.json`, so forwarding an unset input as the empty string erases a consumer's configured value. `WorkflowParityTests` enforces this.
- **CLI output must not be reflowed.** `CLIConsole` lifts the Spectre profile width on every write: at the default 80 columns, redirected output gets newlines inserted inside JSON strings and diagnostics, which breaks `jq` and `grep`.
- **Do not push release tags manually.** The tool owns tagging (`v{version}`) and the GitHub release (`CreateGitHubReleaseModule`); releasing = bump `package.json` and merge. See `docs/wiki/Release-Flow.md`.
- **Secrets**: supplied at runtime via env vars / CI secrets (`NUGET_APIKEY`/`NUGET_API_KEY`, `GITHUB_TOKEN`, `LOCAL_NUGET_FEED_PATH`). Never hardcode or commit them.
- **Configuration precedence**: command line > environment variables (nested keys use `__`) > `purview-build.json` > baked-in defaults (`appsettings.json`).
- **LocalNuGet is local-only**: `Release:Mode=LocalNuGet` is ignored in CI; it only works when running the tool locally.

## Change process

1. Understand the affected surface by reading `docs/wiki/Architecture.md` and `docs/wiki/Configuration-Reference.md` before changing settings/modules.
2. Make the change, format with `just lint-fix`, and verify with `dotnet build -c Release --warnaserror` (and `just test` when tests exist).
3. Follow the Conventional Commit style for the commit message.

## Documentation

Keep `README.md`, `docs/wiki/Architecture.md`, `docs/wiki/Configuration-Reference.md`, and the workflow/action inputs in sync when changing behavior. If a new setting or default is added, it must be reflected in the `docs/wiki/Configuration-Reference.md` tables and in `src/src/Build/appsettings.json` defaults.

### Consuming repositories' own docs

`purview.dev` is generated from each repository's `docs/wiki/`, so a behaviour change here can leave a consumer's page stating something the pipeline no longer does. Those pages live in the consuming repositories, not in this one, and are fixed there — the portal picks the correction up on release.

When changing release behaviour, grep the consuming repositories' `docs/wiki/Release-Flow.md` for claims about the shared pipeline. Three claims that were wrong and have been corrected, as examples of the kind to look for:

- "The workflow does not publish to NuGet" — `release-mode: NuGet` runs `PublishNuGetModule` and pushes to `NuGet:FeedUrl`.
- "The shared pipeline does not mark the GitHub release with the prerelease flag" — `Release:MarkPrerelease` defaults to `true`.
- "…and attaches the package artifacts" — assets are attached only when the caller passes `upload-artifacts: true`, which no consumer currently does.
