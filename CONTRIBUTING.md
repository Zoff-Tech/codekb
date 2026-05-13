# Contributing to codekb

Thanks for considering a contribution. codekb is an open code-knowledge ingestion worker, and we welcome bug reports, feature ideas, docs improvements, and pull requests.

By participating in this project you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to contribute

- **Report bugs** — open an issue using the bug-report template.
- **Suggest features** — open an issue using the feature-request template; describe the problem first, the proposed solution second.
- **Improve docs** — even a typo fix is a real contribution.
- **Submit code** — see the workflow below.

## Development workflow

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Git](https://git-scm.com/)
- (Optional, for integration work) PostgreSQL 16 with the [`pgvector`](https://github.com/pgvector/pgvector) extension. Use the `pgvector/pgvector:pg16` Docker image.

### Setting up

```bash
git clone https://github.com/Zoff-Tech/codekb.git
cd codekb
dotnet build
dotnet test
```

All 263 unit tests should pass with no external dependencies.

### Branching

Work in a feature branch off `main`:

```bash
git checkout -b feature/short-description
```

Use these prefixes:

- `feature/` — new functionality
- `fix/` — bug fixes
- `docs/` — documentation only
- `refactor/` — internal cleanup, no behavior change
- `test/` — adding or improving tests only

### Spec-driven changes

codekb follows a [spec-driven workflow](docs/specs/codekb-mvp/). Before making non-trivial behavior changes:

1. Update or add a requirement in [docs/specs/codekb-mvp/requirements.md](docs/specs/codekb-mvp/requirements.md) (EARS-style: WHEN/WHILE/IF … THEN … SHALL …).
2. Update the relevant section of [docs/specs/codekb-mvp/design.md](docs/specs/codekb-mvp/design.md).
3. Add a tickable line to [docs/specs/codekb-mvp/tasks.md](docs/specs/codekb-mvp/tasks.md).
4. Implement the change, referencing the requirement ID(s) (`Rx.y`) in the commit message.

Pure bug fixes and small refactors don't need the spec round-trip.

### Coding style

- Target framework: **.NET 8**, C# 12, `Nullable` enabled.
- Indent: **4 spaces**. EditorConfig is configured.
- Default to **no comments**. Add one only when *why* is non-obvious.
- Prefer composition over inheritance, interfaces over concrete dependencies.
- Keep CLI handlers thin — no business logic; all orchestration lives in `CodeKb.Core`.
- New public APIs should have at least one unit test exercising the happy path and one for the most likely failure mode.

### Tests

- Unit tests use [xUnit](https://xunit.net/) and [FluentAssertions](https://fluentassertions.com/).
- Tests must run **in-process**, with no network or database. If a feature needs a real Postgres, mark the impl `[ExcludeFromCodeCoverage]` and write a future integration test instead.
- Coverage target is **≥ 90%** on the global line metric (currently 92.5%). Don't regress it.

Run the full suite:

```bash
dotnet test codekb.sln
```

Run with coverage:

```bash
dotnet test codekb.sln --collect:"XPlat Code Coverage" --results-directory ./coverage
```

### Commit messages

Use a short imperative subject (≤ 70 chars), then a body that explains the *why*:

```
Add HNSW partial-index fallback for older pgvector versions

The 0.4.x pgvector release added HNSW; on 0.3.x we have to fall back to
IVFFlat. Detect the version on startup and skip the HNSW CREATE INDEX
statement if HNSW is unavailable, falling back to IVFFlat with a logged
warning. Refs R6.7, R12.2.
```

Reference requirement IDs (`R6.7`) and design sections (`D §10`) where applicable.

### Pull requests

1. Make sure `dotnet build` is clean (no warnings).
2. Make sure `dotnet test` passes locally.
3. Push your branch and open a PR against `main`.
4. Fill in the PR template — what changed, why, and how you tested it.
5. CI must be green before merge.

### Reviews

We aim to give a first review within 3 business days. Smaller PRs get reviewed faster — split work whenever you reasonably can.

## Bug reports

A good bug report has:

- What you expected to happen
- What actually happened
- A minimal reproduction (a tiny C# fixture, a config snippet, the exact CLI command)
- Versions: `dotnet --version`, Postgres + pgvector versions, codekb commit SHA
- Relevant logs (with secrets redacted — codekb tries to do this but double-check)

## Feature requests

Lead with the **problem** you're trying to solve, not the **solution**. The maintainers may have ideas that solve it more cleanly than the API you'd reach for first.

## Releasing (maintainers)

1. Update [CHANGELOG.md](CHANGELOG.md): move *Unreleased* items into a new `## [vX.Y.Z] — YYYY-MM-DD` section.
2. Bump version in tags only (no version field in csproj yet — MVP).
3. Open a release PR; once merged, tag `vX.Y.Z` on the merge commit.
4. GitHub Actions publishes the release notes from the changelog section.

Thanks for contributing!
