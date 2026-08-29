using Lex.Index;
using static Lex.Web.PageShell;

namespace Lex.Web;

/// <summary>
/// The Phase 0 trust notices: typed, evidence-conditioned banners whose exact copy is frozen by
/// Decisions 41 and 44. A notice renders only when its typed evidence condition is satisfied by
/// the mounted index; missing evidence never becomes a prose claim. Server code decides, browser
/// code only displays.
/// </summary>
public static class TrustNotices
{
    // The interim temporary-derogation banner is deliberately hardcoded to one act and one
    // article (Decision 41: an interim banner until relation ingestion lands in Phase 3; the
    // work slug is the Code du travail's production coordinate, re-checked against the live
    // index in the acceptance evidence; a canon/2 slug change must update it via the alias
    // review). The
    // body follows Decision 44: it names the act and its act date, which are derivable from the
    // publisher identifier, and says plainly that the act-level force-boundary dates are not
    // held. A consolidation-state interval boundary is never rendered as an act-level
    // entry-into-force or no-longer-in-force fact (Decision 44(b)).
    internal const string DerogationPublisher = "lu-legilux";
    internal const string DerogationWork = "loi-2006-07-31-n2";
    internal const string DerogationAnchor = "art_l_121-6";
    internal const string DerogationAct = "loi-2020-12-19-a1039";
    internal const string DerogationActDateText = "19 December 2020";

    /// <summary>
    /// The unknown_work refusal, with the nearest held records. Copy frozen by Decision 41.
    ///
    /// The refusal itself always renders, because the page exists precisely when the identifier
    /// resolves to nothing. What is evidence-conditioned is the candidate block: it appears only
    /// when the index actually returns held records, so the offer is never a promise the corpus
    /// cannot keep.
    ///
    /// This replaces a sterile refusal. The live page said the work was not held and pointed at
    /// search, which trains a reader that honesty means uselessness; the verdict names that
    /// pattern directly. Absence of a record is also never absence of law, which is why the body
    /// says so in its own second sentence rather than leaving a reader to infer it.
    /// </summary>
    public static string UnknownWork(LexIndexReader reader, string publisher, string workSlug)
    {
        // One deliberate string: frozen copy must never depend on source-code line wrapping,
        // because tests and reviews assert it verbatim.
        var body = "Lex does not hold an instrument matching this identifier. This is not evidence "
            + "that the instrument or law does not exist. Check the identifier, choose a possible "
            + "held record below, or search the official publisher.";

        var candidates = NearestHeld(reader, workSlug);

        var offered = candidates.Count == 0 ? "" : $"""
            <h2>Possible held records</h2>
            <ul class="rows">
            {string.Join("", candidates.Select(row => $"""
                <li><a href="/{H(row.Collection)}/{H(row.GroupKey)}">{H(Describe(row))}</a>
                <span class="sub mono">{H(row.GroupIdentifier)} &middot; {H(row.Collection)}</span></li>
                """))}
            </ul>
            """;

        return $"""
            <div class="notice" role="note" aria-label="Instrument not found in held records">
            <b>Instrument not found in held records.</b>
            {body}
            <span class="sub"><a href="/search">Search the official publisher</a></span>
            </div>
            {offered}
            """;
    }

    /// <summary>
    /// The held records nearest to a slug that is not held.
    ///
    /// The underlying search is a substring match, so the exact miss this notice exists for finds
    /// nothing on its own: a wrong trailing segment. The verdict's own example is the question
    /// catalog asking for `loi-2004-11-12-n3` when the held work is `loi-2004-11-12-n1`, and
    /// `%loi-2004-11-12-n3%` matches neither. So a failed lookup drops the last hyphen segment and
    /// tries again, which turns that case into `%loi-2004-11-12%` and finds the sibling.
    ///
    /// Bounded on purpose: it stops at three attempts and at five characters, because a prefix
    /// short enough to match anything is not a candidate, it is noise wearing a candidate's shape.
    /// </summary>
    private static List<DocRow> NearestHeld(LexIndexReader reader, string workSlug)
    {
        var filters = new FilterSet(null, null, null, null);
        var probe = workSlug;
        for (var attempt = 0; attempt < 3 && probe.Length >= 5; attempt++)
        {
            var hits = reader.SearchWorksByIdentifierOrTitle(probe, filters, 40)
                .GroupBy(row => row.GroupKey, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(row => row.ValidFrom, StringComparer.Ordinal).First())
                // The requested slug can never be its own candidate: it is the thing not held.
                .Where(row => !string.Equals(row.GroupKey, workSlug, StringComparison.Ordinal))
                .Take(5)
                .ToList();
            if (hits.Count > 0) return hits;

            var cut = probe.LastIndexOf('-');
            if (cut < 0) break;
            probe = probe[..cut];
        }
        return [];
    }

    /// <summary>
    /// A candidate's human label. The publisher title when there is one, otherwise the work slug,
    /// because an untitled record still has a coordinate and a blank link is not a choice.
    /// </summary>
    private static string Describe(DocRow row) =>
        string.IsNullOrWhiteSpace(row.Title) ? row.GroupKey : row.Title!;

