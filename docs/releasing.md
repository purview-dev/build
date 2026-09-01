# Versioning and release strategy

`Purview.Build` follows SemVer. The package version is the compatibility contract for configuration keys, defaults, module ordering, and tool behavior.

- Patch: fixes that preserve configuration and pipeline behavior.
- Minor: additive options or modules with backward-compatible defaults.
- Major: renamed/removed keys, changed defaults with material effects, or a required runtime upgrade.

Consumers pin an exact version in `.config/dotnet-tools.json`; never use a floating range. Automated dependency updates should open a pull request, where the consumer's normal build validates the new tool before merge. Keep the previous major supported while migrations are in progress.

The version is declared by the `Version` property in `src/Purview.Build/Purview.Build.csproj`. Releasing consists of bumping that property and merging the validated pull request into `main`.

CI performs locked restore, warnings-as-errors compilation, packing, installation from the generated package, and an end-to-end CLI smoke run. Only after that workflow succeeds on `main` does CD run. CD reads and validates the project version, skips it when `v{version}` already exists, publishes the immutable package to the Purview-Dev GitHub Packages NuGet registry, and creates `v{version}` plus a generated-notes GitHub release against the exact validated commit. The workflow therefore owns tagging; maintainers must not push release tags manually.

GitHub creates the package as private on its first publication. An organization owner must make the package Internal once in the package settings so Purview-Dev members can consume it, and must permit internal package creation in the organization's package policy. NuGet versions are immutable; `--skip-duplicate` makes recovery safe if publication succeeded but tagging was interrupted.

For local validation:

```shell
dotnet pack src/Purview.Build/Purview.Build.csproj -c Release -o artifacts
dotnet tool install Purview.Build --tool-path ./.tools --add-source ./artifacts --version 0.1.0
./.tools/purview-build
```
