# Migration: aspire-resourcekit

This repository currently contains the representative copied implementation at `build/PipelineCLI`. Migrate it as follows.

1. Add and commit a tool manifest with `Purview.Build` pinned to the chosen released version.
2. Add this `purview-build.json`:

```json
{
  "Build": {
    "Solution": "src/ResourceKit.slnx",
    "ArtifactsDirectory": "artifacts",
    "TestRoot": "src/tests",
    "TestPatterns": ["*Tests.csproj"],
    "TestFilter": "/*/*/*/*[Category=Unit]",
    "PackTarget": "src/ResourceKit.slnx"
  },
  "Release": { "Mode": "None" }
}
```

3. Replace `dotnet run --project build/PipelineCLI/PipelineCLI.csproj --configuration Release` in both workflows with `dotnet tool restore` followed by `dotnet purview-build`.
4. In the release job set `Release__Mode=NuGet`, `NUGET_API_KEY`, and `GITHUB_TOKEN`. Keep the existing untagged-version guard until it is generalized here.
5. Run the PR pipeline, then delete `build/PipelineCLI` and its pipeline-only central package declarations.

The old solution path and unit-test filter are preserved exactly. Other repositories migrate by changing only the JSON paths/patterns; for example `dotnet-project-sdk` can list unit and integration project globs in `Build:TestPatterns`.
