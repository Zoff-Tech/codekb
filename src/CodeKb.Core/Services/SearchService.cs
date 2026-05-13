using CodeKb.Contracts;
using CodeKb.Core.Configuration;
using CodeKb.Embedding;
using CodeKb.Storage.Postgres;

namespace CodeKb.Core.Services;

public sealed class SearchService : ISearchService
{
    private readonly CodeKbOptions _options;
    private readonly IEmbeddingClient _embedding;
    private readonly ICodeRecordStore _records;

    public SearchService(CodeKbOptions options, IEmbeddingClient embedding, ICodeRecordStore records)
    {
        _options = options;
        _embedding = embedding;
        _records = records;
    }

    public async Task<IReadOnlyList<SearchHit>> AskAsync(SearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("Question must be supplied.");

        var vectors = await _embedding.EmbedBatchAsync(new[] { request.Question }, ct);
        if (vectors.Count == 0)
            return Array.Empty<SearchHit>();

        var recordTypes = request.RecordTypes;
        if (!string.IsNullOrEmpty(request.FeatureFlag) && recordTypes.Count == 0)
            recordTypes = new[] { RecordType.FeatureFlagUsage };

        var query = new SearchQuery(
            QueryVector: vectors[0],
            EmbeddingModel: _embedding.ModelId,
            TopK: request.TopK <= 0 ? 10 : request.TopK,
            Repositories: request.Repositories,
            Branch: request.Branch,
            RecordTypes: recordTypes,
            FeatureFlag: request.FeatureFlag,
            IncludeStale: request.IncludeStale,
            IncludeOtherModels: request.IncludeOtherModels);

        var hits = await _records.SearchAsync(query, ct);

        if (request.MinScore is double minScore)
            hits = hits.Where(h => h.Score >= minScore).ToList();
        return hits;
    }
}
