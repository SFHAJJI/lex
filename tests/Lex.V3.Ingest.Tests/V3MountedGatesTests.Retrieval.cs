using System.Globalization;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
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
/// </summary>
public sealed partial class V3MountedGatesTests
{
    private const string RetrievalCollection = "lu-mount";
    private const int RetrievalFloor = 3;
    private const int MaxWordCases = 6;
    private const int MaxJudgedPerWord = 5;
    private static readonly string[] NoHitStrings = ["zqxjvkwp", "qqzzkxjw", "wxqzjvkk"];

    /// <summary>A request and what the mount's text says it must rank.</summary>
    internal sealed record RetrievalRequest(EvaluationCaseKind Kind, string Operation, object Parameters, JudgedAnchor[] Judged);

    internal static IReadOnlyDictionary<string, RetrievalRequest> RetrievalCases(string mountDirectory, IReadOnlyList<Timeline> timelines)
    {
        var requests = new SortedDictionary<string, RetrievalRequest>(StringComparer.Ordinal);
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
            if (requests.Count >= MaxWordCases)
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

        // A word scoped to its own work finds that work's articles; scoped to another work that lacks it, nothing.
        var otherWork = held.Select(static value => value.Timeline.WorkKey).Distinct(StringComparer.Ordinal).ToArray();
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
            var permalink = $"/lu-legilux/{timeline.WorkKey}/{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}--{state}";
            var anchors = Rows(connection, "SELECT a.publisher_id FROM articles a JOIN states s ON s.expression_iri = a.expression_iri " +
                    $"WHERE s.state_sha256 = '{state}' AND s.language = '{timeline.Language}' ORDER BY a.publisher_id LIMIT 3")
                .Select(static row => row[0]).ToArray();
            foreach (var anchor in anchors)
            {
                requests[$"exact-{timeline.WorkKey}-{anchor}"] = new(EvaluationCaseKind.ExactIdentifier, "verify", new { identifier = $"{permalink}#{anchor}", language = timeline.Language },
                    [new JudgedAnchor(timeline.WorkKey, anchor, JudgedAnchor.SupportingGrade)]);
            }

            if (anchors.Length > 0)
            {
                requests[$"near-miss-{timeline.WorkKey}"] = new(EvaluationCaseKind.Retrieval, "verify", new { identifier = $"{permalink}#art_no_such_anchor", language = timeline.Language }, []);
            }
        }

        return requests;
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
                ? [new RankedAnchor(value.GetProperty("work_key").GetString()!, value.GetProperty("requested_anchor").GetString()!)]
                : value.GetProperty("hits").EnumerateArray()
                    .Select(static hit => new RankedAnchor(hit.GetProperty("work_key").GetString()!, hit.GetProperty("publisher_id").GetString()!))
                    .Distinct()
                    .ToArray();
        };
        var report = RetrievalEvaluation.Evaluate(cases, arm, RetrievalFloor, ndcgThreshold: 1.0);
        var control = cases.Length == 0
            ? new ControlResult(ShuffledControlNames.QrelsShuffle, ControlVerdict.NotApplicable, "there is no retrieval case to shuffle: the mount holds no Luxembourg state held alone on its date", Seed)
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
}
