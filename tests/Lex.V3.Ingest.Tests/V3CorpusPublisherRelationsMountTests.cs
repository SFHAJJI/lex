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
/// the relation tabs' data and R72's notice, which fires when an amending act was published after every consolidated version whose
/// text the index holds, of the act and of every act the publisher records as integrating its changes (or there is none), and the
/// act is not recorded as integrated in a held version; and not otherwise; an index 9 mount of the same rows answers as before,
/// claiming nothing.
/// Every value is synthetic, driven through the real handler.
/// </summary>
[TestClass]
public sealed class V3CorpusPublisherRelationsMountTests
{
    private const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";

    private static readonly string CodeActAsPublished = W("loi/2006/07/31/n2/jo");

    /// <summary>
    /// The relations fixture (its act amended by an act whose impact record names two integrating versions, its one consolidation's
    /// text not held) and the works for R72: one never consolidated and amended later; one with a held consolidation published after
    /// its amending act; one whose held consolidation names no publication date; one amended by an act of no date; one with no
    /// relation; one whose held consolidation precedes its amending act and whose later consolidation's text is not held; a grand-ducal
    /// regulation amended later; and an act held only as published whose changes a code integrates (the Code du travail's act of
    /// 31 July 2006, as run 15 records it).
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
        h.State(W("loi/2012/01/01/n1/consolide/20190101"), "2019-01-01", publication: "2019-01-05", memberOf: W("loi/2012/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2012/01/01/n1/consolide/20190101"), ["art_1"], inFileDate: "2019-01-01");
        h.Fact(W("loi/2018/01/01/n7/jo"), "publicationDate", "2018-01-03").Relation(W("loi/2018/01/01/n7/jo"), "modifies", W("loi/2012/01/01/n1/jo"));
        Act("loi/2013/01/01/n1", "2013-01-05");
        h.State(W("loi/2013/01/01/n1/consolide/20190101"), "2019-01-01", memberOf: W("loi/2013/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2013/01/01/n1/consolide/20190101"), ["art_1"], inFileDate: "2019-01-01");
        h.Fact(W("loi/2020/01/01/n7/jo"), "publicationDate", "2020-01-03").Relation(W("loi/2020/01/01/n7/jo"), "modifies", W("loi/2013/01/01/n1/jo"));
        Act("loi/2014/01/01/n1", "2014-01-05");
        h.Relation(W("loi/2021/01/01/n7/jo"), "modifies", W("loi/2014/01/01/n1/jo"));
        Act("loi/2015/01/01/n1", "2015-01-05");

        // Held 2018, amended 2019, consolidated again in 2020 in a version whose text is not held: R72 compares held versions only.
        Act("loi/2017/01/01/n1", "2017-01-05");
        h.State(W("loi/2017/01/01/n1/consolide/20180101"), "2018-01-01", "2020-01-01", publication: "2018-01-05", memberOf: W("loi/2017/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2017/01/01/n1/consolide/20180101"), ["art_1"], inFileDate: "2018-01-01");
        h.State(W("loi/2017/01/01/n1/consolide/20200101"), "2020-01-01", publication: "2020-01-05", memberOf: W("loi/2017/01/01/n1"), expressions: ("fr", Fra));
        h.Fact(W("loi/2019/06/01/n7/jo"), "publicationDate", "2019-06-03").Relation(W("loi/2019/06/01/n7/jo"), "modifies", W("loi/2017/01/01/n1/jo"));

        // A grand-ducal regulation, never consolidated, amended later: the notice says "this act" of it, as of every class.
        Act("rgd/2016/01/01/n1", "2016-01-05");
        h.Fact(W("rgd/2016/05/01/n7/jo"), "publicationDate", "2016-05-03").Relation(W("rgd/2016/05/01/n7/jo"), "modifies", W("rgd/2016/01/01/n1/jo"));

        // The code's act, held only as published; the code's states are another work's, two held and the latest not.
        Act("loi/2006/07/31/n2", "2006-08-28");
        h.Fact(W("code/travail"), "rdf:type", "http://data.legilux.public.lu/resource/ontology/jolux#ComplexWork");
        h.State(W("code/travail/20250401"), "2025-04-01", "2026-07-01", publication: "2025-04-05", expressions: ("fr", Fra))
            .Held(W("code/travail/20250401"), ["art_1"]);
        h.State(W("code/travail/20260701"), "2026-07-01", "2026-10-01", publication: "2026-07-30", expressions: ("fr", Fra))
            .Held(W("code/travail/20260701"), ["art_1"]);
        h.State(W("code/travail/20261001"), "2026-10-01", publication: "2026-10-02", expressions: ("fr", Fra));
        void Amends(string act, string published, params string[] consolidatedBy)
        {
            h.Fact(W(act + "/jo"), "publicationDate", published).Relation(W(act + "/jo"), "modifies", CodeActAsPublished);
            if (consolidatedBy.Length == 0)
            {
                return;
            }

            var impact = W(act + "/legal-analysis/LegalResourceImpact/1");
            h.Relation(impact, "impactFromLegalResource", W(act + "/jo"))
                .Relation(impact, "impactToLegalResource", CodeActAsPublished)
                .Relation(impact, "legalResourceImpactHasType", "http://data.legilux.public.lu/resource/authority/impact-type/1");
            foreach (var version in consolidatedBy)
            {
                h.Relation(impact, "impactConsolidatedBy", W(version));
            }
        }

