using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Names;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// R92 end to end over fixture mounts: the measurement (<see cref="NamesMeasurement"/>) over a mount whose one act names a
/// person (synthetic text in the fixture document's shape: every name, date and place is invented), the two records it writes,
/// and the real handler serving the mount with them beside it. Every operation that answered about the act without the names
/// record answers <c>text_withheld</c> with the reason <c>names_private_individuals</c>, its identity, hashes and official
/// link with it; search no longer matches its text; an act measured to name no one is listed in the indexing record and served
/// as before; a record of another build, or a broken one, refuses the mount.
/// </summary>
[TestClass]
public sealed class V3CorpusNamesMountTests
{
    /// <summary>The synthetic provision: an individual grant naming an invented person with invented particulars.</summary>
    private const string NamingProvision =
        "La naturalisation est conférée à Monsieur Jean Exemple, né le 1er janvier 1950 à Testville, demeurant à Testville.";

    [TestMethod]
    public async Task AnActWhoseTextNamesAPersonIsWithheldOnEveryOperationThatWouldShowItsText()
    {
        var fixture = await MountedFixture.CreateAsync(await ActNamingAPersonAsync());
        await using var cleanup = fixture;
        var workKey = fixture.WorkKey;
        var dossier = $"/lu-legilux/{workKey}";

        // The measurement: the act's text names a person, so it is withheld and listed nowhere for search engines.
        var outcome = NamesMeasurement.Measure(fixture.Directory);
        var withheld = outcome.Withheld.Single();
        Assert.AreEqual(workKey, withheld.WorkKey);
        Assert.AreEqual(PrivateIndividuals.LuxembourgTextNamesPerson, withheld.Class);
        CollectionAssert.AreEqual(
            new[] { PrivateIndividuals.BirthDateCode, PrivateIndividuals.BirthPlaceCode, PrivateIndividuals.DomicileCode, PrivateIndividuals.PersonNamedCode },
            withheld.Evidence.ToArray());
        CollectionAssert.Contains(withheld.StateSha256s.ToArray(), fixture.StateSha256);
        Assert.IsEmpty(outcome.Indexed, "a withheld act is never offered to search engines");
        AssertNoPersonalData(outcome);

        // One form for one mount.
        var again = NamesMeasurement.Measure(fixture.Directory);
        CollectionAssert.AreEqual(outcome.NamesRecordBytes, again.NamesRecordBytes);
        CollectionAssert.AreEqual(outcome.IndexingRecordBytes, again.IndexingRecordBytes);
        CollectionAssert.AreEqual(outcome.MeasurementBytes, again.MeasurementBytes);

        // Without the record, the operations answer the act.
        var requests = LuxembourgRequests(fixture);
        Dictionary<string, V3Envelope> before;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            before = await AskAllAsync(mount, requests);
            Assert.IsGreaterThan(0, (await SearchAsync(mount, "naturalisation", null)).Result!.Value.GetProperty("hits").GetArrayLength(),
                "without the record, search matches the act's text");
        }

        var answered = before.Where(static pair => pair.Value.Result is not null).Select(static pair => pair.Key).ToArray();
        Assert.HasCount(requests.Length, answered,
            "the fixture answers every operation asked about the act: " + string.Join(", ", before.Select(static pair => $"{pair.Key}={pair.Value.Refusal?.Code ?? "answer"}")));

        // With it, every one of them is text_withheld, naming the act by its identity and hashes, and nothing of its text.
        NamesMeasurement.WriteFiles(outcome, fixture.Directory);
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var after = await AskAllAsync(mount, requests);
            foreach (var operation in answered)
            {
                var envelope = after[operation];
                Assert.AreEqual("text_withheld", envelope.Refusal?.Code, operation);
                var payload = envelope.Refusal!.HelpfulPayload;
                Assert.AreEqual(PrivateIndividuals.Reason, payload.GetProperty("reason").GetString(), operation);
                Assert.AreEqual(withheld.WorkIri, payload.GetProperty("official_link").GetString(), operation);
                Assert.AreEqual(workKey, payload.GetProperty("work_key").GetString(), operation);
                Assert.AreEqual(withheld.Class, payload.GetProperty("class").GetString(), operation);
                CollectionAssert.Contains(
                    payload.GetProperty("state_sha256s").EnumerateArray().Select(static digest => digest.GetString()).ToArray(),
                    fixture.StateSha256, operation);
                Assert.AreEqual(64, payload.GetProperty("content_sha256").GetString()!.Length, operation);
                var text = payload.GetRawText();
                Assert.IsFalse(text.Contains("Exemple", StringComparison.Ordinal) || text.Contains("Testville", StringComparison.Ordinal), operation);
            }

