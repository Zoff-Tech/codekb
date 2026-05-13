using CodeKb.Contracts;

namespace CodeKb.Embedding;

public sealed record EmbeddingResult(IReadOnlyList<EmbeddingRow> Succeeded, IReadOnlyList<Guid> FailedRecordIds);

public sealed class EmbeddingPipeline
{
    private readonly IEmbeddingClient _client;
    private readonly RetryPolicy _retry;
    private readonly int _batchSize;

    public EmbeddingPipeline(IEmbeddingClient client, RetryPolicy retry, int batchSize)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        _client = client;
        _retry = retry;
        _batchSize = batchSize;
    }

    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<CodeRecord> records, CancellationToken ct)
    {
        var success = new List<EmbeddingRow>(records.Count);
        var failed = new List<Guid>();

        foreach (var batch in Batcher.Batch(records, _batchSize))
        {
            var texts = batch.Select(EmbeddingTextBuilder.Build).ToArray();
            IReadOnlyList<float[]>? vectors = null;
            try
            {
                vectors = await _retry.ExecuteAsync(
                    ctk => _client.EmbedBatchAsync(texts, ctk),
                    shouldRetry: ex => ex is EmbeddingException,
                    ct);
            }
            catch (EmbeddingException)
            {
                foreach (var r in batch) failed.Add(r.Id);
                continue;
            }

            for (int i = 0; i < batch.Count; i++)
            {
                success.Add(new EmbeddingRow(
                    batch[i].Id,
                    _client.ModelId,
                    _client.ModelVersion,
                    _client.Dimension,
                    vectors[i],
                    texts[i]));
            }
        }

        return new EmbeddingResult(success, failed);
    }
}
