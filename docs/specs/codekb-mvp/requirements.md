# Requirements — codekb MVP

Refined from [docs/requirements/codekb.md](../../requirements/codekb.md). Acceptance criteria use EARS notation: WHEN/WHILE/IF [condition] THEN [system] SHALL [behavior].

## Introduction

codekb is a Roslyn-based code ingestion worker that scans C# repositories, extracts normalized code records, generates embeddings, and stores them in Postgres + pgvector so they can be searched semantically. The MVP is CLI-only and supports one primary workflow: ingest a repo, then ask questions about it.

Out of scope for this spec: OmniSharp, LangGraph orchestration, automatic code modification, multi-language support, full dependency-graph generation, Jira integration, Kubernetes hard dependency, cross-language migration.

---

## Requirement 1 — Repository Ingestion (`codekb scan`)

**User story.** As a developer, I want to point `codekb scan` at a C# repository (remote URL or local path) on a given branch so that its code is indexed and searchable.

### Acceptance criteria

1.1. WHEN the user runs `codekb scan --repo <url> --branch <name>` THEN the system SHALL perform a shallow clone (`--depth 1`) of `<url>` at `<name>` into a working directory.

1.2. WHEN the user runs `codekb scan --path <dir>` THEN the system SHALL read the repository from `<dir>` without performing any fetch or checkout.

1.3. WHEN `--repo` and `--path` are both supplied THEN the system SHALL reject the invocation with a non-zero exit code and an explanatory error.

1.4. WHEN `--branch` is omitted with `--repo` THEN the system SHALL scan the repository's default branch.

1.5. WHEN the target repository is private AND `GIT_TOKEN`/`GIT_USERNAME` are set in the environment THEN the system SHALL authenticate using those credentials.

1.6. WHEN the repository URL is an SSH URL THEN the system SHALL delegate authentication to the operator's `ssh-agent`.

1.7. IF Git credentials appear in the YAML config file THEN the system SHALL ignore them and log a warning. Credentials SHALL be read only from environment variables or ssh-agent.

1.8. WHEN a clone or pull fails THEN the system SHALL record `status = failed` on the `scan_job` row, exit non-zero, and SHALL NOT print credentials or tokens in any log line.

1.9. WHEN a scan completes successfully THEN the system SHALL persist the captured commit SHA, repository name, and branch on the `repository` and `scan_job` rows.

1.10. WHEN the resolved `(repo, branch, commit_sha)` is already indexed AND `--force` is not set THEN the system SHALL exit with status 0 and a "no-op, already indexed" message without re-scanning.

1.11. WHEN a previous scan for the same `(repo, branch, commit_sha)` was interrupted THEN re-running `codekb scan` without `--force` SHALL resume by inserting only records that don't already exist for that commit (idempotent via the unique index in §6.3).

---

## Requirement 2 — Roslyn Scanning

**User story.** As a developer, I want the scanner to load C# source via Roslyn so that classes, methods, and references are recognized as structured units rather than text.

### Acceptance criteria

2.1. WHEN a `.sln` file is present at the repo root THEN the scanner SHALL load it via `MSBuildWorkspace`.

2.2. IF no `.sln` is present THEN the scanner SHALL discover `.csproj` files and load each individually.

2.3. IF neither `.sln` nor `.csproj` is present THEN the scanner SHALL fall back to syntax-only parsing of every `.cs` file under the repo root.

2.4. WHEN project references cannot be resolved (missing SDK, missing NuGet) THEN the scanner SHALL degrade to syntax-only parsing for that project, log a warning, and continue.

2.5. WHEN a single file fails to parse THEN the scanner SHALL skip the file, record the failure on `scan_job.records_failed`, and continue with remaining files.

2.6. WHEN a file's size exceeds `scanner.maxFileSizeKB` THEN the scanner SHALL skip it and log the skip with file path and size.

2.7. WHILE scanning THEN the scanner SHALL skip any path matching `scanner.ignorePaths` (default: `bin`, `obj`, `.git`, `node_modules`, `packages`).

2.8. WHILE scanning THEN the scanner SHALL extract: classes, interfaces, records, enums, methods, properties, and fields.

