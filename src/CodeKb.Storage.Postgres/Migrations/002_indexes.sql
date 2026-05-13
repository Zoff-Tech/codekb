CREATE INDEX IF NOT EXISTS code_record_filter_idx
    ON code_record (repository_id, branch, is_stale);

CREATE INDEX IF NOT EXISTS code_record_feature_flag_idx
    ON code_record (feature_flag_name)
    WHERE feature_flag_name IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS code_record_unique_idx
    ON code_record (repository_id, file_path, symbol_name, record_type, line_start, commit_sha);

CREATE INDEX IF NOT EXISTS code_embedding_hnsw_idx
    ON code_embedding USING hnsw (embedding_vector vector_cosine_ops);
