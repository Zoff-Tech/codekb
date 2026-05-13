using CodeKb.Contracts;
using CodeKb.Embedding;
using CodeKb.Scanner.Roslyn;
using CodeKb.Storage.Postgres;

namespace CodeKb.Core.Tests;

internal sealed class FakeRepositoryLoader : IRepositoryLoader
{
    public LoadedRepository Loaded { get; set; } = new("/tmp/repo", "fake-repo", "main", "abc1234", null);
    public Task<LoadedRepository> LoadAsync(RepoSource source, CancellationToken ct) => Task.FromResult(Loaded);
}

internal sealed class FakeScanner : IRoslynScanner
{
    public List<CodeRecord> Records { get; set; } = new();
    public ScanCounters Counters { get; } = new();

    public async IAsyncEnumerable<CodeRecord> ScanAsync(LoadedRepository repo, ScanContext context, ScanOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var rec in Records)
        {
            Counters.FilesScanned++;
            Counters.RecordsCreated++;
            yield return rec with
            {
                RepositoryId = context.RepositoryId,
                ScanJobId = context.ScanJobId,
                RepositoryName = context.RepositoryName,
                Branch = context.Branch,
                CommitSha = context.CommitSha,
            };
            await Task.Yield();
        }
    }
}

internal sealed class FakeRepositoryStore : IRepositoryStore
{
    public Dictionary<string, RepositoryRow> ByName { get; } = new();
    public Task<RepositoryRow> UpsertAsync(string name, string? url, string? localPath, string branch, string commitSha, CancellationToken ct)
    {
        if (ByName.TryGetValue(name, out var existing))
        {
            var refreshed = existing with { Branch = branch, CommitSha = commitSha };
            ByName[name] = refreshed;
            return Task.FromResult(refreshed);
        }
        var row = new RepositoryRow(Guid.NewGuid(), name, url, localPath, branch, commitSha);
        ByName[name] = row;
        return Task.FromResult(row);
    }
    public Task<RepositoryRow?> GetByNameAsync(string name, CancellationToken ct)
        => Task.FromResult(ByName.TryGetValue(name, out var r) ? r : null);
}

internal sealed class FakeScanJobStore : IScanJobStore
{
    public Dictionary<Guid, (Guid repo, string branch, string sha, ScanJobOutcome? outcome)> Jobs { get; } = new();
    public HashSet<(Guid repo, string branch, string sha)> Completed { get; } = new();
    public Task<Guid> StartAsync(Guid repoId, string branch, string commitSha, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        Jobs[id] = (repoId, branch, commitSha, null);
        return Task.FromResult(id);
    }
    public Task FinishAsync(Guid scanJobId, ScanJobOutcome outcome, CancellationToken ct)
    {
        var prev = Jobs[scanJobId];
        Jobs[scanJobId] = (prev.repo, prev.branch, prev.sha, outcome);
        if (outcome.Status == ScanStatus.Completed)
            Completed.Add((prev.repo, prev.branch, prev.sha));
        return Task.CompletedTask;
    }
    public Task<bool> HasCompletedAsync(Guid repoId, string branch, string commitSha, CancellationToken ct)
        => Task.FromResult(Completed.Contains((repoId, branch, commitSha)));
}

internal sealed class FakeCodeRecordStore : ICodeRecordStore
{
    public List<CodeRecord> Inserted { get; } = new();
    public List<EmbeddingRow> Embeddings { get; } = new();
    public List<Guid> FailedEmbeddings { get; } = new();
    public HashSet<(Guid repo, string branch)> StaleMarked { get; } = new();
    public Func<SearchQuery, IReadOnlyList<SearchHit>> SearchResponder { get; set; } = _ => Array.Empty<SearchHit>();

    public Task MarkStaleAsync(Guid repoId, string branch, CancellationToken ct)
    {
        StaleMarked.Add((repoId, branch));
        return Task.CompletedTask;
    }
    public Task<int> InsertBatchAsync(IReadOnlyList<CodeRecord> records, CancellationToken ct)
    {
        Inserted.AddRange(records);
        return Task.FromResult(records.Count);
    }
    public Task UpdateEmbeddingsAsync(IReadOnlyList<EmbeddingRow> rows, CancellationToken ct)
    {
        Embeddings.AddRange(rows);
        return Task.CompletedTask;
    }
    public Task MarkEmbeddingFailedAsync(IReadOnlyList<Guid> recordIds, CancellationToken ct)
    {
        FailedEmbeddings.AddRange(recordIds);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
        => Task.FromResult(SearchResponder(query));
}

internal sealed class FakeEmbeddingClient : IEmbeddingClient
{
    public string ModelId { get; set; } = "fake-model";
    public string ModelVersion { get; set; } = "v1";
    public int Dimension { get; set; } = 3;
    public Func<IReadOnlyList<string>, IReadOnlyList<float[]>> Responder { get; set; }
        = inputs => inputs.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray();
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> inputs, CancellationToken ct)
        => Task.FromResult(Responder(inputs));
}
