using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;
using static Lex.V3.Ingest.Tests.LuxembourgIndexCatalogueStatesTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The publisher's relation records served (R29, R72, R21): over a verified mount of index 10, <c>relations</c> returns the typed
/// edges of a work in both directions, each with the acting act's dates by role and the inverse views derived; the dossier carries
/// the relation tabs' data and R72's notice, which fires when every consolidated version in the catalogue was published before an
/// amending act (or there is none), and not otherwise; an index 9 mount of the same rows answers as before, claiming nothing.
/// Every value is synthetic, driven through the real handler.
/// </summary>
[TestClass]
public sealed class V3CorpusPublisherRelationsMountTests
{
    private const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";

    /// <summary>
    /// The relations fixture and five works for R72: one never consolidated and amended later, one consolidated after its
    /// amending act, one whose consolidation names no publication date, one amended by an act of no date, one with no relation.
    /// </summary>
    private static Harness NoticeFixture()
    {
        var h = LuxembourgPublisherRelationsTests.RelationsFixture();
        void Act(string work, string entry)
        {
            h.State(W(work + "/jo"), entryIntoForce: entry, publication: entry, memberOf: W(work), expressions: ("fr", Fra))
                .Held(W(work + "/jo"), ["art_1"], inFileDate: entry);
        }

        Act("loi/2011/01/01/n1", "2011-01-05");
        h.Fact(W("loi/2012/01/01/n7/jo"), "publicationDate", "2012-01-03").Relation(W("loi/2012/01/01/n7/jo"), "modifies", W("loi/2011/01/01/n1/jo"));
        Act("loi/2012/01/01/n1", "2012-01-05");
        h.State(W("loi/2012/01/01/n1/consolide/20190101"), "2019-01-01", publication: "2019-01-05", memberOf: W("loi/2012/01/01/n1"), expressions: ("fr", Fra));
        h.Fact(W("loi/2018/01/01/n7/jo"), "publicationDate", "2018-01-03").Relation(W("loi/2018/01/01/n7/jo"), "modifies", W("loi/2012/01/01/n1/jo"));
        Act("loi/2013/01/01/n1", "2013-01-05");
        h.State(W("loi/2013/01/01/n1/consolide/20190101"), "2019-01-01", memberOf: W("loi/2013/01/01/n1"), expressions: ("fr", Fra));
        h.Fact(W("loi/2020/01/01/n7/jo"), "publicationDate", "2020-01-03").Relation(W("loi/2020/01/01/n7/jo"), "modifies", W("loi/2013/01/01/n1/jo"));
        Act("loi/2014/01/01/n1", "2014-01-05");
        h.Relation(W("loi/2021/01/01/n7/jo"), "modifies", W("loi/2014/01/01/n1/jo"));
        Act("loi/2015/01/01/n1", "2015-01-05");
        return h;
    }

    /// <summary>A verified mount of the harness's index (or of the index 9 it becomes), bound to the shared acquisition's corpus.</summary>
    private sealed class RelationsMount : IAsyncDisposable
    {
        private readonly MountedFixture _shared;

        private RelationsMount(MountedFixture shared, V3CorpusMount mount)
        {
            _shared = shared;
            Mount = mount;
        }

        public V3CorpusMount Mount { get; }

        public static async Task<RelationsMount> CreateAsync(Harness harness, bool asIndexNine = false)
        {
            var shared = await MountedFixture.CreateAsync();
            var corpusRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(shared.CorpusSha256), shared.CorpusSha256);
            var built = harness.BuildFor(corpusRef);
            var index = asIndexNine ? LuxembourgIndexBuilderTests.Schema9Of(built.IndexBytes.Span) : built.IndexBytes.ToArray();
            var reference = LuxembourgIndexBuilderTests.IndexRefOf(index);
            using var manifest = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(manifest, LuxembourgIndexBuilderTests.RebindManifest(built.CapabilityManifest, reference.Sha256));
            await File.WriteAllBytesAsync(Path.Combine(shared.Directory, V3CorpusMount.IndexFileName), index);
            await File.WriteAllBytesAsync(Path.Combine(shared.Directory, V3CorpusMount.CapabilityManifestFileName), manifest.ToArray());
            var mount = V3CorpusMount.Served(await V3CorpusMount.OpenAsync(shared.Directory, CancellationToken.None), shared.Directory);
            Assert.IsNotNull(mount);
            return new RelationsMount(shared, mount);
        }

