using System.Globalization;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The launch contract's machine gates, run against the real handler on a mounted corpus: the temporal
/// case set at 100 percent (a dated request never receives a different date silently: <c>as_of</c> and
/// <c>in_force_on</c> select the one state that applies, or refuse <c>no_version_for_date</c> or
/// <c>ambiguous_version</c> rather than choose), the refusal case set at 100 percent (every absence is typed:
/// each request that must be refused is refused with its registry code) and the retrieval case set (anchor
/// nDCG@10, no-hit accuracy and resolver exactness), each with the shuffled control that proves the harness
/// would notice if it were wrong: the three controls the contract asks to see fail.
/// </summary>
/// <remarks>
/// <para>
/// The harness is the one in <c>Lex.V3.Contracts.Evaluation</c> (<see cref="TemporalEvaluation"/>,
/// <see cref="VerdictEvaluation"/>, <see cref="RetrievalEvaluation"/>, <see cref="ShuffledControls"/>), which until
/// now nothing outside its own tests called. The arms are the served operations, asked through
/// <see cref="V3ApiHandler"/> as a client asks them; a refusal is the key <c>refusal:{code}</c>, a selected state its
/// digest, a search hit its work and publisher article id. The cases are the fixture's, so the gates
/// prove the path, not a corpus: the first real mount is still blocked.
/// </para>
/// <para>
/// The temporal set runs through four arms: <c>as_of</c> and <c>in_force_on</c>, each asked with the work's language
/// and with none, since the contract names both operations and each has its own selection. The refusal set covers
/// every code the refusal census records as produced, across three mounts (the rights and empty-text cases need
/// their own). The retrieval set's judgments are written from the corpus the test builds, so its nDCG threshold is
/// 1.0: the path's exactness, not a quality claim.
/// </para>
/// <para>
/// The date control shifts every case forward by an interval the corpus holds and requires every expectation to
/// break. A case at or after the latest held state can never break under a forward shift (the same state still
/// applies), which is the control's own design, so it runs over the cases before that state; the gate itself runs
/// over all of them.
/// </para>
/// </remarks>
[TestClass]
public sealed class V3MachineGatesTests
{
    private const int Spacing = 400;
    private const ulong Seed = 20260930;

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task TheTemporalCaseSetPassesAtOneHundredPercentAndTheDateShiftControlCatchesAShiftedSet()
    {
        // One work in three held dates: the fixture's own state, two states on one later date (ambiguous), and one
        // latest state held singly, so every region a date can fall in is represented.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var first = fixture.ApplicabilityDate;
        var twinDate = Shift(first, Spacing);
        var latestDate = Shift(first, 2 * Spacing);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        var latest = await fixture.AddStateAsync(latestDate, "latest");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        const string NoVersion = "refusal:no_version_for_date";
        const string Ambiguous = "refusal:ambiguous_version";
        var cases = new[]
        {
            new TemporalCase("before-history", fixture.WorkKey, Day(Shift(first, -1)), NoVersion),
            new TemporalCase("first-day", fixture.WorkKey, Day(first), fixture.StateSha256),
            new TemporalCase("inside-first", fixture.WorkKey, Day(Shift(first, 100)), fixture.StateSha256),
            new TemporalCase("last-day-of-first", fixture.WorkKey, Day(Shift(twinDate, -1)), fixture.StateSha256),
            new TemporalCase("twin-day", fixture.WorkKey, Day(twinDate), Ambiguous),
            new TemporalCase("inside-twins", fixture.WorkKey, Day(Shift(twinDate, 100)), Ambiguous),
            new TemporalCase("latest-day", fixture.WorkKey, Day(latestDate), latest.StateSha256),
            new TemporalCase("after-latest", fixture.WorkKey, Day(Shift(latestDate, 1000)), latest.StateSha256),
        };

        // The contract names both dated operations, and each has its own selection: as_of and in_force_on, each
        // asked with the work's language and with none (the default request, which serves every language held).
        var arms = new (string Name, TemporalArm Arm)[]
        {
            ("as_of in fra", AsOfArm(mount, "fra")),
            ("as_of with no language", AsOfArm(mount, null)),
            ("in_force_on in fra", InForceOnArm(mount, "fra")),
            ("in_force_on with no language", InForceOnArm(mount, null)),
        };
        var beforeLatest = cases.Where(value => value.AsOf < Day(latestDate)).ToArray();
        foreach (var (name, arm) in arms)
        {
            var report = TemporalEvaluation.Evaluate(cases, arm, floor: cases.Length);
            Assert.AreEqual(1.0, report.Exactness.Value, $"{name}: every dated request selects exactly its state or refuses as it must.");
            Assert.AreEqual(GateVerdict.Pass, report.Gate.Verdict, name);
            Assert.AreEqual(EvaluationGateNames.TemporalExactness, report.Gate.Name);

            var control = ShuffledControls.DateShuffle(
                beforeLatest, arm, (set, run) => TemporalEvaluation.Evaluate(set, run, floor: set.Count), [Spacing], Seed);
            Assert.AreEqual(ControlVerdict.CaughtTheShuffle, control.Verdict, $"{name}: {control.Reason}");
        }
    }

