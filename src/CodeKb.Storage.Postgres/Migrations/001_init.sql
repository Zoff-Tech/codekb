CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS repository (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    url TEXT,
    local_path TEXT,
    branch TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    last_scanned_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (name, branch)
);

CREATE TABLE IF NOT EXISTS scan_job (
    id UUID PRIMARY KEY,
    repository_id UUID NOT NULL REFERENCES repository(id) ON DELETE CASCADE,
    branch TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    status TEXT NOT NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    error_message TEXT,
    records_created INT NOT NULL DEFAULT 0,
    records_failed INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS code_record (
    id UUID PRIMARY KEY,
    repository_id UUID NOT NULL REFERENCES repository(id) ON DELETE CASCADE,
    scan_job_id UUID NOT NULL REFERENCES scan_job(id) ON DELETE CASCADE,
    repository_name TEXT NOT NULL,
    branch TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    file_path TEXT NOT NULL,
    line_start INT NOT NULL,
    line_end INT NOT NULL,
    record_type TEXT NOT NULL,
    symbol_name TEXT,
    symbol_kind TEXT,
    namespace TEXT,
    class_name TEXT,
    method_name TEXT,
    summary TEXT NOT NULL,
    code_snippet TEXT NOT NULL,
    metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_test_code BOOLEAN NOT NULL DEFAULT FALSE,
    is_generated_code BOOLEAN NOT NULL DEFAULT FALSE,
    is_stale BOOLEAN NOT NULL DEFAULT FALSE,
    embedding_status TEXT NOT NULL DEFAULT 'pending',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS code_embedding (
    id UUID PRIMARY KEY,
    code_record_id UUID NOT NULL REFERENCES code_record(id) ON DELETE CASCADE,
    embedding_model TEXT NOT NULL,
    embedding_model_version TEXT NOT NULL,
    embedding_dimension INT NOT NULL,
    embedding_vector vector({{dimension}}) NOT NULL,
    embedding_text TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
