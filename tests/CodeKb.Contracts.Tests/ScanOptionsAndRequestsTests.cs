using CodeKb.Contracts;
using FluentAssertions;
using Xunit;

namespace CodeKb.Contracts.Tests;

public class ScanOptionsAndRequestsTests
{
    [Fact]
    public void ScanOptions_Defaults()
    {
        var opts = new ScanOptions();
        opts.IgnorePaths.Should().Contain("bin");
        opts.FeatureFlagMethodNames.Should().Contain("IsEnabled");
        opts.FeatureFlagClientNames.Should().Contain("IFeatureManager");
        opts.Parallelism.Should().Be(4);
        opts.MaxFileSizeKb.Should().Be(512);
        opts.SearchTerms.Should().BeEmpty();
    }

    [Fact]
    public void LoadedRepository_Constructed()
    {
        var l = new LoadedRepository("/tmp", "n", "main", "sha", "url");
        l.Name.Should().Be("n");
        l.OriginalUrl.Should().Be("url");
    }

    [Fact]
    public void RepoSource_Constructed()
    {
        var rs = new RepoSource("u", "p", "b");
        rs.Url.Should().Be("u");
        rs.Path.Should().Be("p");
        rs.Branch.Should().Be("b");
    }

    [Fact]
    public void SearchHit_Constructed()
    {
        var h = new SearchHit("r", "b", "s", "f.cs", 1, 2, "sym", RecordType.FileSummary, "summary", "snippet", 0.5);
        h.Score.Should().Be(0.5);
        h.RecordType.Should().Be(RecordType.FileSummary);
    }

    [Fact]
    public void RepositoryRow_Constructed()
    {
        var r = new RepositoryRow(Guid.NewGuid(), "n", null, null, "main", "sha");
        r.Name.Should().Be("n");
        r.Url.Should().BeNull();
    }

    [Fact]
    public void SearchRequest_Defaults()
    {
        var r = new SearchRequest { Question = "q" };
        r.TopK.Should().Be(10);
        r.Format.Should().Be("text");
        r.Repositories.Should().BeEmpty();
        r.RecordTypes.Should().BeEmpty();
        r.IncludeStale.Should().BeFalse();
    }

    [Fact]
    public void ScanResult_Constructed()
    {
        var r = new ScanResult
        {
            ScanJobId = Guid.NewGuid(),
            RepositoryName = "r",
            Branch = "main",
            CommitSha = "s",
            Outcome = new ScanJobOutcome(ScanStatus.Completed, 1, 1, 0, 0, 1, 0, null),
            Duration = TimeSpan.FromSeconds(1),
        };
        r.AlreadyIndexed.Should().BeFalse();
    }
}
