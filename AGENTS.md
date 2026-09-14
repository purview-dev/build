# AGENTS.md

This file is the primary instruction set for human and AI agents working in this repository. It is authoritative; other files referenced below document specifics.

## What this repository is

`Purview.Build` is the shared build/test/release system for the `purview-dev` organisation: a Generalized Modular Pipelines pipeline, packaged as a pinned .NET tool (`src/Purview.Build`) and exposed through a GitHub composite action (`.github/actions/purview-build`) and thin reusable workflows (`.github/workflows/purview-build.yml`, `purview-release.yml`). Consuming repositories own configuration (`purview-build.json`); they do not own pipeline code.

## Repository layout

- `src/Purview.Build/` — the dotnet tool (the pipeline): `Program.cs`, `Modules/`, `Settings/`, `Helpers/`, `appsettings.json` (tool defaults).
- `.github/actions/purview-build/` and `.github/workflows/` — the shared action and reusable workflows.
- `.github/workflows/ci.yml` and `release.yml` — this repository's own CI/CD, which dogfoods the tool.
- `docs/` — authoritative documentation:
  - `docs/architecture.md` — architecture and the full configuration reference (settings keys, defaults, modules, release behavior).
  - `docs/releasing.md` — versioning, branch models, and release strategy.
  - `docs/migrations/` — per-consumer migration guides.
- `README.md` — user-facing overview and minimal consumer setup.
- `purview-build.json` — this repository's own pipeline configuration.
- `Justfile` — developer recipes (`just --list`).

## Build, test, lint

The developer entry points are the `just` recipes; the .NET SDK and `just` are required.

- `just build` — compile `src/Purview.Build` (Debug).
- `just test` / `just test-unit` — run tests under `src/tests` (this repo has no tests yet).
- `just lint-check` / `just lint-fix` — CSharpier (via local tool manifest `.config/dotnet-tools.json`).
- `just pipeline-pr` / `just pipeline-build` / `just pipeline-release` / `just pipeline-tests` — run the shared tool itself.
- `just clean-all` / `just scrub` — clean build outputs.

CI builds with `--warnaserror`.

## Code conventions

- C# is formatted with **CSharpier** (pinned in `.config/dotnet-tools.json`); `just lint-check` must pass. `.pre-commit` (Lefthook, `.config/lefthook.yml`) runs it.
- Target `net10.0`; the codebase uses C# modern idioms (primary constructors, collection expressions, file-scoped namespaces).
- Commits must follow [Conventional Commits](https://www.conventionalcommits.org) (enforced by commitlint via Lefthook). Examples: `fix:`, `feat:`, `build:`, `docs:`, `chore:`.
- Do not add code comments unless they explain non-obvious rationale (see existing comments in `Settings/` and `Modules/` for the style).
- Do not commit secrets or modify secret-handling settings without review.

## Rules and invariants

- **Versioning**: the single source of truth is the `version` field in `package.json`. The pipeline reads it (`VersionModule`); `PackModule` overrides `Version`/`PackageVersion` from it.
- **Do not push release tags manually.** The tool owns tagging (`v{version}`) and the GitHub release (`CreateGitHubReleaseModule`); releasing = bump `package.json` and merge. See `docs/releasing.md`.
- **Secrets**: supplied at runtime via env vars / CI secrets (`NUGET_APIKEY`/`NUGET_API_KEY`, `GITHUB_TOKEN`, `LOCAL_NUGET_FEED_PATH`). Never hardcode or commit them.
- **Configuration precedence**: command line > environment variables (nested keys use `__`) > `purview-build.json` > baked-in defaults (`appsettings.json`).
- **LocalNuGet is local-only**: `Release:Mode=LocalNuGet` is ignored in CI; it only works when running the tool locally.

## Change process

1. Understand the affected surface by reading `docs/architecture.md` (configuration reference) and `docs/releasing.md` before changing settings/modules.
2. Make the change, format with `just lint-fix`, and verify with `dotnet build -c Release --warnaserror` (and `just test` when tests exist).
3. Follow the Conventional Commit style for the commit message.

## Documentation

Keep `README.md`, `docs/architecture.md`, `docs/releasing.md`, and the workflow/action inputs in sync when changing behavior. If a new setting or default is added, it must be reflected in the `docs/architecture.md` configuration tables and in `src/Purview.Build/appsettings.json` defaults.