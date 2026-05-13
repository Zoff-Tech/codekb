## What

Brief description of the change.

## Why

The motivation — bug being fixed, capability being added, debt being paid off.
Link to any related issue (`Fixes #123`) or requirement
(`R6.7`, `D §10`).

## How

A short description of the approach, plus any non-obvious decisions and the
alternatives you considered.

## Testing

- [ ] `dotnet build codekb.sln` is clean (no new warnings).
- [ ] `dotnet test codekb.sln` passes locally.
- [ ] Coverage didn't regress below 90 % on the global line metric.
- [ ] New behavior has a unit test covering the happy path **and** the most
      likely failure mode.
- [ ] If the change touches secret redaction, storage, or the embedding
      pipeline, a corresponding test asserts the security-sensitive
      invariant.

## Docs / spec

- [ ] `README.md` updated if user-visible behavior changed.
- [ ] `docs/specs/codekb-mvp/requirements.md` updated (or N/A — pure
      refactor / bug fix).
- [ ] `docs/specs/codekb-mvp/design.md` updated if the architecture
      changed.
- [ ] `docs/specs/codekb-mvp/tasks.md` checkbox ticked.
- [ ] `CHANGELOG.md` updated under `## [Unreleased]`.

## Checklist

- [ ] Commit messages follow the project style (imperative subject ≤ 70
      chars, body explains *why*).
- [ ] No secrets, no large binaries, no generated artifacts staged.
- [ ] CI is green.
