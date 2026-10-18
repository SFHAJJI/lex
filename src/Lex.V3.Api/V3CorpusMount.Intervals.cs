using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// The reader's interval rules over lex-v3-luxembourg-index/9 (data design §3.2, slice S7a): which state of a work covers a
/// date, from every catalogue state of the work (held or textless) with its own end, the end derived from the next state, and
/// the boundary kinds the index materialises. An index of schema 8 or 6 holds none of this and keeps the rule it had
/// (<see cref="SelectAtDate"/>), byte for byte.
/// <para>
/// A state covers a date D when its interval <c>[from, to)</c> holds D: <c>from</c> its date, <c>to</c> the end the publisher
/// states, else the next dated state's start (derived), else none; a state whose end is on or before its start covers no date.
/// A translation (a language other than French) takes the interval of the French state it translates, as the index records it.
/// </para>
/// </summary>
internal sealed partial class V3CorpusMount
{
    /// <summary>One work's catalogue as index 9 holds it: every state row in key order, its transitions, and the rows by digest.</summary>
    private sealed record CatalogueWork(
        IReadOnlyList<LuxembourgIndexCatalogueState> Rows,
        IReadOnlyList<LuxembourgIndexInterval> Intervals,
        IReadOnlyDictionary<string, LuxembourgIndexCatalogueState> ByDigest)
    {
        public static CatalogueWork Of(LuxembourgIndexReader reader, string workKey)
        {
            var rows = reader.ResolveCatalogueStates(workKey);
            return new CatalogueWork(
                rows,
                reader.ResolveIntervals(workKey),
                rows.ToDictionary(static row => row.StateSha256, StringComparer.Ordinal));
        }
    }

    // The mounted index is immutable, so each work's catalogue is read once (a primary-key range of two tables).
    private readonly ConcurrentDictionary<string, CatalogueWork> _catalogueWorks = new(StringComparer.Ordinal);

    /// <summary>Whether the mounted Luxembourg index is index 9, whose interval rules select a date's state.</summary>
    private bool RulesByInterval => _reader?.RecordsCatalogueStates == true;

    private CatalogueWork CatalogueOf(string workKey) =>
        _catalogueWorks.GetOrAdd(workKey, key => CatalogueWork.Of(_reader!, key));

    internal const string FrenchLanguage = "fra";

    private const string TextHeld = "held";

    // What covered a date in one language, by the interval rules: one held state (or several held with one wording, one of
    // them designated), one state whose text is not held, several that are not one wording, or none (Cover.Reason says why).
    private const string CoverServed = "served";
    private const string CoverTextless = "textless";
    private const string CoverAmbiguous = "ambiguous";
    private const string CoverNone = "none";

    /// <summary>
    /// The interval rules' answer for one language on one date: the covering row (served or textless) with the same-text
    /// states served under it, the candidates of an ambiguity, or why nothing covers the date, with the states that reason
    /// names, the work's first date, the nearest dates around the date and a catalogue hole's span.
    /// </summary>
    private sealed record Cover(
        string Kind,
        LuxembourgIndexCatalogueState? Row,
        IReadOnlyList<LuxembourgIndexCatalogueState> Others,
        string? Reason,
        string? HistoryBegins,
        string? NearestEarlier,
        string? NearestLater,
        (string From, string To)? Hole);

    internal const string ReasonBeforeHistory = "before_history";
    internal const string ReasonCatalogueHole = "catalogue_hole";
    internal const string ReasonAfterClosedEnd = "after_closed_end";
    internal const string ReasonEndConventionUnclear = "publisher_end_convention_unclear";
    internal const string ReasonIntervalInvalid = "publisher_interval_invalid";
    internal const string ReasonNoExpressionInLanguage = "no_expression_in_language";

    /// <summary>
    /// The rows of one language's chain: for French, the French rows and the rows that name no expression (the catalogue's
    /// states with no expression stand in the French chain); for another language, its own rows, each dated and bounded as
    /// the French state it translates is.
    /// </summary>
    private static LuxembourgIndexCatalogueState[] ChainOf(CatalogueWork work, string language) =>
        work.Rows.Where(row => string.Equals(language, FrenchLanguage, StringComparison.Ordinal)
                ? row.Language.Length == 0 || string.Equals(row.Language, FrenchLanguage, StringComparison.Ordinal)
                : string.Equals(row.Language, language, StringComparison.Ordinal))
            .OrderBy(static row => row.ApplicabilityDate, StringComparer.Ordinal)
            .ThenBy(static row => KindRank(row.StateKind))
            .ThenBy(static row => row.PublisherLegalResourceIri, StringComparer.Ordinal)
            .ThenBy(static row => row.ExpressionIri, StringComparer.Ordinal)
            .ToArray();

    private static int KindRank(string? kind) => kind switch
    {
        "as_published" => 0,
        "consolidation" => 1,
        _ => 2,
    };

    /// <summary>Whether a row's interval holds the date: <c>from ≤ date &lt; to</c>, and never for an invalid interval.</summary>
    private static bool Covers(LuxembourgIndexCatalogueState row, string date) =>
        !string.Equals(row.Boundary, "invalid", StringComparison.Ordinal) &&
        string.CompareOrdinal(row.ApplicabilityDate, date) <= 0 &&
        (row.ApplicableTo is null || string.CompareOrdinal(date, row.ApplicableTo) < 0);

