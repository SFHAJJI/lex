using System.Globalization;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The retrieval set over a mount, judged by the search's own stated matching (<c>SearchMatching</c>: a byte-exact
/// substring of an article's searchable text, nothing folded) computed from the mount's index: a word held by a few
/// articles is judged to find exactly those (work and publisher article id), which all must rank within the first ten;
/// a string held nowhere must find nothing, as must a word scoped to a work that does not hold it; and an article
/// permalink must be accepted under its own work and anchor, and one naming an anchor its state lacks refused. The
/// judgments are the corpus's own truth about its text, so 1.0 is the path's exactness, not a quality claim.
/// An EU index gives its own cases the same way (<c>EuropeSearchMatching</c> is the same byte-exact substring): a word
/// held by a few provisions of one work in one language, searched with that work as its scope (EU search is served in
/// one work), and strings that work holds nowhere. And, since EU <c>verify</c> is served over the EU permalink grammar (#850),
/// exact cases: three provisions of the work's one wording, each pinned by the digest recomputed by the stated rule, judged
/// to resolve to exactly that provision, and a provision the wording does not hold, judged to find nothing.
/// </summary>
public sealed partial class V3MountedGatesTests
{
    private const string RetrievalCollection = "mount";
    private const int EuropeWorkSample = 2;
    private const int RetrievalFloor = 3;
    private const int MaxWordCases = 6;
    private const int MaxJudgedPerWord = 5;
    private static readonly string[] NoHitStrings = ["zqxjvkwp", "qqzzkxjw", "wxqzjvkk"];

    /// <summary>A request and what the mount's text says it must rank.</summary>
    internal sealed record RetrievalRequest(EvaluationCaseKind Kind, string Operation, object Parameters, JudgedAnchor[] Judged);

    internal static IReadOnlyDictionary<string, RetrievalRequest> RetrievalCases(string mountDirectory, IReadOnlyList<Timeline> timelines)
    {
        var requests = new SortedDictionary<string, RetrievalRequest>(StringComparer.Ordinal);
        EuropeCases(mountDirectory, requests);
        var held = timelines
            .SelectMany(static timeline => timeline.Dates.Where(static date => date.States.Count == 1).Select(date => (Timeline: timeline, date.Date, State: date.States[0])))
            .ToArray();
        if (held.Length == 0)
        {
            return requests;
        }

        using var connection = LuxembourgIndexBuilder.Open(Path.Combine(mountDirectory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        var language = held[0].Timeline.Language;

        // Words of the sampled states' articles, each judged to find every (work, article) of the language whose
        // text holds it, when that is between one and five.
        var words = Rows(connection,
                "SELECT a.searchable_text FROM articles a JOIN states s ON s.expression_iri = a.expression_iri AND s.language = a.language " +
                $"WHERE a.language = '{language}' AND s.work_key IN ({string.Join(",", held.Select(static value => $"'{value.Timeline.WorkKey.Replace("'", "''", StringComparison.Ordinal)}'").Distinct())}) ORDER BY a.article_identity_sha256")
            .SelectMany(static row => row[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(static word => word.Length >= 6 && word.All(char.IsLetter))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var random = new SplitMix64(Seed);
        for (var at = words.Length - 1; at > 0; at--)
        {
            var other = random.NextBelow(at + 1);
            (words[at], words[other]) = (words[other], words[at]);
        }

        var workOfWord = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            if (requests.Keys.Count(static key => key.StartsWith("word-", StringComparison.Ordinal)) >= MaxWordCases)
            {
                break;
            }

            // Between one and five articles, and hits (one per article per state) within the answer's one page, so
            // every judged article is on it.
            var found = Holding(connection, language, word, workKey: null, out var hits);
            if (found.Count is < 1 or > MaxJudgedPerWord || hits > V3CorpusMount.SearchMaxHits)
            {
                continue;
            }

            requests[$"word-{word}"] = new(EvaluationCaseKind.Retrieval, "search", new { query = word, language },
                found.Select(static pair => new JudgedAnchor(pair.Work, pair.Anchor, JudgedAnchor.SupportingGrade)).ToArray());
            workOfWord[word] = found[0].Work;
        }

        // A word scoped to its own work finds that work's articles; scoped to another work held in the language that lacks
        // it, nothing. A work not held in the language is refused language_not_available, rightly, so it is never the
        // other work (review of #842).
        var otherWork = held.Where(value => value.Timeline.Language == language).Select(static value => value.Timeline.WorkKey).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var (word, work) in workOfWord.Take(2))
        {
            var ofWork = Holding(connection, language, word, work, out _);
            requests[$"scoped-{word}"] = new(EvaluationCaseKind.Retrieval, "search", new { query = word, language, identifier = $"/lu-legilux/{work}" },
                ofWork.Select(static pair => new JudgedAnchor(pair.Work, pair.Anchor, JudgedAnchor.SupportingGrade)).ToArray());
            var elsewhere = otherWork.FirstOrDefault(candidate => candidate != work && Holding(connection, language, word, candidate, out _).Count == 0);
            if (elsewhere is not null)
            {
                requests[$"scoped-elsewhere-{word}"] = new(EvaluationCaseKind.Retrieval, "search", new { query = word, language, identifier = $"/lu-legilux/{elsewhere}" }, []);
            }
        }

