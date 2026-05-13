namespace CodeKb.Embedding;

public interface IEmbeddingClient
{
    string ModelId { get; }
    string ModelVersion { get; }
    int Dimension { get; }
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}

public sealed class EmbeddingException : Exception
{
    public string? Reason { get; }
    public EmbeddingException(string message, string? reason = null, Exception? inner = null) : base(message, inner)
    {
        Reason = reason;
    }
}
