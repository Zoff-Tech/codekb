using CodeKb.Contracts;
using FluentAssertions;
using Xunit;

namespace CodeKb.Contracts.Tests;

public class ScanRequestTests
{
    [Fact]
    public void Validate_BothRepoAndPath_Throws()
    {
        var r = new ScanRequest { RepoUrl = "https://x", Path = "/y" };
        Assert.Throws<ArgumentException>(() => r.Validate());
    }

    [Fact]
    public void Validate_Neither_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ScanRequest().Validate());
    }

    [Fact]
    public void Validate_RepoOnly_Ok()
    {
        new ScanRequest { RepoUrl = "https://x" }.Validate();
    }

    [Fact]
    public void Validate_PathOnly_Ok()
    {
        new ScanRequest { Path = "/y" }.Validate();
    }
}
