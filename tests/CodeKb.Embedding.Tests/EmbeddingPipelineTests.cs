using CodeKb.Contracts;
using CodeKb.Embedding;
using FluentAssertions;
using Xunit;

namespace CodeKb.Embedding.Tests;

public class EmbeddingPipelineTests
{
    private sealed class FakeClient : IEmbeddingClient
    {
        public string ModelId => "fake-model";
        public string ModelVersion => "v1";
        public int Dimension => 3;
        public int Calls;
        public Func<IReadOnlyList<string>, IReadOnlyList<float[]>> Responder { get; set; }
            = inputs => inputs.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToArray();

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> inputs, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Responder(inputs));
        }
    }

    private static CodeRecord Record(int i) => new()
    {
        RepositoryName = "r", Branch = "main", CommitSha = "sha", FilePath = $"f{i}.cs",
        LineStart = 1, LineEnd = 1, RecordType = RecordType.FileSummary,
        Summary = $"s{i}", CodeSnippet = $"snippet {i}",
    };

    [Fact]
    public async Task EmbedAsync_Success_AllRowsReturned()
    {
        var client = new FakeClient();
        var pipeline = new EmbeddingPipeline(client, new RetryPolicy(0, 0, sleep: (_, _) => Task.CompletedTask), 2);
        var records = new[] { Record(1), Record(2), Record(3) };
        var result = await pipeline.EmbedAsync(records, CancellationToken.None);
        result.Succeeded.Should().HaveCount(3);
        result.FailedRecordIds.Should().BeEmpty();
        client.Calls.Should().Be(2);
    }

    [Fact]
    public async Task EmbedAsync_BatchFails_MarksRecordsFailed()
    {
        var client = new FakeClient
        {
            Responder = _ => throw new EmbeddingException("dead"),
        };
        var pipeline = new EmbeddingPipeline(client, new RetryPolicy(1, 0, sleep: (_, _) => Task.CompletedTask), 2);
        var records = new[] { Record(1), Record(2) };
        var result = await pipeline.EmbedAsync(records, CancellationToken.None);
        result.Succeeded.Should().BeEmpty();
        result.FailedRecordIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task EmbedAsync_RetriesUntilSuccess()
    {
        int n = 0;
        var client = new FakeClient
        {
            Responder = inputs =>
            {
                n++;
                if (n < 3) throw new EmbeddingException("transient");
                return inputs.Select(_ => new[] { 1f, 2f, 3f }).ToArray();
            },
        };
        var pipeline = new EmbeddingPipeline(client, new RetryPolicy(5, 0, sleep: (_, _) => Task.CompletedTask), 10);
        var result = await pipeline.EmbedAsync(new[] { Record(1) }, CancellationToken.None);
        result.Succeeded.Should().HaveCount(1);
        n.Should().Be(3);
    }

    [Fact]
    public void Ctor_InvalidBatchSize_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new EmbeddingPipeline(new FakeClient(), new RetryPolicy(0, 0), 0));
}
