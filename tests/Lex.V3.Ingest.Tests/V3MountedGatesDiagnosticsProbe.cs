using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// An opt-in probe (<c>V3_GATES_DIAGNOSE</c> names an output file, <c>V3_EVALUATE_MOUNT</c> the mount): every temporal case of
/// the mounted gates whose answer is not the oracle's, with the work's catalogue rows as index 9 holds them, and the refusal
/// set's answers, so a failing gate's cases can be read one by one. It checks nothing.
/// </summary>
[TestClass]
public sealed class V3MountedGatesDiagnosticsProbe
{
    [TestMethod]
    public async Task TheMountedGatesFailingCasesArePrintedWithTheirWorksCatalogueRows()
    {
        var output = Environment.GetEnvironmentVariable("V3_GATES_DIAGNOSE");
        var directory = Environment.GetEnvironmentVariable("V3_EVALUATE_MOUNT");
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(directory))
        {
            Assert.Inconclusive("V3_GATES_DIAGNOSE and V3_EVALUATE_MOUNT name no output and no mount.");
        }

        using var mount = await V3CorpusMount.OpenAsync(directory!, CancellationToken.None);
        Assert.IsNotNull(mount);
        var timelines = V3MountedGatesTests.Sample(V3MountedGatesTests.Timelines(directory!), 40, 20260930);
        var text = new StringBuilder();
        using var connection = LuxembourgIndexBuilder.Open(Path.Combine(directory!, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);

        string RowsOf(string work)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT applicability_date, language, state_kind, date_role, applicable_to, applicable_to_source, boundary, text_status, substr(state_sha256,1,12), publisher_legal_resource_iri FROM states WHERE work_key=$w ORDER BY applicability_date, language, expression_iri";
            command.Parameters.AddWithValue("$w", work);
            using var reader = command.ExecuteReader();
            var lines = new StringBuilder();
            while (reader.Read())
            {
                lines.Append("      ");
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    lines.Append(reader.IsDBNull(i) ? "null" : reader.GetValue(i)).Append(i + 1 < reader.FieldCount ? " | " : "");
                }

                lines.AppendLine();
            }

