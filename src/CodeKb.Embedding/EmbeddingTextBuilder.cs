using System.Text;
using CodeKb.Contracts;

namespace CodeKb.Embedding;

public static class EmbeddingTextBuilder
{
    public static string Build(CodeRecord record)
    {
        var sb = new StringBuilder();
        AppendIfNonEmpty(sb, "Repository", record.RepositoryName);
        AppendIfNonEmpty(sb, "Branch", record.Branch);
        AppendIfNonEmpty(sb, "Commit", record.CommitSha);
        AppendIfNonEmpty(sb, "File", $"{record.FilePath}:{record.LineStart}-{record.LineEnd}");
        AppendIfNonEmpty(sb, "Language", "csharp");
        AppendIfNonEmpty(sb, "Record Type", record.RecordType.ToWire());
        AppendIfNonEmpty(sb, "Namespace", record.Namespace);
        AppendIfNonEmpty(sb, "Class", record.ClassName);
        AppendIfNonEmpty(sb, "Method", record.MethodName);
        AppendIfNonEmpty(sb, "Symbol", record.SymbolName);
        AppendIfNonEmpty(sb, "Feature Flag", record.FeatureFlagName);
        if (record.UsageType.HasValue)
            AppendIfNonEmpty(sb, "Usage Type", record.UsageType.Value.ToWire());
        AppendIfNonEmpty(sb, "Summary", record.Summary);
        AppendIfNonEmpty(sb, "Tokens", BuildTokenLine(record));
        if (!string.IsNullOrEmpty(record.CodeSnippet))
        {
            sb.Append("Code Snippet:\n").Append(record.CodeSnippet);
            if (!record.CodeSnippet.EndsWith("\n")) sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    internal static string BuildTokenLine(CodeRecord record)
    {
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddTokens(record.Namespace, tokens, seen);
        AddTokens(record.ClassName, tokens, seen);
        AddTokens(record.MethodName, tokens, seen);
        AddTokens(record.SymbolName, tokens, seen);
        AddTokens(record.FeatureFlagName, tokens, seen);
        AddTokens(FileBaseName(record.FilePath), tokens, seen);
        return tokens.Count == 0 ? string.Empty : string.Join(' ', tokens);
    }

    private static void AddTokens(string? source, List<string> tokens, HashSet<string> seen)
    {
        foreach (var tok in IdentifierTokenizer.Split(source))
            if (seen.Add(tok)) tokens.Add(tok);
    }

    private static string? FileBaseName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var slash = Math.Max(path!.LastIndexOf('/'), path.LastIndexOf('\\'));
        var name = slash >= 0 ? path.Substring(slash + 1) : path;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    private static void AppendIfNonEmpty(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(label).Append(": ").Append(value).Append('\n');
    }
}
