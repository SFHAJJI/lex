using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Release 3 relations slice's measurement: index 10's relation rows over Luxembourg run 15's custody, and R72 over the acts
/// derive-r1 serves, without a derive. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like S2's and S3a's probes. It runs only with <c>LEX_LU_RELATIONS_PROBE=1</c>;
/// <c>LEX_LU_RELATIONS_PROBE_CUSTODY</c> names run 15's custody root (read by digest through S2's read-only decorator, every
/// object's digest checked), <c>LEX_LU_RELATIONS_PROBE_INDEX</c> derive-r1's Luxembourg index (opened read-only:
/// its <c>work_facts</c> are run 15's typed assertions, its <c>states</c> the catalogue states of the acts it serves) and
/// <c>LEX_LU_RELATIONS_PROBE_OUT</c> the summary file it writes. Nothing else is written and nothing goes over the network.
/// </para>
/// <para>
/// WHAT IT RUNS. Run 15's acquisition journal names its query families; the relation family (set G) and the census (set S) are
/// read from their cover checkpoints, leaves and pages, both passes, down to each page's retained body. A relation row whose
/// subject the census does not hold, or that is not an exact publisher URI, is excluded as the adapter excludes it; every other
/// row is a resolved, accepted relation of the run's one observation (the scope resolver accepts every predicate but
/// consolidates by its rule, and consolidates is not projected). The production projection
/// (<see cref="LuxembourgIndexBuilder.ProjectPublisherRelations"/>) makes the rows, and R72's rule
/// (<see cref="V3CorpusMount.AmendedAfterLatestHeldConsolidation"/>) reads them for each act the index holds as published, over the
/// held consolidation and code states of the act and of every act its received impact records name as integrating its changes, as
/// the server reads a dossier.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgPublisherRelationsRun15Probe
{
    private const string EnableVariable = "LEX_LU_RELATIONS_PROBE";
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";

    [TestMethod]
    public async Task IndexTensRelationRowsAndR72OverRun15AreMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_LU_RELATIONS_PROBE=1, LEX_LU_RELATIONS_PROBE_CUSTODY, LEX_LU_RELATIONS_PROBE_INDEX and LEX_LU_RELATIONS_PROBE_OUT for the read-only run-15 measurement.");
        }

        var custody = Environment.GetEnvironmentVariable("LEX_LU_RELATIONS_PROBE_CUSTODY")!;
        var indexPath = Environment.GetEnvironmentVariable("LEX_LU_RELATIONS_PROBE_INDEX")!;
        var output = Environment.GetEnvironmentVariable("LEX_LU_RELATIONS_PROBE_OUT")!;
        Assert.IsTrue(Directory.Exists(custody), "the custody root");
        Assert.IsTrue(File.Exists(indexPath), "derive-r1's Luxembourg index");
        var clock = Stopwatch.StartNew();
        var store = new LuxembourgAknInventoryV4Run15Probe.ReadOnlyCustody(FileSystemCustodyStore.WithBrotliCompression(custody));
        async Task<JsonNode> Json(string digest) =>
            JsonNode.Parse((await CustodyRestore.ReadByDigestCheckedAsync(store, digest, CancellationToken.None)).Span)!;

        // ---- The journal: the query families and the run's one observation. ----
        var journal = Directory.GetFiles(custody, "acquisition-progress-*.jsonl").Single();
        var families = new List<(string Set, string Key, string Checkpoint)>();
        string? observation = null;
        foreach (var line in File.ReadLines(journal))
        {
            var entry = JsonNode.Parse(line)!;
            var phase = (string)entry["phase"]!;
            if (phase == "lu-vocabulary")
            {
                observation = (string)entry["payload"]!["observation"]!["sha256"]!;
            }
            else if (phase == "lu-query-family" && (string)entry["payload"]!["set"]! is "G" or "S")
            {
                families.Add(((string)entry["payload"]!["set"]!, (string)entry["key"]!, (string)entry["payload"]!["checkpoint"]!["sha256"]!));
            }
        }

        Assert.IsNotNull(observation, "the journal names the run's observation");
        async Task<(List<string[]> First, List<string[]> Second)> RowsOf(string checkpoint, string[] variables)
        {
            var cover = await Json(checkpoint);
            var first = new List<string[]>();
            var second = new List<string[]>();
            foreach (var leafRef in cover["leaves"]!.AsArray())
            {
                var leaf = await Json((string)leafRef!["sha256"]!);
                foreach (var (pass, rows) in new[] { ("pages_a", first), ("pages_b", second) })
                {
                    foreach (var page in leaf[pass]!["pages"]!.AsArray())
                    {
                        var evidence = await Json((string)page!["evidence"]!["http_evidence_ref"]!["sha256"]!);
                        var hops = evidence["hops"]!.AsArray();
                        var body = await Json((string)hops[^1]!["sha256"]!);
                        foreach (var binding in body["results"]!["bindings"]!.AsArray())
                        {
                            rows.Add(variables.Select(variable => binding![variable] is { } term
                                ? $"{(string)term["type"]!}|{(string)term["value"]!}"
                                : "unbound|").ToArray());
                        }
                    }
                }
            }

            return (first, second);
        }

        var census = new HashSet<string>(StringComparer.Ordinal);
        var relationRows = new List<string[]>();
        var summary = new JsonObject { ["observation_sha256"] = observation };
        var familyCounts = new JsonObject();
        foreach (var (set, key, checkpoint) in families)
        {
            var (first, second) = await RowsOf(checkpoint, set == "S" ? ["key_1"] : ["subject", "predicate", "object"]);
            var same = first.Select(static row => string.Join("\t", row)).Order(StringComparer.Ordinal)
                .SequenceEqual(second.Select(static row => string.Join("\t", row)).Order(StringComparer.Ordinal), StringComparer.Ordinal);
            Assert.IsTrue(same, $"{key}: both passes delivered the same rows");
            familyCounts[key] = first.Count;
            if (set == "S")
            {
                foreach (var row in first) census.Add(row[0][(row[0].IndexOf('|') + 1)..]);
            }
            else
            {
                relationRows.AddRange(first);
            }
        }

        summary["family_rows"] = familyCounts;
        summary["census_subjects"] = census.Count;
        var excluded = new JsonObject();
        var relations = new List<LuxembourgIndexBuilder.ProjectionRelation>();
        foreach (var row in relationRows)
        {
            var terms = row.Select(static term => (Kind: term[..term.IndexOf('|')], Value: term[(term.IndexOf('|') + 1)..])).ToArray();
            string? cause = terms.Any(static term => term.Kind != "uri") ? "term_not_iri"
                : !census.Contains(terms[0].Value) ? "relation_subject_not_in_census"
                : !Uri.TryCreate(terms[0].Value, UriKind.Absolute, out _) || terms[0].Value.Any(static character => character > '~') ? "subject_not_a_publisher_uri"
                : null;
            if (cause is not null)
            {
                excluded[cause] = ((int?)excluded[cause] ?? 0) + 1;
                continue;
            }

            relations.Add(new(terms[0].Value, terms[1].Value, terms[2].Value, observation!));
        }

        summary["relation_rows"] = relationRows.Count;
        summary["relation_rows_excluded"] = excluded;
        summary["relation_rows_by_predicate"] = new JsonObject(relations
            .GroupBy(static relation => relation.PredicateIri.StartsWith(Jolux, StringComparison.Ordinal) ? relation.PredicateIri[Jolux.Length..] : relation.PredicateIri)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new KeyValuePair<string, JsonNode?>(group.Key, group.Count())));

        // ---- The catalogue: run 15's typed assertions and derive-r1's states, read-only. ----
        var facts = new List<LuxembourgIndexBuilder.WorkFactRow>();
        var states = new List<(string WorkKey, string WorkIri, string LegalResource, string Kind, string Text, string? Publication)>();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = indexPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
               }.ToString()))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts " +
                    "WHERE predicate IN ('publicationDate','dateEntryInForce')";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    facts.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                        reader.GetString(5), reader.GetString(6), reader.GetString(7)));
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT work_key,publisher_work_iri,publisher_legal_resource_iri,state_kind,text_status,publication_date FROM states";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    states.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
            }
        }

        SqliteConnection.ClearAllPools();
        var rows = LuxembourgIndexBuilder.ProjectPublisherRelations(relations, [], facts);
        summary["publisher_relations"] = new JsonObject(rows.Relations
            .GroupBy(static row => row.EdgeType, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new KeyValuePair<string, JsonNode?>(group.Key, new JsonObject
            {
                ["rows"] = group.Count(),
                ["acting_acts"] = group.Select(static row => row.ActingIri).Distinct(StringComparer.Ordinal).Count(),
                ["acted_on"] = group.Select(static row => row.ActedOnIri).Distinct(StringComparer.Ordinal).Count(),
                ["with_publication_date"] = group.Count(static row => row.ActingPublicationDate is not null),
                ["with_entry_into_force"] = group.Count(static row => row.ActingEntryIntoForce is not null),
            })));
        summary["publisher_impacts"] = new JsonObject
        {
            ["rows"] = rows.Impacts.Length,
            ["with_from_and_to"] = rows.Impacts.Count(static row => row.FromLegalResourceIri is not null && row.ToLegalResourceIri is not null),
            ["naming_the_integrating_consolidation"] = rows.Impacts.Count(static row => row.ConsolidatedByIri is not null),
            ["with_an_ambiguous_part"] = rows.Impacts.Count(static row => row.AmbiguousPartsJson != "[]"),
            ["with_publication_date"] = rows.Impacts.Count(static row => row.ActingPublicationDate is not null),
        };

        // ---- R72 over the acts the index serves: each held as published, over the held versions, as a dossier reads it. ----
        var byObject = rows.Relations.ToLookup(static row => row.ObjectIri, StringComparer.Ordinal);
        var impactsTo = rows.Impacts.Where(static row => row.ToLegalResourceIri is not null && row.FromLegalResourceIri is not null)
            .ToLookup(static row => row.ToLegalResourceIri!, StringComparer.Ordinal);
        var factsOfWork = states
            .Select(static state => new V3CorpusMount.ConsolidationFacts(state.WorkKey, state.LegalResource, state.Kind, state.Text, state.Publication))
            .ToLookup(static state => state.WorkKey, StringComparer.Ordinal);
        IReadOnlyList<V3CorpusMount.ConsolidationFacts> StatesOf(string key) => factsOfWork[key].ToArray();
        var statuses = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int served = 0, amended = 0, amendingEdges = 0, repealed = 0, noticeWithoutConsolidation = 0, noticeAfterConsolidation = 0;
        int noticeAfterIntegrating = 0, withIntegratingActs = 0, integratedActs = 0, undecidedActs = 0;
        foreach (var work in states.GroupBy(static state => state.WorkKey, StringComparer.Ordinal))
        {
            if (!work.Any(static state => state.Kind == "as_published" && state.Text == "held"))
            {
                continue;
            }

            served++;
            var own = work.SelectMany(static state => new[] { state.WorkIri, state.WorkIri + "/jo", state.LegalResource })
                .ToHashSet(StringComparer.Ordinal);
            var ownWork = work.First().WorkIri;
            var inbound = own.SelectMany(iri => byObject[iri]).Distinct().ToArray();
            var amending = inbound
                .Where(row => row.EdgeType == LuxembourgIndexBuilder.EdgeModifies &&
                              !string.Equals(Lex.V3.Ingest.Names.PrivateIndividuals.LuxembourgWorkIriOf(row.SubjectIri), ownWork, StringComparison.Ordinal))
                .Select(static row => new V3CorpusMount.AmendingAct(row.ActingIri, row.ActingPublicationDate, row.EvidenceSha256))
                .ToArray();
            if (amending.Length > 0)
            {
                amended++;
                amendingEdges += amending.Length;
            }

            if (inbound.Any(static row => row.EdgeType == LuxembourgIndexBuilder.EdgeRepeals))
            {
                repealed++;
            }

            var received = own.SelectMany(iri => impactsTo[iri]).Distinct()
                .Select(static row => new V3CorpusMount.ReceivedImpact(
                    row.FromLegalResourceIri!, row.ConsolidatedByIri, row.AmbiguousPartsJson.Contains("jolux:impactConsolidatedBy", StringComparison.Ordinal)))
                .ToArray();
            var reading = V3CorpusMount.AmendedAfterLatestHeldConsolidation(work.Key, StatesOf(work.Key), StatesOf, amending, received);
            statuses[reading.Status] = statuses.GetValueOrDefault(reading.Status) + 1;
            withIntegratingActs += reading.IntegratingWorkKeys.Count > 0 ? 1 : 0;
            integratedActs += reading.AmendingActsIntegrated;
            undecidedActs += reading.AmendingActsIntegrationUndecided;
            if (reading.Status == V3CorpusMount.AmendedAfterNotice)
            {
                if (reading.LatestHeld is null) noticeWithoutConsolidation++;
                else if (reading.LatestHeld.WorkKey != work.Key) noticeAfterIntegrating++;
                else noticeAfterConsolidation++;
            }
        }

        summary["r72"] = new JsonObject
        {
            ["served_acts"] = served,
            ["amended_acts"] = amended,
            ["amending_edges"] = amendingEdges,
            ["repealed_acts"] = repealed,
            ["statuses"] = new JsonObject(statuses.Select(static entry => new KeyValuePair<string, JsonNode?>(entry.Key, entry.Value))),
            ["notice_with_no_held_consolidation"] = noticeWithoutConsolidation,
            ["notice_after_the_latest_held_consolidation"] = noticeAfterConsolidation,
            ["notice_after_the_latest_held_integrating_version"] = noticeAfterIntegrating,
            ["acts_with_an_integrating_act"] = withIntegratingActs,
            ["amending_acts_integrated_in_a_held_version"] = integratedActs,
            ["amending_acts_whose_integration_is_undecided"] = undecidedActs,
        };
        summary["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1);
        summary["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
        await File.WriteAllTextAsync(output, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
