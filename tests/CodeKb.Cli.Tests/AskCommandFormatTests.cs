using CodeKb.Cli.Commands;
using CodeKb.Contracts;
using FluentAssertions;
using Xunit;

namespace CodeKb.Cli.Tests;

public class AskCommandFormatTests
{
    private static SearchHit Hit(double score) => new(
        "platform-service", "main", "abc1234",
        "src/Workflow.cs", 42, 78,
        "WorkflowService.Process", RecordType.MethodSummary,
        "summary", "snippet", score);

    [Fact]
    public void PrintHits_TextFormat()
    {
        var sw = new StringWriter();
        var req = new SearchRequest { Question = "where?", Format = "text" };
        AskCommand.PrintHits(req, new[] { Hit(0.91), Hit(0.84) }, sw);
        var s = sw.ToString();
        s.Should().Contain("Question: where?");
        s.Should().Contain("1. platform-service");
        s.Should().Contain("File:   src/Workflow.cs:42-78");
        s.Should().Contain("Score:  0.91");
        s.Should().Contain("2. platform-service");
    }

    [Fact]
    public void PrintHits_JsonFormat()
    {
        var sw = new StringWriter();
        var req = new SearchRequest { Question = "q", Format = "json" };
        AskCommand.PrintHits(req, new[] { Hit(0.91) }, sw);
        var s = sw.ToString();
        s.Should().Contain("\"repository\":");
        s.Should().Contain("\"score\":");
        s.Should().Contain("\"record_type\": \"method_summary\"");
    }

    [Fact]
    public void PrintHits_EmptyResults_PrintsHeaderOnly()
    {
        var sw = new StringWriter();
        AskCommand.PrintHits(new SearchRequest { Question = "q" }, Array.Empty<SearchHit>(), sw);
        sw.ToString().Should().Contain("Question: q");
    }
}
