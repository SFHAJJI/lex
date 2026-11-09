using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.LuxembourgIndexCatalogueStatesTests;
using static Lex.V3.Ingest.Tests.V3CorpusIntervalMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The catalogue's chain where a work's history is read (data design §3.2, slice S7b), driven through the real handler on the
/// verified mount of the multi-body fixture S7a built (<see cref="V3CorpusIntervalMountTests.IntervalFixture"/>): the timeline
/// and the dossier name the textless states and the chain's gaps, overlaps, same-date groups, unclear end days and invalid
/// intervals as the publisher left them; an article's history says where no state with held text covers its dates; a date is
/// said with its role (R21: a comparison of an act dated by its entry into force never says it applicable from that date);
/// an event of a state the catalogue names no expression of has no permalink; and coverage's build-time row says what a
/// build's time is on an index that may be derived again from custody (S7c). Every value is synthetic.
/// </summary>
[TestClass]
public sealed class V3CorpusContinuityMountTests
{
    private static Task<V3Envelope> TimelineAsync(V3CorpusMount mount, string work) =>
        AskAsync(mount, "timeline", new { identifier = "/lu-legilux/" + work });

    private static JsonElement Value(V3Envelope envelope)
    {
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.HelpfulPayload.GetRawText());
        return envelope.Result!.Value;
    }

    private static string[] Strings(JsonElement list) => list.EnumerateArray().Select(static item => item.GetString()!).ToArray();

    /// <summary>The fixture's states of one work by digest, each named by its legal resource below the ELI root.</summary>
    private static Dictionary<string, string> ResourcesOf(JsonElement timeline) =>
        timeline.GetProperty("states").EnumerateArray()
            .Concat(timeline.GetProperty("textless_states").EnumerateArray())
            .ToDictionary(static state => state.GetProperty("state_sha256").GetString()!, static state => state.GetProperty("publisher_legal_resource_iri").GetString()![Eli.Length..], StringComparer.Ordinal);

    [TestMethod]
    public async Task TheTimelineNamesTheTextlessStatesAndTheChainsTransitionsAsThePublisherLeftThem()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        // A code: two held states, then two misfiled, a PDF and one not acquired; every transition contiguous but one derived.
        var code = Value(await TimelineAsync(mount, "code-civil"));
        var names = ResourcesOf(code);
        CollectionAssert.AreEqual(new[] { "code/civil/20160901", "code/civil/20170401" },
            code.GetProperty("states").EnumerateArray().Select(state => names[state.GetProperty("state_sha256").GetString()!]).ToArray());
        CollectionAssert.AreEqual(
            new[] { "not_available_misfiled", "not_available_misfiled", "not_available_pdf", "not_acquired" },
            code.GetProperty("textless_states").EnumerateArray().Select(static state => state.GetProperty("text_status").GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "contiguous", "contiguous", "contiguous", "derived", "contiguous" },
            code.GetProperty("transitions").EnumerateArray().Select(static transition => transition.GetProperty("kind").GetString()).ToArray());
        foreach (var name in new[] { "gaps", "overlaps", "same_date", "unclear_end_days", "invalid_intervals" })
        {
            Assert.AreEqual(0, code.GetProperty(name).GetArrayLength(), name);
        }

        Assert.AreEqual(V3CorpusMount.CatalogueChainRule, code.GetProperty("catalogue_chain_rule").GetString());
        // A textless state is named, never served: it is a summary (no article list), and its permalink refuses for its text.
        var pdf = code.GetProperty("textless_states").EnumerateArray().Single(static state => state.GetProperty("text_status").GetString() == "not_available_pdf");
        Assert.IsFalse(pdf.TryGetProperty("articles", out _));
        Assert.AreEqual("text_not_available", (await AskAsync(mount, "as_of", new { identifier = pdf.GetProperty("permalink").GetString(), date = "2020-01-01" })).Refusal!.Code);

        // A hole after a withheld consolidation, between its publisher's end and the next start.
        var hole = Value(await TimelineAsync(mount, "loi-1798-11-03-n1"));
        var holeNames = ResourcesOf(hole);
        var gap = hole.GetProperty("gaps").EnumerateArray().Single();
        Assert.AreEqual("2021-06-01", gap.GetProperty("from").GetString());
        Assert.AreEqual("2022-01-01", gap.GetProperty("to").GetString());
        Assert.AreEqual("loi/1798/11/03/n1/consolide/20200101", holeNames[gap.GetProperty("after_state_sha256").GetString()!]);
        Assert.AreEqual("loi/1798/11/03/n1/consolide/20220101", holeNames[gap.GetProperty("before_state_sha256").GetString()!]);
        Assert.AreEqual("entry_into_force", hole.GetProperty("states")[0].GetProperty("date_role").GetString());

        // An overlap and two invalid intervals (an end on its start, an end before it).
        var rgd = Value(await TimelineAsync(mount, "rgd-2007-06-07-n2"));
        var rgdNames = ResourcesOf(rgd);
        var overlap = rgd.GetProperty("overlaps").EnumerateArray().Single();
        Assert.AreEqual("2024-06-01", overlap.GetProperty("from").GetString());
        Assert.AreEqual("2024-12-31", overlap.GetProperty("to").GetString());
        CollectionAssert.AreEqual(new[] { "rgd/2007/06/07/n2/consolide/20240101", "rgd/2007/06/07/n2/consolide/20240601" },
            Strings(overlap.GetProperty("state_sha256s")).Select(digest => rgdNames[digest]).ToArray());
        CollectionAssert.AreEqual(new[] { "2022-12-14|2022-12-14|withheld_rights", "2023-07-17|2023-07-01|held" },
            rgd.GetProperty("invalid_intervals").EnumerateArray().Select(static row =>
                $"{row.GetProperty("applicability_date").GetString()}|{row.GetProperty("applicable_to").GetString()}|{row.GetProperty("text_status").GetString()}").ToArray());

        // A suspected inclusive end: its end day, given to neither state.
        var unclear = Value(await TimelineAsync(mount, "loi-1996-11-07-n1")).GetProperty("unclear_end_days").EnumerateArray().Single();
        Assert.AreEqual("2022-12-31", unclear.GetProperty("date").GetString());

        // An act and its consolidation of one date: one group, the act first (the chain's order).
        var sameDate = Value(await TimelineAsync(mount, "loi-2018-09-14-a883"));
        var sameNames = ResourcesOf(sameDate);
        var group = sameDate.GetProperty("same_date").EnumerateArray().Single();
        Assert.AreEqual("2018-09-14", group.GetProperty("applicability_date").GetString());
        CollectionAssert.AreEqual(new[] { "loi/2018/09/14/a883/jo", "loi/2018/09/14/a883/consolide/20180914" },
            Strings(group.GetProperty("state_sha256s")).Select(digest => sameNames[digest]).ToArray());

        // The dossier carries the same chain, member for member.
        foreach (var work in new[] { "code-civil", "loi-1798-11-03-n1", "rgd-2007-06-07-n2", "loi-1996-11-07-n1", "loi-2018-09-14-a883" })
        {
            var timeline = Value(await TimelineAsync(mount, work));
            var dossier = Value(await AskAsync(mount, "dossier", new { identifier = "/lu-legilux/" + work }));
            foreach (var name in new[] { "textless_states", "transitions", "gaps", "overlaps", "same_date", "unclear_end_days", "invalid_intervals", "catalogue_chain_rule" })
            {
                Assert.AreEqual(timeline.GetProperty(name).GetRawText(), dossier.GetProperty(name).GetRawText(), $"{work} {name}");
            }

            CollectionAssert.DoesNotContain(
                dossier.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray(), "coverage_gaps",
                "the gaps are listed, so the dossier no longer says they are not");
        }
    }

    [TestMethod]
    public async Task AnArticlesHistorySaysWhereNoStateWithHeldTextCoversItsDates()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;
        Task<V3Envelope> HistoryAsync(string work, string anchor) =>
            AskAsync(mount, "article_history", new { identifier = "/lu-legilux/" + work, anchor });

        // After the act as published: its withheld consolidation, the catalogue's hole and a state not acquired, one run.
        var act = Value(await HistoryAsync("loi-1798-11-03-n1", "art_1"));
        var row = act.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("entry_into_force", row.GetProperty("date_role").GetString(), "each row carries its state's date role (R21)");
        Assert.IsFalse(row.GetProperty("after_break").GetBoolean());
        var run = act.GetProperty("continuity_breaks").EnumerateArray().Single();
        Assert.AreEqual("fra", run.GetProperty("language").GetString());
        Assert.AreEqual("2020-01-01", run.GetProperty("from").GetString());
        Assert.AreEqual(JsonValueKind.Null, run.GetProperty("to").ValueKind, "no later state holds text");
        CollectionAssert.AreEqual(new[] { "withheld_rights", "catalogue_hole", "not_acquired" }, Strings(run.GetProperty("reasons")));
        CollectionAssert.AreEqual(new[] { "2020-01-01", "2022-01-01" },
            Strings(run.GetProperty("named_states")).Select(static address => address.Split('/')[3][..10]).ToArray());
        Assert.AreEqual(V3CorpusMount.ContinuityRule, act.GetProperty("continuity_rule").GetString());
        Assert.AreEqual(V3CorpusMount.AnchorRule, act.GetProperty("anchor_rule").GetString());

        // The end day of a suspected inclusive end parts two held states: the later row says a break lies before it.
        var unclear = Value(await HistoryAsync("loi-1996-11-07-n1", "art_1"));
        CollectionAssert.AreEqual(new[] { false, true },
            unclear.GetProperty("states").EnumerateArray().Select(static state => state.GetProperty("after_break").GetBoolean()).ToArray());
        var day = unclear.GetProperty("continuity_breaks").EnumerateArray().Single();
        Assert.AreEqual("2022-12-31", day.GetProperty("from").GetString());
        Assert.AreEqual("2023-01-01", day.GetProperty("to").GetString());
        CollectionAssert.AreEqual(new[] { "publisher_end_convention_unclear" }, Strings(day.GetProperty("reasons")));
        Assert.AreEqual(2, day.GetProperty("named_state_sha256s").GetArrayLength());

        // A held state whose end is before its start covers no date: from it to the next state's start is a break, and the
        // overlap after it is no break (two held states with one wording cover it).
        var invalid = Value(await HistoryAsync("rgd-2007-06-07-n2", "art_1"));
        CollectionAssert.AreEqual(new[] { false, true, false },
            invalid.GetProperty("states").EnumerateArray().Select(static state => state.GetProperty("after_break").GetBoolean()).ToArray());
        var span = invalid.GetProperty("continuity_breaks").EnumerateArray().Single();
        Assert.AreEqual("2023-07-17", span.GetProperty("from").GetString());
        Assert.AreEqual("2024-01-01", span.GetProperty("to").GetString());
        CollectionAssert.AreEqual(new[] { "publisher_interval_invalid" }, Strings(span.GetProperty("reasons")));

        // A code's two held states, then four textless states to the end of the catalogue.
        var code = Value(await HistoryAsync("code-civil", "art_1"));
        var after = code.GetProperty("continuity_breaks").EnumerateArray().Single();
        Assert.AreEqual("2018-04-01", after.GetProperty("from").GetString());
        Assert.AreEqual(JsonValueKind.Null, after.GetProperty("to").ValueKind);
        CollectionAssert.AreEqual(new[] { "not_available_misfiled", "not_available_pdf", "not_acquired" }, Strings(after.GetProperty("reasons")));
        Assert.AreEqual(4, after.GetProperty("named_states").GetArrayLength());

        // A closed end is the end of the catalogue's states, not a break.
        var closed = Value(await HistoryAsync("loi-2005-01-01-n1", "art_1"));
        Assert.AreEqual(0, closed.GetProperty("continuity_breaks").GetArrayLength());
        Assert.IsTrue(closed.GetProperty("states").EnumerateArray().All(static state => !state.GetProperty("after_break").GetBoolean()));
    }

    [TestMethod]
    public async Task AComparisonOfAnActDatedByItsEntryIntoForceSaysThatAndNeverApplicableFrom()
    {
        // The fixture, with an act its file does not date (so dated by its entry into force) and a later consolidation held.
        var harness = IntervalFixture();
        const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";
        harness.State(W("loi/2008/08/08/n1/jo"), entryIntoForce: "2008-08-12", memberOf: W("loi/2008/08/08/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2008/08/08/n1/jo"), ["art_1"]);
        harness.State(W("loi/2008/08/08/n1/consolide/20150101"), "2015-01-01", memberOf: W("loi/2008/08/08/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2008/08/08/n1/consolide/20150101"), ["art_1"], identityClass: "same_work_act", textPrefix: "texte consolidé de ");
        await using var fixture = await IntervalMount.CreateAsync(harness);
        var mount = fixture.Mount;

        var version = Value(await AskAsync(mount, "diff", new { identifier = "/lu-legilux/loi-2008-08-08-n1", date_from = "2009-01-01", date_to = "2010-01-01" }))
            .GetProperty("comparisons").EnumerateArray().Single();
        Assert.IsTrue(version.GetProperty("same_state").GetBoolean());
        Assert.AreEqual("entry_into_force", version.GetProperty("from").GetProperty("date_role").GetString());
        Assert.AreEqual(V3CorpusMount.SameVersionNote("2008-08-12", datedByEntryIntoForce: true), version.GetProperty("note").GetString());
        StringAssert.Contains(version.GetProperty("note").GetString(), "dated by its entry into force on 2008-08-12 (publisher)");

        // An act held only as published, dated by its entry into force: the one text held, said with its date's role.
        var held = Value(await AskAsync(mount, "diff", new { identifier = "/lu-legilux/loi-1798-11-03-n1", date_from = "1900-01-01", date_to = "1950-01-01" }))
            .GetProperty("comparisons").EnumerateArray().Single();
        StringAssert.StartsWith(held.GetProperty("note").GetString(),
            "Both dates select the one text held, the act as published, dated by its entry into force on 1798-11-07 (publisher)");
        foreach (var note in new[] { version, held }.Select(static comparison => comparison.GetProperty("note").GetString()!))
        {
            Assert.IsFalse(note.Contains("applicable from 2008-08-12", StringComparison.Ordinal) || note.Contains("applicable from 1798-11-07", StringComparison.Ordinal), note);
        }

        // A state dated by applicability keeps the note it had.
        var applicable = Value(await AskAsync(mount, "diff", new { identifier = "/lu-legilux/loi-2008-08-08-n1", date_from = "2015-02-01", date_to = "2016-01-01" }))
            .GetProperty("comparisons").EnumerateArray().Single();
        Assert.AreEqual(V3CorpusMount.SameVersionNote("2015-01-01"), applicable.GetProperty("note").GetString());
    }

    [TestMethod]
    public async Task AnEventOfAStateNamingNoExpressionHasNoPermalinkAndATextlessStatesPermalinkRefusesForItsText()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        var events = Value(await AskAsync(mount, "events", new { limit = 200 }));
        Assert.AreEqual(V3CorpusMount.EventsPermalinkRuleNine, events.GetProperty("permalink_rule").GetString());
        var rows = events.GetProperty("events").EnumerateArray().ToArray();
        var noExpression = rows.Single(static row => row.GetProperty("stable_coordinate").GetString() == "/lu-legilux/code-civil/2027-01-01");
        Assert.AreEqual(JsonValueKind.Null, noExpression.GetProperty("permalink").ValueKind, "a state the catalogue names no expression of has no permalink");
        Assert.AreEqual("not_acquired", noExpression.GetProperty("detail").GetProperty("text_status").GetString());
        Assert.IsTrue(rows.Where(static row => row.GetProperty("language").GetString()!.Length > 0)
            .All(static row => row.GetProperty("permalink").ValueKind == JsonValueKind.String));

        // A withheld state's event: its permalink refuses for its text, naming it.
        var withheld = rows.Single(static row => row.GetProperty("stable_coordinate").GetString() == "/lu-legilux/loi-1798-11-03-n1/2020-01-01");
        var refused = await AskAsync(mount, "as_of", new { identifier = withheld.GetProperty("permalink").GetString(), date = "2020-01-01" });
        Assert.AreEqual("text_withheld", refused.Refusal!.Code);
        Assert.AreEqual(withheld.GetProperty("state_sha256").GetString(), refused.Refusal.HelpfulPayload.GetProperty("state_sha256").GetString());
    }

    /// <summary>
    /// The Release 2 rehearsal's J8 failure (derive-r2, the first mount serving codes): the log gives a code's withheld and
    /// misfiled states their permalinks, as its permalink_rule says, but verify and resolve answered those permalinks
    /// identifier_unknown, so a monitoring client read a state the log had just named as unknown. Every event's permalink now
    /// answers verify and resolve as the log says of its state: held text verifies digest_matches; a rights reason is refused
    /// text_withheld with the hashes (R64); any other textless state text_not_available; each naming that state, and as_of
    /// refusing it alike.
    /// </summary>
    [TestMethod]
    public async Task EveryEventsPermalinkAnswersVerifyAndResolveAsTheLogSaysOfItsStatesText()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var mount = fixture.Mount;

        var rows = Value(await AskAsync(mount, "events", new { limit = 200 })).GetProperty("events").EnumerateArray()
            .Where(static row => row.GetProperty("permalink").ValueKind == JsonValueKind.String)
            .ToArray();
        var statuses = rows.Select(static row => row.GetProperty("detail").GetProperty("text_status").GetString()!).ToArray();
        // The fixture's log holds held text, a rights withholding, a PDF and the code's misfiled state, each with its permalink.
        foreach (var status in new[] { "held", "withheld_rights", "not_available_pdf", "not_available_misfiled" })
        {
            CollectionAssert.Contains(statuses, status);
        }

        foreach (var row in rows)
        {
            var permalink = row.GetProperty("permalink").GetString()!;
            var state = row.GetProperty("state_sha256").GetString();
            var status = row.GetProperty("detail").GetProperty("text_status").GetString();
            var verified = await AskAsync(mount, "verify", new { identifier = permalink });
            var resolved = await AskAsync(mount, "resolve", new { identifier = permalink });
            if (status == "held")
            {
                var value = Value(verified);
                Assert.AreEqual("digest_matches", value.GetProperty("verdict").GetString(), permalink);
                Assert.AreEqual(state, value.GetProperty("state_sha256").GetString(), permalink);
                Assert.AreEqual(permalink, value.GetProperty("permalink").GetString(), permalink);
                Assert.AreEqual(state, Value(resolved).GetProperty("state_sha256").GetString(), permalink);
                continue;
            }

            var code = status == "withheld_rights" ? "text_withheld" : "text_not_available";
            foreach (var (operation, envelope) in new[] { ("verify", verified), ("resolve", resolved) })
            {
                Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, $"{operation} {permalink}");
                Assert.AreEqual(code, envelope.Refusal!.Code, $"{operation} {permalink}: {envelope.Refusal.HelpfulPayload.GetRawText()}");
                var payload = envelope.Refusal.HelpfulPayload;
                Assert.AreEqual(state, payload.GetProperty("state_sha256").GetString(), $"{operation} {permalink}");
                Assert.AreEqual(permalink, payload.GetProperty("permalink").GetString(), $"{operation} {permalink}");
                Assert.AreEqual(status, payload.GetProperty("text_status").GetString(), $"{operation} {permalink}");
                Assert.AreEqual(row.GetProperty("applicability_date").GetString(), payload.GetProperty("requested_date").GetString(), $"{operation} {permalink}");
                if (code == "text_withheld")
                {
                    Assert.AreEqual(state, payload.GetProperty("content_sha256").GetString(), $"{operation} {permalink}: the hashes (R64)");
                }
            }

            Assert.AreEqual(code, (await AskAsync(mount, "as_of", new { identifier = permalink, date = row.GetProperty("applicability_date").GetString() })).Refusal!.Code, permalink);
        }

        // An article permalink under a textless state is refused for its text too. A digest the catalogue never named, at that
        // textless coordinate, is never verified as the state there: it is refused for the text the coordinate does not hold,
        // naming the state there by its own digest beside the digest asked (R64; the review of #1008, which answered it
        // identifier_unknown before: TextlessDigestsAndWorksAreRefusedForTheirTextNeverUnknown below).
        var misfiled = rows.First(static row => row.GetProperty("detail").GetProperty("text_status").GetString() == "not_available_misfiled");
        Assert.AreEqual("text_not_available",
            (await AskAsync(mount, "verify", new { identifier = misfiled.GetProperty("permalink").GetString() + "/art_1" })).Refusal!.Code);
        var other = misfiled.GetProperty("permalink").GetString()!.Replace(misfiled.GetProperty("state_sha256").GetString()!, new string('0', 64), StringComparison.Ordinal);
        var refused = await AskAsync(mount, "verify", new { identifier = other });
        Assert.AreEqual("text_not_available", refused.Refusal!.Code);
        Assert.AreEqual(misfiled.GetProperty("state_sha256").GetString(), refused.Refusal.HelpfulPayload.GetProperty("state_sha256").GetString());
        Assert.AreEqual(new string('0', 64), refused.Refusal.HelpfulPayload.GetProperty("requested_digest").GetString(), "the digest asked is said, and not as the state's");
    }

    /// <summary>
    /// The review of #1008's two notes, answered as R64 wants (a later withheld text answers <c>text_withheld</c> with the hashes;
    /// a text not available is <c>text_not_available</c>), never <c>identifier_unknown</c> and never a
    /// <c>pinned_digest_mismatch</c> whose current link opens no text:
    /// <list type="bullet">
    /// <item>a digest the index no longer holds, at a stable coordinate whose states in the permalink's language hold no text, is
    /// refused for that text naming the state there (its digest, permalink and status, the digest asked and the rule), through
    /// verify, resolve, as_of and evidence_bundle alike; two such states there are an ambiguity naming both;</item>
    /// <item>a coordinate that holds text keeps its answer: a changed digest is <c>pinned_digest_mismatch</c> naming the current
    /// state, and a coordinate the index holds no state at stays unknown.</item>
    /// </list>
    /// Before this repair each of the textless cases answered <c>identifier_unknown</c>.
    /// </summary>
    [TestMethod]
    public async Task TextlessDigestsAndWorksAreRefusedForTheirTextNeverUnknown()
    {
        await using var fixture = await IntervalMount.CreateAsync(RepairFixture());
        var mount = fixture.Mount;
        var stale = new string('0', 64);

        // The code's misfiled state (2018-04-01) and an act's withheld consolidation (2020-01-01), each alone at its coordinate.
        foreach (var (work, date, code, status, resource) in new[]
                 {
                     ("code-civil", "2018-04-01", "text_not_available", "not_available_misfiled", "code/civil/20180401"),
                     ("loi-1798-11-03-n1", "2020-01-01", "text_withheld", "withheld_rights", "loi/1798/11/03/n1/consolide/20200101"),
                 })
        {
            var permalink = $"/lu-legilux/{work}/{date}--{stale}/fra";
            foreach (var (operation, parameters) in new (string, object)[]
                     {
                         ("verify", new { identifier = permalink }),
                         ("resolve", new { identifier = permalink }),
                         ("as_of", new { identifier = permalink, date }),
                         ("evidence_bundle", new { identifier = permalink, date }),
                     })
            {
                var envelope = await AskAsync(mount, operation, parameters);
                Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, $"{operation} {permalink}");
                Assert.AreEqual(code, envelope.Refusal!.Code, $"{operation} {permalink}: {envelope.Refusal.HelpfulPayload.GetRawText()}");
                var payload = envelope.Refusal.HelpfulPayload;
                Assert.AreEqual(W(resource), payload.GetProperty("official_identity").GetString(), operation);
                Assert.AreEqual(status, payload.GetProperty("text_status").GetString(), operation);
                var state = payload.GetProperty("state_sha256").GetString()!;
                Assert.AreNotEqual(stale, state, operation);
                Assert.AreEqual($"/lu-legilux/{work}/{date}--{state}/fra", payload.GetProperty("permalink").GetString(), $"{operation}: the state's own permalink");
                Assert.AreEqual(stale, payload.GetProperty("requested_digest").GetString(), operation);
                Assert.AreEqual(V3CorpusMount.TextlessCoordinateRule, payload.GetProperty("requested_digest_rule").GetString(), operation);
                Assert.AreEqual(date, payload.GetProperty("requested_date").GetString(), operation);
                if (code == "text_withheld")
                {
                    Assert.AreEqual(state, payload.GetProperty("content_sha256").GetString(), $"{operation}: the hashes (R64)");
                }

                // The state's own permalink is refused alike, without a digest asked beside it (#1008's answer, unchanged).
                var own = await AskAsync(mount, operation, operation is "verify" or "resolve"
                    ? new { identifier = payload.GetProperty("permalink").GetString() }
                    : (object)new { identifier = payload.GetProperty("permalink").GetString(), date });
                Assert.AreEqual(code, own.Refusal!.Code, $"{operation}, the state's own permalink");
                Assert.IsFalse(own.Refusal.HelpfulPayload.TryGetProperty("requested_digest", out _), $"{operation}, the state's own permalink");
            }
        }

        // Two textless states on one date (a withheld consolidation and a PDF, 2010-01-01): neither is the one a stale digest
        // named, so the answer names both, each by its permalink.
        var pair = $"/lu-legilux/loi-2001-01-01-n1/2010-01-01--{stale}/fra";
        foreach (var operation in new[] { "verify", "resolve" })
        {
            var envelope = await AskAsync(mount, operation, new { identifier = pair });
            Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code, $"{operation}: {envelope.Refusal.HelpfulPayload.GetRawText()}");
            var candidates = Strings(envelope.Refusal.HelpfulPayload.GetProperty("candidates"));
            Assert.HasCount(2, candidates, operation);
            foreach (var candidate in candidates)
            {
                StringAssert.StartsWith(candidate, "/lu-legilux/loi-2001-01-01-n1/2010-01-01--", operation);
                var named = await AskAsync(mount, "verify", new { identifier = candidate });
                Assert.IsTrue(named.Refusal!.Code is "text_withheld" or "text_not_available", $"{candidate}: {named.Refusal.Code}");
            }
        }

        // A coordinate that holds text keeps R64's mismatch naming the current state; one the index holds no state at stays unknown.
        var held = $"/lu-legilux/code-civil/2016-09-01--{stale}/fra";
        foreach (var operation in new[] { "verify", "resolve" })
        {
            var mismatch = await AskAsync(mount, operation, new { identifier = held });
            Assert.AreEqual("pinned_digest_mismatch", mismatch.Refusal!.Code, operation);
            StringAssert.StartsWith(mismatch.Refusal.HelpfulPayload.GetProperty("current_hash_pinned_url").GetString(), "/lu-legilux/code-civil/2016-09-01--", operation);
            Assert.AreEqual("identifier_unknown",
                (await AskAsync(mount, operation, new { identifier = $"/lu-legilux/code-civil/2016-09-02--{stale}/fra" })).Refusal!.Code, operation);
        }
    }

    /// <summary>
    /// A work whose every state in the index is textless (the review of #1008: the builder writes no such work today, since index
    /// 9 records the catalogue's states of works with a held state, so the index is written here with the work's one held row
    /// taken out): its textless state's permalink is refused for its text, naming it, through verify, resolve and as_of, as
    /// every other textless state's is; before this repair it was <c>identifier_unknown</c>.
    /// </summary>
    [TestMethod]
    public async Task AWorkWhoseEveryStateIsTextlessAnswersItsStatesPermalinkForItsText()
    {
        const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";
        var harness = IntervalFixture();
        harness.State(W("loi/2009/09/09/n1/jo"), entryIntoForce: "2009-09-09", memberOf: W("loi/2009/09/09/n1"), expressions: ("fr", Fra))
            .Held(W("loi/2009/09/09/n1/jo"), ["art_1"], inFileDate: "2009-09-09");
        harness.State(W("loi/2009/09/09/n1/consolide/20150101"), "2015-01-01", memberOf: W("loi/2009/09/09/n1"), expressions: ("fr", Fra))
            .Withheld(W("loi/2009/09/09/n1/consolide/20150101"), inFileDate: "2015-01-01");
        await using var fixture = await IntervalMount.CreateAsync(harness, withoutHeldStatesOf: "loi-2009-09-09-n1");
        var mount = fixture.Mount;

        var (states, _) = harness.Project();
        var withheld = states.Single(static row => row.PublisherLegalResourceIri == W("loi/2009/09/09/n1/consolide/20150101"));
        var permalink = $"/lu-legilux/loi-2009-09-09-n1/2015-01-01--{withheld.StateSha256}/fra";
        foreach (var (operation, parameters) in new (string, object)[]
                 {
                     ("verify", new { identifier = permalink }),
                     ("resolve", new { identifier = permalink }),
                     ("as_of", new { identifier = permalink, date = "2015-01-01" }),
                 })
        {
            var envelope = await AskAsync(mount, operation, parameters);
            Assert.AreEqual("text_withheld", envelope.Refusal!.Code, $"{operation}: {envelope.Refusal.HelpfulPayload.GetRawText()}");
            var payload = envelope.Refusal.HelpfulPayload;
            Assert.AreEqual(withheld.StateSha256, payload.GetProperty("state_sha256").GetString(), operation);
            Assert.AreEqual(withheld.StateSha256, payload.GetProperty("content_sha256").GetString(), $"{operation}: the hashes (R64)");
            Assert.AreEqual(permalink, payload.GetProperty("permalink").GetString(), operation);
            Assert.AreEqual("withheld_rights", payload.GetProperty("text_status").GetString(), operation);
            Assert.IsFalse(payload.TryGetProperty("requested_digest", out _), $"{operation}: the permalink names this state");
        }
    }

    [TestMethod]
    public async Task CoveragesBuildTimeRowSaysABuildsTimeIsItsAcquisitionsAndTheDerivationsIsTheLogs()
    {
        await using var fixture = await IntervalMount.CreateAsync();
        var coverage = Value(await AskAsync(fixture.Mount, "coverage", new { }));
        var row = coverage.GetProperty("not_held").EnumerateArray().Single(static entry => entry.GetProperty("item").GetString() == "build_time_and_currency");
        Assert.AreEqual(V3CorpusMount.CoverageBuildTimeRowNine[1], row.GetProperty("reason").GetString());
        StringAssert.Contains(row.GetProperty("reason").GetString(), "log.derived_at");
        Assert.IsFalse(row.GetProperty("reason").GetString()!.Contains("when each build ran", StringComparison.Ordinal));
    }
}