    [TestMethod]
    public async Task TheRefusalCaseSetPassesAtOneHundredPercentAndTheVerdictShuffleControlCatchesAShuffledSet()
    {
        // The main mount: two titled works, a later state with another rule profile, and two states on one date.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddTwoWorkTitlesAsync();
        var laterDate = Shift(fixture.ApplicabilityDate, Spacing);
        var later = await fixture.AddStateAsync(laterDate, "later");
        await fixture.AddRuleProfileToStateAsync(later.ExpressionIri, new string('a', 64));
        var twinDate = Shift(fixture.ApplicabilityDate, 2 * Spacing);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // A mount whose member's rights did not agree, and one whose articles hold no text.
        var withheld = await MountedFixture.CreateAsync();
        await using var cleanupWithheld = withheld;
        await withheld.SetMemberRightsDispositionAsync("non_admitting_licence_scl");
        using var withheldMount = await V3CorpusMount.OpenAsync(withheld.Directory, CancellationToken.None);
        Assert.IsNotNull(withheldMount);
        var empty = await MountedFixture.CreateAsync();
        await using var cleanupEmpty = empty;
        foreach (var (_, publisherId, _) in empty.ArticlesOfOwnState())
        {
            await empty.SetArticleTokensAsync(empty.ExpressionIri, publisherId, string.Empty, "[]");
        }

        using var emptyMount = await V3CorpusMount.OpenAsync(empty.Directory, CancellationToken.None);
        Assert.IsNotNull(emptyMount);

        var work = $"/lu-legilux/{fixture.WorkKey}";
        var date = fixture.ApplicabilityDate;
        var permalink = $"/lu-legilux/{fixture.WorkKey}/{date}--{fixture.StateSha256}";
        var wrongDigest = $"/lu-legilux/{fixture.WorkKey}/{date}--{new string('0', 64)}";

        // Each case is a request a client can send and the one code the registry says answers it, covering every
        // code the platform produces (the refusal census's `produced` list); "answer" where the request must be
        // answered, so the set also holds that a refusal is not the default.
        var requests = new Dictionary<string, (V3CorpusMount Mount, string Operation, object Parameters, string Gold)>(StringComparer.Ordinal)
        {
            ["unknown-work"] = (mount, "as_of", new { identifier = "/lu-legilux/no-such-work", date }, "identifier_unknown"),
            ["title-two-works-carry"] = (mount, "resolve", new { identifier = "Reglement sur l'epreuve" }, "ambiguous_identifier"),
            ["language-not-held"] = (mount, "dossier", new { identifier = work, language = "eng" }, "language_not_available"),
            ["as-of-language-not-held"] = (mount, "as_of", new { identifier = work, date, language = "eng" }, "language_not_available"),
            ["before-history"] = (mount, "as_of", new { identifier = work, date = Shift(date, -1), language = "fra" }, "no_version_for_date"),
            ["twin-states"] = (mount, "as_of", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version"),
            ["anchor-not-held"] = (mount, "article_history", new { identifier = work, anchor = "art_no_such_anchor", language = "fra" }, "anchor_not_in_version"),
            ["pinned-digest-not-held"] = (mount, "verify", new { identifier = wrongDigest, language = "fra" }, "pinned_digest_mismatch"),
            ["profiles-differ"] = (mount, "diff", new { identifier = work, date_from = date, date_to = laterDate, language = "fra" }, "profiles_differ"),
            ["mode-not-held"] = (mount, "search", new { query = "loyer", language = "fra", mode = "bm25" }, "retrieval_mode_unavailable"),
            ["foreign-cursor"] = (mount, "events", new { after = new string('0', 64) + ":1" }, "snapshot_unknown"),
            ["format-not-held"] = (mount, "manifestation", new { identifier = work, format = "docx" }, "format_not_available"),
            ["eu-work-no-eu-index"] = (mount, "search", new { query = "x", language = "eng", identifier = "32016R0679" }, "no_corpus_mounted"),
            ["rights-not-agreed"] = (withheldMount, "evidence_bundle", new { identifier = $"/lu-legilux/{withheld.WorkKey}", date = withheld.ApplicabilityDate, language = "fra" }, "text_withheld"),
            ["no-text-held"] = (emptyMount, "evidence_bundle", new { identifier = $"/lu-legilux/{empty.WorkKey}", date = empty.ApplicabilityDate, language = "fra" }, "text_not_available"),
            ["a-held-state"] = (mount, "as_of", new { identifier = work, date, language = "fra" }, "answer"),
            ["the-held-permalink"] = (mount, "verify", new { identifier = permalink, language = "fra" }, "answer"),
            ["coverage"] = (mount, "coverage", new { }, "answer"),
        };
        var cases = requests.Select(static pair => new VerdictCase(pair.Key, pair.Value.Gold)).ToArray();
        VerdictArm arm = caseId =>
        {
            var (target, operation, parameters, _) = requests[caseId];
            var envelope = EnvelopeAsync(target, "/api/v3/" + operation, operation, parameters).GetAwaiter().GetResult();
            return envelope.Refusal?.Code ?? "answer";
        };

        var report = VerdictEvaluation.Evaluate(cases, arm, floor: cases.Length);
        Assert.AreEqual(1.0, report.ExactMatch.Value, "every request is refused with its own code, or answered where it must be.");
        Assert.AreEqual(GateVerdict.Pass, report.Gate.Verdict);
        var golds = cases.Select(static value => value.GoldVerdict).Where(static gold => gold != "answer").Distinct().Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(ProducedCodes().Order(StringComparer.Ordinal).ToArray(), golds,
            "the set covers exactly the codes the platform produces, as the refusal census records them.");

        var control = ShuffledControls.VerdictShuffle(cases, arm, (set, run) => VerdictEvaluation.Evaluate(set, run, floor: set.Count), Seed);
        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, control.Verdict, control.Reason);
    }

