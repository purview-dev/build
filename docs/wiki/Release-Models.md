# Release Models

Diagrams for the three supported release trigger models. See [Release Flow](Release-Flow.md) for the
prose and [Configuration Reference](Configuration-Reference.md#releaseeligibility) for the rules.

> The diagrams below are **raw Mermaid source**, in fenced blocks marked `text`. The published site
> (`mkdocs.yml`) configures `pymdownx.superfences` without a Mermaid custom fence, so a `mermaid`
> block would render as unstyled text rather than a diagram. Paste a block into any Mermaid renderer
> to view it. If Mermaid support is added to `mkdocs.yml`, change these fences to `mermaid`.

## Model A — release on `main`

Every consuming repository uses this today. Each merge to `main` may release.

```text
gitGraph
    commit id: "1.0.0"
    branch feature/add-thing
    commit
    checkout main
    merge feature/add-thing tag: "v1.1.0"
    branch feature/fix-thing
    commit
    checkout main
    merge feature/fix-thing tag: "v1.1.1"
```

```text
flowchart LR
    PR[Pull request] -->|purview-build.yml| Gate[Build, lint, test]
    Gate --> Merge[Merge to main]
    Merge -->|push: branches main| Explain[purview-build release-explain]
    Explain -->|Release| Pipeline[Pack, validate, publish, tag]
    Explain -->|Skip: already tagged| Green[Job succeeds, nothing published]
    Explain -->|Fail: policy violation| Red[Job fails, names the REL0nn rule]
```

Policy: `ReleaseOnMain` (the default). `StableRefs` is `refs/heads/main` and `refs/heads/release`.

## Model B — main-as-head / release branch

Documented, and supported by the default policy. No repository uses it yet.

```text
gitGraph
    commit id: "1.0.0"
    branch release
    checkout main
    commit
    commit
    checkout release
    merge main tag: "v1.1.0"
    checkout main
    commit
    checkout release
    merge main tag: "v1.2.0"
```

Development merges to `main`; merging `main` into `release` performs the release. The caller
triggers on `push: branches: [release]`. Policy: `ReleaseOnMain`.

## Model C — per-line release branches

Merging to `main` ships nothing. Work accumulates, and a release is cut from a `release/<line>`
branch. A serviced stable line and an in-flight prerelease line advance independently.

```text
gitGraph
    commit id: "2.0.0"
    branch release/2.0
    checkout main
    commit id: "work"
    commit id: "more work"
    checkout release/2.0
    merge main tag: "v2.0.1"
    checkout main
    branch release/2.1
    commit id: "2.1 prep"
    checkout release/2.1
    commit tag: "v2.1.0-prerelease.1"
    checkout release/2.0
    commit tag: "v2.0.2"
    checkout release/2.1
    commit tag: "v2.1.0-prerelease.2"
```

```text
flowchart TB
    Main[main: trunk, reserves the minor] -->|merge, or cherry-pick| R20[release/2.0: serviced stable line]
    Main -->|branch when the minor opens| R21[release/2.1: in-flight prerelease line]
    R20 -->|push or workflow_dispatch| E20[release-explain]
    R21 -->|push or workflow_dispatch| E21[release-explain]
    E20 --> P20[Publish 2.0.2, tag v2.0.2, stable GitHub release]
    E21 --> P21[Publish 2.1.0-prerelease.2, tag it, GitHub prerelease]
    Main -.->|stable version from trunk| X[REL003 fails: trunk is not a StableRef]
    Main -.->|non-zero PATCH from trunk| Y[REL004 fails: patch is serviced from the line]
```

Policy: `TrunkReservesMinor` — `REL001`–`REL005`, with `StableRefs` and `ServicingRefs` both
`refs/heads/release/*` and `TrunkRefs` `refs/heads/main`.

Caller workflow:

```yaml
on:
  push:
    branches: ['release/**']
  workflow_dispatch:

concurrency:
  # Callers own release serialization; the reusable workflow defines no concurrency group.
  group: release-${{ github.ref }}
  cancel-in-progress: false

jobs:
  release:
    uses: purview-dev/build/.github/workflows/purview-release.yml@main
    with:
      release-mode: NuGet
      eligibility-policy: TrunkReservesMinor
    secrets: inherit
```

## Rule evaluation

Rules short-circuit in ID order. The first non-passing rule decides the verdict.

```text
flowchart TB
    Start[Version from the configured source] --> R1{REL001: parses for the strictness?}
    R1 -->|no| F1[Fail, exit 1]
    R1 -->|yes| R2{REL002: already tagged or published?}
    R2 -->|yes| S[Skip, exit 0 — nothing to do]
    R2 -->|no| R3{REL003: stable version from a StableRef?}
    R3 -->|no| F3[Fail, exit 1]
    R3 -->|yes| R4{REL004: non-zero PATCH from a ServicingRef?}
    R4 -->|no| F4[Fail, exit 1]
    R4 -->|yes| R5{REL005: greater than the line's highest?}
    R5 -->|no| F5[Fail, exit 1]
    R5 -->|yes| R6{REL006: four-part version permitted here?}
    R6 -->|no| F6[Fail, exit 1]
    R6 -->|yes| R7{REL007: label matches the channel pattern?}
    R7 -->|no| F7[Fail, exit 1]
    R7 -->|yes| Rel[Release, exit 0]
```

Only the rules a policy enables are evaluated; a disabled rule is skipped entirely rather than
passing vacuously. `ReleaseOnMain` evaluates `REL001`–`REL003` only, which is why Model A and
Model B repositories keep behaving exactly as they always have.

## Where each layer sits

```text
flowchart LR
    subgraph Workflow[Workflow layer — owns the decision]
        T[Trigger: on push / workflow_dispatch]
        D[Read the verdict, set Release__Mode]
    end
    subgraph Tool[Tool layer — owns evaluation and execution]
        V[Version: resolve the release units]
        E[Eligibility: evaluate REL0nn, report]
        X[Execution: clean, restore, build, test, lint, pack, validate]
        P[Publication: push packages, tag, GitHub release]
    end
    T --> V --> E --> D --> X --> P
```

## See also

- [Release Flow](Release-Flow.md)
- [Configuration Reference](Configuration-Reference.md)
- [Local Development](Local-Development.md)
- [Architecture](Architecture.md)
