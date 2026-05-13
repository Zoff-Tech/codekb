using CodeKb.Contracts;

namespace CodeKb.Storage.Postgres;

public sealed record BuiltSql(string Sql, IReadOnlyList<(string Name, object? Value)> Parameters);

public static class SearchSqlBuilder
{
    public static BuiltSql Build(SearchQuery q)
    {
        var parameters = new List<(string, object?)>();
        var where = new List<string>();

        parameters.Add(("query_vec", q.QueryVector));
        parameters.Add(("top_k", q.TopK));

        if (!q.IncludeStale) where.Add("cr.is_stale = FALSE");

        if (!q.IncludeOtherModels)
        {
            parameters.Add(("embedding_model", q.EmbeddingModel));
            where.Add("ce.embedding_model = @embedding_model");
        }

        if (q.Repositories.Count > 0)
        {
            parameters.Add(("repos", q.Repositories.ToArray()));
            where.Add("cr.repository_name = ANY(@repos)");
        }

        if (!string.IsNullOrEmpty(q.Branch))
        {
            parameters.Add(("branch", q.Branch));
            where.Add("cr.branch = @branch");
        }

        if (q.RecordTypes.Count > 0)
        {
            parameters.Add(("record_types", q.RecordTypes.Select(r => r.ToWire()).ToArray()));
            where.Add("cr.record_type = ANY(@record_types)");
        }

        if (!string.IsNullOrEmpty(q.FeatureFlag))
        {
            parameters.Add(("feature_flag_name", q.FeatureFlag));
            where.Add("cr.feature_flag_name = @feature_flag_name");
        }

        var whereClause = where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where);
        var sql = $@"
SELECT cr.repository_name, cr.branch, cr.commit_sha, cr.file_path,
       cr.line_start, cr.line_end, cr.symbol_name, cr.record_type,
       cr.summary, cr.code_snippet,
       1 - (ce.embedding_vector <=> @query_vec) AS score
FROM code_record cr
INNER JOIN code_embedding ce ON ce.code_record_id = cr.id
{whereClause}
ORDER BY ce.embedding_vector <=> @query_vec
LIMIT @top_k";
        return new BuiltSql(sql.Trim(), parameters);
    }
}
