using CodeKb.Contracts;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace CodeKb.Storage.Postgres;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Requires live Postgres; covered by integration tests.")]
public sealed class PostgresRepositoryStore : IRepositoryStore
{
    private readonly NpgsqlDataSource _ds;
    public PostgresRepositoryStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task<RepositoryRow> UpsertAsync(string name, string? url, string? localPath, string branch, string commitSha, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(@"
INSERT INTO repository (id, name, url, local_path, branch, commit_sha, last_scanned_at)
VALUES (@id, @name, @url, @local, @branch, @sha, NOW())
ON CONFLICT (name, branch) DO UPDATE
    SET url = EXCLUDED.url, local_path = EXCLUDED.local_path, commit_sha = EXCLUDED.commit_sha,
        last_scanned_at = NOW(), updated_at = NOW()
RETURNING id");
        var id = Guid.NewGuid();
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("url", (object?)url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("local", (object?)localPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("branch", branch);
        cmd.Parameters.AddWithValue("sha", commitSha);
        var resolved = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        return new RepositoryRow(resolved, name, url, localPath, branch, commitSha);
    }

    public async Task<RepositoryRow?> GetByNameAsync(string name, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT id, name, url, local_path, branch, commit_sha FROM repository WHERE name = @name LIMIT 1");
        cmd.Parameters.AddWithValue("name", name);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new RepositoryRow(r.GetGuid(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.GetString(4), r.GetString(5));
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Requires live Postgres.")]
public sealed class PostgresScanJobStore : IScanJobStore
{
    private readonly NpgsqlDataSource _ds;
    public PostgresScanJobStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task<Guid> StartAsync(Guid repoId, string branch, string commitSha, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var cmd = _ds.CreateCommand(@"
INSERT INTO scan_job (id, repository_id, branch, commit_sha, status, started_at)
VALUES (@id, @repo, @branch, @sha, 'running', NOW())");
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("repo", repoId);
        cmd.Parameters.AddWithValue("branch", branch);
        cmd.Parameters.AddWithValue("sha", commitSha);
        await cmd.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task FinishAsync(Guid scanJobId, ScanJobOutcome outcome, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(@"
UPDATE scan_job
SET status = @status, completed_at = NOW(), records_created = @c, records_failed = @f, error_message = @err
WHERE id = @id");
        cmd.Parameters.AddWithValue("id", scanJobId);
        cmd.Parameters.AddWithValue("status", outcome.Status switch
        {
            ScanStatus.Completed => "completed",
            ScanStatus.Failed => "failed",
            _ => "completed",
        });
        cmd.Parameters.AddWithValue("c", outcome.RecordsCreated);
        cmd.Parameters.AddWithValue("f", outcome.RecordsFailed);
        cmd.Parameters.AddWithValue("err", (object?)outcome.ErrorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> HasCompletedAsync(Guid repoId, string branch, string commitSha, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(@"
SELECT 1 FROM scan_job
WHERE repository_id = @r AND branch = @b AND commit_sha = @s AND status = 'completed'
LIMIT 1");
        cmd.Parameters.AddWithValue("r", repoId);
        cmd.Parameters.AddWithValue("b", branch);
        cmd.Parameters.AddWithValue("s", commitSha);
        var x = await cmd.ExecuteScalarAsync(ct);
        return x is not null;
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Requires live Postgres.")]
public sealed class PostgresCodeRecordStore : ICodeRecordStore
{
    private readonly NpgsqlDataSource _ds;
    public PostgresCodeRecordStore(NpgsqlDataSource ds) => _ds = ds;

    public async Task MarkStaleAsync(Guid repoId, string branch, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "UPDATE code_record SET is_stale = TRUE, updated_at = NOW() WHERE repository_id = @r AND branch = @b AND is_stale = FALSE");
        cmd.Parameters.AddWithValue("r", repoId);
        cmd.Parameters.AddWithValue("b", branch);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> InsertBatchAsync(IReadOnlyList<CodeRecord> records, CancellationToken ct)
    {
        int inserted = 0;
        foreach (var r in records)
        {
            await using var cmd = _ds.CreateCommand(@"
INSERT INTO code_record (id, repository_id, scan_job_id, repository_name, branch, commit_sha,
    file_path, line_start, line_end, record_type, symbol_name, symbol_kind, namespace,
    class_name, method_name, summary, code_snippet,
    metadata_json, is_test_code, is_generated_code, is_stale, embedding_status)
VALUES (@id, @repo, @job, @repo_name, @branch, @sha, @fp, @ls, @le, @rt,
    @sname, @skind, @ns, @cls, @mname, @summary, @snippet,
    @meta::jsonb, @test, @gen, FALSE, 'pending')
ON CONFLICT (repository_id, file_path, symbol_name, record_type, line_start, commit_sha) DO NOTHING");
            cmd.Parameters.AddWithValue("id", r.Id);
            cmd.Parameters.AddWithValue("repo", r.RepositoryId);
            cmd.Parameters.AddWithValue("job", r.ScanJobId);
            cmd.Parameters.AddWithValue("repo_name", r.RepositoryName);
            cmd.Parameters.AddWithValue("branch", r.Branch);
            cmd.Parameters.AddWithValue("sha", r.CommitSha);
            cmd.Parameters.AddWithValue("fp", r.FilePath);
            cmd.Parameters.AddWithValue("ls", r.LineStart);
            cmd.Parameters.AddWithValue("le", r.LineEnd);
            cmd.Parameters.AddWithValue("rt", r.RecordType.ToWire());
            cmd.Parameters.AddWithValue("sname", (object?)r.SymbolName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("skind", r.SymbolKind.ToString());
            cmd.Parameters.AddWithValue("ns", (object?)r.Namespace ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cls", (object?)r.ClassName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("mname", (object?)r.MethodName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("summary", r.Summary);
            cmd.Parameters.AddWithValue("snippet", r.CodeSnippet);
            cmd.Parameters.AddWithValue("meta", r.MetadataJson);
            cmd.Parameters.AddWithValue("test", r.IsTestCode);
            cmd.Parameters.AddWithValue("gen", r.IsGeneratedCode);
            inserted += await cmd.ExecuteNonQueryAsync(ct);
        }
        return inserted;
    }

    public async Task UpdateEmbeddingsAsync(IReadOnlyList<EmbeddingRow> rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            await using var cmd = _ds.CreateCommand(@"
INSERT INTO code_embedding (id, code_record_id, embedding_model, embedding_model_version,
    embedding_dimension, embedding_vector, embedding_text)
VALUES (@id, @rec, @model, @ver, @dim, @vec, @text);
UPDATE code_record SET embedding_status = 'embedded', updated_at = NOW() WHERE id = @rec");
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("rec", row.CodeRecordId);
            cmd.Parameters.AddWithValue("model", row.EmbeddingModel);
            cmd.Parameters.AddWithValue("ver", row.EmbeddingModelVersion);
            cmd.Parameters.AddWithValue("dim", row.EmbeddingDimension);
            cmd.Parameters.Add(new NpgsqlParameter("vec", NpgsqlDbType.Unknown) { Value = new Vector(row.Vector) });
            cmd.Parameters.AddWithValue("text", row.EmbeddingText);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task MarkEmbeddingFailedAsync(IReadOnlyList<Guid> recordIds, CancellationToken ct)
    {
        if (recordIds.Count == 0) return;
        await using var cmd = _ds.CreateCommand(
            "UPDATE code_record SET embedding_status = 'failed', updated_at = NOW() WHERE id = ANY(@ids)");
        cmd.Parameters.AddWithValue("ids", recordIds.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        var built = SearchSqlBuilder.Build(query);
        await using var cmd = _ds.CreateCommand(built.Sql);
        foreach (var (name, value) in built.Parameters)
        {
            if (name == "query_vec")
                cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Unknown) { Value = new Vector((float[])value!) });
            else
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var hits = new List<SearchHit>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            hits.Add(new SearchHit(
                Repository: r.GetString(0),
                Branch: r.GetString(1),
                CommitSha: r.GetString(2),
                FilePath: r.GetString(3),
                LineStart: r.GetInt32(4),
                LineEnd: r.GetInt32(5),
                SymbolName: r.IsDBNull(6) ? null : r.GetString(6),
                RecordType: RecordTypes.FromWire(r.GetString(7)),
                Summary: r.GetString(8),
                CodeSnippet: r.GetString(9),
                Score: r.GetDouble(10)));
        }
        return hits;
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Wraps Npgsql native code; integration only.")]
public static class NpgsqlDataSourceFactory
{
    public static NpgsqlDataSource Build(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        return builder.Build();
    }
}
