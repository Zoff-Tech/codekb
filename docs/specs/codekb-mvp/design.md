# Design — codekb MVP

Companion to [requirements.md](./requirements.md). This document captures the technical design: components, contracts between them, data flow, persistence, and the key decisions that shape the codebase.

## 1. Goals & Non-Goals

**Goals**
- Reliable, idempotent ingestion of C# repos into Postgres + pgvector.
- Decoupled core so a future HTTP API can be added without rewriting.
- Predictable failure modes: clone, parse, embed, persist each have explicit error paths.

**Non-goals** (mirrors §3 of the requirements): OmniSharp, LangGraph, multi-language, dependency graphs, automatic code modification, Kubernetes hard dep.

## 2. Stack Decisions

| Concern | Choice | Reason |
| --- | --- | --- |
| Language / Runtime | .NET 8 (C# 12) | Roslyn ships with the SDK; one toolchain for scanner and CLI. |
| Roslyn entrypoint | `Microsoft.CodeAnalysis.CSharp` 4.x via `MSBuildWorkspace`, fallback to `CSharpSyntaxTree.ParseText` | Solution loading gets semantic info; syntax-only fallback keeps the scanner unblocked when SDK/refs are missing. |
| CLI framework | `System.CommandLine` | First-party, supports subcommands, good help generation. |
| Config | YAML via `YamlDotNet` + env var overrides via `Microsoft.Extensions.Configuration` | Standard binding pipeline; env overrides map naturally to `CODEKB__` prefix. |
| DB | PostgreSQL 16 + `pgvector` 0.7+ | Single store for relational metadata and vectors. |
| DB client | `Npgsql` + `Pgvector.Npgsql` | First-party Npgsql plug-in for the `vector` type. |
| Migrations | Plain SQL files in `migrations/` run by a small bootstrapper | No EF for MVP — schema is small and stable. |
| Embedding providers | OpenAI + Azure OpenAI (interface-based, local model later) | Two providers with effectively one HTTP shape. |
| Git | `LibGit2Sharp` for clone/pull; system `ssh-agent` for SSH | In-process clone; no shelling out to `git`. |
| Logging | `Microsoft.Extensions.Logging` + JSON formatter | Structured JSON Lines per §11.1. |
| Metrics | `System.Diagnostics.Metrics` exported as stdout summary for MVP; Prometheus exporter is a slot-in later. | Avoid a hard Prom dependency for MVP. |
| Tests | `xUnit` + Testcontainers (Postgres+pgvector image) for integration | Real DB beats mocks for vector queries. |

## 3. High-Level Architecture

```text
                       ┌───────────────┐
                       │ codekb CLI    │
                       │  scan / ask   │
                       └──────┬────────┘
                              │ (no business logic)
                              ▼
                   ┌────────────────────┐
                   │ ScanService /      │
                   │ SearchService      │
                   └─┬──────┬──────┬────┘
                     │      │      │
        ┌────────────┘      │      └─────────────────┐
        ▼                   ▼                        ▼
┌───────────────┐  ┌────────────────┐    ┌────────────────────┐
│ Repository    │  │ RoslynScanner  │    │ EmbeddingClient    │
│ Loader        │  │ (+ classifier, │    │ (OpenAI/Azure)     │
│ (git/local)   │  │  detectors)    │    └──────────┬─────────┘
└───────────────┘  └──────┬─────────┘               │
                          ▼                         │
                ┌────────────────────┐              │
                │ Record Normalizer  │              │
                │ + Redactor         │              │
                └──────────┬─────────┘              │
                           ▼                        ▼
                       ┌────────────────────────────────┐
                       │ Storage (Postgres + pgvector)  │
                       └────────────────────────────────┘
```

Every box is an interface; the wiring lives in `CodeKb.Core` composition root and is reused by `CodeKb.Cli` (today) and a future `CodeKb.Api` (tomorrow).

## 4. Project Layout

```text
codekb/
  src/
    CodeKb.Cli/                  # System.CommandLine entrypoints, no business logic
    CodeKb.Core/                 # Orchestration: ScanService, SearchService, DI wiring
    CodeKb.Scanner.Roslyn/       # Roslyn loader, detectors, classifier, normalizer, redactor
    CodeKb.Embedding/            # IEmbeddingClient + OpenAI/Azure implementations + batcher
    CodeKb.Storage.Postgres/     # Repositories, migrations runner, pgvector binding
    CodeKb.Contracts/            # DTOs shared across modules (CodeRecord, ScanJob, ...)
  tests/
    CodeKb.Scanner.Tests/
    CodeKb.Storage.Tests/        # Testcontainers-backed
    CodeKb.Embedding.Tests/
    CodeKb.Cli.Tests/            # end-to-end against fixture repos
  migrations/
    001_init.sql
    002_indexes.sql
  config/
    codekb.yaml
  docker/
    Dockerfile
```

CLI handlers in `CodeKb.Cli` map flags onto a `ScanRequest` / `SearchRequest` and call into `CodeKb.Core`. They contain no scanning, no SQL, no HTTP. (Requirement 13.)

## 5. Core Interfaces

These are the boundaries that future HTTP, RPC, or in-process callers will reuse.

```csharp
// CodeKb.Core
public interface IScanService {
    Task<ScanResult> ScanAsync(ScanRequest req, CancellationToken ct);
}
public interface ISearchService {
    Task<IReadOnlyList<SearchHit>> AskAsync(SearchRequest req, CancellationToken ct);
}

// CodeKb.Scanner.Roslyn
public interface IRepositoryLoader {
    Task<LoadedRepository> LoadAsync(RepoSource src, CancellationToken ct);
}
public interface IRoslynScanner {
    IAsyncEnumerable<CodeRecord> ScanAsync(LoadedRepository repo, ScanOptions opts, CancellationToken ct);
}
public interface IFileClassifier { FileKind Classify(string path, string content); }
public interface IFeatureFlagDetector { IEnumerable<FlagHit> Detect(SemanticModel? sm, SyntaxTree tree, ScanOptions opts); }
public interface IRedactor { RedactionResult Redact(string snippet, string contextKey); }

// CodeKb.Embedding
public interface IEmbeddingClient {
    string ModelId { get; }
    string ModelVersion { get; }
    int Dimension { get; }
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}

// CodeKb.Storage.Postgres
public interface IRepositoryStore {
    Task<RepositoryRow> UpsertAsync(RepositoryKey key, CancellationToken ct);
}
public interface IScanJobStore {
    Task<Guid> StartAsync(Guid repoId, string branch, string commitSha, CancellationToken ct);
    Task FinishAsync(Guid scanJobId, ScanOutcome outcome, CancellationToken ct);
}
public interface ICodeRecordStore {
    Task MarkStaleAsync(Guid repoId, string branch, CancellationToken ct);
    Task<int> InsertBatchAsync(IReadOnlyList<CodeRecord> records, CancellationToken ct);   // idempotent
    Task UpdateEmbeddingsAsync(IReadOnlyList<EmbeddingRow> rows, CancellationToken ct);
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery q, CancellationToken ct);
}
```

All public DTOs live in `CodeKb.Contracts` so the storage layer doesn't take a dependency on the scanner.

## 6. Data Flow — `codekb scan`

```text
1. CLI parses flags → ScanRequest
2. ScanService.ScanAsync:
   a. RepositoryLoader.LoadAsync  → LoadedRepository {root, repoName, branch, commitSha}
   b. RepositoryStore.UpsertAsync → repositoryId
   c. Check duplicate: SELECT 1 FROM scan_job WHERE repo+branch+commit AND completed.
      If found and not --force → exit 0 "no-op".
   d. ScanJobStore.StartAsync     → scanJobId
   e. CodeRecordStore.MarkStaleAsync(repoId, branch)   [step 1 of stale handling]
   f. For each project / file via RoslynScanner.ScanAsync:
        - syntax + (optional) semantic model
        - FileClassifier → FileKind
        - emit file_summary, class_summary, method_summary
        - FeatureFlagDetector → feature_flag_usage
        - SearchTermMatcher → search_term_match
        - ConfigScanner (non-Roslyn) → configuration_reference
        - Redactor applied to every snippet before yield
   g. Records buffered into batches (size = embedding.batchSize); each batch:
        - InsertBatchAsync (idempotent ON CONFLICT DO NOTHING via unique index)
        - EmbeddingClient.EmbedBatchAsync over embedding_text
        - UpdateEmbeddingsAsync writes vectors and sets embedding_status='embedded'
        - failures after retry → embedding_status='failed', no vector row
   h. ScanJobStore.FinishAsync with counts
3. CLI prints scan summary
```

### Concurrency
- A bounded channel between the scanner (producer) and the embedding/storage pipeline (consumer).
- `scanner.parallelism` workers on the consumer side issue concurrent embedding batches.
- Roslyn project loading itself is single-threaded per workspace; multiple projects can be scanned in parallel when `.sln` is absent.

### Idempotency & resume
- `code_record` unique index on `(repository_id, file_path, symbol_name, record_type, line_start, commit_sha)` means `INSERT ... ON CONFLICT DO NOTHING` is safe to retry.
- A resumed scan repeats the same orchestration and simply no-ops on already-present rows.

## 7. Data Flow — `codekb ask`

```text
1. CLI parses flags → SearchRequest {question, filters, topK, minScore, includeStale, includeOtherModels, format}
2. SearchService.AskAsync:
   a. EmbeddingClient.EmbedBatchAsync([question]) → query vector
   b. SearchQuery built:
        - filters (repos, branch, record_type, feature_flag_name)
        - is_stale = false unless includeStale
        - embedding_model = current unless includeOtherModels
   c. CodeRecordStore.SearchAsync:
        ORDER BY embedding_vector <=> $1     -- cosine
        LIMIT topK
3. CLI formats text or JSON output
```

The SQL uses the cosine operator (`<=>`) so the HNSW index built with `vector_cosine_ops` is the matching access path. If `--min-score` is supplied, similarity is computed as `1 - distance` and the application filters in-memory after the top-k pull (filtering inside the SQL would break the index ordering).

## 8. Database Schema

Tables already specified in §6 of the requirements. Important implementation notes:

### `code_embedding.embedding_vector`
Declared as `vector(1536)` for the default model. Dimension is set at migration time from config; deploying with a different dimension is a fresh migration, not an in-place ALTER (mixing dimensions in one column is unsupported).

### Indexes (migration `002_indexes.sql`)
```sql
CREATE INDEX code_record_filter_idx
  ON code_record (repository_id, branch, is_stale);

CREATE INDEX code_record_feature_flag_idx
  ON code_record (feature_flag_name)
  WHERE feature_flag_name IS NOT NULL;

CREATE UNIQUE INDEX code_record_unique_idx
  ON code_record (repository_id, file_path, symbol_name, record_type, line_start, commit_sha);

CREATE INDEX code_embedding_hnsw_idx
  ON code_embedding USING hnsw (embedding_vector vector_cosine_ops);
```

If HNSW is unavailable, fall back to IVFFlat; rebuild after large batches is the operator's responsibility (documented in the README).

### Stale marking is one statement, not a loop
```sql
UPDATE code_record SET is_stale = TRUE
WHERE repository_id = $1 AND branch = $2 AND is_stale = FALSE;
```
Run inside the same transaction as the first insert batch so an aborted scan doesn't leave the index empty.

## 9. Roslyn Scanning Details

### Loading
- `MSBuildWorkspace.Create()` after `MSBuildLocator.RegisterDefaults()`. The host must have a matching .NET SDK on `PATH`.
- On `WorkspaceFailed` events, log the diagnostic and downgrade the affected project to syntax-only.
- `.sln` → `OpenSolutionAsync`. No `.sln` → discover `.csproj` files and call `OpenProjectAsync` per file. None → glob `.cs` files and parse with `CSharpSyntaxTree.ParseText`.

### Per-file pipeline
1. Skip if path matches `scanner.ignorePaths` or size > `scanner.maxFileSizeKB`.
2. Classify (test / generated / config / production) via `IFileClassifier`.
3. Walk the syntax tree with a `CSharpSyntaxWalker` that:
   - Emits `file_summary` once per file.
   - Emits `class_summary` per top-level type (class, interface, record, enum, struct).
   - Emits `method_summary` per method/property body.
   - Forwards to `IFeatureFlagDetector` and `ISearchTermMatcher`.
4. Snippet rules from Requirements 3.5–3.7 are encoded in a `SnippetBuilder` shared by every emitter.

### Feature flag detector
- Two passes:
  - **Constant-definition pass**: walks `FieldDeclarationSyntax` for `public const string X = "X";` shapes.
  - **Invocation pass**: visits `InvocationExpressionSyntax`. If `SemanticModel` is available, resolve `IMethodSymbol` and check method name + receiver type (including interfaces & base types) against config. Without a semantic model, fall back to receiver identifier text matching the configured client names — this is documented as best-effort.
- Direct string literals matching a configured flag name (e.g. constants registered during the constant pass) emit `usage_type = "runtime_branch"` records.

### Config / non-Roslyn pass
Run after Roslyn pass:
- JSON: parse with `System.Text.Json`, walk paths, match `appsettings*` and `Feature*/Flag*/Toggle*` sections.
- YAML: `YamlDotNet` traversal.
- XML: `System.Xml.Linq`.
- `.env`: line-by-line `KEY=VALUE`. Always redact the value.

## 10. Redaction Pipeline

Single chokepoint: every snippet flows through `IRedactor.Redact(snippet, contextKey)` before being attached to a record and again as part of `embedding_text` construction.

Detection order:
1. Key-based: regex matches `(password|secret|token|api[_-]?key|connection[_-]?string|client[_-]?secret|private[_-]?key)\s*[:=]\s*` then redact the value capture.
2. Pattern-based: AWS access keys (`AKIA[0-9A-Z]{16}`), GitHub PATs (`ghp_…`, `github_pat_…`), JWTs (`eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+`), PEM (`-----BEGIN [A-Z ]+PRIVATE KEY-----`), Base64 ≥ 40 chars assigned to a secret-named key.
3. If pattern (2) matches inside an interpolated string or other construct where the redactor's regex can't safely splice → return `RedactionResult.Failed` and the record is dropped (counter incremented).

The metric `codekb_redaction_hits_total{pattern}` is incremented per match for visibility.

## 11. Embedding Pipeline

### Provider abstraction
`IEmbeddingClient` exposes batch embedding. Concrete clients:
- `OpenAIEmbeddingClient` — POST `/v1/embeddings`, supports `text-embedding-3-small` (1536) and `text-embedding-3-large` (3072).
- `AzureOpenAIEmbeddingClient` — same shape with deployment + api-version.

### Startup validation
On worker boot, query
```sql
SELECT typmodifier_to_dim(c.atttypmod) FROM ... WHERE table='code_embedding' AND column='embedding_vector';
```
(or simpler: a `SELECT vector_dims(NULL::vector(1)::vector)` style probe). Compare with `IEmbeddingClient.Dimension`; abort startup on mismatch.

### Batching
- Producer side hands records to the batcher in pages of `embedding.batchSize` (capped to provider limit ≤ 2048).
- Retries: exponential backoff with jitter, `maxRetries` attempts, `retryBackoffSeconds` base.
- On final failure: persist `code_record` with `embedding_status = 'failed'`, no `code_embedding` row.

### Model-version awareness
The current `embedding_model` + `embedding_model_version` are stamped on every `code_embedding` row. `SearchService` injects `WHERE embedding_model = @current` unless `--include-other-models` is set, and logs a startup warning if the most recent scan used a different model.

## 12. CLI Surface

```text
codekb scan
  --repo <url>            # mutex with --path
  --path <dir>
  --branch <name>         # default: repo default branch
  --search <term>         # repeatable
  --force
  --config <path>         # default: ./config/codekb.yaml

codekb ask "<question>"
  --repo <name>           # repeatable
  --branch <name>
  --record-type <type>    # repeatable
  --feature-flag <name>
  --top-k <n>             # default 10
  --min-score <float>
  --format text|json      # default text
  --include-stale
  --include-other-models
  --config <path>
```

Exit codes:
- `0` success (including "already indexed" no-op)
- `2` user error (bad flags, conflicting flags, invalid config)
- `3` infrastructure error (DB down, clone failed, embedding API unreachable after retries)
- `4` partial success with redaction-drop failures (logged on the scan job)

## 13. Observability

- Logger: `Microsoft.Extensions.Logging` configured with a JSON console formatter. `LoggerMessage` source-gen for hot paths.
- Every scope opens with `scan_job_id` once it's known. Anything before that point uses `correlation_id = GUID`.
- Metrics via `System.Diagnostics.Metrics`. For MVP a stdout summary at end of scan; Prometheus exporter is a small swap.
- A redact-fail log includes the pattern name and the file/line, never the matched value.

## 14. Configuration Loading

1. Default: read `./config/codekb.yaml`.
2. Bind into a typed `CodeKbOptions` record.
3. Apply env-var overrides via `Microsoft.Extensions.Configuration.EnvironmentVariables` with `CODEKB__` prefix and `__` as nested separator. Also accept the bare `EMBEDDING_API_KEY` / `EMBEDDING_ENDPOINT` / `GIT_TOKEN` / `GIT_USERNAME` shortcuts.
4. Reject the file if any of: `gitToken`, `gitUsername`, `git.token`, `git.password`, `embedding.apiKey` appears under storage/git keys in YAML — credentials must come from env or ssh-agent.
5. Validate: provider/model/dimension set; dimension matches DB column; connection string parses.

## 15. Future HTTP API (informative)

Endpoints likely to map 1:1 onto the core services:

| HTTP | Service call |
| --- | --- |
| `POST /scan` | `IScanService.ScanAsync` |
| `GET /scan/{id}` | `IScanJobStore.GetAsync` |
| `GET /feature-flags/{name}` | `ISearchService.AskAsync` with `--feature-flag` filter |
| `POST /search` | `ISearchService.AskAsync` |

Because the CLI already calls these services without business logic, the HTTP host is just a thin ASP.NET wrapper.

## 16. Test Strategy

- **Scanner unit tests** — small in-memory C# snippets through `CSharpSyntaxTree.ParseText`; assert record shape, snippet boundaries, classifier output, redaction.
- **Detector tests** — fixtures for each feature-flag pattern (literal, constant, method-call with right/wrong client type).
- **Redactor tests** — table-driven over every secret pattern from §15.1 of the requirements; both successful redaction and forced-drop cases.
- **Storage integration tests** — Testcontainers Postgres + pgvector; assert idempotency, stale-marking transactional behavior, HNSW index existence, similarity ordering.
- **Embedding tests** — fake `HttpMessageHandler` for retries/backoff; one live-call smoke test gated behind env var.
- **End-to-end** — fixture repo under `tests/fixtures/sample-csharp-repo/`; full `codekb scan --path ...` followed by `codekb ask` asserting expected top-1 hit.

## 17. Risks & Mitigations

| Risk | Mitigation |
| --- | --- |
| Roslyn workspace fails to resolve refs on operator machines | Documented degradation to syntax-only; warning logged; tests cover both paths. |
| Embedding API outage stalls scans | Per-batch retry with backoff; failed records persist as `embedding_status='failed'` so reruns recover without rescanning. |
| Index dimension drift after model change | Startup dimension check (§6.7); `--include-other-models` opt-in for ask. |
| Secret accidentally embedded | Single chokepoint redactor; failed-redaction is a hard drop; redaction-hits metric. |
| Large monorepo blows up token budget | Open question §23.4; design leaves room for a pre-scan dry-run cost estimator without changing the orchestration. |
