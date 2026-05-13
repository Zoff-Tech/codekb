using CodeKb.Scanner.Roslyn.Snippets;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class SnippetBuilderTests
{
    [Fact]
    public void BuildMethod_RespectsLineRange()
    {
        var source = string.Join("\n", Enumerable.Range(1, 5).Select(i => $"line {i};"));
        var snippet = SnippetBuilder.BuildMethod(source, 2, 4);
        snippet.Should().Contain("line 2;");
        snippet.Should().Contain("line 3;");
        snippet.Should().Contain("line 4;");
        snippet.Should().NotContain("line 5;");
    }

    [Fact]
    public void BuildMethod_CapsAt200Lines()
    {
        var lines = Enumerable.Range(1, 500).Select(i => $"x = {i};").ToArray();
        var source = string.Join("\n", lines);
        var snippet = SnippetBuilder.BuildMethod(source, 1, 500);
        snippet.Split('\n').Length.Should().BeLessOrEqualTo(SnippetBuilder.MaxMethodLines);
    }

    [Fact]
    public void BuildMethod_TruncatesToBytes()
    {
        var line = new string('a', 200) + ";";
        var source = string.Join("\n", Enumerable.Repeat(line, 30));
        var snippet = SnippetBuilder.BuildMethod(source, 1, 30);
        System.Text.Encoding.UTF8.GetByteCount(snippet).Should().BeLessOrEqualTo(SnippetBuilder.MaxBytes);
    }

    [Fact]
    public void BuildMethod_EndsAtStatementBoundary()
    {
        var source = "int a = 1;\nint b = 2;\nint c = 3 +";
        var snippet = SnippetBuilder.BuildMethod(source, 1, 3);
        snippet.Should().EndWith(";");
    }

    [Fact]
    public void BuildMethod_BadRange_StillReturns()
    {
        var snippet = SnippetBuilder.BuildMethod("a;\nb;\nc;", -5, 999);
        snippet.Should().Contain("a;");
    }

    [Fact]
    public void BuildClassSignatures_BoundedByBytes()
    {
        var members = Enumerable.Repeat("public string Member { get; set; }", 1000);
        var s = SnippetBuilder.BuildClassSignatures("public class Foo", members);
        System.Text.Encoding.UTF8.GetByteCount(s).Should().BeLessOrEqualTo(SnippetBuilder.MaxBytes);
    }

    [Fact]
    public void BuildClassSignatures_IncludesMembers()
    {
        var s = SnippetBuilder.BuildClassSignatures("public class Foo", new[] { "void Bar();" });
        s.Should().Contain("public class Foo");
        s.Should().Contain("void Bar();");
        s.Should().Contain("{");
        s.Should().Contain("}");
    }

    [Fact]
    public void BuildAroundLine_ClampedToEnclosing()
    {
        var source = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line{i};"));
        var s = SnippetBuilder.BuildAroundLine(source, 50, contextLines: 100, enclosingStart: 45, enclosingEnd: 55);
        s.Should().Contain("line45;");
        s.Should().Contain("line55;");
        s.Should().NotContain("line44;");
        s.Should().NotContain("line56;");
    }

    [Fact]
    public void BuildAroundLine_EmptySource_ReturnsEmpty()
    {
        SnippetBuilder.BuildAroundLine("", 5).Should().BeEmpty();
    }

    [Fact]
    public void BuildAroundLine_LineBeyondSource_Clamps()
    {
        var s = SnippetBuilder.BuildAroundLine("a;\nb;\nc;", 999);
        s.Should().Contain("c;");
    }

    [Fact]
    public void BuildAroundLine_NegativeLine_ClampedToOne()
    {
        var s = SnippetBuilder.BuildAroundLine("a;\nb;\nc;", -3);
        s.Should().Contain("a;");
    }

    [Fact]
    public void BuildFileSummary_RendersFields()
    {
        var s = SnippetBuilder.BuildFileSummary("src/X.cs", new[] { "Ns" }, new[] { "Foo" }, new[] { "Term" });
        s.Should().Contain("File: src/X.cs");
        s.Should().Contain("Namespaces: Ns");
        s.Should().Contain("Top-level types: Foo");
        s.Should().Contain("Search-term hits: Term");
    }

    [Fact]
    public void BuildFileSummary_OmitsEmptyFields()
    {
        var s = SnippetBuilder.BuildFileSummary("src/X.cs", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        s.Should().Contain("File: src/X.cs");
        s.Should().NotContain("Namespaces:");
        s.Should().NotContain("Top-level types:");
        s.Should().NotContain("Search-term hits:");
    }

    [Fact]
    public void SplitLines_NormalizesEndings()
    {
        SnippetBuilder.SplitLines("a\r\nb\rc\nd").Should().Equal(new[] { "a", "b", "c", "d" });
    }

    [Fact]
    public void TruncateToBytes_RespectsUtf8Boundary()
    {
        var s = "abcñé"; // multibyte
        var truncated = SnippetBuilder.TruncateToBytes(s, 4);
        System.Text.Encoding.UTF8.GetByteCount(truncated).Should().BeLessOrEqualTo(4);
    }

    [Fact]
    public void TruncateAtStatementBoundary_EmptyString_Empty()
    {
        SnippetBuilder.TruncateAtStatementBoundary("").Should().BeEmpty();
    }

    [Fact]
    public void TruncateAtStatementBoundary_NoBoundary_ReturnsInput()
    {
        SnippetBuilder.TruncateAtStatementBoundary("abc def").Should().Be("abc def");
    }
}