    /// <summary>
    /// The temporary_derogation notice for one provision card, or null when the typed evidence
    /// condition fails. Evidence: the page is the governed publisher, work and anchor, and the
    /// derogating act is actually held by the mounted index, so both actions resolve to real
    /// records rather than promises.
    /// </summary>
    public static string? TemporaryDerogation(
        LexIndexReader reader, string publisher, string workSlug, string anchor)
    {
        if (!string.Equals(publisher, DerogationPublisher, StringComparison.Ordinal)
            || !string.Equals(workSlug, DerogationWork, StringComparison.Ordinal)
            || !string.Equals(anchor, DerogationAnchor, StringComparison.Ordinal)
            || !reader.WorkExists(DerogationAct))
            return null;

        // The publisher-asserted source URI of the act's earliest held state, when one exists,
        // gives the "publisher metadata" action an evidence-backed target instead of a guessed
        // ELI. No held source URI, no second action.
        var sourceUri = reader.Timeline(DerogationAct)
            .OrderBy(document => document.ValidFrom, StringComparer.Ordinal)
            .Select(document => document.SourceUri)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)
                && value.StartsWith("https://", StringComparison.Ordinal));
        var actions = $"<a href=\"/{H(DerogationPublisher)}/{H(DerogationAct)}\" style=\"display:inline-block\">Open the derogating act</a>";
        if (sourceUri is not null)
            actions += $" &nbsp;&nbsp;<a href=\"{H(sourceUri)}\" rel=\"noopener\" style=\"display:inline-block\">Open publisher metadata</a>";
        // The body is one deliberate string: frozen copy must never depend on source-code line
        // wrapping, because tests and reviews assert it verbatim.
        var body = "A separate act temporarily derogated from article L. 121-6. "
            + $"The act is dated {H(DerogationActDateText)}. Lex does not yet hold the "
            + "publisher's act-level entry-into-force and no-longer-in-force dates for it.";
        return $"""
            <div class="notice" role="note" aria-label="Temporary derogation recorded">
            <b>Temporary derogation recorded.</b>
            {body}
            <span class="sub">{actions}</span>
            </div>
            """;
    }

    /// <summary>
    /// A publisher-asserted application date for an act, once one is actually indexed. Decision
    /// 41 lets the pre_application_state notice appear only when an indexed application-date
    /// fact supports it. The index holds no such fact today (verified 2026-08-28: the EU
    /// publisher metadata kinds are EuroVoc and directory classifications plus short titles),
    /// so this source answers null until EU typed dates (E1) land and replace this body with a
    /// real index read. The seam exists so the renderer, its condition and its tests are
    /// already load-bearing on that day.
    /// </summary>
    public static PreApplicationFact? FindPreApplicationFact(LexIndexReader reader, DocRow doc)
        => null;

    /// <summary>
    /// The pre_application_state notice, or null without evidence. The condition is strict:
    /// there must be an indexed publisher application date, and the consolidated state must be
    /// dated before it. Copy frozen by Decision 41.
    /// </summary>
    public static string? PreApplicationState(DocRow doc, PreApplicationFact? fact)
    {
        // Fail closed on every member (Codex review O1): the notice renders only from a fully
        // valid evidence contract. Decision 41 requires both actions, so a partially valid fact
        // suppresses the notice entirely rather than rendering a reduced one. HTML escaping is
        // not URL validation: a javascript: or protocol-relative href survives escaping, so the
        // schemes are constrained here, before any markup exists.
        if (fact is null
            || !TryIsoDate(fact.ApplicationDate, out var application)
            || !TryIsoDate(doc.ValidFrom, out var stateDate)
            || stateDate >= application
            || !IsInternalRoute(fact.TypedDatesHref)
            || !IsOfficialHttpsUri(fact.OfficialJournalHref))
            return null;
        return $"""
            <div class="notice" role="note" aria-label="Pre-application state">
            <b>Pre-application state.</b>
            This consolidated state is dated before the publisher's application date. Do not read
            the state date or an "in force" label as a claim that the act applied on that date.
            Entry into force and application are separate publisher dates. Consolidated texts are
            documentation without legal effect.
            <span class="sub"><a href="{H(fact.TypedDatesHref)}" style="display:inline-block">View typed dates</a>
            &nbsp;&nbsp;<a href="{H(fact.OfficialJournalHref)}" rel="noopener" style="display:inline-block">Open the Official Journal source</a></span>
            </div>
            """;
    }

    /// <summary>One canonical ISO date, nothing else.</summary>
    private static bool TryIsoDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

    /// <summary>
    /// The official publisher hosts this product cites, exactly. A notice evidence link that
    /// is HTTPS but points anywhere else is not publisher evidence (review round 2, O1).
    /// </summary>
    private static readonly string[] OfficialPublisherHosts =
    [
        "eur-lex.europa.eu",
        "publications.europa.eu",
        "legilux.public.lu",
        "data.legilux.public.lu",
    ];

    /// <summary>
    /// A same-origin route, proven by resolution rather than string shape: resolved against a
    /// fixed base origin, the result must stay on that origin. This closes the backslash
    /// authority escape ("/\evil.example/x" resolves off-origin in browsers) and rejects
    /// control characters outright.
    /// </summary>
    private static bool IsInternalRoute(string? value)
    {
        if (value is null
            || !value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.Any(char.IsControl))
            return false;
        var baseOrigin = new Uri("https://lex.invalid/");
        return Uri.TryCreate(baseOrigin, value, out var resolved)
            && resolved.Scheme == Uri.UriSchemeHttps
            && string.Equals(resolved.Host, baseOrigin.Host, StringComparison.OrdinalIgnoreCase)
            && resolved.IsDefaultPort
            && string.IsNullOrEmpty(resolved.UserInfo);
    }

    /// <summary>An absolute HTTPS URI on an exact official publisher host, nothing else.</summary>
    private static bool IsOfficialHttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && OfficialPublisherHosts.Any(host =>
            string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// An indexed publisher application-date fact: the date itself plus the two evidence links the
/// notice's actions require. Constructed only from indexed publisher data, never inferred.
/// </summary>
public sealed record PreApplicationFact(
    string ApplicationDate,
    string TypedDatesHref,
    string OfficialJournalHref);
