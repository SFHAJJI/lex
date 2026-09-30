using System.Globalization;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The refusal set over a mount: each request is built from what the mount holds, so the one code the
/// registry says answers it follows from the mount's own data (a date before a held work's first state, a
/// digest that is not its state's, a title two of its works carry). A code this mount cannot produce (no
/// two states share a date, no member's licence withholds text) is named as not produced, with its reason,
/// and never faked. A mount with no Luxembourg state holds only what does not need one.
/// </summary>
public sealed partial class V3MountedGatesTests
{
    /// <summary>A request a client can send, and the one verdict the mount's data says answers it.</summary>
    internal sealed record RefusalRequest(string Operation, object Parameters, string Gold);

    /// <summary>The refusal set a mount gives, and the codes it cannot produce, each with why.</summary>
    internal sealed record RefusalSet(IReadOnlyDictionary<string, RefusalRequest> Requests, IReadOnlyDictionary<string, string> NotProduced);

    private const string NoSuchWork = "/lu-legilux/no-such-work";
    private static readonly string[] CandidateLanguages = ["eng", "deu", "fra", "ltz"];

    internal static RefusalSet RefusalCases(string mountDirectory, IReadOnlyList<Timeline> timelines)
    {
        var requests = new SortedDictionary<string, RefusalRequest>(StringComparer.Ordinal)
        {
            ["coverage"] = new("coverage", new { }, "answer"),
        };
        var notProduced = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var hasEuropeIndex = File.Exists(Path.Combine(mountDirectory, V3CorpusMount.EuropeIndexFileName));
        if (!hasEuropeIndex)
        {
            requests["eu-work-no-eu-index"] = new("search", new { query = "x", language = "eng", identifier = "32016R0679" }, "no_corpus_mounted");
        }
        else
        {
            notProduced["no_corpus_mounted"] = "the mount holds an EU index, so an EU request is not refused for want of one";
        }

        // A dated state held alone on its date: the anchor of every request that names one state.
        var held = timelines
            .SelectMany(static timeline => timeline.Dates.Where(static date => date.States.Count == 1).Select(date => (Timeline: timeline, date.Date, State: date.States[0])))
            .FirstOrDefault();
        if (held.Timeline is null)
        {
            foreach (var code in new[] { "identifier_unknown", "language_not_available", "no_version_for_date", "anchor_not_in_version", "pinned_digest_mismatch", "retrieval_mode_unavailable", "snapshot_unknown", "format_not_available", "ambiguous_version", "ambiguous_identifier", "profiles_differ", "text_withheld", "text_not_available" })
            {
                notProduced[code] = "the mount holds no Luxembourg state held alone on its date";
            }

            return new RefusalSet(requests, notProduced);
        }

        var (timeline, date, state) = held;
        var work = $"/lu-legilux/{timeline.WorkKey}";
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var language = timeline.Language;
        requests["a-held-state"] = new("as_of", new { identifier = work, date = day, language }, "answer");
        requests["the-held-permalink"] = new("verify", new { identifier = $"{work}/{day}--{state}", language }, "answer");
        requests["unknown-work"] = new("as_of", new { identifier = NoSuchWork, date = day }, "identifier_unknown");
        requests["before-history"] = new("as_of", new { identifier = work, date = timeline.Dates[0].Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), language }, "no_version_for_date");
        requests["anchor-not-held"] = new("article_history", new { identifier = work, anchor = "art_no_such_anchor", language }, "anchor_not_in_version");
        requests["pinned-digest-not-held"] = new("verify", new { identifier = $"{work}/{day}--{new string('0', 64)}", language }, "pinned_digest_mismatch");
        requests["mode-not-held"] = new("search", new { query = "loi", language, mode = "bm25" }, "retrieval_mode_unavailable");
        requests["foreign-cursor"] = new("events", new { after = new string('0', 64) + ":1" }, "snapshot_unknown");
        requests["format-not-held"] = new("manifestation", new { identifier = work, format = "docx" }, "format_not_available");

        var heldLanguages = timelines.Where(value => value.WorkKey == timeline.WorkKey).Select(static value => value.Language).ToHashSet(StringComparer.Ordinal);
        var absent = CandidateLanguages.FirstOrDefault(candidate => !heldLanguages.Contains(candidate));
        if (absent is not null)
        {
            requests["language-not-held"] = new("dossier", new { identifier = work, language = absent }, "language_not_available");
            requests["as-of-language-not-held"] = new("as_of", new { identifier = work, date = day, language = absent }, "language_not_available");
        }
        else
        {
            notProduced["language_not_available"] = $"{timeline.WorkKey} is held in every candidate language";
        }