        foreach (var nothing in NoHitStrings.Where(value => Holding(connection, language, value, workKey: null, out _).Count == 0))
        {
            requests[$"no-hit-{nothing}"] = new(EvaluationCaseKind.Retrieval, "search", new { query = nothing, language }, []);
        }

        // Article permalinks of held states, three anchors each (so one state reaches the floor): each accepted under its own work and anchor; and an
        // anchor the state lacks, refused.
        foreach (var (timeline, date, state) in held.Take(3))
        {
            var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var permalink = $"/lu-legilux/{timeline.WorkKey}/{day}--{state}";
            var anchors = Rows(connection, "SELECT a.publisher_id FROM articles a JOIN states s ON s.expression_iri = a.expression_iri " +
                    $"WHERE s.state_sha256 = '{state}' AND s.language = '{timeline.Language}' ORDER BY a.publisher_id LIMIT 3")
                .Select(static row => row[0]).ToArray();
            foreach (var anchor in anchors)
            {
                // Keyed by the state too: two states of one work keep their own cases (review of #842).
                requests[$"exact-{timeline.WorkKey}-{timeline.Language}-{day}-{anchor}"] = new(EvaluationCaseKind.ExactIdentifier, "verify", new { identifier = $"{permalink}#{anchor}", language = timeline.Language },
                    [new JudgedAnchor(timeline.WorkKey, anchor, JudgedAnchor.SupportingGrade)]);
            }

            if (anchors.Length > 0)
            {
                requests[$"near-miss-{timeline.WorkKey}-{timeline.Language}-{day}"] = new(EvaluationCaseKind.Retrieval, "verify", new { identifier = $"{permalink}#art_no_such_anchor", language = timeline.Language }, []);
            }
        }

