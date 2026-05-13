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
        if (!string.IsNullOrEmpty(record.CodeSnippet))
        {
            sb.Append("Code Snippet:\n").Append(record.CodeSnippet);
            if (!record.CodeSnippet.EndsWith("\n")) sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendIfNonEmpty(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(label).Append(": ").Append(value).Append('\n');
    }
}
