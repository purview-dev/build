# Repository CI/CD

This repository dogfoods the shared `Purview.Build` tool: it builds and packs the tool from source, installs the generated package, then runs `purview-build` against itself so the project builds and packs itself.

## CI (`ci.yml`)

Runs on pull requests and pushes to `main`:

1. Check out the repository.
2. Set up .NET and Bun.
3. **Configure NuGet sources** with the shared `./actions/nuget-sources` action, which is the
   same implementation the reusable workflows use. It registers nuget.org and the organisation GitHub
   Packages feed for the job. The workflow declares `packages: read`, and `GITHUB_TOKEN` is job-level so
   the `%GITHUB_TOKEN%` credential can be resolved.
4. Restore `src/Build.slnx`.
5. **Build gate**: `dotnet build src/Build.slnx --configuration Release --no-restore --warnaserror`.
6. Read and SemVer-validate the `package.json` version.
7. Pack the tool from source (`dotnet pack src/Build.slnx --configuration Release --no-build --output artifacts -p:Version=… -p:PackageVersion=…`).
8. Install the packed tool from the `artifacts` source into a temp tool path.
9. **Dogfood**: run the freshly installed `purview-build` against this repository. The tool restores, builds, lints, runs tests, packs, and validates itself.
10. Remove the temporary NuGet sources (`if: always()`).

This repository's `purview-build.json` sets `Build:TestFilter` to `/*/*/*/*/`, so the dogfood run executes **every** suite — unit and integration alike. Categories still come from the test project names — `Build.UnitTests` and `Build.IntegrationTests` — which the Purview SDK turns into assembly-level TUnit categories; no test writes a `[Category]` attribute by hand. Narrow to the unit suites locally with `just test-unit`.

The filter used to be `/*/*/*/*[Category=Unit]`, which quietly excluded the integration suites from every PR and from the release. A suite that only ever runs on a developer's machine is not a gate, so the filter now matches what `just test` runs.

## Release (`release.yml`)

Runs on push to `main` and is serialized by its own `concurrency` group (`purview-build-release`, `cancel-in-progress: false`):

1. Read the `package.json` version. This step is irreducible: the tool cannot read its own version for `dotnet pack` before it has been packed. It makes **no** eligibility decision.
2. Configure NuGet sources with the shared `./actions/nuget-sources` action (nuget.org + the organisation feed).
3. Restore and build `src/Build.slnx` with `--warnaserror`.
4. Pack the tool from source with `-p:ContinuousIntegrationBuild=true`.
5. Verify the `NUGET__APIKEY` secret is set.
6. Install the packed tool.
7. **Evaluate eligibility** by running the freshly installed `purview-build release-explain --format=json` and reading the verdict. `Skip` finishes the job green, `Fail` fails it with the rule's message, and `Release` continues.
8. **Run the release pipeline** with `Release__Mode` taken from the evaluated decision, plus `NuGet__FeedUrl=https://api.nuget.org/v3/index.json`, `Release__UploadArtifacts=true`, `Build__RunTests=false`, `Build__RunLint=false`, and `Build__ValidatePack=true`, passing `GITHUB_TOKEN` and `NUGET_APIKEY`. The workflow maps `PURVIEW_PUBLISH_GITHUB_PACKAGES` onto `NuGet__GitHubPackages__Enabled` and passes the repository owner, so setting the variable makes this repository dual-publish; leaving it unset keeps NuGet.org-only publication.
9. **Prune older GitHub Packages versions** when the variable is set: keep the newest
   `PURVIEW_GITHUB_PACKAGES_KEEP_VERSIONS` (default 1), never the version just released. Best effort —
   NuGet.org keeps every version.
10. Remove the temporary NuGet sources (`if: always()`).

The workflow declares `contents: write` and `packages: write`. This repository dogfoods the eligibility evaluation as well as the pipeline: the `git rev-parse` tag check that used to live in this workflow is gone, replaced by the tool's own rules.

The tool therefore publishes the immutable package to nuget.org (and, when opted in, GitHub Packages) and tags and releases itself (`v{version}` + generated-notes GitHub release with the package attached) — exactly like every other purview-dev repository. Maintainers bump the `package.json` version and merge; they do not create release tags manually.

## GitHub package visibility

GitHub creates NuGet packages as **private** on first publication. Workflow access is independent of
visibility: the organisation-level `PURVIEW_PACKAGES_READ_TOKEN` secret lets every repository read
private packages too. For people, an organization owner can change a package to **Internal** in its
**Package settings** → **Danger Zone** (per package, UI only — there is no REST or GraphQL endpoint).
See [Release Flow](Release-Flow.md#visibility) for the detail.

## See also

- [Release Flow](Release-Flow.md)
- [Getting Started](Getting-Started.md)
- [Architecture](Architecture.md)