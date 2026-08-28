using Lex.Index;

namespace Lex.Mcp;

/// <summary>
/// Nearest held works for an unknown identifier (Decision 41, unknown_work). An unknown
/// identifier is usually a near miss; the finder surfaces the closest held records so a dead
/// end becomes a one-click correction, while every consumer keeps the honest boundary:
/// absence of a record is never evidence that the law does not exist. Lives beside McpCore so
/// both the MCP envelopes and the server pages share one bounded implementation.
/// </summary>
public static class WorkCandidateFinder
{
    public const int Limit = 5;

    public sealed record Candidate(string Work, string? Title, string Publisher);

    /// <summary>
    /// The verbatim identifier first, then a separator-widened variant, then the widened
    /// variant with trailing tokens dropped progressively: the underlying lookup is
    /// conjunctive, so a single wrong trailing token (an ordinal, a suffix) would otherwise
    /// hide every neighbour. The shortest query keeps two tokens so one generic word never
    /// floods the list. Read-only, bounded, distinct by work.
    /// </summary>
    public static IReadOnlyList<Candidate> Nearest(LexIndexReader reader, string requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || requested.Length > 200) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Candidate>();
        foreach (var query in Queries(requested))
        {
            foreach (var document in reader.SearchWorksByIdentifierOrTitle(
                         query, FilterSet.All, Limit * 2))
            {
                if (!seen.Add(document.GroupKey)) continue;
                result.Add(new Candidate(
                    document.GroupKey, document.TitleShort ?? document.Title,
                    document.Collection));
                if (result.Count >= Limit) return result;
            }
            if (result.Count > 0) break;
        }
        return result;
    }

    private static IEnumerable<string> Queries(string requested)
    {
        // A collection-qualified identifier ("lu-legilux:loi-...") would poison conjunctive
        // widening with its publisher tokens, so the tail after the final colon gets its own
        // full ladder alongside the verbatim form.
        var forms = new List<string> { requested };
        var colon = requested.LastIndexOf(':');
        if (colon > 0 && colon < requested.Length - 1)
            forms.Add(requested[(colon + 1)..]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            if (seen.Add(form)) yield return form;
            var widened = form.Replace('-', ' ').Replace('_', ' ').Replace(':', ' ');
            if (seen.Add(widened)) yield return widened;
            var tokens = widened.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Down to a single remaining token: the lookup already drops sub-two-character
            // terms, the result cap bounds the flood, and the copy labels these as
            // possibilities, never matches.
            for (var keep = tokens.Length - 1; keep >= 1 && keep >= tokens.Length - 2; keep--)
            {
                var query = string.Join(' ', tokens.Take(keep));
                if (seen.Add(query)) yield return query;
            }
        }
    }
}
