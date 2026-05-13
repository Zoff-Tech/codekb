using System.Runtime.CompilerServices;
using System.Text.Json;
using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn.Classification;
using CodeKb.Scanner.Roslyn.Detection;
using CodeKb.Scanner.Roslyn.Redaction;
using CodeKb.Scanner.Roslyn.Snippets;
using CodeKb.Scanner.Roslyn.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using SymbolKind = CodeKb.Contracts.SymbolKind;

namespace CodeKb.Scanner.Roslyn;

public sealed record ScanContext(
    Guid RepositoryId,
    Guid ScanJobId,
    string RepositoryName,
    string Branch,
    string CommitSha,
    string Root);

public interface IRoslynScanner
{
    IAsyncEnumerable<CodeRecord> ScanAsync(LoadedRepository repo, ScanContext context, ScanOptions options, CancellationToken ct);
    ScanCounters Counters { get; }
}

public sealed class ScanCounters
{
    public int FilesScanned;
    public int RecordsCreated;
    public int RecordsFailed;
    public int RecordsRedactionFailed;
    public int FeatureFlagMatches;
}

public sealed class RoslynScanner : IRoslynScanner
{
    private readonly IFileClassifier _classifier;
    private readonly ISyntaxRecordExtractor _extractor;
    private readonly IFeatureFlagDetector _flagDetector;
    private readonly ISearchTermMatcher _searchTermMatcher;
    private readonly IConfigFileScanner _configScanner;
    private readonly IRedactor _redactor;

    public ScanCounters Counters { get; } = new();

    public RoslynScanner(
        IFileClassifier classifier,
        ISyntaxRecordExtractor extractor,
        IFeatureFlagDetector flagDetector,
        ISearchTermMatcher searchTermMatcher,
        IConfigFileScanner configScanner,
        IRedactor redactor)
    {
        _classifier = classifier;
        _extractor = extractor;
        _flagDetector = flagDetector;
        _searchTermMatcher = searchTermMatcher;
        _configScanner = configScanner;
        _redactor = redactor;
    }

    public async IAsyncEnumerable<CodeRecord> ScanAsync(LoadedRepository repo, ScanContext context, ScanOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        var filter = new IgnorePathFilter(options.IgnorePaths);
        var maxBytes = options.MaxFileSizeKb * 1024L;

        foreach (var absPath in EnumerateFiles(repo.LocalRoot))
        {
            ct.ThrowIfCancellationRequested();
            var relPath = Path.GetRelativePath(repo.LocalRoot, absPath).Replace('\\', '/');
            if (filter.IsIgnored(relPath)) continue;

            FileInfo info;
            try { info = new FileInfo(absPath); }
            catch { continue; }
            if (!info.Exists) continue;
            if (info.Length > maxBytes) continue;

            string content;
            try { content = await File.ReadAllTextAsync(absPath, ct); }
            catch { Interlocked.Increment(ref Counters.RecordsFailed); continue; }

            var kind = _classifier.Classify(relPath, content);
            Interlocked.Increment(ref Counters.FilesScanned);

            if (kind == FileKind.Generated) continue;
            if (kind == FileKind.Configuration)
            {
                foreach (var rec in HandleConfigFile(relPath, content, context, options))
                {
                    Interlocked.Increment(ref Counters.RecordsCreated);
                    yield return rec;
                }
                continue;
            }

            if (!relPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var rec in HandleCSharpFile(relPath, content, kind, context, options))
            {
                Interlocked.Increment(ref Counters.RecordsCreated);
                yield return rec;
            }
        }
    }

