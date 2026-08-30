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

        // "the official publisher" must point at the publisher or at nothing. The first draft
        // linked it to /search, which is Lex's own search: a link that promises the reader is
        // leaving for the source and does not. The origin is derived from a held record's
        // publisher-asserted source URI, the same evidence rule the derogation notice follows, and
        // with no held URI the phrase stays plain text rather than becoming a false destination.
        var origin = candidates.Select(row => row.SourceUri).Select(OriginOf)
            .FirstOrDefault(value => value is not null);
        var publisherAction = origin is null
            ? "Search the official publisher"
            : $"<a href=\"{H(origin)}\" rel=\"noopener\">Search the official publisher</a>";

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
            <span class="sub">{publisherAction}</span>
            </div>
            {offered}
            """;
    }

    /// <summary>The 2017 boundary below which Legilux consolidation coverage thins.</summary>
    internal const string LuDensityBoundary = "2017-01-01";

    /// <summary>
    /// The historical_density notice, or null when the window does not reach before 2017 with
    /// Luxembourg in scope. Copy frozen by Decision 41.
    ///
    /// This is the fifth Phase 0 notice and it existed only in the browser bundle. The
    /// server-rendered change report is the one surface in this lane that counts changes, so the
    /// same reader was given the caveat in the workspace and not on the page that states the
    /// number. The predicate mirrors the browser's exactly: the window must start before the
    /// boundary AND Luxembourg must be in scope, both from server-provided facts.
    ///
    /// The publisher href is a literal here on purpose. Decision 41 ships it as part of this
    /// notice, so using it follows frozen copy rather than guessing a home page, which is why
    /// unknown_work derives its origin from held evidence and this one does not.
    /// </summary>
    public static string? HistoricalDensity(
        string fromDate, IEnumerable<LexIndexReader> selectedReaders)
    {
        if (string.CompareOrdinal(fromDate, LuDensityBoundary) >= 0) return null;
        // Only readers that actually ran. An unrecognised publisher value selects none, and a
        // caveat about a set that never ran is an observation nobody made.
        if (!selectedReaders.Any(IsLuxembourgReader)) return null;

        var body = "For Luxembourg periods before 2017, Lex holds fewer dated consolidation "
            + "states. This result counts changes observed in held states, not every legal "
            + "change. A lower count may reflect coverage.";

        return $"""
            <div class="notice" role="note" aria-label="Historical coverage is less dense">
            <b>Historical coverage is less dense.</b>
            {body}
            <span class="sub"><a href="/coverage">View coverage for this period</a>
            &nbsp;&nbsp;<a href="https://legilux.public.lu" rel="noopener">Open the official publisher</a></span>
            </div>
            """;
    }

    /// <summary>
    /// Whether a mounted reader is the Luxembourg publisher, by its own stamp or collection.
    /// </summary>
    public static bool IsLuxembourgReader(LexIndexReader reader) =>
        IsLuxembourg(reader.Stamp.GetValueOrDefault("jurisdiction"))
        || IsLuxembourg(reader.Collection);

    private static bool IsLuxembourg(string? value) =>
        value is not null
        && (value.Equals("lu", StringComparison.OrdinalIgnoreCase)
            || value.Equals("lu-legilux", StringComparison.OrdinalIgnoreCase)
            || value.Equals("luxembourg", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The metadata_only no-hit card, or null when no metadata-only match was actually suppressed.
    /// Copy frozen by Decision 41.
    ///
    /// The evidence condition is the suppression itself: the notice says Lex found records that
    /// match only in metadata, so it may only appear when it did. A search that genuinely returned
    /// nothing is a different state and gets the Phase 2 typed no-hit card, not this one.
    ///
    /// Scope note. The verdict's Phase 0 wording is "metadata-only match suppression when the query
    /// names an absent instrument", which covers only half the failure that was actually proven.
    /// Attack 41 ran two probes: one naming an instrument, and one purely lay-language query that
    /// returned tachograph and toll regulations for a speeding question. Both returned status ok
    /// with metadata matches presented as answers. Detecting whether a query names an instrument is
    /// also more code than not needing to know. So suppression here is general, and the widening is
    /// deliberate and declared rather than silently inherited from the narrower sentence.
    ///
    /// No population figure appears in this copy. The count-at-build rule forbids literals, and the
    /// gap matrix proves the specification's own "~24,579 never consolidated" is wrong: the true
    /// set is 23,370 of a 24,622 population. Coverage computes both at build, so the card links
    /// there instead of restating a number that would rot.
    /// </summary>
    public static string? MetadataOnly(LexIndexReader reader, IReadOnlyList<string> suppressedWorks)
    {
        if (suppressedWorks.Count == 0) return null;

        var body = "Lex found records that match only in metadata. They are not shown as text "
            + "answers. This is not evidence that the named instrument or law does not exist. "
            + "Check the name or identifier, review coverage and known gaps, or search the "
            + "official publisher.";

        var origin = suppressedWorks
            .Select(work => reader.Timeline(work)
                .OrderBy(document => document.ValidFrom, StringComparer.Ordinal)
                .Select(document => document.SourceUri)
                .FirstOrDefault(value => OriginOf(value) is not null))
            .Select(OriginOf)
            .FirstOrDefault(value => value is not null);
        var publisherAction = origin is null
            ? "Search the official publisher"
            : $"<a href=\"{H(origin)}\" rel=\"noopener\">Search the official publisher</a>";

        return $"""
            <div class="notice" role="note" aria-label="No held text match">
            <b>No held text match.</b>
            {body}
            <span class="sub"><a href="/coverage">View coverage and known gaps</a>
            &nbsp;&nbsp;{publisherAction}</span>
            </div>
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
    /// The https origin of a publisher-asserted source URI, or null. Never guessed: a publisher
    /// home page this product invented is exactly the kind of unsupported claim the notice
    /// contract exists to prevent.
    /// </summary>
    private static string? OriginOf(string? sourceUri) =>
        Uri.TryCreate(sourceUri, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            ? $"{uri.Scheme}://{uri.Host}"
            : null;

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
