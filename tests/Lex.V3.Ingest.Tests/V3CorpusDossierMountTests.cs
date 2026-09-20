using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The work record, driven through the real handler on a verified mount: the titles the publisher stated
/// for one work, its publisher-dated states in <c>timeline</c>'s vocabulary, and a fixed list of what the
/// mounted index does not hold, with every refusal the closed registry names for it. The record is what
/// this corpus holds, so the tests hold the words that say so, the list of what is not held, and that the
/// states agree with <c>timeline</c> field for field.
/// </summary>
[TestClass]
public sealed class V3CorpusDossierMountTests
{
    private const string DossierRawTarget = "/api/v3/dossier";
    private const string TimelineRawTarget = "/api/v3/timeline";

    private static readonly string[] NotHeldItems =
    [
        "document_type", "current_state_flag", "publication_date", "entry_into_force", "application",
        "historical_identifiers", "responsible_ministry", "first_observed", "coverage_gaps",
    ];

    [TestMethod]
    public async Task AWorkWithNoTitlesHeldIsARecordOfItsStatesWithAnEmptyTitleList()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}");

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("dossier", envelope.OperationId);
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
        Assert.AreEqual("lu", envelope.Context.Jurisdiction);
        Assert.AreEqual(TimelineSemantics.PublisherApplicability, envelope.Context.TimelineSemantics);
        Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256);
        Assert.AreEqual("work_record", envelope.Result!.ObjectType);
        var value = envelope.Result.Value;
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", value.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(JsonValueKind.Null, value.GetProperty("requested_language").ValueKind);
        Assert.AreEqual("lu-legilux", value.GetProperty("publisher").GetString());
        Assert.AreEqual(fixture.WorkKey, value.GetProperty("work_key").GetString());
        Assert.AreEqual(PublisherWorkIriOf(fixture), value.GetProperty("publisher_work_iri").GetString());
        CollectionAssert.AreEqual(new[] { "fra" },
            value.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual(0, value.GetProperty("titles").GetArrayLength(),
            "A work the index holds no title for says so with an empty list, never with an invented title.");
        Assert.AreEqual(1, value.GetProperty("state_count").GetInt32());
        Assert.AreEqual(fixture.ApplicabilityDate, value.GetProperty("history_begins").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, value.GetProperty("latest_applicability_date").GetString());
        Assert.AreEqual(fixture.CorpusSha256, value.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(fixture.IndexSha256, value.GetProperty("index_sha256").GetString());
        var row = value.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("fra", row.GetProperty("language").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, row.GetProperty("applicability_date").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("next_applicability_date").ValueKind,
            "The last publisher-dated state has no next date; no end is invented.");
        Assert.AreEqual(fixture.StateSha256, row.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.ExpressionIri, row.GetProperty("expression_iri").GetString());
        Assert.AreEqual(fixture.StableCoordinate, row.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(fixture.Permalink, row.GetProperty("permalink").GetString());
        Assert.AreEqual(value.GetProperty("publisher_work_iri").GetString(), row.GetProperty("publisher_work_iri").GetString());
        Assert.IsGreaterThan(0, row.GetProperty("article_count").GetInt32());
        // The scope sentence is what tells a reader whose record this is.
        Assert.AreEqual(
            "The titles and the publisher-dated states the mounted index holds for this work. It is a record of what this corpus holds and not of what the publisher holds: " +
            "a title or a state absent here may exist at the publisher, and absence from this corpus is neither absence from the publisher's record nor absence of law. " +
            "When a language is requested, the titles, the states, the state count and the first and latest applicability dates are that language's alone, and available_languages still lists every language the work has.",
            value.GetProperty("scope").GetString());
    }

    [TestMethod]
    public async Task TitlesAreTheOnesHeldForThisWorkOnlyGroupedByExpressionInOrderWithTheirEvidenceAndAShortTitleListEvenWhenEmpty()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var german = await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        // A second French expression: a title is read for an expression, so the same language has two groups.
        var later = await fixture.AddStateAsync(Shift(fixture.ApplicabilityDate, 400), "later");
        const string otherWork = "http://data.legilux.public.lu/eli/etat/leg/loi/2099/01/01/n1";
        // Given out of order on purpose: the answer's order is the reader's, not the insert's. The title
        // table's own key is the article's wId or the expression's IRI and names no work, so a title belongs
        // to this work by its expression: the French short title is keyed by the expression's IRI, one French
        // title is stored a second time under another key (a title is stored once for each article it was
        // matched to) and is served once, the German expression has no short title, and one title belongs to
        // another work's expression and must not appear.
        await fixture.AddTitleRowsAsync(
            ("another-article-key", fixture.ExpressionIri, "fra", "title", "Loi longue", new string('a', 64)),
            (fixture.ExpressionIri, fixture.ExpressionIri, "fra", "title_short", "Loi courte", new string('b', 64)),
            (null, german.ExpressionIri, "deu", "title", "Langes Gesetz", new string('c', 64)),
            (null, later.ExpressionIri, "fra", "title", "Loi longue, modifiee", new string('d', 64)),
            (null, fixture.ExpressionIri, "fra", "title", "Loi longue, autre lecture", new string('e', 64)),
            (null, fixture.ExpressionIri, "fra", "title", "Loi longue", new string('a', 64)),
            (otherWork, otherWork + "/fra", "fra", "title", "Autre loi", new string('f', 64)));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var value = (await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;

        var groups = value.GetProperty("titles").EnumerateArray().ToArray();
        Assert.HasCount(3, groups, "One group per language and expression; the other work's title is not among them.");
        // Sorted by language, then by expression IRI, and not in the order they were stored.
        CollectionAssert.AreEqual(
            new[] { ("deu", german.ExpressionIri), ("fra", fixture.ExpressionIri), ("fra", later.ExpressionIri) }
                .OrderBy(static key => key.Item1, StringComparer.Ordinal).ThenBy(static key => key.Item2, StringComparer.Ordinal).ToArray(),
            groups.Select(static group => (group.GetProperty("language").GetString()!, group.GetProperty("expression_iri").GetString()!)).ToArray());
        var byExpression = groups.ToDictionary(static group => group.GetProperty("expression_iri").GetString()!, StringComparer.Ordinal);
        CollectionAssert.AreEqual(
            new[] { "Langes Gesetz|" + new string('c', 64) },
            Rows(byExpression[german.ExpressionIri], "titles"));
        Assert.AreEqual(JsonValueKind.Array, byExpression[german.ExpressionIri].GetProperty("short_titles").ValueKind,
            "An expression with no short title held still carries the list, empty, and never omits it.");
        Assert.AreEqual(0, byExpression[german.ExpressionIri].GetProperty("short_titles").GetArrayLength());
        CollectionAssert.AreEqual(
            new[] { "Loi longue|" + new string('a', 64), "Loi longue, autre lecture|" + new string('e', 64) },
            Rows(byExpression[fixture.ExpressionIri], "titles"),
            "Two titles of one kind for one expression are both served, in the words' order, each with its own evidence, and a copy stored under another key is not a third.");
        CollectionAssert.AreEqual(
            new[] { "Loi courte|" + new string('b', 64) },
            Rows(byExpression[fixture.ExpressionIri], "short_titles"));
        CollectionAssert.AreEqual(
            new[] { "Loi longue, modifiee|" + new string('d', 64) },
            Rows(byExpression[later.ExpressionIri], "titles"),
            "The same language, another expression, its own title.");
        Assert.AreEqual(0, byExpression[later.ExpressionIri].GetProperty("short_titles").GetArrayLength());
        Assert.IsFalse(value.GetRawText().Contains("Autre loi", StringComparison.Ordinal),
            "A title held for another work's expression is not this work's.");

        // A language filter narrows the titles and the states together and leaves the work's languages whole.
        var germanOnly = (await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "deu")).Result!.Value;
        Assert.AreEqual("deu", germanOnly.GetProperty("requested_language").GetString());
        var germanTitles = germanOnly.GetProperty("titles").EnumerateArray().Single();
        Assert.AreEqual("deu", germanTitles.GetProperty("language").GetString());
        Assert.AreEqual(1, germanOnly.GetProperty("state_count").GetInt32());
        Assert.AreEqual(german.StateSha256,
            germanOnly.GetProperty("states").EnumerateArray().Single().GetProperty("state_sha256").GetString());
        CollectionAssert.AreEqual(new[] { "deu", "fra" },
            germanOnly.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
        var frenchOnly = (await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "fra")).Result!.Value;
        Assert.AreEqual(2, frenchOnly.GetProperty("titles").GetArrayLength());
        Assert.IsTrue(frenchOnly.GetProperty("titles").EnumerateArray().All(static group => group.GetProperty("language").GetString() == "fra"));
    }

    [TestMethod]
    public async Task ADossierAndATimelineNameTheSameStatesWithTheSameFieldsForOneRequest()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var german = await fixture.AddSecondLanguageStateAsync(laterDate);
        var later = await fixture.AddStateAsync(laterDate, "later");
        var latestDate = Shift(fixture.ApplicabilityDate, 800);
        var latest = await fixture.AddStateAsync(latestDate, "latest");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var language in new string?[] { null, "fra", "deu" })
        {
            var dossier = (await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}", language)).Result!.Value;
            var timeline = (await TimelineAsync(mount, $"/lu-legilux/{fixture.WorkKey}", language)).Result!.Value;

            // The work-level facts both operations state.
            foreach (var name in new[]
                     {
                         "requested_identifier", "requested_language", "publisher", "work_key", "history_begins", "state_count",
                         "available_languages", "corpus_sha256", "index_sha256",
                     })
            {
                Assert.AreEqual(timeline.GetProperty(name).GetRawText(), dossier.GetProperty(name).GetRawText(), $"{language}: {name}");
            }

            var dossierRows = dossier.GetProperty("states").EnumerateArray().ToArray();
            var timelineRows = timeline.GetProperty("states").EnumerateArray().ToArray();
            Assert.HasCount(timelineRows.Length, dossierRows, language);
            Assert.IsGreaterThan(0, dossierRows.Length, "A comparison over no state proves nothing.");
            Assert.AreEqual(timelineRows[^1].GetProperty("applicability_date").GetString(),
                dossier.GetProperty("latest_applicability_date").GetString(), language);

            // A row is `timeline`'s row without the article list and with its count: every key it carries
            // is a key `timeline` carries, or the count, and each shared key holds the very same value.
            var shared = 0;
            for (var index = 0; index < dossierRows.Length; index++)
            {
                var timelineKeys = timelineRows[index].EnumerateObject().Select(static p => p.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var property in dossierRows[index].EnumerateObject())
                {
                    if (property.Name == "article_count")
                    {
                        Assert.AreEqual(
                            timelineRows[index].GetProperty("article_identities").GetArrayLength(),
                            property.Value.GetInt32(),
                            $"{language} row {index}: the count is the length of the list `timeline` serves");
                        continue;
                    }

                    Assert.IsTrue(timelineKeys.Contains(property.Name), $"{language} row {index}: `{property.Name}` is not a field `timeline` carries");
                    Assert.AreEqual(timelineRows[index].GetProperty(property.Name).GetRawText(), property.Value.GetRawText(), $"{language} row {index}: {property.Name}");
                    shared += 1;
                }
            }

            // The comparison is not empty: nine shared fields on every row.
            Assert.AreEqual(9 * dossierRows.Length, shared, language);
        }

        // Every state the fixture holds is in the unfiltered answer, in the reader's order.
        var all = (await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;
        CollectionAssert.AreEqual(
            new[] { fixture.StateSha256, german.StateSha256, later.StateSha256, latest.StateSha256 },
            all.GetProperty("states").EnumerateArray().Select(static row => row.GetProperty("state_sha256").GetString()).ToArray());
    }

    private static void CollectPaths(JsonElement element, string prefix, SortedSet<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                    paths.Add(path);
                    CollectPaths(property.Value, path, paths);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPaths(item, prefix + "[]", paths);
                }

                break;
        }
    }

    [TestMethod]
    public async Task TheAnswerHasExactlyTheseProperties_SoAFieldOfAnyNameFailsUntilItIsDeclared()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var german = await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        // Both kinds of title in both groups, so every object the answer can carry is walked.
        await fixture.AddTitleRowsAsync(
            (null, fixture.ExpressionIri, "fra", "title", "Loi longue", new string('a', 64)),
            (null, fixture.ExpressionIri, "fra", "title_short", "Loi courte", new string('b', 64)),
            (null, german.ExpressionIri, "deu", "title", "Langes Gesetz", new string('c', 64)));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        CollectPaths(body, string.Empty, paths);
        var declared = new[]
        {
            "available_languages",
            "corpus_sha256",
            "history_begins",
            "index_sha256",
            "latest_applicability_date",
            "not_held", "not_held[].item", "not_held[].reason",
            "publisher",
            "publisher_work_iri",
            "requested_identifier",
            "requested_language",
            "scope",
            "state_count",
            "states", "states[].applicability_date", "states[].article_count", "states[].expression_iri", "states[].language",
            "states[].next_applicability_date", "states[].permalink", "states[].publisher_legal_resource_iri",
            "states[].publisher_work_iri", "states[].stable_coordinate", "states[].state_sha256",
            "titles", "titles[].expression_iri", "titles[].language",
            "titles[].short_titles", "titles[].short_titles[].evidence_sha256", "titles[].short_titles[].title",
            "titles[].titles", "titles[].titles[].evidence_sha256", "titles[].titles[].title",
            "work_key",
        }.Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(declared, paths.ToArray(), "The answer carries a property that is not declared, or lost one that is.");
        // Every array the paths walked held a row, so a missing row-shape is not hiding behind an empty list.
        foreach (var name in new[] { "titles", "states", "not_held" })
        {
            Assert.IsGreaterThan(0, body.GetProperty(name).GetArrayLength(), name);
        }

        // The document date the index stores beside a title is not the publisher's, and is not served.
        Assert.IsFalse(paths.Any(static path => path.Contains("document_date", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task WhatIsNotHeldIsAFixedListInFixedWordsAndNoItemOnItIsAlsoAPropertyOfTheAnswer()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        JsonElement bare;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            bare = (await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;
        }

        await fixture.AddTitleRowsAsync((null, fixture.ExpressionIri, "fra", "title", "Loi longue", new string('a', 64)));
        using var titled = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(titled);
        var withTitle = (await DossierAsync(titled, $"/lu-legilux/{fixture.WorkKey}")).Result!.Value;

        foreach (var value in new[] { bare, withTitle })
        {
            var notHeld = value.GetProperty("not_held").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(NotHeldItems, notHeld.Select(static row => row.GetProperty("item").GetString()).ToArray());
            // The reasons are what a reader reads, so they are pinned in these words and not by their length.
            CollectionAssert.AreEqual(
                new[]
                {
                    "no publisher document type is held, so nothing here says whether this work is a law, a grand-ducal regulation or an order",
                    "no current-state flag is held, so nothing here says whether the publisher treats this work as in force, and a flag about now would not be a statement about any date listed here",
                    "no publication date is held; the document date the index stores beside a title falls back to an article's applicability date, so it is not served as one",
                    "no entry-into-force date is held; a state's applicability date is the date that state applies from, which is a different fact",
                    "no application date is held; a state's applicability date is the date that state applies from, which is a different fact",
                    "no historical identifier is held, so no earlier or later identifier of this work is mapped to it",
                    "no responsible ministry is held",
                    "no observation time or first-sighting event is held, so nothing here says when this work was first seen",
                    "each state carries the date it applies from and no end date, so no gap between states can be stated, and this answer never says there is none",
                },
                notHeld.Select(static row => row.GetProperty("reason").GetString()).ToArray());
        }

        // A row cannot outlive its truth: nothing the list says is absent is a property of the answer.
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        CollectPaths(withTitle, string.Empty, paths);
        foreach (var item in NotHeldItems)
        {
            Assert.IsFalse(
                paths.Any(path => path.Split('.', '[', ']').Contains(item, StringComparer.Ordinal)),
                $"`{item}` is said not to be held and is also a property of the answer.");
        }

        // And no state row carries an end, a force, a gap or an overlap.
        var row = withTitle.GetProperty("states").EnumerateArray().Single();
        foreach (var forbidden in new[] { "valid_to", "end_date", "in_force", "gap", "overlap" })
        {
            Assert.IsFalse(row.TryGetProperty(forbidden, out _), forbidden);
        }
    }

    [TestMethod]
    public async Task PublisherWorkIriAndStableWorkCoordinateNameTheSameWork()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddTitleRowsAsync((null, fixture.ExpressionIri, "fra", "title", "Loi longue", new string('a', 64)));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var byIri = await DossierAsync(mount, PublisherWorkIriOf(fixture));
        var byCoordinate = await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}");
        var byOrigin = await DossierAsync(mount, $"https://law.soufien.lu/lu-legilux/{fixture.WorkKey}");

        foreach (var envelope in new[] { byIri, byCoordinate, byOrigin })
        {
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
            Assert.AreEqual(fixture.StateSha256,
                envelope.Result!.Value.GetProperty("states").EnumerateArray().Single().GetProperty("state_sha256").GetString());
            Assert.AreEqual(1, envelope.Result.Value.GetProperty("titles").GetArrayLength());
        }

        foreach (var foreign in new[]
                 {
                     $"https://any-host.example/lu-legilux/{fixture.WorkKey}",
                     $"http://law.soufien.lu/lu-legilux/{fixture.WorkKey}",
                     $"https://law.soufien.lu:8443/lu-legilux/{fixture.WorkKey}",
                     $"https://law.soufien.lu/lu-legilux/{fixture.WorkKey}?x=1",
                 })
        {
            var envelope = await DossierAsync(mount, foreign);
            Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, foreign);
            Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, foreign);
        }
    }

    [TestMethod]
    public async Task UnknownWorkHashPinnedPermalinkAndAnUnavailableLanguageRefuseAsTimelineRefuses()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[] { "/lu-legilux/no-such-work", fixture.Permalink, "eli/unknown" })
        {
            var envelope = await DossierAsync(mount, identifier);
            var timeline = await TimelineAsync(mount, identifier);
            Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, identifier);
            Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, identifier);
            Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, identifier);
            Assert.AreEqual(identifier, envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
            Assert.AreEqual(timeline.Refusal!.HelpfulPayload.GetRawText(), envelope.Refusal.HelpfulPayload.GetRawText(), identifier);
        }

        var english = await DossierAsync(mount, $"/lu-legilux/{fixture.WorkKey}", "eng");
        Assert.AreEqual(V3Verdicts.Refuse, english.Verdict);
        Assert.AreEqual("language_not_available", english.Refusal!.Code);
        Assert.AreEqual("eng", english.Refusal.HelpfulPayload.GetProperty("requested_language").GetString());
        CollectionAssert.AreEqual(new[] { "fra" },
            english.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
    }

    [TestMethod]
    public async Task EuIdentifiersAndEuOnlyMountsRefuseTheModeWithEuContextNamingTheDossierMode()
    {
        var luxembourg = await MountedFixture.CreateAsync();
        await using var cleanupLuxembourg = luxembourg;
        using (var mount = await V3CorpusMount.OpenAsync(luxembourg.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            foreach (var identifier in new[]
                     {
                         "32016R0679",
                         "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
                         "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679",
                     })
            {
                var envelope = await DossierAsync(mount, identifier);
                Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, identifier);
                Assert.AreEqual("retrieval_mode_unavailable", envelope.Refusal!.Code, identifier);
                Assert.AreEqual("r6_dossier", envelope.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
                CollectionAssert.AreEqual(new[] { "r0_exact_coordinate" },
                    envelope.Refusal.HelpfulPayload.GetProperty("available_modes").EnumerateArray()
                        .Select(static v => v.GetString()).ToArray());
                Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, identifier);
                Assert.AreEqual(TimelineSemantics.OfficialConsolidationState, envelope.Context.TimelineSemantics, identifier);
            }
        }

        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanupEurope = europe;
        using var europeMount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(europeMount);
        var onEuropeOnly = await DossierAsync(europeMount, $"/lu-legilux/{luxembourg.WorkKey}");
        Assert.AreEqual(V3Verdicts.Refuse, onEuropeOnly.Verdict);
        Assert.AreEqual("no_corpus_mounted", onEuropeOnly.Refusal!.Code);
        Assert.AreEqual("lu", onEuropeOnly.Refusal.HelpfulPayload.GetProperty("required_corpus").GetString());
        Assert.AreEqual(PublisherId.LuLegilux, onEuropeOnly.Context.Publisher);
        var euOnEuropeOnly = await DossierAsync(europeMount, "32016R0679");
        Assert.AreEqual("retrieval_mode_unavailable", euOnEuropeOnly.Refusal!.Code);
        Assert.AreEqual("r6_dossier", euOnEuropeOnly.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"dossier\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"dossier\",\"parameters\":{\"identifier\":\" \"}}")]
    [DataRow("{\"operation_id\":\"dossier\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"language\":\"\"}}")]
    [DataRow("{\"operation_id\":\"dossier\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"date\":\"2026-01-01\"}}")]
    [DataRow("{\"operation_id\":\"dossier\",\"parameters\":{\"identifier\":\"/lu-legilux/w\",\"extra\":1}}")]
    [DataRow("{\"operation_id\":\"dossier\",\"parameters\":{\"identifier\":1}}")]
    public async Task UnusableDossierRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, DossierRawTarget, body);

        AssertTransportProblem(context, "request_schema_invalid", StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ABodyNamingAnotherOperationFailsTheRouteBoundSchemaAndAQueryStringIsDrift()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var dossierBody = "{\"operation_id\":\"dossier\",\"parameters\":{\"identifier\":\"/lu-legilux/" + fixture.WorkKey + "\"}}";
        var timelineBody = "{\"operation_id\":\"timeline\",\"parameters\":{\"identifier\":\"/lu-legilux/" + fixture.WorkKey + "\"}}";

        Assert.AreEqual(StatusCodes.Status200OK, (await PostAsync(mount, DossierRawTarget, dossierBody)).Response.StatusCode);

        // A timeline body is the same shape as a dossier body; the operation_id constant is what keeps it out.
        foreach (var (rawTarget, body) in new[]
                 {
                     (DossierRawTarget, timelineBody),
                     (TimelineRawTarget, dossierBody),
                     (V3ResolveRestRoute.RawTarget, dossierBody),
                 })
        {
            AssertTransportProblem(
                await PostAsync(mount, rawTarget, body), "request_schema_invalid", StatusCodes.Status400BadRequest,
                rawTarget + " " + body);
        }

        AssertTransportProblem(
            await PostAsync(mount, DossierRawTarget + "?x=1", dossierBody), "unknown_route", StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task DossierWithoutAMountRefusesNoCorpusMountedThroughTheSameRoute()
    {
        var context = Body(DossierRawTarget,
            "{\"operation_id\":\"dossier\",\"parameters\":{\"identifier\":\"/lu-legilux/w\"}}");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, null);

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        Assert.AreEqual("dossier", envelope.OperationId);
    }

    private static string PublisherWorkIriOf(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT publisher_work_iri FROM states WHERE work_key=$work LIMIT 1";
        command.Parameters.AddWithValue("$work", fixture.WorkKey);
        return (string)command.ExecuteScalar()!;
    }

    private static string[] Rows(JsonElement group, string list) =>
        group.GetProperty(list).EnumerateArray()
            .Select(static row => row.GetProperty("title").GetString() + "|" + row.GetProperty("evidence_sha256").GetString())
            .ToArray();

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<V3Envelope> DossierAsync(V3CorpusMount mount, string identifier, string? language = null) =>
        await AskAsync(mount, DossierRawTarget, "dossier", identifier, language);

    private static async Task<V3Envelope> TimelineAsync(V3CorpusMount mount, string identifier, string? language = null) =>
        await AskAsync(mount, TimelineRawTarget, "timeline", identifier, language);

    private static async Task<V3Envelope> AskAsync(
        V3CorpusMount mount, string rawTarget, string operationId, string identifier, string? language)
    {
        var parameters = new Dictionary<string, string> { ["identifier"] = identifier };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var body = JsonSerializer.Serialize(new { operation_id = operationId, parameters });
        var context = await PostAsync(mount, rawTarget, body);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode,
            Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var context = Body(rawTarget, body);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }

    private static DefaultHttpContext Body(string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-dossier";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AssertTransportProblem(DefaultHttpContext context, string code, int status, string? because = null)
    {
        Assert.AreEqual(status, context.Response.StatusCode, because);
        Assert.AreEqual("application/problem+json", context.Response.ContentType, because);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString(), because);
        Assert.IsFalse(problem.RootElement.TryGetProperty("verdict", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("refusal", out _));
    }
}
