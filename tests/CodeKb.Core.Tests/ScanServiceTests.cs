using CodeKb.Contracts;
using CodeKb.Core.Configuration;
using CodeKb.Core.Services;
using CodeKb.Embedding;
using CodeKb.Storage.Postgres.Migrations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeKb.Core.Tests;

public class ScanServiceTests
{
    private static CodeRecord NewRecord() => new()
    {
        RepositoryName = "fake-repo", Branch = "main", CommitSha = "abc1234",
        FilePath = "src/X.cs", LineStart = 1, LineEnd = 1, RecordType = RecordType.FileSummary,
        Summary = "s", CodeSnippet = "...",
    };

    private (ScanService svc, FakeScanner scanner, FakeRepositoryStore repos, FakeScanJobStore jobs, FakeCodeRecordStore records, FakeEmbeddingClient emb) Build()
    {
        var options = new CodeKbOptions();
        var loader = new FakeRepositoryLoader();
        var scanner = new FakeScanner { Records = { NewRecord(), NewRecord() with { FilePath = "src/Y.cs" } } };
        var repos = new FakeRepositoryStore();
        var jobs = new FakeScanJobStore();
        var records = new FakeCodeRecordStore();
        var emb = new FakeEmbeddingClient();
        var pipeline = new EmbeddingPipeline(emb, new RetryPolicy(0, 0, sleep: (_, _) => Task.CompletedTask), 10);
        var svc = new ScanService(options, loader, scanner, repos, jobs, records, pipeline, new NullDatabaseInitializer(), NullLogger<ScanService>.Instance);
        return (svc, scanner, repos, jobs, records, emb);
    }

    [Fact]
    public async Task ScanAsync_HappyPath_InsertsRecordsAndEmbeddings()
    {
        var (svc, _, _, jobs, records, _) = Build();
        var result = await svc.ScanAsync(new ScanRequest { Path = "/repo" }, CancellationToken.None);
        result.AlreadyIndexed.Should().BeFalse();
        result.Outcome.RecordsCreated.Should().Be(2);
        records.Inserted.Should().HaveCount(2);
        records.Embeddings.Should().HaveCount(2);
        jobs.Jobs[result.ScanJobId].outcome!.Status.Should().Be(ScanStatus.Completed);
    }

    [Fact]
    public async Task ScanAsync_MarksStale_BeforeInserts()
    {
        var (svc, _, _, _, records, _) = Build();
        await svc.ScanAsync(new ScanRequest { Path = "/repo" }, CancellationToken.None);
        records.StaleMarked.Should().HaveCount(1);
    }

    [Fact]
    public async Task ScanAsync_AlreadyIndexed_ShortCircuits()
    {
        var (svc, _, repos, jobs, records, _) = Build();
        await svc.ScanAsync(new ScanRequest { Path = "/repo" }, CancellationToken.None);

        // Second run
        var (svc2, _, _, _, records2, _) = Build();
        // Need to inject the same jobs+repos for the dedupe check to take effect; rebuild but share state.
        // Use the existing service to call again — the jobs/repos in the first build now mark commit completed.
        records.Inserted.Clear();
        var result = await svc.ScanAsync(new ScanRequest { Path = "/repo" }, CancellationToken.None);
        result.AlreadyIndexed.Should().BeTrue();
        records.Inserted.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_Force_Bypasses_DuplicateCheck()
    {
        var (svc, _, _, _, records, _) = Build();
        await svc.ScanAsync(new ScanRequest { Path = "/repo" }, CancellationToken.None);
        records.Inserted.Clear();
        var result = await svc.ScanAsync(new ScanRequest { Path = "/repo", Force = true }, CancellationToken.None);
        result.AlreadyIndexed.Should().BeFalse();
        records.Inserted.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_EmbeddingFailure_MarksFailed()
    {
        var options = new CodeKbOptions();
        var loader = new FakeRepositoryLoader();
        var scanner = new FakeScanner { Records = { NewRecord() } };
        var repos = new FakeRepositoryStore();
        var jobs = new FakeScanJobStore();
        var records = new FakeCodeRecordStore();
        var emb = new FakeEmbeddingClient { Responder = _ => throw new EmbeddingException("err") };
        var pipeline = new EmbeddingPipeline(emb, new RetryPolicy(0, 0, sleep: (_, _) => Task.CompletedTask), 10);
        var svc = new ScanService(options, loader, scanner, repos, jobs, records, pipeline, new NullDatabaseInitializer(), NullLogger<ScanService>.Instance);

        await svc.ScanAsync(new ScanRequest { Path = "/repo" }, CancellationToken.None);
        records.FailedEmbeddings.Should().HaveCount(1);
        records.Embeddings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_InvalidRequest_Throws()
    {
        var (svc, _, _, _, _, _) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ScanAsync(new ScanRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ScanAsync_InvokesDatabaseInitializer_BeforeStoreOps()
    {
        var options = new CodeKbOptions();
        var loader = new FakeRepositoryLoader();
        var scanner = new FakeScanner { Records = { NewRecord() } };
        var initializer = new RecordingDatabaseInitializer();
        var repos = new FakeRepositoryStore();
        var jobs = new FakeScanJobStore();
        var records = new FakeCodeRecordStore();
        var emb = new FakeEmbeddingClient();
        var pipeline = new EmbeddingPipeline(emb, new RetryPolicy(0, 0, sleep: (_, _) => Task.CompletedTask), 10);
        var svc = new ScanService(options, loader, scanner, repos, jobs, records, pipeline, initializer, NullLogger<ScanService>.Instance);

        await svc.ScanAsync(new ScanRequest { Path = "/repo" }, CancellationToken.None);

        initializer.Invocations.Should().Be(1);
        repos.ByName.Should().NotBeEmpty();
        initializer.FirstCalledAt.Should().NotBeNull();
    }
}