2.9. WHILE scanning THEN the scanner SHALL classify each file as production, test, generated, or configuration per §9 of the requirements.

---

## Requirement 3 — Normalized Code Records

**User story.** As a downstream consumer (search, future agents), I want code findings as a stable normalized schema so that storage, search, and embedding don't depend on Roslyn types.

### Acceptance criteria

3.1. WHEN the scanner emits a record THEN the record SHALL be one of: `file_summary`, `class_summary`, `method_summary`, `feature_flag_usage`, `search_term_match`, `test_reference`, `configuration_reference`.

3.2. WHEN a record is created THEN it SHALL include `repository_id`, `scan_job_id`, denormalized `repository_name`/`branch`/`commit_sha`, `file_path`, `line_start`, `line_end`, `record_type`, and `metadata_json` shaped per §6.3 of the requirements doc.

3.3. WHEN a `file_summary` record is created THEN `line_start` SHALL be 1 and `line_end` SHALL be the file's last line.

3.4. WHEN any record is created THEN `line_start` and `line_end` SHALL be 1-indexed and inclusive.

3.5. WHEN a method-level snippet is captured THEN the snippet SHALL contain the full method body up to 200 lines or 4 KB (whichever first), truncated at a statement boundary.

3.6. WHEN a class-level snippet is captured THEN the snippet SHALL contain the class declaration plus member signatures (no method bodies), up to 4 KB.

3.7. WHEN a feature-flag or search-term snippet is captured THEN the snippet SHALL be ±20 lines around the match, clamped to the enclosing method or block.

3.8. WHEN a file is classified as test or generated THEN `is_test_code` / `is_generated_code` SHALL be set on every record produced from that file.

---

## Requirement 4 — Feature Flag Detection

**User story.** As a developer, I want feature flag usages detected reliably so that I can answer "where is flag X used."

### Acceptance criteria

4.1. WHEN a string literal exactly matches a configured search term OR a flag name discovered as a constant THEN the scanner SHALL emit a `feature_flag_usage` record with `usage_type = "constant_definition"` (for `const string X = "X";`) or `usage_type = "runtime_branch"` (for in-code literal use).

4.2. WHEN a method invocation is encountered AND the method name is in `scanner.featureFlagMethodNames` AND the receiver's declared type (or one of its interfaces/base types) is in `scanner.featureFlagClientNames` THEN the scanner SHALL emit a `feature_flag_usage` record with `usage_type = "runtime_branch"`.

4.3. IF the method name matches but the receiver type does not THEN the scanner SHALL NOT emit a flag record (avoids false positives on generic `IsEnabled`).

4.4. WHEN a configuration file (extension `.json`/`.yaml`/`.yml`/`.xml`/`.env`, or path matching `appsettings*`) contains a key under a section whose name contains `Feature`/`Flag`/`Toggle` (case-insensitive) THEN the scanner SHALL emit a `configuration_reference` record for that key, regardless of the value.

4.5. WHEN a `--search <term>` flag matches a key in any config file THEN the scanner SHALL emit a `configuration_reference` record, independent of the section-name heuristic.

4.6. WHEN a `.env` file is matched THEN the value SHALL be redacted to `«REDACTED»` before storage (per Requirement 8).

---

## Requirement 5 — Search Term Matching (`--search`)

**User story.** As a developer, I want named search terms tracked explicitly so that arbitrary identifiers (not just flags) get first-class records.

### Acceptance criteria

5.1. WHEN `--search <term>` is supplied THEN the scanner SHALL accept the flag repeatably.

5.2. WHILE scanning source files AND a token matching `<term>` is found in code (identifier, type name, member name) THEN the scanner SHALL emit a `search_term_match` record with `match_kind = "identifier"`. Identifier matching SHALL be case-sensitive.

5.3. WHILE scanning string literals THEN matching SHALL be case-insensitive and the term SHALL appear as a whole word (`\b`-bounded). `match_kind` SHALL be `"string_literal"`.

