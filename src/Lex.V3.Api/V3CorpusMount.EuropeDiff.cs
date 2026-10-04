using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Api;

/// <summary>
/// EU <c>diff</c> over the EU time view: two dated wordings of an EU act, compared article by article. Each bound is the wording
/// <c>as_of</c> answers on its date (<see cref="SelectEuropeAt"/>, the one selection rule every dated EU answer uses), per served
/// language; two different held texts for a bound's date refuse <c>ambiguous_version</c>, never chosen between. The comparison
/// is by the publisher's article identifier and the SHA-256 of each article's held text, as Luxembourg's is by publisher id and
/// wording digest: no text is quoted or diffed, and nothing about legal effect is asserted.
/// </summary>
internal sealed partial class V3CorpusMount
{
    /// <summary>What an EU comparison of two wordings says about itself.</summary>
    internal const string EuropeComparisonNote =
        "two dated EU wordings; each article is compared by the publisher's article identifier and the SHA-256 of its held text, " +
        "an article whose text this index does not hold on a side is text_not_held and never compared, and nothing about legal " +
        "effect is asserted";

    /// <summary>What an EU comparison says when both dates fall in one wording.</summary>
    internal const string EuropeSameWordingNote = "the same wording answers on both dates";

    /// <summary>
    /// EU <c>diff</c>. Both bounds are selected before anything is classified, so a bound with no wording never masks an
    /// ambiguity at the other: an ambiguity at either bound refuses the whole answer, the from bound's first. A language that
    /// answers at one bound only is listed as not compared, with the bound it misses; when no language answers at both bounds,
    /// the refusal is the one <c>as_of</c> gives for the first such language at the bound it misses.
    /// </summary>
    private V3PlatformOperationOutcome DiffEurope(
        V3PlatformOperationRequest request,
        string identifier,
        string seed,
        string dateFrom,
        string dateTo,
        string? requestedLanguage,
        DateTimeOffset observedAt)
    {
        var timeline = EuropeTimelineOf(seed);
        var fromRefused = SelectEuropeAt(request, timeline, dateFrom, requestedLanguage, observedAt, out var fromAnswered);
        var toRefused = SelectEuropeAt(request, timeline, dateTo, requestedLanguage, observedAt, out var toAnswered);
        foreach (var refused in new[] { fromRefused, toRefused })
        {
            if (refused?.Refusal?.Code is "ambiguous_version" or "language_not_available")
            {
                return refused;
            }
        }

        var served = requestedLanguage is null ? timeline.Languages : new[] { requestedLanguage };
        var comparisons = new List<object>();
        var notCompared = new List<object>();
        (string Language, string Date)? firstMissing = null;
        foreach (var language in served)
        {
            var from = fromAnswered.FirstOrDefault(selection => string.Equals(selection.Language, language, StringComparison.Ordinal));
            var to = toAnswered.FirstOrDefault(selection => string.Equals(selection.Language, language, StringComparison.Ordinal));
            if (from is null || to is null)
            {
                var (bound, date) = from is null ? ("from", dateFrom) : ("to", dateTo);
                firstMissing ??= (language, date);
                var selection = EuropeSelectAt(timeline.ByLanguage[language], date);
                notCompared.Add(new
                {
                    language,
                    bound,
                    reason = selection.Entry is null
                        ? "no wording dated at or before the date"
                        : "the wording of that date is not held in this language",
                });
                continue;
            }

            var fromEntry = from.Entry!;
            var toEntry = to.Entry!;
            if (string.Equals(fromEntry.Date, toEntry.Date, StringComparison.Ordinal))
            {
                comparisons.Add(new
                {
                    language,
                    from = EuropeDatedRow(seed, fromEntry, from.NextDate),
                    to = EuropeDatedRow(seed, toEntry, to.NextDate),
                    same_wording = true,
                    note = EuropeSameWordingNote,
                    articles = (object?)null,
                    counts = (object?)null,
                });
                continue;
            }

            var (articles, counts) = CompareEuropeArticles(
                EuropeArticlesOf(fromEntry.Answer!.ExpressionId), EuropeArticlesOf(toEntry.Answer!.ExpressionId));
            comparisons.Add(new
            {
                language,
                from = EuropeDatedRow(seed, fromEntry, from.NextDate),
                to = EuropeDatedRow(seed, toEntry, to.NextDate),
                same_wording = false,
                note = EuropeComparisonNote,
                articles = (object?)articles,
                counts = (object?)counts,
            });
        }

        if (comparisons.Count == 0)
        {
            // No language answers at both bounds: the refusal as_of gives that language at the bound it misses (no wording
            // dated by then, or its text not held there), so the payload speaks for one language and one date.
            var (language, date) = firstMissing!.Value;
            return SelectEuropeAt(request, timeline, date, language, observedAt, out _)
                ?? throw new InvalidOperationException("A language missing a bound was answered at that bound.");
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            requested_date_from = dateFrom,
            requested_date_to = dateTo,
            requested_language = requestedLanguage,
            publisher = EuropePermalinkPublisher,
            seed_celex = seed,
            original_work_id = timeline.RootWorkIri,
            comparisons,
            languages_not_compared = notCompared,
            available_languages = timeline.Languages,
            date_semantics = EuropeStateDateSemantics,
            selection_rule = EuropeSelectionRule,
            digest_rule = EuropeStateDigestRule,
            not_held = EuropeTimeNotHeld.Select(static row => new { item = row[0], reason = row[1] }).ToArray(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _europeReader!.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationResult(request, "diff", result.RootElement));
    }

    /// <summary>
    /// Article-level comparison of two EU wordings by the publisher's article identifier: present in both with the same text
    /// digests is unchanged, with different digests changed; only in the later wording added; only in the earlier removed; held
    /// on both sides with no text on either is <c>text_not_held</c>, since the digest of a text not held compares nothing. An
    /// identifier the publisher gives twice in one wording compares as its ordered digests. A text digest is the SHA-256 of the
    /// article's held text as UTF-8, the digest an EU evidence bundle states for the text it quotes, and null when the index
    /// holds the article without text (the bundle lists it under <c>articles_without_text</c>).
    /// </summary>
    private static (IReadOnlyList<object> Articles, object Counts) CompareEuropeArticles(
        IReadOnlyList<EuropeIndexArticleText> from, IReadOnlyList<EuropeIndexArticleText> to)
    {
        static Dictionary<string, EuropeIndexArticleText[]> ById(IReadOnlyList<EuropeIndexArticleText> articles) =>
            articles.GroupBy(static article => article.PublisherIdentifier, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        static string? TextSha256(EuropeIndexArticleText article) =>
            article.Text.Length == 0 ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(article.Text)));
        static object[] Side(EuropeIndexArticleText[]? side) =>
            side is null
                ? []
                : side.Select(static article => (object)new
                {
                    article_identity_sha256 = article.ArticleIdentitySha256,
                    wording_sha256 = TextSha256(article),
                }).ToArray();

        var left = ById(from);
        var right = ById(to);
        var rows = new List<object>();
        int unchanged = 0, changed = 0, added = 0, removed = 0, textNotHeld = 0;
        foreach (var id in left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(id, out var before);
            right.TryGetValue(id, out var after);
            string status;
            if (before is null)
            {
                status = "added"; added++;
            }
            else if (after is null)
            {
                status = "removed"; removed++;
            }
            else if (before.Concat(after).Any(static article => article.Text.Length == 0))
            {
                status = "text_not_held"; textNotHeld++;
            }
            else if (before.Select(TextSha256).SequenceEqual(after.Select(TextSha256), StringComparer.Ordinal))
            {
                status = "unchanged"; unchanged++;
            }
            else
            {
                status = "changed"; changed++;
            }

            rows.Add(new { publisher_id = id, status, from = Side(before), to = Side(after) });
        }

        return (rows, new { unchanged, changed, added, removed, text_not_held = textNotHeld });
    }
}