    /// <summary>
    /// The interval rules for one work, one language and one date (design §3.2's table): no covering state is a refusal with
    /// its reason (before the history, a catalogue hole, after a closed end, the end day of a suspected inclusive end, where
    /// an invalid interval would apply); one covering state is served, or refused for its text when it holds none; several
    /// are served once when every one is held with the same wording (the as-published act designated, otherwise the one that
    /// begins last) and are otherwise an ambiguity naming each.
    /// </summary>
    private Cover CoverAt(CatalogueWork work, string language, string date, Func<LuxembourgIndexCatalogueState, bool>? comparable = null)
    {
        var chain = ChainOf(work, language);
        var covering = chain.Where(row => Covers(row, date)).ToArray();
        if (covering.Length == 1)
        {
            return new Cover(
                string.Equals(covering[0].TextStatus, TextHeld, StringComparison.Ordinal) ? CoverServed : CoverTextless,
                covering[0], [], null, null, null, null, null);
        }

        if (covering.Length > 1)
        {
            return (comparable is null || covering.All(comparable)) && SameWording(covering) is { } designated
                ? new Cover(CoverServed, designated, covering.Where(row => !ReferenceEquals(row, designated)).ToArray(), null, null, null, null, null)
                : new Cover(CoverAmbiguous, null, covering, null, null, null, null, null);
        }

        var historyBegins = chain.Length == 0 ? null : chain[0].ApplicabilityDate;
        var nearestLater = chain.Select(static row => row.ApplicabilityDate)
            .FirstOrDefault(from => string.CompareOrdinal(from, date) > 0);
        // The latest state date on or before the date, an invalid interval's included: a state is held there, which its
        // permalink opens, and a refusal names a date on each side whenever a history holds one.
        var nearestEarlier = chain.Where(row => string.CompareOrdinal(row.ApplicabilityDate, date) <= 0)
            .Select(static row => row.ApplicabilityDate)
            .LastOrDefault();

        // Another language takes its intervals from the French chain: when a French state covers the date and none of this
        // language does, the state covering it has no expression held in this language, said so, naming it; when no French
        // state covers it either, the French chain's reason is the reason.
        if (!string.Equals(language, FrenchLanguage, StringComparison.Ordinal) &&
            CoverAt(work, FrenchLanguage, date) is { } french && (french.Kind != CoverNone || french.HistoryBegins is not null))
        {
            return french.Kind == CoverNone
                ? french with { HistoryBegins = historyBegins, NearestEarlier = nearestEarlier, NearestLater = nearestLater }
                : new Cover(CoverNone, null, french.Row is { } row ? [row, .. french.Others] : french.Others,
                    ReasonNoExpressionInLanguage, historyBegins, nearestEarlier, nearestLater, null);
        }

        if (historyBegins is null || string.CompareOrdinal(date, historyBegins) < 0)
        {
            return new Cover(CoverNone, null, [], ReasonBeforeHistory, historyBegins, null, historyBegins, null);
        }

        // The end day of a suspected inclusive end: the earlier state ends the day before the next begins, so this day is the
        // publisher's convention's, unclear, and is given to neither (it is never called a hole).
        var unclear = chain.FirstOrDefault(row =>
            string.Equals(row.Boundary, "inclusive_suspected", StringComparison.Ordinal) &&
            string.Equals(row.ApplicableTo, date, StringComparison.Ordinal));
        if (unclear is not null)
        {
            var next = chain.Where(row => string.CompareOrdinal(row.ApplicabilityDate, date) > 0).ToArray();
            var nextFrom = next.FirstOrDefault()?.ApplicabilityDate;
            return new Cover(CoverNone, unclear,
                next.Where(row => string.Equals(row.ApplicabilityDate, nextFrom, StringComparison.Ordinal)).ToArray(),
                ReasonEndConventionUnclear, historyBegins, nearestEarlier, nearestLater, null);
        }

        // Where a state whose end is on or before its start would apply: from its start to the next later start.
        var invalid = chain.LastOrDefault(row =>
            string.Equals(row.Boundary, "invalid", StringComparison.Ordinal) &&
            string.CompareOrdinal(row.ApplicabilityDate, date) <= 0 &&
            chain.Where(later => string.CompareOrdinal(later.ApplicabilityDate, row.ApplicabilityDate) > 0)
                .Select(static later => later.ApplicabilityDate)
                .FirstOrDefault() is var following &&
            (following is null || string.CompareOrdinal(date, following) < 0));
        if (invalid is not null)
        {
            return new Cover(CoverNone, invalid, [], ReasonIntervalInvalid, historyBegins, nearestEarlier, nearestLater, null);
        }

        // Otherwise the state before the date has ended: a hole in the catalogue when a later state begins, else a closed end.
        var previous = chain.LastOrDefault(row =>
            !string.Equals(row.Boundary, "invalid", StringComparison.Ordinal) &&
            string.CompareOrdinal(row.ApplicabilityDate, date) <= 0);
        if (nearestLater is not null)
        {
            return new Cover(CoverNone, previous, [], ReasonCatalogueHole, historyBegins, nearestEarlier, nearestLater,
                (previous?.ApplicableTo ?? date, nearestLater));
        }

        return new Cover(CoverNone, previous, [], ReasonAfterClosedEnd, historyBegins, nearestEarlier, null, null);
    }

    /// <summary>
    /// Several states covering one date are one wording when every one is held, under the same rule profiles, with the same
    /// articles by publisher id and wording digest. Then one is served: the act as published when exactly one is, otherwise
    /// the one that begins last, which the earlier rule (the greatest date on or before) would have served; a tie is none.
    /// </summary>
    private LuxembourgIndexCatalogueState? SameWording(IReadOnlyList<LuxembourgIndexCatalogueState> covering)
    {
        if (covering.Any(row => !string.Equals(row.TextStatus, TextHeld, StringComparison.Ordinal)))
        {
            return null;
        }

        string Wording(LuxembourgIndexCatalogueState row) => string.Join('\n', _reader!.ResolveStateArticles(row.StateSha256)
            .Select(static article => article.PublisherId + "\t" + article.WordingSha256)
            .Order(StringComparer.Ordinal));
        var profiles = covering.Select(row => string.Join(',', StateOfRow(row).RuleProfileSha256s)).Distinct(StringComparer.Ordinal).Count();
        if (profiles != 1 || covering.Select(Wording).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            return null;
        }

        var published = covering.Where(static row => string.Equals(row.StateKind, "as_published", StringComparison.Ordinal)).ToArray();
        if (published.Length == 1)
        {
            return published[0];
        }

        var latest = covering.Max(static row => row.ApplicabilityDate)!;
        var last = covering.Where(row => string.Equals(row.ApplicabilityDate, latest, StringComparison.Ordinal)).ToArray();
        return last.Length == 1 ? last[0] : null;
    }

