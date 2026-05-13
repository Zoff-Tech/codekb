using CodeKb.Embedding;
using FluentAssertions;
using Xunit;

namespace CodeKb.Embedding.Tests;

public class BatcherTests
{
    [Fact]
    public void Batch_FullAndPartialBatches()
    {
        var batches = Batcher.Batch(Enumerable.Range(0, 7), 3).Select(b => b.ToArray()).ToArray();
        batches.Should().HaveCount(3);
        batches[0].Should().Equal(0, 1, 2);
        batches[1].Should().Equal(3, 4, 5);
        batches[2].Should().Equal(6);
    }

    [Fact]
    public void Batch_EmptyInput()
        => Batcher.Batch(Array.Empty<int>(), 3).Should().BeEmpty();

    [Fact]
    public void Batch_InvalidSize_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Batcher.Batch(new[] { 1 }, 0).ToArray());

    [Fact]
    public void Batch_ExactMultiple()
    {
        var b = Batcher.Batch(new[] { 1, 2, 3, 4 }, 2).Select(x => x.ToArray()).ToArray();
        b.Should().HaveCount(2);
    }
}
