using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.LuxembourgIndexCatalogueStatesTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The reader's interval rules over index 9 (data design §3.2, §3.4; slice S7a), driven through the real handler on a verified
/// mount of the multi-body fixture: the index the builder's projection and writer make from the fixture of
/// <see cref="LuxembourgIndexCatalogueStatesTests.Fixture"/>, with five works more (two closed ends, a same-date pair that differs,
/// a provisional state, a document whose articles skip a place), bound to the shared acquisition's corpus (which holds none of
/// its bodies, so its log names none). Every value is synthetic. The dates are the design's list: before the history, the start,
/// a day inside and the last day; a hole; the inclusive-end day; an invalid interval; a closed end with the base act in force and
/// no longer in force; an overlap; the same-date pairs; textless states; after an act's withheld consolidation; a translation.
/// </summary>
[TestClass]
public sealed class V3CorpusIntervalMountTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The fixture of §3.4 and the works this slice adds.</summary>
    internal static Harness IntervalFixture()
    {
        var h = Fixture();
        const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";

        // A closed end with the base act no longer in force, and one with it still in force (no end-of-force fact).
        h.State(W("loi/2005/01/01/n1/jo"), entryIntoForce: "2005-01-05", memberOf: W("loi/2005/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2005/01/01/n1/jo"), ["art_1"], inFileDate: "2005-01-05");
        h.State(W("loi/2005/01/01/n1/consolide/20100101"), "2010-01-01", "2015-01-01", memberOf: W("loi/2005/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2005/01/01/n1/consolide/20100101"), ["art_1", "art_2"], identityClass: "same_work_act");
        h.Fact(W("loi/2005/01/01/n1/jo"), "dateNoLongerInForce", "2015-01-01");
        h.State(W("loi/2006/02/02/n1/jo"), entryIntoForce: "2006-02-02", memberOf: W("loi/2006/02/02/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2006/02/02/n1/jo"), ["art_1"], inFileDate: "2006-02-02");
        h.State(W("loi/2006/02/02/n1/consolide/20120101"), "2012-01-01", "2016-01-01", memberOf: W("loi/2006/02/02/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2006/02/02/n1/consolide/20120101"), ["art_1"], identityClass: "same_work_act", textPrefix: "texte consolidé de ");

        // An act and its consolidation on one date whose wordings differ.
        h.State(W("loi/2021/05/05/n1/jo"), entryIntoForce: "2021-05-09", memberOf: W("loi/2021/05/05/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2021/05/05/n1/jo"), ["art_1"], inFileDate: "2021-05-05", textPrefix: "texte publié de ");
        h.State(W("loi/2021/05/05/n1/consolide/20210505"), "2021-05-05", memberOf: W("loi/2021/05/05/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2021/05/05/n1/consolide/20210505"), ["art_1"], identityClass: "same_work_act", textPrefix: "texte consolidé de ");

        // A held state dated after the build's day: provisional.
        h.State(W("rgd/2026/12/01/n1/jo"), entryIntoForce: "2026-12-01", expressions: ("fr", Fra))
            .Held(W("rgd/2026/12/01/n1/jo"), ["art_1"], inFileDate: "2026-12-01");

        // A document whose third article is not admitted: its place is skipped, and the publisher's order is not the ids'.
        h.State(W("loi/2010/03/03/n1/jo"), entryIntoForce: "2010-03-03", expressions: ("fr", Fra))
            .Held(W("loi/2010/03/03/n1/jo"), ["art_2", "art_10", "art_1bis"], inFileDate: "2010-03-03", positions: [1, 2, 4]);
        return h;
    }

    internal sealed class IntervalMount : IAsyncDisposable
    {
        private readonly MountedFixture _shared;

        private IntervalMount(MountedFixture shared, V3CorpusMount mount)
        {
            _shared = shared;
            Mount = mount;
        }

        public V3CorpusMount Mount { get; }

        public string Directory => _shared.Directory;

        /// <summary>The fixture's index bound to the shared acquisition's corpus, beside it, opened as the server opens a mount.</summary>
        public static async Task<IntervalMount> CreateAsync(Harness? harness = null)
        {
            var shared = await MountedFixture.CreateAsync();
            var corpusRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(shared.CorpusSha256), shared.CorpusSha256);
            var built = (harness ?? IntervalFixture()).BuildFor(corpusRef);
            await File.WriteAllBytesAsync(Path.Combine(shared.Directory, V3CorpusMount.IndexFileName), built.IndexBytes.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(shared.Directory, V3CorpusMount.CapabilityManifestFileName), built.CapabilityManifestBytes.ToArray());
            var mount = V3CorpusMount.Served(await V3CorpusMount.OpenAsync(shared.Directory, CancellationToken.None), shared.Directory);
            Assert.IsNotNull(mount);
            return new IntervalMount(shared, mount);
        }

        public async ValueTask DisposeAsync()
        {
            Mount.Dispose();
            await _shared.DisposeAsync();
        }
    }

    internal static async Task<V3Envelope> AskAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext { TraceIdentifier = "interval-" + operation };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => At, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return V3EnvelopeJson.ParseAndVerify(((MemoryStream)context.Response.Body).ToArray(), V3OperationRegistry.Reviewed);
    }

    private static Task<V3Envelope> AsOfAsync(V3CorpusMount mount, string work, string date, string? language = null) =>
        AskAsync(mount, "as_of", language is null
            ? new { identifier = "/lu-legilux/" + work, date }
            : (object)new { identifier = "/lu-legilux/" + work, date, language });

    private static JsonElement Served(V3Envelope envelope)
    {
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.HelpfulPayload.GetRawText());
        return envelope.Result!.Value.GetProperty("states").EnumerateArray().Single();
    }

    private static string Resource(JsonElement state) => state.GetProperty("publisher_legal_resource_iri").GetString()![Eli.Length..];

    private static JsonElement Refused(V3Envelope envelope, string code)
    {
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, envelope.Result?.Value.GetRawText());
        Assert.AreEqual(code, envelope.Refusal!.Code, envelope.Refusal.HelpfulPayload.GetRawText());
        return envelope.Refusal.HelpfulPayload;
    }

    private static string[] Named(JsonElement payload) =>
        payload.GetProperty("states").EnumerateArray().Select(static state => state.GetProperty("publisher_legal_resource_iri").GetString()![Eli.Length..]).ToArray();

    [TestMethod]
    public async Task ACodeStateIsServedFromItsStartToTheDayBeforeItsEndAndNotBeforeItsHistory()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        var before = Refused(await AsOfAsync(mount, "code-civil", "2016-08-31"), "no_version_for_date");
        Assert.AreEqual("before_history", before.GetProperty("reason").GetString());
        Assert.AreEqual("2016-09-01", before.GetProperty("history_begins").GetString());
        Assert.AreEqual(JsonValueKind.Null, before.GetProperty("nearest_earlier").ValueKind);
        Assert.AreEqual("2016-09-01", before.GetProperty("nearest_later").GetString());
        Assert.IsFalse(before.GetProperty("asserts_absence_of_law").GetBoolean());

        foreach (var date in new[] { "2016-09-01", "2017-01-15", "2017-03-31" })
        {
            var state = Served(await AsOfAsync(mount, "code-civil", date));
            Assert.AreEqual("code/civil/20160901", Resource(state), date);
            Assert.AreEqual("code", state.GetProperty("state_kind").GetString());
            Assert.AreEqual("applicability", state.GetProperty("date_role").GetString());
            Assert.AreEqual("2017-04-01", state.GetProperty("applicable_to").GetString());
            Assert.AreEqual("publisher", state.GetProperty("applicable_to_source").GetString());
            Assert.AreEqual("half_open", state.GetProperty("boundary").GetString());
            Assert.AreEqual("2016-08-30", state.GetProperty("publication_date").GetString());
            Assert.AreEqual("held", state.GetProperty("text_status").GetString());
            Assert.IsFalse(state.GetProperty("provisional").GetBoolean());
            Assert.AreEqual("2017-04-01", state.GetProperty("next_applicability_date").GetString(), "the next held state's date");
        }

        Assert.AreEqual("code/civil/20170401", Resource(Served(await AsOfAsync(mount, "code-civil", "2017-04-01"))), "the end is half-open");
    }

    [TestMethod]
    public async Task AStateWithoutTextCoveringTheDateIsRefusedForItsTextAndNoOtherStateIsServedInItsPlace()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // A misfiled state, a PDF and a catalogued state with no expression, each in the code's chain.
        var misfiled = Refused(await AsOfAsync(mount, "code-civil", "2018-05-01"), "text_not_available");
        Assert.AreEqual(W("code/civil/20180401"), misfiled.GetProperty("official_identity").GetString());
        Assert.AreEqual(W("code/civil/20180401"), misfiled.GetProperty("official_source").GetString());
        Assert.AreEqual("none", misfiled.GetProperty("retained_transport_evidence").GetString());
        Assert.AreEqual("not_available_misfiled", misfiled.GetProperty("state").GetProperty("text_status").GetString());
        StringAssert.StartsWith(misfiled.GetProperty("state").GetProperty("text_status_reason").GetString(), "content consistency: shared_file:");
        Assert.IsFalse(misfiled.GetProperty("asserts_absence_of_law").GetBoolean());
        Assert.AreEqual("not_available_pdf",
            Refused(await AsOfAsync(mount, "code-civil", "2020-06-01"), "text_not_available").GetProperty("state").GetProperty("text_status").GetString());
        var notAcquired = Refused(await AsOfAsync(mount, "code-civil", "2027-02-01"), "text_not_available").GetProperty("state");
        Assert.AreEqual("not_acquired", notAcquired.GetProperty("text_status").GetString());
        Assert.AreEqual(JsonValueKind.Null, notAcquired.GetProperty("permalink").ValueKind, "a state naming no expression has no permalink");
        Assert.IsTrue(notAcquired.GetProperty("provisional").GetBoolean());

        // After an act's withheld consolidation: the consolidation covers the date, so the act as published is not served.
        var withheld = Refused(await AsOfAsync(mount, "loi-1798-11-03-n1", "2020-06-01"), "text_withheld");
        Assert.AreEqual(W("loi/1798/11/03/n1/consolide/20200101"), withheld.GetProperty("official_identity").GetString());
        Assert.AreEqual(W("loi/1798/11/03/n1/consolide/20200101"), withheld.GetProperty("official_link").GetString());
        Assert.AreEqual(withheld.GetProperty("state").GetProperty("state_sha256").GetString(), withheld.GetProperty("content_sha256").GetString());
        Assert.AreEqual("withheld_rights", withheld.GetProperty("state").GetProperty("text_status").GetString());

        // The textless state's own permalink is refused for its text, never unknown.
        var permalink = withheld.GetProperty("state").GetProperty("permalink").GetString()!;
        var pinned = await AskAsync(mount, "as_of", new { identifier = permalink, date = "2020-01-01" });
        Assert.AreEqual(withheld.GetProperty("state").GetRawText(), Refused(pinned, "text_withheld").GetProperty("state").GetRawText());
        Refused(await AskAsync(mount, "evidence_bundle", new { identifier = permalink, date = "2020-01-01" }), "text_withheld");
    }

    [TestMethod]
    public async Task AHoleAnUnclearEndAndAnInvalidIntervalAreRefusedWithTheirReasonsAndNeverBridged()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // The hole between the withheld consolidation's end (2021-06-01, the publisher's) and the next state.
        var hole = Refused(await AsOfAsync(mount, "loi-1798-11-03-n1", "2021-07-01"), "no_version_for_date");
        Assert.AreEqual("catalogue_hole", hole.GetProperty("reason").GetString());
        Assert.AreEqual("2021-06-01", hole.GetProperty("hole").GetProperty("from").GetString());
        Assert.AreEqual("2022-01-01", hole.GetProperty("hole").GetProperty("to").GetString());
        Assert.AreEqual("2020-01-01", hole.GetProperty("nearest_earlier").GetString(), "index 9 names the state before the date");
        Assert.AreEqual("2022-01-01", hole.GetProperty("nearest_later").GetString());
        Assert.AreEqual("1798-11-07", hole.GetProperty("history_begins").GetString());
        CollectionAssert.AreEqual(new[] { "loi/1798/11/03/n1/consolide/20200101" }, Named(hole));
        Assert.AreEqual("2026-10-12T18:17:05Z", hole.GetProperty("catalogue_read_no_later_than").GetString());

        // The end day of a suspected inclusive end: given to neither state, and never called a hole.
        Assert.AreEqual("loi/1996/11/07/n1/consolide/20200101", Resource(Served(await AsOfAsync(mount, "loi-1996-11-07-n1", "2022-12-30"))));
        var unclear = Refused(await AsOfAsync(mount, "loi-1996-11-07-n1", "2022-12-31"), "no_version_for_date");
        Assert.AreEqual("publisher_end_convention_unclear", unclear.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] { "loi/1996/11/07/n1/consolide/20200101", "loi/1996/11/07/n1/consolide/20230101" }, Named(unclear));
        Assert.AreEqual(JsonValueKind.Null, unclear.GetProperty("hole").ValueKind);
        Assert.AreEqual("loi/1996/11/07/n1/consolide/20230101", Resource(Served(await AsOfAsync(mount, "loi-1996-11-07-n1", "2023-01-01"))));

        // Where a state that ends on or before its start would apply: refused, naming it; its own permalink still opens it.
        var invalidFirst = Refused(await AsOfAsync(mount, "rgd-2007-06-07-n2", "2023-01-01"), "no_version_for_date");
        Assert.AreEqual("publisher_interval_invalid", invalidFirst.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] { "rgd/2007/06/07/n2/consolide/20221214" }, Named(invalidFirst));
        var invalidHeld = Refused(await AsOfAsync(mount, "rgd-2007-06-07-n2", "2023-08-01"), "no_version_for_date");
        Assert.AreEqual("publisher_interval_invalid", invalidHeld.GetProperty("reason").GetString());
        var invalidState = invalidHeld.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("invalid", invalidState.GetProperty("boundary").GetString());
        var opened = Served(await AskAsync(mount, "as_of", new { identifier = invalidState.GetProperty("permalink").GetString(), date = "2023-07-17" }));
        Assert.AreEqual(invalidState.GetProperty("state_sha256").GetString(), opened.GetProperty("state_sha256").GetString());
    }

    [TestMethod]
    public async Task AfterAClosedEndTheRefusalNamesTheLastStateAndTheBaseActsOwnForceFactsAndNeverSaysNotInForce()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        var closed = Refused(await AsOfAsync(mount, "loi-2005-01-01-n1", "2016-01-01"), "no_version_for_date");
        Assert.AreEqual("after_closed_end", closed.GetProperty("reason").GetString());
        Assert.AreEqual("2015-01-01", closed.GetProperty("closed_on").GetString());
        Assert.AreEqual("2010-01-01", closed.GetProperty("nearest_earlier").GetString());
        Assert.AreEqual(JsonValueKind.Null, closed.GetProperty("nearest_later").ValueKind);
        Assert.AreEqual(W("loi/2005/01/01/n1/jo"), closed.GetProperty("base_act").GetProperty("iri").GetString());
        Assert.AreEqual("2015-01-01", closed.GetProperty("base_act").GetProperty("no_longer_in_force").EnumerateArray().Single().GetProperty("value").GetString());

        var stillInForce = Refused(await AsOfAsync(mount, "loi-2006-02-02-n1", "2016-01-01"), "no_version_for_date");
        Assert.AreEqual("after_closed_end", stillInForce.GetProperty("reason").GetString());
        Assert.AreEqual(0, stillInForce.GetProperty("base_act").GetProperty("no_longer_in_force").GetArrayLength(),
            "the catalogue does not mark the base act no longer in force, and the refusal says only what it holds");
        Assert.IsFalse(stillInForce.GetProperty("asserts_absence_of_law").GetBoolean());

        // in_force_on with the work named is as_of's question, and so its refusal; across works the work has no row.
        var named = await AskAsync(mount, "in_force_on", new { identifier = "/lu-legilux/loi-2005-01-01-n1", date = "2016-01-01" });
        Assert.AreEqual(closed.GetRawText(), Refused(named, "no_version_for_date").GetRawText());
        var across = (await AskAsync(mount, "in_force_on", new { date = "2016-01-01" })).Result!.Value;
        Assert.IsFalse(across.GetProperty("states").EnumerateArray().Any(static row => row.GetProperty("work_key").GetString() == "loi-2005-01-01-n1"));
        Assert.AreEqual(V3CorpusMount.InForceOnRuleNine, across.GetProperty("derivation").GetProperty("rule").GetString());
    }

    [TestMethod]
    public async Task StatesHoldingTheSameDateAreServedOnceWhenTheirWordingIsOneAndOtherwiseRefusedNamingEach()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // An act and its same-date consolidation with one wording: the act as published, the consolidation named beside it.
        var same = Served(await AsOfAsync(mount, "loi-2018-09-14-a883", "2019-01-01"));
        Assert.AreEqual("loi/2018/09/14/a883/jo", Resource(same));
        Assert.AreEqual("applicability_in_file", same.GetProperty("date_role").GetString());
        Assert.AreEqual("2018-09-18", same.GetProperty("entry_into_force").GetString(), "the entry into force is kept beside the file's date");
        var sibling = same.GetProperty("same_text_states").EnumerateArray().Single();
        Assert.AreEqual(W("loi/2018/09/14/a883/consolide/20180914"), sibling.GetProperty("publisher_legal_resource_iri").GetString());
        Assert.AreEqual("identical_text", sibling.GetProperty("basis").GetString());

        // An overlap with one wording: the state that begins last.
        var overlap = Served(await AsOfAsync(mount, "rgd-2007-06-07-n2", "2024-07-01"));
        Assert.AreEqual("rgd/2007/06/07/n2/consolide/20240601", Resource(overlap));
        Assert.AreEqual(W("rgd/2007/06/07/n2/consolide/20240101"),
            overlap.GetProperty("same_text_states").EnumerateArray().Single().GetProperty("publisher_legal_resource_iri").GetString());

        // An act and its same-date consolidation whose wordings differ: ambiguous, each candidate named with its interval.
        var differ = Refused(await AsOfAsync(mount, "loi-2021-05-05-n1", "2021-06-01"), "ambiguous_version");
        Assert.AreEqual(2, differ.GetProperty("candidates").GetArrayLength());
        var candidates = differ.GetProperty("candidate_states").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(new[] { "as_published", "consolidation" },
            candidates.Select(static candidate => candidate.GetProperty("state_kind").GetString()).ToArray());
        Assert.IsTrue(candidates.All(static candidate => candidate.GetProperty("applicability_date").GetString() == "2021-05-05" &&
                                                        candidate.GetProperty("text_status").GetString() == "held"));
        Assert.AreEqual(V3CorpusMount.AmbiguousCandidatesRule, differ.GetProperty("candidate_states_rule").GetString());
    }

    [TestMethod]
    public async Task ATranslationIsServedInItsOwnLanguageWithTheFrenchStateItTakesItsIntervalFromNamed()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        var answer = (await AsOfAsync(mount, "loi-2017-07-20-a674", "2018-01-01")).Result!.Value;
        var english = answer.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("eng", english.GetProperty("language").GetString());
        Assert.AreEqual(W("loi/2017/07/20/a674/jo/fr"), english.GetProperty("translation_of").GetString());
        Assert.AreEqual("entry_into_force", english.GetProperty("date_role").GetString());
        var french = answer.GetProperty("states_without_text").EnumerateArray().Single();
        Assert.AreEqual("fra", french.GetProperty("language").GetString());
        Assert.AreEqual("not_acquired", french.GetProperty("text_status").GetString());
        Assert.AreEqual(V3CorpusMount.CatalogueRule, answer.GetProperty("catalogue_rule").GetString());

        // French is not held for this work, so a French question is not this work's to answer.
        Refused(await AsOfAsync(mount, "loi-2017-07-20-a674", "2018-01-01", "fra"), "language_not_available");
    }

    [TestMethod]
    public async Task ADateRoleIsServedWithEveryStateAndAProvisionalStateSaysSo()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        var act = Served(await AsOfAsync(mount, "loi-1798-11-03-n1", "1900-01-01"));
        Assert.AreEqual("entry_into_force", act.GetProperty("date_role").GetString(), "an act its file does not date is dated by its entry into force");
        Assert.AreEqual("1798-11-07", act.GetProperty("entry_into_force").GetString());
        Assert.AreEqual("2020-01-01", act.GetProperty("applicable_to").GetString());
        Assert.AreEqual("derived", act.GetProperty("applicable_to_source").GetString());
        Assert.AreEqual(JsonValueKind.Null, act.GetProperty("next_applicability_date").ValueKind, "no later state is held");

        var file = Served(await AsOfAsync(mount, "rgd-2019-03-05-a129", "2019-02-01"));
        Assert.AreEqual("applicability_in_file", file.GetProperty("date_role").GetString());
        Assert.AreEqual("2019-01-01", file.GetProperty("applicability_date").GetString());
        Assert.AreEqual("2019-03-12", file.GetProperty("entry_into_force").GetString(), "the file applies it before its entry into force");

        var provisional = Served(await AsOfAsync(mount, "rgd-2026-12-01-n1", "2026-12-05"));
        Assert.IsTrue(provisional.GetProperty("provisional").GetBoolean(), "dated after the day the index was built");

        // The dossier and the timeline carry the same facts for the same state.
        var dossier = (await AskAsync(mount, "dossier", new { identifier = "/lu-legilux/loi-1798-11-03-n1" })).Result!.Value
            .GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("entry_into_force", dossier.GetProperty("date_role").GetString());
        var timeline = (await AskAsync(mount, "timeline", new { identifier = "/lu-legilux/loi-1798-11-03-n1" })).Result!.Value
            .GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("entry_into_force", timeline.GetProperty("date_role").GetString());
    }

    [TestMethod]
    public async Task TheBundleServesTheArticlesInTheirDocumentsOrderAndSaysTheArticleItLacks()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var bundle = (await AskAsync(fixture.Mount, "evidence_bundle", new { identifier = "/lu-legilux/loi-2010-03-03-n1", date = "2011-01-01" })).Result!.Value;
        var state = bundle.GetProperty("states").EnumerateArray().Single();
        CollectionAssert.AreEqual(new[] { "art_2", "art_10", "art_1bis" },
            state.GetProperty("articles").EnumerateArray().Select(static article => article.GetProperty("publisher_id").GetString()).ToArray(),
            "the publisher's order, not the ids' (art_1bis would come first by its number)");
        CollectionAssert.AreEqual(new[] { 1, 2, 4 },
            state.GetProperty("articles").EnumerateArray().Select(static article => article.GetProperty("document_position").GetInt32()).ToArray());
        var gap = state.GetProperty("missing_articles").EnumerateArray().Single();
        Assert.AreEqual(3, gap.GetProperty("from_position").GetInt32());
        Assert.AreEqual(3, gap.GetProperty("to_position").GetInt32());
        Assert.AreEqual(1, gap.GetProperty("count").GetInt32());
        Assert.AreEqual("art_10", gap.GetProperty("after_publisher_id").GetString());
        Assert.AreEqual("art_1bis", gap.GetProperty("before_publisher_id").GetString());
        Assert.AreEqual(V3CorpusMount.MissingArticlesRule, state.GetProperty("missing_articles_rule").GetString());
        Assert.AreEqual("applicability_in_file", state.GetProperty("date_role").GetString());
    }

    [TestMethod]
    public async Task DiffRefusesForTheBoundWhoseStateHoldsNoTextAndAsObservedSelectsAsAsOfDoesAtTheMountedSnapshot()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        var diff = await AskAsync(mount, "diff", new { identifier = "/lu-legilux/loi-1798-11-03-n1", date_from = "1900-01-01", date_to = "2020-06-01" });
        var payload = Refused(diff, "text_withheld");
        Assert.AreEqual("to", payload.GetProperty("bound").GetString());
        var hole = Refused(await AskAsync(mount, "diff", new { identifier = "/lu-legilux/loi-1798-11-03-n1", date_from = "2021-07-01", date_to = "1900-01-01" }), "no_version_for_date");
        Assert.AreEqual("from", hole.GetProperty("bound").GetString());
        Assert.AreEqual("catalogue_hole", hole.GetProperty("reason").GetString());

        // The mounted snapshot is the genesis build: as_observed refuses and serves on the dates as_of does.
        var snapshot = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName))));
        foreach (var (work, date) in new[] { ("loi-1798-11-03-n1", "2021-07-01"), ("loi-1798-11-03-n1", "2020-06-01"), ("loi-1996-11-07-n1", "2022-12-31"), ("loi-2018-09-14-a883", "2019-01-01") })
        {
            var asOf = await AsOfAsync(mount, work, date);
            var observed = await AskAsync(mount, "as_observed", new { identifier = "/lu-legilux/" + work, date, snapshot });
            Assert.AreEqual(asOf.Verdict, observed.Verdict, $"{work} {date}");
            if (asOf.Verdict == V3Verdicts.Refuse)
            {
                Assert.AreEqual(asOf.Refusal!.HelpfulPayload.GetRawText(), observed.Refusal!.HelpfulPayload.GetRawText(), $"{work} {date}");
            }
            else
            {
                Assert.AreEqual(Served(asOf).GetProperty("state_sha256").GetString(), Served(observed).GetProperty("state_sha256").GetString());
            }
        }
    }

    [TestMethod]
    public async Task ASearchCountsTheVersionsItsRightsWithholdAndSelectsADatesVersionByTheIntervalRules()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // Undated: every version searched, and the two French versions whose rights withhold their text are counted out.
        var undated = (await AskAsync(mount, "search", new { query = "texte", language = "fra", mode = "strict" })).Result!.Value;
        Assert.AreEqual(2, undated.GetProperty("searched").GetProperty("states_withheld_by_rights").GetInt32());
        Assert.AreEqual(V3CorpusMount.SearchedRuleNine, undated.GetProperty("searched").GetProperty("rule").GetString());

        // Dated: the withheld consolidation of the 1798 act covers the date, so that work contributes no hit and is counted
        // out; a closed work (2005) and a hole contribute nothing; the act and its same-date consolidation one hit.
        var dated = (await AskAsync(mount, "search", new { query = "texte", language = "fra", mode = "strict", date = "2020-06-01" })).Result!.Value;
        Assert.AreEqual(1, dated.GetProperty("searched").GetProperty("states_withheld_by_rights").GetInt32());
        var works = dated.GetProperty("hits").EnumerateArray().Select(static hit => hit.GetProperty("work_key").GetString()).Distinct().ToArray();
        CollectionAssert.DoesNotContain(works, "loi-1798-11-03-n1");
        CollectionAssert.DoesNotContain(works, "loi-2005-01-01-n1");
        CollectionAssert.Contains(works, "loi-2018-09-14-a883");
        Assert.IsTrue(dated.GetProperty("hits").EnumerateArray().Where(static hit => hit.GetProperty("work_key").GetString() == "loi-2018-09-14-a883")
            .All(static hit => hit.GetProperty("state_sha256").GetString() == hit.GetProperty("state_sha256").GetString() &&
                               hit.GetProperty("date_role").GetString() == "applicability_in_file"), "the act as published is the version served");
        Assert.AreEqual(JsonValueKind.Undefined, dated.TryGetProperty("fold", out var fold) ? fold.ValueKind : JsonValueKind.Undefined,
            "with a date nothing is folded");

        // With the work named and the date, it is as_of's question and refusal.
        var named = await AskAsync(mount, "search", new { query = "texte", language = "fra", identifier = "/lu-legilux/loi-1798-11-03-n1", date = "2020-06-01" });
        Assert.AreEqual("text_withheld", named.Refusal!.Code);
    }

    [TestMethod]
    public async Task TheReadersChainIsTheIndexsOwn()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        using var reader = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName),
            await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName)),
            CancellationToken.None);
        foreach (var work in new[] { "code-civil", "loi-1798-11-03-n1", "loi-1996-11-07-n1", "loi-2017-07-20-a674", "loi-2018-09-14-a883", "rgd-2007-06-07-n2", "loi-2005-01-01-n1" })
        {
            var rows = reader.ResolveCatalogueStates(work);
            CollectionAssert.AreEqual(rows.ToArray(), V3CorpusMount.Rechain(rows), $"{work}: the API's chain recomputes the index's ends and boundaries");
        }
    }
}