        return requests;
    }

    /// <summary>
    /// The EU cases of a mount's EU index, for a seeded sample of its works in each language among the wordings EU search
    /// answers by CELEX (<see cref="EuropeSearchedArticles"/>): words held by one to five
    /// provisions, each judged to find exactly those (the work's CELEX and the publisher's provision id, as a hit names
    /// them), and strings the work holds nowhere. When the work's expression in the language holds one wording date, its
    /// first three provisions' permalinks (the digest recomputed by the stated rule) are exact cases, each judged to
    /// resolve to that provision, and a provision the wording does not hold is a near miss, judged to find nothing.
    /// </summary>
    private static void EuropeCases(string mountDirectory, IDictionary<string, RetrievalRequest> requests)
    {
        var path = Path.Combine(mountDirectory, V3CorpusMount.EuropeIndexFileName);
        if (!File.Exists(path))
        {
            return;
        }

        using var connection = EuropeIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
        var works = SampleEuropeWorks(
            Rows(connection, $"SELECT DISTINCT publisher_work_celex, language FROM articles WHERE {EuropeSearchedArticles(connection)} ORDER BY publisher_work_celex, language")
                .Select(static row => (row[0], row[1])).ToArray(),
            EuropeWorkSample, Seed);
        var random = new SplitMix64(Seed);
        foreach (var (celex, language) in works)
        {
            var words = Rows(connection, $"SELECT searchable_text FROM articles WHERE publisher_work_celex = '{celex.Replace("'", "''", StringComparison.Ordinal)}' AND language = '{language}' ORDER BY article_identity_sha256")
                .SelectMany(static row => row[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(static word => word.Length >= 6 && word.All(char.IsLetter))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            for (var at = words.Length - 1; at > 0; at--)
            {
                var other = random.NextBelow(at + 1);
                (words[at], words[other]) = (words[other], words[at]);
            }

            var taken = 0;
            foreach (var word in words)
            {
                if (taken >= MaxWordCases)
                {
                    break;
                }

                var found = EuropeHolding(connection, celex, language, word);
                if (found.Count is < 1 or > MaxJudgedPerWord)
                {
                    continue;
                }

                requests[$"eu-word-{celex}-{language}-{word}"] = new(EvaluationCaseKind.Retrieval, "search", new { query = word, language, identifier = celex },
                    found.Select(provision => new JudgedAnchor(celex, provision, JudgedAnchor.SupportingGrade)).ToArray());
                taken++;
            }

            foreach (var nothing in NoHitStrings.Where(value => EuropeHolding(connection, celex, language, value).Count == 0))
            {
                requests[$"eu-no-hit-{celex}-{language}-{nothing}"] = new(EvaluationCaseKind.Retrieval, "search", new { query = nothing, language, identifier = celex }, []);
            }

            var quotedCelex = celex.Replace("'", "''", StringComparison.Ordinal);
            var wordings = Rows(connection, $"SELECT DISTINCT publisher_work_id, publisher_expression_id, wording_date FROM articles WHERE publisher_work_celex = '{quotedCelex}' AND language = '{language}'");
            if (wordings.Count != 1)
            {
                continue;
            }

            var (workId, expressionId, wordingDate) = (wordings[0][0], wordings[0][1], wordings[0][2]);
            var articles = Rows(connection, $"SELECT publisher_identifier, article_identity_sha256 FROM articles WHERE publisher_expression_id = '{expressionId.Replace("'", "''", StringComparison.Ordinal)}' ORDER BY publisher_identifier, article_identity_sha256");
            var wording = $"/eu-eurlex/{celex}/{language}/{wordingDate}--" +
                V3EuropePermalinkTests.WordingSha256ByTheStatedRule(celex, workId, expressionId, language, wordingDate, articles.Select(static row => row[1]));
            foreach (var provision in articles.Select(static row => row[0]).Distinct(StringComparer.Ordinal).Take(3))
            {
                requests[$"eu-exact-{celex}-{language}-{provision}"] = new(EvaluationCaseKind.ExactIdentifier, "verify", new { identifier = $"{wording}#{Uri.EscapeDataString(provision)}", language },
                    [new JudgedAnchor(celex, provision, JudgedAnchor.SupportingGrade)]);
            }

            requests[$"eu-near-miss-{celex}-{language}"] = new(EvaluationCaseKind.Retrieval, "verify", new { identifier = $"{wording}#lex-no-such-provision", language }, []);
        }
    }

    /// <summary>
    /// A seeded sample of up to <paramref name="perLanguage"/> EU works in each language the index holds, so no held
    /// language goes unsearched however many works another language has (review of #845: the first version took two
    /// work-language pairs in all, and could leave French out).
    /// </summary>
    internal static IReadOnlyList<(string Celex, string Language)> SampleEuropeWorks(IReadOnlyList<(string Celex, string Language)> pairs, int perLanguage, ulong seed)
    {
        var sample = new List<(string Celex, string Language)>();
        foreach (var language in pairs.Select(static pair => pair.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var ofLanguage = pairs.Where(pair => pair.Language == language).OrderBy(static pair => pair.Celex, StringComparer.Ordinal).ToArray();
            var random = new SplitMix64(seed);
            for (var at = ofLanguage.Length - 1; at > 0; at--)
            {
                var other = random.NextBelow(at + 1);
                (ofLanguage[at], ofLanguage[other]) = (ofLanguage[other], ofLanguage[at]);
            }

            sample.AddRange(ofLanguage.Take(perLanguage));
        }

        return sample;
    }

    /// <summary>The provisions of one EU work in one language whose searchable text holds a string, by the EU search's own order.</summary>
    private static List<string> EuropeHolding(SqliteConnection connection, string celex, string language, string text)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT publisher_identifier FROM articles WHERE publisher_work_celex = $celex AND language = $language AND instr(searchable_text, $text) > 0 " +
            "ORDER BY publisher_identifier, article_identity_sha256";
        command.Parameters.AddWithValue("$celex", celex);
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$text", text);
        using var reader = command.ExecuteReader();
        var found = new List<string>();
        while (reader.Read())
        {
            var provision = reader.GetString(0);
            if (!found.Contains(provision, StringComparer.Ordinal))
            {
                found.Add(provision);
            }
        }

        return found;
    }

    /// <summary>
    /// The (work, article) pairs of one language whose searchable text holds a string, by the search's own order, and
    /// the hits the search would count (one per article per state).
    /// </summary>
    private static List<(string Work, string Anchor)> Holding(SqliteConnection connection, string language, string text, string? workKey, out int hits)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT s.work_key, a.publisher_id FROM articles a JOIN states s ON s.expression_iri = a.expression_iri AND s.language = a.language " +
            "WHERE a.language = $language AND instr(a.searchable_text, $text) > 0" + (workKey is null ? "" : " AND s.work_key = $work") +
            " ORDER BY s.work_key, s.applicability_date, a.article_identity_sha256, s.state_sha256";
        command.Parameters.AddWithValue("$language", language);
        command.Parameters.AddWithValue("$text", text);
        if (workKey is not null)
        {
            command.Parameters.AddWithValue("$work", workKey);
        }

        using var reader = command.ExecuteReader();
        var found = new List<(string Work, string Anchor)>();
        hits = 0;
        while (reader.Read())
        {
            hits++;
            var pair = (reader.GetString(0), reader.GetString(1));
            if (!found.Contains(pair))
            {
                found.Add(pair);
            }
        }

        return found;
    }

    /// <summary>The retrieval gate over a mount: nDCG@10, no-hit accuracy and resolver exactness, with the judgments control.</summary>
    internal static EvaluationCardSet RunRetrievalGate(V3CorpusMount mount, string mountDirectory, IReadOnlyList<Timeline> timelines)
    {
        var requests = RetrievalCases(mountDirectory, timelines);
        var cases = requests
            .Select(static pair => new EvaluationCase(pair.Key, RetrievalCollection, pair.Value.Kind, new QueryJudgments(pair.Key, pair.Value.Judged)))
            .ToArray();
        RetrievalArm arm = caseId =>
        {
            var request = requests[caseId];
            var envelope = EnvelopeAsync(mount, "/api/v3/" + request.Operation, request.Operation, request.Parameters).GetAwaiter().GetResult();
            if (envelope.Refusal is { } refusal)
            {
                // verify's only "no hit" is the anchor the state does not hold; any other refusal ranks a marker that
                // matches no judgment, so the case fails rather than passing as "found nothing".
                return request.Operation == "verify" && refusal.Code == "anchor_not_in_version" ? [] : [new RankedAnchor("refused", refusal.Code)];
            }

            var value = envelope.Result!.Value;
            return request.Operation == "verify"
                // A Luxembourg verify names its work by work key; an EU verify by its CELEX, and the provision unescaped.
                ? [new RankedAnchor((value.TryGetProperty("work_key", out var verifiedWork) ? verifiedWork : value.GetProperty("celex")).GetString()!, value.GetProperty("requested_anchor").GetString()!)]
                : value.GetProperty("hits").EnumerateArray()
                    // A Luxembourg hit names its work by work key; an EU hit by its CELEX.
                    .Select(static hit => new RankedAnchor(
                        (hit.TryGetProperty("work_key", out var work) ? work : hit.GetProperty("celex")).GetString()!,
                        hit.GetProperty("publisher_id").GetString()!))
                    .Distinct()
                    .ToArray();
        };
        var report = RetrievalEvaluation.Evaluate(cases, arm, RetrievalFloor, ndcgThreshold: 1.0);
        var control = cases.Length == 0
            ? new ControlResult(ShuffledControlNames.QrelsShuffle, ControlVerdict.NotApplicable, "there is no retrieval case to shuffle: the mount holds no Luxembourg state held alone on its date and no EU index", Seed)
            : ShuffledControls.QrelsShuffle(cases, arm, (set, run) => RetrievalEvaluation.Evaluate(set, run, RetrievalFloor, ndcgThreshold: 1.0), Seed);
        return EvaluationCard.Retrieval("search and verify, cases derived from the mount's own text", cases, report, 1.0, control);
    }

    [TestMethod]
    public async Task TheRetrievalSetDerivedFromTheMountsTextPassesEveryGateOnTheFixture()
    {
        // The fixture with a second work of the same date, so words, scopes and permalinks span two works.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(fixture.ApplicabilityDate, "w2", workLeaf: "n4");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var timelines = Timelines(fixture.Directory);
        var requests = RetrievalCases(fixture.Directory, timelines);
        Assert.IsTrue(requests.Keys.Count(static key => key.StartsWith("word-", StringComparison.Ordinal)) > 0, "some word of the text is held by a few articles");
        Assert.IsTrue(requests.Keys.Any(static key => key.StartsWith("no-hit-", StringComparison.Ordinal)));
        Assert.IsTrue(requests.Keys.Any(static key => key.StartsWith("exact-", StringComparison.Ordinal)));
        var set = RunRetrievalGate(mount, fixture.Directory, timelines);
        foreach (var gate in set.Gates)
        {
            Assert.AreEqual(GateVerdict.Pass, gate.Verdict, $"{gate.Gate}: {gate.Value} over {gate.N}");
        }

        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, set.Control.Reason);
    }

    [TestMethod]
    public async Task TwoStatesOfOneWorkKeepTheirOwnPermalinkCases()
    {
        // Review of #842: the case ids lacked the state, so a later state replaced an earlier one's cases.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync(Day(fixture.ApplicabilityDate).AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "later");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var timelines = Timelines(fixture.Directory);
        var requests = RetrievalCases(fixture.Directory, timelines);
        Assert.AreEqual(6, requests.Keys.Count(static key => key.StartsWith("exact-", StringComparison.Ordinal)), "three anchors of each of the two states");
        Assert.AreEqual(2, requests.Keys.Count(static key => key.StartsWith("near-miss-", StringComparison.Ordinal)));
        Assert.IsTrue(RunRetrievalGate(mount, fixture.Directory, timelines).Gates.All(static gate => gate.Verdict == GateVerdict.Pass));
    }

    [TestMethod]
    public async Task AWorkIsAskedForNothingOnlyInALanguageItIsHeldIn()
    {
        // Review of #842: a work not held in the query's language is refused language_not_available, rightly, so it must
        // never be the work a scoped word is asked of to find nothing. Here the first work is held in German and French,
        // and a second work in French only.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddSecondLanguageStateAsync(null);
        await fixture.AddStateAsync(fixture.ApplicabilityDate, "w2", workLeaf: "n4");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var timelines = Timelines(fixture.Directory);
        var requests = RetrievalCases(fixture.Directory, timelines);
        var heldIn = timelines.Select(static value => (value.WorkKey, value.Language)).ToHashSet();
        foreach (var (key, request) in requests.Where(static pair => pair.Key.StartsWith("scoped-elsewhere-", StringComparison.Ordinal)))
        {
            var parameters = System.Text.Json.JsonSerializer.SerializeToElement(request.Parameters);
            var work = parameters.GetProperty("identifier").GetString()!["/lu-legilux/".Length..];
            Assert.IsTrue(heldIn.Contains((work, parameters.GetProperty("language").GetString()!)), $"{key} asks {work} in a language it is not held in");
        }

        var set = RunRetrievalGate(mount, fixture.Directory, timelines);
        foreach (var gate in set.Gates)
        {
            Assert.AreNotEqual(GateVerdict.Fail, gate.Verdict, $"{gate.Gate}: {gate.Value} over {gate.N}");
        }
    }

    [TestMethod]
    public async Task AnEuIndexGivesItsOwnRetrievalCasesFromItsText()
    {
        // The GDPR mounted alone in its EU index: the EU words and strings held nowhere are measured, scoped to the work,
        // and resolver exactness is measured over the EU permalink grammar: three pinned provisions, each verified as
        // exactly itself, and a provision the wording does not hold, which finds nothing.
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var requests = RetrievalCases(fixture.Directory, Timelines(fixture.Directory));
        Assert.IsTrue(requests.Keys.Any(static key => key.StartsWith("eu-word-", StringComparison.Ordinal)), string.Join(", ", requests.Keys));
        Assert.IsTrue(requests.Keys.Any(static key => key.StartsWith("eu-no-hit-", StringComparison.Ordinal)));
        Assert.AreEqual(3, requests.Keys.Count(static key => key.StartsWith("eu-exact-", StringComparison.Ordinal)), string.Join(", ", requests.Keys));
        Assert.IsTrue(requests.Keys.Any(static key => key.StartsWith("eu-near-miss-", StringComparison.Ordinal)));
        var set = RunRetrievalGate(mount, fixture.Directory, Timelines(fixture.Directory));
        var gates = set.Gates.ToDictionary(static gate => gate.Gate);
        Assert.AreEqual(GateVerdict.Pass, gates[EvaluationGateNames.AnchorNdcgAt10].Verdict, $"{gates[EvaluationGateNames.AnchorNdcgAt10].Value} over {gates[EvaluationGateNames.AnchorNdcgAt10].N}");
        Assert.AreEqual(GateVerdict.Pass, gates[EvaluationGateNames.NoHitAccuracy].Verdict);
        Assert.AreEqual(GateVerdict.Pass, gates[EvaluationGateNames.ResolverExactness].Verdict,
            $"each pinned EU provision resolves to exactly itself: {gates[EvaluationGateNames.ResolverExactness].Value} over {gates[EvaluationGateNames.ResolverExactness].N}");
    }

    [TestMethod]
    public void TheEuSampleTakesWorksInEveryLanguage()
    {
        // The review of #845's reproduction: three English works and one French; the first version's sample of two pairs
        // in all, after its shuffle, took two English ones and left French unsearched.
        var pairs = new[] { ("A", "eng"), ("B", "eng"), ("C", "eng"), ("D", "fra") };
        var sample = SampleEuropeWorks(pairs, EuropeWorkSample, Seed);
        CollectionAssert.Contains(sample.ToArray(), ("D", "fra"), "the French work is searched");
        Assert.AreEqual(2, sample.Count(static pair => pair.Language == "eng"), "two of the English works");
        Assert.AreEqual(3, sample.Count);
    }

    [TestMethod]
    public async Task AnEuWorkHeldInFrenchIsSearchedInFrench()
    {
        // The GDPR mounted with its French expression in place of the English one: the language the index holds is the
        // language searched, so a French work gives French cases, and they pass.
        var fixture = await EuropeMountedFixture.CreateAsync(acquireFrenchExpression: true);
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var requests = RetrievalCases(fixture.Directory, Timelines(fixture.Directory));
        Assert.IsTrue(requests.Keys.Any(static key => key.StartsWith("eu-word-", StringComparison.Ordinal) && key.Contains("-fra-", StringComparison.Ordinal)), string.Join(", ", requests.Keys));
        Assert.IsFalse(requests.Keys.Any(static key => key.Contains("-eng-", StringComparison.Ordinal)), "no English is held here, so none is asked");
        var gates = RunRetrievalGate(mount, fixture.Directory, Timelines(fixture.Directory)).Gates.ToDictionary(static gate => gate.Gate);
        Assert.AreEqual(GateVerdict.Pass, gates[EvaluationGateNames.AnchorNdcgAt10].Verdict, $"{gates[EvaluationGateNames.AnchorNdcgAt10].Value} over {gates[EvaluationGateNames.AnchorNdcgAt10].N}");
        Assert.AreEqual(GateVerdict.Pass, gates[EvaluationGateNames.NoHitAccuracy].Verdict);
    }
}
