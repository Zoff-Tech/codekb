using System.Text;

namespace CodeKb.Embedding;

/// <summary>
/// Splits identifier-shaped text into sub-tokens so that the embedding model
/// sees the component words. Handles PascalCase / camelCase (with acronyms),
/// kebab-case, snake_case, dotted names, and path separators.
/// </summary>
public static class IdentifierTokenizer
{
    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Add(text!, tokens, seen);

        foreach (var part in text!.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            Add(part, tokens, seen);
            foreach (var sub in SplitCase(part))
                Add(sub, tokens, seen);
        }

        return tokens;
    }

    public static string Join(string? text)
    {
        var parts = Split(text);
        return parts.Count == 0 ? string.Empty : string.Join(' ', parts);
    }

    private static readonly char[] Separators = new[] { '-', '_', '.', '/', '\\', ' ', '\t' };

    private static void Add(string value, List<string> tokens, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (seen.Add(value)) tokens.Add(value);
    }

    /// <summary>
    /// Splits a single identifier on camelCase / PascalCase boundaries.
    /// "PrePaidAccount" → ["Pre", "Paid", "Account"]
    /// "XMLParser"      → ["XML", "Parser"]
    /// "IOError"        → ["IO", "Error"]
    /// "lowerOnly"      → ["lower", "Only"]
    /// "Plain"          → ["Plain"]
    /// "ABC"            → ["ABC"]
    /// </summary>
    internal static IReadOnlyList<string> SplitCase(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return Array.Empty<string>();
        if (!ContainsLetter(identifier)) return new[] { identifier };

        var result = new List<string>();
        var current = new StringBuilder();
        current.Append(identifier[0]);

        for (int i = 1; i < identifier.Length; i++)
        {
            var prev = identifier[i - 1];
            var ch = identifier[i];

            bool boundary = false;
            if (char.IsLower(prev) && char.IsUpper(ch))
            {
                // camelCase boundary: aB → a | B
                boundary = true;
            }
            else if (char.IsUpper(prev) && char.IsUpper(ch)
                     && i + 1 < identifier.Length && char.IsLower(identifier[i + 1]))
            {
                // acronym → word boundary: XMLParser → XML | Parser
                boundary = true;
            }
            else if (char.IsLetter(prev) != char.IsLetter(ch) && char.IsLetterOrDigit(ch))
            {
                // letter-digit transitions: V2 → V | 2, http2 → http | 2
                boundary = true;
            }

            if (boundary)
            {
                if (current.Length > 0) result.Add(current.ToString());
                current.Clear();
            }
            current.Append(ch);
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    private static bool ContainsLetter(string s)
    {
        foreach (var ch in s) if (char.IsLetter(ch)) return true;
        return false;
    }
}
