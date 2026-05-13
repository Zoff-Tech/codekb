namespace CodeKb.Scanner.Roslyn.Snippets;

public static class SnippetBuilder
{
    public const int MaxMethodLines = 200;
    public const int MaxBytes = 4096;
    public const int FlagContextLines = 20;

    public static string BuildMethod(string source, int startLine, int endLine)
    {
        if (startLine < 1) startLine = 1;
        var lines = SplitLines(source);
        if (endLine > lines.Length) endLine = lines.Length;

        var take = Math.Min(endLine - startLine + 1, MaxMethodLines);
        var subset = new string[take];
        Array.Copy(lines, startLine - 1, subset, 0, take);
        var joined = string.Join('\n', subset);

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(joined);
        if (byteCount > MaxBytes)
            joined = TruncateToBytes(joined, MaxBytes);

        joined = TruncateAtStatementBoundary(joined);
        return joined;
    }

    public static string BuildClassSignatures(string classDeclarationHeader, IEnumerable<string> memberSignatures)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(classDeclarationHeader);
        if (!classDeclarationHeader.EndsWith("\n")) sb.Append('\n');
        sb.Append("{\n");
        foreach (var sig in memberSignatures)
        {
            sb.Append("    ").Append(sig).Append('\n');
            if (System.Text.Encoding.UTF8.GetByteCount(sb.ToString()) > MaxBytes) break;
        }
        sb.Append("}");
        var result = sb.ToString();
        if (System.Text.Encoding.UTF8.GetByteCount(result) > MaxBytes)
            result = TruncateToBytes(result, MaxBytes);
        return result;
    }

    public static string BuildAroundLine(string source, int matchLine, int contextLines = FlagContextLines, int? enclosingStart = null, int? enclosingEnd = null)
    {
        if (matchLine < 1) matchLine = 1;
        var lines = SplitLines(source);
        if (lines.Length == 0) return string.Empty;
        if (matchLine > lines.Length) matchLine = lines.Length;

        int start = Math.Max(1, matchLine - contextLines);
        int end = Math.Min(lines.Length, matchLine + contextLines);

        if (enclosingStart.HasValue) start = Math.Max(start, enclosingStart.Value);
        if (enclosingEnd.HasValue) end = Math.Min(end, enclosingEnd.Value);

        var subset = new string[end - start + 1];
        Array.Copy(lines, start - 1, subset, 0, subset.Length);
        var joined = string.Join('\n', subset);
        if (System.Text.Encoding.UTF8.GetByteCount(joined) > MaxBytes)
            joined = TruncateToBytes(joined, MaxBytes);
        return joined;
    }

    public static string BuildFileSummary(string filePath, IEnumerable<string> namespaces, IEnumerable<string> topLevelTypes, IEnumerable<string> searchTermHits)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("File: ").Append(filePath).Append('\n');
        var nsList = namespaces.Distinct().ToArray();
        if (nsList.Length > 0)
            sb.Append("Namespaces: ").Append(string.Join(", ", nsList)).Append('\n');
        var typeList = topLevelTypes.ToArray();
        if (typeList.Length > 0)
            sb.Append("Top-level types: ").Append(string.Join(", ", typeList)).Append('\n');
        var hitList = searchTermHits.Distinct().ToArray();
        if (hitList.Length > 0)
            sb.Append("Search-term hits: ").Append(string.Join(", ", hitList)).Append('\n');
        var result = sb.ToString();
        if (System.Text.Encoding.UTF8.GetByteCount(result) > MaxBytes)
            result = TruncateToBytes(result, MaxBytes);
        return result;
    }

    internal static string[] SplitLines(string s) =>
        s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    internal static string TruncateToBytes(string s, int maxBytes)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes) return s;
        var safeLen = maxBytes;
        // Back off continuation bytes (10xxxxxx)
        while (safeLen > 0 && (bytes[safeLen - 1] & 0b1100_0000) == 0b1000_0000) safeLen--;
        // If the last byte we kept is a start byte of a multi-byte sequence, drop it too — incomplete sequence
        if (safeLen > 0 && (bytes[safeLen - 1] & 0b1100_0000) == 0b1100_0000) safeLen--;
        return System.Text.Encoding.UTF8.GetString(bytes, 0, safeLen);
    }

    internal static string TruncateAtStatementBoundary(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // Prefer ending at ; or } over a bare newline so the snippet looks like a complete statement.
        int semiOrBrace = s.LastIndexOfAny(new[] { ';', '}' });
        if (semiOrBrace >= 0) return s.Substring(0, semiOrBrace + 1);
        int nl = s.LastIndexOf('\n');
        if (nl >= 0) return s.Substring(0, nl + 1);
        return s;
    }
}
