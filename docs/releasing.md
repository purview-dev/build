# Versioning and release strategy

`Purview.Build` follows SemVer. The package version is the compatibility contract for configuration keys, defaults, module ordering, and tool behavior.

- Patch: fixes that preserve configuration and pipeline behavior.
- Minor: additive options or modules with backward-compatible defaults.
- Major: renamed/removed keys, changed defaults with material effects, or a required runtime upgrade.

Consumers pin an exact version in the reusable-workflow `build-version` input (and, for local use, `.config/dotnet-tools.json`); never use a floating range. Automated dependency updates should open a pull request, where the consumer's normal build validates the new tool before merge. Keep the previous major supported while migrations are in progress.

The version is declared by the `version` field in the repository's root `package.json`. Releasing consists of bumping that field and merging the validated pull request into the release head.

## Branch models

Each repository is gated by a pull-request build. Two release trigger models are supported; the consuming repository's tiny caller workflow chooses:

- **Release on `main`**: the release caller triggers on `push: branches: [main]`.
- **Main-as-head / release branch**: development merges to `main`, and merging `main` into a `release` branch performs the release. The release caller triggers on `push: branches: [release]`.

In both models the reusable `purview-release.yml` workflow reads `package.json`'s `version`, skips when the `v{version}` tag already exists, and otherwise runs the pipeline with `Release__Mode` set. Because publication is idempotent (`--skip-duplicate`) and the tag is created by the workflow, re-merging `main` into `release` after a failed release is safe.

## This repository's CI/CD

This repository dogfoods the shared tool. CI performs locked restore, warnings-as-errors compilation, packing, installation from the generated package, then runs `purview-build` against this repository so the project builds and packs itself.

On a push to `main`, the release workflow reads and validates the `package.json` version, skips when `v{version}` already exists, then builds and installs the tool from the current source and runs it with `Release__Mode=NuGet`, `NuGet__FeedUrl` set to the Purview-Dev GitHub Packages registry, and `Release__UploadArtifacts=true`. The tool performs the release build/pack steps, publishes the immutable package to the registry, and creates `v{version}` plus a generated-notes GitHub release with the package attached — tagging itself exactly like every other purview-dev repository. The tool therefore owns tagging; maintainers must not push release tags manually.

GitHub creates the package as private on its first publication. An organization owner must make the package Internal once in the package settings so Purview-Dev members can consume it, and must permit internal package creation in the organization's package policy. NuGet versions are immutable; `--skip-duplicate` makes recovery safe if publication succeeded but tagging was interrupted.

## For local validation

```shell
dotnet pack src/Purview.Build/Purview.Build.csproj -c Release -o artifacts -p:Version=0.2.0 -p:PackageVersion=0.2.0
dotnet tool install Purview.Build --tool-path ./.tools --add-source ./artifacts --version 0.2.0
./.tools/purview-build
```

To publish packages built by a consumer to a local feed for development:

```shell
LOCAL_NUGET_FEED_PATH=p:/_sync-projects/.local-nuget/ ./.tools/purview-build --Release:Mode=LocalNuGet
```