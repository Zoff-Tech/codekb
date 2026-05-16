using CodeKb.Cli.Commands;
using CodeKb.Contracts;
using FluentAssertions;
using Xunit;

namespace CodeKb.Cli.Tests;

public class ScanCommandFormatTests
{
    private static ScanResult Sample(bool alreadyIndexed = false) => new()
    {
        ScanJobId = Guid.NewGuid(),
        RepositoryName = "platform-service",
        Branch = "main",
        CommitSha = "abc1234567890",
        AlreadyIndexed = alreadyIndexed,
        Duration = TimeSpan.FromSeconds(107),
        Outcome = new ScanJobOutcome(ScanStatus.Completed, 342, 184, 0, 0, 184, null),
    };

    [Fact]
    public void PrintSummary_RendersExpectedFields()
    {
        var sw = new StringWriter();
        ScanCommand.PrintSummary(Sample(), sw);
        var s = sw.ToString();
        s.Should().Contain("Scan completed.");
        s.Should().Contain("Repository:           platform-service");
        s.Should().Contain("Branch:               main");
        s.Should().Contain("Commit:               abc1234");
        s.Should().Contain("Files scanned:        342");
        s.Should().Contain("Records created:      184");
        s.Should().Contain("Embeddings created:   184");
        s.Should().Contain("Duration:             1m 47s");
    }

    [Fact]
    public void PrintSummary_AlreadyIndexed()
    {
        var sw = new StringWriter();
        ScanCommand.PrintSummary(Sample(alreadyIndexed: true), sw);
        sw.ToString().Should().Contain("No-op");
        sw.ToString().Should().Contain("already indexed");
    }

    [Fact]
    public void Trim_HandlesShortSha()
        => ScanCommand.Trim("abc").Should().Be("abc");

    [Theory]
    [InlineData(0.5, "0.5s")]
    [InlineData(45, "45.0s")]
    [InlineData(75, "1m 15s")]
    public void FormatDuration(double seconds, string expected)
        => ScanCommand.FormatDuration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
}
