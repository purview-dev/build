# package.json Validation

Every consuming repository has a root `package.json`, even a .NET one: it is where the release
version lives. This check holds that file to the content a best-in-class package of the repository's
kind is expected to declare — a name, a version, a description, a licence, and the details a
consumer, a registry and a maintainer need.

It is **opt-in in the pipeline and always available as a command**:

| Invocation | Behaviour |
| --- | --- |
| `purview-build package-validate` | Runs the check on its own, runs no module, and exits `0` when it passes, `1` when it does not. Needs no configuration. |
| `purview-build package-validate --format=json` | The same check, as the JSON contract below, for `jq` or a workflow step. |
| `PackageJson:Enabled=true` | Adds `ValidatePackageJsonModule` to the pipeline, so a failing file fails the build. |

Because it reads one file, it is the cheapest gate to add to a PR: it needs no build, no restore and
no network.

## The profile decides which rules apply

The rules are not one list. A published npm package, an application that must never be published, a
.NET library that ships NuGet packages and a .NET repository that packs nothing all need different
things from the same file. That is the **profile**:

| Profile | Resolved when (with `Profile: Auto`) | What it means |
| --- | --- | --- |
| `DotNetNuGet` | `Build:ProjectType=DotNet` and `Build:RunPack=true` | A .NET repository that packs NuGet package(s). The file is not published to npm |
| `DotNetSolution` | `Build:ProjectType=DotNet` and `Build:RunPack=false` | A .NET repository that packs nothing |
| `WebPackage` | `Build:ProjectType=Web` and the file is not `private` | A Web repository whose file is published to npm |
| `WebApp` | `Build:ProjectType=Web` and the file is `private: true` | A Web application, which is never published |

`PackageJson:Profile` overrides the derivation. That is the only way to express the two cases Auto
cannot: a published npm package that wrongly says `private: true`, and an application that has
forgotten to say it at all — Auto derives the profile *from* that flag, so the contradiction only
exists once a profile is declared.

## Rules

Rule IDs are permanent: never renumbered, never repurposed. Severity depends on the profile, which is
the whole point — the same defect is fatal for a published package and advisory for a private one.

| Rule | Checks | Severity |
| --- | --- | --- |
| `PKG001` | `name` is present, and valid for npm (lowercase, url-safe, optional `@scope/`) | error when missing; error for `WebPackage`, warning elsewhere when npm would reject it |
| `PKG002` | `version` is present, parses as SemVer 2.0, is not the `0.0.0` placeholder, and is the version the pipeline resolved | error when missing or unparseable; error for `WebPackage`, warning elsewhere for a four-part version npm rejects (`Version:Strictness=NuGet` accepts it) |
| `PKG003` | `description` is present and says something | error when missing; warning when shorter than 10 characters or identical to the name |
| `PKG004` | `license` is an SPDX identifier or `SEE LICENSE IN <file>` | error when missing; warning for an unlisted identifier, or for `UNLICENSED` on a published package |
| `PKG005` | `repository` is an object with `type` and `url` | error when missing from a published file, warning elsewhere; warning for the shorthand string form or a missing field |
| `PKG006` | `author` is a string, or an object with `name` | error when missing from a published file or empty, warning elsewhere |
| `PKG007` | `homepage` or `bugs.url` is declared | warning |
| `PKG008` | `keywords` are declared | warning; skipped when the file is not published |
| `PKG009` | `private` agrees with what the repository does with the file | error for a private `WebPackage` or a publishable `WebApp`; warning for a `.NET` file that is not `private` |
| `PKG010` | `engines.node` declares the supported Node range | error for `WebPackage`, warning for `WebApp`, skipped for `.NET` |
| `PKG011` | one of `main`/`module`/`exports`/`types`/`bin` is declared | warning; `WebPackage` only |
| `PKG012` | the scripts this run drives are declared. The build command's script is required; the test, lint and format-check scripts are named | error for a missing build script, warning for the rest; skipped when the run drives none |
| `PKG013` | `files` declares the published tarball's contents | warning; `WebPackage` only |
| `PKG014` | every field named by `PackageJson:AdditionalRequiredFields` is present and non-empty | error; skipped when the list is empty |
| `PKG000` | the file could not be read or parsed | error — reported in the same shape as any other finding, never thrown |

`PKG012` reads the script names out of `Build:WebBuildCommand`, `Build:WebTestCommand`,
`Build:WebLintCommand` and `Build:WebFormatCheckCommand`, so overriding a command moves the check
with it.

## The JSON contract

`--format=json` emits the same shape whether the file passed or failed, including for a file that
could not be parsed:

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

`outcome` is one of `pass`, `warning`, `fail` or `skipped`. Every rule is reported, in ID order,
whether or not it applied — so a skipped rule is visibly skipped rather than missing.

## Tuning

| Key | Effect |
| --- | --- |
| `PackageJson:Enabled` | Adds the step to the pipeline. Off by default |
| `PackageJson:Profile` | `Auto` (default), `DotNetNuGet`, `DotNetSolution`, `WebPackage`, `WebApp` |
| `PackageJson:DisabledRules` | Rule IDs to skip, e.g. `["PKG008"]`. Unknown IDs are ignored |
| `PackageJson:AdditionalRequiredFields` | Extra top-level fields the repository requires, e.g. `["funding"]` |
| `PackageJson:TreatWarningsAsErrors` | Makes a warning fail the run, once the file is clean |

A repository that has decided a rule does not apply should disable it by ID and say why, rather than
relaxing the check for everyone. A first run on an existing file is likely to report warnings from
`PKG007`, `PKG008` and `PKG013`: they are advisory precisely so that adopting the check does not
become a gate on day one.

## See also

- [Configuration Reference](Configuration-Reference.md)
- [Pipeline Modules](Pipeline-Modules.md)
- [Pack Validation](Pack-Validation.md)
