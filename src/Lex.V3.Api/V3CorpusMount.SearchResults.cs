using System.Security.Cryptography;
using System.Text;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// What a Luxembourg <c>search</c> answer carries so its results can be read by act (the plan's item 4.3; the specification
/// of record's R59, R21 and R11), and so a search with no hit can say what was searched (item 4.5): each hit's word-for-word
/// extract with the matched words marked (<c>extract</c>), each act of the page once with its title and how it is held
/// (<c>acts</c>), the work key of each act the title ladder names (so a card links to it), and the population the lanes
/// matched against (<c>searched</c>), every count from this index. Each is additive to the answer as it was; the hits
/// within one version are in the order of the publisher's article ids, as a reading lists them.
/// </summary>
internal sealed partial class V3CorpusMount
{
    /// <summary>The most characters an extract keeps before its first match.</summary>
    internal const int SearchExtractBefore = 80;

    /// <summary>The most characters an extract keeps after its first match.</summary>
    internal const int SearchExtractAfter = 200;

    internal const string SearchExtractRule =
        "extract is a run of the article's searchable text, the bytes the lane matched and evidence_bundle quotes, word for word: around the " +
        "first match, at most 80 characters before it and 200 after it, cut at a breaking space and never at a no-break space (U+00A0, " +
        "U+202F, U+2007, which hold a date's day to its month or a number to its unit; a word that alone passes a bound is cut at the " +
        "bound); parts are that run in order, and a part is matched when it is a whole match: the query as typed for a strict hit, a term " +
        "or overlapping terms for a relaxed hit; a match the run cuts is not marked; byte_start and byte_end are the run's UTF-8 byte offsets " +
        "in the text whose SHA-256 is text_sha256, and nothing is folded, normalised or elided inside the run";

    internal const string SearchActsRule =
        "acts names each act of this page once, in the order its hits first appear: its work key, its dossier, the publisher's title of it " +
        "this index holds (in the language searched when one is held, else French, else another; a title before a short title), or null " +
        "when none is held, the as-published label when every state this index holds of it is the act as first published, and its hits in " +
        "the whole result, not the page";

    internal const string SearchedRule =
        "searched counts what the lanes matched against, from this index: the states in the language asked whose text the rights rule " +
        "admits (with a date, the one state the date selects for each work, a work with several on that date contributing none), their " +
        "works and their articles with searchable text, within the work named when one is; states_withheld_by_rights counts the states of " +
        "that scope whose text the rights rule keeps out; index_built_at is the build time the mounted log records, null when it records " +
        "none; nothing here counts what the publisher holds";

    /// <summary>
    /// <see cref="SearchedRule"/> over lex-v3-luxembourg-index/9, which keeps rows for versions it holds no text of: they are in
    /// none of the counts of what was matched; the ones its rights withhold are counted with the states the rights rule keeps
    /// out (S7a, the hand-off of #982); with a date, the state the interval rules select.
    /// </summary>
    internal const string SearchedRuleNine = SearchedRule +
        "; this index also records versions it holds no text of (withheld by their rights, not available or not acquired): those rows " +
        "are in none of the counts of what was matched, and states_withheld_by_rights counts, beside the held states whose sources the " +
        "rights rule does not admit, the versions in that scope whose text is withheld by their rights (text_status withheld_rights; " +
        "with a date, when the version the interval rules select for a work is one); a version not available or not acquired is in no " +
        "count";

    private readonly object _searchPopulationGate = new();
    private SearchPopulation? _searchPopulation;

    /// <summary>One held state, whether the rights rule admits its text to search, and its articles with searchable text.</summary>
    private sealed record SearchableState(LuxembourgIndexResolvedState State, bool Admitted, long ArticlesWithText);

