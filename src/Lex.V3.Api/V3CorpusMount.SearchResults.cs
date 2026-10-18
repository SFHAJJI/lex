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
        "first match, at most 80 characters before it and 200 after it, cut at whitespace (a word that alone passes a bound is cut at the " +
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

    private readonly object _searchPopulationGate = new();
    private SearchPopulation? _searchPopulation;

    /// <summary>One held state, whether the rights rule admits its text to search, and its articles with searchable text.</summary>
    private sealed record SearchableState(LuxembourgIndexResolvedState State, bool Admitted, long ArticlesWithText);

    /// <summary>Every held state as a search sees it, by work, and the build time the mounted log records.</summary>
    private sealed record SearchPopulation(
        IReadOnlyDictionary<string, SearchableState> ByState,
        IReadOnlyDictionary<string, SearchableState[]> ByWork,
        string? BuiltAt);

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
            _searchPopulation = new SearchPopulation(
                byState,
                byState.Values
                    .GroupBy(static entry => entry.State.WorkKey, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal),
                observations.Count == 0 ? null : observations[^1].BuiltAt);
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
            if (requestedDate is null)
            {
                searched.AddRange(ofLanguage.Where(static entry => entry.Admitted));
                withheld += ofLanguage.Count(static entry => !entry.Admitted);
                continue;
            }

            var selected = SelectAtDate(ofLanguage.Select(static entry => entry.State).ToArray(), requestedDate).Selected;
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
            rule = SearchedRule,
        };
    }

    /// <summary>
    /// A lane's hits in the order a page serves them: by work key, publisher date, the publisher's article id in the reading's
    /// order (<see cref="CompareLuxembourgArticleIds"/>, so the hits of one version are listed as its reading lists them),
    /// article identity and state digest (<see cref="SearchRanking"/>).
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
                compared = CompareArticleIdParts(left.Parts, right.Parts, left.Hit.PublisherId, right.Hit.PublisherId);
            }

            if (compared == 0)
            {
                compared = string.CompareOrdinal(left.Hit.ArticleIdentitySha256, right.Hit.ArticleIdentitySha256);
            }

            return compared != 0 ? compared : string.CompareOrdinal(left.Hit.StateSha256, right.Hit.StateSha256);
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
        if (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            // Cut at whitespace: the run begins after the first whitespace past the bound, unless the word the bound falls in
            // reaches the match, which is then cut at the bound.
            var space = IndexOfWhiteSpace(text, start, first.Start);
            if (space >= 0)
            {
                start = space + 1;
            }
        }

        while (start < first.Start && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        if (start > 0 && start < first.Start && char.IsLowSurrogate(text[start]))
        {
            start++;
        }

        var end = Math.Min(text.Length, first.End + SearchExtractAfter);
        if (end < text.Length && !char.IsWhiteSpace(text[end]) && !char.IsWhiteSpace(text[end - 1]))
        {
            var space = LastIndexOfWhiteSpace(text, first.End, end);
            if (space >= 0)
            {
                end = space;
            }
        }

        while (end > first.End && char.IsWhiteSpace(text[end - 1]))
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

    private static int IndexOfWhiteSpace(string text, int from, int to)
    {
        for (var index = from; index < to; index++)
        {
            if (char.IsWhiteSpace(text[index]))
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
            if (char.IsWhiteSpace(text[index]))
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
