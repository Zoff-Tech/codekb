using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn;
using CodeKb.Scanner.Roslyn.Classification;
using CodeKb.Scanner.Roslyn.Detection;
using CodeKb.Scanner.Roslyn.Redaction;
using CodeKb.Scanner.Roslyn.Syntax;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class MoreRoslynScannerTests : IDisposable
{
    private readonly string _root;

    public MoreRoslynScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codekb-more-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private RoslynScanner BuildScanner() => new(
        new FileClassifier(),
        new SyntaxRecordExtractor(),
        new FeatureFlagDetector(),
        new SearchTermMatcher(),
        new ConfigFileScanner(),
        new Redactor());

    [Fact]
    public async Task SkipsNonCsFiles()
    {
        File.WriteAllText(Path.Combine(_root, "README.md"), "# Hello");
        File.WriteAllText(Path.Combine(_root, "X.cs"), "class X {}");
        var scanner = BuildScanner();
        var ctx = new ScanContext(Guid.NewGuid(), Guid.NewGuid(), "r", "main", "sha", _root);
        var repo = new LoadedRepository(_root, "r", "main", "sha", null);
        var opts = new ScanOptions();
        var records = new List<CodeRecord>();
        await foreach (var rec in scanner.ScanAsync(repo, ctx, opts, CancellationToken.None))
            records.Add(rec);
        records.Should().NotContain(r => r.FilePath.EndsWith(".md"));
    }

    [Fact]
    public async Task RedactsSecretInSnippet()
    {
        File.WriteAllText(Path.Combine(_root, "Cfg.cs"), @"
class Cfg {
    public string ConnectionString = ""Host=db;Password=hunter2"";
}");
        var scanner = BuildScanner();
        var ctx = new ScanContext(Guid.NewGuid(), Guid.NewGuid(), "r", "main", "sha", _root);
        var repo = new LoadedRepository(_root, "r", "main", "sha", null);
        var records = new List<CodeRecord>();
        await foreach (var rec in scanner.ScanAsync(repo, ctx, new ScanOptions(), CancellationToken.None))
            records.Add(rec);
        records.Should().Contain(r => r.CodeSnippet.Contains("«REDACTED»"));
    }

    [Fact]
    public async Task RecordsRedactionFailedCounter_OnUnsafeSnippet()
    {
        // Embed a known unsafe interpolated secret in source that survives Roslyn parsing
        File.WriteAllText(Path.Combine(_root, "Bad.cs"), @"
class Bad { void M() { var msg = $""password is {password}""; } }");
        var scanner = BuildScanner();
        var ctx = new ScanContext(Guid.NewGuid(), Guid.NewGuid(), "r", "main", "sha", _root);
        var repo = new LoadedRepository(_root, "r", "main", "sha", null);
        await foreach (var _ in scanner.ScanAsync(repo, ctx, new ScanOptions(), CancellationToken.None)) { }
        scanner.Counters.RecordsRedactionFailed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EnvFile_ProducesRedactedConfigReference()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "API_KEY=ghp_" + new string('a', 40));
        var scanner = BuildScanner();
        var ctx = new ScanContext(Guid.NewGuid(), Guid.NewGuid(), "r", "main", "sha", _root);
        var repo = new LoadedRepository(_root, "r", "main", "sha", null);
        var records = new List<CodeRecord>();
        await foreach (var rec in scanner.ScanAsync(repo, ctx, new ScanOptions(), CancellationToken.None))
            records.Add(rec);
        records.Should().Contain(r => r.RecordType == RecordType.ConfigurationReference);
        records.Where(r => r.RecordType == RecordType.ConfigurationReference)
            .Should().AllSatisfy(r => r.CodeSnippet.Should().Contain("«REDACTED»"));
    }
}