            // The refusals that carry no text are as they were.
            foreach (var (operation, envelope) in before.Where(static pair => pair.Value.Refusal is not null))
            {
                Assert.AreEqual(envelope.Refusal!.Code, after[operation].Refusal?.Code, operation);
            }

            var search = await SearchAsync(mount, "naturalisation", null);
            Assert.IsNull(search.Refusal);
            Assert.AreEqual(0, search.Result!.Value.GetProperty("hits").GetArrayLength(), "search never matches a withheld act's text");
            Assert.IsFalse(search.Result.Value.GetRawText().Contains("Exemple", StringComparison.Ordinal));
            Assert.AreEqual("text_withheld", (await SearchAsync(mount, "naturalisation", dossier)).Refusal?.Code,
                "a search within the withheld act is refused as its reading is");
        }
    }

    [TestMethod]
    public async Task AnActMeasuredToNameNoOneIsListedForSearchEnginesAndServedAsBefore()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var outcome = NamesMeasurement.Measure(fixture.Directory);
        Assert.IsEmpty(outcome.Withheld);
        var listed = outcome.Indexed.Single();
        Assert.AreEqual($"/lu-legilux/{fixture.WorkKey}", listed.Address);
        CollectionAssert.AreEqual(new[] { "fra" }, listed.Languages.ToArray());
        var record = V3WebRoot.ReadIndexingRecord(IndexingRecord.Text(outcome.IndexingRecordBytes));
        Assert.IsNotNull(record, "the web root admits the indexing record the measurement writes");
        Assert.AreEqual(outcome.Measured.BuiltAt, record.BuiltAt);
        var measurement = JsonDocument.Parse(outcome.MeasurementBytes).RootElement;
        Assert.AreEqual(1, measurement.GetProperty("luxembourg_held").GetProperty("works").GetInt32());
        Assert.AreEqual(1, measurement.GetProperty("luxembourg_held").GetProperty("indexed").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, measurement.GetProperty("europe").ValueKind, "a mount with no EU index measures none");

        NamesMeasurement.WriteFiles(outcome, fixture.Directory);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        Assert.IsFalse(mount.WithholdsUnderR92);
        var envelope = await AskAsync(mount, "dossier", new Dictionary<string, object> { ["identifier"] = $"/lu-legilux/{fixture.WorkKey}" });
        Assert.IsNull(envelope.Refusal, "a work no record withholds is answered");
    }

    [TestMethod]
    public async Task AnEuActTheRecordWithholdsIsTextWithheldOnEveryOperationThatWouldShowItsText()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var outcome = NamesMeasurement.Measure(fixture.Directory);
        Assert.IsEmpty(outcome.Withheld, "the GDPR names no one");
        var listed = outcome.Indexed.Single();
        Assert.IsTrue(listed.Address.StartsWith("/eu-eurlex/3", StringComparison.Ordinal), listed.Address);
        var celex = listed.Address["/eu-eurlex/".Length..];
        Assert.IsNotNull(V3WebRoot.ReadIndexingRecord(IndexingRecord.Text(outcome.IndexingRecordBytes)));

        string rootWork;
        string[] states;
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(fixture.Directory, V3CorpusMount.EuropeIndexFileName)};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT root_work_iri, state_identity_sha256 FROM states WHERE seed_celex = $seed ORDER BY state_identity_sha256";
            command.Parameters.AddWithValue("$seed", celex);
            using var reader = command.ExecuteReader();
            var rows = new List<(string Root, string State)>();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }

            rootWork = rows.Select(static row => row.Root).Distinct(StringComparer.Ordinal).Single();
            states = rows.Select(static row => row.State).ToArray();
        }

        var coordinate = $"{fixture.PublisherExpressionId}#lex-provision={Uri.EscapeDataString(fixture.PublisherProvisionIdentifier)}";

        // A record withholding the act (as if it were a restrictive measure), bound to this mount.
        var requests = new (string Operation, Dictionary<string, object> Parameters)[]
        {
            ("dossier", new() { ["identifier"] = celex }),
            ("timeline", new() { ["identifier"] = celex }),
            ("as_of", new() { ["identifier"] = celex, ["date"] = "2026-01-01" }),
            ("evidence_bundle", new() { ["identifier"] = celex, ["date"] = "2026-01-01" }),
            ("search", new() { ["identifier"] = celex, ["query"] = "data", ["language"] = "eng" }),
            ("resolve", new() { ["identifier"] = celex }),
            // The review of #1005, F1: the act named by a provision coordinate or by an article identity, which search locates to
            // the same act (as_of reads a coordinate and no bare article identity), and which named the act nowhere the gate read.
            ("search", new() { ["identifier"] = coordinate, ["query"] = "data", ["language"] = "eng" }),
            ("search", new() { ["identifier"] = fixture.ArticleIdentitySha256, ["query"] = "data", ["language"] = "eng" }),
            ("as_of", new() { ["identifier"] = coordinate, ["date"] = "2026-01-01" }),
        };
        Dictionary<string, V3Envelope> before;
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            before = await AskAllAsync(mount, requests);
        }

        var answered = before.Where(static pair => pair.Value.Result is not null).Select(static pair => pair.Key).ToArray();
        Assert.HasCount(requests.Length, answered,
            string.Join(", ", before.Select(static pair => $"{pair.Key}={pair.Value.Refusal?.Code ?? "answer"}")));

        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, NamesRecord.FileName), NamesRecord.Write(
            outcome.Measured,
            [new NamesWithheldWork(NamesRecord.EuropePublisher, rootWork, null, celex, PrivateIndividuals.EuropeRestrictiveMeasures,
                [PrivateIndividuals.ClassCode], states)]));
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, IndexingRecord.FileName), IndexingRecord.Write(outcome.Measured.BuiltAt, []));
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            var after = await AskAllAsync(mount, requests);
            foreach (var operation in answered)
            {
                var envelope = after[operation];
                Assert.AreEqual("text_withheld", envelope.Refusal?.Code, operation);
                Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, operation);
                var payload = envelope.Refusal!.HelpfulPayload;
                Assert.AreEqual(PrivateIndividuals.Reason, payload.GetProperty("reason").GetString(), operation);
                Assert.AreEqual(celex, payload.GetProperty("celex").GetString(), operation);
                Assert.AreEqual(rootWork, payload.GetProperty("official_link").GetString(), operation);
                Assert.AreEqual($"http://publications.europa.eu/resource/celex/{celex}", payload.GetProperty("official_identity").GetString(), operation);
            }
        }
    }

    [TestMethod]
    public async Task ANamesRecordOfAnotherBuildOrThatBreaksARuleRefusesTheMount()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var path = Path.Combine(fixture.Directory, NamesRecord.FileName);
        var measured = NamesMeasurement.Measure(fixture.Directory).Measured;
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, IndexingRecord.FileName), IndexingRecord.Write(measured.BuiltAt, []));
        var other = NamesRecord.Write(
            new NamesMeasuredMount(fixture.CorpusSha256, new string('0', 64), null, null),
            []);
        foreach (var (what, bytes) in new (string, byte[])[]
        {
            ("a record of another index build", other),
            ("a record of another corpus", NamesRecord.Write(new NamesMeasuredMount(new string('0', 64), fixture.IndexSha256, null, null), [])),
            ("a record of another build time than its indexing record's", NamesRecord.Write(measured with { BuiltAt = "2001-01-01T00:00:00Z" }, [])),
            ("not JSON", "not json"u8.ToArray()),
            ("another schema", Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(NamesRecord.Write(
                new NamesMeasuredMount(fixture.CorpusSha256, fixture.IndexSha256, null, null), [])).Replace("lex-v3-names-record/1", "lex-v3-names-record/9", StringComparison.Ordinal))),
        })
        {
            await File.WriteAllBytesAsync(path, bytes);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None), what);
        }

        await File.WriteAllBytesAsync(path, NamesRecord.Write(measured, []));
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount, "a pair of this build that withholds nothing mounts");
        }

        // The two records stand together or not at all (the review of #1001), and no work is in both.
        var indexing = Path.Combine(fixture.Directory, IndexingRecord.FileName);
        var indexingBytes = await File.ReadAllBytesAsync(indexing);
        File.Delete(indexing);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None), "a names record alone");
        await File.WriteAllBytesAsync(indexing, indexingBytes);
        File.Delete(path);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None), "an indexing record alone");
        await File.WriteAllBytesAsync(indexing, "not json"u8.ToArray());
        await File.WriteAllBytesAsync(path, NamesRecord.Write(measured, []));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None), "a broken indexing record");
        var states = NamesMeasurement.Measure(fixture.Directory).Indexed;
        await File.WriteAllBytesAsync(indexing, IndexingRecord.Write(measured.BuiltAt, states));
        await File.WriteAllBytesAsync(path, NamesRecord.Write(measured,
            [new NamesWithheldWork(NamesRecord.LuxembourgPublisher, "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3", fixture.WorkKey, null,
                PrivateIndividuals.LuxembourgTextNamesPerson, [PrivateIndividuals.PersonNamedCode], [fixture.StateSha256])]));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None), "a work both indexed and withheld");
        File.Delete(indexing);
        File.Delete(path);
        using (var bare = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(bare, "a mount with neither record mounts, and withholds nothing");
            Assert.IsFalse(bare.WithholdsUnderR92);
        }
    }

    [TestMethod]
    public async Task AWithheldWorksArticlesAreNotQuotedInAnotherActsRelationsNorCitedBy()
    {
        // The review of #1001, F1: relations served the same inbound edges as cited_by, with their labels, unfiltered.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var date = DateOnly.ParseExact(fixture.ApplicabilityDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            .AddDays(400).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var citing = await fixture.AddStateAsync(date, "citing", workLeaf: "n9");
        const string Label = "WITHHELD-LABEL-TEXT";
        var tokens = "[" + JsonSerializer.Serialize(new
        {
            kind = "reference", text = Label, target = "/eli/etat/leg/loi/1991/08/10/n3/jo", marker = (string?)null, note_body = (object?)null,
        }) + "]";
        await fixture.SetArticleTokensAsync(citing.ExpressionIri, "art_1er", Label, tokens);
        var work = $"/lu-legilux/{fixture.WorkKey}";
        var requests = new (string Operation, Dictionary<string, object> Parameters)[]
        {
            ("relations", new() { ["identifier"] = work, ["direction"] = "inbound" }),
            ("relations", new() { ["identifier"] = work, ["direction"] = "both" }),
            ("cited_by", new() { ["identifier"] = work }),
        };
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            foreach (var (operation, parameters) in requests)
            {
                var envelope = await AskAsync(mount, operation, parameters);
                Assert.IsTrue(envelope.Result!.Value.GetRawText().Contains(Label, StringComparison.Ordinal), $"{operation} {parameters.Count}: without the record the edge is quoted");
            }
        }

        var measured = NamesMeasurement.Measure(fixture.Directory).Measured;
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, IndexingRecord.FileName), IndexingRecord.Write(measured.BuiltAt, []));
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, NamesRecord.FileName), NamesRecord.Write(measured,
            [new NamesWithheldWork(NamesRecord.LuxembourgPublisher, citing.PublisherWorkIri, citing.WorkKey, null,
                PrivateIndividuals.LuxembourgTextNamesPerson, [PrivateIndividuals.PersonNamedCode], [citing.StateSha256])]));
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            foreach (var (operation, parameters) in requests)
            {
                var envelope = await AskAsync(mount, operation, parameters);
                Assert.IsNull(envelope.Refusal, operation);
                var value = envelope.Result!.Value;
                Assert.IsFalse(value.GetRawText().Contains(Label, StringComparison.Ordinal), $"{operation}: a withheld work's article is never quoted");
                Assert.IsFalse(value.GetProperty("edges").EnumerateArray()
                    .Any(edge => string.Equals(edge.GetProperty("citing_work_key").GetString(), citing.WorkKey, StringComparison.Ordinal)), operation);
                if (operation == "relations")
                {
                    Assert.AreEqual(0, value.GetProperty("edge_counts").GetProperty("inbound").GetInt32(), "the counts follow the edges served");
                }
            }

            // The withheld work's own relations answer text_withheld.
            Assert.AreEqual("text_withheld", (await AskAsync(mount, "relations", new() { ["identifier"] = $"/lu-legilux/{citing.WorkKey}" })).Refusal?.Code);
        }
    }

    [TestMethod]
    public async Task AnArticleOfNoStateIsServedNowhereAndRecordedAsUnserved()
    {
        // derive-r3 (2026-10-31): two acts admitted by the rulings kept their articles and references in the index after the
        // derive dropped their states. No reader query reaches an article but through a state, so such an article is served
        // nowhere; cited_by and relations leave its edge out instead of refusing, and the measurement records it.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var date = DateOnly.ParseExact(fixture.ApplicabilityDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            .AddDays(400).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var citing = await fixture.AddStateAsync(date, "citing", workLeaf: "n9");
        const string Label = "ORPHAN-LABEL-TEXT";
        var tokens = "[" + JsonSerializer.Serialize(new
        {
            kind = "reference", text = Label, target = "/eli/etat/leg/loi/1991/08/10/n3/jo", marker = (string?)null, note_body = (object?)null,
        }) + "]";
        await fixture.SetArticleTokensAsync(citing.ExpressionIri, "art_1er", Label, tokens);
        var work = $"/lu-legilux/{fixture.WorkKey}";
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            Assert.IsTrue((await AskAsync(mount, "cited_by", new() { ["identifier"] = work })).Result!.Value.GetRawText().Contains(Label, StringComparison.Ordinal),
                "with its state, the citing article's edge is served");
        }

        // The citing state goes; its articles and references stay, as the derive left them.
        await fixture.DropStateAsync(citing.ExpressionIri);
        var articles = fixture.ArticleCount(citing.ExpressionIri);
        Assert.IsGreaterThan(0, articles);

        var outcome = NamesMeasurement.Measure(fixture.Directory);
        Assert.IsEmpty(outcome.Withheld);
        var held = JsonDocument.Parse(outcome.MeasurementBytes).RootElement.GetProperty("luxembourg_held");
        Assert.AreEqual(articles, held.GetProperty("unserved_articles").GetInt64());
        Assert.AreEqual(articles, held.GetProperty("unserved_expressions").GetProperty(citing.ExpressionIri).GetInt64());
        Assert.AreEqual(1, held.GetProperty("works").GetInt32(), "the work of no state is not a held work");
        AssertNoPersonalData(outcome);
        CollectionAssert.AreEqual(outcome.MeasurementBytes, NamesMeasurement.Measure(fixture.Directory).MeasurementBytes, "one form for one mount");

        NamesMeasurement.WriteFiles(outcome, fixture.Directory);
        using (var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            foreach (var (operation, parameters) in new (string Operation, Dictionary<string, object> Parameters)[]
                     {
                         ("cited_by", new() { ["identifier"] = work }),
                         ("relations", new() { ["identifier"] = work, ["direction"] = "inbound" }),
                         ("relations", new() { ["identifier"] = work, ["direction"] = "both" }),
                     })
            {
                var envelope = await AskAsync(mount, operation, parameters);
                Assert.IsNull(envelope.Refusal, operation);
                var value = envelope.Result!.Value;
                Assert.IsFalse(value.GetRawText().Contains(Label, StringComparison.Ordinal), $"{operation}: an article of no state is served nowhere");
                Assert.IsFalse(value.GetProperty("edges").EnumerateArray()
                    .Any(edge => string.Equals(edge.GetProperty("citing_work_key").GetString(), citing.WorkKey, StringComparison.Ordinal)), operation);
            }
        }
    }

    [TestMethod]
    public async Task APersonNamedOnlyInAnArticlesNoteIsFound()
    {
        // The review of #1001: the searchable text holds no note body, and the API serves the notes; the measurement reads them.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var tokens = "[" + string.Join(",",
            JsonSerializer.Serialize(new { kind = "text", text = "Le présent article renvoie à la note.", target = (string?)null, marker = (string?)null, note_body = (object?)null }),
            JsonSerializer.Serialize(new
            {
                kind = "note_reference", text = (string?)null, target = (string?)null, marker = "1",
                note_body = new object[] { new { kind = "text", text = NamingProvision, target = (string?)null, marker = (string?)null } },
            })) + "]";
        await fixture.SetArticleTokensAsync(fixture.ExpressionIri, "art_1er", "Le présent article renvoie à la note.", tokens);
        var outcome = NamesMeasurement.Measure(fixture.Directory);
        var withheld = outcome.Withheld.Single();
        Assert.AreEqual(fixture.WorkKey, withheld.WorkKey);
        CollectionAssert.Contains(withheld.Evidence.ToArray(), PrivateIndividuals.BirthDateCode);
        Assert.IsEmpty(outcome.Indexed);
        CollectionAssert.AreEqual(new[] { NamingProvision }, NamesMeasurement.NoteBodies(tokens).ToArray());
        Assert.IsEmpty(NamesMeasurement.NoteBodies("[]"));
    }

    /// <summary>The fixture act's document with its body replaced by two synthetic articles, one naming an invented person.</summary>
    internal static async Task<byte[]> ActNamingAPersonAsync()
    {
        var fixture = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", "loi-1991-08-10-n3--2024-02-01--fr.bin")));
        var bodyAt = fixture.IndexOf("<body>", StringComparison.Ordinal);
        var bodyEnd = fixture.IndexOf("</body>", StringComparison.Ordinal);
        Assert.IsGreaterThan(bodyAt, bodyEnd);
        var body = new StringBuilder("<body>\n<chapter id=\"chapitre_i\">\n<num>Chapitre I. </num>\n<heading> Chapitre synthétique</heading>\n");
        foreach (var (id, label, text) in new[]
        {
            ("art_1er", "Art. 1<sup>er</sup>.", NamingProvision),
            ("art_2", "Art. 2.", "Le présent texte synthétique sert au test seulement et ne dit rien du droit applicable."),
        })
        {
            body.Append("<article id=\"").Append(id).Append("\">\n<num>").Append(label).Append("</num>\n<alinea>\n<content>\n<p>")
                .Append(text).Append("</p>\n</content>\n</alinea>\n</article>\n");
        }

        body.Append("</chapter>\n");
        return Encoding.UTF8.GetBytes(fixture[..bodyAt] + body + fixture[bodyEnd..]);
    }

    private static (string Operation, Dictionary<string, object> Parameters)[] LuxembourgRequests(MountedFixture fixture)
    {
        var work = $"/lu-legilux/{fixture.WorkKey}";
        var date = fixture.ApplicabilityDate;
        return
        [
            ("resolve", new() { ["identifier"] = fixture.Permalink }),
            ("as_of", new() { ["identifier"] = work, ["date"] = date }),
            ("timeline", new() { ["identifier"] = work }),
            ("article_history", new() { ["identifier"] = work, ["anchor"] = "art_1er" }),
            ("diff", new() { ["identifier"] = work, ["date_from"] = date, ["date_to"] = date }),
            ("in_force_on", new() { ["identifier"] = work, ["date"] = date }),
            ("provenance", new() { ["identifier"] = work, ["date"] = date }),
            ("dossier", new() { ["identifier"] = work }),
            ("citation", new() { ["identifier"] = work, ["date"] = date }),
            ("cited_by", new() { ["identifier"] = work }),
            ("verify", new() { ["identifier"] = fixture.Permalink }),
            ("relations", new() { ["identifier"] = work }),
            ("evidence_bundle", new() { ["identifier"] = work, ["date"] = date }),
            ("classification", new() { ["identifier"] = work }),
            ("manifestation", new() { ["identifier"] = work }),
            ("status_on", new() { ["identifier"] = work, ["date"] = date }),
            ("as_observed", new() { ["identifier"] = work, ["date"] = date, ["snapshot"] = fixture.IndexSha256 }),
            ("search", new() { ["identifier"] = work, ["query"] = "naturalisation", ["language"] = "fra" }),
        ];
    }

    private static async Task<Dictionary<string, V3Envelope>> AskAllAsync(
        V3CorpusMount mount,
        IEnumerable<(string Operation, Dictionary<string, object> Parameters)> requests)
    {
        var answers = new Dictionary<string, V3Envelope>(StringComparer.Ordinal);
        foreach (var (operation, parameters) in requests)
        {
            // Keyed by the operation and the identifier asked, so one operation asked by two identifiers is two answers.
            answers[parameters.TryGetValue("identifier", out var identifier) ? $"{operation} {identifier}" : operation] =
                await AskAsync(mount, operation, parameters);
        }

        return answers;
    }

    private static Task<V3Envelope> SearchAsync(V3CorpusMount mount, string query, string? identifier)
    {
        var parameters = new Dictionary<string, object> { ["query"] = query, ["language"] = "fra" };
        if (identifier is not null)
        {
            parameters["identifier"] = identifier;
        }

        return AskAsync(mount, "search", parameters);
    }

    private static async Task<V3Envelope> AskAsync(V3CorpusMount mount, string operation, Dictionary<string, object> parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext { TraceIdentifier = "names-r92" };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, $"{operation}: {Encoding.UTF8.GetString(ResponseBytes(context))}");
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    /// <summary>The three files hold identifiers, classes, codes, counts and hashes: none of the invented person's data.</summary>
    private static void AssertNoPersonalData(NamesMeasurement.Outcome outcome)
    {
        foreach (var bytes in new[] { outcome.NamesRecordBytes, outcome.IndexingRecordBytes, outcome.MeasurementBytes })
        {
            var text = Encoding.UTF8.GetString(bytes);
            foreach (var personal in new[] { "Exemple", "Testville", "1950", "Jean" })
            {
                Assert.IsFalse(text.Contains(personal, StringComparison.Ordinal), personal);
            }
        }
    }
}
