using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn;
using CodeKb.Scanner.Roslyn.Classification;
using CodeKb.Scanner.Roslyn.Detection;
using CodeKb.Scanner.Roslyn.Redaction;
using CodeKb.Scanner.Roslyn.Syntax;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class RoslynScannerTests : IDisposable
{
    private readonly string _root;

    public RoslynScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codekb-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private RoslynScanner BuildScanner() => new(
        new FileClassifier(),
        new SyntaxRecordExtractor(),
        new FeatureFlagDetector(),
        new SearchTermMatcher(),
        new ConfigFileScanner(),
        new Redactor());

    private (LoadedRepository repo, ScanContext context, ScanOptions opts) BuildContext(string[]? searchTerms = null)
    {
        var repo = new LoadedRepository(_root, "test-repo", "main", "abc1234", null);
        var ctx = new ScanContext(Guid.NewGuid(), Guid.NewGuid(), "test-repo", "main", "abc1234", _root);
        var opts = new ScanOptions { SearchTerms = searchTerms ?? Array.Empty<string>() };
        return (repo, ctx, opts);
    }

    [Fact]
    public async Task EmitsFileClassMethodSummaries_ForSimpleCs()
    {
        File.WriteAllText(Path.Combine(_root, "Foo.cs"), @"
namespace Acme;
public class Foo { public int M() => 1; }");

        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext();
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(r);

        records.Should().Contain(r => r.RecordType == RecordType.FileSummary);
        records.Should().Contain(r => r.RecordType == RecordType.ClassSummary);
        records.Should().Contain(r => r.RecordType == RecordType.MethodSummary);
        scanner.Counters.FilesScanned.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Skips_GeneratedFiles()
    {
        File.WriteAllText(Path.Combine(_root, "A.g.cs"), "class A {}");
        File.WriteAllText(Path.Combine(_root, "B.cs"), "class B {}");
        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext();
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(r);

        records.Should().NotContain(r => r.FilePath.EndsWith(".g.cs"));
        records.Should().Contain(r => r.FilePath.EndsWith("B.cs"));
    }

    [Fact]
    public async Task EmitsFeatureFlagRecord_ForLiteralInIsEnabledCall()
    {
        File.WriteAllText(Path.Combine(_root, "Use.cs"), @"
class Use {
    IFeatureManager _featureManager;
    public void M() { _featureManager.IsEnabled(""EnableNewWorkflow""); }
}");
        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext();
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(r);

        records.Should().Contain(r => r.RecordType == RecordType.FeatureFlagUsage
            && r.FeatureFlagName == "EnableNewWorkflow");
        scanner.Counters.FeatureFlagMatches.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EmitsConfigurationReference_ForAppsettings()
    {
        File.WriteAllText(Path.Combine(_root, "appsettings.json"),
            @"{ ""FeatureFlags"": { ""EnableNewWorkflow"": true } }");
        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext();
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(r);

        records.Should().Contain(r => r.RecordType == RecordType.ConfigurationReference
            && r.FeatureFlagName == "EnableNewWorkflow");
    }

    [Fact]
    public async Task EmitsTestReference_ForTestFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tests"));
        File.WriteAllText(Path.Combine(_root, "tests", "FooTests.cs"), @"
using Xunit;
public class FooTests { [Fact] public void Test1() {} }");
        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext();
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(r);

        records.Should().Contain(r => r.RecordType == RecordType.TestReference);
        records.Where(r => r.IsTestCode).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Skips_FilesLargerThanLimit()
    {
        var big = new string('a', 600 * 1024);
        File.WriteAllText(Path.Combine(_root, "Big.cs"), big);
        File.WriteAllText(Path.Combine(_root, "Small.cs"), "class S {}");
        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext();
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts with { MaxFileSizeKb = 512 }, CancellationToken.None))
            records.Add(r);

        records.Should().NotContain(r => r.FilePath.EndsWith("Big.cs"));
    }

    [Fact]
    public async Task Skips_IgnoredPaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        File.WriteAllText(Path.Combine(_root, "bin", "Garbage.cs"), "class G {}");
        File.WriteAllText(Path.Combine(_root, "Keep.cs"), "class K {}");

        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext();
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(r);

        records.Should().NotContain(r => r.FilePath.Contains("bin/"));
        records.Should().Contain(r => r.FilePath.EndsWith("Keep.cs"));
    }

    [Fact]
    public async Task EmitsSearchTermMatches()
    {
        File.WriteAllText(Path.Combine(_root, "S.cs"), @"
// Comment about MyFlag
class S { string s = ""use MyFlag""; void MyFlag() {} }");
        var scanner = BuildScanner();
        var (repo, ctx, opts) = BuildContext(new[] { "MyFlag" });
        var records = new List<CodeRecord>();
        await foreach (var r in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(r);

        records.Should().Contain(r => r.RecordType == RecordType.SearchTermMatch && r.SymbolName == "MyFlag");
    }

    [Fact]
    public void LineContainsIdentifierToken_RespectsBoundary()
    {
        RoslynScanner.LineContainsIdentifierToken("Foo bar", "Foo").Should().BeTrue();
        RoslynScanner.LineContainsIdentifierToken("FooBar", "Foo").Should().BeFalse();
        RoslynScanner.LineContainsIdentifierToken("_Foo", "Foo").Should().BeFalse();
    }

    [Fact]
    public void DetectFramework_DetectsKnownFrameworks()
    {
        RoslynScanner.DetectFramework("[Fact] public void T() {}").Should().Be("xunit");
        RoslynScanner.DetectFramework("[Test] public void T() {}").Should().Be("nunit");
        RoslynScanner.DetectFramework("[TestMethod] public void T() {}").Should().Be("mstest");
        RoslynScanner.DetectFramework("no attrs").Should().Be("unknown");
    }

    [Fact]
    public void EnumerateFiles_MissingRoot_Empty()
    {
        RoslynScanner.EnumerateFiles("/no/such/path").Should().BeEmpty();
    }
}
