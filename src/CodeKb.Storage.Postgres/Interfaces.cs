using CodeKb.Contracts;

namespace CodeKb.Storage.Postgres;

public interface IRepositoryStore
{
    Task<RepositoryRow> UpsertAsync(string name, string? url, string? localPath, string branch, string commitSha, CancellationToken ct);
    Task<RepositoryRow?> GetByNameAsync(string name, CancellationToken ct);
}

public interface IScanJobStore
{
    Task<Guid> StartAsync(Guid repoId, string branch, string commitSha, CancellationToken ct);
    Task FinishAsync(Guid scanJobId, ScanJobOutcome outcome, CancellationToken ct);
    Task<bool> HasCompletedAsync(Guid repoId, string branch, string commitSha, CancellationToken ct);
}

public interface ICodeRecordStore
{
    Task MarkStaleAsync(Guid repoId, string branch, CancellationToken ct);
    Task<int> InsertBatchAsync(IReadOnlyList<CodeRecord> records, CancellationToken ct);
    Task UpdateEmbeddingsAsync(IReadOnlyList<EmbeddingRow> rows, CancellationToken ct);
    Task MarkEmbeddingFailedAsync(IReadOnlyList<Guid> recordIds, CancellationToken ct);
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct);
}

public sealed record SearchQuery(
    float[] QueryVector,
    string EmbeddingModel,
    int TopK,
    IReadOnlyList<string> Repositories,
    string? Branch,
    IReadOnlyList<RecordType> RecordTypes,
    string? FeatureFlag,
    bool IncludeStale,
    bool IncludeOtherModels);
