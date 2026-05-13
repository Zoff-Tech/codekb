using CodeKb.Embedding;
using FluentAssertions;
using Xunit;

namespace CodeKb.Embedding.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task SucceedsOnFirstAttempt()
    {
        var policy = new RetryPolicy(maxRetries: 3, baseSeconds: 0, sleep: (_, _) => Task.CompletedTask);
        var result = await policy.ExecuteAsync(_ => Task.FromResult(42), null, CancellationToken.None);
        result.Should().Be(42);
    }

    [Fact]
    public async Task RetriesUpToMaxAndThenSucceeds()
    {
        int attempts = 0;
        var policy = new RetryPolicy(maxRetries: 3, baseSeconds: 0, sleep: (_, _) => Task.CompletedTask);
        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3) throw new InvalidOperationException("nope");
            return Task.FromResult("ok");
        }, null, CancellationToken.None);
        result.Should().Be("ok");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task RetriesUpToMaxAndThenThrows()
    {
        var policy = new RetryPolicy(maxRetries: 2, baseSeconds: 0, sleep: (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ => throw new InvalidOperationException("boom"), null, CancellationToken.None));
    }

    [Fact]
    public async Task ShouldRetryFilter_FalseStopsImmediately()
    {
        int attempts = 0;
        var policy = new RetryPolicy(maxRetries: 5, baseSeconds: 0, sleep: (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ => { attempts++; throw new InvalidOperationException(); }, _ => false, CancellationToken.None));
        attempts.Should().Be(1);
    }

    [Fact]
    public void DefaultDelay_Exponential()
    {
        var p = new RetryPolicy(3, 2);
        p.DefaultDelay(0).Should().Be(TimeSpan.FromSeconds(2));
        p.DefaultDelay(1).Should().Be(TimeSpan.FromSeconds(4));
        p.DefaultDelay(2).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void NegativeMaxRetries_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(-1, 1));

    [Fact]
    public void NegativeBaseSeconds_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(1, -1));
}