    /// <summary>A row as the resolved-state vocabulary the operations serve (a textless row has no profile and no article).</summary>
    private LuxembourgIndexResolvedState StateOfRow(LuxembourgIndexCatalogueState row) =>
        _reader!.ResolveWorkStates(row.WorkKey).FirstOrDefault(state => string.Equals(state.StateSha256, row.StateSha256, StringComparison.Ordinal))
        ?? new LuxembourgIndexResolvedState(
            row.WorkKey, row.ApplicabilityDate, row.StateSha256, row.ExpressionIri, row.PublisherWorkIri,
            row.PublisherLegalResourceIri, row.Language, Array.Empty<string>(), row.ArticleIdentities);

    /// <summary>
    /// The selection every dated operation makes over one language's held states: on index 9 by the interval rules (the one
    /// state served, or the candidates of an ambiguity, or none when no held state covers the date: a refusal the caller
    /// asks <see cref="RefuseNoVersionForDate(V3PlatformOperationRequest, DateTimeOffset, IReadOnlyList{LuxembourgIndexResolvedState}, string, string?)"/>
    /// for, which says why); on index 8 or 6 by <see cref="SelectAtDate"/>, as before.
    /// </summary>
    private (LuxembourgIndexResolvedState[] Selected, string? NextDate) SelectAt(
        IReadOnlyList<LuxembourgIndexResolvedState> ofLanguage, string requestedDate)
    {
        if (!RulesByInterval || ofLanguage.Count == 0)
        {
            return SelectAtDate(ofLanguage, requestedDate);
        }

        var cover = CoverAt(CatalogueOf(ofLanguage[0].WorkKey), ofLanguage[0].Language, requestedDate);
        switch (cover.Kind)
        {
            case CoverServed:
                // The next held state's date after the date asked: a held state that begins between the served one and the date
                // and does not cover it (an invalid interval inside an overlap) is not "next" of a date already past it.
                var served = ofLanguage.Single(state => string.Equals(state.StateSha256, cover.Row!.StateSha256, StringComparison.Ordinal));
                var after = string.CompareOrdinal(requestedDate, served.ApplicabilityDate) > 0 ? requestedDate : served.ApplicabilityDate;
                return ([served], ofLanguage
                    .Where(other => string.Equals(other.Language, served.Language, StringComparison.Ordinal) &&
                                    string.CompareOrdinal(other.ApplicabilityDate, after) > 0)
                    .Select(static other => other.ApplicabilityDate)
                    .Order(StringComparer.Ordinal)
                    .FirstOrDefault());
            case CoverAmbiguous:
                return (cover.Others.Select(StateOfRow).ToArray(), null);
            default:
                return ([], null);
        }
    }

    /// <summary>The same-text states served under the designated one (basis <c>identical_text</c>), or none.</summary>
    private IReadOnlyList<LuxembourgIndexCatalogueState> SameTextOf(LuxembourgIndexResolvedState state, string requestedDate)
    {
        if (!RulesByInterval)
        {
            return [];
        }

        var cover = CoverAt(CatalogueOf(state.WorkKey), state.Language, requestedDate);
        return cover.Kind == CoverServed && string.Equals(cover.Row!.StateSha256, state.StateSha256, StringComparison.Ordinal)
            ? cover.Others
            : [];
    }

    /// <summary>
    /// A snapshot's catalogue of one work for <c>as_observed</c>: every state row the log held at that observation, each with
    /// its own facts from the index that holds it (the mounted one, else the newest retained generation of index 9), else from
    /// the log (its text status and the end the publisher states), and the chain's derived ends and boundaries computed again
    /// over the snapshot's own rows (<see cref="Rechain"/>), since a state the snapshot did not hold bounds nothing in it.
    /// </summary>
    private CatalogueWork ObservedCatalogue(string workKey, IReadOnlyList<LuxembourgIndexObservedState> observed)
    {
        var mounted = _reader!.ResolveWorkStates(workKey).Count > 0 ? CatalogueOf(workKey) : null;
        var generations = _generationReaders.Where(static generation => generation.Reader.RecordsCatalogueStates)
            .Select(generation => generation.Reader.ResolveCatalogueStates(workKey)
                .ToDictionary(static row => row.StateSha256, StringComparer.Ordinal))
            .ToArray();
        var rows = observed.Select(state =>
                mounted?.ByDigest.GetValueOrDefault(state.StateSha256)
                ?? generations.Select(held => held.GetValueOrDefault(state.StateSha256)).FirstOrDefault(static row => row is not null)
                ?? LoggedRow(state))
            .ToArray();
        var chained = Rechain(rows);
        return new CatalogueWork(chained, [], chained.ToDictionary(static row => row.StateSha256, StringComparer.Ordinal));
    }

