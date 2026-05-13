using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn.Detection;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class SearchTermMatcherTests
{
    private readonly SearchTermMatcher _m = new();

    [Fact]
    public void Identifier_CaseSensitive_MatchesExact()
    {
        var hits = _m.MatchInIdentifier("EnableNewWorkflow", 1, 1, new[] { "EnableNewWorkflow" });
        hits.Should().ContainSingle().Which.Kind.Should().Be(SearchMatchKind.Identifier);
    }

    [Fact]
    public void Identifier_DiffCase_NoMatch()
    {
        var hits = _m.MatchInIdentifier("enablenewworkflow", 1, 1, new[] { "EnableNewWorkflow" });
        hits.Should().BeEmpty();
    }

    [Fact]
    public void Identifier_Substring_NoMatch()
    {
        var hits = _m.MatchInIdentifier("EnableNewWorkflowExtended", 1, 1, new[] { "EnableNewWorkflow" });
        hits.Should().BeEmpty();
    }

    [Fact]
    public void Literal_CaseInsensitive_WordBoundary()
    {
        var hits = _m.MatchInLiteral("This uses the enablenewWorkflow flag", 1, 1, new[] { "EnableNewWorkflow" });
        hits.Should().ContainSingle().Which.Kind.Should().Be(SearchMatchKind.StringLiteral);
    }

    [Fact]
    public void Literal_SubstringInsideWord_NoMatch()
    {
        var hits = _m.MatchInLiteral("EnableNewWorkflowExtended", 1, 1, new[] { "EnableNewWorkflow" });
        hits.Should().BeEmpty();
    }

    [Fact]
    public void Comment_DistinguishesXmlDoc()
    {
        var xml = _m.MatchInComment("/// uses EnableNewWorkflow", 1, 1, new[] { "EnableNewWorkflow" }, isXmlDoc: true);
        xml.Should().ContainSingle().Which.Kind.Should().Be(SearchMatchKind.XmlDoc);
        var plain = _m.MatchInComment("// uses EnableNewWorkflow", 1, 1, new[] { "EnableNewWorkflow" }, isXmlDoc: false);
        plain.Should().ContainSingle().Which.Kind.Should().Be(SearchMatchKind.Comment);
    }

    [Fact]
    public void EmptyInputs_Return_Empty()
    {
        _m.MatchInIdentifier("", 1, 1, new[] { "x" }).Should().BeEmpty();
        _m.MatchInLiteral("anything", 1, 1, Array.Empty<string>()).Should().BeEmpty();
        _m.MatchInComment(null!, 1, 1, new[] { "x" }, false).Should().BeEmpty();
    }
}
