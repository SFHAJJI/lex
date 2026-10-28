using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Names;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU act's titles and Official Journal references in its answers (Release 3's item 1): over an EU index /7, dossier, as_of,
/// evidence_bundle and an EU search say each listed expression's titles with their sources and its wording's reference, the
/// not-held rows say only what is still not held, the authenticity rule no longer says no publication date is held, and a
/// search that names no work lists the EU acts its query names by title (withheld acts left out, R92). Over an EU index /6 or
/// older the answers are unchanged: no member of these, and the not-held row that says no title is held.
/// </summary>
[TestClass]
public sealed class V3CorpusEuropeTitlesMountTests
{
    private const string Celex = "32016R0679";

    private static readonly (string Operation, object Parameters)[] Answers =
    [
        ("dossier", new { identifier = Celex }),
        ("as_of", new { identifier = Celex, date = "2018-06-01", language = "eng" }),
        ("evidence_bundle", new { identifier = Celex, date = "2018-06-01", language = "eng" }),
        ("search", new { identifier = Celex, query = "personal data", language = "eng" }),
    ];

    [TestMethod]
    public async Task EveryEuAnswerOfTheActSaysItsTitlesAndItsOfficialJournalReferenceWithTheirSources()
    {
        var fixture = await V3CorpusResolveMountTests.EuropeMountedFixture.CreateAsync(withTitles: true);
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (operation, parameters) in Answers)
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{operation}: {envelope.Refusal?.Code}");
            var body = envelope.Result!.Value;
            var titles = body.GetProperty("titles");
            Assert.AreEqual(V3CorpusMount.EuropeTitlesRule, titles.GetProperty("rule").GetString());
            var entries = titles.GetProperty("entries").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(new[] { "cellar_expression_title", "cellar_expression_title_short", "formex_title" },
                entries.Select(static entry => entry.GetProperty("source").GetString()).ToArray(), operation);
            Assert.IsTrue(entries.All(entry => entry.GetProperty("publisher_expression_id").GetString() == fixture.PublisherExpressionId), operation);
            Assert.AreEqual(EuTitlesFixture.EnglishTitle, entries[0].GetProperty("text").GetString(), operation);
            Assert.AreEqual("short_title", entries[1].GetProperty("kind").GetString(), operation);
            Assert.AreEqual(0, titles.GetProperty("expressions_without_title").GetArrayLength(), operation);

            var journal = body.GetProperty("official_journal");
            var reference = journal.GetProperty("references").EnumerateArray().Single();
            Assert.AreEqual("act_publication", reference.GetProperty("kind").GetString(), operation);
            Assert.AreEqual("series_number", reference.GetProperty("number_form").GetString(), operation);
            Assert.AreEqual(("L", "119", "2016", "1", "2016-05-04"),
                (reference.GetProperty("series").GetString(), reference.GetProperty("oj_number").GetString(), reference.GetProperty("year").GetString(),
                    reference.GetProperty("first_page").GetString(), reference.GetProperty("publication_date").GetString()), operation);
            Assert.AreEqual(JsonValueKind.Null, reference.GetProperty("act_number").ValueKind, operation);
            Assert.AreEqual(0, journal.GetProperty("expressions_without_reference").GetArrayLength(), operation);

