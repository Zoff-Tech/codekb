using CodeKb.Contracts;
using CodeKb.Core.Configuration;
using CodeKb.Core.Services;
using CodeKb.Storage.Postgres;
using FluentAssertions;
using Xunit;

namespace CodeKb.Core.Tests;

public class SearchServiceTests
{
    private static SearchHit Hit(double score, RecordType rt = RecordType.FileSummary)
        => new("r", "main", "sha", "f.cs", 1, 1, "s", rt, "summary", "snippet", score);

    private (SearchService svc, FakeCodeRecordStore store, FakeEmbeddingClient emb) Build()
    {
        var options = new CodeKbOptions();
        var emb = new FakeEmbeddingClient();
        var store = new FakeCodeRecordStore { SearchResponder = _ => new[] { Hit(0.9), Hit(0.5), Hit(0.3) } };
        var svc = new SearchService(options, emb, store);
        return (svc, store, emb);
    }

    [Fact]
    public async Task AskAsync_EmbedsQuestion_AndReturnsHits()
    {
        var (svc, _, _) = Build();
        var hits = await svc.AskAsync(new SearchRequest { Question = "where?", TopK = 5 }, CancellationToken.None);
        hits.Should().HaveCount(3);
    }

    [Fact]
    public async Task AskAsync_AppliesMinScore()
    {
        var (svc, _, _) = Build();
        var hits = await svc.AskAsync(new SearchRequest { Question = "q", MinScore = 0.6 }, CancellationToken.None);
        hits.Should().HaveCount(1);
        hits[0].Score.Should().Be(0.9);
    }

    [Fact]
    public async Task AskAsync_EmptyQuestion_Throws()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.AskAsync(new SearchRequest { Question = "" }, CancellationToken.None));
    }

    [Fact]
    public async Task AskAsync_FlagNameQuery_FlowsThroughAsSemanticSearch()
    {
        // After removing the feature-flag feature, querying for a flag is
        // just a plain semantic search — the embedded snippet / tokens take
        // care of matching. The SearchService no longer forces a record-type
        // filter, so the caller sees results across all record kinds.
        var (svc, store, _) = Build();
        SearchQuery? captured = null;
        store.SearchResponder = q => { captured = q; return Array.Empty<SearchHit>(); };
        await svc.AskAsync(new SearchRequest { Question = "EnableNewWorkflow" }, CancellationToken.None);
        captured!.RecordTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task AskAsync_DefaultsTopKWhenZero()
    {
        var (svc, store, _) = Build();
        SearchQuery? captured = null;
        store.SearchResponder = q => { captured = q; return Array.Empty<SearchHit>(); };
        await svc.AskAsync(new SearchRequest { Question = "q", TopK = 0 }, CancellationToken.None);
        captured!.TopK.Should().Be(10);
    }

    [Fact]
    public async Task AskAsync_EmbeddingReturnsEmpty_ReturnsEmptyHits()
    {
        var (svc, _, emb) = Build();
        emb.Responder = _ => Array.Empty<float[]>();
        var hits = await svc.AskAsync(new SearchRequest { Question = "q" }, CancellationToken.None);
        hits.Should().BeEmpty();
    }
}
