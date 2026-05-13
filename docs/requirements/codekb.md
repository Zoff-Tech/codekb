# Requirements Document: Roslyn Code Ingestion Worker (MVP)

## 1. Purpose

Build a focused Roslyn-based ingestion worker that scans C# repositories, extracts code intelligence, generates embeddings, and stores searchable records in a vector database.

This is the first shippable component of the larger agentic SDLC / code intelligence framework. The goal is **not** to build an autonomous coding agent. The goal is a reliable code-knowledge ingestion pipeline that can answer questions such as:

- Where is this feature flag used?
- Which files mention a given business workflow?
- Which classes or methods are related to workflow operations?
- Which tests reference this feature?
- What code context should an agent retrieve before planning a change?

## 2. MVP Goal

Support one primary workflow:

> Given a C# repository and a search term or feature flag name, scan the codebase, extract relevant code records, generate embeddings, and store them in a vector database so they can be searched later.

Example — two separate invocations, scan first, then query:

```bash
# Ingest the repository
codekb scan \
  --repo https://github.com/example/platform-service \
  --branch main \
  --search EnableNewWorkflow

# Query the index later, from any machine pointed at the same database
codekb ask "Where is EnableNewWorkflow implemented?"
```

## 3. Non-Goals (for MVP)

The first version intentionally avoids:

