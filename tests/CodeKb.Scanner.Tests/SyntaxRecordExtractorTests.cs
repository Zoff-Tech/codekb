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

    [Fact]
    public void Extracts_Constructors()
    {
        var src = @"
namespace Acme {
    public class Foo {
        public Foo(int x) {}
        static Foo() {}
    }
}";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == ".ctor" && s.ClassName == "Foo");
        extract.Symbols.Count(s => s.MethodName == ".ctor").Should().Be(2);
    }

    [Fact]
    public void Extracts_Indexers()
    {
        var src = "class Foo { public int this[int i] => 0; }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "this[]");
    }

    [Fact]
    public void Extracts_Operators_And_Conversions()
    {
        var src = @"
public struct Money {
    public static Money operator +(Money a, Money b) => default;
    public static implicit operator decimal(Money m) => 0m;
}";
        var extract = _e.Extract(src, "Money.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "operator +");
        extract.Symbols.Should().Contain(s => s.MethodName != null && s.MethodName.Contains("operator decimal"));
    }

    [Fact]
    public void Extracts_Fields()
    {
        var src = "class Foo { private readonly int _x; public const string Name = \"foo\"; }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "_x" && s.SymbolKind == SymbolKind.Field);
        extract.Symbols.Should().Contain(s => s.MethodName == "Name" && s.SymbolKind == SymbolKind.Field);
    }

    [Fact]
    public void Extracts_Fields_WithMultipleDeclarators()
    {
        var src = "class Foo { private int a, b, c; }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "a");
        extract.Symbols.Should().Contain(s => s.MethodName == "b");
        extract.Symbols.Should().Contain(s => s.MethodName == "c");
    }

    [Fact]
    public void Extracts_Events()
    {
        var src = "using System; class Foo { public event Action Changed; public event Action Other { add {} remove {} } }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "Changed");
        extract.Symbols.Should().Contain(s => s.MethodName == "Other");
    }

    [Fact]
    public void Extracts_EnumMembers()
    {
        var src = "public enum Color { Red, Green = 2, Blue }";
        var extract = _e.Extract(src, "Color.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "Red");
        extract.Symbols.Should().Contain(s => s.MethodName == "Green");
        extract.Symbols.Should().Contain(s => s.MethodName == "Blue");
    }

    [Fact]
    public void Extracts_Delegates()
    {
        var src = "namespace Acme { public delegate int Reducer(int a, int b); }";
        var extract = _e.Extract(src, "Reducer.cs");
        extract.Symbols.Should().Contain(s => s.ClassName == "Reducer" && s.RecordType == RecordType.ClassSummary);
        extract.TopLevelTypes.Should().Contain("Reducer");
    }

    [Fact]
    public void Extracts_Destructors()
    {
        var src = "class Foo { ~Foo() {} }";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "~Foo");
    }

    [Fact]
    public void Extracts_LocalFunctions()
    {
        var src = @"
class Foo {
    public int M() {
        int Helper(int x) => x + 1;
        return Helper(1);
    }
}";
        var extract = _e.Extract(src, "Foo.cs");
        extract.Symbols.Should().Contain(s => s.MethodName == "Helper");
        var helper = extract.Symbols.First(s => s.MethodName == "Helper");
        helper.MetadataJson.Should().Contain("local_function");
        helper.MetadataJson.Should().Contain("\"enclosing_method\":\"M\"");
    }

    [Fact]
    public void CapturesCallGraph_PerMethod()
    {
        var src = @"
class Service {
    public void Process() {
        Validate();
        new Logger().Log(""x"");
        Helper.Run();
    }
    void Validate() {}
}
class Logger { public void Log(string s) {} }
class Helper { public static void Run() {} }";
        var extract = _e.Extract(src, "Service.cs");
        var process = extract.Symbols.First(s => s.MethodName == "Process");
        process.MetadataJson.Should().Contain("\"calls\"");
        process.MetadataJson.Should().Contain("Validate");
        process.MetadataJson.Should().Contain("Log");
        process.MetadataJson.Should().Contain("Run");
        process.MetadataJson.Should().Contain("\"instantiates\"");
        process.MetadataJson.Should().Contain("Logger");
    }

    [Fact]
    public void CapturesUsingDirectives_PerFile()
    {
        var src = @"
using System;
using System.Collections.Generic;
using Acme.Workflow;

namespace Demo { public class X {} }";
        var extract = _e.Extract(src, "X.cs");
        extract.UsingDirectives.Should().Contain("System");
        extract.UsingDirectives.Should().Contain("System.Collections.Generic");
        extract.UsingDirectives.Should().Contain("Acme.Workflow");
    }

    [Fact]
    public void CapturesExternalTypeReferences()
    {
        var src = @"
using System.Threading.Tasks;
class Foo {
    public Task<int> M(System.IO.Stream s) {
        var list = new System.Collections.Generic.List<int>();
        return Task.FromResult(0);
    }
}";
        var extract = _e.Extract(src, "Foo.cs");
        extract.ExternalTypes.Should().Contain(t => t.Contains("Task"));
        extract.ExternalTypes.Should().Contain(t => t.Contains("Stream"));
        extract.ExternalTypes.Should().Contain(t => t.Contains("List"));
    }

    [Fact]
    public void Extracts_NestedTypeMembers()
    {
        var src = @"
public class Outer {
    public class Inner {
        public int Compute() => 0;
    }
}";
        var extract = _e.Extract(src, "Outer.cs");
        extract.Symbols.Should().Contain(s => s.ClassName == "Inner" && s.RecordType == RecordType.ClassSummary);
        extract.Symbols.Should().Contain(s => s.ClassName == "Inner" && s.MethodName == "Compute");
    }
}
