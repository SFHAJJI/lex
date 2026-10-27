using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The replay guarantees of <c>33-product-spec.md</c>, G1 to G5, run against the real handler on mounted corpora. G2
/// snapshot determinism and G5 independent verifiability need one build:
/// <list type="bullet">
/// <item>G2: every served operation answers the same canonical bytes from the same snapshot, from a byte copy of it and
/// when asked again later, apart from what belongs to the request (the observation time and the request's own reference)
/// and to the moment it is asked (the review of #998, F1): each channel's data age and staleness and the envelope's, on a
/// mount whose build report dates its channel, and, for a search that reads an exact reference naming no date, the date it
/// reads it at (today in Luxembourg), the version that date selects and whether it opens the readable address or a
/// permalink. Nothing else moves;</item>
/// <item>G5: a reader holding one evidence bundle and the publisher's file finds each served article's text in that file,
/// and recomputes the body digest, each text digest, the article identities digest and the state digest from the
/// derivations the platform publishes. On an index 9 the state digest is digest /2, whose row facts after the language
/// the answer carries since S7a, so this test recomputes it from the answer alone (the expected gap of #980 is closed),
/// and still holds those facts to the mounted index.</item>
/// </list>
/// G1 version immutability, G3 bitemporal completeness and G4 as-observed answering need a chain: three real builds of
/// the state fixture on three days, each chained to the one before and written as the mount it would be, keeping the
/// earlier builds beside it as generations. The second build reads the publisher's file with one byte added and its
/// text unchanged; the third reads it with one article reworded.
/// <list type="bullet">
/// <item>G1: each replaced file is a <c>file_replaced</c> event, and only the changed text is a new version id; the
/// earlier id still verifies on the latest mount, naming what replaced it;</item>
/// <item>G3: each earlier build's events are the latest log's prefix, row for row, and every state the log names still
/// verifies;</item>
/// <item>G4: <c>as_observed</c> at each build's snapshot answers the state that build answered when it was mounted, field
/// for field, bounded by its build time.</item>
/// </list>
/// </summary>
/// <remarks>
/// The retention line (S7-A09) decides which earlier builds keep their text. A state whose text no kept build holds is
/// still named by the log, without text (<c>V3CorpusGenerationQuoteTests</c>). <c>knowable_on</c> waits for observation
/// times, which no build holds, so it stays registered and not served. G2's detached signature comes from the release
/// pipeline. G5 does not recompute <c>wording_sha256</c> (its input is the stored token stream, which the bundle does
/// not serve), nor the article identities, the rule-profile digests or the body receipt, whose derivations are not
/// published. The mounts are test fixtures, so this proves the path, not a corpus.
/// </remarks>
[TestClass]
public sealed class V3ReplayGuaranteesTests
{
    private static readonly DateTimeOffset Later = ObservedAt.AddDays(30);