- OmniSharp integration
- LangGraph or other agent orchestration
- Automatic code modification or PR creation
- Multi-language support (C# only)
- Full dependency graph generation
- Jira / requirements integration
- Kubernetes as a hard runtime requirement
- Code migration between languages

These can be added once the ingestion worker proves useful.

## 4. High-Level Architecture

```text
Repo URL / Local Path
        ↓
Repository Loader
        ↓
Roslyn Scanner
        ↓
Normalized Code Records
        ↓
Embedding Generator
        ↓
Postgres + pgvector
        ↓
Search / Query API or CLI
```

## 5. Core Components

### 5.1 CLI Entry Point

Required commands for the MVP:

```text
codekb scan
codekb ask
```

Optional future commands:

```text
codekb rescan
codekb status
codekb delete-repo
codekb list-repos
```

### 5.2 Repository Loader

Responsible for getting source code onto the local machine or worker container. Must support:

- Cloning a remote Git repository (shallow clone with `--depth 1` for the target branch by default)
- Pulling a specific branch
- Reading from a local repository path (no fetch, no checkout — the loader trusts what is on disk)
- Capturing commit SHA, repository name, and branch name
- Authenticating to private repositories via `GIT_TOKEN` / `GIT_USERNAME` environment variables, or via the operator's existing `ssh-agent` for SSH URLs. Credentials must never be read from the YAML config (§13) or logged (§16).

Examples:

```bash
codekb scan --repo https://github.com/org/service-a --branch main
codekb scan --path /repos/service-a
```

### 5.3 Roslyn Scanner

The core of the MVP. It should:

- Load `.sln` files when available; fall back to `.csproj` files when no solution exists; if neither is present, fall back to ad-hoc parsing of every `.cs` file under the repo root (no compilation, syntax-tree only)
- Parse C# files using Roslyn and build syntax trees
- Extract classes, interfaces, records, enums, methods, properties, and fields
- Detect search terms and feature flag references (see §8)
- Capture surrounding code context using a bounded window (see §5.7)
- Classify files as production, test, configuration, or generated (see §9)

Runtime assumptions:

- Targets `Microsoft.CodeAnalysis.CSharp` 4.x (Roslyn shipped with the .NET 8 SDK or newer)
- The worker host must have the matching .NET SDK on `PATH` to resolve project references; missing references degrade to syntax-only parsing, which is logged but not fatal

### 5.4 Normalized Code Record Generator

The scanner must not store raw Roslyn objects. It must convert findings into normalized records, each representing a meaningful unit:

- File summary
- Class summary
- Method summary
- Feature flag usage
- Search term match
- Test reference
- Configuration reference

### 5.5 Embedding Generator

Convert each normalized code record into structured embedding text (not raw code) and produce an embedding vector. Use the template in §11.1.

Example:

```text
Repository: platform-service
File: src/Funding/WorkflowService.cs
Symbol: WorkflowService.ProcessFundingAsync
Record Type: feature_flag_usage
Feature Flag: EnableNewWorkflow
Usage Type: runtime_branch
Summary: Checks EnableNewWorkflow before routing funding processing through the new workflow.
Code Snippet:
if (_featureFlags.IsEnabled("EnableNewWorkflow")) { ... }
```

### 5.6 Vector Storage

PostgreSQL with `pgvector`. The storage layer must support:

- Insert embeddings
- Search by semantic similarity (cosine or inner product)
- Filter by repository, branch, commit SHA, file path, and feature flag name
- Mark records stale when rescanning (see §12.2)

### 5.7 Snippet & Chunking Rules

To keep embeddings stable and cost-bounded:

- **Method-level records:** snippet = full method body, up to 200 lines or 4 KB, whichever comes first; truncate at a statement boundary.
- **Class-level records:** snippet = class declaration + member signatures (no bodies), up to 4 KB.
- **File-level records:** snippet = a generated summary of namespaces, top-level types, and search-term hits — never the full file.
- **Feature flag / search term records:** snippet = ±20 lines around the match, clamped to the enclosing method or block.

## 6. Data Model

### 6.1 `repository`

`id, name, url, local_path, branch, commit_sha, last_scanned_at, created_at, updated_at`

### 6.2 `scan_job`

`id, repository_id, branch, commit_sha, status, started_at, completed_at, error_message, records_created, records_failed`

### 6.3 `code_record`

```text
id, repository_id, scan_job_id, repository_name, branch, commit_sha,
file_path, line_start, line_end, record_type, symbol_name, symbol_kind,
namespace, class_name, method_name,
feature_flag_name, usage_type,
summary, code_snippet, metadata_json,
is_test_code, is_generated_code, is_stale,
embedding_status,
created_at, updated_at
```

Notes:

- `line_start` / `line_end` are 1-indexed and inclusive. For `file_summary` records they cover the whole file. They are required on every record so agents can jump straight to the code.
- `repository_name`, `branch`, and `commit_sha` are denormalized from `repository` / `scan_job` so result rows can be served without joins and so historical records remain readable after a repo is renamed or deleted.
- `feature_flag_name` and `usage_type` are denormalized top-level columns (also surfaced inside `metadata_json` for `feature_flag_usage` / `configuration_reference` records). Top-level columns exist to support cheap b-tree indexes for filter queries like "all usages of flag X"; `metadata_json` remains the source of truth for record-specific shape. Keep them in sync at write time.
- `language` is not stored for the MVP — C# is the only supported language. Add the column when multi-language support is implemented.
- `embedding_status` is an enum (`pending`, `embedded`, `failed`) that lets a background job retry records whose initial embedding call failed (see §17).

Required indexes:

- `(repository_id, branch, is_stale)` — primary filter for `codekb ask`
- `(feature_flag_name)` — feature-flag lookups
- `(repository_id, file_path, symbol_name, record_type, line_start, commit_sha)` UNIQUE — idempotent insert key (see §17). `line_start` is included to disambiguate overloads and partial-class members that share `(file_path, symbol_name)`.

#### `metadata_json` contents by record type

`metadata_json` is a free-form JSON column for record-specific detail. Each record type populates a known shape:

| Record type | `metadata_json` shape |
| --- | --- |
| `file_summary` | `{ "loc": int, "top_level_types": [string], "uses_unsafe": bool }` |
| `class_summary` | `{ "base_types": [string], "implements": [string], "attributes": [string], "is_abstract": bool, "is_sealed": bool, "is_partial": bool }` |
| `method_summary` | `{ "signature": string, "returns": string, "parameters": [{"name": string, "type": string}], "attributes": [string], "is_async": bool, "is_static": bool, "cyclomatic_complexity": int }` |
| `feature_flag_usage` | `{ "client_type": string, "method": string, "usage_type": "runtime_branch"\|"constant_definition"\|"injection"\|"config", "default_value": string\|null }` |
| `search_term_match` | `{ "term": string, "match_kind": "identifier"\|"string_literal"\|"comment"\|"xml_doc", "hit_count": int }` |
| `test_reference` | `{ "test_framework": "xunit"\|"nunit"\|"mstest"\|"unknown", "test_attributes": [string], "subjects": [string] }` |
| `configuration_reference` | `{ "file_format": "json"\|"yaml"\|"xml"\|"env", "json_path": string, "value_redacted": bool }` |

### 6.4 `code_embedding`

`id, code_record_id, embedding_model, embedding_model_version, embedding_dimension, embedding_vector, embedding_text, created_at`

- `embedding_model_version` enables re-embedding without losing history when a provider changes its model.
- `embedding_dimension` is stored explicitly because `pgvector`'s `vector(N)` column has a fixed `N`. The MVP uses a single vector column sized to the configured model (e.g. `vector(1536)` for `text-embedding-3-small`). Mixing models with different dimensions in one column is unsupported; switching dimensions requires a new column or table partition. Document the chosen dimension in `config/codekb.yaml`.
- Required index: `embedding_vector` with an HNSW index (`USING hnsw (embedding_vector vector_cosine_ops)`). IVFFlat is acceptable if HNSW is unavailable, but it must be re-trained after large insert batches. The chosen distance operator must match the operator used by `codekb ask` (cosine by default).

## 7. Record Types

| Type | Produced from | Purpose |
| --- | --- | --- |
| `file_summary` | every C# file | Coarse-grained recall ("which file mentions X") |
| `class_summary` | each top-level type | Symbol-level recall by class/interface/record/enum |
| `method_summary` | each method/property body | Fine-grained recall by method |
| `feature_flag_usage` | matches from §8 | "Where is flag X read or branched on" |
| `search_term_match` | hits for `--search` term | Targeted recall for terms named at scan time |
| `test_reference` | code in files classified as test (§9.1) | "Which tests cover X" |
| `configuration_reference` | matches in config files (§8.4) | "Where is flag X declared in config" |

## 8. Feature Flag Detection

The scanner detects feature flags via configurable patterns.

### 8.1 Direct string literal

```csharp
"EnableNewWorkflow"
```

### 8.2 Named constant

```csharp
public const string EnableNewWorkflow = "EnableNewWorkflow";
```

### 8.3 Feature service call

```csharp
_featureFlags.IsEnabled("EnableNewWorkflow")
_featureManager.IsEnabledAsync("EnableNewWorkflow")
launchDarklyClient.BoolVariation("EnableNewWorkflow", user, false)
```

A call is treated as a feature-flag usage only when **both** conditions hold:

1. The invoked method name appears in `scanner.featureFlagMethodNames`
2. The receiver's declared type (or one of its interfaces / base types) appears in `scanner.featureFlagClientNames`

The method-name check alone would false-positive on every `IsEnabled` in the codebase. Both are configurable (see §13).

### 8.4 Configuration match

```json
"FeatureFlags": {
  "EnableNewWorkflow": true
}
```

JSON/YAML/XML configuration files are scanned with text-based matching (no Roslyn) — they produce `configuration_reference` records.

Matching rules:

- A flag is matched when its name appears as a **key** under a section whose name contains `Feature`, `Flag`, or `Toggle` (case-insensitive), or as a key in any file whose path matches `appsettings*.json` / `appsettings*.yaml`.
- A record is emitted whether the value is `true`, `false`, or a string — the existence of the key is what matters for "where is this flag declared".
- Plain `key=value` `.env` lines are matched but the value is redacted (see §15.1) before being stored.
- A flag also produces a `configuration_reference` record for every explicit `--search` term that matches a key, independent of the section-name heuristic above.

## 9. File Classification

### 9.1 Test code

Marked as test if **any** of:

- Path contains `/test/`, `/tests/`, `.Tests`, or `.Test`
- Project name contains `Tests`
- File contains `[Fact]`, `[Theory]`, `[Test]`, or `[TestMethod]`

### 9.2 Generated code

- File ends with `.g.cs`, `.Designer.cs`, or `.generated.cs`
- File contains `<auto-generated`
- Path contains `/generated/`

### 9.3 Configuration

- Extension is `.json`, `.yaml`, `.yml`, `.config`, `.xml`, or `.env`
- Path contains `appsettings`

## 10. CLI Requirements

### 10.1 `codekb scan`

```bash
codekb scan \
  --repo https://github.com/org/service-a \
  --branch main \
  --search EnableNewWorkflow
```

Flags:

- `--repo <url>` — clone target (mutually exclusive with `--path`)
- `--path <dir>` — local repository path
- `--branch <name>` — branch to scan (default: repo default branch)
- `--search <term>` — names a term to track explicitly. Every literal, identifier, or symbol matching the term produces a `search_term_match` record (in addition to the normal scan output). The flag is repeatable. Matching is **case-sensitive** for identifiers (matches C# semantics) and **case-insensitive** inside string literals, comments, and XML doc comments. Substring matches do not count — the term must appear as a whole token in code, or as a whole word (`\b`-bounded) in literals/comments.
- `--force` — re-scan even if the latest commit SHA is already indexed for this branch

Behavior:

1. Clone or update the repository
2. Resolve the latest commit SHA on the target branch
3. If that `(repo, branch, commit_sha)` is already indexed and `--force` is not set, exit early with a "no-op, already indexed" message
4. Identify solution/project files
5. Run the scanner
6. Generate normalized records
7. Generate embeddings (see §11.4 on batching)
8. Persist records and embeddings
9. Print a scan summary

Expected output:

```text
Scan completed.
Repository:           service-a
Branch:               main
Commit:               abc1234
Files scanned:        342
Records created:      184
Feature flag matches: 12
Embeddings created:   184
Duration:             1m 47s
```

### 10.2 `codekb ask`

```bash
codekb ask "Where is EnableNewWorkflow implemented?"
```

Behavior:

- Embed the question
- Perform vector similarity search with optional metadata filters
- Return top-k matching code records
- With no `--repo` filter, search across **all** indexed repositories
- By default, exclude stale records and records whose `embedding_model` differs from the currently configured model (see §11.3)

Flags:

```text
--repo <name>             # filter by repository (repeatable)
--branch <name>           # filter by branch
--record-type <type>      # filter by record_type (repeatable, e.g. feature_flag_usage)
--feature-flag <name>     # shortcut for --record-type feature_flag_usage with name filter
--top-k <n>               # number of results (default 10)
--min-score <float>       # drop results below this similarity score
--format text|json        # output format (default text)
--include-stale           # include records where is_stale = true
--include-other-models    # include records embedded with a different model
```

Default text output:

```text
Question: Where is the new workflow implemented?
1. platform-service
   File:   src/Workflow/WorkflowService.cs:42-78
   Symbol: WorkflowService.ProcessAsync
   Match:  semantic
   Score:  0.91
2. platform-api
   File:   src/Controllers/WorkflowController.cs:15-31
   Symbol: WorkflowController.Submit
   Match:  semantic
   Score:  0.84
```

> "Score" is the raw similarity (e.g. cosine), not a calibrated confidence. The output uses the literal score so callers don't conflate ranking strength with truth.

JSON output (for agent consumption) must include: `repository`, `branch`, `commit_sha`, `file_path`, `line_start`, `line_end`, `symbol_name`, `record_type`, `summary`, `code_snippet`, and `score`.

## 11. Embedding Requirements

### 11.1 Embedding Text Template

```text
Repository: {repository_name}
Branch: {branch}
Commit: {commit_sha}
File: {file_path}:{line_start}-{line_end}
Language: csharp
Record Type: {record_type}
Namespace: {namespace}
Class: {class_name}
Method: {method_name}
Symbol: {symbol_name}
Feature Flag: {feature_flag_name}
Usage Type: {usage_type}
Summary: {summary}
Code Snippet:
{code_snippet}
```

Empty fields are omitted, not left blank.

### 11.2 Embedding Provider

Configurable. Supported in MVP:

- OpenAI embeddings
- Azure OpenAI embeddings
- Local model (future)

Required configuration:

```text
EMBEDDING_PROVIDER
EMBEDDING_MODEL
EMBEDDING_MODEL_DIMENSION   # must match the pgvector column width (§6.4)
EMBEDDING_API_KEY
EMBEDDING_ENDPOINT          # optional, defaults to provider default
```

On startup the worker must validate that `EMBEDDING_MODEL_DIMENSION` matches the actual `vector(N)` declared on the `code_embedding.embedding_vector` column and fail fast otherwise — silently truncating or padding vectors corrupts the index.

### 11.3 Model Versioning

The `embedding_model` and `embedding_model_version` fields are persisted alongside every vector. When the configured model changes, the worker must skip search results from old model versions by default (`codekb ask --include-other-models` overrides) and warn the operator that a re-embed is needed.

### 11.4 Batching & Concurrency

- Embedding calls must batch requests up to the provider's per-call input limit (typically 100–2048 inputs). Each batch is one HTTP call.
- Concurrent batches per scan are bounded by `scanner.parallelism` (§13).
- Records whose embedding call fails after retry are persisted with `embedding_status = failed` so a later run can pick them up without rescanning.

## 12. Database Requirements

PostgreSQL with `pgvector`.

### 12.1 Required Capabilities

- Store code records and embeddings
- Vector similarity search
- Metadata filtering
- Mark records stale by `(repository_id, branch)`
- Track scan jobs

### 12.2 Stale Record Handling

When rescanning the same repo + branch:

1. Mark existing records for that `(repo, branch)` as `is_stale = true`
2. Insert new records for the latest commit
3. Retain stale records for audit/history
4. Query APIs default to `is_stale = false`

## 13. Configuration

The worker reads a YAML config file:

```yaml
storage:
  postgresConnectionString: "Host=localhost;Database=codekb;Username=postgres;Password=postgres"

embedding:
  provider: openai
  model: text-embedding-3-small
  dimension: 1536        # must match the pgvector column width
  batchSize: 256         # capped by the provider's per-call input limit
  maxRetries: 5
  retryBackoffSeconds: 2 # exponential backoff base

scanner:
  ignorePaths:
    - bin
    - obj
    - .git
    - node_modules
    - packages
  featureFlagMethodNames:
    - IsEnabled
    - IsEnabledAsync
    - IsFeatureEnabled
    - BoolVariation
  featureFlagClientNames:
    - IFeatureFlagService
    - IFeatureManager
    - ILaunchDarklyClient
  parallelism: 4         # number of project workers
  maxFileSizeKB: 512     # files larger than this are skipped
```

Environment variables override file values for secrets (e.g. `EMBEDDING_API_KEY`, `CODEKB__STORAGE__POSTGRESCONNECTIONSTRING`).

## 14. Future API Surface

The MVP is CLI-only. The internal architecture must allow adding an HTTP API later without rewrites. Likely future endpoints:

```http
POST /scan
GET  /scan/{scanJobId}
GET  /feature-flags/{flagName}
POST /search
```

Designing for the API now means: keep scan orchestration, scanning, embedding, and storage decoupled behind interfaces; do not put business logic in CLI command handlers.

## 15. Security Requirements

The worker must:

- Avoid embedding secrets
- Redact obvious secret values before storing snippets (see §15.1)
- Ignore `.env` values unless explicitly allowed
- Never print access tokens, API keys, or Git credentials in logs
- Prefer read-only Git tokens
- Accept private-repo credentials via environment, not config files

### 15.1 Secret Redaction Patterns

Redaction applies to both the `code_snippet` and the `embedding_text` — a secret must never leave the worker, even as input to the embedding API.

Redact the **value** when the surrounding key name matches (case-insensitive):

```text
password
secret
token
api_key | apikey
connection_string | connectionstring
client_secret
private_key
```

Also redact any literal that matches well-known secret formats (e.g. AWS access keys, GitHub PATs, JWTs, PEM blocks, Base64 strings ≥ 40 chars assigned to a secret-named key) before persisting. Replacement is the fixed token `«REDACTED»` so downstream callers can detect it.

Failed redaction is a hard error: if the worker detects a known-secret pattern it cannot safely redact (e.g. inside a complex interpolated string), it must drop the record entirely and increment a `records_redaction_failed` counter on the scan job, rather than persist a partial snippet.

## 16. Logging & Observability

Log:

- Repository, branch, commit SHA
- Files scanned, records created, embeddings created, failures
- Scan duration
- Error categories and counts

Never log:

- Secrets, API keys, or Git credentials
- Full source files
- Raw embedding vectors

Structured logs only (JSON lines), with a stable schema so they can be ingested by downstream pipelines. Each log line must include `scan_job_id` when one exists so events can be correlated across components.

Metrics the worker must expose (Prometheus or stdout-summary acceptable for MVP):

- `codekb_scan_duration_seconds{repo,branch}` (histogram)
- `codekb_records_created_total{record_type}` (counter)
- `codekb_embedding_failures_total{provider,reason}` (counter)
- `codekb_redaction_hits_total{pattern}` (counter)
- `codekb_ask_latency_seconds` (histogram)

## 17. Error Handling

The worker must gracefully handle:

- Repository clone failure (fail the scan job, exit non-zero)
- Invalid branch (fail the scan job, exit non-zero)
- Missing `.sln` / `.csproj` (degrade to syntax-only parsing per §5.3, log a warning, continue)
- Roslyn parse errors (skip the file, record the failure, continue)
- Embedding API failure — retry with exponential backoff (`embedding.maxRetries`, base `embedding.retryBackoffSeconds`); if still failing, persist the `code_record` with `embedding_status = failed` and no vector so a later run can retry the record without rescanning the repository
- Database connection failure (fail fast)
- Duplicate records (idempotent insert by the unique index on `(repository_id, file_path, symbol_name, record_type, line_start, commit_sha)` — see §6.3)
- Files larger than `maxFileSizeKB` (skip, log)
- Partial scan interruption (Ctrl-C, OOM): on next run with the same `(repo, branch, commit_sha)` and no `--force`, the worker must resume by inserting only records that don't already exist for that commit, rather than re-doing the whole pass

Failures are recorded in the `scan_job` table and counted in the summary.

## 18. Performance Targets

- Scan a medium C# repository (~100k LOC) in under 5 minutes
- Support at least 100,000 code records over time
- Top-k semantic search returns in under 3 seconds at typical load
- Skip `bin`, `obj`, `.git`, `node_modules`, and generated folders by default

## 19. Acceptance Criteria

The MVP is complete when:

1. A user can scan a public or private C# repository
2. The worker captures repository, branch, and commit metadata
3. The worker detects feature flag usages by literal, constant, and method-call patterns
4. The worker creates normalized code records
5. The worker generates embeddings for those records
6. The worker stores records and embeddings in Postgres + pgvector
7. The user can search for a feature flag and see matching files/symbols
8. The user can ask a semantic question and retrieve relevant code records
9. The user can request JSON output from `codekb ask` for agent consumption
10. The system marks older records stale after rescanning
11. The system redacts obvious secrets before embedding or storing snippets

## 20. Recommended Implementation Order

1. **CLI skeleton** — `codekb scan` and `codekb ask` with stubbed behavior
2. **Repository loader** — clone, pull, local path
3. **Basic Roslyn scanner** — file path, namespace, class, method, search-term snippet
4. **Feature flag detection** — configurable literal, constant, and method-call patterns
5. **Database schema** — Postgres tables and `pgvector` extension
6. **Embedding generator** — template, provider abstraction, model versioning
7. **Search commands** — text and JSON output for `codekb ask`
8. **Rescan / stale logic** — mark previous records stale by `(repo, branch)`
9. **Secret redaction** — redact before embedding and persisting
10. **Container packaging** — Dockerfile for repeatable execution

## 21. Suggested Project Structure

```text
codekb/
  src/
    CodeKb.Cli/
    CodeKb.Core/
    CodeKb.Scanner.Roslyn/
    CodeKb.Embedding/
    CodeKb.Storage.Postgres/
  tests/
    CodeKb.Scanner.Tests/
    CodeKb.Storage.Tests/
    CodeKb.Embedding.Tests/
  docker/
    Dockerfile
  config/
    codekb.yaml
  README.md
```

## 22. Design Principle

> Keep the first version boring and useful.

The MVP is not an AI developer. It is a trustworthy code-knowledge ingestion engine. Once it works, downstream agents — LangGraph orchestrators, requirement reviewers, feature-flag removers, coding agents — can build on top of a reliable index.

## 23. Open Questions

These need answers before or during implementation:

1. **Cross-repo deduplication** — when the same NuGet-published library is vendored into two repos, do we want one logical record or two? MVP says two (simpler, correct per-repo); flag for revisit once we have real usage.
2. **Symbol resolution across projects** — should `WorkflowService` in `Project.A` link to its callers in `Project.B` within the same solution? MVP says no (no call graph); revisit when LangGraph integration begins.
3. **Branch lifecycle** — if a branch is deleted upstream, do we delete its records, mark them stale, or keep them indefinitely? Default for MVP: keep, mark stale on next scan failure, prune via an explicit `codekb delete-repo --branch` later.
4. **Embedding cost ceiling** — should the scanner refuse to start if the projected token count for a scan exceeds a configured budget? Recommended for MVP given that one bad rescan of a large monorepo can be expensive.

## 24. Glossary

- **Code record** — a normalized, language-agnostic representation of one meaningful unit (file, class, method, flag usage, etc.). One Roslyn syntax node may produce several records.
- **Embedding text** — the structured, templated string (see §11.1) that is sent to the embedding provider. Not the same as `code_snippet`; the snippet is one field within it.
- **Stale record** — a row left over from a previous scan of the same `(repo, branch)`. Hidden from search by default but retained for audit (see §12.2).
- **Feature flag usage** — a code site where a flag's value is read or branched on (not merely declared). Declarations land in `configuration_reference`.
- **Search term match** — a hit for a term named explicitly with `--search`. Distinct from feature-flag detection because the term may not be a flag at all.