        var twin = timelines.SelectMany(static value => value.Dates.Where(static date => date.States.Count > 1).Select(date => (Timeline: value, date.Date))).FirstOrDefault();
        if (twin.Timeline is not null)
        {
            requests["twin-states"] = new("as_of", new { identifier = $"/lu-legilux/{twin.Timeline.WorkKey}", date = twin.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), language = twin.Timeline.Language }, "ambiguous_version");
        }
        else
        {
            notProduced["ambiguous_version"] = "no two states of one work share a date in one language";
        }

        using var connection = LuxembourgIndexBuilder.Open(Path.Combine(mountDirectory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        var title = AmbiguousTitle(Rows(connection, "SELECT work_identifier, normalized_title FROM work_titles").Select(static row => (row[0], row[1])).ToArray());
        if (title is not null)
        {
            requests["title-two-works-carry"] = new("resolve", new { identifier = title }, "ambiguous_identifier");
        }
        else
        {
            notProduced["ambiguous_identifier"] = "no title, nor any beginning of one, is carried by two works";
        }

        // Two states of one work in one language, each alone on its date, whose rule profiles differ.
        var profiles = Rows(connection, "SELECT work_key, language, applicability_date, rule_profiles_json FROM states ORDER BY work_key, language, applicability_date")
            .GroupBy(static row => (row[0], row[1], row[2]))
            .Where(static group => group.Count() == 1)
            .Select(static group => group.Single())
            .GroupBy(static row => (row[0], row[1]))
            .SelectMany(static group => group.Zip(group.Skip(1), static (from, to) => (From: from, To: to)))
            .FirstOrDefault(static pair => pair.From[3] != pair.To[3]);
        if (profiles.From is not null)
        {
            requests["profiles-differ"] = new("diff", new { identifier = $"/lu-legilux/{profiles.From[0]}", date_from = profiles.From[2], date_to = profiles.To[2], language = profiles.From[1] }, "profiles_differ");
        }
        else
        {
            notProduced["profiles_differ"] = "no two states of one work differ in rule profile";
        }

        // A state held alone on its date whose every article belongs to a member whose licence does not admit
        // redistribution; and one whose every article holds no text.
        var withheld = StateWhere(connection, "NOT EXISTS (SELECT 1 FROM articles a JOIN members m ON m.object_ref_sha256 = a.object_ref_sha256 WHERE a.expression_iri = s.expression_iri AND (m.rights_disposition IS NULL OR m.rights_disposition NOT LIKE 'non_admitting%'))");
        if (withheld is { } blocked)
        {
            requests["rights-not-agreed"] = new("evidence_bundle", new { identifier = $"/lu-legilux/{blocked.Work}", date = blocked.Date, language = blocked.Language }, "text_withheld");
        }
        else
        {
            notProduced["text_withheld"] = "no member's licence withholds the text of a whole state";
        }

        var empty = StateWhere(connection, "NOT EXISTS (SELECT 1 FROM articles a WHERE a.expression_iri = s.expression_iri AND a.searchable_text <> '')");
        if (empty is { } textless)
        {
            requests["no-text-held"] = new("evidence_bundle", new { identifier = $"/lu-legilux/{textless.Work}", date = textless.Date, language = textless.Language }, "text_not_available");
        }
        else
        {
            notProduced["text_not_available"] = "every state holds text";
        }

        return new RefusalSet(requests, notProduced);
    }

    /// <summary>
    /// A title query the resolver answers with two works or more, by its own rule (an exact normalized title
    /// first, else the titles it begins): a normalized title two works carry, or the longest beginning two works'
    /// titles share, which sorted titles place side by side. Null when the mount has none.
    /// </summary>
    internal static string? AmbiguousTitle(IReadOnlyList<(string Work, string Normalized)> titles)
    {
        var sorted = titles.OrderBy(static title => title.Normalized, StringComparer.Ordinal).ToArray();
        var candidates = sorted.Select(static title => title.Normalized)
            .Concat(sorted.Zip(sorted.Skip(1)).Where(static pair => pair.First.Work != pair.Second.Work).Select(static pair => SharedBeginning(pair.First.Normalized, pair.Second.Normalized)))
            .Select(static candidate => LuxembourgIndexBuilder.NormalizeTitle(candidate))
            .Where(static candidate => candidate.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return candidates.FirstOrDefault(candidate =>
        {
            var exact = titles.Where(title => title.Normalized == candidate).ToArray();
            var selected = exact.Length > 0 ? exact : titles.Where(title => title.Normalized.StartsWith(candidate, StringComparison.Ordinal)).ToArray();
            return selected.Select(static title => title.Work).Distinct(StringComparer.Ordinal).Count() >= 2;
        });
    }

    private static string SharedBeginning(string a, string b)
    {
        var length = 0;
        while (length < a.Length && length < b.Length && a[length] == b[length])
        {
            length++;
        }

        return a[..length].TrimEnd();
    }

    /// <summary>A state alone on its date, with articles, that meets a condition on its articles (as <c>s</c>).</summary>
    private static (string Work, string Date, string Language)? StateWhere(SqliteConnection connection, string condition)
    {
        var row = Rows(connection,
            "SELECT s.work_key, s.applicability_date, s.language FROM states s " +
            "WHERE EXISTS (SELECT 1 FROM articles a WHERE a.expression_iri = s.expression_iri) AND " + condition + " " +
            "AND (SELECT COUNT(*) FROM states t WHERE t.work_key = s.work_key AND t.language = s.language AND t.applicability_date = s.applicability_date) = 1 " +
            "ORDER BY s.work_key, s.applicability_date, s.language LIMIT 1").FirstOrDefault();
        return row is null ? null : (row[0], row[1], row[2]);
    }

    private static string? Scalar(SqliteConnection connection, string sql) => Rows(connection, sql).FirstOrDefault()?[0];

    private static List<string[]> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string[]>();
        while (reader.Read())
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount).Select(at => reader.GetString(at)).ToArray());
        }

        return rows;
    }

    /// <summary>The refusal gate over a mount: every request answered with its one code, and the verdict control.</summary>
    internal static (EvaluationCardSet Set, RefusalSet Cases) RunRefusalGate(V3CorpusMount mount, string mountDirectory, IReadOnlyList<Timeline> timelines)
    {
        var derived = RefusalCases(mountDirectory, timelines);
        var cases = derived.Requests.Select(static pair => new VerdictCase(pair.Key, pair.Value.Gold)).ToArray();
        VerdictArm arm = caseId =>
        {
            var request = derived.Requests[caseId];
            var envelope = EnvelopeAsync(mount, "/api/v3/" + request.Operation, request.Operation, request.Parameters).GetAwaiter().GetResult();
            return envelope.Refusal?.Code ?? "answer";
        };
        var report = VerdictEvaluation.Evaluate(cases, arm, floor: Math.Max(1, cases.Length));
        var control = ShuffledControls.VerdictShuffle(cases, arm, (set, run) => VerdictEvaluation.Evaluate(set, run, floor: Math.Max(1, set.Count)), Seed);
        var notProduced = derived.NotProduced.Count == 0 ? "every produced code" : "not produced here: " + string.Join("; ", derived.NotProduced.Select(static pair => $"{pair.Key} ({pair.Value})"));
        return (EvaluationCard.Verdict("refusal", $"the served operations, cases derived from the mount; {notProduced}", cases, report, control), derived);
    }

    [TestMethod]
    public async Task TheRefusalSetDerivedFromTheMountIsAnsweredCodeForCodeOnTheFixture()
    {
        // The fixture with what a refusal needs to be producible: two titled works, a later state with another
        // rule profile and two states on one date.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddTwoWorkTitlesAsync();
        var first = Day(fixture.ApplicabilityDate);
        var later = await fixture.AddStateAsync(first.AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "later");
        await fixture.AddRuleProfileToStateAsync(later.ExpressionIri, new string('a', 64));
        await fixture.AddStateAsync(first.AddDays(800).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "twin-a");
        await fixture.AddStateAsync(first.AddDays(800).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var (set, derived) = RunRefusalGate(mount, fixture.Directory, Timelines(fixture.Directory));
        Assert.AreEqual(GateVerdict.Pass, set.Gates.Single().Verdict, "every derived request is refused with its own code, or answered where it must be");
        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, set.Control.Reason);
        CollectionAssert.AreEquivalent(
            new[] { "text_withheld", "text_not_available" }, derived.NotProduced.Keys.ToArray(),
            "one mount cannot hold a withheld licence and text beside them; every other produced code is derived: " + string.Join("; ", derived.NotProduced.Select(static pair => pair.Key + " (" + pair.Value + ")")));
        Assert.AreEqual(16, set.CaseCount, "the fixture machine gates' 18 less the two codes another mount holds");
    }

    [TestMethod]
    public async Task ALicenceThatWithholdsTextIsDerivedAsTextWithheld()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.SetMemberRightsDispositionAsync("non_admitting_licence_scl");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var (set, derived) = RunRefusalGate(mount, fixture.Directory, Timelines(fixture.Directory));
        Assert.AreEqual("text_withheld", derived.Requests["rights-not-agreed"].Gold);
        Assert.AreEqual(GateVerdict.Pass, set.Gates.Single().Verdict);
    }
}