            return lines.ToString();
        }

        var works = new SortedSet<string>(StringComparer.Ordinal);

        // The release's gate as it ran at 7152de65: the timelines over every state row of index 9, textless ones included, by
        // the index 8 rule; each case it failed, classed by what the reader answered.
        var allRows = LexIndexAllRowTimelines(connection);
        var releaseTimelines = allRows.Where(value => timelines.Any(held => held.WorkKey == value.WorkKey)).ToArray();
        foreach (var (name, arm) in new[] { ("release as_of in fra", V3MachineGatesTests.AsOfArm(mount, "fra")), ("release in_force_on in fra", V3MachineGatesTests.InForceOnArm(mount, "fra")) })
        {
            var cases = releaseTimelines.Where(static value => value.Language == "fra").SelectMany(V3MountedGatesTests.Cases).ToArray();
            var wrong = 0;
            foreach (var one in cases)
            {
                var actual = arm(one.WorkKey, one.AsOf);
                if (actual == one.ExpectedStateKey)
                {
                    continue;
                }

                wrong++;
                text.AppendLine($"{name} | {one.CaseId} | {one.AsOf:yyyy-MM-dd} | expected {Short(one.ExpectedStateKey)} | actual {Short(actual)} | {Detail(mount, one.WorkKey, one.AsOf, "fra")}");
            }

            text.AppendLine($"== {name}: {wrong} of {cases.Length} differ");
        }

        // The release's refusal set's profiles pair: two states alone on their dates whose profiles differ, over every row.
        var diff = await EnvelopeAsync(mount, "/api/v3/diff", "diff", new { identifier = "/lu-legilux/code-civil", date_from = "2018-09-16", date_to = "2018-11-01", language = "fra" });
        text.AppendLine($"== release refusal set, profiles-differ (code-civil 2018-09-16 to 2018-11-01): {(diff.Refusal is { } refused ? refused.Code + " " + refused.HelpfulPayload.GetRawText()[..Math.Min(300, refused.HelpfulPayload.GetRawText().Length)] : "answer")}");

        foreach (var language in timelines.Select(static value => value.Language).Distinct().Order(StringComparer.Ordinal))
        {
            var ofLanguage = timelines.Where(value => value.Language == language).ToArray();
            foreach (var (name, arm) in new[] { ($"as_of in {language}", V3MachineGatesTests.AsOfArm(mount, language)), ($"in_force_on in {language}", V3MachineGatesTests.InForceOnArm(mount, language)) })
            {
                var cases = ofLanguage.SelectMany(V3MountedGatesTests.Cases).ToArray();
                var wrong = 0;
                foreach (var one in cases)
                {
                    var actual = arm(one.WorkKey, one.AsOf);
                    if (actual == one.ExpectedStateKey)
                    {
                        continue;
                    }

                    wrong++;
                    works.Add(one.WorkKey);
                    text.AppendLine($"{name} | {one.CaseId} | {one.AsOf:yyyy-MM-dd} | expected {Short(one.ExpectedStateKey)} | actual {Short(actual)} | {Detail(mount, one.WorkKey, one.AsOf, language)}");
                }

                text.AppendLine($"== {name}: {wrong} of {cases.Length} differ");
            }
        }

        var workTimelines = timelines.GroupBy(static value => value.WorkKey).Select(static group => new V3MountedGatesTests.WorkTimeline(group.Key, group.ToArray())).ToArray();
        foreach (var (name, arm) in new[] { ("as_of with no language", V3MountedGatesTests.DefaultAsOfArm(mount)), ("in_force_on with no language", V3MountedGatesTests.DefaultInForceOnArm(mount)) })
        {
            var cases = workTimelines.SelectMany(V3MountedGatesTests.Cases).ToArray();
            var wrong = 0;
            foreach (var one in cases)
            {
                var actual = arm(one.WorkKey, one.AsOf);
                if (actual == one.ExpectedStateKey)
                {
                    continue;
                }

                wrong++;
                text.AppendLine($"{name} | {one.CaseId} | {one.AsOf:yyyy-MM-dd} | expected {Short(one.ExpectedStateKey)} | actual {Short(actual)}");
            }

            text.AppendLine($"== {name}: {wrong} of {cases.Length} differ");
        }

        // The interval oracle's cases against the reader (what the gate asks now), over the stratified sample, with each stratum's
        // works in the population and in the sample.
        using (var oracle = V3MountedGatesTests.IntervalOracle.Open(directory!))
        {
            if (oracle is not null)
            {
                var population = V3MountedGatesTests.Timelines(directory!);
                var stratified = V3MountedGatesTests.Stratified(timelines, population, oracle);
                var sampled = stratified.Select(static value => value.WorkKey).ToHashSet(StringComparer.Ordinal);
                var classed = population.Select(static value => value.WorkKey).Distinct(StringComparer.Ordinal).Select(work => (Work: work, Classes: oracle.Classes(work))).ToArray();
                foreach (var kind in V3MountedGatesTests.StratumWorks.Keys)
                {
                    var holding = classed.Where(value => value.Classes.Contains(kind)).Select(static value => value.Work).ToArray();
                    text.AppendLine($"== stratum {kind}: {holding.Length} works in the population, {holding.Count(sampled.Contains)} in the sample");
                }

                text.AppendLine($"== the stratified sample: {sampled.Count} works of {classed.Length}");
                var arms = new List<(string Name, string? Language, TemporalArm Arm, Func<string, string?, DateOnly, string> Expected, string[] Works)>();
                foreach (var language in stratified.Select(static value => value.Language).Distinct().Order(StringComparer.Ordinal))
                {
                    var held = stratified.Where(value => value.Language == language).Select(static value => value.WorkKey).Distinct().Order(StringComparer.Ordinal).ToArray();
                    arms.Add(($"interval as_of in {language}", language, V3MountedGatesTests.IntervalAsOfArm(mount, language), oracle.AsOf, held));
                    arms.Add(($"interval in_force_on in {language}", language, V3MountedGatesTests.IntervalInForceOnArm(mount, language), oracle.InForceOn, held));
                }

                var all = stratified.Select(static value => value.WorkKey).Distinct().Order(StringComparer.Ordinal).ToArray();
                arms.Add(("interval as_of with no language", null, V3MountedGatesTests.IntervalAsOfArm(mount, null), oracle.AsOf, all));
                arms.Add(("interval in_force_on with no language", null, V3MountedGatesTests.IntervalInForceOnArm(mount, null), oracle.InForceOn, all));
                foreach (var (name, language, arm, expected, held) in arms)
                {
                    var cases = held.SelectMany(work => V3MountedGatesTests.IntervalCases(oracle, work, language, expected)).ToArray();
                    var wrong = 0;
                    foreach (var one in cases)
                    {
                        var actual = arm(one.WorkKey, one.AsOf);
                        if (actual == one.ExpectedStateKey)
                        {
                            continue;
                        }

                        wrong++;
                        works.Add(one.WorkKey);
                        text.AppendLine($"{name} | {one.CaseId} | {one.AsOf:yyyy-MM-dd} | expected {Short(one.ExpectedStateKey)} | actual {Short(actual)}");
                    }

                    text.AppendLine($"== {name}: {wrong} of {cases.Length} differ");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("== the works of the differing cases, their catalogue rows: date | language | kind | role | applicable_to | source | boundary | text_status | digest | legal resource");
        foreach (var work in works)
        {
            text.AppendLine($"  {work}");
            text.Append(RowsOf(work));
        }

        text.AppendLine();
        text.AppendLine("== the refusal set");
        var set = V3MountedGatesTests.RefusalCases(directory!, timelines);
        foreach (var (id, request) in set.Requests)
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/" + request.Operation, request.Operation, request.Parameters);
            var verdict = envelope.Refusal is { } refusal ? refusal.Code : "answer";
            text.AppendLine($"{(verdict == request.Gold ? "ok  " : "DIFF")} {id} | {request.Operation} {JsonSerializer.Serialize(request.Parameters)} | gold {request.Gold} | actual {verdict}{(envelope.Refusal is { } r && r.HelpfulPayload.TryGetProperty("reason", out var reason) ? " (" + reason + ")" : "")}");
        }

        await File.WriteAllTextAsync(output!, text.ToString());
        Assert.Fail($"V3_GATES_DIAGNOSE wrote {output} and checked nothing.");
    }

    /// <summary>
    /// The EU temporal cases and the refusal set the release gate asks (<c>V3_GATES_DIAGNOSE_EU</c> names the output file), each
    /// case whose answer is not the one its set derives, with the refusal's code and payload, so the EU and refusal gates can be
    /// read case by case without the Luxembourg arms. It checks nothing.
    /// </summary>
    [TestMethod]
    public async Task TheEuTemporalAndRefusalCasesThatDifferArePrinted()
    {
        var output = Environment.GetEnvironmentVariable("V3_GATES_DIAGNOSE_EU");
        var directory = Environment.GetEnvironmentVariable("V3_EVALUATE_MOUNT");
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(directory))
        {
            Assert.Inconclusive("V3_GATES_DIAGNOSE_EU and V3_EVALUATE_MOUNT name no output and no mount.");
        }

        using var mount = await V3CorpusMount.OpenAsync(directory!, CancellationToken.None);
        Assert.IsNotNull(mount);
        var text = new StringBuilder();
        var timelines = V3MountedGatesTests.Sample(V3MountedGatesTests.EuropeTimelines(directory!), 40, 20260930);
        var acts = timelines.GroupBy(static value => value.WorkKey, StringComparer.Ordinal)
            .Select(static group => new V3MountedGatesTests.EuropeActTimeline(group.Key, group.ToArray())).ToArray();
        var arms = timelines.Select(static value => value.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(language => ($"EU as_of in {language}", (string?)language, timelines.Where(value => value.Language == language).SelectMany(V3MountedGatesTests.Cases).ToArray()))
            .Append(("EU as_of with no language", null, acts.SelectMany(V3MountedGatesTests.Cases).ToArray()));
        foreach (var (name, language, cases) in arms)
        {
            var wrong = 0;
            foreach (var one in cases)
            {
                var actual = V3MachineGatesTests.EuropeAsOfArm(mount, language)(one.WorkKey, one.AsOf);
                if (actual == one.ExpectedStateKey)
                {
                    continue;
                }

                wrong++;
                var request = language is null
                    ? (object)new { identifier = one.WorkKey, date = one.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
                    : new { identifier = one.WorkKey, date = one.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), language };
                var envelope = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", request);
                var payload = envelope.Refusal is { } refusal ? refusal.Code + " " + Clip(refusal.HelpfulPayload.GetRawText()) : "answer " + Clip(envelope.Result!.Value.GetRawText());
                text.AppendLine($"{name} | {one.CaseId} | expected {one.ExpectedStateKey} | actual {actual ?? "null"} | {payload}");
            }

            text.AppendLine($"== {name}: {wrong} of {cases.Length} differ");
        }

        text.AppendLine();
        text.AppendLine("== the refusal set");
        var set = V3MountedGatesTests.RefusalCases(directory!, V3MountedGatesTests.Sample(V3MountedGatesTests.Timelines(directory!), 40, 20260930));
        foreach (var (id, request) in set.Requests)
        {
            var envelope = await EnvelopeAsync(mount, "/api/v3/" + request.Operation, request.Operation, request.Parameters);
            var verdict = envelope.Refusal is { } refusal ? refusal.Code : "answer";
            text.AppendLine($"{(verdict == request.Gold ? "ok  " : "DIFF")} {id} | {request.Operation} {JsonSerializer.Serialize(request.Parameters)} | gold {request.Gold} | actual {verdict}" +
                            (verdict == request.Gold ? "" : " | " + (envelope.Refusal is { } refused ? Clip(refused.HelpfulPayload.GetRawText()) : Clip(envelope.Result?.Value.GetRawText() ?? ""))));
        }

        await File.WriteAllTextAsync(output!, text.ToString());
        Assert.Fail($"V3_GATES_DIAGNOSE_EU wrote {output} and checked nothing.");
    }

    /// <summary>
    /// The interval arms over one stratum's works in the stratified sample (<c>V3_GATES_STRATUM</c> names the stratum,
    /// <c>V3_GATES_DIAGNOSE</c> the output file): the works, each arm's cases and the ones that differ, so a stratum can be
    /// confirmed over a mount without the whole gate. It checks nothing.
    /// </summary>
    [TestMethod]
    public async Task TheIntervalArmsOverOneStratumArePrinted()
    {
        var output = Environment.GetEnvironmentVariable("V3_GATES_DIAGNOSE");
        var stratum = Environment.GetEnvironmentVariable("V3_GATES_STRATUM");
        var directory = Environment.GetEnvironmentVariable("V3_EVALUATE_MOUNT");
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(stratum) || string.IsNullOrWhiteSpace(directory))
        {
            Assert.Inconclusive("V3_GATES_DIAGNOSE, V3_GATES_STRATUM and V3_EVALUATE_MOUNT name no output, stratum or mount.");
        }

        using var mount = await V3CorpusMount.OpenAsync(directory!, CancellationToken.None);
        Assert.IsNotNull(mount);
        using var oracle = IndexNineOracle(directory!);
        var population = V3MountedGatesTests.Timelines(directory!);
        var stratified = V3MountedGatesTests.Stratified(V3MountedGatesTests.Sample(population, 40, 20260930), population, oracle);
        var works = stratified.Select(static value => value.WorkKey).Distinct(StringComparer.Ordinal).Where(work => oracle.Classes(work).Contains(stratum!)).Order(StringComparer.Ordinal).ToArray();
        var text = new StringBuilder($"== stratum {stratum}: {works.Length} works in the sample: {string.Join(", ", works)}\n");
        var languages = stratified.Where(value => works.Contains(value.WorkKey)).Select(static value => (string?)value.Language).Distinct().Order(StringComparer.Ordinal).Append(null);
        foreach (var language in languages)
        {
            foreach (var (name, arm, expected) in new[]
                     {
                         ($"as_of in {language ?? "no language"}", V3MountedGatesTests.IntervalAsOfArm(mount, language), (Func<string, string?, DateOnly, string>)oracle.AsOf),
                         ($"in_force_on in {language ?? "no language"}", V3MountedGatesTests.IntervalInForceOnArm(mount, language), oracle.InForceOn),
                     })
            {
                var asked = (language is null ? works : stratified.Where(value => value.Language == language && works.Contains(value.WorkKey)).Select(static value => value.WorkKey).Distinct().ToArray())
                    .SelectMany(work => V3MountedGatesTests.IntervalCases(oracle, work, language, expected)).ToArray();
                var wrong = asked.Where(one => arm(one.WorkKey, one.AsOf) != one.ExpectedStateKey).ToArray();
                foreach (var one in wrong)
                {
                    text.AppendLine($"{name} | {one.CaseId} | expected {one.ExpectedStateKey} | actual {arm(one.WorkKey, one.AsOf) ?? "null"}");
                }

                text.AppendLine($"== {name}: {wrong.Length} of {asked.Length} differ");
            }
        }

        await File.WriteAllTextAsync(output!, text.ToString());
        Assert.Fail($"V3_GATES_DIAGNOSE wrote {output} and checked nothing.");
    }

    private static V3MountedGatesTests.IntervalOracle IndexNineOracle(string directory) =>
        V3MountedGatesTests.IntervalOracle.Open(directory) ?? throw new AssertInconclusiveException($"{directory} holds no index 9.");

    private static string Clip(string value) => value.Length <= 600 ? value : value[..600] + "…";

    /// <summary>The timelines as the release's gate read them: every state row, textless ones included, by work and language.</summary>
    private static V3MountedGatesTests.Timeline[] LexIndexAllRowTimelines(SqliteConnection connection) =>
        LuxembourgIndexBuilderTests.ReadStates(connection)
            .GroupBy(static row => (row.WorkKey, row.Language))
            .OrderBy(static group => group.Key.WorkKey, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Language, StringComparer.Ordinal)
            .Select(static group => new V3MountedGatesTests.Timeline(group.Key.WorkKey, group.Key.Language, group
                .GroupBy(static row => DateOnly.ParseExact(row.ApplicabilityDate, "yyyy-MM-dd", CultureInfo.InvariantCulture))
                .OrderBy(static date => date.Key)
                .Select(static date => (date.Key, (IReadOnlyList<string>)date.Select(static row => row.StateSha256).Order(StringComparer.Ordinal).ToArray()))
                .ToArray()))
            .ToArray();

    private static string Short(string? key) => key is null ? "null" : string.Join('+', key.Split('+').Select(static part => part.Length == 64 ? part[..12] : part));

    private static string Detail(V3CorpusMount mount, string work, DateOnly day, string language)
    {
        var envelope = EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = $"/lu-legilux/{work}", date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), language })
            .GetAwaiter().GetResult();
        if (envelope.Refusal is not { } refusal)
        {
            return "as_of answers " + string.Join('+', envelope.Result!.Value.GetProperty("states").EnumerateArray().Select(static state => state.GetProperty("state_sha256").GetString()![..12]));
        }

        var payload = refusal.HelpfulPayload;
        string Member(string name) => payload.TryGetProperty(name, out var value) ? value.ToString() : "-";
        return refusal.Code switch
        {
            "no_version_for_date" => $"reason {Member("reason")}, hole {Member("hole_from")}..{Member("hole_to")}, closed_on {Member("closed_on")}, nearest {Member("nearest_earlier")}/{Member("nearest_later")}",
            "text_withheld" or "text_not_available" => $"textless {Member("text_status")} {Member("state_kind")} {Member("applicability_date")}..{Member("applicable_to")}",
            "ambiguous_version" => $"candidates {Member("candidate_text_statuses")} {Member("candidate_applicability_dates")}",
            _ => refusal.Code,
        };
    }
}