        Amends("loi/2026/01/01/n1", "2026-01-03", "code/travail/20250401");
        Amends("loi/2026/08/01/n1", "2026-08-03", "code/travail/20260701");
        Amends("loi/2026/09/01/n1", "2026-09-03");
        Amends("loi/2026/09/20/n1", "2026-09-22", "code/travail/20261001");
        Amends("loi/2026/09/25/n1", "2026-09-27", "code/travail/20260701", "code/travail/20261001");
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
    public async Task TheDossierCarriesTheRelationTabsAndR72FiresOnlyWhenAnAmendingActIsLaterThanEveryHeldConsolidatedVersion()
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

        const string Derived = " This is derived from the publisher's records (derived from the publisher's forward relation).";

        // Amended by an act published on 2016-06-03, consolidated on 2015-01-10 in a version whose text is not held; the amending
        // act's impact record names two versions as integrating it, so whether one this index holds does is not decided.
        var undecided = await NoticeOf("loi-2010-01-01-n1");
        Assert.AreEqual("undetermined", undecided.GetProperty("status").GetString());
        Assert.AreEqual(0, undecided.GetProperty("held_versions").GetInt32(), "a consolidation whose text is not held is not compared");
        Assert.AreEqual(1, undecided.GetProperty("amending_acts_integration_undecided").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, undecided.GetProperty("latest_consolidation").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, undecided.GetProperty("notice").ValueKind);

        // Never consolidated, amended later: fires, naming no consolidated version.
        var never = await NoticeOf("loi-2011-01-01-n1");
        Assert.AreEqual("notice", never.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, never.GetProperty("latest_consolidation").ValueKind);
        Assert.AreEqual(
            "An act published on 2012-01-03 (http://data.legilux.public.lu/eli/etat/leg/loi/2012/01/01/n7/jo) amends this act; no consolidated " +
            "version Lex holds was published after it. Lex holds no consolidated version of this act, or of another act the publisher records " +
            "as integrating changes made to it." + Derived,
            never.GetProperty("notice").GetString());

        // A held consolidation published after its amending act: no notice.
        var consolidated = await NoticeOf("loi-2012-01-01-n1");
        Assert.AreEqual("no_later_amending_act", consolidated.GetProperty("status").GetString());
        Assert.AreEqual(1, consolidated.GetProperty("held_versions").GetInt32());
        Assert.AreEqual(0, consolidated.GetProperty("acts").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, consolidated.GetProperty("notice").ValueKind);

        // A held consolidation without a publication date: the comparison cannot be made.
        var undated = await NoticeOf("loi-2013-01-01-n1");
        Assert.AreEqual("undetermined", undated.GetProperty("status").GetString());
        Assert.AreEqual(1, undated.GetProperty("held_versions_without_publication_date").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, undated.GetProperty("notice").ValueKind);

        // An amending act without a publication date, no consolidated version: undetermined.
        var undatedAct = await NoticeOf("loi-2014-01-01-n1");
        Assert.AreEqual("undetermined", undatedAct.GetProperty("status").GetString());
        Assert.AreEqual(1, undatedAct.GetProperty("amending_acts_without_publication_date").GetInt32());

        // No relation recorded.
        Assert.AreEqual("no_amending_act_recorded", (await NoticeOf("loi-2015-01-01-n1")).GetProperty("status").GetString());

        // A grand-ducal regulation: the sentence says "this act", never "this law".
        var regulation = await NoticeOf("rgd-2016-01-01-n1");
        Assert.AreEqual("notice", regulation.GetProperty("status").GetString());
        StringAssert.StartsWith(regulation.GetProperty("notice").GetString(),
            "An act published on 2016-05-03 (http://data.legilux.public.lu/eli/etat/leg/rgd/2016/05/01/n7/jo) amends this act; no consolidated version Lex holds was published after it.");
        Assert.IsFalse(regulation.GetProperty("notice").GetString()!.Contains("this law", StringComparison.Ordinal));

