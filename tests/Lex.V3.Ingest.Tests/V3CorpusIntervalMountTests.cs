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

        // Two closed codes (the review of #987, F4): one whose state names its base act by isMemberOf, one that names none.
        h.Fact(W("code/route"), "rdf:type", "http://data.legilux.public.lu/resource/ontology/jolux#ComplexWork");
        h.State(W("code/route/20100101"), "2010-01-01", "2015-01-01", memberOf: W("loi/1955/02/14/n1"), expressions: ("fr", Fra))
            .Held(W("code/route/20100101"), ["art_1"]);
        h.Fact(W("code/eau"), "rdf:type", "http://data.legilux.public.lu/resource/ontology/jolux#ComplexWork");
        h.State(W("code/eau/20110101"), "2011-01-01", "2014-01-01", expressions: ("fr", Fra))
            .Held(W("code/eau/20110101"), ["art_1"]);
        return h;
    }

    /// <summary>
    /// <see cref="IntervalFixture"/> and the works the review of #987 reproduced its findings on: two states of one date neither
    /// of which holds text between two held ones; an act whose text is not held before a held consolidation; an act held beside a
    /// consolidation of its date that names no expression; a consolidation with another nested in its interval, then a hole, and
    /// the same without a later state (a closed end); an act held with two held consolidations after it.
    /// </summary>
    internal static Harness RepairFixture()
    {
        var h = IntervalFixture();
        const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";
        h.State(W("loi/2001/01/01/n1/jo"), entryIntoForce: "2001-01-05", memberOf: W("loi/2001/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2001/01/01/n1/jo"), ["art_1"], inFileDate: "2001-01-05");
        h.State(W("loi/2001/01/01/n1/consolide/20100101"), "2010-01-01", memberOf: W("loi/2001/01/01/n1"), expressions: ("fr", Fra))
            .Withheld(W("loi/2001/01/01/n1/consolide/20100101"));
        h.State(W("loi/2001/01/01/n1/consolide/20100102"), "2010-01-01", memberOf: W("loi/2001/01/01/n1"), expressions: ("fr", Fra))
            .Pdf(W("loi/2001/01/01/n1/consolide/20100102"));
        h.State(W("loi/2001/01/01/n1/consolide/20150101"), "2015-01-01", memberOf: W("loi/2001/01/01/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2001/01/01/n1/consolide/20150101"), ["art_1"], identityClass: "same_work_act");

        h.State(W("loi/2002/02/02/n1/jo"), entryIntoForce: "2002-02-05", memberOf: W("loi/2002/02/02/n1"), expressions: ("fr", Fra))
            .Pdf(W("loi/2002/02/02/n1/jo"));
        h.State(W("loi/2002/02/02/n1/consolide/20120101"), "2012-01-01", memberOf: W("loi/2002/02/02/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2002/02/02/n1/consolide/20120101"), ["art_1"], identityClass: "same_work_act");

        h.State(W("loi/2003/03/03/n1/jo"), entryIntoForce: "2003-03-07", memberOf: W("loi/2003/03/03/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2003/03/03/n1/jo"), ["art_1"], inFileDate: "2003-03-03");
        h.State(W("loi/2003/03/03/n1/consolide/20030303"), "2003-03-03", memberOf: W("loi/2003/03/03/n1"));

        h.State(W("loi/2004/04/04/n1/consolide/20200101"), "2020-01-01", "2023-01-01", memberOf: W("loi/2004/04/04/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2004/04/04/n1/consolide/20200101"), ["art_1"]);
        h.State(W("loi/2004/04/04/n1/consolide/20210101"), "2021-01-01", "2022-01-01", memberOf: W("loi/2004/04/04/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2004/04/04/n1/consolide/20210101"), ["art_1"]);
        h.State(W("loi/2004/04/04/n1/consolide/20250101"), "2025-01-01", memberOf: W("loi/2004/04/04/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2004/04/04/n1/consolide/20250101"), ["art_1"]);
        h.State(W("loi/2007/07/07/n1/consolide/20200101"), "2020-01-01", "2023-01-01", memberOf: W("loi/2007/07/07/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2007/07/07/n1/consolide/20200101"), ["art_1"]);
        h.State(W("loi/2007/07/07/n1/consolide/20210101"), "2021-01-01", "2022-01-01", memberOf: W("loi/2007/07/07/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2007/07/07/n1/consolide/20210101"), ["art_1"]);

        h.State(W("loi/2008/08/08/n1/jo"), entryIntoForce: "2008-08-10", memberOf: W("loi/2008/08/08/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2008/08/08/n1/jo"), ["art_1"], inFileDate: "2008-08-10");
        h.State(W("loi/2008/08/08/n1/consolide/20100101"), "2010-01-01", memberOf: W("loi/2008/08/08/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2008/08/08/n1/consolide/20100101"), ["art_1"], identityClass: "same_work_act");
        h.State(W("loi/2008/08/08/n1/consolide/20120101"), "2012-01-01", memberOf: W("loi/2008/08/08/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2008/08/08/n1/consolide/20120101"), ["art_1"], identityClass: "same_work_act");
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

        /// <summary>
        /// The fixture's index bound to the shared acquisition's corpus, beside it, opened as the server opens a mount; with
        /// <paramref name="withoutHeldStatesOf"/>, that work's held rows and their articles are left out of the index, so every
        /// state of it the index holds is textless (a shape the builder does not write today).
        /// </summary>
        public static async Task<IntervalMount> CreateAsync(Harness? harness = null, string? withoutHeldStatesOf = null)
        {
            var shared = await MountedFixture.CreateAsync();
            var corpusRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(shared.CorpusSha256), shared.CorpusSha256);
            var built = (harness ?? IntervalFixture()).BuildFor(corpusRef, withoutHeldStatesOf: withoutHeldStatesOf);
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

    /// <summary>The states a refusal names, each by its date (the payload names them by permalink, scalars only).</summary>
    private static string[] Named(JsonElement payload) =>
        payload.GetProperty("named_states").EnumerateArray().Select(static state => state.GetString()!.Split('/')[3][..10]).ToArray();

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
        Assert.AreEqual("not_available_misfiled", misfiled.GetProperty("text_status").GetString());
        StringAssert.StartsWith(misfiled.GetProperty("text_status_reason").GetString(), "content consistency: shared_file:");
        Assert.AreEqual("2018-04-01", misfiled.GetProperty("applicability_date").GetString());
        Assert.AreEqual("code", misfiled.GetProperty("state_kind").GetString());
        Assert.IsFalse(misfiled.GetProperty("asserts_absence_of_law").GetBoolean());
        Assert.AreEqual("not_available_pdf",
            Refused(await AsOfAsync(mount, "code-civil", "2020-06-01"), "text_not_available").GetProperty("text_status").GetString());
        var notAcquired = Refused(await AsOfAsync(mount, "code-civil", "2027-02-01"), "text_not_available");
        Assert.AreEqual("not_acquired", notAcquired.GetProperty("text_status").GetString());
        Assert.AreEqual(JsonValueKind.Null, notAcquired.GetProperty("permalink").ValueKind, "a state naming no expression has no permalink");
        Assert.AreEqual("/lu-legilux/code-civil/2027-01-01", notAcquired.GetProperty("stable_coordinate").GetString());
        // Every member a scalar or a list of scalars, as a refusal card reads payloads.
        foreach (var payload in new[] { misfiled, notAcquired })
        {
            Assert.IsTrue(payload.EnumerateObject().All(static member => member.Value.ValueKind is not JsonValueKind.Object &&
                (member.Value.ValueKind != JsonValueKind.Array || member.Value.EnumerateArray().All(static item => item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))));
        }

        // After an act's withheld consolidation: the consolidation covers the date, so the act as published is not served.
        var withheld = Refused(await AsOfAsync(mount, "loi-1798-11-03-n1", "2020-06-01"), "text_withheld");
        Assert.AreEqual(W("loi/1798/11/03/n1/consolide/20200101"), withheld.GetProperty("official_identity").GetString());
        Assert.AreEqual(W("loi/1798/11/03/n1/consolide/20200101"), withheld.GetProperty("official_link").GetString());
        Assert.AreEqual(withheld.GetProperty("state_sha256").GetString(), withheld.GetProperty("content_sha256").GetString());
        Assert.AreEqual("withheld_rights", withheld.GetProperty("text_status").GetString());

        // The textless state's own permalink is refused for its text, never unknown.
        var permalink = withheld.GetProperty("permalink").GetString()!;
        var pinned = Refused(await AskAsync(mount, "as_of", new { identifier = permalink, date = "2020-01-01" }), "text_withheld");
        Assert.AreEqual(withheld.GetProperty("state_sha256").GetString(), pinned.GetProperty("state_sha256").GetString());
        Assert.AreEqual("withheld_rights", pinned.GetProperty("text_status").GetString());
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
        Assert.AreEqual("2021-06-01", hole.GetProperty("hole_from").GetString());
        Assert.AreEqual("2022-01-01", hole.GetProperty("hole_to").GetString());
        Assert.AreEqual("2020-01-01", hole.GetProperty("nearest_earlier").GetString(), "index 9 names the state before the date");
        Assert.AreEqual("2022-01-01", hole.GetProperty("nearest_later").GetString());
        Assert.AreEqual("1798-11-07", hole.GetProperty("history_begins").GetString());
        CollectionAssert.AreEqual(new[] { "2020-01-01" }, Named(hole));
        Assert.AreEqual("2026-10-12T18:17:05Z", hole.GetProperty("catalogue_read_no_later_than").GetString());

        // The end day of a suspected inclusive end: given to neither state, and never called a hole.
        Assert.AreEqual("loi/1996/11/07/n1/consolide/20200101", Resource(Served(await AsOfAsync(mount, "loi-1996-11-07-n1", "2022-12-30"))));
        var unclear = Refused(await AsOfAsync(mount, "loi-1996-11-07-n1", "2022-12-31"), "no_version_for_date");
        Assert.AreEqual("publisher_end_convention_unclear", unclear.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] { "2020-01-01", "2023-01-01" }, Named(unclear));
        Assert.AreEqual(JsonValueKind.Null, unclear.GetProperty("hole_from").ValueKind);
        Assert.AreEqual("loi/1996/11/07/n1/consolide/20230101", Resource(Served(await AsOfAsync(mount, "loi-1996-11-07-n1", "2023-01-01"))));

        // Where a state that ends on or before its start would apply: refused, naming it; its own permalink still opens it.
        var invalidFirst = Refused(await AsOfAsync(mount, "rgd-2007-06-07-n2", "2023-01-01"), "no_version_for_date");
        Assert.AreEqual("publisher_interval_invalid", invalidFirst.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] { "2022-12-14" }, Named(invalidFirst));
        var invalidHeld = Refused(await AsOfAsync(mount, "rgd-2007-06-07-n2", "2023-08-01"), "no_version_for_date");
        Assert.AreEqual("publisher_interval_invalid", invalidHeld.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] { "2023-07-17" }, Named(invalidHeld));
        var invalidPermalink = invalidHeld.GetProperty("named_states").EnumerateArray().Single().GetString();
        var opened = Served(await AskAsync(mount, "as_of", new { identifier = invalidPermalink, date = "2023-07-17" }));
        Assert.AreEqual(invalidHeld.GetProperty("named_state_sha256s").EnumerateArray().Single().GetString(), opened.GetProperty("state_sha256").GetString());
        Assert.AreEqual("invalid", opened.GetProperty("boundary").GetString());
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
        Assert.AreEqual(W("loi/2005/01/01/n1/jo"), closed.GetProperty("base_act_iri").GetString());
        Assert.AreEqual(V3CorpusMount.BaseActRule, closed.GetProperty("base_act_rule").GetString());
        Assert.IsFalse(Refused(await AsOfAsync(mount, "loi-1798-11-03-n1", "2021-07-01"), "no_version_for_date").TryGetProperty("base_act_iri", out _),
            "only a closed end names the base act's facts");
        Assert.AreEqual("2015-01-01", closed.GetProperty("base_act_no_longer_in_force").EnumerateArray().Single().GetString());

        var stillInForce = Refused(await AsOfAsync(mount, "loi-2006-02-02-n1", "2016-01-01"), "no_version_for_date");
        Assert.AreEqual("after_closed_end", stillInForce.GetProperty("reason").GetString());
        Assert.AreEqual(0, stillInForce.GetProperty("base_act_no_longer_in_force").GetArrayLength(),
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
        Assert.AreEqual(2, differ.GetProperty("candidate_state_sha256s").GetArrayLength());
        CollectionAssert.AreEquivalent(new[] { "as_published", "consolidation" },
            differ.GetProperty("candidate_state_kinds").EnumerateArray().Select(static kind => kind.GetString()).ToArray());
        Assert.IsTrue(differ.GetProperty("candidate_applicability_dates").EnumerateArray().All(static date => date.GetString() == "2021-05-05"));
        Assert.IsTrue(differ.GetProperty("candidate_text_statuses").EnumerateArray().All(static status => status.GetString() == "held"));
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
        // Every hit of the work is the act as published's (the review of #987: this compared a hit's digest with itself).
        var act = (await AskAsync(mount, "dossier", new { identifier = "/lu-legilux/loi-2018-09-14-a883" })).Result!.Value.GetProperty("states")
            .EnumerateArray().Single(static state => state.GetProperty("publisher_legal_resource_iri").GetString()!.EndsWith("/jo", StringComparison.Ordinal))
            .GetProperty("state_sha256").GetString();
        var actHits = dated.GetProperty("hits").EnumerateArray().Where(static hit => hit.GetProperty("work_key").GetString() == "loi-2018-09-14-a883").ToArray();
        Assert.IsNotEmpty(actHits);
        Assert.IsTrue(actHits.All(hit => hit.GetProperty("state_sha256").GetString() == act &&
                                         hit.GetProperty("date_role").GetString() == "applicability_in_file"), "the act as published is the version served");
        Assert.AreEqual(JsonValueKind.Undefined, dated.TryGetProperty("fold", out var fold) ? fold.ValueKind : JsonValueKind.Undefined,
            "with a date nothing is folded");

        // With the work named and the date, it is as_of's question and refusal.
        var named = await AskAsync(mount, "search", new { query = "texte", language = "fra", identifier = "/lu-legilux/loi-1798-11-03-n1", date = "2020-06-01" });
        Assert.AreEqual("text_withheld", named.Refusal!.Code);
    }

    [TestMethod]
    public async Task AnEliThatNamesOneVersionOpensThatVersionThoughTheIntervalRulesDesignateAnother()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // On 2018-09-14 the act as published and its same-date consolidation hold one wording, and the rules serve the act; the
        // consolidation's own ELI names that version, and the exact reference opens it, selected within the scope it names.
        var states = (await AskAsync(mount, "dossier", new { identifier = "/lu-legilux/loi-2018-09-14-a883" })).Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        var consolidation = states.Single(static state => state.GetProperty("publisher_legal_resource_iri").GetString()!.EndsWith("/consolide/20180914", StringComparison.Ordinal));
        var opened = await AskAsync(mount, "search", new { query = W("loi/2018/09/14/a883/consolide/20180914"), language = "fra" });
        Assert.AreEqual(V3Verdicts.Answer, opened.Verdict, opened.Refusal?.HelpfulPayload.GetRawText());
        StringAssert.Contains(opened.Result!.Value.GetRawText(), consolidation.GetProperty("state_sha256").GetString());
    }

    /// <summary>Every row of an in_force_on question across works, read page by page from its continue_after.</summary>
    private static async Task<(string[] Keys, int Pages)> PagedInForceOnAsync(V3CorpusMount mount, string date, int limit)
    {
        var keys = new List<string>();
        string? after = null;
        var pages = 0;
        do
        {
            Assert.IsLessThan(50, pages, "the cursor advances");
            var page = (await AskAsync(mount, "in_force_on", after is null ? new { date, limit } : (object)new { date, limit, after_work_key = after })).Result!.Value;
            pages++;
            keys.AddRange(page.GetProperty("states").EnumerateArray().Select(static row => row.GetProperty("work_key").GetString()!));
            after = page.GetProperty("truncated").GetBoolean() ? page.GetProperty("continue_after").GetString() : null;
            Assert.AreEqual(page.GetProperty("truncated").GetBoolean(), page.GetProperty("continue_after").ValueKind == JsonValueKind.String);
        }
        while (after is not null);
        return (keys.ToArray(), pages);
    }

    [TestMethod]
    public async Task InForceOnAcrossWorksPagesOverWorksNoStateCoversAndNeverEndsAPageShortOfTheWorksAfterIt()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // The review of #987, F1: a work with a state before the date and none covering it gives no row, and the page goes on
        // reading works until it is filled or none remain, so paging finds every row the unlimited answer holds.
        foreach (var date in new[] { "2021-07-01", "2016-01-01", "2026-10-18" })
        {
            var whole = (await AskAsync(mount, "in_force_on", new { date })).Result!.Value;
            var expected = whole.GetProperty("states").EnumerateArray().Select(static row => row.GetProperty("work_key").GetString()!).ToArray();
            Assert.IsFalse(whole.GetProperty("truncated").GetBoolean(), date);
            foreach (var limit in new[] { 1, 2 })
            {
                var (paged, pages) = await PagedInForceOnAsync(mount, date, limit);
                CollectionAssert.AreEqual(expected, paged, $"{date}, limit {limit}: every row, in order, over {pages} pages");
            }
        }

        var later = (await AskAsync(mount, "in_force_on", new { date = "2016-01-01" })).Result!.Value;
        var works = later.GetProperty("states").EnumerateArray().Select(static row => row.GetProperty("work_key").GetString()).ToArray();
        CollectionAssert.Contains(works, "loi-2010-03-03-n1", "a work after a closed one is not lost");
        CollectionAssert.DoesNotContain(works, "loi-2005-01-01-n1");

        // F8: a work with a state before the date and none covering it is not a work with a state on the date.
        var population = later.GetProperty("population");
        var covered = population.GetProperty("works_with_a_state_on_date").GetInt64();
        Assert.AreEqual(works.Distinct().LongCount(), covered, "the covered works are the works the answer gives rows for");
        Assert.IsGreaterThan(0, population.GetProperty("works_not_covered_on_date").GetInt64(), "the closed works are counted apart");

        // REST and MCP answer the same page.
        var rest = (await AskAsync(mount, "in_force_on", new { date = "2021-07-01", limit = 1 })).Result!.Value;
        var bytes = await V3McpJsonRpc.HandleAsync(
            JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "in_force_on", arguments = new { date = "2021-07-01", limit = 1 } } }),
            new V3PlatformHost(), mount, static () => At, "mcp_in_force_on_paging", CancellationToken.None);
        Assert.IsNotNull(bytes);
        using var mcp = JsonDocument.Parse(bytes);
        var structured = mcp.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("result").GetProperty("value");
        Assert.AreEqual(rest.GetProperty("states").GetRawText(), structured.GetProperty("states").GetRawText());
        Assert.AreEqual(rest.GetProperty("continue_after").GetRawText(), structured.GetProperty("continue_after").GetRawText());
    }

    [TestMethod]
    public async Task AnEliThatNamesOneVersionOpensItOnlyWhereTheRulesServeItAndElseRefusesWithTheirReason()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // The review of #987, F2: the act as published of 2005, named by its ELI, after the work's last state has closed: the
        // rules' reason, never the act in its place.
        var closed = await AskAsync(mount, "search", new { query = W("loi/2005/01/01/n1/jo") + ", au 1er janvier 2016", language = "fra" });
        var closedPayload = Refused(closed, "no_version_for_date");
        Assert.AreEqual("after_closed_end", closedPayload.GetProperty("reason").GetString());

        // On a date the 2010 consolidation covers, the act is not the version the date selects: refused, the consolidation named.
        var later = await AskAsync(mount, "search", new { query = W("loi/2005/01/01/n1/jo") + ", au 1er janvier 2012", language = "fra" });
        var laterPayload = Refused(later, "no_version_for_date");
        Assert.AreEqual(V3CorpusMount.ReasonOutsideNamedVersion, laterPayload.GetProperty("reason").GetString());
        StringAssert.Contains(laterPayload.GetProperty("covering_states").EnumerateArray().Single().GetString(), "/2010-01-01--");
        StringAssert.Contains(laterPayload.GetProperty("named_states").EnumerateArray().Single().GetString(), "/2005-01-05--");
        // The work's first and nearest dates, as every reason gives them (the French review of #987's repair round): the 2010
        // consolidation is the nearest earlier state, and none is later.
        Assert.AreEqual("2005-01-05", laterPayload.GetProperty("history_begins").GetString());
        Assert.AreEqual("2010-01-01", laterPayload.GetProperty("nearest_earlier").GetString());
        Assert.AreEqual(JsonValueKind.Null, laterPayload.GetProperty("nearest_later").ValueKind);
        Assert.IsTrue(laterPayload.EnumerateObject().All(static member => member.Value.ValueKind is not JsonValueKind.Object), "flat, as a card reads it");

        // On a date the act covers, the act opens.
        var own = await AskAsync(mount, "search", new { query = W("loi/2005/01/01/n1/jo") + ", au 1er janvier 2008", language = "fra" });
        Assert.AreEqual(V3Verdicts.Answer, own.Verdict, own.Refusal?.HelpfulPayload.GetRawText());

        // An ELI with no date is asked at its version's own date, and is refused there when that date is ambiguous or the
        // version's interval is invalid (the review of #987: the comment said it always opened).
        Assert.AreEqual(V3Verdicts.Answer, (await AskAsync(mount, "search", new { query = W("loi/2005/01/01/n1/jo"), language = "fra" })).Verdict);
        foreach (var eli in new[] { "loi/2021/05/05/n1/jo", "loi/2021/05/05/n1/consolide/20210505" })
        {
            Refused(await AskAsync(mount, "search", new { query = W(eli), language = "fra" }), "ambiguous_version");
        }

        var invalid = Refused(await AskAsync(mount, "search", new { query = W("rgd/2007/06/07/n2/consolide/20230717"), language = "fra" }), "no_version_for_date");
        Assert.AreEqual(V3CorpusMount.ReasonIntervalInvalid, invalid.GetProperty("reason").GetString());
    }

    [TestMethod]
    public async Task ACodesBaseActIsTheOneItsStateNamesAndACodeThatNamesNoneNamesNoBaseAct()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // The review of #987, F4: a code's work is the code, which has no /jo; its base act is the one isMemberOf names.
        var route = Refused(await AsOfAsync(mount, "code-route", "2016-01-01"), "no_version_for_date");
        Assert.AreEqual("after_closed_end", route.GetProperty("reason").GetString());
        Assert.AreEqual("2015-01-01", route.GetProperty("closed_on").GetString());
        Assert.AreEqual(W("loi/1955/02/14/n1/jo"), route.GetProperty("base_act_iri").GetString());

        var eau = Refused(await AsOfAsync(mount, "code-eau", "2016-01-01"), "no_version_for_date");
        Assert.AreEqual("after_closed_end", eau.GetProperty("reason").GetString());
        Assert.AreEqual("2014-01-01", eau.GetProperty("closed_on").GetString());
        Assert.IsFalse(eau.TryGetProperty("base_act_iri", out _), "no base act is invented for a code that names none");
        Assert.IsFalse(eau.TryGetProperty("base_act_rule", out _));
    }

    [TestMethod]
    public async Task TheRadarFollowsTheIntervalRulesItsSameDatePairIsOneRowAndAHoleOrAnUnclearEndLeavesNoBaseline()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // The review of #987, F6.
        var changes = (await AskAsync(mount, "changes_in_period", new { date_from = "2018-01-01", date_to = "2024-12-31" })).Result!.Value
            .GetProperty("changes").EnumerateArray().ToArray();
        JsonElement[] Of(string work, string date) => changes.Where(row => row.GetProperty("work_key").GetString() == work &&
            row.GetProperty("state").GetProperty("applicability_date").GetString() == date).ToArray();

        // The act and its same-date consolidation of one wording are one row, the act's.
        var a883 = Of("loi-2018-09-14-a883", "2018-09-14");
        Assert.HasCount(1, a883);
        StringAssert.EndsWith(a883[0].GetProperty("state").GetProperty("expression_iri").GetString(), "/jo/fr");

        // The day before the 2023 consolidation is the unclear end day, and the day before rgd-2007's 2024-01-01 state falls
        // where an invalid interval would apply: no baseline, never the state before them.
        Assert.AreEqual("no_held_baseline", Of("loi-1996-11-07-n1", "2023-01-01").Single().GetProperty("reason").GetString());
        var invalid = Of("rgd-2007-06-07-n2", "2024-01-01").Single();
        Assert.AreEqual("no_held_baseline", invalid.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, invalid.GetProperty("baseline").ValueKind);

        // Where the day before is covered, the baseline is the state the rules serve there.
        var overlap = Of("rgd-2007-06-07-n2", "2024-06-01").Single();
        Assert.AreEqual("2024-01-01", overlap.GetProperty("baseline").GetProperty("applicability_date").GetString());

        // The differing same-date pair is ambiguous on its own date, both named.
        var differ = changes.Where(static row => row.GetProperty("work_key").GetString() == "loi-2021-05-05-n1").ToArray();
        Assert.IsTrue(differ.Length > 0 && differ.All(static row => row.GetProperty("reason").GetString() == "ambiguous_version" &&
            row.GetProperty("candidates").GetArrayLength() == 2));
    }

    [TestMethod]
    public async Task AnArticlesHistoryCarriesEachStatesCatalogueFactsOnItsRowsAndItsAbsentStates()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // The review of #987 (R21 on the history): a row and an absent state carry the catalogue facts the dossier gives the
        // same state, so the page says each date with its role.
        var dossier = (await AskAsync(mount, "dossier", new { identifier = "/lu-legilux/loi-2005-01-01-n1" })).Result!.Value
            .GetProperty("states").EnumerateArray().ToDictionary(static state => state.GetProperty("state_sha256").GetString()!);
        var history = (await AskAsync(mount, "article_history", new { identifier = "/lu-legilux/loi-2005-01-01-n1", anchor = "art_2" })).Result!.Value;
        var row = history.GetProperty("states").EnumerateArray().Single();
        var absent = history.GetProperty("absent_in_states").EnumerateArray().Single();
        StringAssert.Contains(row.GetProperty("permalink").GetString(), "/2010-01-01--");
        StringAssert.Contains(absent.GetProperty("permalink").GetString(), "/2005-01-05--");
        foreach (var state in new[] { row, absent })
        {
            var served = dossier[state.GetProperty("state_sha256").GetString()!];
            foreach (var key in new[] { "date_role", "entry_into_force", "state_kind", "applicable_to", "boundary", "text_status" })
            {
                Assert.AreEqual(served.GetProperty(key).GetRawText(), state.GetProperty(key).GetRawText(), key);
            }
        }

        Assert.AreEqual("as_published", absent.GetProperty("state_kind").GetString());
    }

    [TestMethod]
    public async Task TheReadersChainIsTheIndexsOwn()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        using var reader = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName),
            await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName)),
            CancellationToken.None);
        foreach (var work in new[] { "code-civil", "loi-1798-11-03-n1", "loi-1996-11-07-n1", "loi-2017-07-20-a674", "loi-2018-09-14-a883", "rgd-2007-06-07-n2", "loi-2005-01-01-n1", "code-route", "code-eau" })
        {
            var rows = reader.ResolveCatalogueStates(work);
            CollectionAssert.AreEqual(rows.ToArray(), V3CorpusMount.Rechain(rows), $"{work}: the API's chain recomputes the index's ends and boundaries");
        }
    }

    [TestMethod]
    public async Task AnAmbiguityOfStatesNoneOfWhichHoldsTextIsAContinuityBreakAndLeavesTheRadarNoBaseline()
    {
        await using var fixture = await IntervalMount.CreateAsync(RepairFixture());
        var mount = fixture.Mount;

        // The review of #987: two consolidations of 2010, one withheld and one a PDF, cover 2010 to 2015; as_of names both.
        var asOf = Refused(await AsOfAsync(mount, "loi-2001-01-01-n1", "2012-01-01"), "ambiguous_version");
        CollectionAssert.AreEquivalent(new[] { "withheld_rights", "not_available_pdf" },
            asOf.GetProperty("candidate_text_statuses").EnumerateArray().Select(static status => status.GetString()).ToArray());

        // No state with held text covers those dates: a break, with each state's text status, and the next row after it.
        var history = (await AskAsync(mount, "article_history", new { identifier = "/lu-legilux/loi-2001-01-01-n1", anchor = "art_1" })).Result!.Value;
        var gap = history.GetProperty("continuity_breaks").EnumerateArray().Single();
        Assert.AreEqual("2010-01-01", gap.GetProperty("from").GetString());
        Assert.AreEqual("2015-01-01", gap.GetProperty("to").GetString());
        CollectionAssert.AreEquivalent(new[] { "withheld_rights", "not_available_pdf" },
            gap.GetProperty("reasons").EnumerateArray().Select(static reason => reason.GetString()).ToArray());
        Assert.AreEqual(2, gap.GetProperty("named_state_sha256s").GetArrayLength());
        CollectionAssert.AreEqual(new[] { "2001-01-05:False", "2015-01-01:True" },
            history.GetProperty("states").EnumerateArray()
                .Select(static row => $"{row.GetProperty("applicability_date").GetString()}:{row.GetProperty("after_break").GetBoolean()}").ToArray());

        // The radar: the day before the 2015 consolidation is covered by the two states without text, so it has no baseline.
        var row = (await AskAsync(mount, "changes_in_period", new { identifier = "/lu-legilux/loi-2001-01-01-n1", date_from = "2015-01-01", date_to = "2015-01-01" }))
            .Result!.Value.GetProperty("changes").EnumerateArray().Single();
        Assert.AreEqual("no_held_baseline", row.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("candidates").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("baseline").ValueKind);

        // An ambiguity one of whose states holds text is no break (the act and its consolidation of 2021 differ, both held).
        var held = (await AskAsync(mount, "article_history", new { identifier = "/lu-legilux/loi-2021-05-05-n1", anchor = "art_1" })).Result!.Value;
        Assert.AreEqual(0, held.GetProperty("continuity_breaks").GetArrayLength());
    }

    [TestMethod]
    public async Task InForceOnAcrossWorksNamesAWorkWhoseOnlyStateOnOrBeforeTheDateHoldsNoTextAndCountsItCovered()
    {
        await using var fixture = await IntervalMount.CreateAsync(RepairFixture());
        var mount = fixture.Mount;

        // The review of #987: the 2002 act's text is not held (a PDF) and its first held state is of 2012; on 2005-01-01 the act
        // covers the date, so the work is a row naming it and why, as as_of refuses the date for its text.
        Refused(await AsOfAsync(mount, "loi-2002-02-02-n1", "2005-01-01"), "text_not_available");
        foreach (var language in new[] { null, "fra" })
        {
            var across = (await AskAsync(mount, "in_force_on", language is null ? new { date = "2005-01-01" } : (object)new { date = "2005-01-01", language })).Result!.Value;
            var row = across.GetProperty("states").EnumerateArray().Single(static row => row.GetProperty("work_key").GetString() == "loi-2002-02-02-n1");
            Assert.AreEqual("text_not_available", row.GetProperty("reason").GetString(), language);
            Assert.AreEqual("not_available_pdf", row.GetProperty("state").GetProperty("text_status").GetString());
            Assert.AreEqual(JsonValueKind.Null, row.GetProperty("resolve").ValueKind);

            // Counted as F8 counts a covered work: the covered works are the works with rows, and every work is counted once.
            var population = across.GetProperty("population");
            var works = across.GetProperty("states").EnumerateArray().Select(static row => row.GetProperty("work_key").GetString()).Distinct().LongCount();
            Assert.AreEqual(works, population.GetProperty("works_with_a_state_on_date").GetInt64(), language);
            var inScope = language is null ? population.GetProperty("works_held").GetInt64() : population.GetProperty("works_holding_the_language").GetInt64();
            Assert.AreEqual(inScope,
                population.GetProperty("works_with_a_state_on_date").GetInt64() + population.GetProperty("works_not_covered_on_date").GetInt64() +
                population.GetProperty("works_beginning_later").GetInt64(), language);
            Assert.IsGreaterThanOrEqualTo(0L, population.GetProperty("works_not_covered_on_date").GetInt64());
        }

        // Paging finds it too.
        var (paged, _) = await PagedInForceOnAsync(mount, "2005-01-01", 1);
        CollectionAssert.Contains(paged, "loi-2002-02-02-n1");
    }

    [TestMethod]
    public async Task AnAmbiguityNamesAStateTheCatalogueNamesNoExpressionOfByItsCoordinateWithItsListsAligned()
    {
        await using var fixture = await IntervalMount.CreateAsync(RepairFixture());
        var mount = fixture.Mount;

        // The review of #987: the 2003 act held and a consolidation of its date that names no expression (so no language and
        // no permalink) both cover 2004-01-01. Each operation names both, the consolidation by its stable coordinate, never a
        // permalink without a language, and the candidate lists stay aligned on the candidates.
        const string Coordinate = "/lu-legilux/loi-2003-03-03-n1/2003-03-03";
        foreach (var (operation, parameters) in new (string, object)[]
                 {
                     ("as_of", new { identifier = "/lu-legilux/loi-2003-03-03-n1", date = "2004-01-01" }),
                     ("evidence_bundle", new { identifier = "/lu-legilux/loi-2003-03-03-n1", date = "2004-01-01" }),
                     ("in_force_on", new { identifier = "/lu-legilux/loi-2003-03-03-n1", date = "2004-01-01" }),
                 })
        {
            var payload = Refused(await AskAsync(mount, operation, parameters), "ambiguous_version");
            var candidates = payload.GetProperty("candidates").EnumerateArray().Select(static candidate => candidate.GetString()!).ToArray();
            Assert.HasCount(2, candidates, operation);
            Assert.AreEqual(Coordinate, candidates[0], operation);
            StringAssert.StartsWith(candidates[1], Coordinate + "--");
            StringAssert.EndsWith(candidates[1], "/fra");
            var digests = payload.GetProperty("candidate_state_sha256s").EnumerateArray().Select(static digest => digest.GetString()!).ToArray();
            Assert.HasCount(2, digests, operation);
            Assert.AreEqual(digests[1], candidates[1][(Coordinate.Length + 2)..^4], "the permalink's digest is its place's");
            foreach (var list in new[] { "candidate_state_kinds", "candidate_date_roles", "candidate_applicability_dates", "candidate_applicable_tos",
                         "candidate_applicable_to_sources", "candidate_boundaries", "candidate_text_statuses" })
            {
                Assert.AreEqual(2, payload.GetProperty(list).GetArrayLength(), $"{operation} {list}");
            }

            CollectionAssert.AreEqual(new[] { "not_acquired", "held" },
                payload.GetProperty("candidate_text_statuses").EnumerateArray().Select(static status => status.GetString()).ToArray(), operation);
        }

        // Across works, the dated search and the radar name it the same way.
        var across = (await AskAsync(mount, "in_force_on", new { date = "2004-01-01" })).Result!.Value.GetProperty("states").EnumerateArray()
            .Single(static row => row.GetProperty("work_key").GetString() == "loi-2003-03-03-n1");
        Assert.AreEqual(Coordinate, across.GetProperty("candidates")[0].GetString());
        var search = (await AskAsync(mount, "search", new { query = "texte", language = "fra", date = "2004-01-01" })).Result!.Value
            .GetProperty("ambiguous_works").EnumerateArray().Single(static row => row.GetProperty("work_key").GetString() == "loi-2003-03-03-n1");
        Assert.AreEqual(Coordinate, search.GetProperty("candidates")[0].GetString());
        var radar = (await AskAsync(mount, "changes_in_period", new { identifier = "/lu-legilux/loi-2003-03-03-n1", date_from = "2003-03-03", date_to = "2003-03-03" }))
            .Result!.Value.GetProperty("changes").EnumerateArray().Single();
        Assert.AreEqual("ambiguous_version", radar.GetProperty("reason").GetString());
        Assert.AreEqual(Coordinate, radar.GetProperty("candidates")[0].GetString());
        Assert.AreEqual(2, radar.GetProperty("candidates").GetArrayLength());
    }

    [TestMethod]
    public async Task AHoleOrAClosedEndAfterANestedStateBeginsWhereTheLastStateCoveringEnds()
    {
        await using var fixture = await IntervalMount.CreateAsync(RepairFixture());
        var mount = fixture.Mount;

        // The review of #987, F7: the 2020 consolidation covers to 2023 and the 2021 one, nested in it, to 2022.
        Assert.AreEqual("loi/2004/04/04/n1/consolide/20200101", Resource(Served(await AsOfAsync(mount, "loi-2004-04-04-n1", "2022-06-01"))));
        var hole = Refused(await AsOfAsync(mount, "loi-2004-04-04-n1", "2024-01-01"), "no_version_for_date");
        Assert.AreEqual("catalogue_hole", hole.GetProperty("reason").GetString());
        Assert.AreEqual("2023-01-01", hole.GetProperty("hole_from").GetString(), "the 2020 consolidation covers 2022 to 2023");
        Assert.AreEqual("2025-01-01", hole.GetProperty("hole_to").GetString());
        CollectionAssert.AreEqual(new[] { "2020-01-01" }, Named(hole), "the state whose end opens the hole");

        // The dossier and the timeline say the same gap, and the overlap ends where the nested state does.
        var dossier = (await AskAsync(mount, "dossier", new { identifier = "/lu-legilux/loi-2004-04-04-n1" })).Result!.Value;
        var digest = dossier.GetProperty("states").EnumerateArray().ToDictionary(static state => state.GetProperty("applicability_date").GetString()!,
            static state => state.GetProperty("state_sha256").GetString()!);
        foreach (var answer in new[] { dossier, (await AskAsync(mount, "timeline", new { identifier = "/lu-legilux/loi-2004-04-04-n1" })).Result!.Value })
        {
            var gap = answer.GetProperty("gaps").EnumerateArray().Single();
            Assert.AreEqual("2023-01-01", gap.GetProperty("from").GetString());
            Assert.AreEqual("2025-01-01", gap.GetProperty("to").GetString());
            Assert.AreEqual(digest["2020-01-01"], gap.GetProperty("after_state_sha256").GetString());
            Assert.AreEqual(digest["2025-01-01"], gap.GetProperty("before_state_sha256").GetString());
            var overlap = answer.GetProperty("overlaps").EnumerateArray().Single();
            Assert.AreEqual("2021-01-01", overlap.GetProperty("from").GetString());
            Assert.AreEqual("2022-01-01", overlap.GetProperty("to").GetString(), "the two states both cover 2021 only");
        }

        // The history breaks over the same span.
        var history = (await AskAsync(mount, "article_history", new { identifier = "/lu-legilux/loi-2004-04-04-n1", anchor = "art_1" })).Result!.Value;
        var broken = history.GetProperty("continuity_breaks").EnumerateArray().Single();
        Assert.AreEqual("2023-01-01", broken.GetProperty("from").GetString());
        Assert.AreEqual("2025-01-01", broken.GetProperty("to").GetString());

        // Without a later state, the catalogue's states end where the 2020 consolidation does.
        var closed = Refused(await AsOfAsync(mount, "loi-2007-07-07-n1", "2024-01-01"), "no_version_for_date");
        Assert.AreEqual("after_closed_end", closed.GetProperty("reason").GetString());
        Assert.AreEqual("2023-01-01", closed.GetProperty("closed_on").GetString());
        Assert.AreEqual(JsonValueKind.Null, closed.GetProperty("nearest_later").ValueKind);
    }

    [TestMethod]
    public async Task AnIdentifierOutsideItsVersionGivesTheWorksFirstAndNearestDatesNotTheVersionsOwn()
    {
        await using var fixture = await IntervalMount.CreateAsync(RepairFixture());
        var mount = fixture.Mount;

        // The French review of #987's repair round: the ELI names the 2008 act as published, and its 2010 consolidation covers
        // 2011-06-01, before the 2012 one. Over the named version alone, the refusal said the nearest earlier state is the act
        // and gave no later one, which the card says as "No later state is held: the requested date follows every state
        // held"; over the work's chain, 2010 is the nearest earlier date and 2012 the nearest later.
        var payload = Refused(await AskAsync(mount, "search", new { query = W("loi/2008/08/08/n1/jo") + ", au 1er juin 2011", language = "fra" }),
            "no_version_for_date");
        Assert.AreEqual(V3CorpusMount.ReasonOutsideNamedVersion, payload.GetProperty("reason").GetString());
        StringAssert.Contains(payload.GetProperty("named_states").EnumerateArray().Single().GetString(), "/2008-08-10--");
        StringAssert.Contains(payload.GetProperty("covering_states").EnumerateArray().Single().GetString(), "/2010-01-01--");
        Assert.AreEqual("2008-08-10", payload.GetProperty("history_begins").GetString());
        Assert.AreEqual("2010-01-01", payload.GetProperty("nearest_earlier").GetString());
        Assert.AreEqual("2012-01-01", payload.GetProperty("nearest_later").GetString());
    }
}