5.4. WHILE scanning comments and XML doc comments THEN matching SHALL be case-insensitive and `\b`-bounded. `match_kind` SHALL be `"comment"` or `"xml_doc"` respectively.

5.5. IF a match would be only a substring of a larger token in code THEN the scanner SHALL NOT emit a record.

---

## Requirement 6 — Embedding Generation

**User story.** As a developer, I want each code record embedded so that semantic search works.

### Acceptance criteria

6.1. WHEN a record is ready for embedding THEN the system SHALL build embedding text using the template in §11.1, omitting empty fields (not leaving them blank).

6.2. WHEN embedding text is built THEN it SHALL include the (already-redacted) `code_snippet` and never include any raw secret.

6.3. WHEN issuing embedding requests THEN the system SHALL batch up to the provider's per-call input limit and SHALL bound concurrent in-flight batches to `scanner.parallelism`.

6.4. WHEN an embedding API call fails THEN the system SHALL retry with exponential backoff using `embedding.maxRetries` and `embedding.retryBackoffSeconds`.

6.5. IF retries are exhausted THEN the system SHALL persist the `code_record` with `embedding_status = "failed"` and no vector, so a later run can retry without rescanning.

6.6. WHEN persisting an embedding THEN the system SHALL record `embedding_model`, `embedding_model_version`, `embedding_dimension`, `embedding_vector`, and `embedding_text`.

6.7. WHEN the worker starts THEN it SHALL verify that `embedding.dimension` matches the `vector(N)` width declared on `code_embedding.embedding_vector` and SHALL fail fast with a clear error otherwise.

---

## Requirement 7 — Semantic Search (`codekb ask`)

**User story.** As a developer, I want to ask a natural-language question and get the most relevant code records back.

### Acceptance criteria

7.1. WHEN the user runs `codekb ask "<question>"` THEN the system SHALL embed `<question>` using the currently configured model and perform vector similarity search.

7.2. WHEN no `--repo` filter is supplied THEN the system SHALL search across all indexed repositories.

7.3. BY DEFAULT the system SHALL exclude rows with `is_stale = true` and rows whose `embedding_model` differs from the currently configured one.

7.4. WHEN `--include-stale` is set THEN stale rows SHALL be included.

7.5. WHEN `--include-other-models` is set THEN rows from other embedding models SHALL be included.

7.6. WHEN `--top-k <n>` is supplied THEN the system SHALL return at most `<n>` results (default 10).

7.7. WHEN `--min-score <f>` is supplied THEN the system SHALL drop results with similarity below `<f>`.

7.8. WHEN `--feature-flag <name>` is supplied THEN the system SHALL filter to `record_type = "feature_flag_usage"` AND `feature_flag_name = <name>`.

7.9. WHEN `--record-type <t>` is supplied (repeatable) THEN the system SHALL filter to the union of those record types.

7.10. WHEN `--format json` is supplied THEN the output SHALL include `repository`, `branch`, `commit_sha`, `file_path`, `line_start`, `line_end`, `symbol_name`, `record_type`, `summary`, `code_snippet`, and `score` for each result.

7.11. WHEN `--format text` (default) is used THEN output SHALL match the format shown in §10.2 of the requirements, presenting the raw similarity score (not a calibrated confidence).

7.12. WHEN search returns results THEN top-k semantic search SHALL complete in under 3 seconds at typical load (per §18).

---

## Requirement 8 — Secret Redaction

**User story.** As a security-conscious operator, I want obvious secrets removed before they are stored or embedded so that the index doesn't become a credential leak.

### Acceptance criteria

8.1. WHEN building a `code_snippet` or `embedding_text` THEN the system SHALL redact the value of any assignment whose surrounding key matches (case-insensitive): `password`, `secret`, `token`, `api_key`, `apikey`, `connection_string`, `connectionstring`, `client_secret`, `private_key`.

8.2. WHEN a literal matches a known secret format (AWS access key, GitHub PAT, JWT, PEM block, or Base64 string ≥ 40 chars assigned to a secret-named key) THEN the value SHALL be replaced with the literal token `«REDACTED»`.

