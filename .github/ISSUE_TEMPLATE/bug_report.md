---
name: Bug report
about: Report something that's broken or behaving unexpectedly
title: "[bug] "
labels: bug
assignees: ''
---

## What happened

A clear description of the bug.

## What you expected to happen

What did you think `codekb` should have done instead?

## Reproduction

Minimal, copy-pasteable steps to reproduce the bug:

1. ...
2. ...
3. ...

If the bug involves a specific repository or C# fixture, include the smallest
snippet that triggers it (or a link to a public repo / branch).

## Command and config

```bash
# the exact command you ran
codekb scan --repo ... --branch ...
```

```yaml
# relevant portion of config/codekb.yaml (REDACT any secrets)
```

## Logs

```
# relevant log output (REDACT any secrets — codekb tries to but double-check)
```

## Environment

- `dotnet --version`:
- OS:
- PostgreSQL version:
- `pgvector` version:
- codekb commit SHA:
- Embedding provider and model:

## Additional context

Anything else that might help — screenshots, related issues, recent changes
to your environment.