            var items = body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray();
            CollectionAssert.DoesNotContain(items, "titles", $"{operation}: the titles are held");
            CollectionAssert.DoesNotContain(items, "official_journal", $"{operation}: the reference is held");
            if (body.TryGetProperty("authenticity_rule", out var rule))
            {
                Assert.AreEqual(V3CorpusMount.EuropeAuthenticityRuleWithJournal, rule.GetString(), operation);
                Assert.AreNotEqual(V3CorpusMount.EuropeAuthenticityRule, rule.GetString(), operation);
            }
        }
    }

    [TestMethod]
    public async Task AWordingWhosePackageStatesNoReferenceSaysSoAndKeepsThePublishersTitle()
    {
        var fixture = await V3CorpusResolveMountTests.EuropeMountedFixture.CreateAsync(acquireFrenchExpression: true, withTitles: true);
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var envelope = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = Celex, language = "fra" });
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        var body = envelope.Result!.Value;
        var entries = body.GetProperty("titles").GetProperty("entries").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { "cellar_expression_title", "cellar_expression_title_short" },
            entries.Select(static entry => entry.GetProperty("source").GetString()).ToArray(), "the synthetic package states no title");
        Assert.AreEqual(EuTitlesFixture.FrenchTitle, entries[0].GetProperty("text").GetString());
        Assert.AreEqual("fra", entries[0].GetProperty("language").GetString());
        var journal = body.GetProperty("official_journal");
        Assert.AreEqual(0, journal.GetProperty("references").GetArrayLength());
        CollectionAssert.AreEqual(new[] { fixture.PublisherExpressionId },
            journal.GetProperty("expressions_without_reference").EnumerateArray().Select(static value => value.GetString()).ToArray());
        var rows = body.GetProperty("not_held").EnumerateArray().ToDictionary(static row => row.GetProperty("item").GetString()!, static row => row.GetProperty("reason").GetString());
        Assert.IsFalse(rows.ContainsKey("titles"), "the publisher's title is held");
        Assert.AreEqual(V3CorpusMount.EuropeOfficialJournalNotHeldReason, rows["official_journal"], "the not-held line stays where no reference is held");
    }

    [TestMethod]
    public async Task ASearchThatNamesNoWorkListsTheEuActsItsQueryNamesByTitle()
    {
        var fixture = await V3CorpusResolveMountTests.EuropeMountedFixture.CreateAsync(withTitles: true);
        await using var cleanup = fixture;
        await fixture.AddLuxembourgMountAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (query, reason, source, language) in new[]
                 {
                     ("general data protection regulation", "phrase_in_title", "cellar_expression_title", "eng"),
                     // A whole title asked as typed: the act's full title begins with its name, which the exact-reference phase
                     // reads first, so the whole short title stands for it here (EuropeIndexTitlesTests matches a whole title).
                     ("GDPR, Personal Data, Personal Data Protection", "whole_title", "cellar_expression_title_short", "eng"),
                     ("GDPR", "phrase_in_title", "cellar_expression_title_short", "eng"),
                 })
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/search", "search", new { query, language = "fra" });
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{query}: {envelope.Refusal?.Code}");
            var body = envelope.Result!.Value;
            Assert.AreEqual("lu-legilux", body.GetProperty("publisher").GetString(), "the search is Luxembourg's, its text unchanged");
            var europe = body.GetProperty("europe_titles");
            Assert.AreEqual("eu_title_discovery", europe.GetProperty("retrieval_lane").GetString());
            Assert.AreEqual("one_act", europe.GetProperty("outcome").GetString(), query);
            var act = europe.GetProperty("acts").EnumerateArray().Single();
            Assert.AreEqual(Celex, act.GetProperty("seed_celex").GetString());
            Assert.AreEqual(Celex, act.GetProperty("dossier").GetProperty("identifier").GetString());
            Assert.AreEqual(reason, act.GetProperty("match_reason").GetString(), query);
            Assert.AreEqual(source, act.GetProperty("title_source").GetString(), query);
            Assert.AreEqual(language, act.GetProperty("matched_title_language").GetString(), query);
            Assert.AreEqual(V3CorpusMount.EuropeTitleSearchRule, europe.GetProperty("rule").GetString());
            StringAssert.Contains(europe.GetProperty("matching").GetString(), TextFolding.Rule);
        }

        var none = await EnvelopeAsync(mount, "/api/v3/search", "search", new { query = "a phrase no title holds", language = "fra" });
        Assert.AreEqual("no_title_match", none.Result!.Value.GetProperty("europe_titles").GetProperty("outcome").GetString());
        Assert.AreEqual(0, none.Result.Value.GetProperty("europe_titles").GetProperty("acts").GetArrayLength());
    }

    [TestMethod]
    public async Task AnActWithheldUnderR92IsNotListedByAnyOfItsTitles()
    {
        var fixture = await V3CorpusResolveMountTests.EuropeMountedFixture.CreateAsync(withTitles: true);
        await using var cleanup = fixture;
        await fixture.AddLuxembourgMountAsync();
        var outcome = NamesMeasurement.Measure(fixture.Directory);
        string rootWork;
        string[] states;
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(fixture.Directory, V3CorpusMount.EuropeIndexFileName)};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT root_work_iri, state_identity_sha256 FROM states WHERE seed_celex = $seed ORDER BY state_identity_sha256";
            command.Parameters.AddWithValue("$seed", Celex);
            using var reader = command.ExecuteReader();
            var rows = new List<(string Root, string State)>();
            while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1)));
            rootWork = rows.Select(static row => row.Root).Distinct(StringComparer.Ordinal).Single();
            states = rows.Select(static row => row.State).ToArray();
        }

        SqliteConnection.ClearAllPools();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, NamesRecord.FileName), NamesRecord.Write(outcome.Measured,
            [new NamesWithheldWork(NamesRecord.EuropePublisher, rootWork, null, Celex, PrivateIndividuals.EuropeRestrictiveMeasures,
                [PrivateIndividuals.ClassCode], states)]));
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, IndexingRecord.FileName), IndexingRecord.Write(outcome.Measured.BuiltAt, []));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var envelope = await EnvelopeAsync(mount, "/api/v3/search", "search", new { query = "general data protection regulation", language = "fra" });
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        var europe = envelope.Result!.Value.GetProperty("europe_titles");
        Assert.AreEqual("no_title_match", europe.GetProperty("outcome").GetString(), "the withheld act is listed by no title");
        Assert.DoesNotContain(EuTitlesFixture.EnglishTitle, envelope.Result.Value.GetRawText());
    }

    [TestMethod]
    public async Task OverAnEuIndex6TheAnswersCarryNoTitleNorReferenceAndSayNoTitleIsHeld()
    {
        var fixture = await V3CorpusResolveMountTests.EuropeMountedFixture.CreateAsync(withTitles: true);
        await using var cleanup = fixture;
        await fixture.AddLuxembourgMountAsync();
        AsSchema6(fixture.Directory);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount, "an index /6 mounts as before");
        foreach (var (operation, parameters) in Answers)
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{operation}: {envelope.Refusal?.Code}");
            var body = envelope.Result!.Value;
            Assert.IsFalse(body.TryGetProperty("titles", out _), operation);
            Assert.IsFalse(body.TryGetProperty("official_journal", out _), operation);
            if (body.TryGetProperty("authenticity_rule", out var rule)) Assert.AreEqual(V3CorpusMount.EuropeAuthenticityRule, rule.GetString(), operation);
        }

        var dossier = (await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = Celex })).Result!.Value;
        Assert.AreEqual("no title of the EU work is held by the EU index",
            dossier.GetProperty("not_held").EnumerateArray().Single(static row => row.GetProperty("item").GetString() == "titles").GetProperty("reason").GetString());
        var search = (await EnvelopeAsync(mount, "/api/v3/search", "search", new { query = "general data protection regulation", language = "fra" })).Result!.Value;
        Assert.IsFalse(search.TryGetProperty("europe_titles", out _), "a search over an EU index /6 lists no EU act by title");
    }

    /// <summary>The mount's EU index rewritten as an index /6: the two tables dropped, the version and stamp set back, the rows re-sealed.</summary>
    private static void AsSchema6(string directory)
    {
        var path = Path.Combine(directory, V3CorpusMount.EuropeIndexFileName);
        EuropeIndexBuilder.ArticleRow[] articles;
        using (var connection = EuropeIndexBuilder.Open(path, SqliteOpenMode.ReadWrite))
        {
            T[] Rows<T>(string method) => ((IEnumerable<T>)typeof(EuropeIndexReader).GetMethod(method,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [connection])!).ToArray();
            articles = Rows<EuropeIndexBuilder.ArticleRow>("StreamArticles");
            var logical = EuropeIndexBuilder.HashLogicalRows(
                Rows<EuropeIndexBuilder.MemberRow>("ReadMembers"), Rows<EuropeIndexBuilder.CorrigendumLineRow>("ReadLines"),
                Rows<EuropeIndexBuilder.CorrigendumGapRow>("ReadGaps"), articles, Rows<EuropeIndexBuilder.ArticleSourceRow>("StreamArticleSources"),
                Rows<EuropeIndexBuilder.ArticleDigestRow>("StreamArticleDigests"), Rows<EuropeIndexState>("ReadStates"),
                EuropeIndexReader.ReadForceDateRows(connection));
            EuropeIndexBuilder.Execute(connection, "DROP TABLE expression_titles; DROP TABLE official_journal_references; PRAGMA user_version=6;");
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE stamp SET schema_identity='lex-v3-europe-index/6',logical_rows_sha256=$logical";
            command.Parameters.AddWithValue("$logical", logical);
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        var manifest = EuropeIndexBuilder.MeasureCapabilities(sha, articles);
        using var stream = new MemoryStream();
        V3IndexCapabilityManifestArtifact.Write(stream, manifest);
        File.WriteAllBytes(Path.Combine(directory, V3CorpusMount.EuropeCapabilityManifestFileName), stream.ToArray());
    }
}