    internal IEnumerable<CodeRecord> HandleCSharpFile(string relPath, string content, FileKind kind, ScanContext context, ScanOptions options)
    {
        FileExtraction extraction;
        try { extraction = _extractor.Extract(content, relPath); }
        catch { Interlocked.Increment(ref Counters.RecordsFailed); yield break; }

        var isTest = kind == FileKind.Test;

        var fileSummarySnippet = SnippetBuilder.BuildFileSummary(relPath, extraction.Namespaces, extraction.TopLevelTypes, GatherSearchHitsForSummary(content, options));
        var redactedFileSummary = _redactor.Redact(fileSummarySnippet);
        if (redactedFileSummary.Status == RedactionStatus.Failed)
            Interlocked.Increment(ref Counters.RecordsRedactionFailed);
        else
        {
            yield return new CodeRecord
            {
                RepositoryId = context.RepositoryId,
                ScanJobId = context.ScanJobId,
                RepositoryName = context.RepositoryName,
                Branch = context.Branch,
                CommitSha = context.CommitSha,
                FilePath = relPath,
                LineStart = 1,
                LineEnd = Math.Max(1, extraction.LineCount),
                RecordType = RecordType.FileSummary,
                SymbolName = relPath,
                SymbolKind = SymbolKind.File,
                Summary = $"File {relPath} with {extraction.TopLevelTypes.Count} top-level type(s)",
                CodeSnippet = redactedFileSummary.Text,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    loc = extraction.LineCount,
                    top_level_types = extraction.TopLevelTypes,
                    uses_unsafe = content.Contains("unsafe", StringComparison.Ordinal),
                }),
                IsTestCode = isTest,
                IsGeneratedCode = false,
            };
        }

        foreach (var sym in extraction.Symbols)
        {
            var redacted = _redactor.Redact(sym.CodeSnippet);
            if (redacted.Status == RedactionStatus.Failed)
            {
                Interlocked.Increment(ref Counters.RecordsRedactionFailed);
                continue;
            }
            yield return new CodeRecord
            {
                RepositoryId = context.RepositoryId,
                ScanJobId = context.ScanJobId,
                RepositoryName = context.RepositoryName,
                Branch = context.Branch,
                CommitSha = context.CommitSha,
                FilePath = relPath,
                LineStart = sym.LineStart,
                LineEnd = sym.LineEnd,
                RecordType = sym.RecordType,
                SymbolName = sym.SymbolName,
                SymbolKind = sym.SymbolKind,
                Namespace = sym.Namespace,
                ClassName = sym.ClassName,
                MethodName = sym.MethodName,
                Summary = sym.Summary,
                CodeSnippet = redacted.Text,
                MetadataJson = sym.MetadataJson,
                IsTestCode = isTest,
            };

            if (isTest && sym.RecordType == RecordType.MethodSummary)
            {
                yield return new CodeRecord
                {
                    RepositoryId = context.RepositoryId,
                    ScanJobId = context.ScanJobId,
                    RepositoryName = context.RepositoryName,
                    Branch = context.Branch,
                    CommitSha = context.CommitSha,
                    FilePath = relPath,
                    LineStart = sym.LineStart,
                    LineEnd = sym.LineEnd,
                    RecordType = RecordType.TestReference,
                    SymbolName = sym.SymbolName,
                    SymbolKind = SymbolKind.Method,
                    Namespace = sym.Namespace,
                    ClassName = sym.ClassName,
                    MethodName = sym.MethodName,
                    Summary = $"test {sym.SymbolName}",
                    CodeSnippet = redacted.Text,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        test_framework = DetectFramework(content),
                        test_attributes = Array.Empty<string>(),
                        subjects = Array.Empty<string>(),
                    }),
                    IsTestCode = true,
                };
            }
        }

        var tree = CSharpSyntaxTree.ParseText(content);
        foreach (var hit in _flagDetector.Detect(tree, semanticModel: null, options))
        {
            Interlocked.Increment(ref Counters.FeatureFlagMatches);
            var snippet = SnippetBuilder.BuildAroundLine(content, hit.Line);
            var redacted = _redactor.Redact(snippet);
            if (redacted.Status == RedactionStatus.Failed)
            {
                Interlocked.Increment(ref Counters.RecordsRedactionFailed);
                continue;
            }
            yield return new CodeRecord
            {
                RepositoryId = context.RepositoryId,
                ScanJobId = context.ScanJobId,
                RepositoryName = context.RepositoryName,
                Branch = context.Branch,
                CommitSha = context.CommitSha,
                FilePath = relPath,
                LineStart = hit.Line,
                LineEnd = hit.Line,
                RecordType = RecordType.FeatureFlagUsage,
                SymbolName = $"{relPath}:{hit.Line}",
                FeatureFlagName = hit.FlagName,
                UsageType = hit.UsageType,
                Summary = $"Feature flag '{hit.FlagName}' usage ({hit.UsageType.ToWire()})",
                CodeSnippet = redacted.Text,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    client_type = hit.ReceiverTypeName,
                    method = hit.MethodName,
                    usage_type = hit.UsageType.ToWire(),
                    default_value = hit.DefaultValue,
                }),
                IsTestCode = isTest,
            };
        }

        if (options.SearchTerms.Count > 0)
        {
            foreach (var rec in EmitSearchTermRecords(content, relPath, context, isTest, options))
                yield return rec;
        }
    }

    internal IEnumerable<CodeRecord> EmitSearchTermRecords(string content, string relPath, ScanContext context, bool isTest, ScanOptions options)
    {
        var lines = SnippetBuilder.SplitLines(content);
        var perTermCounts = new Dictionary<(string term, SearchMatchKind kind), int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var literalHits = _searchTermMatcher.MatchInLiteral(lines[i], i + 1, 1, options.SearchTerms);
            foreach (var h in literalHits) Tally(perTermCounts, h.Term, h.Kind);

            var commentHits = _searchTermMatcher.MatchInComment(lines[i], i + 1, 1, options.SearchTerms, isXmlDoc: lines[i].TrimStart().StartsWith("///"));
            foreach (var h in commentHits) Tally(perTermCounts, h.Term, h.Kind);

            foreach (var term in options.SearchTerms)
            {
                if (LineContainsIdentifierToken(lines[i], term))
                    Tally(perTermCounts, term, SearchMatchKind.Identifier);
            }
        }

        foreach (var ((term, kind), count) in perTermCounts)
        {
            var snippet = SnippetBuilder.BuildAroundLine(content, FindFirstLineFor(lines, term));
            var redacted = _redactor.Redact(snippet);
            if (redacted.Status == RedactionStatus.Failed)
            {
                Interlocked.Increment(ref Counters.RecordsRedactionFailed);
                continue;
            }
            yield return new CodeRecord
            {
                RepositoryId = context.RepositoryId,
                ScanJobId = context.ScanJobId,
                RepositoryName = context.RepositoryName,
                Branch = context.Branch,
                CommitSha = context.CommitSha,
                FilePath = relPath,
                LineStart = FindFirstLineFor(lines, term),
                LineEnd = FindFirstLineFor(lines, term),
                RecordType = RecordType.SearchTermMatch,
                SymbolName = term,
                Summary = $"search term '{term}' matched as {kind.ToWire()} ({count}x)",
                CodeSnippet = redacted.Text,
                MetadataJson = JsonSerializer.Serialize(new { term, match_kind = kind.ToWire(), hit_count = count }),
                IsTestCode = isTest,
            };
        }
    }

    internal static int FindFirstLineFor(string[] lines, string term)
    {
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(term, StringComparison.Ordinal)) return i + 1;
        return 1;
    }

    internal static bool LineContainsIdentifierToken(string line, string term)
    {
        int idx = 0;
        while ((idx = line.IndexOf(term, idx, StringComparison.Ordinal)) >= 0)
        {
            bool boundaryLeft = idx == 0 || !IsIdentChar(line[idx - 1]);
            bool boundaryRight = idx + term.Length == line.Length || !IsIdentChar(line[idx + term.Length]);
            if (boundaryLeft && boundaryRight) return true;
            idx += term.Length;
        }
        return false;
    }

    internal static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static void Tally(Dictionary<(string, SearchMatchKind), int> dict, string term, SearchMatchKind kind)
    {
        var key = (term, kind);
        dict[key] = dict.TryGetValue(key, out var n) ? n + 1 : 1;
    }

    internal static string DetectFramework(string content)
    {
        if (content.Contains("[Fact]") || content.Contains("[Theory]") || content.Contains("Xunit")) return "xunit";
        if (content.Contains("[Test]") || content.Contains("NUnit")) return "nunit";
        if (content.Contains("[TestMethod]") || content.Contains("VisualStudio.TestTools")) return "mstest";
        return "unknown";
    }

    internal IEnumerable<CodeRecord> HandleConfigFile(string relPath, string content, ScanContext context, ScanOptions options)
    {
        var matches = _configScanner.Scan(relPath, content, options.SearchTerms);
        foreach (var m in matches)
        {
            var line = m.LineStart;
            var snippetSource = m.ValueRedacted
                ? $"{m.Key}={Redactor.Token}"
                : $"{m.Key}={m.Value}";
            var redacted = _redactor.Redact(snippetSource);
            if (redacted.Status == RedactionStatus.Failed)
            {
                Interlocked.Increment(ref Counters.RecordsRedactionFailed);
                continue;
            }
            yield return new CodeRecord
            {
                RepositoryId = context.RepositoryId,
                ScanJobId = context.ScanJobId,
                RepositoryName = context.RepositoryName,
                Branch = context.Branch,
                CommitSha = context.CommitSha,
                FilePath = relPath,
                LineStart = line,
                LineEnd = m.LineEnd,
                RecordType = RecordType.ConfigurationReference,
                SymbolName = m.Key,
                FeatureFlagName = m.Key,
                UsageType = FeatureFlagUsageType.Config,
                Summary = $"config key '{m.Key}' in {relPath}",
                CodeSnippet = redacted.Text,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    file_format = m.FileFormat,
                    json_path = m.JsonPath,
                    value_redacted = m.ValueRedacted || redacted.Status == RedactionStatus.Redacted,
                }),
            };
        }
    }

    internal static IEnumerable<string> EnumerateFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            yield return f;
    }

    private static IEnumerable<string> GatherSearchHitsForSummary(string content, ScanOptions options)
    {
        foreach (var term in options.SearchTerms)
            if (content.Contains(term, StringComparison.Ordinal)) yield return term;
    }
}
