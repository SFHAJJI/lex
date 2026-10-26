using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// The catalogue's chain said where a work's history is read (data design §3.2, slice S7b): the timeline and the dossier name,
/// on lex-v3-luxembourg-index/9, the states of the publisher's catalogue whose text this index does not hold and the chain's
/// transitions as the publisher left them (the gaps, the overlaps, the same-date groups, the days an unclear end gives to
/// neither state and the states whose end is on or before their start); an article's history says where no state with held
/// text covers its dates, so an unchanged wording is never read across a stretch the index holds no text of; and a date is
/// said with its role (R21). An index of schema 8 or 6 holds none of this, and its answers are the ones the code before sent.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string CatalogueChainRule =
        "on lex-v3-luxembourg-index/9 the publisher catalogue's states of this work as this index read it, beside the held states: " +
        "textless_states names each state of the catalogue in scope whose text this index does not hold, with why (text_status), " +
        "and none of them is served in another's place; transitions is the chain of the work's French states (a state of another " +
        "language takes its French state's interval), each from one state to the first state of the next later date, its kind how " +
        "the first state's end meets that start (contiguous: the end the publisher states is the next start; derived: no end is " +
        "stated, so the next start ends it; inclusive_suspected: the stated end is the day before the next start; hole: the stated " +
        "end is before the next start; overlap: the stated end is after the next start; invalid: the stated end is on or before " +
        "the state's own start), or to the next state of the same date (same_date), each span between those two states alone; gaps " +
        "lists each hole, from the furthest end of the states that begin before it to the next start, which no state covers and " +
        "nothing bridges; overlaps lists each span two states of different dates both cover, from the later start to the earlier of " +
        "their ends; same_date lists each group of states of one date; unclear_end_days lists each day a " +
        "suspected inclusive end gives to neither state; invalid_intervals lists each state whose end is on or before its start, " +
        "which covers no date and whose permalink still opens it; every date here is the catalogue's own, and a gap in the " +
        "catalogue is not a gap in the law";

    /// <summary>
    /// A timeline's or a dossier's answer with the catalogue's chain of its work (<see cref="CatalogueChainRule"/>) on index 9:
    /// the textless states in the language scope, and the French chain's transitions with the gaps, overlaps, same-date groups,
    /// unclear end days and invalid intervals they make. An answer of an index of schema 8 or 6 is unchanged.
    /// </summary>
    private object WithCatalogueChain(object result, string workKey, string? requestedLanguage)
    {
        if (!RulesByInterval)
        {
            return result;
        }

        var work = CatalogueOf(workKey);
        var node = result as JsonObject ?? JsonSerializer.SerializeToNode(result)!.AsObject();
        var inScope = requestedLanguage is null ? work.Rows : ChainOf(work, requestedLanguage);
        node["textless_states"] = new JsonArray(inScope
            .Where(static row => !string.Equals(row.TextStatus, TextHeld, StringComparison.Ordinal))
            .Select(static row => (JsonNode)CatalogueRowSummary(row))
            .ToArray());

        var byDigest = work.ByDigest;
        string DateOf(string digest) => byDigest[digest].ApplicabilityDate;
        node["transitions"] = new JsonArray(work.Intervals.Select(interval => (JsonNode)new JsonObject
        {
            ["ordinal"] = interval.Ordinal,
            ["kind"] = interval.Kind,
            ["from_state_sha256"] = interval.FromStateSha256,
            ["from_applicability_date"] = DateOf(interval.FromStateSha256),
            ["to_state_sha256"] = interval.ToStateSha256,
            ["to_applicability_date"] = DateOf(interval.ToStateSha256),
            ["span_from"] = interval.SpanFrom,
            ["span_to"] = interval.SpanTo,
        }).ToArray());
        // The gaps and overlaps are the reader's, from the chain's own rows as CoverAt reads them, not the transitions' pairwise
        // spans: a state nested in an earlier one's interval neither opens a gap the earlier one covers nor stretches an
        // overlap past its own end (the review of #987, F7).
        node["gaps"] = new JsonArray(GapsOf(work).Select(static gap => (JsonNode)new JsonObject
        {
            ["from"] = gap.From,
            ["to"] = gap.To,
            ["after_state_sha256"] = gap.After.StateSha256,
            ["before_state_sha256"] = gap.Before.StateSha256,
        }).ToArray());
        node["overlaps"] = new JsonArray(OverlapsOf(work).Select(static overlap => (JsonNode)new JsonObject
        {
            ["from"] = overlap.From,
            ["to"] = overlap.To,
            ["state_sha256s"] = new JsonArray(overlap.Earlier.StateSha256, overlap.Later.StateSha256),
        }).ToArray());

        // A same-date group is every state the chain's same_date links join on one date, in chain order.
        var groups = new List<(string Date, List<string> States)>();
        foreach (var interval in work.Intervals.Where(static interval => interval.Kind == "same_date"))
        {
            var date = DateOf(interval.FromStateSha256);
            var group = groups.FirstOrDefault(candidate => string.Equals(candidate.Date, date, StringComparison.Ordinal));
            if (group.States is null)
            {
                group = (date, new List<string>());
                groups.Add(group);
            }

            foreach (var digest in new[] { interval.FromStateSha256, interval.ToStateSha256 })
            {
                if (!group.States.Contains(digest, StringComparer.Ordinal))
                {
                    group.States.Add(digest);
                }
            }
        }

        node["same_date"] = new JsonArray(groups.Select(static group => (JsonNode)new JsonObject
        {
            ["applicability_date"] = group.Date,
            ["state_sha256s"] = new JsonArray(group.States.Select(static digest => (JsonNode)digest).ToArray()),
        }).ToArray());
        node["unclear_end_days"] = new JsonArray(work.Intervals.Where(static interval => interval.Kind == "inclusive_suspected").Select(interval => (JsonNode)new JsonObject
        {
            ["date"] = byDigest[interval.FromStateSha256].ApplicableTo,
            ["after_state_sha256"] = interval.FromStateSha256,
            ["before_state_sha256"] = interval.ToStateSha256,
        }).ToArray());
        node["invalid_intervals"] = new JsonArray(ChainOf(work, FrenchLanguage)
            .Where(static row => string.Equals(row.Boundary, "invalid", StringComparison.Ordinal))
            .Select(static row => (JsonNode)new JsonObject
            {
                ["state_sha256"] = row.StateSha256,
                ["applicability_date"] = row.ApplicabilityDate,
                ["applicable_to"] = row.ApplicableTo,
                ["text_status"] = row.TextStatus,
            }).ToArray());
        node["catalogue_chain_rule"] = CatalogueChainRule;
        return node;
    }

    /// <summary>
    /// The catalogue's holes in a work's French chain, as <see cref="CoverAt"/> reads them: between two consecutive start dates,
    /// the span from the furthest end of the states that begin on or before the first to the second, when that end falls
    /// before it; never where a state whose end is on or before its start begins (that span is the invalid interval's), and
    /// never the one day a suspected inclusive end gives to neither state. Each with the state whose end opens it and the
    /// first state of the next date.
    /// </summary>
    private static List<(string From, string To, LuxembourgIndexCatalogueState After, LuxembourgIndexCatalogueState Before)> GapsOf(CatalogueWork work)
    {
        static bool Invalid(LuxembourgIndexCatalogueState row) => string.Equals(row.Boundary, "invalid", StringComparison.Ordinal);
        var chain = ChainOf(work, FrenchLanguage);
        var dates = chain.Select(static row => row.ApplicabilityDate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var gaps = new List<(string, string, LuxembourgIndexCatalogueState, LuxembourgIndexCatalogueState)>();
        for (var index = 0; index + 1 < dates.Length; index++)
        {
            var (start, next) = (dates[index], dates[index + 1]);
            if (chain.Any(row => Invalid(row) && string.Equals(row.ApplicabilityDate, start, StringComparison.Ordinal)))
            {
                continue;
            }

            var before = chain.Where(row => !Invalid(row) && string.CompareOrdinal(row.ApplicabilityDate, start) <= 0).ToArray();
            if (before.Length == 0 || before.Any(static row => row.ApplicableTo is null))
            {
                continue;
            }

            var furthest = before.OrderBy(static row => row.ApplicableTo, StringComparer.Ordinal).Last();
            var end = furthest.ApplicableTo!;
            if (string.CompareOrdinal(end, next) >= 0 ||
                string.Equals(end, DayBefore(next), StringComparison.Ordinal) &&
                chain.Any(row => string.Equals(row.Boundary, "inclusive_suspected", StringComparison.Ordinal) &&
                                 string.Equals(row.ApplicableTo, end, StringComparison.Ordinal)))
            {
                continue;
            }

            gaps.Add((end, next, furthest, chain.First(row => string.Equals(row.ApplicabilityDate, next, StringComparison.Ordinal))));
        }

        return gaps;
    }

    /// <summary>
    /// The spans two states of a work's French chain both cover, for every two of different start dates whose intervals meet:
    /// from the later start to the earlier of their ends (none for a state whose end is on or before its start, which covers
    /// no date), in chain order.
    /// </summary>
    private static List<(string From, string To, LuxembourgIndexCatalogueState Earlier, LuxembourgIndexCatalogueState Later)> OverlapsOf(CatalogueWork work)
    {
        var chain = ChainOf(work, FrenchLanguage).Where(static row => !string.Equals(row.Boundary, "invalid", StringComparison.Ordinal)).ToArray();
        var overlaps = new List<(string, string, LuxembourgIndexCatalogueState, LuxembourgIndexCatalogueState)>();
        for (var first = 0; first < chain.Length; first++)
        {
            for (var second = first + 1; second < chain.Length; second++)
            {
                var (earlier, later) = (chain[first], chain[second]);
                if (string.Equals(earlier.ApplicabilityDate, later.ApplicabilityDate, StringComparison.Ordinal) ||
                    earlier.ApplicableTo is not { } earlierEnd || string.CompareOrdinal(earlierEnd, later.ApplicabilityDate) <= 0)
                {
                    continue;
                }

                var to = later.ApplicableTo is { } laterEnd && string.CompareOrdinal(laterEnd, earlierEnd) < 0 ? laterEnd : earlierEnd;
                overlaps.Add((later.ApplicabilityDate, to, earlier, later));
            }
        }

        return overlaps;
    }

    internal const string ContinuityRule =
        "on lex-v3-luxembourg-index/9 continuity_breaks names, per language, each run of dates from the language's first held " +
        "state on that no state with held text covers, by the interval rules as_of applies: a state of the publisher's catalogue " +
        "whose text this index does not hold, or several such states none of which holds text (reasons names each one's " +
        "text_status), a hole in the catalogue (catalogue_hole), the end " +
        "day of an unclear end (publisher_end_convention_unclear), where a state whose end is on or before its start would apply " +
        "(publisher_interval_invalid), or a French state with no expression held in the language (no_expression_in_language); " +
        "from is its first date and to the first date a state with held text covers again, null when none does; named_states " +
        "names the states each reason is about; a date after the catalogue's last state ends is not a break, since no state of " +
        "the catalogue is there; wording_changed compares two held states across a break as it compares any two, and after_break " +
        "says that a break lies between a row and the row before it in its language, so an unchanged wording across a break says " +
        "nothing of the dates between";

    internal const string AnchorRule =
        "the lineage follows the one publisher article id asked, exactly: an id the publisher minted with a date suffix (such as " +
        "art_2_20211219) is another id, never joined to this one, and its own history is asked under that id";

    /// <summary>One run of dates no state with held text covers, in one language (<see cref="ContinuityRule"/>).</summary>
    private sealed record ContinuityBreak(
        string Language,
        string From,
        string? To,
        List<string> Reasons,
        List<LuxembourgIndexCatalogueState> Named);

    /// <summary>
    /// The runs of dates, from the language's first held state on, that no state with held text covers by the interval rules
    /// (<see cref="CoverAt"/>): the interval rules' answer is constant between the dates a state of the chain starts or ends,
    /// so each such date is asked once, and consecutive dates without held text make one run. A date after a closed end, or
    /// before the history, ends the walk: no state of the catalogue is there. Several held states with one wording, or with
    /// different wordings (an ambiguity one of whose states holds text), hold text, so neither is a break; an ambiguity of
    /// states none of which holds text is one, its reasons each state's text status (the review of #987).
    /// </summary>
    private List<ContinuityBreak> ContinuityBreaksOf(string workKey, string language, string firstHeld)
    {
        var work = CatalogueOf(workKey);
        var rows = string.Equals(language, FrenchLanguage, StringComparison.Ordinal)
            ? ChainOf(work, language)
            : ChainOf(work, language).Concat(ChainOf(work, FrenchLanguage)).ToArray();
        var dates = rows
            .SelectMany(static row => row.ApplicableTo is null ? new[] { row.ApplicabilityDate } : new[] { row.ApplicabilityDate, row.ApplicableTo })
            .Append(firstHeld)
            .Where(date => string.CompareOrdinal(date, firstHeld) >= 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var breaks = new List<ContinuityBreak>();
        ContinuityBreak? open = null;
        foreach (var date in dates)
        {
            var cover = CoverAt(work, language, date);
            var held = HoldsText(cover);
            var outside = cover.Kind == CoverNone && cover.Reason is ReasonAfterClosedEnd or ReasonBeforeHistory;
            if (held || outside)
            {
                if (open is not null)
                {
                    breaks.Add(open with { To = date });
                    open = null;
                }

                if (outside)
                {
                    break;
                }

                continue;
            }

            open ??= new ContinuityBreak(language, date, null, [], []);
            // An ambiguity of states none of which holds text gives each one's text status (the review of #987).
            var reasons = cover.Kind == CoverTextless ? new[] { cover.Row!.TextStatus }
                : cover.Kind == CoverAmbiguous ? cover.Others.Select(static row => row.TextStatus).ToArray()
                : new[] { cover.Reason! };
            foreach (var reason in reasons.Where(reason => !open.Reasons.Contains(reason, StringComparer.Ordinal)))
            {
                open.Reasons.Add(reason);
            }

            var named = new List<LuxembourgIndexCatalogueState>();
            if (cover.Row is not null && cover.Reason is not ReasonNoExpressionInLanguage)
            {
                named.Add(cover.Row);
            }

            named.AddRange(cover.Others);
            foreach (var row in named.Where(row => open.Named.All(seen => !string.Equals(seen.StateSha256, row.StateSha256, StringComparison.Ordinal))))
            {
                open.Named.Add(row);
            }
        }

        if (open is not null)
        {
            breaks.Add(open);
        }

        return breaks;
    }

    /// <summary>One continuity break as an article history serves it: scalars and lists of scalars.</summary>
    private static JsonObject ContinuityBreakNode(ContinuityBreak value) => new()
    {
        ["language"] = value.Language,
        ["from"] = value.From,
        ["to"] = value.To,
        ["reasons"] = new JsonArray(value.Reasons.Select(static reason => (JsonNode)reason).ToArray()),
        ["named_states"] = new JsonArray(value.Named.Select(static row => (JsonNode)RowAddress(row)).ToArray()),
        ["named_state_sha256s"] = new JsonArray(value.Named.Select(static row => (JsonNode)row.StateSha256).ToArray()),
    };

    /// <summary>Whether a state's date is its act's entry into force on index 9 (R21): said as that, never as applicable from it.</summary>
    private bool DatedByEntryIntoForce(LuxembourgIndexResolvedState state) =>
        RulesByInterval &&
        string.Equals(CatalogueOf(state.WorkKey).ByDigest.GetValueOrDefault(state.StateSha256)?.DateRole, "entry_into_force", StringComparison.Ordinal);
}