    [TestMethod]
    public async Task TheRetrievalCaseSetPassesEveryGateAndTheJudgmentsShuffleControlCatchesAShuffledSet()
    {
        // Two works of one date, every article text written by the test: a few words each held by chosen articles,
        // and text that matches none of them everywhere else, so the judgments below are the corpus's own truth.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var second = await fixture.AddStateAsync(fixture.ApplicabilityDate, "w2", workLeaf: "n4");
        var workA = fixture.WorkKey;
        var workB = second.WorkKey;
        await WriteTextsAsync(fixture, fixture.ExpressionIri, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["art_2"] = "Le quorum est atteint lorsque la moitie des membres est presente.",
            ["art_3"] = "Le membre suppléant remplace le titulaire empeche.",
            ["art_4"] = "Le délai de recours est d'un mois.",
            ["art_5"] = "Le préavis est de trois mois.",
            ["art_8"] = "Le mandat est renouvelable une fois.",
        });
        await WriteTextsAsync(fixture, second.ExpressionIri, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["art_4"] = "Le recours est introduit dans un délai raisonnable, de bonne foi.",
            ["art_5"] = "Aucun préavis n'est requis.",
            ["art_8"] = "Le mandat prend fin a l'assemblee suivante.",
            ["art_7"] = "La cotisation annuelle est fixee par le conseil.",
        });
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var permalinkA = await PermalinkAsync(mount, workA, fixture.ApplicabilityDate);
        var permalinkB = await PermalinkAsync(mount, workB, fixture.ApplicabilityDate);

        // Search cases are asked through `search` (a strict hit, the phrase, above a relaxed one, every term). A scoped
        // search must find the scoped work's article and not the other's, so scope that is ignored and scope that
        // finds nothing both fail. The exact-identifier cases are article permalinks (`permalink#anchor`), the form
        // only `verify` accepts: it answers a held anchor with its work and must refuse an anchor the state does not
        // hold, which the near-miss case (no hit) holds it to. Grades: 3 supports, 1 is context (the relaxed hit that
        // holds the terms but not the phrase).
        const string Collection = "lu-fixture";
        var requests = new Dictionary<string, (EvaluationCaseKind Kind, string Operation, object Parameters, JudgedAnchor[] Judged)>(StringComparer.Ordinal)
        {
            ["one-article"] = (EvaluationCaseKind.Retrieval, "search", new { query = "quorum", language = "fra" },
                [Supporting(workA, "art_2")]),
            ["accented-word"] = (EvaluationCaseKind.Retrieval, "search", new { query = "suppléant", language = "fra" },
                [Supporting(workA, "art_3")]),
            ["strict-above-relaxed"] = (EvaluationCaseKind.Retrieval, "search", new { query = "délai de recours", language = "fra" },
                [Supporting(workA, "art_4"), new JudgedAnchor(workB, "art_4", JudgedAnchor.ContextGrade)]),
            ["across-works"] = (EvaluationCaseKind.Retrieval, "search", new { query = "préavis", language = "fra" },
                [Supporting(workA, "art_5"), Supporting(workB, "art_5")]),
            ["second-work"] = (EvaluationCaseKind.Retrieval, "search", new { query = "cotisation", language = "fra" },
                [Supporting(workB, "art_7")]),
            ["no-hit-word"] = (EvaluationCaseKind.Retrieval, "search", new { query = "zéphyr", language = "fra" }, []),
            ["no-hit-scoped-to-b"] = (EvaluationCaseKind.Retrieval, "search", new { query = "quorum", language = "fra", identifier = $"/lu-legilux/{workB}" }, []),
            ["no-hit-scoped-to-a"] = (EvaluationCaseKind.Retrieval, "search", new { query = "cotisation", language = "fra", identifier = $"/lu-legilux/{workA}" }, []),
            ["scoped-to-b"] = (EvaluationCaseKind.Retrieval, "search", new { query = "mandat", language = "fra", identifier = $"/lu-legilux/{workB}" },
                [Supporting(workB, "art_8")]),
            ["near-miss-anchor-not-held"] = (EvaluationCaseKind.Retrieval, "verify", new { identifier = permalinkA + "#art_44", language = "fra" }, []),
            ["exact-first-article"] = (EvaluationCaseKind.ExactIdentifier, "verify", new { identifier = permalinkA + "#art_1er", language = "fra" },
                [Supporting(workA, "art_1er")]),
            ["exact-numbered-sub-article"] = (EvaluationCaseKind.ExactIdentifier, "verify", new { identifier = permalinkA + "#art_24-1", language = "fra" },
                [Supporting(workA, "art_24-1")]),
            ["exact-in-second-work"] = (EvaluationCaseKind.ExactIdentifier, "verify", new { identifier = permalinkB + "#art_10", language = "fra" },
                [Supporting(workB, "art_10")]),
        };
        var held = fixture.ArticlesOfOwnState().Select(static article => article.PublisherId).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "art_1er", "art_24-1", "art_10" }, held, "every anchor an exact case names is held.");
        CollectionAssert.DoesNotContain(held, "art_44", "the near-miss anchor is one the fixture's reviewed profile does not admit.");
        var cases = requests
            .Select(static pair => new EvaluationCase(pair.Key, Collection, pair.Value.Kind, new QueryJudgments(pair.Key, pair.Value.Judged)))
            .ToArray();
        RetrievalArm arm = caseId =>
        {
            var (_, operation, parameters, _) = requests[caseId];
            var envelope = EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters).GetAwaiter().GetResult();
            if (envelope.Refusal is { } refusal)
            {
                // Search serves zero hits as an answer, so a refused search is a defect, never "found nothing";
                // verify's only "no hit" is the anchor the state does not hold.
                Assert.AreEqual("verify", operation, $"{caseId}: search refused {refusal.Code}.");
                Assert.AreEqual("anchor_not_in_version", refusal.Code, caseId);
                return [];
            }

            var value = envelope.Result!.Value;
            if (operation == "verify")
            {
                return [new RankedAnchor(value.GetProperty("work_key").GetString()!, value.GetProperty("requested_anchor").GetString()!)];
            }

            // A hit is one article in one held state; the ranking is of provisions, first appearance kept.
            return value.GetProperty("hits").EnumerateArray()
                .Select(static hit => new RankedAnchor(hit.GetProperty("work_key").GetString()!, hit.GetProperty("publisher_id").GetString()!))
                .Distinct()
                .ToArray();
        };

        // The judgments are written from the corpus this test builds, so the served order must meet them exactly:
        // 1.0 here is the path's exactness, not a quality threshold. The labelled row stays "not yet labelled".
        const int Floor = 3;
        var report = RetrievalEvaluation.Evaluate(cases, arm, Floor, ndcgThreshold: 1.0);
        var rankings = string.Join(" | ", cases.Select(value =>
            $"{value.CaseId}: {string.Join(", ", arm(value.CaseId).Select(static item => $"{item.WorkKey}/{item.AnchorId}"))}"));
        Assert.AreEqual(1.0, report.AnchorNdcgAt10.Value, $"every search ranks its judged provisions as judged. {rankings}");
        Assert.AreEqual(1.0, report.NoHitAccuracy.Value, "every search that must find nothing finds nothing.");
        Assert.AreEqual(1.0, report.ResolverExactness.Value, "every article anchor resolves to exactly its provision.");
        Assert.IsTrue(report.Releases, string.Join("; ", report.Gates.Select(static gate => $"{gate.Name} {gate.Verdict}")));

        var control = ShuffledControls.QrelsShuffle(
            cases, arm, (set, run) => RetrievalEvaluation.Evaluate(set, run, Floor, ndcgThreshold: 1.0), Seed);
        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, control.Verdict, control.Reason);
    }

    private static JudgedAnchor Supporting(string workKey, string anchorId) =>
        new(workKey, anchorId, JudgedAnchor.SupportingGrade);

    /// <summary>
    /// Writes the text of every article of one expression: the texts given by publisher id, and text that matches
    /// none of this set's queries everywhere else. Every publisher id given must be held.
    /// </summary>
    private static async Task WriteTextsAsync(MountedFixture fixture, string expressionIri, IReadOnlyDictionary<string, string> texts)
    {
        var held = fixture.ArticlesOfOwnState().Select(static article => article.PublisherId).ToArray();
        CollectionAssert.IsSubsetOf(texts.Keys.ToArray(), held, $"every article the set writes is held by the fixture's state ({string.Join(", ", held)}).");
        foreach (var publisherId in held)
        {
            await fixture.RewriteArticleTextAsync(
                expressionIri, publisherId, texts.TryGetValue(publisherId, out var text) ? text : "Le present article ne dit rien.");
        }
    }

    /// <summary>The permalink of a work's one state on a date, as <c>as_of</c> serves it.</summary>
    private static async Task<string> PermalinkAsync(V3CorpusMount mount, string workKey, string date)
    {
        var envelope = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = $"/lu-legilux/{workKey}", date, language = "fra" });
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        var states = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        Assert.HasCount(1, states);
        return $"/lu-legilux/{workKey}/{date}--{states[0].GetProperty("state_sha256").GetString()}";
    }

    /// <summary>The codes the refusal census records as produced by the served operations.</summary>
    private static string[] ProducedCodes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        using var census = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            directory!.FullName, "schemas", "v3-platform", "refusal-payload-samples.json")));
        return census.RootElement.GetProperty("produced").EnumerateArray()
            .Select(static row => row.GetProperty("code").GetString()!)
            .ToArray();
    }

    private static object DatedRequest(string workKey, DateOnly asOf, string? language)
    {
        var date = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return language is null
            ? new { identifier = $"/lu-legilux/{workKey}", date }
            : new { identifier = $"/lu-legilux/{workKey}", date, language };
    }

    /// <summary>as_of's selection: the one state it serves, its digest; a refusal, its code; anything else, null.</summary>
    private static TemporalArm AsOfArm(V3CorpusMount mount, string? language) => (workKey, asOf) =>
    {
        var envelope = EnvelopeAsync(mount, "/api/v3/as_of", "as_of", DatedRequest(workKey, asOf, language)).GetAwaiter().GetResult();
        if (envelope.Refusal is { } refusal)
        {
            return "refusal:" + refusal.Code;
        }

        var states = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        return states.Length == 1 ? states[0].GetProperty("state_sha256").GetString() : null;
    };

    /// <summary>in_force_on's selection for one named work: its one row's state, its digest; a refusal, its code; anything else, null.</summary>
    private static TemporalArm InForceOnArm(V3CorpusMount mount, string? language) => (workKey, asOf) =>
    {
        var envelope = EnvelopeAsync(mount, "/api/v3/in_force_on", "in_force_on", DatedRequest(workKey, asOf, language)).GetAwaiter().GetResult();
        if (envelope.Refusal is { } refusal)
        {
            return "refusal:" + refusal.Code;
        }

        var rows = envelope.Result!.Value.GetProperty("states").EnumerateArray().ToArray();
        return rows.Length == 1 && rows[0].GetProperty("state").ValueKind == JsonValueKind.Object
            ? rows[0].GetProperty("state").GetProperty("state_sha256").GetString()
            : null;
    };
}
