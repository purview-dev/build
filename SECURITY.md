# Security policy

This repository is the organisation's **shared build and release pipeline**: a .NET tool
(`Purview.Build`), a composite action, and the reusable workflows every purview-dev repository
calls. A defect here executes in the CI of all of them, with their tokens and their publishing
credentials. Treat it as the highest-value target in the organisation.

## Reporting a vulnerability

**Do not open a public issue for a security report.**

Use GitHub's private reporting:
[Report a vulnerability](https://github.com/purview-dev/build/security/advisories/new).

Please include the workflow or tool version, the inputs involved, and what an attacker would gain.
You can expect an acknowledgement within 5 working days.

## Scope

Particularly in scope:

- **Script injection** into a `run:` body through a `workflow_call` input, a branch or tag name, or
  any other value a calling repository controls. Caller inputs are passed through `env:` and
  referenced as shell variables for exactly this reason; an interpolated `${{ }}` inside a `run:`
  body is a finding.
- **`$GITHUB_ENV` or `$GITHUB_OUTPUT` injection** — a value containing a newline that defines
  further variables for later steps. Writes use a random heredoc delimiter.
- **Credential exposure**: a publishing secret readable by a step that does not publish, forwarded
  to a fork pull request, or printed to the log.
- **Privilege escalation through a floating reference**: an unpinned action, or a tool version
  resolved at job time rather than pinned by the caller.

Out of scope:

- An attack that requires write access to this repository.
- A consuming repository granting its own job more permission than it needs.

## Commitments

- **Third-party actions are pinned to a commit SHA**, with the tag in a trailing comment.
  Dependabot keeps them current, so pinning does not become staleness.
- **Caller-controlled input never reaches a shell as source text.** It is passed through `env:`.
- **Publishing credentials are scoped to the publishing step**, not exported at job level.
- **Trusted Publishing is supported** (`trusted-publishing: true` plus `nuget-user`), exchanging a
  GitHub OIDC token for a nuget.org key valid for one hour. Prefer it to a stored API key.
- **`build-version` should be pinned by every caller.** Left empty, the workflow installs and
  executes whatever is latest on nuget.org at job time; it now emits a warning when that happens.

## A note on same-repository action references

The reusable workflows reference this repository's own composite actions as
`purview-dev/build/actions/<name>@main`. A caller that pins the *workflow* to a SHA therefore
still picks up a floating reference to those actions. This is a known gap: a reusable workflow
cannot resolve `./` against its own repository, and it cannot self-reference a SHA that does not
exist until after the commit. It will be closed by referencing a release tag once this repository
tags its releases. Until then, the blast radius is the same as the workflow itself — a compromise
of this repository compromises both.