        public async ValueTask DisposeAsync()
        {
            Mount.Dispose();
            await _shared.DisposeAsync();
        }
    }

    private static async Task<JsonElement> AnswerAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var envelope = await V3CorpusIntervalMountTests.AskAsync(mount, operation, parameters);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.HelpfulPayload.GetRawText());
        return envelope.Result!.Value.Clone();
    }

    private static string Short(string iri) => iri.StartsWith(Eli, StringComparison.Ordinal) ? iri[Eli.Length..] : iri;

    [TestMethod]
    public async Task RelationsServesTheTypedEdgesOfAWorkInBothDirectionsWithDatesByRoleAndTheInverseViewsDerived()
    {
        await using var fixture = await RelationsMount.CreateAsync(NoticeFixture());
        var value = await AnswerAsync(fixture.Mount, "relations", new { identifier = "/lu-legilux/loi-2010-01-01-n1" });
        Assert.AreEqual("cites", value.GetProperty("edge_type").GetString(), "the references' own members are as before");
        var relations = value.GetProperty("publisher_relations");
        Assert.AreEqual(V3CorpusMount.PublisherRelationsRule, relations.GetProperty("rule").GetString());
        Assert.AreEqual("derived from the publisher's forward relation", relations.GetProperty("inverse_label").GetString());
        StringAssert.Contains(relations.GetProperty("not_seen").GetString(), "under code, loi and rgd");

        var edges = relations.GetProperty("edges").EnumerateArray().ToArray();
        string Edge(JsonElement edge) =>
            $"{edge.GetProperty("view").GetString()} {Short(edge.GetProperty("other").GetProperty("iri").GetString()!)} " +
            $"derived={edge.GetProperty("derived").GetBoolean()} published={edge.GetProperty("acting_publication_date").GetString() ?? "-"} " +
            $"entry={edge.GetProperty("acting_entry_into_force").GetString() ?? "-"}";
        CollectionAssert.AreEqual(
            new[]
            {
                "amended_by loi/2016/06/01/n1/jo derived=True published=2016-06-03 entry=2016-06-07",
                "amends loi/2000/01/01/n1/jo derived=False published=2010-01-03 entry=2010-01-05",
                "repealed_by rgd/2017/01/01/n1/jo derived=True published=- entry=-",
                "modified_temporarily_by loi/2020/05/12/a385/jo derived=False published=2020-05-12 entry=-",
                "rectifies loi/2009/12/01/n1/jo derived=False published=2010-01-03 entry=2010-01-05",
                "impact_received loi/2016/06/01/n1/jo derived=True published=2016-06-03 entry=2016-06-07",
            },
            edges.Select(Edge).ToArray(),
            "each relation from this act's side, in the views' order: the inverse views and the impact record derived");
        var amendedBy = edges[0];
        Assert.AreEqual("jolux:modifies", amendedBy.GetProperty("source_predicate").GetString());
        Assert.AreEqual("inbound", amendedBy.GetProperty("direction").GetString());
        Assert.AreEqual("other_act", amendedBy.GetProperty("asserted_on").GetString());
        Assert.AreEqual(Harness.RelationEvidence, amendedBy.GetProperty("evidence_sha256").GetString());
        var other = amendedBy.GetProperty("other");
        Assert.AreEqual("Loi synthétique du 1er juin 2016 modifiant une loi", other.GetProperty("title").GetString());
        Assert.AreEqual("fra", other.GetProperty("title_language").GetString());
        Assert.AreEqual("/lu-legilux/loi-2016-06-01-n1", other.GetProperty("address").GetString(), "an act the catalogue describes has its record's address");
        Assert.IsFalse(other.GetProperty("held").GetBoolean());
        Assert.AreEqual(("2016-06-03", "2016-06-07"), (other.GetProperty("publication_date").GetString(), other.GetProperty("entry_into_force").GetString()),
            "the other act's own dates, each its own value");
        Assert.AreEqual(JsonValueKind.Null, edges[1].GetProperty("other").GetProperty("publication_date").ValueKind,
            "an act this act amends is dated by its own facts, which state none here, never by this act's");
        Assert.AreEqual(JsonValueKind.Null, edges[2].GetProperty("other").GetProperty("address").ValueKind, "an act the catalogue does not describe has none");
        var impact = edges[^1].GetProperty("impact");
        Assert.AreEqual("1", impact.GetProperty("type_code").GetString());
        Assert.AreEqual(JsonValueKind.Null, impact.GetProperty("consolidated_by_iri").ValueKind);
        Assert.AreEqual("jolux:impactConsolidatedBy", impact.GetProperty("ambiguous_parts")[0].GetString());

        var counts = relations.GetProperty("edge_counts");
        Assert.AreEqual(1, counts.GetProperty("amended_by").GetInt32());
        Assert.AreEqual(0, counts.GetProperty("modifies_temporarily").GetInt32());
        Assert.AreEqual(edges.Length, relations.GetProperty("edge_count").GetInt32());
        foreach (var family in relations.GetProperty("families").EnumerateArray())
        {
            Assert.AreEqual("acquired_complete", family.GetProperty("state").GetString());
            CollectionAssert.AreEqual(new[] { "code", "loi", "rgd" }, family.GetProperty("subject_classes").EnumerateArray().Select(static value => value.GetString()).ToArray());
        }

        // The not-held list says the records are served here, not that they are not held.
        var structured = value.GetProperty("not_held").EnumerateArray().Single(static row => row.GetProperty("item").GetString() == "structured_relations");
        Assert.AreEqual(V3CorpusMount.RelationsStructuredRelationsTen, structured.GetProperty("reason").GetString());

        // One side asked, one side's edges.
        var outbound = (await AnswerAsync(fixture.Mount, "relations", new { identifier = "/lu-legilux/loi-2010-01-01-n1", direction = "outbound" }))
            .GetProperty("publisher_relations");
        Assert.IsTrue(outbound.GetProperty("edges").EnumerateArray().All(static edge => edge.GetProperty("direction").GetString() == "outbound"));
        Assert.AreEqual(3, outbound.GetProperty("edge_count").GetInt32());

        // From the other side: the amending act's record names the act it amends, asserted on itself.
        var cited = await AnswerAsync(fixture.Mount, "cited_by", new { identifier = "/lu-legilux/loi-2010-01-01-n1" });
        Assert.AreEqual(V3CorpusMount.ReferencesStructuredRelationsTen,
            cited.GetProperty("not_held").EnumerateArray().Single(static row => row.GetProperty("item").GetString() == "structured_relations").GetProperty("reason").GetString());
        Assert.IsFalse(cited.TryGetProperty("publisher_relations", out _), "cited_by serves the references only");
    }

    [TestMethod]
    public async Task TheDossierCarriesTheRelationTabsAndR72FiresOnlyWhenAnAmendingActIsLaterThanEveryConsolidatedVersion()
    {
        await using var fixture = await RelationsMount.CreateAsync(NoticeFixture());
        async Task<JsonElement> NoticeOf(string work)
        {
            var dossier = await AnswerAsync(fixture.Mount, "dossier", new { identifier = "/lu-legilux/" + work });
            Assert.IsTrue(dossier.TryGetProperty("publisher_relations", out _), work);
            var notice = dossier.GetProperty("amended_after_consolidation");
            Assert.AreEqual(V3CorpusMount.AmendedAfterConsolidationRule, notice.GetProperty("rule").GetString());
            Assert.IsTrue(notice.GetProperty("derived").GetBoolean());
            return notice;
        }

        // Consolidated on 2015-01-10, amended by an act published on 2016-06-03 (and by none other recorded).
        var fires = await NoticeOf("loi-2010-01-01-n1");
        Assert.AreEqual("notice", fires.GetProperty("status").GetString());
        Assert.AreEqual("2015-01-10", fires.GetProperty("latest_consolidation_publication_date").GetString());
        Assert.AreEqual("2015-01-01", fires.GetProperty("latest_consolidation").GetProperty("applicability_date").GetString());
        // The latest consolidated version is a state whose text is not held: its permalink, as the notice names it, is refused by
        // verify and resolve for its text (#1008), naming that state, never identifier_unknown.
        var latest = fires.GetProperty("latest_consolidation");
        Assert.AreEqual("not_acquired", latest.GetProperty("text_status").GetString());
        foreach (var operation in new[] { "verify", "resolve" })
        {
            var refused = await V3CorpusIntervalMountTests.AskAsync(fixture.Mount, operation, new { identifier = latest.GetProperty("permalink").GetString() });
            Assert.AreEqual(V3Verdicts.Refuse, refused.Verdict, operation);
            Assert.AreEqual("text_not_available", refused.Refusal!.Code, operation);
            StringAssert.Contains(refused.Refusal.HelpfulPayload.GetRawText(), latest.GetProperty("state_sha256").GetString(), operation);
        }
        var act = fires.GetProperty("acts").EnumerateArray().Single();
        Assert.AreEqual(("2016-06-03", W("loi/2016/06/01/n1/jo")), (act.GetProperty("publication_date").GetString(), act.GetProperty("acting_iri").GetString()));
        Assert.AreEqual(
            "An act published on 2016-06-03 (Loi synthétique du 1er juin 2016 modifiant une loi, http://data.legilux.public.lu/eli/etat/leg/loi/2016/06/01/n1/jo) " +
            "amends this act, according to the publisher's relation records (derived from the publisher's forward relation); no consolidated version in the " +
            "publisher's catalogue, as this index read it, was published after it (the latest was published on 2015-01-10).",
            fires.GetProperty("notice").GetString());

        // Never consolidated, amended later: fires, naming no consolidated version.
        var never = await NoticeOf("loi-2011-01-01-n1");
        Assert.AreEqual("notice", never.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, never.GetProperty("latest_consolidation").ValueKind);
        StringAssert.EndsWith(never.GetProperty("notice").GetString(), "the publisher's catalogue, as this index read it, lists no consolidated version of this act.");

        // Consolidated after its amending act: no notice.
        var consolidated = await NoticeOf("loi-2012-01-01-n1");
        Assert.AreEqual("no_later_amending_act", consolidated.GetProperty("status").GetString());
        Assert.AreEqual(0, consolidated.GetProperty("acts").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, consolidated.GetProperty("notice").ValueKind);

        // A consolidated version without a publication date: the comparison cannot be made.
        var undated = await NoticeOf("loi-2013-01-01-n1");
        Assert.AreEqual("undetermined", undated.GetProperty("status").GetString());
        Assert.AreEqual(1, undated.GetProperty("consolidations_without_publication_date").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, undated.GetProperty("notice").ValueKind);

        // An amending act without a publication date, no consolidated version: undetermined.
        var undatedAct = await NoticeOf("loi-2014-01-01-n1");
        Assert.AreEqual("undetermined", undatedAct.GetProperty("status").GetString());
        Assert.AreEqual(1, undatedAct.GetProperty("amending_acts_without_publication_date").GetInt32());

        // No relation recorded.
        Assert.AreEqual("no_amending_act_recorded", (await NoticeOf("loi-2015-01-01-n1")).GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task ARelationNotAcquiredInFullIsSaidSoAndRaisesNoNoticeAndNoAbsence()
    {
        // A run whose relation family was not acquired holds no relation row and says so for each relation.
        var unacquired = new Harness();
        unacquired.State(W("loi/2010/01/01/n1/jo"), entryIntoForce: "2010-01-05", publication: "2010-01-03", memberOf: W("loi/2010/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2010/01/01/n1/jo"), ["art_1"], inFileDate: "2010-01-05")
            .RelationFamilies("unacquired");
        await using var fixture = await RelationsMount.CreateAsync(unacquired);
        var dossier = await AnswerAsync(fixture.Mount, "dossier", new { identifier = "/lu-legilux/loi-2010-01-01-n1" });
        var relations = dossier.GetProperty("publisher_relations");
        Assert.AreEqual(0, relations.GetProperty("edge_count").GetInt32());
        Assert.IsTrue(relations.GetProperty("families").EnumerateArray().All(static family =>
            family.GetProperty("state").GetString() == "unacquired" && family.GetProperty("subject_classes").ValueKind == JsonValueKind.Null &&
            family.GetProperty("reason").GetString()!.Length > 0));
        StringAssert.StartsWith(relations.GetProperty("not_seen").GetString(), "no relation family is held as acquired in full");
        var notice = dossier.GetProperty("amended_after_consolidation");
        Assert.AreEqual("not_acquired", notice.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, notice.GetProperty("notice").ValueKind);
    }

    /// <summary>
    /// The same rows mounted as index 9 (index 10 less its relation tables) answer exactly as index 10 does without what index 10
    /// adds: no relation record, no notice, the not-held list's words for an index that holds none; nothing else differs but the
    /// index digest.
    /// </summary>
    [TestMethod]
    public async Task AnIndexNineMountAnswersAsBeforeAndClaimsNoNewMember()
    {
        await using var ten = await RelationsMount.CreateAsync(NoticeFixture());
        await using var nine = await RelationsMount.CreateAsync(NoticeFixture(), asIndexNine: true);
        foreach (var (operation, parameters) in new (string, object)[]
                 {
                     ("dossier", new { identifier = "/lu-legilux/loi-2010-01-01-n1" }),
                     ("relations", new { identifier = "/lu-legilux/loi-2010-01-01-n1" }),
                     ("cited_by", new { identifier = "/lu-legilux/loi-2010-01-01-n1" }),
                     ("citation", new { identifier = "/lu-legilux/loi-2010-01-01-n1", date = "2010-01-05" }),
                 })
        {
            var tenAnswer = JsonNode.Parse((await AnswerAsync(ten.Mount, operation, parameters)).GetRawText())!.AsObject();
            var nineAnswer = JsonNode.Parse((await AnswerAsync(nine.Mount, operation, parameters)).GetRawText())!.AsObject();
            Assert.IsFalse(nineAnswer.ContainsKey("publisher_relations"), operation);
            Assert.IsFalse(nineAnswer.ContainsKey("amended_after_consolidation"), operation);
            if (nineAnswer["not_held"] is JsonArray rows && rows.FirstOrDefault(static entry => (string?)entry!["item"] == "structured_relations") is { } row)
            {
                StringAssert.Contains((string?)row["reason"], "not", operation);
                Assert.AreNotEqual(V3CorpusMount.RelationsStructuredRelationsTen, (string?)row["reason"], operation);
                Assert.AreNotEqual(V3CorpusMount.ReferencesStructuredRelationsTen, (string?)row["reason"], operation);
            }

            // Index 10's answer, less what it adds and with index 9's not-held words, is index 9's, but for the mounts' digests
            // (the shared fixture mints its corpus's identifiers anew for each mount, and the two indexes are two files).
            tenAnswer.Remove("publisher_relations");
            tenAnswer.Remove("amended_after_consolidation");
            tenAnswer["not_held"] = nineAnswer["not_held"]!.DeepClone();
            var tenText = tenAnswer.ToJsonString()
                .Replace((string)tenAnswer["corpus_sha256"]!, (string)nineAnswer["corpus_sha256"]!, StringComparison.Ordinal)
                .Replace((string)tenAnswer["index_sha256"]!, (string)nineAnswer["index_sha256"]!, StringComparison.Ordinal);
            Assert.AreEqual(nineAnswer.ToJsonString(), tenText, operation);
        }
    }
}