8.3. WHEN redaction occurs THEN both `code_snippet` (persisted) and `embedding_text` (sent to provider) SHALL use the redacted form. The original secret SHALL NEVER leave the worker process.

8.4. IF a known-secret pattern is detected inside a construct the redactor cannot safely transform (e.g. complex interpolated string) THEN the record SHALL be dropped, the `records_redaction_failed` counter on the scan job SHALL be incremented, and a structured log line SHALL be emitted.

8.5. WHEN a `.env` file is scanned THEN every value SHALL be treated as secret and redacted before producing a `configuration_reference` record.

---

## Requirement 9 — Stale Record Handling

**User story.** As an operator who re-scans repos, I want old records superseded but auditable.

### Acceptance criteria

9.1. WHEN a new successful scan starts for `(repository_id, branch)` THEN the system SHALL mark all existing records for that `(repository_id, branch)` as `is_stale = true` before inserting new records (or atomically as part of the same transaction).

9.2. WHEN insertion of new records succeeds THEN the freshly inserted rows SHALL have `is_stale = false`.

9.3. WHEN search runs without `--include-stale` THEN rows with `is_stale = true` SHALL be excluded.

9.4. WHEN a scan fails partway THEN previously stale records SHALL remain in place (no rollback to non-stale).

---

## Requirement 10 — Configuration

**User story.** As an operator, I want behavior configured via YAML with environment-variable overrides for secrets.

### Acceptance criteria

10.1. WHEN the worker starts THEN it SHALL read `config/codekb.yaml` (or a path supplied via `--config`).

10.2. WHEN an environment variable matching the override prefix (e.g. `CODEKB__STORAGE__POSTGRESCONNECTIONSTRING`, `EMBEDDING_API_KEY`) is set THEN it SHALL override the corresponding YAML value.

10.3. IF the YAML contains a Git credential key THEN it SHALL be rejected at load time with an explanatory error.

10.4. WHEN required configuration is missing or malformed (e.g. embedding model unset, dimension mismatch) THEN the worker SHALL fail fast with a clear error and non-zero exit code.

---

## Requirement 11 — Logging & Observability

**User story.** As an operator, I want structured logs and metrics so that scans are debuggable and operable.

### Acceptance criteria

11.1. WHILE the worker is running THEN all log output SHALL be JSON Lines on stdout.

11.2. WHEN a `scan_job_id` exists for the current operation THEN every log line for that operation SHALL include `scan_job_id`.

11.3. WHEN logging THEN the system SHALL NEVER log secrets, API keys, Git credentials, full source files, or raw embedding vectors.

11.4. WHILE the worker is running THEN it SHALL expose these metrics (Prometheus or stdout-summary):
- `codekb_scan_duration_seconds{repo,branch}` (histogram)
- `codekb_records_created_total{record_type}` (counter)
- `codekb_embedding_failures_total{provider,reason}` (counter)
- `codekb_redaction_hits_total{pattern}` (counter)
- `codekb_ask_latency_seconds` (histogram)

11.5. WHEN a scan completes THEN the CLI SHALL print the summary shown in §10.1 of the requirements: repository, branch, commit, files scanned, records created, feature flag matches, embeddings created, duration.

---

## Requirement 12 — Performance Targets

12.1. WHEN scanning a medium C# repository (~100k LOC) on the reference host THEN the scan SHALL complete in under 5 minutes.

12.2. WHEN the index contains at least 100,000 records THEN search and insert SHALL remain within their respective latency budgets (§7.12; insert governed by §6.3 batching).

12.3. WHEN `codekb ask` is invoked at typical load THEN it SHALL return in under 3 seconds.

---

## Requirement 13 — Future-Proofing the Internal API

**User story.** As a future maintainer who will add an HTTP API, I want the internals decoupled so that exposing scan/search over HTTP doesn't require a rewrite.

### Acceptance criteria

13.1. WHEN the CLI handlers run THEN they SHALL contain no business logic — only argument parsing, configuration loading, and calls into the core service layer.

13.2. WHEN scan orchestration, scanning, embedding, and storage are wired together THEN each SHALL sit behind an interface so that an HTTP host can construct the same graph without modification.
