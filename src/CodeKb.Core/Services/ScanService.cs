using System.Diagnostics;
using CodeKb.Contracts;
using CodeKb.Core.Configuration;
using CodeKb.Embedding;
using CodeKb.Scanner.Roslyn;
using CodeKb.Storage.Postgres;
using CodeKb.Storage.Postgres.Migrations;
using Microsoft.Extensions.Logging;

namespace CodeKb.Core.Services;

public sealed class ScanService : IScanService
{
    private readonly CodeKbOptions _options;
    private readonly IRepositoryLoader _loader;
    private readonly IRoslynScanner _scanner;
    private readonly IRepositoryStore _repoStore;
    private readonly IScanJobStore _jobStore;
    private readonly ICodeRecordStore _recordStore;
    private readonly EmbeddingPipeline _embedding;
    private readonly IDatabaseInitializer _initializer;
    private readonly ILogger<ScanService> _logger;

    public ScanService(
        CodeKbOptions options,
        IRepositoryLoader loader,
        IRoslynScanner scanner,
        IRepositoryStore repoStore,
        IScanJobStore jobStore,
        ICodeRecordStore recordStore,
        EmbeddingPipeline embedding,
        IDatabaseInitializer initializer,
        ILogger<ScanService> logger)
    {
        _options = options;
        _loader = loader;
        _scanner = scanner;
        _repoStore = repoStore;
        _jobStore = jobStore;
        _recordStore = recordStore;
        _embedding = embedding;
        _initializer = initializer;
        _logger = logger;
    }

    public async Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken ct)
    {
        request.Validate();
        var sw = Stopwatch.StartNew();

        await _initializer.InitializeAsync(ct);

        var loaded = await _loader.LoadAsync(
            new RepoSource(request.RepoUrl, request.Path, request.Branch), ct);

        var repo = await _repoStore.UpsertAsync(loaded.Name, loaded.OriginalUrl, loaded.LocalRoot, loaded.Branch, loaded.CommitSha, ct);

        if (!request.Force && await _jobStore.HasCompletedAsync(repo.Id, loaded.Branch, loaded.CommitSha, ct))
        {
            _logger.LogInformation("scan_already_indexed repo={Repo} branch={Branch} commit={Commit}",
                loaded.Name, loaded.Branch, loaded.CommitSha);
            return new ScanResult
            {
                ScanJobId = Guid.Empty,
                RepositoryName = loaded.Name,
                Branch = loaded.Branch,
                CommitSha = loaded.CommitSha,
                AlreadyIndexed = true,
                Duration = sw.Elapsed,
                Outcome = new ScanJobOutcome(ScanStatus.Completed, 0, 0, 0, 0, 0, null),
            };
        }

        var scanJobId = await _jobStore.StartAsync(repo.Id, loaded.Branch, loaded.CommitSha, ct);

        await _recordStore.MarkStaleAsync(repo.Id, loaded.Branch, ct);

        var context = new ScanContext(repo.Id, scanJobId, loaded.Name, loaded.Branch, loaded.CommitSha, loaded.LocalRoot);
        var options = new ScanOptions
        {
            IgnorePaths = _options.Scanner.IgnorePaths,
            Parallelism = _options.Scanner.Parallelism,
            MaxFileSizeKb = _options.Scanner.MaxFileSizeKb,
            SearchTerms = request.SearchTerms,
        };

        var records = new List<CodeRecord>();
        await foreach (var rec in _scanner.ScanAsync(loaded, context, options, ct))
            records.Add(rec);

        var insertedCount = await _recordStore.InsertBatchAsync(records, ct);

        var embedResult = await _embedding.EmbedAsync(records, ct);
        await _recordStore.UpdateEmbeddingsAsync(embedResult.Succeeded, ct);
        if (embedResult.FailedRecordIds.Count > 0)
            await _recordStore.MarkEmbeddingFailedAsync(embedResult.FailedRecordIds, ct);

        var counters = _scanner.Counters;
        var outcome = new ScanJobOutcome(
            Status: ScanStatus.Completed,
            FilesScanned: counters.FilesScanned,
            RecordsCreated: insertedCount,
            RecordsFailed: counters.RecordsFailed,
            RecordsRedactionFailed: counters.RecordsRedactionFailed,
            EmbeddingsCreated: embedResult.Succeeded.Count,
            ErrorMessage: null);

        await _jobStore.FinishAsync(scanJobId, outcome, ct);
        sw.Stop();

        return new ScanResult
        {
            ScanJobId = scanJobId,
            RepositoryName = loaded.Name,
            Branch = loaded.Branch,
            CommitSha = loaded.CommitSha,
            AlreadyIndexed = false,
            Duration = sw.Elapsed,
            Outcome = outcome,
        };
    }
}
