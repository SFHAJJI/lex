using Lex.Index;
using static Lex.Web.PageShell;

namespace Lex.Web;

/// <summary>
/// Nearest held records for an unknown-work refusal (Decision 41, unknown_work notice). An
/// unknown identifier is usually a near miss, not a fabrication; showing the closest held
/// records turns a dead end into a one-click correction while the copy keeps the honest
/// boundary: absence of a record is never evidence that the law does not exist.
/// </summary>
public static class WorkCandidates
{
    public const int Limit = 5;

    /// <summary>Decision 41 frozen copy, heading and body, shared by every surface.</summary>
    public const string Heading = "Instrument not found in held records";
    public const string Body =
        "Lex does not hold an instrument matching this identifier. This is not evidence that "
        + "the instrument or law does not exist. Check the identifier, choose a possible held "
        + "record below, or search the official publisher.";
    public const string CandidatesHeading = "Possible held records";

    /// <summary>
    /// The official publisher search entry per collection, exact hosts only. Unknown
    /// collections fall back to the internal search page rather than guessing a URL.
    /// </summary>
    public static string OfficialSearchHref(string collection) => collection switch
    {
        "lu-legilux" => "https://legilux.public.lu",
        "eu-eurlex" => "https://eur-lex.europa.eu",
        _ => "/search",
    };

    public sealed record Candidate(string Work, string? Title, string Publisher);

    /// <summary>
    /// Nearest held works for a requested identifier: the verbatim string first, then a
    /// separator-widened variant so a near-miss slug (one wrong ordinal, a missing suffix)
    /// still finds its neighbours. Read-only, bounded, distinct by work.
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
        yield return requested;
        var widened = requested.Replace('-', ' ').Replace('_', ' ').Replace(':', ' ');
        if (!string.Equals(widened, requested, StringComparison.Ordinal))
            yield return widened;
        // The lookup is conjunctive, so a single wrong token (usually the most specific,
        // trailing one: an ordinal, a suffix) hides every neighbour. Drop trailing tokens
        // progressively; the shortest query still needs two tokens so "loi" alone never
        // floods the candidate list.
        var tokens = widened.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var keep = tokens.Length - 1; keep >= 2 && keep >= tokens.Length - 2; keep--)
            yield return string.Join(' ', tokens.Take(keep));
    }

    /// <summary>The server-page notice: frozen copy, candidate list, official search action.</summary>
    public static string NoticeHtml(
        string requested, string collection, IReadOnlyList<Candidate> candidates)
    {
        var items = string.Join("", candidates.Select(candidate =>
            $"<li><a href=\"/{H(candidate.Publisher)}/{H(candidate.Work)}\">"
            + $"{H(candidate.Title ?? candidate.Work)}</a> "
            + $"<span class=\"sub mono\">{H(candidate.Work)} · {H(candidate.Publisher)}</span></li>"));
        var list = candidates.Count > 0
            ? $"<p><b>{H(CandidatesHeading)}</b></p><ul>{items}</ul>"
            : "";
        var official = OfficialSearchHref(collection);
        var officialLink = official.StartsWith("https://", StringComparison.Ordinal)
            ? $"<a href=\"{H(official)}\" rel=\"noopener\">Search the official publisher</a>"
            : $"<a href=\"{H(official)}\">Search Lex</a>";
        // Byte-exact boundaries: the notice must begin at <div and end at </div> with nothing
        // outside, because the golden classifier requires every inserted byte to live inside
        // the declared subtree. Raw-string margins would leak boundary whitespace.
        return "<div class=\"notice\" role=\"note\" data-testid=\"unknown-work-notice\""
            + $" aria-label=\"{H(Heading)}\">"
            + $"<b>{H(Heading)}.</b> status <span class=\"mono\">unknown_work</span>, requested "
            + $"<span class=\"mono\">{H(requested)}</span>. {H(Body)}"
            + list
            + $"<span class=\"sub\"><a href=\"/search\">Search Lex</a> &nbsp;&nbsp;{officialLink}</span>"
            + "</div>";
    }
}
