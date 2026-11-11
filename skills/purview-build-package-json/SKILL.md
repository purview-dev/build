---
name: purview-build-package-json
description: >-
  Use when a Purview.Build package-json validation fails, or when bringing an existing root
  package.json up to standard - the four profiles (DotNetNuGet, DotNetSolution, WebPackage, WebApp),
  the PKG0nn rules and their per-profile severity, the package-validate command, and the JSON report.
metadata:
  product: Purview.Build
  repository: purview-dev/build
---

# package.json validation

Every consuming repository has a root `package.json`, even a .NET one: it is where the release version
lives. `ValidatePackageJsonModule` checks it against what a best-in-class package of the repository's
kind should declare, and **fails the run on any error-level finding** — this one is a gate, unlike
the release-eligibility report.

Two ways in:

| Invocation | Behaviour |
| --- | --- |
| `purview-build package-validate` | runs on its own, builds **no pipeline**, exits `0` on pass / `1` on fail |
| `PackageJson:Enabled=true` | adds the module to the pipeline, gated on `Version` |

`package-validate` needs no configuration and no build, so it is the cheapest PR gate: try it before
changing any settings.

## The profile is the whole design

One rule list cannot serve four kinds of repository. The **profile** decides which rules apply and, for
several of them, whether a miss is an error or a warning:

| Profile | With `Profile: Auto` | Meaning |
| --- | --- | --- |
| `DotNetNuGet` | `ProjectType=DotNet`, `RunPack=true` | .NET repo that packs NuGet package(s); the file is **not** published to npm |
| `DotNetSolution` | `ProjectType=DotNet`, `RunPack=false` | .NET repo that packs nothing |
| `WebPackage` | `ProjectType=Web`, file is not `private` | the file **is** published to npm |
| `WebApp` | `ProjectType=Web`, file is `private: true` | a web application, never published |

`Auto` derives the profile *from* `private`, so it can never see a contradiction between the two. The
only way to catch those is to declare `PackageJson:Profile` explicitly:

- a published npm package that wrongly says `private: true` → `WebPackage` + `private` → `PKG009` error;
- an application that forgot `private` entirely → `WebApp` without it → `PKG009` error.

A `.NET` file that is not `private` is only a warning: it is not an npm package, but a stray
`npm publish` at the root would still try to publish it.

## The rules

Rule IDs are permanent. Severity is stated per profile.

| Rule | Checks | Severity |
| --- | --- | --- |
| `PKG001` | `name` present, and a valid npm name (lowercase, url-safe, optional `@scope/`) | error when missing; error for `WebPackage`, warning elsewhere when npm would reject it |
| `PKG002` | `version` present, SemVer 2.0, not the `0.0.0` placeholder | error when missing/unparseable; error for `WebPackage`, warning elsewhere for a four-part version |
| `PKG003` | `description` present and substantive | error when missing; warning when under 10 characters or equal to the name |
| `PKG004` | `license` is an SPDX identifier or `SEE LICENSE IN <file>` | error when missing; warning for an unlisted identifier or `UNLICENSED` on a published package |
| `PKG005` | `repository` object with `type` and `url` | error when missing from a published file, warning elsewhere; warning for the shorthand string form |
| `PKG006` | `author` is a string, or an object with `name` | error when missing from a published file or empty, warning elsewhere |
| `PKG007` | `homepage` or `bugs.url` declared | warning |
| `PKG008` | `keywords` declared | warning; **skipped** when the file is not published |
| `PKG009` | `private` agrees with how the file is used | error for a private `WebPackage` or a publishable `WebApp`; warning for a `.NET` file without it |
| `PKG010` | `engines.node` range declared | error for `WebPackage`, warning for `WebApp`, skipped for `.NET` |
| `PKG011` | one of `main`/`module`/`exports`/`types`/`bin` declared | warning; `WebPackage` only |
| `PKG012` | the scripts the run drives are declared | error for the **build** script, warning for test/lint/format-check; skipped when the run drives none |
| `PKG013` | `files` declares the published contents | warning; `WebPackage` only |
| `PKG014` | each `PackageJson:AdditionalRequiredFields` entry is present and non-empty | error; skipped when the list is empty |
| `PKG000` | the file could not be read or parsed | error — never thrown, always reported |

Two behaviours worth remembering:

- **Every rule is reported, in ID order**, including `skipped` ones. One missing field never hides the
  rest of the findings, and a rule that did not apply is visibly absent rather than silently passing.
- **`PKG002` is deliberately profile-dependent.** `Version:Strictness` defaults to `NuGet`, which
  accepts four-part versions such as `13.5.3.10` — two consuming repositories ship them. npm accepts
  only SemVer 2.0, so the same value is fatal for a `WebPackage` and merely a warning for a `.NET` repo.
  Do not "fix" the warning by tightening `Version:Strictness`, which would stop those repositories
  releasing.

`PKG012` reads its script names from `Build:WebBuildCommand`, `Build:WebTestCommand`,
`Build:WebLintCommand` and `Build:WebFormatCheckCommand`, so overriding a command moves the check with
it.

## The report

`--format=json` emits the same shape whether it passed or failed, including when the file could not be
parsed, and `package-validate` exits `1` when `succeeded` is false:

```json
{
  "file": "/repo/package.json",
  "profile": "WebPackage",
  "succeeded": false,
  "exitCode": 1,
  "errorCount": 1,
  "warningCount": 1,
  "results": [
    { "ruleId": "PKG001", "outcome": "pass", "message": "Name '@contoso/core' is a valid npm package name." },
    { "ruleId": "PKG003", "outcome": "fail", "message": "The 'description' field is missing or empty. ..." }
  ],
  "errors": ["PKG003: The 'description' field is missing or empty. ..."],
  "warnings": ["PKG008: No 'keywords' are declared, ..."]
}
```

Read `errors`/`warnings` for a summary, `results` for which rules ran and which were skipped.
`PKG000` in `results` means the file was missing or malformed — everything else was never evaluated.

## Tuning, and how to adopt it

| Key | Effect |
| --- | --- |
| `PackageJson:Enabled` | adds the step to the pipeline; off by default |
| `PackageJson:Profile` | `Auto` (default), `DotNetNuGet`, `DotNetSolution`, `WebPackage`, `WebApp` |
| `PackageJson:DisabledRules` | `PKG0nn` IDs to skip, e.g. `["PKG008"]`. Unknown IDs are ignored |
| `PackageJson:AdditionalRequiredFields` | extra top-level fields this repository requires, e.g. `["funding"]` |
| `PackageJson:TreatWarningsAsErrors` | fail on a warning too, once the file is clean |

Starting from `Auto` on an existing repository usually reports warnings from `PKG007`, `PKG008` and
`PKG013` — that is intended, so adopting the check is not an instant gate. Fix them, or disable a rule
by ID and say why; do not relax a rule for every repository.

If a rule fires and looks wrong, the finding says which field it read and what it expected. A warning
about a **four-part version** on a `.NET` repository is expected and is not a reason to tighten
`Version:Strictness`.

## Related skills

`purview-build-configuration` for the `PackageJson` keys and the CLI verbs, `purview-build-pipeline`
for where `ValidatePackageJson` sits in the graph, and `purview-build-release-eligibility` for the
`REL0nn` version and ref policies, which are separate from `PKG0nn`.
