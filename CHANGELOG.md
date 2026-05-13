# Changelog

All notable changes to codekb will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial MVP of the Roslyn-based ingestion worker:
  - `codekb scan` — clones (or reads) a C# repository, walks `.sln` /
    `.csproj` / files via Roslyn, classifies files (production, test,
    generated, configuration), and emits normalized `CodeRecord` rows
    (`file_summary`, `class_summary`, `method_summary`,
    `feature_flag_usage`, `search_term_match`, `test_reference`,
    `configuration_reference`).
  - `codekb ask` — semantic search over the indexed code with filters
    (`--repo`, `--branch`, `--record-type`, `--feature-flag`, `--top-k`,
    `--min-score`) and text / JSON output.
- Embedding pipeline:
  - OpenAI and Azure OpenAI providers via `IEmbeddingClient`.
  - Batching with provider-aware caps and exponential-backoff retry.
- Storage:
  - PostgreSQL + `pgvector` with embedded migrations.
  - HNSW index on the vector column (IVFFlat fallback when HNSW is not
    available).
  - Idempotent insert by `(repo, file_path, symbol_name, record_type,
    line_start, commit_sha)`.
  - Stale-marking of records from previous scans of the same branch.
- Security:
  - Key-based and format-based secret redactor applied **before** storage
    and **before** the embedding API call.
  - `.env` value redaction.
  - YAML config loader rejects credential keys, forcing secrets through
    environment variables.
  - Drop-and-count behavior (`records_redaction_failed`) when a record
    cannot be safely redacted.
- Testing:
  - 263 unit tests across 6 projects, all in-process (no Postgres or
    network dependency).
  - 92.5 % line coverage on the global metric, with live-infrastructure
    code marked `[ExcludeFromCodeCoverage]`.
- Documentation:
  - Top-level `README.md`, `CONTRIBUTING.md`, `SECURITY.md`,
    `CODE_OF_CONDUCT.md`.
  - Spec-driven docs under `docs/requirements/` and
    `docs/specs/codekb-mvp/` (requirements, design, tasks).

[Unreleased]: https://github.com/Zoff-Tech/codekb/commits/main
