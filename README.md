# codekb

> A Roslyn-based code-knowledge ingestion worker for C# repositories.

[![CI](https://github.com/Zoff-Tech/codekb/actions/workflows/ci.yml/badge.svg)](https://github.com/Zoff-Tech/codekb/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Coverage](https://img.shields.io/badge/coverage-92%25-brightgreen.svg)](#testing--coverage)

codekb scans a C# repository, extracts a normalized, language-aware model of the code (solutions, projects, files, classes, methods, constructors, indexers, operators, fields, events, enum members, delegates, local functions, search-term hits, test references, configuration references), generates embeddings for each record, and stores them in PostgreSQL with [`pgvector`](https://github.com/pgvector/pgvector) so that you can ask **semantic** questions over the codebase later.

It is **not** an autonomous coding agent. It is the trustworthy code-knowledge index that an agentic SDLC framework can build on top of.

## What it answers

- *Where is this term used?* (e.g. a feature-flag key like `EnableNewWorkflow`, a workflow name, a config key — flag names flow into method snippets, identifier tokens, and search-term records, so semantic search surfaces them like any other identifier)
- *Which files mention a given business workflow?*
- *Which classes or methods are related to this term?*
- *Which tests reference this feature?*
- *What does this method call, and what does it instantiate?* (call graph)
- *Which projects depend on this package?* (project metadata)
- *Which files import `Acme.Workflow`?* (using directives)
- *What code context should an agent retrieve before planning a change?*

Sub-word tokenization means a query like *"account"* matches `PrePaidAccount`, `process-payment` matches `processPayment`, and `is_enabled` matches `IsEnabled` — see [Embedding-time tokenization](#embedding-time-tokenization).

---

## Quickstart

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- PostgreSQL 16 with the [`pgvector`](https://github.com/pgvector/pgvector) extension
- An OpenAI or Azure OpenAI API key (for embeddings)

### 1 — clone & build

```bash
git clone https://github.com/Zoff-Tech/codekb.git
cd codekb
dotnet build
```

### 2 — start Postgres (Docker)

```bash
docker run -d --name codekb-pg \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=codekb \
  -p 5432:5432 \
  pgvector/pgvector:pg16
```

No manual schema setup needed — codekb embeds its SQL migrations and applies them on the first `codekb scan` against a fresh database. The migrations are tracked in a `_migrations` table, so subsequent scans skip work that's already been done.

### 3 — configure

Create `config/codekb.yaml`:

```yaml
storage:
  postgresConnectionString: "Host=localhost;Database=codekb;Username=postgres;Password=postgres"

embedding:
  provider: openai
  model: text-embedding-3-small
  dimension: 1536
  batchSize: 256
  maxRetries: 5
  retryBackoffSeconds: 2

scanner:
  ignorePaths: [bin, obj, .git, node_modules, packages]
  parallelism: 4
  maxFileSizeKb: 512
```

Set your API key in the environment:

```bash
export EMBEDDING_API_KEY=sk-...
```

> Secrets **must never** appear in the YAML. codekb rejects YAML files that contain `gitToken`, `embeddingApiKey`, etc., to keep credentials out of source control. See [Configuration](#configuration).

### 4 — scan a repository

```bash
dotnet run --project src/CodeKb.Cli -- scan \
  --repo https://github.com/example/platform-service \
  --branch main \
  --search EnableNewWorkflow
```

Output:

```
Scan completed.
Repository:           platform-service
Branch:               main
Commit:               abc1234
Files scanned:        342
Records created:      184
Embeddings created:   184
Duration:             1m 47s
```

### 5 — ask questions

```bash
dotnet run --project src/CodeKb.Cli -- ask "Where is EnableNewWorkflow implemented?"
```

```
Question: Where is EnableNewWorkflow implemented?
1. platform-service
   File:   src/Workflow/WorkflowService.cs:42-78
   Symbol: WorkflowService.ProcessAsync
   Match:  semantic
   Score:  0.91
2. platform-service
   File:   src/Controllers/WorkflowController.cs:15-31
   Symbol: WorkflowController.Submit
   Match:  semantic
   Score:  0.84
```

JSON output for agent consumption:

```bash
codekb ask "..." --format json
```

---

## Architecture

```text
Repo URL / Local Path
        │
        ▼
RepositoryLoader      (LibGit2Sharp — shallow clone or local read)
        │
        ▼
RoslynScanner         (orchestrator: project + syntax + detectors)
   │ │ │ │
   │ │ │ └─── ProjectScanner    (.sln + .csproj → projects, packages, references)
   │ │ └───── SyntaxExtractor   (types, methods, ctors, indexers, operators,
   │ │                           conversions, fields, events, enum members,
   │ │                           delegates, local functions, nested types;
   │ │                           + call graph: calls + instantiates per method;
   │ │                           + using directives & external-type refs)
   │ └─────── SearchTermMatcher (identifier / literal / comment / xml-doc)
   └───────── ConfigFileScanner (JSON, YAML, XML, .env)
                  │
                  ▼
            Redactor (secret patterns; drop-and-count if unsafe)
                  │
                  ▼
            CodeRecord (DTO)
                  │
                  ▼
EmbeddingPipeline     (IdentifierTokenizer sub-word split → embedding text;
                       OpenAI / Azure OpenAI, batched with retry)
        │
        ▼
Postgres + pgvector   (idempotent insert, HNSW index on vector(N))
        │
        ▼
SearchService         (codekb ask → cosine top-K with filters)
```

Every component sits behind an interface so the same core can be exposed via an HTTP API later with no rewrites. See [docs/specs/codekb-mvp/design.md](docs/specs/codekb-mvp/design.md) for the full design.

### What gets extracted

For every C# repository, codekb emits records for:

| Record kind | Source constructs | Notes |
|---|---|---|
| `file_summary` | every `.cs` file | metadata includes `loc`, `top_level_types`, `using_directives`, `external_types`, `uses_unsafe` |
| `file_summary` (project) | every `.csproj` | metadata includes `sdk`, `target_frameworks`, `root_namespace`, `assembly_name`, `lang_version`, `nullable`, `implicit_usings`, `package_references` (name + version), `project_references` |
| `file_summary` (solution) | every `.sln` | metadata lists all project paths |
| `class_summary` | classes, interfaces, records, structs, enums, **delegates**, nested types | base types, implements, attributes, abstract/sealed/partial flags |
| `method_summary` | methods, properties, **constructors**, **destructors**, **indexers**, **operators**, **conversion operators**, **fields** (each variable in a declaration), **events** (both forms), **enum members**, **local functions** | per-symbol metadata: signature, return type, parameters, attributes, async/static/const/readonly flags, cyclomatic complexity, **`calls`** (invocation targets), **`instantiates`** (object-creation targets), and `kind` discriminator |
| `search_term_match` | identifier / string-literal / comment / XML-doc hits | repeatable via `--search` — useful for tracking feature-flag keys, business terms, or any token you want callable out explicitly |
| `test_reference` | each method in a test file | framework auto-detected (xUnit, NUnit, MSTest) |
| `configuration_reference` | JSON / YAML / XML / `.env` keys | values redacted by default |

Each method-shaped record also includes the method body (or expression body) as `CodeSnippet`, capped at 200 lines / 4 KB and truncated to a statement boundary. Property bodies, constructor bodies, indexer bodies, operator bodies, and local-function bodies are captured the same way.

### Call graph and dependencies

- **Per method / constructor / local function** — Roslyn walks the body and collects:
  - `calls: [...]` — every `InvocationExpressionSyntax` callee name (up to 50, deduplicated).
  - `instantiates: [...]` — every `ObjectCreationExpressionSyntax` target type (up to 50, deduplicated).
- **Per file** — `using_directives` and `external_types` (base types, return types, parameter types, field types, instantiated types) are aggregated into the `file_summary` metadata.
- **Per project** — `package_references` and `project_references` are extracted from `.csproj` and stored on the project's `file_summary` record so an agent can answer *"which projects depend on Npgsql 8.0?"*.

### Embedding-time tokenization

Before each record's embedding text is built, identifier-shaped fields (`Namespace`, `ClassName`, `MethodName`, `SymbolName`, and the file basename) flow through `IdentifierTokenizer.Split`, which adds the sub-word tokens to a `Tokens:` line in the embedding text. This means the model sees:

| Input | Tokens added |
|---|---|
| `PrePaidAccount` | `PrePaidAccount`, `Pre`, `Paid`, `Account` |
| `processPaymentAsync` | `processPaymentAsync`, `process`, `Payment`, `Async` |
| `XMLParser` | `XMLParser`, `XML`, `Parser` (acronym-aware) |
| `process-payment` | `process-payment`, `process`, `payment` |
| `is_enabled` | `is_enabled`, `is`, `enabled` |
| `Acme.Workflow.WorkflowService` | `Acme`, `Workflow`, `WorkflowService`, `Service` |
| `HttpV2Client` | `Http`, `V`, `2`, `Client` (letter/digit boundary) |

Tokenization runs in-memory inside the embedding pipeline — it does not change what's stored in Postgres, only what the embedding model "reads".

### Project layout

```text
src/
  CodeKb.Contracts/         DTOs and enums shared across modules
  CodeKb.Scanner.Roslyn/    File classifier, project scanner, detectors,
                            snippet builder, redactor, syntax extractor,
                            scanner orchestrator, repository loader
  CodeKb.Embedding/         IEmbeddingClient, OpenAI client, batcher,
                            retry policy, identifier tokenizer,
                            embedding-text builder, pipeline
  CodeKb.Storage.Postgres/  Stores, SQL builder, embedded migrations
  CodeKb.Core/              ConfigLoader, ScanService, SearchService, DI
  CodeKb.Cli/               System.CommandLine entry, scan/ask handlers
tests/
  CodeKb.{Contracts,Scanner,Embedding,Storage,Core,Cli}.Tests/
docs/
  requirements/codekb.md
  specs/codekb-mvp/{requirements,design,tasks}.md
```

---

## CLI reference

### `codekb scan`

```bash
codekb scan [--repo <url> | --path <dir>] [--branch <name>] [--search <term>]... [--force] [--config <path>]
```

| Flag | Description |
|---|---|
| `--repo <url>` | Remote repository URL (mutually exclusive with `--path`) |
| `--path <dir>` | Local repository path (no fetch, no checkout) |
| `--branch <name>` | Branch to scan (default: repo default) |
| `--search <term>` | Track a search term explicitly. Repeatable. |
| `--force` | Re-scan even if the commit SHA is already indexed |
| `--config <path>` | Override config path (default `./config/codekb.yaml`) |

**Behavior**

1. Clone (shallow) or read the local path.
2. Resolve commit SHA on the target branch.
3. If `(repo, branch, commit)` is already indexed and `--force` is not set, exit 0 with a "no-op" message.
4. Mark previous records for the branch as `is_stale = true`.
5. Walk solution / projects / files. Classify each file (production, test, generated, configuration).
6. Extract `class_summary`, `method_summary`, `file_summary`, plus detector hits.
7. Redact secrets (key-based + format-based patterns).
8. Batch-embed all records (with exponential-backoff retry).
9. Insert idempotently via `(repo, file_path, symbol_name, record_type, line_start, commit_sha)` unique key.

### `codekb ask`

```bash
codekb ask "<question>" [filters] [output options]
```

| Flag | Description |
|---|---|
| `--repo <name>` | Filter by repository name. Repeatable. |
| `--branch <name>` | Filter by branch |
| `--record-type <t>` | `file_summary`, `class_summary`, `method_summary`, `search_term_match`, `test_reference`, `configuration_reference`. Repeatable. |
| `--top-k <n>` | Number of results (default 10) |
| `--min-score <f>` | Drop results below this cosine similarity |
| `--format text\|json` | Output format (default text) |
| `--include-stale` | Include records from earlier scans |
| `--include-other-models` | Include records embedded with a different model |
| `--config <path>` | Override config path |

By default codekb excludes stale records and records embedded with a different model, so search results stay coherent across re-scans and model upgrades.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including "already indexed" no-op) |
| `2` | User error (bad flags, conflicting flags, invalid config) |
| `3` | Infrastructure error (DB down, clone failed, embedding API down) |
| `4` | Partial success with redaction-drop failures |

---

## Configuration

codekb reads `config/codekb.yaml` by default. Environment variables override YAML values for secrets and per-host settings.

### YAML schema

```yaml
storage:
  postgresConnectionString: string

embedding:
  provider: openai | azure
  model: string
  modelVersion: string         # optional, defaults to "1"
  dimension: int               # must match the pgvector column width
  batchSize: int               # capped by provider per-call limit
  maxRetries: int
  retryBackoffSeconds: float   # exponential base

scanner:
  ignorePaths: [string]
  parallelism: int
  maxFileSizeKb: int
```

### Environment overrides

| Var | Maps to |
|---|---|
| `EMBEDDING_API_KEY` | `embedding.apiKey` |
| `EMBEDDING_ENDPOINT` | `embedding.endpoint` |
| `EMBEDDING_MODEL` | `embedding.model` |
| `EMBEDDING_PROVIDER` | `embedding.provider` |
| `EMBEDDING_MODEL_DIMENSION` | `embedding.dimension` |
| `CODEKB__STORAGE__POSTGRESCONNECTIONSTRING` | `storage.postgresConnectionString` |
| `CODEKB__EMBEDDING__*` | Nested embedding fields |
| `GIT_TOKEN` / `GIT_USERNAME` | Used by the repository loader for private HTTPS clones |

### Credential handling

- API keys, Git tokens, and connection strings **must** come from environment variables (or your OS keychain via env injection).
- YAML files that contain a credential key (`gitToken`, `embeddingApiKey`, etc.) are rejected at load time.
- SSH-URL clones delegate authentication to your existing `ssh-agent`.

---

## Security

codekb is paranoid about leaking secrets into the index:

- Every code snippet flows through a redactor before storage **and** before being sent to the embedding API.
- Key-based redaction covers `password`, `secret`, `token`, `api_key`, `connection_string`, `client_secret`, `private_key`.
- Format-based redaction covers AWS access keys, GitHub PATs, JWTs, PEM private-key blocks.
- `.env` values are always redacted.
- If a known secret pattern appears inside a construct the redactor cannot safely transform (e.g., a C# interpolated string), the **record is dropped entirely** and a counter (`records_redaction_failed`) is incremented on the scan job.
- Logs never include source files, raw vectors, API keys, or Git credentials.

See [SECURITY.md](SECURITY.md) for the vulnerability-disclosure policy.

---

## Development

### Build and test

```bash
dotnet build codekb.sln
dotnet test codekb.sln
```

### Coverage

```bash
dotnet test codekb.sln --collect:"XPlat Code Coverage" --results-directory ./coverage
```

Cobertura reports land under `coverage/<run-id>/coverage.cobertura.xml`. Use [ReportGenerator](https://github.com/danielpalme/ReportGenerator) for an HTML view:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:'coverage/**/coverage.cobertura.xml' -targetdir:coverage/html
open coverage/html/index.html
```

### Testing & coverage

| Project | Tests |
|---|---|
| CodeKb.Contracts.Tests | 31 |
| CodeKb.Scanner.Tests | 152 |
| CodeKb.Embedding.Tests | 41 |
| CodeKb.Storage.Tests | 11 |
| CodeKb.Core.Tests | 24 |
| CodeKb.Cli.Tests | 19 |
| **Total** | **278** |

- All tests pure-unit (in-process). No Postgres or network dependency.
- **Line coverage: 92.52%**. Live-infrastructure code (Postgres stores, DI composition root, LibGit2Sharp clone path, CLI entry point) is marked `[ExcludeFromCodeCoverage]` since it requires integration tests (Postgres container, real network).

### Spec-driven development

This project follows a spec-driven workflow. Before changing behavior, refresh the relevant artifact:

- [docs/requirements/codekb.md](docs/requirements/codekb.md) — high-level requirements
- [docs/specs/codekb-mvp/requirements.md](docs/specs/codekb-mvp/requirements.md) — EARS-style acceptance criteria
- [docs/specs/codekb-mvp/design.md](docs/specs/codekb-mvp/design.md) — technical design
- [docs/specs/codekb-mvp/tasks.md](docs/specs/codekb-mvp/tasks.md) — implementation task checklist

---

## Roadmap

What's **in** the MVP (and present in this repo):

- C# scanning via Roslyn (`.sln` → `.csproj` → syntax-only fallback)
- Full syntactic coverage: classes, interfaces, records, structs, enums, delegates, nested types, methods, properties, constructors, destructors, indexers, operators, conversion operators, fields, events, enum members, local functions
- Method-implementation snippets (body or expression-body, capped at 200 lines / 4 KB)
- **Per-method call graph** (`calls` + `instantiates`) extracted syntactically
- **Per-file dependencies** (`using_directives` + `external_types`)
- **Project + solution metadata** (`.csproj` and `.sln`: SDK, target frameworks, package references, project references, root namespace, assembly name, lang version, nullable, implicit usings)
- **Embedding-time identifier tokenization** (PascalCase / camelCase with acronym handling, kebab-case, snake_case, dotted names, path segments) — means feature-flag keys and other identifier-shaped tokens are retrievable through plain semantic search; no dedicated detector required
- Configuration scanning (JSON, YAML, XML, `.env`)
- Secret redaction
- Search-term records (identifier / literal / comment / XML-doc)
- OpenAI / Azure OpenAI embeddings with retry & batching
- Postgres + pgvector storage with HNSW index, stale-marking, idempotent insert
- Text and JSON output for `ask`

What's **out** of the MVP (deliberately):

- Semantic-model (cross-file) call graph — current call graph is syntactic only
- OmniSharp integration
- LangGraph / agent orchestration
- Automatic code modification or PR creation
- Multi-language support (C# only)
- Cross-language migration

These can come once the ingestion worker is proven in production.

---

## Contributing

Contributions welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow, coding style, and tests-must-pass policy. By participating you agree to follow our [Code of Conduct](CODE_OF_CONDUCT.md).

## Security disclosure

Please **do not** open public issues for security vulnerabilities. Follow the responsible-disclosure process in [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE) © Zoff-Tech contributors.
