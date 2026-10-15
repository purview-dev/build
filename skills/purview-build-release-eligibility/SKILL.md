---
name: purview-build-release-eligibility
description: >-
  Use when a Purview.Build release is skipped or fails a REL0nn rule, or when choosing a release
  model - release-on-main, main-as-head with a release branch, or per-line release branches. Covers
  the rules, the built-in policies, defining your own, and simulating an evaluation with
  Release:Context.
metadata:
  product: Purview.Build
  repository: purview-dev/build
---

# Release eligibility

Two layers, and keeping them apart is the design:

- **The workflow owns the decision.** It runs `release-explain`, reads the verdict, and sets
  `Release__Mode`.
- **The tool owns evaluation and execution.** `ReportEligibilityModule` evaluates and *reports* the
  verdict during a real run — it never declines a release.

```text
Trigger → resolve version → evaluate REL0nn → workflow reads the verdict → execute → publish, tag, release
```

Verdicts: **Release** (exit 0), **Skip** (exit 0 — the job finishes green, nothing published), or
**Fail** (exit 1, naming the rule).

## Checking before you push

```shell
dotnet purview-build release-explain
dotnet purview-build release-explain --format=json    # the contract the reusable workflow consumes
```

## The rules

They short-circuit **in ID order**. The first non-passing rule decides the verdict.

| Rule | Passes when | In the default policy |
| --- | --- | --- |
| `REL001` | the version parses for the configured strictness | yes |
| `REL002` | `v{version}` is not already tagged, and the version is not already on the target feed | yes |
| `REL003` | a stable (non-prerelease) version originates from a ref in `StableRefs` | yes |
| `REL004` | a non-zero `PATCH` originates from a ref matching `ServicingRefs` | no |
| `REL005` | the version is strictly greater than the highest existing version on the same line | no |
| `REL006` | a four-part version is permitted — `AllowFourPart`, and the ref matches `FourPartRefs` | no |
| `REL007` | the prerelease label matches the channel's allowed pattern | no |

`REL002` is the one that produces a **Skip** rather than a failure: an already-released version means
there is nothing to do, so merging `main` into `release` releases exactly once however many times it
happens.

**Only the rules a policy enables are evaluated.** A disabled rule is skipped entirely rather than
passing vacuously — which is why adopting a stricter policy can surface a problem that was always
there.

## Built-in policies

| Policy | Rules | Ref sets |
| --- | --- | --- |
| `ReleaseOnMain` (default) | `REL001`–`REL003` | `StableRefs`: `refs/heads/main`, `refs/heads/release` |
| `TrunkReservesMinor` | `REL001`–`REL005` | `StableRefs`/`ServicingRefs`: `refs/heads/release/*`; `TrunkRefs`: `refs/heads/main` |
| `FourPartServicing` | `TrunkReservesMinor` + `REL006` | as above, plus `AllowFourPart` and `FourPartRefs`: `refs/heads/release/*` |

## The three models

### Model A — release on `main`

Every merge to `main` may release. Trigger `push: branches: [main]`, policy `ReleaseOnMain`.

```text
PR → build, lint, test → merge to main → release-explain
    Release → pack, validate, publish, tag
    Skip (already tagged) → job succeeds, nothing published
    Fail (policy violation) → job fails, naming the REL0nn rule
```

### Model B — main-as-head with a release branch

Development merges to `main`; merging `main` into `release` performs the release. Trigger
`push: branches: [release]`, policy `ReleaseOnMain`.

### Model C — per-line release branches

Merging to `main` ships **nothing**. Work accumulates on trunk, and a release is cut from a
`release/<line>` branch, so a serviced stable line and an in-flight prerelease line advance
independently.

```text
main (trunk, reserves the minor)
 ├─ merge or cherry-pick → release/2.0 → v2.0.1, v2.0.2            (serviced stable line)
 └─ branch when the minor opens → release/2.1 → v2.1.0-prerelease.1, .2
```

Trunk itself is guarded: a stable version from `main` fails `REL003` (trunk is not a `StableRef`),
and a non-zero `PATCH` from `main` fails `REL004` (a patch is serviced from the line).

```yaml
on:
  push:
    branches: ['release/**']
  workflow_dispatch:

concurrency:
  # Callers own release serialisation; the reusable workflow defines no concurrency group.
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

## Defining a policy

`Release:Eligibility:Policies` is merged **over** the built-ins, so declaring a built-in's name
overrides it.

| Key | Meaning |
| --- | --- |
| `Inherits` | the policy this one is a diff from. A missing parent is an error naming the unresolved policy |
| `Rules` | either an **absolute** list (`["REL001","REL002"]`) or **deltas** against the parent (`["+REL006","-REL005"]`). **Mixing the two forms is an error.** |
| `StableRefs`, `ServicingRefs`, `TrunkRefs`, `FourPartRefs` | ref patterns. A single trailing `*` is a prefix match (`refs/heads/release/*`). A non-empty child list **replaces** the parent's, so a policy can narrow a ref set |
| `AllowFourPart` | whether four-part versions are permitted at all (`REL006`) |

## Simulating an evaluation

`Release:Context` lets you answer "what would happen on that branch?" without being on it:

| Key | Effect |
| --- | --- |
| `Ref` | the evaluated ref; defaults to `GITHUB_REF`, else the current git branch |
| `ExistingTags` | a path to a newline- or JSON-delimited tag list, used instead of querying git |
| `PublishedVersions` | a path to a version list treated as already on the feed, instead of querying it |

When a simulated context is in play, `ReportEligibilityModule`'s report says so — a simulated verdict
is never mistaken for a real one.

## Every failure here is a warning

Inside `ReportEligibilityModule`, every failure path is a **warning**. A misconfigured policy or an
unreadable simulated tag list must not fail a build that would otherwise succeed: the module
observes, and the workflow decides.

## Related skills

`purview-build-adoption` for the caller workflows, `purview-build-configuration` for the
`Release:*` settings, and `purview-build-pipeline` for where the release modules sit.
