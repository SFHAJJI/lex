using System.Text.Json.Nodes;
using static Lex.Web.PageShell;

namespace Lex.Web;

/// <summary>
/// The B2 lane classifier (agreed design plus the Codex ruling of 2026-08-28). A hit's served
/// match reasons place it in exactly one lane: text (provision evidence), identity (a name or
/// identifier answers "which law is this"), metadata (a subject association, never an answer)
/// or unclassified_render (an unknown reason renders through the existing visible path and is
/// never suppressed, but the code must not assert identity it has not classified). The
/// normative case table in tests/Lex.Tests/match-lane-cases.json binds this classifier and its
/// TypeScript twin to the same behavior.
/// </summary>
public static class MatchLanes
{
    public const string Text = "text";
    public const string Identity = "identity";
    public const string Metadata = "metadata";
    public const string UnclassifiedRender = "unclassified_render";

    private static readonly HashSet<string> TextReasons =
        ["keyword", "semantic", "semantic_work", "semantic_concept", "article_intent", "fuzzy"];

    private static readonly string[] IdentitySuffixes =
        ["_identifier", "_publisher_short_title", "_title", "_alias"];

    private const string AmbiguousPrefix = "ambiguous_";
    private const string MetadataReason = "work_metadata";

    public static string Classify(IReadOnlyList<string?> reasons)
    {
        if (reasons.Count == 0) return UnclassifiedRender;
        var sawIdentity = false;
        var sawMetadata = false;
        foreach (var raw in reasons)
        {
            var reason = raw ?? "";
            if (TextReasons.Contains(reason)) return Text;
            if (reason.StartsWith(AmbiguousPrefix, StringComparison.Ordinal))
            {
                // Ambiguity arises only during identity resolution: render, never suppress.
                sawIdentity = true;
                continue;
            }
            if (IdentitySuffixes.Any(suffix =>
                    reason.EndsWith(suffix, StringComparison.Ordinal)))
            {
                sawIdentity = true;
                continue;
            }
            if (string.Equals(reason, MetadataReason, StringComparison.Ordinal))
            {
                sawMetadata = true;
                continue;
            }
            // An unknown reason forbids every positive claim for this hit.
            return UnclassifiedRender;
        }
        if (sawIdentity) return Identity;
        return sawMetadata ? Metadata : UnclassifiedRender;
    }

    /// <summary>
    /// The response-level state: metadata_only holds only when at least one hit exists and
    /// every hit is POSITIVELY metadata (Codex Q1 ruling: unclassified never triggers the
    /// notice, it renders through the normal path).
    /// </summary>
    public static bool MetadataOnly(IReadOnlyList<IReadOnlyList<string?>> hitReasons) =>
        hitReasons.Count > 0
        && hitReasons.All(reasons => Classify(reasons) == Metadata);

    /// <summary>Decision 41 frozen copy for the metadata_only notice.</summary>
    public const string Heading = "No held text match";
    public const string Body =
        "Lex found records that match only in metadata. They are not shown as text answers. "
        + "This is not evidence that the named instrument or law does not exist. Check the name "
        + "or identifier, review coverage and known gaps, or search the official publisher.";
    public const string DisclosureLabel = "Matched only in metadata";

    /// <summary>
    /// The server-page notice plus the subordinate disclosure list. Byte-exact boundaries and
    /// append-only insertion, per the B1 classifier finding.
    /// </summary>
    public static string NoticeHtml(
        string collection,
        IReadOnlyList<(string Href, string Title, string Detail)> matches)
    {
        var official = WorkCandidates.OfficialSearchHref(collection);
        var officialLink = official.StartsWith("https://", StringComparison.Ordinal)
            ? $"<a href=\"{H(official)}\" rel=\"noopener\">Search the official publisher</a>"
            : $"<a href=\"{H(official)}\">Search Lex</a>";
        var items = string.Join("", matches.Select(match =>
            $"<li><a href=\"{H(match.Href)}\">{H(match.Title)}</a> "
            + $"<span class=\"sub mono\">{H(match.Detail)}</span></li>"));
        var disclosure = matches.Count == 0 ? "" :
            $"<details><summary>{H(DisclosureLabel)}</summary><ul>{items}</ul></details>";
        return "<div class=\"notice\" role=\"note\" data-testid=\"metadata-only-notice\""
            + $" aria-label=\"{H(Heading)}\">"
            + $"<b>{H(Heading)}.</b> {H(Body)}"
            + disclosure
            + $"<span class=\"sub\"><a href=\"/coverage\">View coverage and known gaps</a>"
            + $" &nbsp;&nbsp;{officialLink}</span>"
            + "</div>";
    }

    /// <summary>Served reasons for one hit, read fail-open: a missing array is unclassified.</summary>
    public static IReadOnlyList<string?> ReasonsOf(JsonObject hit) =>
        hit["match_reasons"] is JsonArray reasons
            ? reasons.Select(reason => reason?.GetValue<string>()).ToArray()
            : [];
}
