using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn.Syntax;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class SyntaxRecordExtractorTests
{
    private readonly SyntaxRecordExtractor _e = new();

    [Fact]
    public void Extracts_ClassAndMethod()
    {
        var src = @"
namespace Acme {
    public class Foo {
        public int Bar() { return 42; }
    }
}";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Namespaces.Should().Contain("Acme");
        extract.TopLevelTypes.Should().Contain("Foo");
        extract.Symbols.Should().Contain(s => s.RecordType == RecordType.ClassSummary && s.SymbolName == "Acme.Foo");
        extract.Symbols.Should().Contain(s => s.RecordType == RecordType.MethodSummary && s.MethodName == "Bar");
    }

    [Fact]
    public void Handles_FileScopedNamespace()
    {
        var src = @"namespace Acme;
public class Foo { public void M() {} }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Namespaces.Should().Contain("Acme");
    }

    [Fact]
    public void Handles_NoNamespace()
    {
        var src = "public class Foo { public void M() {} }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.RecordType == RecordType.ClassSummary);
    }

    [Fact]
    public void Extracts_Properties_AsMethodSummary()
    {
        var src = "class Foo { public int Bar { get; set; } }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "Bar" && s.SymbolKind == SymbolKind.Property);
    }

    [Fact]
    public void Extracts_Records_And_Interfaces_And_Enums()
    {
        var src = @"
public record R(int X);
public interface I {}
public enum E { A, B }";
        var extract = _e.Extract(src, "Mixed.cs");
        extract.TopLevelTypes.Should().BeEquivalentTo(new[] { "R", "I", "E" });
        extract.Symbols.Should().Contain(s => s.SymbolKind == SymbolKind.Record);
        extract.Symbols.Should().Contain(s => s.SymbolKind == SymbolKind.Interface);
        extract.Symbols.Should().Contain(s => s.SymbolKind == SymbolKind.Enum);
    }

    [Fact]
    public void Cyclomatic_CountsBranches()
    {
        var src = @"
class C {
    int M(int x) {
        if (x > 0) return 1;
        if (x < 0) return -1;
        return 0;
    }
}";
        var extract = _e.Extract(src, "C.cs");
        var m = extract.Symbols.First(s => s.MethodName == "M");
        m.MetadataJson.Should().Contain("cyclomatic_complexity");
        m.MetadataJson.Should().MatchRegex("\"cyclomatic_complexity\":\\s*3");
    }

    [Fact]
    public void Method_WithoutBody_DefaultsToComplexityOne()
    {
        var src = "interface I { int M(); }";
        var extract = _e.Extract(src, "I.cs");
        var m = extract.Symbols.First(s => s.MethodName == "M");
        m.MetadataJson.Should().MatchRegex("\"cyclomatic_complexity\":\\s*1");
    }

    [Fact]
    public void IsTopLevel_DetectsCompilationUnit()
    {
        var src = "public class Top {}";
        var extract = _e.Extract(src, "x.cs");
        extract.TopLevelTypes.Should().Contain("Top");
    }
}