    [TestMethod]
    public async Task EveryServedOperationAnswersTheSameBytesFromTheSameSnapshotFromACopyOfItAndLater()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = DateOnly.ParseExact(fixture.ApplicabilityDate, "yyyy-MM-dd").AddDays(400).ToString("yyyy-MM-dd");
        await fixture.AddStateAsync(laterDate, "later");
        // A version whose date falls between the two answers' days, which a reference naming no date opens once that day is today.
        var today = LuxembourgCalendar.DateAt(ObservedAt);
        var laterToday = LuxembourgCalendar.DateAt(Later);
        var between = today.AddDays(13).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.IsTrue(today < DateOnly.ParseExact(between, "yyyy-MM-dd") && DateOnly.ParseExact(between, "yyyy-MM-dd") < laterToday);
        await fixture.AddStateAsync(between, "between");
        // The build report dates the Luxembourg channel's newest acquisition twenty days before the first answer: twenty days
        // old then, within the maximum; fifty days old a month later, past it.
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.BuildReportFileName),
            JsonSerializer.Serialize(new
            {
                schema = "lex-v3-first-mount-report/1",
                corpus = new { resourceId = "urn:uuid:00000000-0000-0000-0000-000000000000", Sha256 = fixture.CorpusSha256 },
                resumption = new
                {
                    statement = "This population was not observed in one window.",
                    luxembourg = new { resumedAt = ObservedAt.AddDays(-21).ToString("O"), catalogedAt = ObservedAt.AddDays(-20).ToString("O") },
                },
            }));
        var copy = Path.Combine(Path.GetTempPath(), $"lex-v3-replay-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(copy);
        try
        {
            foreach (var name in new[] { V3CorpusMount.IndexFileName, V3CorpusMount.CapabilityManifestFileName, V3CorpusMount.CorpusFileName, V3CorpusMount.BuildReportFileName })
            {
                File.Copy(Path.Combine(fixture.Directory, name), Path.Combine(copy, name));
            }

            using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
            using var copied = await V3CorpusMount.OpenAsync(copy, CancellationToken.None);
            Assert.IsNotNull(mount);
            Assert.IsNotNull(copied);

            var work = $"/lu-legilux/{fixture.WorkKey}";
            var date = fixture.ApplicabilityDate;
            const string undated = "article 2 de la loi du 10 août 1991";
            var todayText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            (string Operation, object Parameters)[] requests =
            [
                ("resolve", new { identifier = fixture.Permalink }),
                ("as_of", new { identifier = work, date, language = "fra" }),
                ("timeline", new { identifier = work, language = "fra" }),
                ("article_history", new { identifier = work, anchor = "art_2", language = "fra" }),
                ("diff", new { identifier = work, date_from = date, date_to = laterDate, language = "fra" }),
                ("changes_in_period", new { identifier = work, date_from = date, date_to = laterDate }),
                ("in_force_on", new { identifier = work, date, language = "fra" }),
                ("search", new { query = "Assemblée", language = "fra" }),
                ("coverage", new { }),
                ("provenance", new { identifier = work, date, language = "fra" }),
                ("dossier", new { identifier = work, language = "fra" }),
                ("citation", new { identifier = work, date }),
                ("cited_by", new { identifier = work }),
                ("verify", new { identifier = fixture.Permalink + "/art_2", language = "fra" }),
                ("relations", new { identifier = work }),
                ("evidence_bundle", new { identifier = work, date, language = "fra" }),
                ("classification", new { identifier = work, language = "fra" }),
                ("manifestation", new { identifier = work }),
                ("status_on", new { identifier = work, date, language = "fra" }),
                ("browse", new { }),
                ("ask", new { question = $"Quel est le texte de {work} au {date} ?" }),
                ("events", new { }),
                ("answer_drift", new { }),
                ("as_observed", new { identifier = work, date, language = "fra", snapshot = fixture.IndexSha256 }),
                // An exact reference that names no date, read at today in Luxembourg; and one asked with the search's date set
                // to the first answer's today, as the home page's box sends it, which is no date given only on that day.
                ("search", new { query = undated, language = "fra" }),
                ("search", new { query = undated, language = "fra", date = todayText }),
            ];
            CollectionAssert.AreEquivalent(
                V3RestRouteBinding.Served.Select(static binding => binding.OperationId).ToArray(),
                requests.Select(static request => request.Operation).Distinct().ToArray(),
                "every served operation has a replay case, so a new operation fails here until it has one.");

            // What moves with the moment asked, in every answer: the request's own members, and each channel's data age and
            // staleness and the envelope's, counted to the moment asked.
            string[] everyAnswer =
            [
                "context.freshness.channels[0].age_days", "context.freshness.channels[0].stale", "context.freshness.observed_at",
                "context.freshness.stale", "request_ref",
            ];
            foreach (var (index, (operation, parameters)) in requests.Index())
            {
                var label = $"{operation} (case {index})";
                // The same request (one trace identity) asked twice, and of a byte copy of the mount: the same bytes.
                var first = await AnswerAsync(mount, operation, parameters, ObservedAt, "replay-first");
                CollectionAssert.AreEqual(first, await AnswerAsync(mount, operation, parameters, ObservedAt, "replay-first"), $"{label}: asked again");
                CollectionAssert.AreEqual(first, await AnswerAsync(copied, operation, parameters, ObservedAt, "replay-first"), $"{label}: from a byte copy of the mount");

                // Asked a month later, by another request: only the members G2 names move.
                var later = await AnswerAsync(mount, operation, parameters, Later, "replay-later");
                var (earlier, then) = (JsonNode.Parse(first)!, JsonNode.Parse(later)!);
                string[] expected = index switch
                {
                    24 => [.. everyAnswer, "result.value.reference.opens.state_date", "result.value.reference.recognised.date.date"],
                    25 => [.. everyAnswer, "result.value.reference.opens.address", "result.value.reference.opens.address_kind"],
                    _ => everyAnswer,
                };
                CollectionAssert.AreEqual(expected.Order(StringComparer.Ordinal).ToArray(), MovedMembers(earlier, then), $"{label}: asked later by another request");

                var (channel, laterChannel) = (earlier["context"]!["freshness"]!["channels"]![0]!, then["context"]!["freshness"]!["channels"]![0]!);
                Assert.AreEqual(20, (int)channel["age_days"]!, label);
                Assert.AreEqual(50, (int)laterChannel["age_days"]!, label);
                Assert.IsFalse((bool)channel["stale"]! || (bool)earlier["context"]!["freshness"]!["stale"]!, label);
                Assert.IsTrue((bool)laterChannel["stale"]! && (bool)then["context"]!["freshness"]!["stale"]!, label);
            }

            // The undated reference: read at today in Luxembourg, so at each answer's own day, it opens the version that day
            // selects, at the act's readable address both times.
            var (undatedFirst, undatedLater) = (await ReferenceAsync(mount, requests[24].Parameters, ObservedAt), await ReferenceAsync(mount, requests[24].Parameters, Later));
            Assert.AreEqual(todayText, (string?)undatedFirst["recognised"]!["date"]!["date"]);
            Assert.AreEqual(laterToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (string?)undatedLater["recognised"]!["date"]!["date"]);
            Assert.AreEqual("today_in_luxembourg", (string?)undatedFirst["recognised"]!["date"]!["source"]);
            Assert.AreEqual("today_in_luxembourg", (string?)undatedLater["recognised"]!["date"]!["source"]);
            Assert.AreEqual(laterDate, (string?)undatedFirst["opens"]!["state_date"]);
            Assert.AreEqual(between, (string?)undatedLater["opens"]!["state_date"]);
            Assert.AreEqual("readable", (string?)undatedFirst["opens"]!["address_kind"]);
            Assert.AreEqual("readable", (string?)undatedLater["opens"]!["address_kind"]);

            // The box's date: today on the first day, so no date given and the readable address; a given date a month later,
            // so the permalink of the version it selects, which is the same version.
            var (boxFirst, boxLater) = (await ReferenceAsync(mount, requests[25].Parameters, ObservedAt), await ReferenceAsync(mount, requests[25].Parameters, Later));
            Assert.AreEqual("readable", (string?)boxFirst["opens"]!["address_kind"]);
            Assert.AreEqual("permalink", (string?)boxLater["opens"]!["address_kind"]);
            Assert.AreEqual(laterDate, (string?)boxFirst["opens"]!["state_date"]);
            Assert.AreEqual(laterDate, (string?)boxLater["opens"]!["state_date"]);
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [TestMethod]
    public async Task OneEvidenceBundleAndThePublisherFileAreEnoughToCheckItsTextAndRecomputeItsDerivedDigests()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var envelope = V3EnvelopeJson.ParseAndVerify(
            await AnswerAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }, ObservedAt, "replay-bundle"),
            V3OperationRegistry.Reviewed);
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        var bundle = envelope.Result!.Value;
        var state = bundle.GetProperty("states").EnumerateArray().Single();
        var articles = state.GetProperty("articles").EnumerateArray().ToArray();
        Assert.IsNotEmpty(articles);

        // The publisher's file, as the publisher serves it, hashes to the body digest the bundle names for its source.
        var publisherFile = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", "loi-1991-08-10-n3--2024-02-01--fr.bin"));
        var source = state.GetProperty("sources").EnumerateArray().Single();
        Assert.AreEqual(Sha256(publisherFile), source.GetProperty("body_sha256").GetString(), "the source's body digest is the publisher file's");
        Assert.AreEqual(publisherFile.Length, source.GetProperty("body_byte_length").GetInt32());
        CollectionAssert.AreEqual(
            new[] { source.GetProperty("body_sha256").GetString() },
            state.GetProperty("body_sha256s").EnumerateArray().Select(static value => value.GetString()).ToArray());

        // Each served article's text is in the publisher's file: the article with that id, its non-blank text nodes
        // in document order, outside the publisher's own scl: annotations and the authorial notes (which travel as
        // notes). So the text a quote carries is the publisher's, not only self-consistent with its digest.
        XNamespace scl = "http://www.scl.lu";
        var publisherArticles = XDocument.Load(new MemoryStream(publisherFile)).Descendants()
            .Where(static element => element.Name.LocalName == "article" && element.Attribute("id") is not null)
            .ToDictionary(static element => element.Attribute("id")!.Value, StringComparer.Ordinal);
        foreach (var article in articles)
        {
            var id = article.GetProperty("publisher_id").GetString()!;
            Assert.IsTrue(publisherArticles.TryGetValue(id, out var inFile), $"{id}: the publisher's file holds that article");
            var publisherText = string.Concat(inFile.DescendantNodes().OfType<XText>()
                .Where(node => node.Ancestors().TakeWhile(ancestor => ancestor != inFile)
                    .All(ancestor => ancestor.Name.Namespace != scl && ancestor.Name.LocalName != "authorialNote"))
                .Select(static node => node.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
            Assert.AreEqual(publisherText, article.GetProperty("text").GetString(), $"{id}: the served text is the publisher's");

            var text = Encoding.UTF8.GetBytes(article.GetProperty("text").GetString()!);
            Assert.AreEqual(Sha256(text), article.GetProperty("text_sha256").GetString(), $"{id}: the text digest is the served text's");
            Assert.AreEqual(text.Length, article.GetProperty("text_byte_length").GetInt32(), id);
            Assert.AreEqual(source.GetProperty("body_sha256").GetString(), article.GetProperty("body_sha256").GetString(), $"{id}: read from the named source");
            Assert.AreEqual(state.GetProperty("permalink").GetString() + "/" + Uri.EscapeDataString(id), article.GetProperty("article_permalink").GetString(), id);
        }

        // The article identities digest and the state digest, recomputed from the derivations the platform publishes
        // (provenance's `derivation`): length-prefixed UTF-8 fields in a fixed order after a domain tag.
        var identities = articles.Select(static article => article.GetProperty("article_identity_sha256").GetString()!)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(LengthPrefixedSha256(identities), state.GetProperty("article_identities_sha256").GetString(),
            "article_identities_sha256 is the digest of the identities in sorted order, each preceded by its length");

        // The fixture is an index 9: state digest /2, recomputed from the answer alone (G5; S7a serves the row's own facts with
        // each state, which the review of #980 left as an expected gap): the date's role and the state's kind, then the end the
        // publisher states (a derived end is not in the digest) to the French expression a translation takes its interval from.
        var stateSha256 = state.GetProperty("state_sha256").GetString()!;
        string Fact(string name) => state.GetProperty(name).ValueKind == JsonValueKind.Null ? "" : state.GetProperty(name).GetString()!;
        string[] rowFacts =
        [
            Fact("date_role"),
            Fact("state_kind"),
            state.GetProperty("applicable_to_source").ValueKind == JsonValueKind.String &&
                state.GetProperty("applicable_to_source").GetString() == "publisher" ? Fact("applicable_to") : "",
            Fact("publication_date"),
            Fact("text_status"),
            Fact("text_status_reason"),
            Fact("in_file_identity"),
            Fact("in_file_date"),
            Fact("entry_into_force"),
            Fact("translation_of"),
        ];
        CollectionAssert.AreEqual(RowFactsOf(fixture.Directory, stateSha256), rowFacts,
            "the answer carries the row's own facts as the mounted index holds them");
        var profiles = state.GetProperty("rule_profile_sha256s").EnumerateArray().Select(static value => value.GetString()!)
            .Order(StringComparer.Ordinal).ToArray();
        string[] fields =
        [
            "lex-v3-luxembourg-expression-state/2",
            bundle.GetProperty("publisher").GetString()!,
            bundle.GetProperty("work_key").GetString()!,
            state.GetProperty("applicability_date").GetString()!,
            rowFacts[0],
            rowFacts[1],
            state.GetProperty("expression_iri").GetString()!,
            state.GetProperty("publisher_work_iri").GetString()!,
            state.GetProperty("publisher_legal_resource_iri").GetString()!,
            state.GetProperty("language").GetString()!,
            .. rowFacts[2..],
            profiles.Length.ToString(CultureInfo.InvariantCulture),
            .. profiles,
            identities.Length.ToString(CultureInfo.InvariantCulture),
            .. identities,
        ];
        Assert.AreEqual(LengthPrefixedSha256(fields), stateSha256,
            "the state digest recomputes from the values provenance's derivation names, in its order, the domain tag first");
        StringAssert.EndsWith(state.GetProperty("permalink").GetString(), "--" + stateSha256 + "/" + state.GetProperty("language").GetString(), "the permalink pins that digest, in its language (R64)");

        // The derivation the reader followed is the one the platform states: the domain tag is length-prefixed like
        // every value after it (an earlier wording put it outside that clause, and a reader who followed it got another
        // digest).
        var provenance = V3EnvelopeJson.ParseAndVerify(
            await AnswerAsync(mount, "provenance", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }, ObservedAt, "replay-provenance"),
            V3OperationRegistry.Reviewed).Result!.Value.GetProperty("derivation").GetString();
        StringAssert.StartsWith(provenance,
            "state_sha256 is state digest /2, a SHA-256 over these values, each as UTF-8 preceded by its length as four bytes big-endian: " +
            "the domain tag lex-v3-luxembourg-expression-state/2, the publisher, the work key, the applicability date, the date's role, the " +
            "state's kind, the expression, the publisher work IRI, the publisher legal-resource IRI, the language, the end the publisher " +
            "states, the publication date, the text status, its reason, the in-file identity, the in-file date, the entry into force and the " +
            "French expression a translation takes its interval from (each the empty string when there is none), then the number of " +
            "rule-profile digests as decimal text and each digest in sorted order, then the number of article identities as decimal text and " +
            "each identity in sorted order;");
        StringAssert.Contains(provenance, "so a caller recomputes the digest from this answer and the article identities",
            "and it says the answer carries the row facts G5 needs");
        Assert.IsFalse(provenance.Contains("cannot recompute", StringComparison.Ordinal), "the expected gap of #980 is closed");
    }

    /// <summary>
    /// The facts state digest /2 takes from an index 9's row, read from the mounted index to hold the answer's copy to it, in the derivation's order
    /// (the date's role and the state's kind, then the publisher's end to the translation's French expression), each the
    /// empty string when there is none.
    /// </summary>
    private static string[] RowFactsOf(string mountDirectory, string stateSha256)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(mountDirectory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT date_role,state_kind,CASE WHEN applicable_to_source='publisher' THEN applicable_to END,publication_date," +
            "text_status,text_status_reason,in_file_identity,in_file_date,entry_into_force,translation_of FROM states WHERE state_sha256=$s";
        command.Parameters.AddWithValue("$s", stateSha256);
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read(), "the bundle's state is a row of the mounted index");
        var facts = Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? "" : reader.GetString(index)).ToArray();
        Assert.IsFalse(reader.Read());
        return facts;
    }

    [TestMethod]
    public async Task AReplacedPublisherFileIsAFileReplacedEventAndANewVersionIdOnlyWhenItsTextChanged()
    {
        using var chain = await ChainAsync();
        Assert.AreEqual(chain.Versions[0], chain.Versions[1], "new bytes with the same text: the same version id");
        Assert.AreNotEqual(chain.Versions[1], chain.Versions[2], "one article reworded: a new version id");

        using var mount = await V3CorpusMount.OpenAsync(chain.Mounts[2], CancellationToken.None);
        Assert.IsNotNull(mount);
        var events = await ResultAsync(mount, "events", new { });
        Assert.IsFalse(events.GetProperty("has_more").GetBoolean());
        var ends = events.GetProperty("log").GetProperty("ancestors").EnumerateArray().Select(static ancestor => ancestor.GetProperty("last_seq").GetInt64()).ToArray();
        var replaced = events.GetProperty("events").EnumerateArray().Where(static row => row.GetProperty("event").GetString() == "file_replaced").ToArray();
        Assert.HasCount(2, replaced, "one file_replaced for each replaced file");
        var seqs = replaced.Select(static row => row.GetProperty("seq").GetInt64()).ToArray();
        Assert.IsTrue(seqs[0] > ends[0] && seqs[0] <= ends[1], "the first in the second build's events");
        Assert.IsGreaterThan(ends[1], seqs[1], "the second in the third build's events");

        // Each names the version it replaced and the version it holds, read from bodies that differ.
        CollectionAssert.AreEqual(
            new[] { (chain.Versions[0], chain.Versions[1]), (chain.Versions[1], chain.Versions[2]) },
            replaced.Select(static row => (row.GetProperty("replaced_permalink").GetString(), row.GetProperty("permalink").GetString())).ToArray());
        foreach (var row in replaced)
        {
            CollectionAssert.AreNotEqual(
                row.GetProperty("detail").GetProperty("replaced_source_body_sha256").EnumerateArray().Select(static body => body.GetString()).ToArray(),
                row.GetProperty("detail").GetProperty("source_body_sha256").EnumerateArray().Select(static body => body.GetString()).ToArray(),
                "the bodies differ");
        }

        // The earlier version id is immutable: on the latest mount it still verifies as the same state, and names the
        // version that replaced it.
        var verified = await ResultAsync(mount, "verify", new { identifier = chain.Versions[0] });
        Assert.AreEqual("digest_matches", verified.GetProperty("verdict").GetString());
        Assert.AreEqual(chain.Versions[2], verified.GetProperty("superseded_by").GetProperty("permalink").GetString());
    }

    [TestMethod]
    public async Task NothingIsHardDeletedAcrossBuildsEveryEventIsCarriedAndEveryStateTheLogNamesStillVerifies()
    {
        using var chain = await ChainAsync();
        using var mount = await V3CorpusMount.OpenAsync(chain.Mounts[2], CancellationToken.None);
        Assert.IsNotNull(mount);
        var latest = (await ResultAsync(mount, "events", new { })).GetProperty("events").EnumerateArray().ToArray();

        // Each earlier build's events are the latest log's prefix, row for row: no event a build answered is removed or
        // rewritten by a later one. Only the cursor differs, since it names the log it was read from.
        for (var build = 0; build < 2; build++)
        {
            using var earlier = await V3CorpusMount.OpenAsync(chain.Mounts[build], CancellationToken.None);
            Assert.IsNotNull(earlier);
            var rows = (await ResultAsync(earlier, "events", new { })).GetProperty("events").EnumerateArray().ToArray();
            Assert.IsLessThan(latest.Length, rows.Length, $"build {build + 1}: a later build appends");
            foreach (var (row, carried) in rows.Zip(latest))
            {
                Assert.AreEqual(WithoutCursor(row), WithoutCursor(carried), $"build {build + 1}: event {row.GetProperty("seq").GetInt64()} is carried unchanged");
            }
        }

        // Every state the log names, held or replaced, still verifies on the latest mount: the original version from a
        // kept generation, the reworded one from the mounted index.
        var named = latest.Where(static row => row.GetProperty("state_sha256").ValueKind == JsonValueKind.String)
            .Select(static row => row.GetProperty("permalink").GetString()!)
            .Concat(latest.Select(static row => row.GetProperty("replaced_permalink")).Where(static value => value.ValueKind == JsonValueKind.String).Select(static value => value.GetString()!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { chain.Versions[0], chain.Versions[2] }, named);
        foreach (var permalink in named)
        {
            Assert.AreEqual("digest_matches", (await ResultAsync(mount, "verify", new { identifier = permalink })).GetProperty("verdict").GetString(), permalink);
        }
    }

    [TestMethod]
    public async Task EverySnapshotTheLogNamesAnswersAsObservedWhatThatBuildAnsweredBoundedByItsBuildTime()
    {
        using var chain = await ChainAsync();
        var identifier = $"/lu-legilux/{chain.WorkKey}";
        using var mount = await V3CorpusMount.OpenAsync(chain.Mounts[2], CancellationToken.None);
        Assert.IsNotNull(mount);
        var log = (await ResultAsync(mount, "events", new { })).GetProperty("log");
        CollectionAssert.AreEqual(
            chain.IndexSha256s,
            log.GetProperty("ancestors").EnumerateArray().Select(static ancestor => ancestor.GetProperty("log_id").GetString()).Append(log.GetProperty("log_id").GetString()).ToArray(),
            "the log names the three builds' snapshots, the mounted one last");

        for (var build = 0; build < 3; build++)
        {
            using var then = await V3CorpusMount.OpenAsync(chain.Mounts[build], CancellationToken.None);
            Assert.IsNotNull(then);
            var answered = (await ResultAsync(then, "as_of", new { identifier, date = "2030-01-01" })).GetProperty("states")[0];
            var date = answered.GetProperty("applicability_date").GetString()!;

            var observed = await ResultAsync(mount, "as_observed", new { identifier, date, snapshot = chain.IndexSha256s[build] });
            var snapshot = observed.GetProperty("snapshot");
            Assert.AreEqual(build + 1, snapshot.GetProperty("observation").GetInt64());
            Assert.AreEqual(3, snapshot.GetProperty("observations_in_log").GetInt64());
            Assert.AreEqual(build == 2, snapshot.GetProperty("mounted").GetBoolean());
            Assert.AreEqual(chain.BuiltAts[build], snapshot.GetProperty("observed_no_later_than").GetString(), $"build {build + 1}: bounded by its build time");
            Assert.IsFalse(snapshot.GetProperty("observation_time_held").GetBoolean(), "no observation time is claimed");

            // The state that build answered, field for field: the version, its article identities and their dates.
            var state = observed.GetProperty("states").EnumerateArray().Single();
            Assert.IsTrue(state.GetProperty("text_held").GetBoolean(), $"build {build + 1}: its state is kept in full");
            Assert.AreEqual(JsonNode.Parse(answered.GetRawText())!.ToJsonString(), WithoutTextSource(state), $"build {build + 1}: the state it answered");

            // Served from the newest kept build that holds that version: the second build for the first two, which hold the
            // same version, and the mounted index for the latest.
            var textFrom = state.GetProperty("text_from");
            if (build == 2)
            {
                Assert.AreEqual(JsonValueKind.Null, textFrom.ValueKind, "the mounted build's state is the mounted index's");
            }
            else
            {
                Assert.AreEqual(chain.IndexSha256s[1], textFrom.GetProperty("snapshot_id").GetString(), $"build {build + 1}: from the newest generation holding its version");
            }
        }
    }

    /// <summary>The chain the G1, G3 and G4 cases run over: each build's mount, index digest, build time and as-of version.</summary>
    internal sealed record Chain(string[] Mounts, string[] IndexSha256s, string[] BuiltAts, string[] Versions, string WorkKey) : IDisposable
    {
        public void Dispose()
        {
            foreach (var directory in Mounts)
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Three real builds of the state fixture, one a day, each chained to the one before: the publisher's file as held,
    /// then with one byte added and its text unchanged, then with one article reworded. Each is written as the mount it
    /// would be, with the earlier builds kept beside it whole and the retention line's record, and verified.
    /// </summary>
    internal static async Task<Chain> ChainAsync()
    {
        Func<string, string>?[] files =
        [
            null,
            static xml => xml + "\n",
            static xml => LuxembourgIndexBuilderTests.ReplaceFirst(xml, "assemblée générale", "assemblée plénière"),
        ];
        var mounts = new List<string>();
        var indexes = new List<string>();
        var builtAts = new List<string>();
        var versions = new List<string>();
        try
        {
            LuxembourgIndexBuildResult? previous = null;
            for (var build = 0; build < files.Length; build++)
            {
                var (envelope, built, _) = await LuxembourgIndexBuilderTests.BuildStateEnvelopeAsync(files[build]);
                var builtAt = LuxembourgIndexBuilderTests.BuiltAt.AddDays(build);
                if (previous is not null)
                {
                    var predecessor = LuxembourgIndexPredecessor.TryRead(previous.IndexRef, previous.IndexBytes.Span, out var readRefusal, out var readDetail);
                    Assert.IsNotNull(predecessor, $"{readRefusal}: {readDetail}");
                    built = LuxembourgIndexBuilder.TryBuild(envelope, predecessor, builtAt, out var refusal, out var detail);
                    Assert.IsNotNull(built, $"build {build + 1}: {refusal}: {detail}");
                }

                var directory = V3CorpusGenerationQuoteTests.WriteMount(envelope, built);
                mounts.Add(directory);
                foreach (var (earlier, index) in mounts.SkipLast(1).Zip(indexes))
                {
                    var kept = Path.Combine(directory, V3CorpusMountWriter.GenerationsDirectoryName, index);
                    Directory.CreateDirectory(kept);
                    foreach (var file in Directory.GetFiles(earlier)) File.Copy(file, Path.Combine(kept, Path.GetFileName(file)));
                }

                await V3CorpusMountWriter.WriteRetentionRecordAsync(directory, built.IndexRef, built.IndexBytes, CancellationToken.None, indexes.ToHashSet(StringComparer.Ordinal));
                var verified = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
                Assert.IsTrue(verified.Verified, $"build {build + 1}: {verified.Detail}");
                indexes.Add(built.IndexRef.Sha256);
                builtAts.Add(builtAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                previous = built;
            }

            string workKey;
            using (var first = await V3CorpusMount.OpenAsync(mounts[0], CancellationToken.None))
            {
                Assert.IsNotNull(first);
                workKey = (await ResultAsync(first, "events", new { })).GetProperty("events")[0].GetProperty("work_key").GetString()!;
            }

            foreach (var directory in mounts)
            {
                using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
                Assert.IsNotNull(mount);
                versions.Add((await ResultAsync(mount, "as_of", new { identifier = $"/lu-legilux/{workKey}", date = "2030-01-01" }))
                    .GetProperty("states")[0].GetProperty("permalink").GetString()!);
            }

            return new Chain([.. mounts], [.. indexes], [.. builtAts], [.. versions], workKey);
        }
        catch
        {
            foreach (var directory in mounts.Where(Directory.Exists)) Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    /// <summary>One answer's result, through the real handler, with no refusal.</summary>
    private static async Task<JsonElement> ResultAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var envelope = V3EnvelopeJson.ParseAndVerify(await AnswerAsync(mount, operation, parameters, ObservedAt, "replay-" + operation), V3OperationRegistry.Reviewed);
        Assert.IsNull(envelope.Refusal, $"{operation}: {envelope.Refusal?.Code}");
        return envelope.Result!.Value;
    }

    /// <summary>An <c>as_observed</c> state without what says where its text was read from, which <c>as_of</c> does not carry.</summary>
    private static string WithoutTextSource(JsonElement state)
    {
        var node = JsonNode.Parse(state.GetRawText())!.AsObject();
        node.Remove("text_held");
        node.Remove("text_from");
        return node.ToJsonString();
    }

    private static string WithoutCursor(JsonElement row)
    {
        var node = JsonNode.Parse(row.GetRawText())!.AsObject();
        node.Remove("cursor");
        return node.ToJsonString();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>SHA-256 over each value as UTF-8 preceded by its length as four bytes big-endian, as the derivation reads.</summary>
    private static string LengthPrefixedSha256(IEnumerable<string> fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The whole response bytes of one REST request with its own trace identity, answered at <paramref name="observedAt"/>.</summary>
    private static async Task<byte[]> AnswerAsync(
        V3CorpusMount mount, string operation, object parameters, DateTimeOffset observedAt, string traceIdentifier)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext { TraceIdentifier = traceIdentifier };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), () => observedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, $"{operation}: {Encoding.UTF8.GetString(bytes)}");
        V3EnvelopeJson.ParseAndVerify(bytes, V3OperationRegistry.Reviewed);
        return bytes;
    }

    /// <summary>A search answer's reference (what it recognised and what it opens), asked at <paramref name="observedAt"/>.</summary>
    private static async Task<JsonNode> ReferenceAsync(V3CorpusMount mount, object parameters, DateTimeOffset observedAt)
    {
        var answer = JsonNode.Parse(await AnswerAsync(mount, "search", parameters, observedAt, "replay-reference"))!;
        Assert.AreEqual("answer", (string?)answer["verdict"], answer.ToJsonString());
        var reference = answer["result"]!["value"]!["reference"]!;
        Assert.AreEqual("opens", (string?)reference["outcome"], reference.ToJsonString());
        return reference;
    }

    /// <summary>
    /// The members of two envelopes whose values differ, each by its path (<c>context.freshness.channels[0].age_days</c>), in
    /// ordinal order; a member one has and the other lacks is one that moved.
    /// </summary>
    private static string[] MovedMembers(JsonNode? one, JsonNode? other)
    {
        var moved = new List<string>();
        Walk(one, other, "");
        return [.. moved.Order(StringComparer.Ordinal)];

        void Walk(JsonNode? a, JsonNode? b, string path)
        {
            if (a is JsonObject left && b is JsonObject right)
            {
                foreach (var key in left.Select(static pair => pair.Key).Union(right.Select(static pair => pair.Key)))
                {
                    var member = path.Length == 0 ? key : $"{path}.{key}";
                    if (left.ContainsKey(key) != right.ContainsKey(key)) moved.Add(member);
                    else Walk(left[key], right[key], member);
                }
            }
            else if (a is JsonArray first && b is JsonArray second && first.Count == second.Count)
            {
                for (var index = 0; index < first.Count; index++) Walk(first[index], second[index], $"{path}[{index}]");
            }
            else if (!JsonNode.DeepEquals(a, b))
            {
                moved.Add(path);
            }
        }
    }
}