    /// <summary>A state only the log names: its key and digest, its text status and the end the publisher states, as logged.</summary>
    private static LuxembourgIndexCatalogueState LoggedRow(LuxembourgIndexObservedState state)
    {
        var legalResource = state.ExpressionIri.Length == 0 ? "" : state.ExpressionIri[..state.ExpressionIri.LastIndexOf('/')];
        var kind = legalResource.EndsWith("/jo", StringComparison.Ordinal) ? "as_published"
            : legalResource.Contains("/consolide/", StringComparison.Ordinal) ? "consolidation"
            : "code";
        return new LuxembourgIndexCatalogueState(
            state.WorkKey, state.ApplicabilityDate, state.StateSha256, state.ExpressionIri, "", legalResource, state.Language,
            Array.Empty<string>(), kind, null, null, state.PublisherEnd, state.PublisherEnd is null ? null : "publisher", "derived", null,
            state.TextStatus ?? TextHeld, null, null, null, null, false);
    }

    /// <summary>
    /// The chain fields of one work's rows computed from their own facts, as the builder computes them (index 9's
    /// <c>CompleteIntervals</c>): over the French chain (French rows and rows naming no expression) in date order, an end is the
    /// publisher's when stated, else the next later date's start (derived); its boundary is invalid for an end on or before its
    /// start, inclusive_suspected for an end the day before the next start, half_open for any other stated end, derived without
    /// one; a translation takes its French state's end and boundary when that state is among the rows, else its own.
    /// </summary>
    internal static LuxembourgIndexCatalogueState[] Rechain(IReadOnlyList<LuxembourgIndexCatalogueState> rows)
    {
        static string? PublisherEnd(LuxembourgIndexCatalogueState row) =>
            string.Equals(row.ApplicableToSource, "publisher", StringComparison.Ordinal) ? row.ApplicableTo : null;
        static bool IsFrenchChain(LuxembourgIndexCatalogueState row) =>
            row.Language.Length == 0 || string.Equals(row.Language, FrenchLanguage, StringComparison.Ordinal);
        static string DayBefore(string date) =>
            DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddDays(-1)
                .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var chain = rows.Where(IsFrenchChain).ToArray();
        var dates = chain.Select(static row => row.ApplicabilityDate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var completed = new Dictionary<string, LuxembourgIndexCatalogueState>(StringComparer.Ordinal);
        foreach (var row in chain)
        {
            var next = dates.FirstOrDefault(date => string.CompareOrdinal(date, row.ApplicabilityDate) > 0);
            var end = PublisherEnd(row);
            var boundary = end is null ? "derived"
                : string.CompareOrdinal(end, row.ApplicabilityDate) <= 0 ? "invalid"
                : next is not null && string.Equals(end, DayBefore(next), StringComparison.Ordinal) ? "inclusive_suspected"
                : "half_open";
            completed[row.StateSha256] = row with
            {
                ApplicableTo = end ?? next,
                ApplicableToSource = end is not null ? "publisher" : next is not null ? "derived" : null,
                Boundary = boundary,
            };
        }

        return rows.Select(row =>
        {
            if (completed.TryGetValue(row.StateSha256, out var chained))
            {
                return chained;
            }

            var end = PublisherEnd(row);
            var french = row.TranslationOf is null
                ? null
                : chain.FirstOrDefault(candidate =>
                    string.Equals(candidate.PublisherLegalResourceIri, row.PublisherLegalResourceIri, StringComparison.Ordinal) &&
                    string.Equals(candidate.ExpressionIri, row.TranslationOf, StringComparison.Ordinal) &&
                    string.Equals(candidate.Language, FrenchLanguage, StringComparison.Ordinal));
            if (french is not null)
            {
                var frenchRow = completed[french.StateSha256];
                return row with { ApplicableTo = frenchRow.ApplicableTo, ApplicableToSource = frenchRow.ApplicableToSource, Boundary = frenchRow.Boundary };
            }

            return row with
            {
                ApplicableTo = end,
                ApplicableToSource = end is null ? null : "publisher",
                Boundary = end is not null && string.CompareOrdinal(end, row.ApplicabilityDate) <= 0 ? "invalid" : end is null ? "derived" : "half_open",
            };
        }).ToArray();
    }

    internal const string CatalogueRule =
        "on lex-v3-luxembourg-index/9 each state carries the publisher catalogue's facts of its row: state_kind (as_published, " +
        "consolidation or code); date_role, the source of applicability_date: applicability (the catalogue's dateApplicability of a " +
        "consolidation or code state), applicability_in_file (the act's file's own dateApplicability) or entry_into_force (the act's " +
        "catalogue dateEntryInForce, used when its file states no date: an entry into force, never an applicability date, and never " +
        "said as one); entry_into_force, an act's catalogue dateEntryInForce whatever its date's role; applicable_to, the end of the " +
        "state's interval [applicability_date, applicable_to), with applicable_to_source publisher (the catalogue's dateEndApplicability) " +
        "or derived (the next dated state's start), null when there is none; boundary, how that end meets the next state (half_open, " +
        "inclusive_suspected: an end one day before the next start, invalid: an end on or before the state's own start, so the state " +
        "covers no date, derived); publication_date, the catalogue's publicationDate; text_status and text_status_reason, why the " +
        "state's text is or is not held; in_file_identity and in_file_date, what the state's own file says; translation_of, for an " +
        "expression in another language than French, the French expression whose interval it takes (the publisher's record does not " +
        "state which language version is authentic); provisional, a date after the day this index was built; a state is selected for a " +
        "date by the interval that holds it, and two states holding it with the same held wording are served once with the others " +
        "named under same_text_states";

    /// <summary>
    /// Adds an index 9 state's catalogue facts to a row the operations serve, from the reader that holds it (the mounted one,
    /// or a retained generation's); a row of an index 8 or 6, or a state the reader holds no catalogue row for, is unchanged.
    /// </summary>
    private object WithCatalogueFacts(object row, LuxembourgIndexResolvedState state, LuxembourgIndexReader? holder = null)
    {
        var reader = holder ?? _reader;
        if (reader?.RecordsCatalogueStates != true)
        {
            return row;
        }

        var facts = ReferenceEquals(reader, _reader)
            ? CatalogueOf(state.WorkKey).ByDigest.GetValueOrDefault(state.StateSha256)
            : reader.ResolveCatalogueStates(state.WorkKey).FirstOrDefault(candidate => string.Equals(candidate.StateSha256, state.StateSha256, StringComparison.Ordinal));
        if (facts is null)
        {
            return row;
        }

        var node = row as JsonObject ?? JsonSerializer.SerializeToNode(row)!.AsObject();
        AddCatalogueFacts(node, facts);
        return node;
    }

    private static void AddCatalogueFacts(JsonObject node, LuxembourgIndexCatalogueState facts)
    {
        node["state_kind"] = facts.StateKind;
        node["date_role"] = facts.DateRole;
        node["entry_into_force"] = facts.EntryIntoForce;
        node["applicable_to"] = facts.ApplicableTo;
        node["applicable_to_source"] = facts.ApplicableToSource;
        node["boundary"] = facts.Boundary;
        node["publication_date"] = facts.PublicationDate;
        node["text_status"] = facts.TextStatus;
        node["text_status_reason"] = facts.TextStatusReason;
        node["in_file_identity"] = facts.InFileIdentity;
        node["in_file_date"] = facts.InFileDate;
        node["translation_of"] = facts.TranslationOf;
        node["provisional"] = facts.Provisional;
    }

    /// <summary>A catalogue row named in a refusal or beside a served state: its identity, dates, end, boundary and text status.</summary>
    private static JsonObject CatalogueRowSummary(LuxembourgIndexCatalogueState row)
    {
        var node = new JsonObject
        {
            ["state_sha256"] = row.StateSha256,
            ["language"] = row.Language.Length == 0 ? null : row.Language,
            ["applicability_date"] = row.ApplicabilityDate,
            ["stable_coordinate"] = $"/lu-legilux/{row.WorkKey}/{row.ApplicabilityDate}",
            ["permalink"] = row.Language.Length == 0 ? null : LuxembourgPermalink(row.WorkKey, row.ApplicabilityDate, row.StateSha256, row.Language),
            ["expression_iri"] = row.ExpressionIri.Length == 0 ? null : row.ExpressionIri,
            ["publisher_legal_resource_iri"] = row.PublisherLegalResourceIri,
        };
        AddCatalogueFacts(node, row);
        return node;
    }

    internal const string SameTextRule =
        "same_text_states names the other states whose interval also holds the requested date and whose held wording is this state's, " +
        "article by article (publisher id and wording digest) under the same rule profiles (basis identical_text): the text is served " +
        "once, as the act as published when one of them is, otherwise as the state that begins last";

    /// <summary>
    /// A served state's same-text states on index 9 (<see cref="SameTextRule"/>), added to its row; on an index of schema 8 or
    /// 6 the row is unchanged.
    /// </summary>
    private object WithSameText(object row, LuxembourgIndexResolvedState state, string requestedDate)
    {
        if (!RulesByInterval)
        {
            return row;
        }

        var node = row as JsonObject ?? JsonSerializer.SerializeToNode(row)!.AsObject();
        node["same_text_states"] = new JsonArray(SameTextOf(state, requestedDate).Select(static other =>
        {
            var summary = CatalogueRowSummary(other);
            summary["basis"] = "identical_text";
            return (JsonNode)summary;
        }).ToArray());
        return node;
    }

    internal const string StatesWithoutTextRule =
        "states_without_text names the French state whose interval holds the requested date when its text is not held and no French " +
        "text is served beside the other languages' (a state of another language takes its interval from it); it is never served in " +
        "another state's place";

    /// <summary>
    /// An index 9 answer to one date: the catalogue rule, and the French state covering the date when it holds no text and no
    /// French state is served (a held translation is then served alone, and the French state it takes its interval from is
    /// named). An answer of an index of schema 8 or 6 is unchanged.
    /// </summary>
    private object WithIntervalFacts(object result, string workKey, IEnumerable<string> servedLanguages, string? requestedLanguage, string requestedDate)
    {
        if (!RulesByInterval)
        {
            return result;
        }

        var node = JsonSerializer.SerializeToNode(result)!.AsObject();
        node["catalogue_rule"] = CatalogueRule;
        var withoutText = new JsonArray();
        if (requestedLanguage is null && !servedLanguages.Contains(FrenchLanguage, StringComparer.Ordinal) &&
            CoverAt(CatalogueOf(workKey), FrenchLanguage, requestedDate) is { Kind: CoverTextless, Row: { } french })
        {
            withoutText.Add(CatalogueRowSummary(french));
        }

        node["states_without_text"] = withoutText;
        node["states_without_text_rule"] = StatesWithoutTextRule;
        return node;
    }

    internal const string MissingArticlesRule =
        "on lex-v3-luxembourg-index/9 each article carries document_position, its place among its publisher document's main-body " +
        "articles (from 1), and the articles are in that order; missing_articles names each run of places this state holds no " +
        "article at (from_position to to_position, between the articles after_publisher_id and before_publisher_id, null at the " +
        "start): an article of the publisher's document this state does not hold, never skipped in silence; an article missing " +
        "after the last one held is not seen by place, and articles_not_admitted counts every article of the document not held";

    /// <summary>An article row with its place in its publisher document, on index 9; unchanged on an index of schema 8 or 6.</summary>
    private static object WithDocumentPosition(object row, LuxembourgIndexStateArticle article)
    {
        if (article.DocumentPosition is not { } position)
        {
            return row;
        }

        var node = row as JsonObject ?? JsonSerializer.SerializeToNode(row)!.AsObject();
        node["document_position"] = position;
        return node;
    }

    /// <summary>
    /// A state row with the runs of its publisher document's places it holds no article at (<see cref="MissingArticlesRule"/>),
    /// from its articles in document order, on index 9; unchanged on an index of schema 8 or 6.
    /// </summary>
    private static object WithDocumentGaps(object row, IReadOnlyList<LuxembourgIndexStateArticle> inDocumentOrder)
    {
        if (inDocumentOrder.Count == 0 || inDocumentOrder.Any(static article => article.DocumentPosition is null))
        {
            return row;
        }

        var gaps = new JsonArray();
        var previous = (LuxembourgIndexStateArticle?)null;
        foreach (var article in inDocumentOrder)
        {
            var from = (previous?.DocumentPosition ?? 0) + 1;
            if (article.DocumentPosition!.Value > from)
            {
                gaps.Add(new JsonObject
                {
                    ["from_position"] = from,
                    ["to_position"] = article.DocumentPosition.Value - 1,
                    ["count"] = article.DocumentPosition.Value - from,
                    ["after_publisher_id"] = previous?.PublisherId,
                    ["before_publisher_id"] = article.PublisherId,
                });
            }

            previous = article;
        }

        var node = row as JsonObject ?? JsonSerializer.SerializeToNode(row)!.AsObject();
        node["missing_articles"] = gaps;
        node["missing_articles_rule"] = MissingArticlesRule;
        return node;
    }

    internal const string SearchFoldRule =
        "on lex-v3-luxembourg-index/9 a search with no date serves one hit per work, publisher article id and wording (the wording " +
        "digest of the article's text and reference tokens): the hit of the latest state that holds that wording, each other state " +
        "that holds it named under the hit's same_wording_states, so an article that did not change is one hit and not one per " +
        "version; strict_hits and relaxed_hits count the hits served, and fold counts the hits before they were folded; with a date, " +
        "one state per work is searched and nothing is folded";

    /// <summary>
    /// The undated search's fold on index 9 (design §3.2, decision 8; <see cref="SearchFoldRule"/>): one lane's hits grouped by
    /// work, publisher article id and wording digest; each group kept as the hit of its latest state (then the greatest digest,
    /// so the choice is total), the others recorded by that hit's article identity; the kept hits in the lane's order again.
    /// </summary>
    private static LuxembourgIndexSearchHit[] FoldByWording(
        IReadOnlyList<LuxembourgIndexSearchHit> hits,
        IReadOnlyDictionary<string, string> wordings,
        Dictionary<string, LuxembourgIndexSearchHit[]> others)
    {
        var kept = new List<LuxembourgIndexSearchHit>();
        foreach (var group in hits.GroupBy(hit => (hit.WorkKey, hit.PublisherId,
                     wordings.TryGetValue(hit.ArticleIdentitySha256, out var wording)
                         ? wording
                         : throw new InvalidDataException("A search hit names an article the index does not hold."))))
        {
            var ordered = group.OrderBy(static hit => hit.ApplicabilityDate, StringComparer.Ordinal)
                .ThenBy(static hit => hit.StateSha256, StringComparer.Ordinal)
                .ToArray();
            var latest = ordered[^1];
            kept.Add(latest);
            others[latest.ArticleIdentitySha256] = ordered[..^1];
        }

        return InSearchOrder(kept);
    }

    /// <summary>
    /// A Luxembourg search hit on index 9: its state's date role (R21: an act dated by its entry into force is not "applicable
    /// from" it), and without a date the other states holding its wording (<see cref="SearchFoldRule"/>); unchanged on an
    /// index of schema 8 or 6.
    /// </summary>
    private object WithSearchHitFacts(object hit, LuxembourgIndexResolvedState state, IReadOnlyList<LuxembourgIndexSearchHit>? sameWording)
    {
        if (!RulesByInterval)
        {
            return hit;
        }

        var node = JsonSerializer.SerializeToNode(hit)!.AsObject();
        var facts = CatalogueOf(state.WorkKey).ByDigest.GetValueOrDefault(state.StateSha256);
        node["date_role"] = facts?.DateRole;
        node["entry_into_force"] = facts?.EntryIntoForce;
        if (sameWording is not null)
        {
            node["same_wording_states"] = new JsonArray(sameWording.Select(other =>
            {
                var row = CatalogueOf(other.WorkKey).ByDigest.GetValueOrDefault(other.StateSha256);
                return (JsonNode)new JsonObject
                {
                    ["applicability_date"] = other.ApplicabilityDate,
                    ["date_role"] = row?.DateRole,
                    ["state_sha256"] = other.StateSha256,
                    ["article_identity_sha256"] = other.ArticleIdentitySha256,
                    ["permalink"] = row is null ? null : LuxembourgPermalink(row.WorkKey, row.ApplicabilityDate, row.StateSha256, row.Language),
                };
            }).ToArray());
        }

        return node;
    }

    /// <summary>An undated search's answer on index 9 with its fold said (<see cref="SearchFoldRule"/>); otherwise unchanged.</summary>
    private static object WithSearchFold(object answer, bool folds, int? strictBefore, int? relaxedBefore)
    {
        if (!folds)
        {
            return answer;
        }

        var node = JsonSerializer.SerializeToNode(answer)!.AsObject();
        node["fold"] = new JsonObject
        {
            ["rule"] = SearchFoldRule,
            ["strict_hits_before_fold"] = strictBefore,
            ["relaxed_hits_before_fold"] = relaxedBefore,
        };
        return node;
    }

    internal const string NoVersionReasonRule =
        "reason says why no state covers the date in the publisher's catalogue as this index read it: before_history, the date is " +
        "before the work's first state; catalogue_hole, the state before the date ends (the publisher's end) before the next begins, " +
        "and the gap (hole) is the catalogue's, never bridged; after_closed_end, the work's last state ends before the date and no later " +
        "state is in the catalogue: an end of the catalogue's states, which is not an end of the law; publisher_end_convention_unclear, the date is the end " +
        "the publisher states for a state that ends the day before the next begins, so whether that day belongs to it is unclear and it " +
        "is given to neither; publisher_interval_invalid, the date falls where a state whose stated end is on or before its own start " +
        "would apply, so no interval is read for it (its permalink still opens it); no_expression_in_language, a French state covers " +
        "the date and this index holds no expression of it in the language asked (a state in another language takes its interval " +
        "from its French state); named_states (with named_state_sha256s) names the states the reason is about, by permalink, or by " +
        "stable coordinate for a state the catalogue names no expression of; hole_from and hole_to are a hole's span; after a closed " +
        "end, closed_on is the last state's end, and base_act_rule says what the base act's facts beside it are";

    internal const string BaseActRule =
        "base_act_iri is the work's act as published; base_act_in_force_status and base_act_no_longer_in_force are the publisher's own " +
        "assertions about it (inForceStatus, dateNoLongerInForce), verbatim and unread, empty when it asserts none: the end of the " +
        "catalogue's last state says nothing of them, and this answer draws nothing from them";

    internal const string TextlessRule =
        "the state covering the date is in the publisher's catalogue and this index holds none of its text: text_status says why " +
        "(withheld_rights, not_available_pdf, not_available_derivation, not_available_misfiled or not_acquired) and text_status_reason " +
        "in the index's words; no other state is served in its place, and an absence of text here is not an absence of law";

    /// <summary>
    /// The refusal for a date no held state covers, on index 9: <c>no_version_for_date</c> with its reason, the states it
    /// names, the nearest dates and a hole's span; or, when the state covering the date holds no text, <c>text_withheld</c>
    /// or <c>text_not_available</c> for it. The French chain speaks when it is among the languages asked.
    /// </summary>
    private V3PlatformOperationOutcome RefuseUncoveredNine(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        string workKey,
        IReadOnlyList<string> languages,
        string requestedDate,
        string? bound) =>
        RefuseUncoveredNine(request, observedAt, CatalogueOf(workKey), languages, requestedDate, bound);

    /// <summary>The same refusal over a given catalogue of one work (<c>as_observed</c> gives a snapshot's).</summary>
    private V3PlatformOperationOutcome RefuseUncoveredNine(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        CatalogueWork work,
        IReadOnlyList<string> languages,
        string requestedDate,
        string? bound)
    {
        var language = languages.Contains(FrenchLanguage, StringComparer.Ordinal) ? FrenchLanguage : languages.Order(StringComparer.Ordinal).First();
        var cover = CoverAt(work, language, requestedDate);
        if (cover.Kind == CoverTextless)
        {
            return RefuseTextless(request, observedAt, cover.Row!, requestedDate, bound);
        }

        if (cover.Kind != CoverNone)
        {
            throw new InvalidOperationException("A covered date is not refused as uncovered.");
        }

        var payload = new JsonObject { ["requested_date"] = requestedDate };
        if (bound is not null)
        {
            payload["bound"] = bound;
        }

        payload["history_begins"] = cover.HistoryBegins;
        payload["nearest_earlier"] = cover.NearestEarlier;
        payload["nearest_later"] = cover.NearestLater;
        payload["what_would_answer"] = new JsonArray(NoVersionForDateRoutes.Select(static route => (JsonNode)route).ToArray());
        payload["asserts_absence_of_law"] = false;
        payload["language"] = language;
        payload["reason"] = cover.Reason;
        payload["reason_rule"] = NoVersionReasonRule;
        payload["catalogue_read_no_later_than"] = _reader!.ResolveObservations()[^1].BuiltAt;
        // Every member a scalar or a list of scalars, so a refusal card reads it as it reads every other (the web's payload rule).
        var named = new List<LuxembourgIndexCatalogueState>();
        if (cover.Row is not null && cover.Reason is not (ReasonBeforeHistory or ReasonNoExpressionInLanguage))
        {
            named.Add(cover.Row);
        }

        named.AddRange(cover.Others);
        payload["named_states"] = new JsonArray(named.Select(static row => (JsonNode)RowAddress(row)).ToArray());
        payload["named_state_sha256s"] = new JsonArray(named.Select(static row => (JsonNode)row.StateSha256).ToArray());
        payload["hole_from"] = cover.Hole?.From;
        payload["hole_to"] = cover.Hole?.To;
        // After a closed end only, the base act's own facts, verbatim (BaseActRule): the end of the catalogue's last state is not
        // the end of the law, and the catalogue may or may not say anything of the base act.
        if (cover.Reason == ReasonAfterClosedEnd && cover.Row is { } closed)
        {
            var baseAct = closed.PublisherWorkIri + "/jo";
            var facts = _reader.ResolveFacts([baseAct]);
            payload["closed_on"] = closed.ApplicableTo;
            payload["base_act_iri"] = baseAct;
            payload["base_act_in_force_status"] = new JsonArray(FactsOf(facts, Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.InForceStatus, baseAct)
                .Select(static fact => (JsonNode)fact.value).ToArray());
            payload["base_act_no_longer_in_force"] = new JsonArray(FactsOf(facts, Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate.DateNoLongerInForce, baseAct)
                .Select(static fact => (JsonNode)fact.value).ToArray());
            payload["base_act_rule"] = BaseActRule;
        }

        using var document = JsonSerializer.SerializeToDocument(payload);
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, "no_version_for_date", document.RootElement));
    }

    /// <summary>
    /// The refusal for a state covering the date whose text this index does not hold (design §3.2): <c>text_withheld</c> for a
    /// rights reason, with the official identity and link and, since the index binds no body to a textless row, the state's
    /// digest as the content digest; <c>text_not_available</c> otherwise (a PDF, a derivation gap, a misfiled file, a state not
    /// acquired), with the official source and no transport evidence. Both name the state, its status and its reason.
    /// </summary>
    private V3PlatformOperationOutcome RefuseTextless(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt,
        LuxembourgIndexCatalogueState row,
        string? requestedDate,
        string? bound)
    {
        var withheld = string.Equals(row.TextStatus, "withheld_rights", StringComparison.Ordinal);
        var payload = new JsonObject { ["official_identity"] = row.PublisherLegalResourceIri };
        if (withheld)
        {
            payload["official_link"] = row.PublisherLegalResourceIri;
            payload["content_sha256"] = row.StateSha256;
        }
        else
        {
            payload["official_source"] = row.PublisherLegalResourceIri;
            payload["retained_transport_evidence"] = "none";
        }

        payload["requested_date"] = requestedDate;
        if (bound is not null)
        {
            payload["bound"] = bound;
        }

        payload["state_sha256"] = row.StateSha256;
        payload["permalink"] = row.Language.Length == 0 ? null : LuxembourgPermalink(row.WorkKey, row.ApplicabilityDate, row.StateSha256, row.Language);
        payload["stable_coordinate"] = $"/lu-legilux/{row.WorkKey}/{row.ApplicabilityDate}";
        payload["language"] = row.Language.Length == 0 ? null : row.Language;
        payload["applicability_date"] = row.ApplicabilityDate;
        payload["date_role"] = row.DateRole;
        payload["state_kind"] = row.StateKind;
        payload["applicable_to"] = row.ApplicableTo;
        payload["applicable_to_source"] = row.ApplicableToSource;
        payload["boundary"] = row.Boundary;
        payload["text_status"] = row.TextStatus;
        payload["text_status_reason"] = row.TextStatusReason;
        payload["rule"] = TextlessRule;
        payload["what_would_answer"] = new JsonArray((JsonNode)"new_official_observation");
        payload["asserts_absence_of_law"] = false;
        using var document = JsonSerializer.SerializeToDocument(payload);
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, withheld ? "text_withheld" : "text_not_available", document.RootElement));
    }

    internal const string AmbiguousCandidatesRule =
        "on lex-v3-luxembourg-index/9 the candidate_ lists name every state whose interval holds the date, each in the order of " +
        "candidate_state_sha256s: its kind, its interval (candidate_applicability_dates to candidate_applicable_tos, and that end's " +
        "source), its boundary and its text status; they are not one wording held, so none is chosen";

    /// <summary>
    /// The candidates of an index 9 ambiguity, by the permalinks the refusal lists (each <c>/lu-legilux/{work}/{date}--{digest}/{language}</c>):
    /// the catalogue row of each, or none when the index holds no row for it.
    /// </summary>
    private JsonObject? CandidateStates(IReadOnlyList<string> candidates)
    {
        if (!RulesByInterval)
        {
            return null;
        }

        var rows = new List<LuxembourgIndexCatalogueState>();
        foreach (var candidate in candidates)
        {
            if (PermalinkSegments(candidate, "lu-legilux") is { } segments &&
                CatalogueOf(segments.Work).ByDigest.GetValueOrDefault(segments.Digest) is { } row)
            {
                rows.Add(row);
            }
        }

        return new JsonObject
        {
            // Lists of scalars, each in the order of candidate_state_sha256s, so a refusal card reads them as every other payload.
            ["candidate_state_sha256s"] = new JsonArray(rows.Select(static row => (JsonNode)row.StateSha256).ToArray()),
            ["candidate_state_kinds"] = new JsonArray(rows.Select(static row => (JsonNode?)row.StateKind).ToArray()),
            ["candidate_applicability_dates"] = new JsonArray(rows.Select(static row => (JsonNode)row.ApplicabilityDate).ToArray()),
            ["candidate_applicable_tos"] = new JsonArray(rows.Select(static row => (JsonNode?)row.ApplicableTo).ToArray()),
            ["candidate_applicable_to_sources"] = new JsonArray(rows.Select(static row => (JsonNode?)row.ApplicableToSource).ToArray()),
            ["candidate_boundaries"] = new JsonArray(rows.Select(static row => (JsonNode?)row.Boundary).ToArray()),
            ["candidate_text_statuses"] = new JsonArray(rows.Select(static row => (JsonNode)row.TextStatus).ToArray()),
        };
    }

    /// <summary>A catalogue row's address: its permalink, or its stable coordinate when the catalogue names no expression of it.</summary>
    private static string RowAddress(LuxembourgIndexCatalogueState row) =>
        row.Language.Length == 0
            ? $"/lu-legilux/{row.WorkKey}/{row.ApplicabilityDate}"
            : LuxembourgPermalink(row.WorkKey, row.ApplicabilityDate, row.StateSha256, row.Language);

    /// <summary>
    /// The state a hash-pinned permalink names when it is a textless row of index 9: its text is not held, so the permalink is
    /// refused for that, naming it, rather than unknown.
    /// </summary>
    private LuxembourgIndexCatalogueState? TextlessRowAt(string workKey, string date, string digest, string language)
    {
        if (!RulesByInterval || _reader!.ResolveWorkStates(workKey).Count == 0)
        {
            return null;
        }

        return CatalogueOf(workKey).ByDigest.GetValueOrDefault(digest) is { } row &&
               string.Equals(row.ApplicabilityDate, date, StringComparison.Ordinal) &&
               string.Equals(row.Language, language, StringComparison.Ordinal) &&
               !string.Equals(row.TextStatus, TextHeld, StringComparison.Ordinal)
            ? row
            : null;
    }
}