    /// <summary>
    /// Every held state as a search sees it, by work, the build time the mounted log records, and on index 9 how many of each
    /// work's versions in each language its rights withhold the text of (textless rows of <c>withheld_rights</c>).
    /// </summary>
    private sealed record SearchPopulation(
        IReadOnlyDictionary<string, SearchableState> ByState,
        IReadOnlyDictionary<string, SearchableState[]> ByWork,
        string? BuiltAt,
        IReadOnlyDictionary<(string WorkKey, string Language), int>? WithheldTextless = null);

    /// <summary>
    /// The states as a search sees them, read once (the index is immutable): every state, whether the rights rule admits it
    /// (<see cref="SearchRightsRule"/>: every source acquired and admitted, as <c>evidence_bundle</c> decides), and its
    /// articles with searchable text.
    /// </summary>
    private SearchPopulation SearchPopulationOf()
    {
        ArgumentNullException.ThrowIfNull(_reader);
        lock (_searchPopulationGate)
        {
            if (_searchPopulation is { } held)
            {
                return held;
            }

            var sources = _reader.ResolveStateTextSources()
                .GroupBy(static row => row.StateSha256, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
            var byState = new Dictionary<string, SearchableState>(StringComparer.Ordinal);
            foreach (var state in _reader.ResolveStatesInPeriod("0000-01-01", "9999-12-31"))
            {
                var rows = sources.GetValueOrDefault(state.StateSha256) ?? [];
                byState[state.StateSha256] = new SearchableState(
                    state,
                    rows.Length > 0 && rows.All(static row =>
                        string.Equals(row.Outcome, AcquiredOutcomeToken, StringComparison.Ordinal) &&
                        string.Equals(row.RightsDisposition, EvidenceBundleAdmittingRightsDisposition, StringComparison.Ordinal)),
                    rows.Sum(static row => row.ArticlesWithSearchableText));
            }

            var observations = _reader.ResolveObservations();
            var byWork = byState.Values
                .GroupBy(static entry => entry.State.WorkKey, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
            // On index 9, each work's versions withheld by their rights (read from each work's catalogue once, which the interval
            // rules cache for every later answer).
            var withheldTextless = RulesByInterval
                ? byWork.Keys
                    .SelectMany(work => CatalogueOf(work).Rows.Where(static row =>
                        string.Equals(row.TextStatus, "withheld_rights", StringComparison.Ordinal) && row.Language.Length > 0))
                    .GroupBy(static row => (row.WorkKey, row.Language))
                    .ToDictionary(static group => group.Key, static group => group.Count())
                : null;
            _searchPopulation = new SearchPopulation(
                byState,
                byWork,
                observations.Count == 0 ? null : observations[^1].BuiltAt,
                withheldTextless);
            return _searchPopulation;
        }
    }

    /// <summary>Whether the rights rule admits a held state's text to search (<see cref="SearchRightsRule"/>).</summary>
    private bool SearchAdmits(string stateSha256) =>
        SearchPopulationOf().ByState.TryGetValue(stateSha256, out var entry) && entry.Admitted;

    /// <summary>The population a search's lanes matched against (<see cref="SearchedRule"/>).</summary>
    private object Searched(string language, string? requestedDate, string? workKey)
    {
        var population = SearchPopulationOf();
        var works = workKey is null
            ? population.ByWork.Values
            : population.ByWork.TryGetValue(workKey, out var named) ? [named] : [];
        var searched = new List<SearchableState>();
        var withheld = 0;
        var several = 0;
        foreach (var ofWork in works)
        {
            var ofLanguage = ofWork.Where(entry => string.Equals(entry.State.Language, language, StringComparison.Ordinal)).ToArray();
            var ofWorkKey = ofWork[0].State.WorkKey;
            if (requestedDate is null)
            {
                searched.AddRange(ofLanguage.Where(static entry => entry.Admitted));
                withheld += ofLanguage.Count(static entry => !entry.Admitted) +
                            (population.WithheldTextless?.GetValueOrDefault((ofWorkKey, language)) ?? 0);
                continue;
            }

            var selected = SelectAt(ofLanguage.Select(static entry => entry.State).ToArray(), requestedDate).Selected;
            if (selected.Length == 0 && ofLanguage.Length > 0 && RulesByInterval &&
                CoverAt(CatalogueOf(ofWorkKey), language, requestedDate) is { Kind: CoverTextless, Row.TextStatus: "withheld_rights" })
            {
                // The version the date selects is withheld by its rights: kept out as a held state the rule keeps out is.
                withheld++;
                continue;
            }

            if (selected.Length > 1)
            {
                several++;
                continue;
            }

            if (selected.Length == 1)
            {
                var entry = population.ByState[selected[0].StateSha256];
                if (entry.Admitted)
                {
                    searched.Add(entry);
                }
                else
                {
                    withheld++;
                }
            }
        }

        return new
        {
            publisher = "lu-legilux",
            language,
            date = requestedDate,
            identifier_work_key = workKey,
            works = searched.Select(static entry => entry.State.WorkKey).Distinct(StringComparer.Ordinal).Count(),
            states = searched.Count,
            articles_with_searchable_text = searched.Sum(static entry => entry.ArticlesWithText),
            states_withheld_by_rights = withheld,
            works_with_several_states_on_date = requestedDate is null ? (int?)null : several,
            index_built_at = population.BuiltAt,
            rule = _reader!.RecordsCatalogueStates ? SearchedRuleNine : SearchedRule,
        };
    }

    /// <summary>
    /// A lane's hits in the order a page serves them: by work key, publisher date, state digest, the publisher's article id in
    /// the reading's order (<see cref="CompareLuxembourgArticleIds"/>, so the hits of one version are listed as its reading
    /// lists them) and article identity (<see cref="SearchRanking"/>). The state comes before the article: two states of one
    /// work on one date (the ambiguous versions an undated search serves both of) keep their hits apart, so a page never
    /// interleaves them under one version's date (the review of #982, F1).
    /// </summary>
    private static LuxembourgIndexSearchHit[] InSearchOrder(IEnumerable<LuxembourgIndexSearchHit> hits)
    {
        // Each id read into its parts once, as the reading's order reads them (a word every article holds is thousands of
        // hits); the order is total, since an article of a state is one hit.
        var keyed = hits.Select(static hit => (Hit: hit, Parts: ArticleIdParts(hit.PublisherId))).ToArray();
        Array.Sort(keyed, static (left, right) =>
        {
            var compared = string.CompareOrdinal(left.Hit.WorkKey, right.Hit.WorkKey);
            if (compared == 0)
            {
                compared = string.CompareOrdinal(left.Hit.ApplicabilityDate, right.Hit.ApplicabilityDate);
            }

            if (compared == 0)
            {
                compared = string.CompareOrdinal(left.Hit.StateSha256, right.Hit.StateSha256);
            }

            if (compared == 0)
            {
                compared = CompareArticleIdParts(left.Parts, right.Parts, left.Hit.PublisherId, right.Hit.PublisherId);
            }

            return compared != 0 ? compared : string.CompareOrdinal(left.Hit.ArticleIdentitySha256, right.Hit.ArticleIdentitySha256);
        });
        return keyed.Select(static entry => entry.Hit).ToArray();
    }

    /// <summary>One part of an extract: a run of the text, and whether it is a whole match.</summary>
    internal sealed record SearchExtractPart(string Text, bool Matched);

    /// <summary>An extract (<see cref="SearchExtractRule"/>): its run's character and byte offsets in the text, and its parts.</summary>
    internal sealed record SearchExtract(int Start, int End, int ByteStart, int ByteEnd, IReadOnlyList<SearchExtractPart> Parts);

    /// <summary>
    /// The extract of <paramref name="text"/> for a hit matched by <paramref name="needles"/> (the query as typed for a strict
    /// hit, the terms for a relaxed one), by <see cref="SearchExtractRule"/>; null when no needle occurs in the text, which a
    /// hit's text never is.
    /// </summary>
    internal static SearchExtract? ExtractOf(string text, IReadOnlyList<string> needles)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(needles);

        // Every occurrence of every needle, each needle's found left to right without overlapping itself; then overlapping
        // occurrences of different needles merged into one run (touching ones stay apart).
        var occurrences = new List<(int Start, int End)>();
        foreach (var needle in needles.Where(static needle => needle.Length > 0).Distinct(StringComparer.Ordinal))
        {
            for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            {
                occurrences.Add((at, at + needle.Length));
            }
        }

        if (occurrences.Count == 0)
        {
            return null;
        }

        occurrences.Sort(static (left, right) => left.Start != right.Start ? left.Start.CompareTo(right.Start) : left.End.CompareTo(right.End));
        var spans = new List<(int Start, int End)>();
        foreach (var occurrence in occurrences)
        {
            if (spans.Count > 0 && occurrence.Start < spans[^1].End)
            {
                spans[^1] = (spans[^1].Start, Math.Max(spans[^1].End, occurrence.End));
            }
            else
            {
                spans.Add(occurrence);
            }
        }

        var first = spans[0];
        var start = Math.Max(0, first.Start - SearchExtractBefore);
        if (start > 0 && !IsExtractBreak(text[start - 1]))
        {
            // Cut at whitespace: the run begins after the first whitespace past the bound, unless the word the bound falls in
            // reaches the match, which is then cut at the bound.
            var space = IndexOfWhiteSpace(text, start, first.Start);
            if (space >= 0)
            {
                start = space + 1;
            }
        }

        while (start < first.Start && IsExtractBreak(text[start]))
        {
            start++;
        }

        if (start > 0 && start < first.Start && char.IsLowSurrogate(text[start]))
        {
            start++;
        }

        var end = Math.Min(text.Length, first.End + SearchExtractAfter);
        if (end < text.Length && !IsExtractBreak(text[end]) && !IsExtractBreak(text[end - 1]))
        {
            var space = LastIndexOfWhiteSpace(text, first.End, end);
            if (space >= 0)
            {
                end = space;
            }
        }

        while (end > first.End && IsExtractBreak(text[end - 1]))
        {
            end--;
        }

        if (end < text.Length && end > first.End && char.IsLowSurrogate(text[end]))
        {
            end--;
        }

        // A match the run cuts is not marked: the parts mark only whole matches.
        var parts = new List<SearchExtractPart>();
        var cursor = start;
        foreach (var (spanStart, spanEnd) in spans.Where(span => span.Start >= start && span.End <= end))
        {
            if (spanStart > cursor)
            {
                parts.Add(new SearchExtractPart(text[cursor..spanStart], false));
            }

            parts.Add(new SearchExtractPart(text[spanStart..spanEnd], true));
            cursor = spanEnd;
        }

        if (end > cursor)
        {
            parts.Add(new SearchExtractPart(text[cursor..end], false));
        }

        var byteStart = Encoding.UTF8.GetByteCount(text.AsSpan(0, start));
        return new SearchExtract(start, end, byteStart, byteStart + Encoding.UTF8.GetByteCount(text.AsSpan(start, end - start)), parts);
    }

    /// <summary>
    /// A space an extract may be cut at: whitespace, but never a no-break space (U+00A0, U+202F, U+2007), which the publisher
    /// sets to hold "6 septembre" or "§ 2" together (the review of #982, F3).
    /// </summary>
    internal static bool IsExtractBreak(char character) =>
        char.IsWhiteSpace(character) && character is not '\u00A0' and not '\u202F' and not '\u2007';

    private static int IndexOfWhiteSpace(string text, int from, int to)
    {
        for (var index = from; index < to; index++)
        {
            if (IsExtractBreak(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int LastIndexOfWhiteSpace(string text, int from, int to)
    {
        for (var index = to - 1; index >= from; index--)
        {
            if (IsExtractBreak(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The extract a hit of this page carries, as the answer serves it, from its article's searchable text.</summary>
    private static object ExtractMember(string text, string lane, string query, IReadOnlyList<string> terms)
    {
        var extract = ExtractOf(text, lane == "strict" ? [query] : terms)
            // The lane matched these bytes, so a text that holds none of its needles is a damaged index, never an extract.
            ?? throw new InvalidDataException("A search hit's searchable text does not hold what its lane matched.");
        var bytes = Encoding.UTF8.GetBytes(text);
        return new
        {
            text_sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            text_byte_length = bytes.Length,
            byte_start = extract.ByteStart,
            byte_end = extract.ByteEnd,
            parts = extract.Parts.Select(static part => new { text = part.Text, matched = part.Matched }).ToArray(),
        };
    }

    /// <summary>
    /// The acts of a page once each (<see cref="SearchActsRule"/>), in the order their hits first appear, with each act's hits
    /// counted over the whole result.
    /// </summary>
    private object[] SearchActs(
        IReadOnlyList<LuxembourgIndexSearchHit> page,
        IReadOnlyList<LuxembourgIndexSearchHit> all,
        IReadOnlyDictionary<string, IReadOnlyList<LuxembourgIndexResolvedState>> statesByWork,
        string language)
    {
        ArgumentNullException.ThrowIfNull(_reader);
        var works = page.Select(static hit => hit.WorkKey).Distinct(StringComparer.Ordinal).ToArray();
        if (works.Length == 0)
        {
            return [];
        }

        var workOfExpression = works
            .SelectMany(work => statesByWork[work].Select(state => (state.ExpressionIri, Work: work)))
            .DistinctBy(static pair => pair.ExpressionIri, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.ExpressionIri, static pair => pair.Work, StringComparer.Ordinal);
        var titles = _reader.ResolveWorkTitles(workOfExpression.Keys.Order(StringComparer.Ordinal).ToArray())
            .Where(title => workOfExpression.ContainsKey(title.ExpressionIri))
            .GroupBy(title => workOfExpression[title.ExpressionIri], StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .OrderBy(title => string.Equals(title.Language, language, StringComparison.Ordinal) ? 0 : title.Language == "fra" ? 1 : 2)
                    .ThenBy(static title => title.Language, StringComparer.Ordinal)
                    .ThenBy(static title => title.TitleKind == "title" ? 0 : 1)
                    .ThenBy(static title => title.Title, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);
        var hitsByWork = all.GroupBy(static hit => hit.WorkKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        return works.Select(work =>
        {
            var title = titles.GetValueOrDefault(work);
            return (object)new
            {
                work_key = work,
                publisher_work_iri = statesByWork[work][0].PublisherWorkIri,
                dossier = "/lu-legilux/" + work,
                title = title?.Title,
                title_language = title?.Language,
                title_kind = title?.TitleKind,
                as_published = HeldOnlyAsPublished(statesByWork[work]) ? AsPublishedLabel : null,
                hits = hitsByWork.GetValueOrDefault(work),
            };
        }).ToArray();
    }

    /// <summary>
    /// The work key of each work the title ladder names, in the order given, by the states this index holds of its
    /// expressions: the key they hold, or null when this index holds no state of them (the title table also holds titles of
    /// acts with no held state), and the card is then shown as an act this site holds no version of. A state's work key is
    /// its work IRI's, so one expression's states never name two keys.
    /// </summary>
    private string?[] WorkKeysOfCards(IReadOnlyList<LuxembourgIndexResolvedWork> cards)
    {
        ArgumentNullException.ThrowIfNull(_reader);
        if (cards.Count == 0)
        {
            return [];
        }

        var workOfExpression = _reader.ResolveStatesOfExpressions(
                cards.SelectMany(static card => card.ExpressionIris).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
            .GroupBy(static state => state.ExpressionIri, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static state => state.WorkKey).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return cards.Select(card =>
        {
            var held = card.ExpressionIris
                .SelectMany(expression => workOfExpression.GetValueOrDefault(expression) ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return held.Length == 1 ? held[0] : null;
        }).ToArray();
    }
}