        // Held 2018-01-05, amended 2019-06-03, consolidated again on 2020-01-05 in a version whose text is not held: the notice
        // fires, since no version Lex holds was published after the amending act, and names the latest it holds.
        var heldOnly = await NoticeOf("loi-2017-01-01-n1");
        Assert.AreEqual("notice", heldOnly.GetProperty("status").GetString());
        Assert.AreEqual(("2018-01-01", "2018-01-05"),
            (heldOnly.GetProperty("latest_consolidation").GetProperty("applicability_date").GetString(),
             heldOnly.GetProperty("latest_consolidation_publication_date").GetString()));
        Assert.IsFalse(heldOnly.GetProperty("latest_consolidation_integrating").GetBoolean());
        Assert.AreEqual(1, heldOnly.GetProperty("held_versions").GetInt32());
        Assert.AreEqual(
            "An act published on 2019-06-03 (http://data.legilux.public.lu/eli/etat/leg/loi/2019/06/01/n7/jo) amends this act; no consolidated " +
            "version Lex holds was published after it. The latest consolidated version Lex holds, /lu-legilux/loi-2017-01-01-n1/2018-01-01, " +
            "was published on 2018-01-05." + Derived,
            heldOnly.GetProperty("notice").GetString());

        // The code's act, held only as published: the code's held states are compared, as the impact records name them as
        // integrating its changes. Of its five amending acts, the one of 2026-01-03 precedes the latest held code state (2026-07-30);
        // the one of 2026-08-03 is integrated in a held state, so it is never later; the one of 2026-09-27 names two versions, so it
        // is not named; the two of 2026-09-03 and 2026-09-22 are named, the second integrated in a state whose text is not held.
        var code = await NoticeOf("loi-2006-07-31-n2");
        Assert.AreEqual("notice", code.GetProperty("status").GetString());
        CollectionAssert.AreEqual(
            new[] { "2026-09-03 loi/2026/09/01/n1/jo", "2026-09-22 loi/2026/09/20/n1/jo" },
            code.GetProperty("acts").EnumerateArray()
                .Select(static act => $"{act.GetProperty("publication_date").GetString()} {Short(act.GetProperty("acting_iri").GetString()!)}").ToArray());
        var latestCode = code.GetProperty("latest_consolidation");
        Assert.AreEqual(("/lu-legilux/code-travail/2026-07-01", "code", "held"),
            (latestCode.GetProperty("stable_coordinate").GetString(), latestCode.GetProperty("state_kind").GetString(), latestCode.GetProperty("text_status").GetString()));
        Assert.AreEqual("2026-07-30", code.GetProperty("latest_consolidation_publication_date").GetString());
        Assert.IsTrue(code.GetProperty("latest_consolidation_integrating").GetBoolean());
        CollectionAssert.AreEqual(new[] { "code-travail" }, code.GetProperty("integrating_work_keys").EnumerateArray().Select(static key => key.GetString()).ToArray());
        Assert.AreEqual((2, 0, 5, 0, 2, 1),
            (code.GetProperty("held_versions").GetInt32(), code.GetProperty("held_versions_without_publication_date").GetInt32(),
             code.GetProperty("amending_acts").GetInt32(), code.GetProperty("amending_acts_without_publication_date").GetInt32(),
             code.GetProperty("amending_acts_integrated").GetInt32(), code.GetProperty("amending_acts_integration_undecided").GetInt32()));
        Assert.AreEqual(
            "2 acts published from 2026-09-03 to 2026-09-22 amend this act; no consolidated version Lex holds was published after them. " +
            "The latest consolidated version Lex holds, /lu-legilux/code-travail/2026-07-01, was published on 2026-07-30; it is a version of " +
            "another act, one the publisher records as integrating changes made to this act." + Derived,
            code.GetProperty("notice").GetString());

        // The version the impact record of 2026-09-22 names as integrating it is the code's, and its text is not held: its permalink,
        // as the relation tab names it, is refused by verify and resolve for its text (#1008), naming that state, never
        // identifier_unknown.
        var dossier = await AnswerAsync(fixture.Mount, "dossier", new { identifier = "/lu-legilux/loi-2006-07-31-n2" });
        var textless = dossier.GetProperty("publisher_relations").GetProperty("edges").EnumerateArray()
            .Single(static edge => edge.GetProperty("view").GetString() == "impact_received" &&
                                   edge.GetProperty("other").GetProperty("iri").GetString() == W("loi/2026/09/20/n1/jo"))
            .GetProperty("impact").GetProperty("consolidated_by_state");
        Assert.AreEqual(("/lu-legilux/code-travail/2026-10-01", "not_acquired"),
            (textless.GetProperty("stable_coordinate").GetString(), textless.GetProperty("text_status").GetString()));
        foreach (var operation in new[] { "verify", "resolve" })
        {
            var refused = await V3CorpusIntervalMountTests.AskAsync(fixture.Mount, operation, new { identifier = textless.GetProperty("permalink").GetString() });
            Assert.AreEqual(V3Verdicts.Refuse, refused.Verdict, operation);
            Assert.AreEqual("text_not_available", refused.Refusal!.Code, operation);
            StringAssert.Contains(refused.Refusal.HelpfulPayload.GetRawText(), textless.GetProperty("state_sha256").GetString(), operation);
        }
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
