using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Api;

/// <summary>
/// EU <c>search</c> in one named act (the audit of 2026-10-27, gap 5): over the act's time view when the EU index holds its
/// census, the original wording and every consolidated version whose text is held, each dated by its role (R21: an original
/// wording by the Formex date of the act, a consolidated version by the publisher's consolidation date, which holds until the
/// next dated wording), or with a date the one wording that date selects, as <c>as_of</c> selects it and with its refusals;
/// over the one original wording otherwise. Matching folds capitals and accents (<see cref="TextFolding"/>), as Luxembourg's
/// does, and each hit carries a word-for-word extract of the bytes it matched, with the acknowledgement and the authenticity
/// statement every EU text served carries (Decision 95). An undated search over several wordings serves an article's wording
/// once, under the latest wording that holds it, as an undated Luxembourg search does on index 9.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string EuropeSearchRightsRule =
        "a wording is searched only when its text is one evidence_bundle would quote: every corpus member its articles come from was acquired, " +
        "which an EU build reaches only after retaining the Decision 95 rights receipt; a wording whose text is not admitted is not matched and " +
        "is counted in searched.wordings_withheld_by_rights, and every text served carries the acknowledgement and the authenticity statement " +
        "Decision 95 requires";

    internal const string EuropeSearchFoldRule =
        "with no date, one hit per publisher article id and text: the hit of the latest wording that holds that text (by wording date), each " +
        "other wording that holds it named under the hit's same_wording, so an article a consolidation did not change is one hit and not one per " +
        "wording; strict_hits and relaxed_hits count the hits served, and fold counts the hits before they were folded; with a date, one wording " +
        "is searched and nothing is folded";

    internal const string EuropeSearchedRule =
        "searched counts what the lanes matched against, from this index: the wordings of the act in the language asked whose text the rights " +
        "rule admits (with a date, the one wording the date selects) and their articles with searchable text; wordings_withheld_by_rights counts " +
        "the wordings of that scope whose text the rights rule keeps out, wordings_without_text the dated wordings whose text this index does not " +
        "hold, and versions_without_usable_date the versions the publisher gives no usable date, none of which is searched; nothing here counts " +
        "what the publisher holds";

    /// <summary>One wording an EU search reads: its expression, its date and role, its articles without their text, and its pin.</summary>
    private sealed record EuropeSearchWording(
        string ExpressionId,
        string Language,
        string? Date,
        string Kind,
        string PublisherWorkId,
        string? Celex,
        string? NextDate,
        bool? NextTextHeld,
        IReadOnlyList<EuropeIndexArticleRow> Articles,
        string? Sha256,
        string? Permalink);

    /// <summary>One article of a searched wording whose text matched, in the lane that served it.</summary>
    private sealed record EuropeSearchHit(string Lane, EuropeSearchWording Wording, EuropeIndexArticleRow Article, string TextKey);

    private readonly Dictionary<string, IReadOnlyList<EuropeIndexArticleRow>> _europeArticleRows = new(StringComparer.Ordinal);

    /// <summary>
    /// An expression's articles without their text, read once per mount (the index does not change while it is mounted): the
    /// rows a search lists its wordings by. Reading them is most of an undated search over a long act's many wordings (the
    /// CRR's 55 French wordings, 38,683 articles: about 1.8 s on the driver's machine, then a few milliseconds), and they hold no
    /// text, so the cache is at most the EU index's article rows (about 40 MB over the population's 174,000).
    /// </summary>
    private IReadOnlyList<EuropeIndexArticleRow> EuropeArticleRowsOf(string expressionId)
    {
        lock (_europeTimeGate)
        {
            if (_europeArticleRows.TryGetValue(expressionId, out var held))
            {
                return held;
            }
        }

        var rows = _europeReader!.ResolveExpressionArticleRows(expressionId);
        lock (_europeTimeGate)
        {
            _europeArticleRows[expressionId] = rows;
        }

        return rows;
    }

    /// <summary>The key one distinct text is read and matched by: its digest where the index stores one, else its article.</summary>
    private static string EuropeTextKey(EuropeIndexArticleRow article) => article.TextSha256 ?? "article:" + article.ArticleIdentitySha256;

    /// <summary>
    /// The seed act a search's identifier names in the EU census, as <c>as_of</c> locates it (<see cref="LocateEuropeSeedForTime"/>),
    /// but leaving an identifier the census does not hold to the original-wording search: null with <paramref name="seed"/> null.
    /// </summary>
    private V3PlatformOperationOutcome? LocateEuropeSeedForSearch(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt,
        out string? seed)
    {
        seed = null;
        if (!HasEuropeCensus())
        {
            return null;
        }

        var located = LocateEuropeSeed(request, identifier, observedAt, out seed);
        if (located is not null || seed is not null || !IsEuropeanUnionShaped(identifier))
        {
            return located;
        }

        foreach (var key in EuropeSeedKeysOf(identifier))
        {
            located = LocateEuropeSeed(request, key, observedAt, out seed);
            if (located is not null || seed is not null)
            {
                return located;
            }
        }

        return null;
    }

    /// <summary>
    /// EU <c>search</c> over the act's time view: the wordings the language and the date choose (<see cref="EuropeSearchScope"/>),
    /// matched and answered by <see cref="SearchEuropeWordings"/>. A language the act holds no wording in is
    /// <c>language_not_available</c>; with a date, the refusals are <c>as_of</c>'s (no_version_for_date, ambiguous_version,
    /// text_not_available), through the shared selection.
    /// </summary>
    private V3PlatformOperationOutcome SearchEuropeTime(
        V3PlatformOperationRequest request,
        string identifier,
        string seed,
        string language,
        string query,
        string[] terms,
        string? requestedDate,
        string? mode,
        string? after,
        int limit,
        DateTimeOffset observedAt)
    {
        // R92 before any text is read (the review of #1005, F1): an act the names record withholds is never matched, however it
        // was named; the answer's own gate (WithNamesWithheld) holds it too.
        if (WithheldEurope(seed) is { } withheld)
        {
            return RefuseNamesWithheld(request, withheld, null, observedAt);
        }

        var timeline = EuropeTimelineOf(seed);
        if (RefuseEuropeLanguage(request, timeline, language, observedAt) is { } unavailable)
        {
            return unavailable;
        }

        if (mode is not null && !SearchModes.Contains(mode, StringComparer.Ordinal))
        {
            return RefuseEuropeSearchMode(request, mode, observedAt);
        }

        var ofLanguage = timeline.ByLanguage[language];
        var chosen = new List<(EuropeDatedWording Entry, string? NextDate, bool? NextTextHeld)>();
        if (requestedDate is not null)
        {
            if (SelectEuropeAt(request, timeline, requestedDate, language, observedAt, out var answered) is { } refused)
            {
                return refused;
            }

            chosen.Add((answered[0].Entry!, answered[0].NextDate, answered[0].NextTextHeld));
        }
        else
        {
            for (var position = 0; position < ofLanguage.Dated.Count; position++)
            {
                if (ofLanguage.Dated[position].Answer is not null)
                {
                    chosen.Add((ofLanguage.Dated[position],
                        position + 1 < ofLanguage.Dated.Count ? ofLanguage.Dated[position + 1].Date : null,
                        NextTextHeldAfter(ofLanguage.Dated, position)));
                }
            }
        }

        var wordings = chosen.Select(choice =>
        {
            var answer = choice.Entry.Answer!;
            var articles = EuropeArticleRowsOf(answer.ExpressionId);
            var sha256 = EuropeWordingSha256(seed, answer.State.PublisherWorkIri, answer.ExpressionId, language, choice.Entry.Date,
                articles.Select(static article => article.ArticleIdentitySha256).ToArray());
            return new EuropeSearchWording(answer.ExpressionId, language, choice.Entry.Date, EuropeKindOf(answer.State),
                answer.State.PublisherWorkIri, answer.State.PublisherWorkCelex, choice.NextDate, choice.NextTextHeld, articles, sha256,
                EuropePermalink(seed, choice.Entry.Date, sha256, language));
        }).ToArray();

        // What the scope holds and is not searched, counted (EuropeSearchedRule).
        var notSearched = new
        {
            wordings_without_text = requestedDate is null ? ofLanguage.Dated.Count(static entry => entry.Held.Count == 0) : 0,
            dates_with_different_texts = requestedDate is null ? ofLanguage.Dated.Count(static entry => entry.Basis == "different_texts") : 0,
            versions_without_usable_date = ofLanguage.Unplaced.Count,
        };
        var authenticity = EuropeAuthenticityOf(timeline.ActDates);
        return SearchEuropeWordings(request, identifier, language, query, terms, requestedDate, mode, after, limit, observedAt,
            wordings, census: (seed, timeline.RootWorkIri, EuropeConsolidationsHeld(timeline), notSearched), authenticity);
    }

    /// <summary>A mode other than the two lanes, refused as the Luxembourg search refuses it.</summary>
    private V3PlatformOperationOutcome RefuseEuropeSearchMode(V3PlatformOperationRequest request, string mode, DateTimeOffset observedAt)
    {
        using var unavailableMode = JsonSerializer.SerializeToDocument(new { requested_mode = mode, available_modes = SearchModes });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationRefusal(request, "retrieval_mode_unavailable", unavailableMode.RootElement));
    }

    /// <summary>
    /// The lanes over the given wordings and the answer: rights first (<see cref="EuropeSearchRightsRule"/>), then each distinct
    /// text read once and matched folded, the hits in <see cref="EuropeSearchRanking"/>'s order, folded by wording when the
    /// census search names no date (<see cref="EuropeSearchFoldRule"/>), paged, and each hit of the page with its extract.
    /// <paramref name="census"/> is null for the original-wording search of an index that holds no census of the act.
    /// </summary>
    private V3PlatformOperationOutcome SearchEuropeWordings(
        V3PlatformOperationRequest request,
        string identifier,
        string language,
        string query,
        string[] terms,
        string? requestedDate,
        string? mode,
        string? after,
        int limit,
        DateTimeOffset observedAt,
        IReadOnlyList<EuropeSearchWording> wordings,
        (string Seed, string? RootWorkIri, bool ConsolidationsHeld, object NotSearched)? census,
        (string Basis, string Sentence, IReadOnlyList<string> ActDates) authenticity)
    {
        ArgumentNullException.ThrowIfNull(_europeReader);

        // Rights before any text is read: a wording whose articles do not all come from acquired members is not matched.
        var admitted = wordings.Where(wording => wording.Articles.Count > 0 && wording.Articles
            .Select(static article => article.ObjectRefSha256).Distinct(StringComparer.Ordinal)
            .All(objectRef => EuropeMemberOf(objectRef) is { Outcome: LexCorpus6OutcomeKind.Acquired })).ToArray();
        var withheld = wordings.Count - admitted.Length;

        // Each distinct text read once, by one of its articles, and matched once in each lane.
        var representatives = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var article in admitted.SelectMany(static wording => wording.Articles))
        {
            representatives.TryAdd(EuropeTextKey(article), article.ArticleIdentitySha256);
        }

        var read = _europeReader.ResolveArticleTexts(representatives.Values.ToArray());
        var texts = representatives.ToDictionary(
            static pair => pair.Key,
            pair => read.TryGetValue(pair.Value, out var text)
                ? text
                : throw new InvalidDataException("An EU article the index lists holds no searchable text row."),
            StringComparer.Ordinal);
        var wantStrict = mode is null || string.Equals(mode, "strict", StringComparison.Ordinal);
        var wantRelaxed = mode is null || string.Equals(mode, "relaxed", StringComparison.Ordinal);
        var relaxedIsStrict = terms.Length == 1 && string.Equals(terms[0], query, StringComparison.Ordinal);
        var strictNeedle = TextFolding.Fold(query);
        var relaxedNeedles = terms.Select(TextFolding.Fold).Where(TextFolding.HoldsSomething).Distinct(StringComparer.Ordinal).ToArray();
        var strictKeys = new HashSet<string>(StringComparer.Ordinal);
        var relaxedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, text) in texts)
        {
            var folded = TextFolding.Fold(text);
            if (wantStrict && TextFolding.CanMatch(strictNeedle) && folded.Contains(strictNeedle, StringComparison.Ordinal))
            {
                strictKeys.Add(key);
            }

            if (wantRelaxed && relaxedNeedles.Length > 0 &&
                relaxedNeedles.All(needle => TextFolding.CanMatch(needle) && folded.Contains(needle, StringComparison.Ordinal)))
            {
                relaxedKeys.Add(key);
            }
        }

        // The lanes, as Luxembourg's: with no mode the relaxed lane adds only what strict did not serve, and nothing when the
        // query is its only term.
        var measured = _europeReader.SearchableLanguages().Contains(language, StringComparer.Ordinal);
        var ordered = admitted
            .OrderBy(static wording => wording.Date ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static wording => wording.ExpressionId, StringComparer.Ordinal)
            .SelectMany(wording => wording.Articles.Select(article => (Wording: wording, Article: article, Key: EuropeTextKey(article))))
            .ToArray();
        IReadOnlyList<EuropeSearchHit> strict = !wantStrict || !measured ? [] : ordered
            .Where(entry => strictKeys.Contains(entry.Key))
            .Select(static entry => new EuropeSearchHit("strict", entry.Wording, entry.Article, entry.Key)).ToArray();
        IReadOnlyList<EuropeSearchHit> relaxed = !wantRelaxed || !measured || (mode is null && relaxedIsStrict) ? [] : ordered
            .Where(entry => relaxedKeys.Contains(entry.Key) && (mode is not null || !strictKeys.Contains(entry.Key)))
            .Select(static entry => new EuropeSearchHit("relaxed", entry.Wording, entry.Article, entry.Key)).ToArray();

        // With no date over the census, one hit per article id and text, under the latest wording that holds it.
        var folds = census is not null && requestedDate is null;
        var before = (Strict: strict.Count, Relaxed: relaxed.Count);
        var sameWording = new Dictionary<string, EuropeSearchHit[]>(StringComparer.Ordinal);
        if (folds)
        {
            strict = FoldEuropeHits(strict, sameWording);
            relaxed = FoldEuropeHits(relaxed, sameWording);
        }

        var all = strict.Concat(relaxed).ToArray();
        static string Cursor(EuropeSearchHit hit) => $"{hit.Lane}.{hit.Article.ArticleIdentitySha256}";
        var start = 0;
        if (after is not null)
        {
            var index = Array.FindIndex(all, hit => string.Equals(Cursor(hit), after, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestSchemaInvalid,
                    "The operation request's 'after' names no hit of this query; a cursor is the continue_after of the same query, scope and index.");
            }

            start = index + 1;
        }

        var page = all.Skip(start).Take(limit).ToArray();
        var truncated = start + page.Length < all.Length;
        var hitsByWording = all.GroupBy(static hit => hit.Wording.ExpressionId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        object WordingRow(EuropeSearchWording wording) => new
        {
            wording_date = wording.Date,
            kind = wording.Kind,
            publisher_work_id = wording.PublisherWorkId,
            celex = wording.Celex,
            publisher_expression_id = wording.ExpressionId,
            language = wording.Language,
            next_date = wording.NextDate,
            next_text_held = wording.NextTextHeld,
            wording_sha256 = wording.Sha256,
            permalink = wording.Permalink,
            hits = hitsByWording.GetValueOrDefault(wording.ExpressionId),
        };

        var hits = page.Select(hit =>
        {
            var text = texts[hit.TextKey];
            var extract = ExtractMember(text, hit.Lane, query, terms);
            if (hit.Article.TextSha256 is { } recorded &&
                !string.Equals(recorded, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), StringComparison.Ordinal))
            {
                // The index records the digest of the very text it searches; a difference is a damaged index, never an extract.
                throw new InvalidDataException($"The EU index's text digest for article {hit.Article.ArticleIdentitySha256} is not its text's.");
            }

            return new
            {
                lane = hit.Lane,
                match_reasons = new[] { hit.Lane == "strict" ? "exact_phrase" : "all_terms" },
                publisher_work_id = hit.Wording.PublisherWorkId,
                celex = hit.Wording.Celex,
                publisher_expression_id = hit.Wording.ExpressionId,
                language = hit.Wording.Language,
                wording_date = hit.Wording.Date,
                kind = hit.Wording.Kind,
                next_date = hit.Wording.NextDate,
                article_identity_sha256 = hit.Article.ArticleIdentitySha256,
                publisher_id = hit.Article.PublisherIdentifier,
                heading = hit.Article.Heading,
                // The provision coordinate of the wording's expression; the text is read at the permalink.
                resolve = new { identifier = hit.Wording.ExpressionId + "#lex-provision=" + Uri.EscapeDataString(hit.Article.PublisherIdentifier) },
                // The hash-pinned provision permalink verify checks (null when the wording has no single date).
                permalink = hit.Wording.Permalink is { } pinned ? EuropeProvisionPermalink(pinned, hit.Article.PublisherIdentifier) : null,
                extract,
                same_wording = !folds ? null : (sameWording.GetValueOrDefault(hit.Article.ArticleIdentitySha256) ?? []).Select(other => new
                {
                    wording_date = other.Wording.Date,
                    kind = other.Wording.Kind,
                    celex = other.Wording.Celex,
                    publisher_expression_id = other.Wording.ExpressionId,
                    article_identity_sha256 = other.Article.ArticleIdentitySha256,
                    permalink = other.Wording.Permalink is { } otherPin ? EuropeProvisionPermalink(otherPin, other.Article.PublisherIdentifier) : null,
                }).ToArray(),
            };
        }).ToArray();

        var single = admitted.Length == 1 ? admitted[0] : null;
        var articlesSearched = admitted.Sum(wording => wording.Articles.Count(article => texts[EuropeTextKey(article)].Length > 0));
        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_query = query,
            requested_language = language,
            requested_date = requestedDate,
            requested_identifier = identifier,
            requested_mode = mode,
            requested_after = after,
            publisher = "eu-eurlex",
            scope = census is null ? EuropeSearchOriginalScope : EuropeSearchScope,
            ranking = EuropeSearchRanking,
            matching = EuropeSearchMatching,
            rights_rule = EuropeSearchRightsRule,
            hit_unit = census is null ? EuropeSearchOriginalHitUnit : EuropeSearchHitUnit,
            lanes = SearchLanes,
            date_semantics = census is null ? EuropeWordingDateSemantics : EuropeStateDateSemantics,
            selection_rule = census is null ? null : EuropeSelectionRule,
            seed_celex = census?.Seed,
            original_work_id = census?.RootWorkIri,
            consolidations_held = census?.ConsolidationsHeld ?? false,
            text_served = true,
            acknowledgement = EuropeTextAcknowledgement,
            authenticity = authenticity.Sentence,
            authenticity_basis = authenticity.Basis,
            authenticity_act_dates = authenticity.ActDates,
            authenticity_rule = EuropeAuthenticityRule,
            searchable_text_held_for_language = measured,
            searchable_languages = _europeReader.SearchableLanguages(),
            modes_held = SearchModes,
            modes_not_held = new[] { "bm25", "semantic" },
            reference = ReferenceNotRead("not_run_identifier_given"),
            lanes_run = true,
            work_resolution = new
            {
                retrieval_lane = "r1_work_discovery",
                outcome = "not_run_identifier_given",
                work = (object?)null,
                candidates = (object?)null,
            },
            terms,
            population = new
            {
                scope = new { identifier, language, date = requestedDate, mode },
                strict_hits = wantStrict ? (int?)strict.Count : null,
                relaxed_hits = wantRelaxed ? (int?)relaxed.Count : null,
                distinct_publisher_articles = all.Select(static hit => hit.Article.PublisherIdentifier).Distinct(StringComparer.Ordinal).Count(),
                works_with_hits = all.Select(static hit => hit.Wording.PublisherWorkId).Distinct(StringComparer.Ordinal).Count(),
            },
            searched = new
            {
                publisher = "eu-eurlex",
                language,
                date = requestedDate,
                seed_celex = census?.Seed,
                wordings = admitted.Length,
                articles_with_searchable_text = articlesSearched,
                wordings_withheld_by_rights = withheld,
                not_searched = census?.NotSearched,
                rule = EuropeSearchedRule,
            },
            fold = !folds ? null : new
            {
                rule = EuropeSearchFoldRule,
                strict_hits_before_fold = wantStrict ? (int?)before.Strict : null,
                relaxed_hits_before_fold = wantRelaxed ? (int?)before.Relaxed : null,
            },
            ambiguous_works = Array.Empty<object>(),
            limit,
            truncated,
            continue_after = truncated ? Cursor(page[^1]) : null,
            page_is = SearchPageIs,
            extract_rule = SearchExtractRule,
            // The one wording searched, pinned, when one is (a date, or an act held in one wording); null when several are,
            // each hit then naming its own.
            pinned_wording = single is not { Permalink: not null } ? null : new
            {
                wording_date = single.Date,
                kind = single.Kind,
                next_date = single.NextDate,
                wording_sha256 = single.Sha256,
                permalink = single.Permalink,
                digest_rule = census is null ? EuropeWordingDigestRule : EuropeStateDigestRule,
            },
            // Each wording searched once, in the order its hits come in, with its hits over the whole result.
            wordings = admitted
                .OrderBy(static wording => wording.Date ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static wording => wording.ExpressionId, StringComparer.Ordinal)
                .Select(WordingRow).ToArray(),
            hits,
            not_held = (census is null ? EuropeSearchOriginalNotHeld : EuropeSearchNotHeld)
                .Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "quote",
                WithEuropePublication(result.RootElement, wordings.Select(static wording => wording.ExpressionId))));
    }

    /// <summary>
    /// One lane's hits folded by text (<see cref="EuropeSearchFoldRule"/>): grouped by publisher article id and text key, each
    /// group kept as the hit of its latest wording (then the greatest expression, so the choice is total), the others recorded
    /// by that hit's article identity; the kept hits in the lane's order again.
    /// </summary>
    private static EuropeSearchHit[] FoldEuropeHits(IReadOnlyList<EuropeSearchHit> hits, Dictionary<string, EuropeSearchHit[]> others)
    {
        var kept = new HashSet<EuropeSearchHit>();
        foreach (var group in hits.GroupBy(static hit => (hit.Article.PublisherIdentifier, hit.TextKey)))
        {
            var ordered = group.OrderBy(static hit => hit.Wording.Date ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static hit => hit.Wording.ExpressionId, StringComparer.Ordinal)
                .ToArray();
            kept.Add(ordered[^1]);
            others[ordered[^1].Article.ArticleIdentitySha256] = ordered[..^1];
        }

        return hits.Where(kept.Contains).ToArray();
    }
}
