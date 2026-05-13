using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeKb.Embedding;
using FluentAssertions;
using Xunit;

namespace CodeKb.Embedding.Tests;

public class OpenAIEmbeddingClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Responder(request));
        }
    }

    private static OpenAIEmbeddingClient BuildClient(StubHandler handler, int dim = 3, string apiKey = "sk-test")
        => new(new HttpClient(handler), apiKey, "text-embedding-3-small", dim);

    [Fact]
    public void Ctor_ValidatesArgs()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenAIEmbeddingClient(null!, "k", "m", 1));
        Assert.Throws<ArgumentException>(() => new OpenAIEmbeddingClient(new HttpClient(new StubHandler()), "", "m", 1));
        Assert.Throws<ArgumentException>(() => new OpenAIEmbeddingClient(new HttpClient(new StubHandler()), "k", "", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAIEmbeddingClient(new HttpClient(new StubHandler()), "k", "m", 0));
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyInput_ReturnsEmpty()
    {
        var client = BuildClient(new StubHandler());
        (await client.EmbedBatchAsync(Array.Empty<string>(), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task EmbedBatchAsync_Success_ReturnsVectors()
    {
        var handler = new StubHandler();
        handler.Responder = _ =>
        {
            var payload = new
            {
                data = new[]
                {
                    new { index = 0, embedding = new[] { 1f, 2f, 3f } },
                    new { index = 1, embedding = new[] { 4f, 5f, 6f } },
                },
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload),
            };
        };
        var client = BuildClient(handler);
        var result = await client.EmbedBatchAsync(new[] { "a", "b" }, CancellationToken.None);
        result.Should().HaveCount(2);
        result[0].Should().Equal(1f, 2f, 3f);
        result[1].Should().Equal(4f, 5f, 6f);
    }

    [Fact]
    public async Task EmbedBatchAsync_OrderByIndex()
    {
        var handler = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new[]
                    {
                        new { index = 1, embedding = new[] { 4f, 5f, 6f } },
                        new { index = 0, embedding = new[] { 1f, 2f, 3f } },
                    },
                }),
            },
        };
        var client = BuildClient(handler);
        var result = await client.EmbedBatchAsync(new[] { "a", "b" }, CancellationToken.None);
        result[0].Should().Equal(1f, 2f, 3f);
    }

    [Fact]
    public async Task EmbedBatchAsync_HttpError_Throws()
    {
        var handler = new StubHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oops") } };
        var client = BuildClient(handler);
        var ex = await Assert.ThrowsAsync<EmbeddingException>(() =>
            client.EmbedBatchAsync(new[] { "x" }, CancellationToken.None));
        ex.Reason.Should().Be("500");
    }

    [Fact]
    public async Task EmbedBatchAsync_NetworkError_Throws()
    {
        var handler = new StubHandler { Responder = _ => throw new HttpRequestException("dns fail") };
        var client = BuildClient(handler);
        var ex = await Assert.ThrowsAsync<EmbeddingException>(() =>
            client.EmbedBatchAsync(new[] { "x" }, CancellationToken.None));
        ex.Reason.Should().Be("network");
    }

    [Fact]
    public async Task EmbedBatchAsync_DimensionMismatch_Throws()
    {
        var handler = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { data = new[] { new { index = 0, embedding = new[] { 1f, 2f } } } }),
            },
        };
        var client = BuildClient(handler, dim: 3);
        await Assert.ThrowsAsync<EmbeddingException>(() =>
            client.EmbedBatchAsync(new[] { "x" }, CancellationToken.None));
    }

    [Fact]
    public async Task EmbedBatchAsync_CountMismatch_Throws()
    {
        var handler = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { data = new[] { new { index = 0, embedding = new[] { 1f, 2f, 3f } } } }),
            },
        };
        var client = BuildClient(handler, dim: 3);
        await Assert.ThrowsAsync<EmbeddingException>(() =>
            client.EmbedBatchAsync(new[] { "a", "b" }, CancellationToken.None));
    }
}
