using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Index;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Api;

/// <summary>
/// The EU act's titles and Official Journal references (Release 3's item 1), as the EU index /7 holds them: the publisher's own
/// title and short title of each English or French expression (its metadata record, family X), the title of each admitted
/// Formex package (its markup removed), and the Official Journal reference each admitted package states (series, issue or act
/// number, year, first page, publication date). <c>dossier</c>, <c>as_of</c>, <c>evidence_bundle</c> and an EU <c>search</c>
/// say them for the expressions they list, and the not-held rows say only what is still not held; a search that names no work
/// also matches the EU acts' titles. Over an EU index /6 or older every answer is unchanged, byte for byte. This platform
/// composes no citation from the reference: the site's citation generator does.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string EuropeTitlesRule =
        "titles are the publisher's own, each with its source: the title and short title of each English or French expression listed, as " +
        "the publisher's metadata record states them (cdm:expression_title and cdm:expression_title_short, acquired by the EU census, each " +
        "literal with its white space made single spaces), and the title of the expression's admitted Formex package (its TITLE: " +
        EuFormexPublicationReader.TitleTextRule + "); the index keeps every raw form a title was read from; a title is never a date and none " +
        "is chosen between; expressions_without_title lists the expressions listed here of which no title is held";

    internal const string EuropeOfficialJournalRule =
        "each reference is read from the bibliographic markup of the expression's admitted Formex package and nothing else: kind " +
        "act_publication is the act's own publication in the Official Journal (the main unit's DOCUMENT.REF, with the publication date of " +
        "the issue its document record names when that record agrees with the reference); kind consolidated_act_publication is the " +
        "publication of the act a consolidated version consolidates (the base act's DOCUMENT.REF.CONS, with the date its bibliography " +
        "gives it), since a consolidated text is not itself published in the Official Journal; number_form series_number gives the series, " +
        "the issue number and the first page as served (the Official Journal before October 2023), act_number the series and the act's own " +
        "number (the Official Journal published act by act since October 2023); a field the package does not state is null; no citation " +
        "is composed here; expressions_without_reference lists the expressions listed here of which no reference is held";

    internal const string EuropeTitlesNotHeldReason =
        "no title is held of the expressions under titles.expressions_without_title: neither the publisher's metadata record of the " +
        "expression, as acquired, nor an admitted Formex package of it states one";

    internal const string EuropeOfficialJournalNotHeldReason =
        "no Official Journal reference is held of the expressions under official_journal.expressions_without_reference: a reference is " +
        "read only from an expression's admitted Formex package, and no admitted package of it states one";

    internal const string EuropeTitleSearchRule =
        "with no work named, the query is also matched against the titles of the EU acts the mounted EU index holds: every title of every " +
        "English and French expression of each act's census works, in either language; each act a title matches is listed once, by its " +
        "CELEX, with the title that matched, its language, kind and source, whole titles first and then in CELEX order; an act is listed " +
        "and not opened, and none of its text is searched here (name the act to search its text); an act withheld under R92 is not listed";

    private const string EuropeAuthenticityRuleOpening =
        "the sentence is chosen by the act's own date, since this index holds no Official Journal publication date:";

    /// <summary>
    /// <see cref="EuropeAuthenticityRule"/> over an EU index that holds Official Journal references: the same choice, and an
    /// opening that no longer says no publication date is held.
    /// </summary>
    internal static readonly string EuropeAuthenticityRuleWithJournal = EuropeAuthenticityRule.Replace(EuropeAuthenticityRuleOpening,
        "the sentence is chosen by the act's own date, and not by the publication date an Official Journal reference under " +
        "official_journal gives:", StringComparison.Ordinal);

    internal const string EuropeTitleMatching =
        "a title matches when the query, folded, is found in the title's words, folded alike (whole_title when the two are the same, " +
        "phrase_in_title otherwise); " + TextFolding.Rule;

    /// <summary>
    /// An EU answer with the titles and Official Journal references of <paramref name="expressions"/> (the expressions it lists):
    /// when the EU index holds them (/7), <c>titles</c> and <c>official_journal</c> are added before <c>not_held</c>, the not-held
    /// row <c>titles</c> is removed, and a <c>titles</c> or <c>official_journal</c> row is added only when one of the expressions has
    /// none (<see cref="EuropeTitlesNotHeldReason"/>, <see cref="EuropeOfficialJournalNotHeldReason"/>), in the removed row's place
    /// or at the end; otherwise the answer is returned as it was, byte for byte.
    /// </summary>
    private JsonElement WithEuropePublication(JsonElement answer, IEnumerable<string> expressions)
    {
        if (_europeReader is not { HasTitles: true })
        {
            return answer;
        }

        var scope = expressions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var titles = _europeReader.ReadTitles(scope);
        var references = _europeReader.ReadOfficialJournalReferences(scope);
        var withoutTitle = scope.Where(expression => titles.All(row => row.PublisherExpressionIri != expression)).ToArray();
        var withoutReference = scope.Where(expression => references.All(row => row.PublisherExpressionIri != expression)).ToArray();
        var titlesMember = new
        {
            rule = EuropeTitlesRule,
            entries = titles.Select(static row => new
            {
                publisher_expression_id = row.PublisherExpressionIri,
                publisher_work_id = row.PublisherWorkIri,
                language = row.Language,
                kind = row.TitleKind,
                source = row.Source,
                text = row.TitleText,
                title_identity_sha256 = row.TitleIdentitySha256,
            }).ToArray(),
            expressions_without_title = withoutTitle,
        };
        var journalMember = new
        {
            rule = EuropeOfficialJournalRule,
            references = references.Select(static row => new
            {
                publisher_expression_id = row.PublisherExpressionIri,
                publisher_work_id = row.PublisherWorkIri,
                language = row.Language,
                kind = row.Kind,
                number_form = row.NumberForm,
                series = row.Series,
                oj_number = row.OjNumber,
                act_number = row.ActNumber,
                year = row.Year,
                first_page = row.FirstPage,
                publication_date = row.PublicationDate,
                package_sha256 = row.PackageSha256,
                package_entry = row.PackageEntry,
                reference_identity_sha256 = row.ReferenceIdentitySha256,
            }).ToArray(),
            expressions_without_reference = withoutReference,
        };

        var source = JsonNode.Parse(answer.GetRawText())!.AsObject();
        var target = new JsonObject();
        foreach (var (key, value) in source.ToArray())
        {
            // The authenticity rule no longer says the index holds no publication date: it holds the reference, and does not
            // choose the sentence by it.
            if (string.Equals(key, "authenticity_rule", StringComparison.Ordinal) &&
                string.Equals((string?)value, EuropeAuthenticityRule, StringComparison.Ordinal))
            {
                target[key] = EuropeAuthenticityRuleWithJournal;
                continue;
            }

            if (!string.Equals(key, "not_held", StringComparison.Ordinal))
            {
                target[key] = value?.DeepClone();
                continue;
            }

            target["titles"] = JsonSerializer.SerializeToNode(titlesMember);
            target["official_journal"] = JsonSerializer.SerializeToNode(journalMember);
            var added = new List<JsonNode>();
            if (withoutTitle.Length > 0)
            {
                added.Add(JsonSerializer.SerializeToNode(new { item = "titles", reason = EuropeTitlesNotHeldReason })!);
            }

            if (withoutReference.Length > 0)
            {
                added.Add(JsonSerializer.SerializeToNode(new { item = "official_journal", reason = EuropeOfficialJournalNotHeldReason })!);
            }

            var rows = new JsonArray();
            var placed = false;
            foreach (var row in value!.AsArray())
            {
                var item = (string?)row!["item"];
                if (string.Equals(item, "titles", StringComparison.Ordinal) || string.Equals(item, "official_journal", StringComparison.Ordinal))
                {
                    if (!placed)
                    {
                        foreach (var node in added) rows.Add(node.DeepClone());
                        placed = true;
                    }

                    continue;
                }

                rows.Add(row!.DeepClone());
            }

            if (!placed)
            {
                foreach (var node in added) rows.Add(node.DeepClone());
            }

            target[key] = rows;
        }

        if (!target.ContainsKey("titles"))
        {
            throw new InvalidOperationException("An EU answer that says the act's titles carries a not_held list.");
        }

        return JsonSerializer.SerializeToElement(target);
    }

    /// <summary>
    /// The EU acts whose titles a search that names no work matches (<see cref="EuropeTitleSearchRule"/>), or null when the EU index
    /// holds no title (an index /6 or older) or no census, so the Luxembourg answer is unchanged.
    /// </summary>
    private object? EuropeTitleResolution(string query, string language)
    {
        if (_europeReader is not { HasTitles: true } || !HasEuropeCensus())
        {
            return null;
        }

        var matches = _europeReader.SearchTitles(query);
        if (matches.Count == 0)
        {
            return EuropeTitleResolutionOf([]);
        }

        // Each census work's seeds and each seed's original work, from the states alone: no act's wordings are read to list it.
        IReadOnlyList<EuropeIndexState> states;
        lock (_europeTimeGate)
        {
            states = EuropeStatesLocked();
        }

        var seedsOfWork = states.GroupBy(static state => state.PublisherWorkIri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static state => state.SeedCelex).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var rootOfSeed = states.GroupBy(static state => state.SeedCelex, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().RootWorkIri, StringComparer.Ordinal);
        var acts = new List<(string Seed, string? Root, EuropeIndexTitleMatch Match)>();
        foreach (var group in matches
                     .SelectMany(match => (seedsOfWork.TryGetValue(match.Title.PublisherWorkIri, out var seeds) ? seeds : [])
                         .Select(seed => (Seed: seed, Match: match)))
                     .GroupBy(static entry => entry.Seed, StringComparer.Ordinal))
        {
            // R92: an act the names record withholds is never listed, by any title.
            if (WithheldEurope(group.Key) is not null)
            {
                continue;
            }

            var root = rootOfSeed.GetValueOrDefault(group.Key);
            var best = group.Select(static entry => entry.Match)
                .OrderByDescending(static match => match.WholeTitle)
                .ThenByDescending(match => string.Equals(match.Title.PublisherWorkIri, root, StringComparison.Ordinal))
                .ThenByDescending(match => string.Equals(match.Title.Language, language, StringComparison.Ordinal))
                .ThenBy(static match => EuropeTitleSources.InOrder.ToList().IndexOf(match.Title.Source))
                .ThenBy(static match => match.Title.TitleIdentitySha256, StringComparer.Ordinal)
                .First();
            acts.Add((group.Key, root, best));
        }

        return EuropeTitleResolutionOf(acts);
    }

    /// <summary>The <c>europe_titles</c> member for the acts found: whole titles first, then in CELEX order.</summary>
    private static object EuropeTitleResolutionOf(IReadOnlyList<(string Seed, string? Root, EuropeIndexTitleMatch Match)> acts)
    {
        var listed = acts.OrderByDescending(static act => act.Match.WholeTitle)
            .ThenBy(static act => act.Seed, StringComparer.Ordinal)
            .Select(static act => new
            {
                seed_celex = act.Seed,
                original_work_id = act.Root,
                matched_title = act.Match.Title.TitleText,
                matched_title_language = act.Match.Title.Language,
                title_kind = act.Match.Title.TitleKind,
                title_source = act.Match.Title.Source,
                match_reason = act.Match.WholeTitle ? "whole_title" : "phrase_in_title",
                dossier = new { identifier = act.Seed },
            })
            .ToArray();
        return new
        {
            retrieval_lane = "eu_title_discovery",
            outcome = listed.Length == 0 ? "no_title_match" : listed.Length == 1 ? "one_act" : "several_acts",
            acts = listed,
            matching = EuropeTitleMatching,
            rule = EuropeTitleSearchRule,
        };
    }

    /// <summary>
    /// A Luxembourg search answer with <c>europe_titles</c>, the EU acts its query names by title (<see cref="EuropeTitleResolution"/>),
    /// after its <c>work_resolution</c>; the answer as it was when the EU index holds no title.
    /// </summary>
    private JsonElement WithEuropeTitleResolution(JsonElement answer, string query, string language)
    {
        var resolution = EuropeTitleResolution(query, language);
        if (resolution is null)
        {
            return answer;
        }

        var source = JsonNode.Parse(answer.GetRawText())!.AsObject();
        var target = new JsonObject();
        foreach (var (key, value) in source.ToArray())
        {
            target[key] = value?.DeepClone();
            if (string.Equals(key, "work_resolution", StringComparison.Ordinal))
            {
                target["europe_titles"] = JsonSerializer.SerializeToNode(resolution);
            }
        }

        if (!target.ContainsKey("europe_titles"))
        {
            throw new InvalidOperationException("A search answer that lists EU acts by title carries a work_resolution.");
        }

        return JsonSerializer.SerializeToElement(target);
    }
}
