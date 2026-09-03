# Migration: aspire-resourcekit

This repository currently contains a vendored copy of `build/PipelineCLI`. Migrate it as follows.

1. Replace the vendored `build/PipelineCLI` with the shared `Purview.Build` tool pinned to the chosen released version.
2. Add this `purview-build.json`:

```json
{
  "Build": {
    "Solution": "src/ResourceKit.slnx",
    "TestRoot": "src/tests",
    "TestPatterns": "*Tests.csproj",
    "TestFilter": "/*/*/*/*[Category=Unit]"
  },
  "Release": { "Mode": "None" }
}
```

3. Replace `pr.yml` and `release.yml` with thin callers of `purview-dev/build/.github/workflows/purview-build.yml` and `.../purview-release.yml`, passing `build-version`. Keep the `[Category=Unit]` filter in the release caller.
4. In the release caller set `release-mode: NuGet` and `secrets: inherit` (`NUGET_APIKEY` and `GITHUB_TOKEN` are read by the shared workflow).
5. Run the PR pipeline, then delete `build/PipelineCLI` and its pipeline-only central package declarations (`ModularPipelines*`, `NuGet.Packaging/Versioning`).

The old solution path and unit-test filter are preserved exactly. Other repositories migrate by changing only the JSON paths/patterns; for example `dotnet-project-sdk` can list unit and integration project globs in `Build:TestPatterns`.