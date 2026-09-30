using System.Globalization;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Two of the launch contract's machine gates, run against the real handler on a mounted corpus: the temporal
/// case set at 100 percent (a dated request never receives a different date silently: <c>as_of</c> and
/// <c>in_force_on</c> select the one state that applies, or refuse <c>no_version_for_date</c> or
/// <c>ambiguous_version</c> rather than choose) and the refusal case set at 100 percent (every absence is typed: each request that must be refused is refused with its
/// registry code), each with the shuffled control that proves the harness would notice if it were wrong.
/// </summary>
/// <remarks>
/// <para>
/// The harness is the one in <c>Lex.V3.Contracts.Evaluation</c> (<see cref="TemporalEvaluation"/>,
/// <see cref="VerdictEvaluation"/>, <see cref="ShuffledControls"/>), which until now nothing outside its own tests
/// called. The arms are the served operations, asked through <see cref="V3ApiHandler"/> as a client asks them; a
/// refusal is the key <c>refusal:{code}</c>, a selected state its digest. The cases are the fixture's, so the gates
/// prove the path, not a corpus: the first real mount is still blocked.
/// </para>
/// <para>
/// The temporal set runs through four arms: <c>as_of</c> and <c>in_force_on</c>, each asked with the work's language
/// and with none, since the contract names both operations and each has its own selection. The refusal set covers
/// every code the refusal census records as produced, across three mounts (the rights and empty-text cases need
/// their own).
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
