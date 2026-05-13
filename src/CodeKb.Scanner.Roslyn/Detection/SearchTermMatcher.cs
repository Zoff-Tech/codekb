using System.Text.RegularExpressions;
using CodeKb.Contracts;

namespace CodeKb.Scanner.Roslyn.Detection;

public sealed record SearchHit(string Term, int Line, int Column, SearchMatchKind Kind);

public interface ISearchTermMatcher
{
    IReadOnlyList<SearchHit> MatchInIdentifier(string identifier, int line, int column, IReadOnlyList<string> terms);
    IReadOnlyList<SearchHit> MatchInLiteral(string literal, int line, int column, IReadOnlyList<string> terms);
    IReadOnlyList<SearchHit> MatchInComment(string comment, int line, int column, IReadOnlyList<string> terms, bool isXmlDoc);
}

public sealed class SearchTermMatcher : ISearchTermMatcher
{
    public IReadOnlyList<SearchHit> MatchInIdentifier(string identifier, int line, int column, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrEmpty(identifier) || terms.Count == 0) return Array.Empty<SearchHit>();
        var hits = new List<SearchHit>();
        foreach (var term in terms)
            if (string.Equals(identifier, term, StringComparison.Ordinal))
                hits.Add(new SearchHit(term, line, column, SearchMatchKind.Identifier));
        return hits;
    }

    public IReadOnlyList<SearchHit> MatchInLiteral(string literal, int line, int column, IReadOnlyList<string> terms)
        => MatchWordBoundary(literal, line, column, terms, SearchMatchKind.StringLiteral);

    public IReadOnlyList<SearchHit> MatchInComment(string comment, int line, int column, IReadOnlyList<string> terms, bool isXmlDoc)
        => MatchWordBoundary(comment, line, column, terms, isXmlDoc ? SearchMatchKind.XmlDoc : SearchMatchKind.Comment);

    private static IReadOnlyList<SearchHit> MatchWordBoundary(string text, int line, int column, IReadOnlyList<string> terms, SearchMatchKind kind)
    {
        if (string.IsNullOrEmpty(text) || terms.Count == 0) return Array.Empty<SearchHit>();
        var hits = new List<SearchHit>();
        foreach (var term in terms)
        {
            var pattern = $@"\b{Regex.Escape(term)}\b";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                hits.Add(new SearchHit(term, line, column, kind));
        }
        return hits;
    }
}
