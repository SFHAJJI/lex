using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S6's measurement: index 9's catalogue-state projection over Luxembourg run 15 under Release 1's rules (rights as
/// acquired, inventory /4, content /2), without a derive. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like S2's and S3a's probes. It runs only with <c>LEX_LU_INDEX9_PROBE=1</c>;
/// <c>LEX_LU_INDEX9_PROBE_ROOT</c> names a directory holding S3a's <c>run15-held.jsonl</c> (every held body: its
/// selected WEMI and rights), S2's <c>csharp_v4.jsonl</c> (inventory /4 per body; or <c>ref_v4.jsonl</c>, the reference
/// implementation's, which S2 matched on every one of the 11,205 bodies) and S3a's <c>csharp_content2.jsonl</c>
/// (content /2 per main-body article); <c>LEX_LU_INDEX9_PROBE_FACTS</c> names derive-c's Luxembourg index, opened read-only,
/// whose <c>work_facts</c> are run 15's typed assertions, the catalogue the projection reads. Nothing is written but the
/// summary in the probe root; nothing goes over the network.
/// </para>
/// <para>
/// WHAT IT RUNS. The production projection (<see cref="LuxembourgIndexBuilder.ProjectCatalogueStates"/>) over bodies made
/// from those outputs: a body is acquired when its rights are <c>agreed_same_run_cc_by</c> and withheld otherwise; an
/// article is admitted when content /2 admitted it in an acquired body. The corpus records of bodies not held are not in
/// these inputs (they carry no IRI in derive-c's members), so a textless state with no held body reads
/// <c>not_a_corpus_member</c> here where the build would name its record's body axis: the counts of rows, works, codes
/// and intervals do not depend on it, the reasons of those rows do.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgIndexNineRun15Probe
{
    private const string EnableVariable = "LEX_LU_INDEX9_PROBE";
    private const string RootVariable = "LEX_LU_INDEX9_PROBE_ROOT";
    private const string FactsVariable = "LEX_LU_INDEX9_PROBE_FACTS";

    [TestMethod]
    public void IndexNinesStatesOverRun15AreMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_LU_INDEX9_PROBE=1, LEX_LU_INDEX9_PROBE_ROOT and LEX_LU_INDEX9_PROBE_FACTS for the read-only run-15 measurement.");
        }

        var root = Environment.GetEnvironmentVariable(RootVariable);
        var factsPath = Environment.GetEnvironmentVariable(FactsVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(root), RootVariable + " names the probe inputs.");
        Assert.IsTrue(File.Exists(factsPath), FactsVariable + " names derive-c's Luxembourg index.");
        var clock = Stopwatch.StartNew();

        // ---- The catalogue: run 15's typed assertions, as derive-c's index holds them. ----
        var facts = new List<LuxembourgIndexBuilder.WorkFactRow>();
        string[] noState;
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = factsPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts " +
                    "WHERE predicate IN ('dateApplicability','dateEndApplicability','dateEntryInForce','publicationDate','isMemberOf','isRealizedBy','language') " +
                    "OR (predicate='rdf:type' AND subject_iri GLOB 'http://data.legilux.public.lu/eli/etat/leg/code/*')";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    facts.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                        reader.GetString(5), reader.GetString(6), reader.GetString(7)));
                }
            }

            // derive-c's expressions with articles and no state (index 8 over inventory /3: their files state no date).
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT DISTINCT a.expression_iri FROM articles a WHERE NOT EXISTS (SELECT 1 FROM states s WHERE s.expression_iri=a.expression_iri) ORDER BY 1";
                using var reader = command.ExecuteReader();
                var values = new List<string>();
                while (reader.Read()) values.Add(reader.GetString(0));
                noState = values.ToArray();
            }
        }

        SqliteConnection.ClearAllPools();
        var catalogueFacts = LuxembourgIndexBuilder.CatalogueFacts.From(facts);
        facts.Clear();

        // ---- The held bodies, what inventory /4 and content /2 made of them. ----
        var inventory = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var inventoryFile = File.Exists(Path.Combine(root, "csharp_v4.jsonl")) ? "csharp_v4.jsonl" : "ref_v4.jsonl";
        foreach (var line in File.ReadLines(Path.Combine(root, inventoryFile)))
        {
            var row = JsonNode.Parse(line)!;
            inventory[(string)row["root"]!] = row;
        }

        var admitted = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dispositions = new Dictionary<string, SortedDictionary<string, long>>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(root, "csharp_content2.jsonl")))
        {
            var row = JsonNode.Parse(line)!;
            var body = (string)row["root"]!;
            var disposition = (string)row["disp"]!;
            if (!dispositions.TryGetValue(body, out var counted)) dispositions[body] = counted = new(StringComparer.Ordinal);
            counted[disposition] = counted.GetValueOrDefault(disposition) + 1;
            if (disposition == "admitted")
            {
                if (!admitted.TryGetValue(body, out var ids)) admitted[body] = ids = [];
                ids.Add((string)row["id"]!);
            }
        }

        var bodies = new List<LuxembourgIndexBuilder.CatalogueBody>();
        var records = new List<LuxembourgIndexBuilder.CatalogueRecord>();
        var articles = new List<LuxembourgIndexBuilder.ArticleRow>();
        foreach (var line in File.ReadLines(Path.Combine(root, "run15-held.jsonl")))
        {
            var row = JsonNode.Parse(line)!;
            var legalResource = (string)row["root_iri"]!;
            var objectRef = (string)row["object_ref_sha256"]!;
            var rights = (string)row["rights_disposition"]!;
            var outcome = rights == "agreed_same_run_cc_by" ? "acquired" : "rights_withheld";
            var expression = (string)row["expression_iri"]!;
            var languageIri = (string)row["language_iri"]!;
            var language = languageIri[(languageIri.LastIndexOf('/') + 1)..].ToLowerInvariant();
            var inv = inventory[legalResource];
            var findings = (inv["findings"] as JsonArray ?? []).Select(static finding => $"{(string)finding![0]!}:{(string)finding[1]!}").ToArray();
            bodies.Add(new(
                objectRef, legalResource, expression, language, outcome, rights, (string)inv["disp"]!, (string?)inv["detail"],
                new string('4', 64), (string?)inv["cls"], (string?)inv["in_date"], findings,
                dispositions.TryGetValue(legalResource, out var counted) ? counted.ToArray() : []));
            records.Add(new(legalResource, objectRef, outcome, "accepted_selected"));
            if (outcome == "acquired" && (string)inv["disp"]! == "inventoried" && admitted.TryGetValue(legalResource, out var ids))
            {
                for (var index = 0; index < ids.Count; index++)
                {
                    articles.Add(new(
                        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{legalResource}#{index}#{ids[index]}"))),
                        objectRef, expression, ids[index], null, null, language, new string('c', 64), "", "[]"));
                }
            }
        }

        inventory.Clear();
        admitted.Clear();
        var projected = LuxembourgIndexBuilder.ProjectCatalogueStates(
            articles.OrderBy(static article => article.ArticleIdentitySha256, StringComparer.Ordinal).ToArray(),
            catalogueFacts, bodies, records, "2026-10-12");
        var states = projected.States;
        var held = states.Where(static state => state.Held).ToArray();
        var textless = states.Where(static state => !state.Held).ToArray();
        string KindOf(LuxembourgIndexBuilder.StateRow state) => state.StateKind!;
        var codes = held.Where(static state => state.StateKind == LuxembourgIndexBuilder.KindCode)
            .GroupBy(static state => state.WorkKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var heldByWork = held.Where(static state => !LuxembourgIndexBuilder.IsTranslationLanguage(state.Language))
            .GroupBy(static state => state.WorkKey, StringComparer.Ordinal).ToArray();
        var rowsByRoot = states.GroupBy(static state => state.PublisherLegalResourceIri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var deriveC209 = noState
            .Select(static expression => expression[..expression.LastIndexOf('/')])
            .Select(legalResource => rowsByRoot.TryGetValue(legalResource, out var rows)
                ? string.Join(",", rows.Select(static row => $"{row.TextStatus}/{row.DateRole}"))
                : "no row")
            .GroupBy(static value => value, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        string[] five = ["loi-2011-11-12-n1", "loi-2019-03-05-a129", "rgd-2015-12-18-n10", "rgd-2018-06-20-a507", "rgd-2019-09-12-a609"];
        var odd = held.Where(state => five.Contains(state.WorkKey, StringComparer.Ordinal) && state.StateKind == LuxembourgIndexBuilder.KindAsPublished)
            .Select(static state => $"{state.WorkKey} {state.ApplicabilityDate} {state.DateRole} entry_into_force={state.EntryIntoForce ?? "-"} in_file={state.InFileDate ?? "-"}")
            .ToArray();
        var summary = new JsonObject
        {
            ["schema"] = "lex-v3-lu-index9-run15-probe/1",
            ["inventory_input"] = inventoryFile,
            ["catalogue_facts_read"] = catalogueFacts.Subjects.Count(),
            ["held_bodies"] = bodies.Count,
            ["admitted_articles"] = articles.Count,
            ["rows"] = states.Length,
            ["held_rows"] = held.Length,
            ["held_rows_by_kind"] = Counted(held.Select(KindOf)),
            ["held_translations"] = held.Count(static state => LuxembourgIndexBuilder.IsTranslationLanguage(state.Language)),
            ["textless_rows"] = textless.Length,
            ["textless_rows_by_kind"] = Counted(textless.Select(KindOf)),
            ["textless_rows_by_status"] = Counted(textless.Select(static state => state.TextStatus!)),
            ["textless_rows_naming_no_expression"] = textless.Count(static state => state.ExpressionIri.Length == 0),
            ["works_with_a_held_row"] = held.Select(static state => state.WorkKey).Distinct(StringComparer.Ordinal).Count(),
            ["works_with_two_or_more_held_french_states"] = heldByWork.Count(static group => group.Count() >= 2),
            ["held_code_states"] = codes.Values.Sum(),
            ["held_code_states_by_code"] = new JsonObject(codes.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
            ["held_rows_by_role"] = Counted(held.Select(static state => state.DateRole!)),
            ["held_acts_whose_date_is_not_their_entry_into_force"] = held.Count(static state =>
                state.StateKind == LuxembourgIndexBuilder.KindAsPublished && state.EntryIntoForce is not null &&
                !string.Equals(state.EntryIntoForce, state.ApplicabilityDate, StringComparison.Ordinal)),
            ["rows_by_boundary"] = Counted(states.Select(static state => state.Boundary!)),
            ["intervals_by_kind"] = Counted(projected.Intervals.Select(static interval => interval.Kind)),
            ["provisional_rows"] = states.Count(static state => state.Provisional),
            ["derive_c_expressions_with_articles_and_no_state"] = noState.Length,
            ["those_by_index9_row"] = new JsonObject(deriveC209.Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
            ["the_five_acts_not_dated_by_entry_into_force_in_derive_c"] = new JsonArray(odd.Select(static value => (JsonNode?)value).ToArray()),
            ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
            ["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024),
        };
        var text = summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "index9-probe.summary.json"), text);
        Console.WriteLine(text);
        Assert.IsGreaterThan(0, held.Length);
    }

    private static JsonObject Counted(IEnumerable<string> values) =>
        new(values.GroupBy(static value => value, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count())));
}
