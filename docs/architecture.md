# Architecture and configuration

## Decision

The shared artifact is a .NET tool NuGet package, not a reusable workflow and not an MSBuild SDK. Modular Pipelines is an executable orchestration system, so a tool is its natural package boundary. A tool manifest gives each consumer deterministic version pinning and Renovate/Dependabot-compatible upgrades. It also keeps GitHub Actions as a thin host; the same command runs locally, in GitHub Actions, or in another CI service.

An MSBuild SDK remains a possible future companion for shared compile-time properties, analyzers, or package metadata. It should not own CI orchestration.

The repository also exposes a thin reusable GitHub workflow. It contains no build policy: it authenticates to the organization feed, installs an exact `Purview.Build` version, and invokes the package. This reduces a consumer to one reusable-workflow job plus `purview-build.json` while the NuGet package remains the portable implementation boundary.

## Ownership boundary

The package owns module implementation, dependency ordering, safe defaults, secret lookup, NuGet/GitHub integration, and diagnostics. Each repository owns its tool-version pin, paths and discovery patterns, feature switches, and release-mode selection. A project needing truly custom behavior can invoke its own command before/after the shared tool; a generally useful variation should be added as a typed option here.

## Configuration reference

| Key | Default | Purpose |
|---|---|---|
| `Build:Solution` | `src` | Solution, project, or directory passed to restore/build |
| `Build:Configuration` | `Release` | .NET configuration |
| `Build:ArtifactsDirectory` | `artifacts` | Package output directory |
| `Build:Lint` | `true` | Restore local tools and run CSharpier check |
| `Build:Test` | `true` | Enable discovered tests |
| `Build:TestRoot` | `src/tests` | Test discovery root |
| `Build:TestPatterns` | `[*Tests.csproj]` | Recursive project search patterns; supports separate unit/integration naming |
| `Build:TestFilter` | `/*/*/*/*/` | TUnit tree-node filter; empty disables it |
| `Build:TestArguments` | `--ignore-exit-code 8` | Additional arguments after `dotnet test --` |
| `Build:Pack` | `true` | Enable packing |
| `Build:PackTarget` | `src` | Solution/project/directory to pack |
| `Build:VersionFile` | `package.json` | JSON file containing a SemVer `version` |
| `Release:Mode` | `None` | `None`, `LocalNuGet`, `NuGet`, or `GitHubRelease` |
| `Release:NuGetFeed` | nuget.org v3 | Remote package source |
| `Release:LocalFeed` | unset | Absolute local package source |
| `Release:CreateGitHubRelease` | `true` | Create a generated-notes release after NuGet publication |

`Release:NuGetApiKey` and `Release:GitHubToken` exist for configuration binding, but committed JSON must not contain them. Use `NUGET_API_KEY` and `GITHUB_TOKEN`.

Command-line overrides use configuration syntax, for example:

```shell
dotnet purview-build --Build:TestPatterns:0=*IntegrationTests.csproj --Build:Pack=false
```

Arrays use indexed keys in environment variables (`Build__TestArguments__0=--coverage`). For substantially different test types, select projects with the pattern/root and supply runner arguments; separate invocations may use different override sets.

## Release behavior

- `None`: build/test/pack may run, but nothing publishes.
- `LocalNuGet`: pushes packages to `Release:LocalFeed` for developer testing.
- `NuGet`: pushes packages to the configured feed and, by default, creates a GitHub release.
- `GitHubRelease`: creates a GitHub release without publishing NuGet packages.

The workflow should decide whether a version is eligible to release (for example, only an untagged version on `main`) and set `Release__Mode`. Credentials remain CI secrets.
