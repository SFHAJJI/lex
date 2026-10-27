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

        // The interval oracle's cases against the reader (what the gate asks now).
        using (var oracle = V3MountedGatesTests.IntervalOracle.Open(directory!))
        {
            if (oracle is not null)
            {
                var arms = new List<(string Name, string? Language, TemporalArm Arm, Func<string, string?, DateOnly, string> Expected, string[] Works)>();
                foreach (var language in timelines.Select(static value => value.Language).Distinct().Order(StringComparer.Ordinal))
                {
                    var held = timelines.Where(value => value.Language == language).Select(static value => value.WorkKey).Distinct().Order(StringComparer.Ordinal).ToArray();
                    arms.Add(($"interval as_of in {language}", language, V3MountedGatesTests.IntervalAsOfArm(mount, language), oracle.AsOf, held));
                    arms.Add(($"interval in_force_on in {language}", language, V3MountedGatesTests.IntervalInForceOnArm(mount, language), oracle.InForceOn, held));
                }

                var all = timelines.Select(static value => value.WorkKey).Distinct().Order(StringComparer.Ordinal).ToArray();
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
