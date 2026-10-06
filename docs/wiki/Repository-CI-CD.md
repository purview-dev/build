# Repository CI/CD

This repository dogfoods the shared `Purview.Build` tool: it builds and packs the tool from source, installs the generated package, then runs `purview-build` against itself so the project builds and packs itself.

## CI (`ci.yml`)

Runs on pull requests and pushes to `main`:

1. Check out the repository.
2. Restore `src/Build.slnx`.
3. **Build gate**: `dotnet build src/Build.slnx --configuration Release --no-restore --warnaserror`.
4. Read and SemVer-validate the `package.json` version.
5. Pack the tool from source (`dotnet pack src/Build.slnx --configuration Release --no-build --output artifacts -p:Version=… -p:PackageVersion=…`).
6. Install the packed tool from the `artifacts` source into a temp tool path.
7. **Dogfood**: run the freshly installed `purview-build` against this repository (with `GITHUB_TOKEN`). The tool restores, builds, lints, runs tests, packs, and validates itself.

This repository's `purview-build.json` sets `Build:TestFilter` to `/*/*/*/*[Category=Unit]`, so the dogfood run executes the unit suites only. Categories come from the test project names — `Build.UnitTests` and `Build.IntegrationTests` — which the Purview SDK turns into assembly-level TUnit categories; no test writes a `[Category]` attribute by hand. Run the integration suites locally with `just test`.

## Release (`release.yml`)

Runs on push to `main` and is serialized by its own `concurrency` group (`purview-build-release`, `cancel-in-progress: false`):

1. Read the `package.json` version. This step is irreducible: the tool cannot read its own version for `dotnet pack` before it has been packed. It makes **no** eligibility decision.
2. Restore and build `src/Build.slnx` with `--warnaserror`.
3. Pack the tool from source with `-p:ContinuousIntegrationBuild=true`.
4. Verify the `NUGET__APIKEY` secret is set.
5. Install the packed tool.
6. **Evaluate eligibility** by running the freshly installed `purview-build release-explain --format=json` and reading the verdict. `Skip` finishes the job green, `Fail` fails it with the rule's message, and `Release` continues.
7. **Run the release pipeline** with `Release__Mode` taken from the evaluated decision, plus `NuGet__FeedUrl=https://api.nuget.org/v3/index.json`, `Release__UploadArtifacts=true`, `Build__RunTests=false`, `Build__RunLint=false`, and `Build__ValidatePack=true`, passing `GITHUB_TOKEN` and `NUGET_APIKEY`.

This repository dogfoods the eligibility evaluation as well as the pipeline: the `git rev-parse` tag check that used to live in this workflow is gone, replaced by the tool's own rules.

The tool therefore publishes the immutable package to nuget.org and tags and releases itself (`v{version}` + generated-notes GitHub release with the package attached) — exactly like every other purview-dev repository. Maintainers bump the `package.json` version and merge; they do not create release tags manually.

## GitHub package visibility

GitHub initially creates NuGet packages as private. An organization owner should set the org default to **Internal** (Purview-Dev → Settings → Packages → **Package Creation** → **Internal**) and change any already-published package's visibility in its **Package settings** → **Danger Zone**. See [Release Flow](Release-Flow.md) for the exact steps and the `gh api` alternative.

## See also

- [Release Flow](Release-Flow.md)
- [Getting Started](Getting-Started.md)
- [Architecture](Architecture.md)