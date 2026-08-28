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

    // semantic_work and semantic_concept are deliberately NOT text: the producer's work
    // vector is subjects plus names, never provision text, and the concept arm is unreachable
    // and kind-unbound (Codex B2 review O3). Both fall through to unclassified_render.
    private static readonly HashSet<string> TextReasons =
        ["keyword", "semantic", "article_intent", "fuzzy"];

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

    /// <summary>A candidate disclosure row straight off served hit fields, validated here.</summary>
    public sealed record DisclosureRow(string Publisher, string Work, string ValidFrom, string Title);

    private static readonly System.Text.RegularExpressions.Regex WorkGrammar = new(
        "^[a-z0-9][a-z0-9._-]{0,199}$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex PublisherGrammar = new(
        "^[a-z0-9][a-z0-9-]{0,63}$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex DateGrammar = new(
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Every hit of a multi-publisher search response paired with its envelope publisher.
    /// Refused envelopes carry no hits array and simply contribute nothing, so a refusal
    /// beside metadata hits neither blocks nor fakes the response-level state.
    /// </summary>
    /// <summary>
    /// The only status under which a search envelope actually executed. Verified against the
    /// producer: the search case emits ok, retrieval_mode_unavailable, unknown_work,
    /// unknown_anchor and no_provision_history, and never no_result. Round 1 admitted
    /// no_result, so a cross-operation envelope carrying metadata hits could authorize
    /// suppression (B1+B2 round 2 review, O1).
    /// </summary>
    private static readonly HashSet<string> SearchSuccessStatuses = ["ok"];

    /// <summary>
    /// The authoritative population plus whether it is COMPLETE. Round 1 discarded response
    /// invalidity: a successful envelope whose hits field was malformed was read as empty, so
    /// a sibling metadata-only response could still authorize suppression (B1+B2 round 2
    /// review, O2). An incomplete population makes the positive claim unreachable.
    /// </summary>
    public static (IReadOnlyList<(string Publisher, JsonObject Hit)> Rows, bool Complete)
        ResponsePopulation(JsonArray envelopes)
    {
        var rows = new List<(string, JsonObject)>();
        var complete = true;
        foreach (var node in envelopes)
        {
            if (node is not JsonObject result) { complete = false; continue; }
            var envelope = result["envelope"] as JsonObject;
            var status = envelope?["status"] is JsonValue statusValue
                && statusValue.TryGetValue<string>(out var statusText) ? statusText : null;
            if (status is null) { complete = false; continue; }
            // Only an authoritative successful envelope may contribute to the positive
            // metadata_only claim; a refusal's rows are not evidence.
            if (!SearchSuccessStatuses.Contains(status)) continue;
            if (result["hits"] is not JsonArray hits) { complete = false; continue; }
            var publisher = envelope!["publisher"] is JsonValue value
                && value.TryGetValue<string>(out var text) ? text : "";
            foreach (var hitNode in hits)
            {
                if (hitNode is not JsonObject hit) { complete = false; continue; }
                // A reasons field that is present but not an array of strings is malformed
                // evidence, not absent evidence.
                if (hit["match_reasons"] is { } reasonsNode
                    && (reasonsNode is not JsonArray reasons
                        || reasons.Any(reason => reason is not JsonValue member
                            || !member.TryGetValue<string>(out _))))
                {
                    complete = false;
                    continue;
                }
                rows.Add((publisher, hit));
            }
        }
        return (rows, complete);
    }

    /// <summary>
    /// True when any envelope reports a truncated row set (B1+B2 review, O4). The producer
    /// carries response_row_set.truncated; an exact overflow count would be a claim the
    /// response cannot support, so the countersigned fallback sentence is used instead.
    /// </summary>
    public static bool AnyRowSetTruncated(JsonArray envelopes) =>
        envelopes.OfType<JsonObject>().Any(result =>
            (result["response_row_set"] as JsonObject)?["truncated"] is JsonValue value
            && value.TryGetValue<bool>(out var truncated) && truncated);

    /// <summary>
    /// The server-page notice plus the subordinate disclosure list, for the ONE response-level
    /// metadata_only state (Codex B2 review, O1: classification is never per publisher).
    /// Rows are validated fail closed against the B1 coordinate grammar and links are rebuilt
    /// from the parsed parts; invalid rows are omitted without suppressing the notice (O4).
    /// One exact-host official action renders per represented collection. Byte-exact
    /// boundaries and append-only insertion, per the B1 classifier finding.
    /// </summary>
    public static string NoticeHtml(
        IReadOnlyList<string> collections,
        IReadOnlyList<DisclosureRow> rows,
        bool truncated = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var valid = rows.Where(row =>
                PublisherGrammar.IsMatch(row.Publisher)
                && WorkGrammar.IsMatch(row.Work)
                && DateGrammar.IsMatch(row.ValidFrom)
                && seen.Add($"{row.Publisher}:{row.Work}"))
            .ToArray();
        var items = string.Join("", valid.Take(10).Select(row =>
        {
            var title = row.Title.Length is > 0 and <= 300 ? row.Title
                : row.Title.Length > 300 ? row.Title[..300] : row.Work;
            return $"<li><a href=\"/{H(row.Publisher)}/{H(row.Work)}/{H(row.ValidFrom)}\">"
                + $"{H(title)}</a> <span class=\"sub mono\">{H(row.Work)} · {H(row.Publisher)}"
                + " · matched in metadata</span></li>";
        }));
        // C3 ruling: N counts only valid, logically deduplicated suppressed matches present
        // in this bounded response, minus the rows shown; never a corpus-wide claim. Search
        // envelopes carry no truncation marker today, so the exact count always exists.
        // C3: an exact N is only honest when the response is complete. A truncated row set
        // holds no exact total, so the countersigned fallback sentence is used instead of
        // inventing one.
        var overflow = truncated
            ? "<span class=\"sub\">additional returned matches are not shown</span>"
            : valid.Length <= 10 ? ""
            : $"<span class=\"sub\">and {valid.Length - 10} more returned matches</span>";
        var disclosure = valid.Length == 0 ? "" :
            $"<details><summary>{H(DisclosureLabel)}</summary><ul>{items}</ul>{overflow}</details>";
        var officials = string.Join("", collections
            .Where(collection => PublisherGrammar.IsMatch(collection))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(collection => collection, StringComparer.Ordinal)
            .Select(WorkCandidates.OfficialSearchHref)
            .Distinct(StringComparer.Ordinal)
            .Select(official => official.StartsWith("https://", StringComparison.Ordinal)
                ? $" &nbsp;&nbsp;<a href=\"{H(official)}\" rel=\"noopener\">Search the official publisher</a>"
                : $" &nbsp;&nbsp;<a href=\"{H(official)}\">Search Lex</a>"));
        return "<div class=\"notice\" role=\"note\" data-testid=\"metadata-only-notice\""
            + $" aria-label=\"{H(Heading)}\">"
            + $"<b>{H(Heading)}.</b> {H(Body)}"
            + disclosure
            + $"<span class=\"sub\"><a href=\"/coverage\">View coverage and known gaps</a>"
            + $"{officials}</span>"
            + "</div>";
    }

    /// <summary>
    /// Served reasons for one hit, read fail-closed (B1+B2 review, O5). A missing array is
    /// unclassified. A member that is not a JSON string is mapped to null rather than read
    /// through GetValue&lt;string&gt;, which throws on a number or object and would take the
    /// whole page down; null is an unknown reason, so the hit renders and is never suppressed.
    /// </summary>
    public static IReadOnlyList<string?> ReasonsOf(JsonObject hit) =>
        hit["match_reasons"] is JsonArray reasons
            ? reasons.Select(reason =>
                reason is JsonValue value && value.TryGetValue<string>(out var text)
                    ? text : null).ToArray()
            : [];
}
