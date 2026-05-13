# Implementation Tasks — codekb MVP

Each task is a discrete, testable, code-producing step. Requirement references point to [requirements.md](./requirements.md) (e.g. `R4.2`). Design references point to [design.md](./design.md) (e.g. `D §10`). Check off as you go.

The order follows §20 of the upstream requirements: a thin slice end-to-end, then deepen each layer.

---

## Phase 0 — Repository scaffolding

- [ ] **0.1 Solution & project skeleton.** Create `codekb.sln` and the six projects under `src/` exactly as laid out in `D §4`. Wire project references: `CodeKb.Cli → CodeKb.Core`; `CodeKb.Core → CodeKb.Scanner.Roslyn, CodeKb.Embedding, CodeKb.Storage.Postgres, CodeKb.Contracts`; the leaves depend only on `CodeKb.Contracts`. _(R13.1, R13.2)_
- [ ] **0.2 Test projects.** Create the four test projects under `tests/` and add `xunit`, `FluentAssertions`, `Testcontainers.PostgreSql`. Confirm `dotnet test` succeeds on the empty solution.
- [ ] **0.3 Editor & lint setup.** Add `.editorconfig` (4-space, `dotnet_diagnostic.IDE0058.severity = none` if you don't want discarded-expression noise) and enable nullable + analyzers solution-wide.
- [ ] **0.4 Dockerfile stub.** Add `docker/Dockerfile` based on `mcr.microsoft.com/dotnet/sdk:8.0`, multistage, that produces a runnable `codekb` ENTRYPOINT image. Tasks 10.x will revisit. _(supports §20.10)_

## Phase 1 — CLI skeleton

- [ ] **1.1 `codekb` entrypoint.** In `CodeKb.Cli`, set up `System.CommandLine` with two subcommands: `scan` and `ask`. Each should currently print "not implemented" and exit 0. _(R7, R1)_
- [ ] **1.2 Flags for `scan`.** Declare every flag listed in `D §12` for `scan`, enforce `--repo` xor `--path`, and bind into a `ScanRequest` DTO in `CodeKb.Contracts`. Reject the conflicting case with exit code 2. _(R1.3, R1.4)_
- [ ] **1.3 Flags for `ask`.** Declare every flag in `D §12` for `ask`, bind into a `SearchRequest` DTO. Repeatable flags become `IReadOnlyList<string>`. _(R7.6–R7.10)_
- [ ] **1.4 Composition root.** In `CodeKb.Core`, register all interfaces from `D §5` in a `ServiceCollection`. CLI handlers resolve `IScanService` / `ISearchService` and call them with the bound requests. CLI handlers contain no other logic. _(R13.1)_

## Phase 2 — Configuration

- [ ] **2.1 `CodeKbOptions` model.** Define a typed record tree mirroring the YAML in §13 of the upstream requirements, in `CodeKb.Core/Configuration/`.
- [ ] **2.2 YAML + env binding.** Implement loader: read `--config` path (default `./config/codekb.yaml`), then layer env-var overrides (`CODEKB__…` plus the bare aliases listed in `D §14.3`). _(R10.1, R10.2)_
- [ ] **2.3 Credential rejection.** During load, fail with exit code 2 if any of the YAML keys in `D §14.4` are populated. _(R1.7, R10.3)_
- [ ] **2.4 Validation.** Validate required fields, model presence, and that `embedding.dimension > 0`. Dimension/DB match is verified later (Task 6.6). _(R10.4)_
- [ ] **2.5 Sample config.** Ship `config/codekb.yaml` matching §13 of the upstream requirements.

## Phase 3 — Storage & migrations

- [ ] **3.1 SQL migrations.** Author `migrations/001_init.sql` with the four tables from §6 of the upstream requirements; `CREATE EXTENSION IF NOT EXISTS vector;` at the top. The `embedding_vector` column width is templated from config (`{{ dimension }}`) and substituted at apply time.
- [ ] **3.2 Indexes migration.** `migrations/002_indexes.sql` per `D §8`. _(R6.7 ground; R7 perf budget)_
- [ ] **3.3 Migration runner.** Small bootstrap in `CodeKb.Storage.Postgres` that applies pending `.sql` files in order on startup, tracked via a `_migrations` table. Idempotent.
- [ ] **3.4 `IRepositoryStore`, `IScanJobStore`.** Implement upsert + start/finish flows in `CodeKb.Storage.Postgres`. _(R1.9)_
- [ ] **3.5 `ICodeRecordStore` — insert + mark stale.** Implement `MarkStaleAsync` (single `UPDATE`) and `InsertBatchAsync` using `INSERT … ON CONFLICT DO NOTHING` keyed on the unique index. Stale-mark must run inside the same transaction as the first insert batch. _(R9.1, R9.2, R1.11)_
- [ ] **3.6 `ICodeRecordStore` — embeddings + search.** Implement `UpdateEmbeddingsAsync` (`pgvector` binding) and `SearchAsync` (`ORDER BY embedding_vector <=> $1 LIMIT topK`) with metadata filters from `SearchQuery`. _(R7.1–R7.10)_
- [ ] **3.7 Testcontainers integration tests.** Cover: migrations apply cleanly; insert idempotency; stale-marking is transactional (abort leaves prior state intact); top-k order matches direct SQL. _(R9.4)_

## Phase 4 — Repository loader

- [ ] **4.1 `IRepositoryLoader` for remote URL.** Use `LibGit2Sharp` for a shallow clone of `--repo --branch`. Honor `GIT_TOKEN`/`GIT_USERNAME` for HTTPS; let SSH URLs fall through to the OS `ssh-agent`. _(R1.1, R1.5, R1.6)_
- [ ] **4.2 Local path loader.** Implement `--path` branch — no fetch, no checkout, just read the working tree and read the commit SHA from `.git/HEAD`. _(R1.2)_
- [ ] **4.3 No credential logging.** Verify with a unit test that even with `GIT_TOKEN=secret` set, no log line contains the value. _(R1.8, R11.3)_

## Phase 5 — Roslyn scanner

- [ ] **5.1 Workspace loader.** Implement loader that tries `.sln` (`MSBuildWorkspace.OpenSolutionAsync`), then each `.csproj`, then syntax-only fallback. Subscribe to `WorkspaceFailed`; log + degrade. _(R2.1–R2.4)_
- [ ] **5.2 File classifier.** `IFileClassifier` per §9 of the upstream requirements: production / test / generated / configuration. Table-driven unit tests for every rule. _(R2.9)_
- [ ] **5.3 Syntax walker emitting summaries.** Emit `file_summary`, `class_summary`, `method_summary` with correct `line_start`/`line_end` (1-indexed, inclusive). _(R3.1–R3.4)_
- [ ] **5.4 Snippet builder.** Centralized helper applying the bounds in `R3.5–R3.7` (200 lines / 4 KB, statement-boundary truncation; class signatures only; ±20 lines around match).
- [ ] **5.5 Ignore-path & size filter.** Skip files matching `scanner.ignorePaths` and bigger than `scanner.maxFileSizeKB`. Log skips. _(R2.6, R2.7)_
- [ ] **5.6 Parse-error tolerance.** A single broken file increments `records_failed` and continues. _(R2.5)_

## Phase 6 — Feature flag & search-term detectors

- [ ] **6.1 Constant-definition pass.** Walk `FieldDeclarationSyntax`/`LocalDeclarationStatementSyntax` for `const string NAME = "NAME";` shapes, emit `feature_flag_usage` with `usage_type = "constant_definition"`. _(R4.1)_
- [ ] **6.2 Invocation-based detection (with semantic model).** Resolve the called `IMethodSymbol`; check method name vs `featureFlagMethodNames` AND receiver type (including interfaces & base types) vs `featureFlagClientNames`. _(R4.2, R4.3)_
- [ ] **6.3 Invocation-based detection (syntax-only fallback).** When no `SemanticModel` is available, match by identifier text of receiver. Document as best-effort. _(R2.4 supports degradation)_
- [ ] **6.4 String-literal flag matches.** When a literal matches a flag name discovered in the constant pass or in `--search`, emit `usage_type = "runtime_branch"`. _(R4.1)_
- [ ] **6.5 Search-term matcher.** Implement `ISearchTermMatcher` for: identifiers (case-sensitive, whole token), string literals (case-insensitive, `\b`-bounded), comments + XML doc (same as literals). Emit `search_term_match` records with the right `match_kind`. _(R5.1–R5.5)_
- [ ] **6.6 Config scanner.** Non-Roslyn pass over `.json`/`.yaml`/`.yml`/`.xml`/`.env` files. Section-name heuristic + `appsettings*` rule + explicit `--search` matches. `.env` values always redacted. _(R4.4–R4.6)_

## Phase 7 — Redaction

- [ ] **7.1 `IRedactor` implementation.** Key-based regex set from `R8.1` and pattern-based detectors from `R8.2` (AWS, GH PAT, JWT, PEM, Base64). Replacement token is the literal `«REDACTED»`.
- [ ] **7.2 Redactor wired as single chokepoint.** Apply to every snippet immediately after `SnippetBuilder` output AND when assembling `embedding_text`. _(R8.3)_
- [ ] **7.3 Failed redaction drops record.** When detection fires inside an unsupported construct, return `Failed`; the scanner drops the record, increments `records_redaction_failed` on the scan job, logs the pattern name without the value. _(R8.4)_
- [ ] **7.4 Table-driven redactor tests.** Cover each pattern, key + value combinations, and the drop-case. _(R8.1, R8.2, R8.4)_

## Phase 8 — Embedding pipeline

- [ ] **8.1 `IEmbeddingClient` + OpenAI implementation.** POST to `/v1/embeddings`, return `float[]` per input. Expose `ModelId`, `ModelVersion`, `Dimension`. _(R6.6)_
- [ ] **8.2 Azure OpenAI implementation.** Same interface; honor `EMBEDDING_ENDPOINT` + deployment + api-version. _(R10.2)_
- [ ] **8.3 Batching layer.** `EmbeddingBatcher` enforces `embedding.batchSize` cap and producer/consumer concurrency limited by `scanner.parallelism`. _(R6.3)_
- [ ] **8.4 Retry with backoff.** Exponential backoff with jitter, `maxRetries`, `retryBackoffSeconds` base. _(R6.4)_
- [ ] **8.5 Failed-embedding persistence.** When retries exhaust, write `code_record` with `embedding_status = 'failed'` and no `code_embedding`. _(R6.5)_
- [ ] **8.6 Embedding-text builder.** Apply the §11.1 template, omit empty fields, use the redacted snippet. _(R6.1, R6.2)_
- [ ] **8.7 Dimension validation at startup.** Query the actual `vector(N)` width on `code_embedding.embedding_vector` and compare with `IEmbeddingClient.Dimension`; abort startup on mismatch. _(R6.7)_

## Phase 9 — Orchestration & CLI wiring

- [ ] **9.1 `ScanService` orchestrator.** Implement the flow from `D §6` step by step: load → upsert repo → duplicate check → start job → mark stale → scan → batch insert + embed → finish job. _(R1.10, R1.11, R9.1)_
- [ ] **9.2 Scan summary output.** Print the summary shown in §10.1 of the upstream requirements when scan completes. _(R11.5)_
- [ ] **9.3 `SearchService` orchestrator.** Implement the flow from `D §7`: embed question → build query with filters & default exclusions → run `SearchAsync` → return hits. _(R7.1–R7.5)_
- [ ] **9.4 `codekb ask` text output.** Format per `D §12` / §10.2 of the upstream requirements. Score is raw similarity. _(R7.11)_
- [ ] **9.5 `codekb ask` JSON output.** Emit the exact field set in `R7.10`. _(R7.10)_
- [ ] **9.6 Exit-code mapping.** Map outcomes to exit codes per `D §12`. _(R1.3, R1.8, R10.4)_

## Phase 10 — Observability

- [ ] **10.1 JSON Lines logger.** Configure `Microsoft.Extensions.Logging` with a JSON formatter writing to stdout. _(R11.1)_
- [ ] **10.2 Scope with `scan_job_id`.** Open a logging scope as soon as the job id is known so every subsequent line carries it. _(R11.2)_
- [ ] **10.3 Secret-safe log redactor.** A log filter that scrubs known patterns from any string argument before emit, as a defense-in-depth layer over R8 (so a stray `_logger.LogInformation("token={Tok}", token)` still won't leak). _(R11.3)_
- [ ] **10.4 Metrics.** Register the five meters from `R11.4`. For MVP, emit a stdout summary on scan completion; leave a hook for a Prometheus exporter.

## Phase 11 — End-to-end & polish

- [ ] **11.1 Sample fixture repo.** Add `tests/fixtures/sample-csharp-repo/` containing: a service with `_featureFlags.IsEnabled("EnableNewWorkflow")`, a `Constants.cs` with `public const string EnableNewWorkflow = "EnableNewWorkflow";`, a test project (`[Fact]`), an `appsettings.json` with the flag under `"FeatureFlags"`, a `.env` with a fake token, and one file with `<auto-generated`.
- [ ] **11.2 E2E test — scan + ask.** Spin up the Postgres container, run `codekb scan --path …` against the fixture, then `codekb ask "EnableNewWorkflow"` and assert: feature_flag_usage hit ranks top-1, configuration_reference hit appears, generated file is excluded, env value is redacted. _(R-all happy path)_
- [ ] **11.3 Force / no-op behavior.** Run `codekb scan` twice; second run prints "already indexed" and inserts no new rows. Then run with `--force` and confirm prior rows are marked stale, new rows are inserted. _(R1.10, R9.1)_
- [ ] **11.4 Performance smoke.** Run a scan over a known ~100k LOC public C# repo (e.g. `dotnet/runtime` subset) on the reference host; assert under 5 minutes. _(R12.1)_
- [ ] **11.5 README.** Author `README.md` covering install, config, running scan + ask, and the no-op / `--force` behavior. _(supports §20.10 — packaging)_
- [ ] **11.6 Dockerfile finalization.** Multi-stage build that emits a slim runtime image including `libgit2` natives. Verify `docker run codekb scan --path /repo` works.

---

## Acceptance gate

Before declaring the MVP done, walk through §19 of the upstream requirements end-to-end against the deployed Docker image and the fixture repo, plus one public C# repo. Every numbered criterion in §19 must be visibly satisfied by either a passing E2E test or a manual reproducible step.
