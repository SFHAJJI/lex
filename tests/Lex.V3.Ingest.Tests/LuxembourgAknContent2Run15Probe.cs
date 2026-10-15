using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S3a's measurement: content /2 over inventory /4 over every held body of Luxembourg run 15, beside
/// content /1 over the same articles, counted by kind and admission class. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like S2's <see cref="LuxembourgAknInventoryV4Run15Probe"/>, over the same
/// inputs. It runs only with <c>LEX_LU_CONTENT2_PROBE=1</c>, and <c>LEX_LU_CONTENT2_PROBE_ROOT</c>
/// names a directory holding <c>run15-held.jsonl</c>, <c>run15-date-applicability.json</c>,
/// <c>ref_v4.jsonl</c> (read only for the design's admission classes) and a <c>probe.json</c> naming
/// the run's custody root. Custody is read by digest and never written; nothing goes over the
/// network and R0 does not run. It writes its two result files into the probe root.
/// </para>
/// <para>
/// WHAT IT COUNTS. Inventory /4 runs once over the whole population; content /2 and content /1 then
/// read its outcomes in chunks of a few bodies, so the token streams of 300,000 articles are never
/// held at once. "Beyond /1" is an article /2 admits that keeps at least one index term: under /1,
/// over the same article, that block is a node outside the AKN namespace, so a typed gap. /1 is run
/// here only as a measuring device: wherever its own article selection agrees with /4's, every
/// article /1 admits must read under /2 to the same disposition and tokens with no index term, and
/// every other /1 gap must be /2's gap with the same detail; the probe counts any article that does
/// not.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgAknContent2Run15Probe
{
    private const string EnableVariable = "LEX_LU_CONTENT2_PROBE";
    private const string RootVariable = "LEX_LU_CONTENT2_PROBE_ROOT";
    private const int ChunkBodies = 32;
    private const string Namespace0 = "unsupported legal-content node or namespace";

    [TestMethod]
    public async Task Content2OverRun15IsMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                "Set LEX_LU_CONTENT2_PROBE=1 and LEX_LU_CONTENT2_PROBE_ROOT for the read-only run-15 measurement.");
        }

        var root = Environment.GetEnvironmentVariable(RootVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(root), RootVariable + " names the probe inputs.");
        var custody = (string)JsonNode.Parse(File.ReadAllText(Path.Combine(root, "probe.json")))!["custody"]!;
        // S2's read-only decorator, so the probes share one custody store the conformance sweep accounts for.
        var store = new LuxembourgAknInventoryV4Run15Probe.ReadOnlyCustody(FileSystemCustodyStore.WithBrotliCompression(custody));
        var clock = Stopwatch.StartNew();

        var template = await new EuAcquisitionTestFixture.EuInMemoryCustodyStore().CreateAsync(
            "template"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var policy = template.PolicyEvidence;
        var held = new List<(LuxembourgHeldPopulationBuilder.Member Member, DurableBlobWriteReceipt Receipt)>();
        foreach (var line in File.ReadLines(Path.Combine(root, "run15-held.jsonl")))
        {
            var row = JsonNode.Parse(line)!;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                (string)row["body_sha256"]!,
                (long)row["body_byte_length"]!,
                CustodyClass.NightlyFloor90d);
            var receipt = new DurableBlobWriteReceipt(
                template.Schema,
                reference,
                new CustodyPolicyEvidence(
                    policy.Schema, reference, policy.VerificationProfile, policy.PolicyKey,
                    policy.Protection, policy.ObservedAt, policy.ProtectedUntil));
            held.Add((new LuxembourgHeldPopulationBuilder.Member(
                (string)row["root_iri"]!,
                [],
                LuxembourgHeldPopulationBuilder.Token((string)row["format"]!),
                (int)row["source_ordinal"]!,
                (string)row["expression_iri"]!,
                (string)row["manifestation_iri"]!,
                (string)row["item_iri"]!,
                (string)row["language_iri"]!), receipt));
        }
        var population = LuxembourgHeldPopulationBuilder.Create(held);
        var facts = new List<(string SubjectIri, string RawLexicalValue)>();
        foreach (var (subject, values) in JsonNode.Parse(
                     File.ReadAllText(Path.Combine(root, "run15-date-applicability.json")))!["date_applicability"]!.AsObject())
        {
            foreach (var value in values!.AsArray())
            {
                facts.Add((subject, (string)value!));
            }
        }
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(root, "ref_v4.jsonl")))
        {
            var row = JsonNode.Parse(line)!;
            groups[(string)row["root"]!] = (string?)row["candidate"] ?? "never";
        }

        var inventory = await new LuxembourgAknArticleInventoryProducerV4(store).RunAsync(
            population, LuxembourgAknArticleInventoryProducerV4.CatalogueStarts(facts), CancellationToken.None);
        var inventoried = clock.Elapsed;

        var tallies = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var disagreements = new List<string>();
        var bodiesCompared = 0;
        using (var articles = new StreamWriter(Path.Combine(root, "csharp_content2.jsonl"), false, new UTF8Encoding(false)))
        {
            var ordered = inventory.Outcomes.OrderBy(static outcome => outcome.Input.ObjectOrdinal).ToArray();
            for (var start = 0; start < ordered.Length; start += ChunkBodies)
            {
                var chunk = ordered.Skip(start).Take(ChunkBodies).ToArray();
                var part = new LuxembourgAknArticleInventoryPopulation(
                    population, chunk, LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256);
                var two = await new LuxembourgAknLegalContentProfile2Producer(store).RunAsync(part, CancellationToken.None);
                var one = await new LuxembourgAknLegalContentProfileProducer(store).RunAsync(part, CancellationToken.None);
                foreach (var body in chunk)
                {
                    if (body.Disposition != LuxembourgAknArticleInventoryDisposition.Inventoried)
                    {
                        continue;
                    }

                    var rootIri = body.Input.CorpusRecord.ObjectRef.PublisherUri;
                    var kind = Kind(rootIri);
                    var group = groups.TryGetValue(rootIri, out var found) ? found : "never";
                    var twoOutcomes = two.Outcomes.Where(outcome => ReferenceEquals(outcome.SourceInventoryOutcome, body)).ToArray();
                    var oneOutcomes = one.Outcomes.Where(outcome => ReferenceEquals(outcome.SourceInventoryOutcome, body)).ToArray();
                    var comparable = oneOutcomes.All(static outcome => outcome.Disposition is not (
                        LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch or
                        LuxembourgAknLegalContentDisposition.XmlRejected));
                    bodiesCompared += comparable ? 1 : 0;
                    for (var index = 0; index < twoOutcomes.Length; index++)
                    {
                        var outcome = twoOutcomes[index];
                        var terms = outcome.Article?.IndexTerms?.Count ?? 0;
                        var admitted = outcome.Disposition is LuxembourgAknLegalContentDisposition.Admitted
                            or LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence;
                        foreach (var scope in new[] { kind + "|all", kind + "|" + group, "all|all" })
                        {
                            Add(tallies, scope + "|articles");
                            Add(tallies, scope + "|content2_" + ContractWire.NameOf(outcome.Disposition));
                            if (admitted && terms != 0)
                            {
                                Add(tallies, scope + "|beyond_content1_admitted_with_index_terms");
                            }
                            if (admitted && terms == 0)
                            {
                                Add(tallies, scope + "|admitted_without_index_terms");
                            }
                        }
                        articles.Write(new JsonObject
                        {
                            ["root"] = rootIri,
                            ["id"] = outcome.Coordinate!.PublisherId,
                            ["kind"] = kind,
                            ["group"] = group,
                            ["disp"] = ContractWire.NameOf(outcome.Disposition),
                            ["detail"] = outcome.Detail,
                            ["terms"] = terms,
                            ["content1_disp"] = comparable ? ContractWire.NameOf(oneOutcomes[index].Disposition) : null,
                            ["content1_detail"] = comparable ? oneOutcomes[index].Detail : null,
                        }.ToJsonString());
                        articles.Write('\n');

                        if (!comparable)
                        {
                            continue;
                        }
                        var first = oneOutcomes[index];
                        var oneAdmitted = first.Disposition is LuxembourgAknLegalContentDisposition.Admitted
                            or LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence;
                        if (oneAdmitted &&
                            (first.Disposition != outcome.Disposition || terms != 0 ||
                             !string.Equals(
                                 LuxembourgIndexBuilder.TokensJson(first.Article!.Tokens),
                                 LuxembourgIndexBuilder.TokensJson(outcome.Article!.Tokens),
                                 StringComparison.Ordinal)))
                        {
                            disagreements.Add(rootIri + " " + outcome.Coordinate.PublisherId + " | /1 admitted, /2 differs");
                        }
                        else if (!oneAdmitted && first.Detail != Namespace0 &&
                                 (outcome.Disposition != first.Disposition || outcome.Detail != first.Detail))
                        {
                            disagreements.Add(rootIri + " " + outcome.Coordinate.PublisherId + " | /1 gap " + first.Detail + ", /2 " + outcome.Detail);
                        }
                        else if (!oneAdmitted && first.Detail == Namespace0)
                        {
                            Add(tallies, kind + "|all|content1_namespace_gap_then_content2_" + (admitted ? "admitted" : "gap"));
                        }
                    }
                }
            }
        }
        var measured = clock.Elapsed;

        var summary = new StringBuilder();
        summary.Append("inventory /4 ").Append(inventory.RuleProfileSha256).Append(" | content /2 ")
            .Append(LuxembourgAknLegalContentProfile2Producer.RuleProfileSha256).Append('\n');
        summary.Append("bodies compared with /1 over the same articles ").Append(bodiesCompared)
            .Append(" | disagreements ").Append(disagreements.Count).Append('\n');
        foreach (var disagreement in disagreements.Take(40))
        {
            summary.Append("  DISAGREE ").Append(disagreement).Append('\n');
        }
        foreach (var (key, value) in tallies)
        {
            summary.Append(key).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        summary.Append("time: inventory /4 ").Append(inventoried.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" s, content /2 and /1 ").Append((measured - inventoried).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" s; peak working set ")
            .Append((Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)).ToString(CultureInfo.InvariantCulture))
            .Append(" MB\n");
        File.WriteAllText(Path.Combine(root, "csharp_content2.summary.txt"), summary.ToString(), new UTF8Encoding(false));
        Console.WriteLine(summary.ToString());

        Assert.AreEqual(0, disagreements.Count, "articles where /2 is not /1 apart from the index terms:\n" +
            string.Join("\n", disagreements.Take(40)));
    }

    private static void Add(SortedDictionary<string, long> tallies, string key) =>
        tallies[key] = tallies.TryGetValue(key, out var value) ? value + 1 : 1;

    /// <summary>The reference's kind names: <c>jo</c>, <c>consolide</c>, <c>code</c> or <c>other</c>.</summary>
    private static string Kind(string rootIri) => LuxembourgEliState.KindOf(rootIri) switch
    {
        "act" => "jo",
        "consolidation" => "consolide",
        "code" => "code",
        _ => "other",
    };
}
