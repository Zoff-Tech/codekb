namespace CodeKb.Scanner.Roslyn;

public sealed class IgnorePathFilter
{
    private readonly string[] _segments;

    public IgnorePathFilter(IEnumerable<string> ignorePaths)
    {
        _segments = ignorePaths
            .Select(s => s.Trim().Trim('/').ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToArray();
    }

    public bool IsIgnored(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').ToLowerInvariant();
        foreach (var seg in _segments)
        {
            if (normalized == seg) return true;
            if (normalized.StartsWith(seg + "/")) return true;
            if (normalized.Contains("/" + seg + "/")) return true;
            if (normalized.EndsWith("/" + seg)) return true;
        }
        return false;
    }
}
