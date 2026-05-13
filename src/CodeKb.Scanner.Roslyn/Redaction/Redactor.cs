using System.Text.RegularExpressions;

namespace CodeKb.Scanner.Roslyn.Redaction;

public enum RedactionStatus
{
    NoMatch,
    Redacted,
    Failed,
}

public sealed record RedactionResult(string Text, RedactionStatus Status, IReadOnlyList<string> PatternsHit);

public interface IRedactor
{
    RedactionResult Redact(string snippet);
}

public sealed class Redactor : IRedactor
{
    public const string Token = "«REDACTED»";

    private static readonly Regex KeyValueRegex = new(
        @"(?ix)
            (?<key>password|secret|token|api[_-]?key|connection[_-]?string|client[_-]?secret|private[_-]?key)
            \s*[:=]\s*
            (?<quote>[""']?)
            (?<value>[^""'\r\n,;]+)
            \k<quote>
        ",
        RegexOptions.Compiled);

    private static readonly Regex AwsAccessKey = new(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled);
    private static readonly Regex GitHubPat = new(@"\b(ghp_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{20,})\b", RegexOptions.Compiled);
    private static readonly Regex Jwt = new(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", RegexOptions.Compiled);
    private static readonly Regex PemBlock = new(@"-----BEGIN [A-Z ]+PRIVATE KEY-----[\s\S]+?-----END [A-Z ]+PRIVATE KEY-----", RegexOptions.Compiled);

    private static readonly Regex InterpolatedSecret = new(
        @"\$""[^""]*\{[^}]*(password|secret|token|api[_-]?key)[^}]*\}[^""]*""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RedactionResult Redact(string snippet)
    {
        if (string.IsNullOrEmpty(snippet))
            return new RedactionResult(snippet ?? string.Empty, RedactionStatus.NoMatch, Array.Empty<string>());

        var hits = new List<string>();

        if (InterpolatedSecret.IsMatch(snippet))
        {
            hits.Add("interpolated_secret");
            return new RedactionResult(snippet, RedactionStatus.Failed, hits);
        }

        var result = snippet;
        result = ReplaceWithReport(result, PemBlock, "pem_private_key", hits, _ => Token);
        result = ReplaceWithReport(result, Jwt, "jwt", hits, _ => Token);
        result = ReplaceWithReport(result, GitHubPat, "github_pat", hits, _ => Token);
        result = ReplaceWithReport(result, AwsAccessKey, "aws_access_key", hits, _ => Token);

        result = KeyValueRegex.Replace(result, m =>
        {
            hits.Add($"key_value:{NormalizeKeyName(m.Groups["key"].Value)}");
            var quote = m.Groups["quote"].Value;
            return $"{m.Groups["key"].Value}={quote}{Token}{quote}";
        });

        var status = hits.Count == 0 ? RedactionStatus.NoMatch : RedactionStatus.Redacted;
        return new RedactionResult(result, status, hits);
    }

    internal static string NormalizeKeyName(string captured)
    {
        var lower = captured.ToLowerInvariant();
        // Normalize: insert underscore in connectionstring → connection_string, etc.
        return lower switch
        {
            "connectionstring" => "connection_string",
            "apikey" => "api_key",
            "clientsecret" => "client_secret",
            "privatekey" => "private_key",
            _ => lower,
        };
    }

    private static string ReplaceWithReport(string input, Regex rx, string label, List<string> hits, MatchEvaluator eval)
    {
        if (!rx.IsMatch(input)) return input;
        return rx.Replace(input, m =>
        {
            hits.Add(label);
            return eval(m);
        });
    }
}
