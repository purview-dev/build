# Changelog

All notable changes to `Purview.Build`.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions come from the
`version` field in `package.json`, which is the single source of truth: a merge to `main` publishes
that version to nuget.org.

This file was reconstructed from the conventional-commit history at 0.6.1, so entries before that
release summarise what the commits recorded rather than what was written at the time.

## [0.7.0] - 2026-10-21

### Changed

- **CI runs every test suite, not just the unit ones.** `Build:TestFilter` was
  `/*/*/*/*[Category=Unit]`, which excluded the 74 integration tests from every PR *and* from the release —
  a suite that only ever ran on a developer's machine, so not a gate at all. The filter is now `/*/*/*/*/`,
  matching what `just test` runs.
- **The release path runs tests and lint.** `release.yml` set `Build__RunTests: "false"` and
  `Build__RunLint: "false"`, so the job that publishes to nuget.org and tags the repository was the one
  place taking the build entirely on trust. Both are now `true`.

### Added

- `Build:MaxTestParallelism` bounds how many test projects run concurrently. `0` (the default) uses
  `Environment.ProcessorCount`; `1` runs them one at a time.
- `Build:MaxParallelTestsPerProject` bounds how many tests run concurrently *inside* each project, passed
  to TUnit as `--maximum-parallel-tests`. `0` (the default) derives three quarters of `ProcessorCount`;
  `-1` hands the decision back to the runner.

  The runner's auto-detection assumes cheap, CPU-bound tests. Measured on a 22-core machine against a
  416-test suite whose tests each spawn `dotnet msbuild`: auto took **183s**, 8 took 138s, 16 took **135s**
  and 32 took 154s — auto was the slowest setting tried. The value is derived rather than fixed because a
  constant tuned on one machine oversubscribes a smaller CI runner. Note that the two bounds multiply:
  projects × tests-per-project.

### Performance

- `RunTestsModule` fanned every test project out through an unbounded `Task.WhenAll`. Each project is a
  separate `dotnet test` process that parallelises internally, so on a large solution they competed for
  the same cores and every project finished later than it would have alone. The fan-out is now bounded by
  `Build:MaxTestParallelism`, and results keep discovery order regardless of which project finishes first.
- `ValidatePackModule` validated packages strictly sequentially, so validation time scaled linearly with
  package count for no reason — each package is an independent archive read with no ordering requirement.
  Validation now runs under the same bound. Pairing a `.nupkg` with its `.snupkg` stays sequential and in
  discovery order, because it mutates shared state and the reported order should not depend on which
  archive finished first.

## [0.6.1]

### Added

- Startup configuration preflight: every missing or invalid required setting is reported at once, before
  the pipeline is built, so a missing credential can never surface after the build has already run.

## [0.6.0]

### Added

- `Build:CSharpierPath`, to run a vendored CSharpier assembly instead of the `dotnet-tools.json` manifest
  tool — for a repository needing an unreleased CSharpier build.

## [0.5.4]

### Added

- Agent skills migrated to the root `skills/` folder and the skills CLI.

### Fixed

- Pack validation was wrong when `contentFiles` were included for any TFM.

### Changed

- `global.json` is the single source of truth for the SDK version in CI.

## [0.5.3]

### Added

- JSON schema for `purview-build.json`.
- Release models and types; GitHub releases marked as prereleases for prerelease versions.
- The package version is logged when packing completes.

### Fixed

- `RequiredCompilerFlags` defaults to empty so it can be configured off.
- Test project paths resolved to absolute before `dotnet test`.
- The dogfood tool path in `pipeline-dogfood`.

[Unreleased]: https://github.com/purview-dev/build/compare/v0.7.0...HEAD
[0.7.0]: https://github.com/purview-dev/build/compare/v0.6.1...v0.7.0
[0.6.1]: https://github.com/purview-dev/build/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/purview-dev/build/compare/v0.5.4...v0.6.0
[0.5.4]: https://github.com/purview-dev/build/compare/v0.5.3...v0.5.4
[0.5.3]: https://github.com/purview-dev/build/releases/tag/v0.5.3
