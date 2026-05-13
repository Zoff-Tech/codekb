using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CodeKb.Embedding;

public sealed class OpenAIEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string _apiKey;

    public string ModelId { get; }
    public string ModelVersion { get; }
    public int Dimension { get; }

    public OpenAIEmbeddingClient(HttpClient http, string apiKey, string model, int dimension, string? endpoint = null, string modelVersion = "1")
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey required", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("model required", nameof(model));
        if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));

        _apiKey = apiKey;
        _model = model;
        _endpoint = (endpoint ?? "https://api.openai.com").TrimEnd('/') + "/v1/embeddings";
        Dimension = dimension;
        ModelId = model;
        ModelVersion = modelVersion;
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0) return Array.Empty<float[]>();
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
        req.Content = JsonContent.Create(new EmbeddingRequest(_model, inputs.ToArray()));

        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new EmbeddingException("network error", "network", ex); }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new EmbeddingException($"OpenAI returned {(int)resp.StatusCode}: {body}", reason: ((int)resp.StatusCode).ToString());
        }

        var payload = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct)
            ?? throw new EmbeddingException("Empty OpenAI response");
        if (payload.Data is null || payload.Data.Count != inputs.Count)
            throw new EmbeddingException($"OpenAI returned {payload.Data?.Count ?? 0} vectors, expected {inputs.Count}");

        var sorted = payload.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToArray();
        foreach (var v in sorted)
            if (v.Length != Dimension)
                throw new EmbeddingException($"Dimension mismatch: expected {Dimension}, got {v.Length}");
        return sorted;
    }

    public sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input);

    public sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingDatum> Data);

    public sealed record EmbeddingDatum(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
