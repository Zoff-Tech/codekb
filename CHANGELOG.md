# Changelog

All notable changes to codekb will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Database migrations now run automatically on `codekb scan`.** A new
  `IDatabaseInitializer` is injected into `ScanService` and invoked before
  any store operation. With Postgres configured, it resolves to
  `MigrationRunner`, which applies the embedded `001_init.sql` and
  `002_indexes.sql` exactly once per process (tracked in a `_migrations`
  table; subsequent scans are no-ops). When no connection string is
  configured, a `NullDatabaseInitializer` is used. Previously the
  `MigrationRunner` existed but had no production call sites, so users
  had to apply the SQL manually against a fresh database.

### Removed

- **Dedicated feature-flag feature.** Removed the `FeatureFlagDetector`, the
  `RecordType.FeatureFlagUsage` record type, the `FeatureFlagUsageType`
  enum, the `CodeRecord.FeatureFlagName` and `CodeRecord.UsageType` fields,
  the `feature_flag_name` / `usage_type` columns and feature-flag index on
  `code_record`, the `--feature-flag` CLI option on `ask`,
  `SearchRequest.FeatureFlag`, the `FeatureFlagMatches` counter on
  `ScanCounters` / `ScanJobOutcome`, and the
  `featureFlagMethodNames` / `featureFlagClientNames` YAML settings.
  - **Migration path:** feature-flag keys are still fully discoverable
    through the embedding pipeline — they appear inside method code
    snippets, are split by the identifier tokenizer (so `EnableNewWorkflow`
    splits into `Enable` + `New` + `Workflow`), and can be tracked
    explicitly with `codekb scan --search <flag-name>` to get a dedicated
    `search_term_match` record. Retrieve them with
    `codekb ask "EnableNewWorkflow"` — no filter required.
- Test count: **278** (was 299) after removing the 16 `FeatureFlagDetector`
  unit tests and 5 feature-flag-specific tests in other projects.

### Added

- **Full Roslyn syntactic coverage.** The extractor now emits records for
  constructors (instance + static), destructors, indexers, operators,
  conversion operators, fields (one record per variable declarator), events
  (both property-style and field-style), enum members, top-level delegates,
  local functions (with their enclosing method recorded), and nested types
  — on top of the existing classes / interfaces / records / structs / enums /
  methods / properties.
- **Per-method call graph.** Methods, constructors, and local functions now
  carry `calls: [...]` (invocation targets) and `instantiates: [...]`
  (object-creation targets) in their `MetadataJson`, capped at 50 each and
  deduplicated. Extraction is syntactic only — cross-file semantic resolution
  remains a future enhancement.
- **Per-file dependencies.** `file_summary` records now include
  `using_directives` and `external_types` (base types, return types,
  parameter types, field types, instantiation targets).
- **Project & solution scanner.** New `ProjectScanner` parses `.csproj`
  (SDK, target framework(s), root namespace, assembly name, lang version,
  nullable, implicit usings, all `PackageReference` and `ProjectReference`
  items) and `.sln` files (project list). A `file_summary` record is
  emitted per project / solution with `kind: project` / `kind: solution` in
  metadata.
- **Embedding-time identifier tokenization.** A new `IdentifierTokenizer`
  splits PascalCase / camelCase (with acronym handling — `XMLParser` →
  `XML`, `Parser`), kebab-case (`process-payment` → `process`, `payment`),
  snake_case (`is_enabled` → `is`, `enabled`), dotted names, and path
  segments. `EmbeddingTextBuilder` adds a `Tokens:` line populated from
  `Namespace`, `ClassName`, `MethodName`, `SymbolName`, and the file
  basename, so a query like *"account"* matches
  `PrePaidAccount`.
- 36 new unit tests across the new functionality. Total test count:
  **299** (was 263), still all in-process with no Postgres or network
  dependency.
- Initial MVP of the Roslyn-based ingestion worker:
  - `codekb scan` — clones (or reads) a C# repository, walks `.sln` /
    `.csproj` / files via Roslyn, classifies files (production, test,
    generated, configuration), and emits normalized `CodeRecord` rows
    (`file_summary`, `class_summary`, `method_summary`,
    `search_term_match`, `test_reference`, `configuration_reference`).
  - `codekb ask` — semantic search over the indexed code with filters
    (`--repo`, `--branch`, `--record-type`, `--top-k`, `--min-score`)
    and text / JSON output.
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
