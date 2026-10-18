using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S3b's measurement: content /2 and content /3 over inventory /4 over every held body of Luxembourg run 15, counted by
/// shape: the articles /3 admits that /2 refused, and the articles still refused and why. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like S3a's <see cref="LuxembourgAknContent2Run15Probe"/>, over the same inputs. It runs only
/// with <c>LEX_LU_CONTENT3_PROBE=1</c>, and <c>LEX_LU_CONTENT3_PROBE_ROOT</c> names a directory holding
/// <c>run15-held.jsonl</c>, <c>run15-date-applicability.json</c>, <c>ref_v4.jsonl</c> (read only for the admission
/// classes) and a <c>probe.json</c> naming the run's custody root. Custody is read by digest through S2's read-only
/// decorator and never written; nothing goes over the network and R0 does not run. It writes its result files into the
/// probe root: <c>csharp_content3.jsonl</c> (one line per main-body article /4 inventoried) and
/// <c>csharp_content3.summary.json</c>.
/// </para>
/// <para>
/// WHAT IT COUNTS. Inventory /4 runs once over the whole population; content /2 and /3 then read its outcomes in chunks of
/// a few bodies, so the token streams of 300,000 articles are never held at once. An article's shape under /2 is its /2
/// detail (the first element /2 cannot read); "recovered" is an article /3 admits and /2 refused. The S6 comparison
/// (index /9's probe: 7,704 of 126,059 articles missing from held states under Release 1's rules) is the articles /2
/// refuses in the bodies admitted today that hold at least one article /2 admits. Every article /2 admits must read
/// under /3 to the same disposition and stored tokens, but one: an article whose wording an attribute marks deleted
/// (<c>style="del"</c>, <c>class="strike"</c>), which /2 reads as in force and /3 refuses (the review of #981, F2), is
/// counted apart; the probe counts any other that does not.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgAknContent3Run15Probe
{
    private const string EnableVariable = "LEX_LU_CONTENT3_PROBE";
    private const string RootVariable = "LEX_LU_CONTENT3_PROBE_ROOT";
    private const int ChunkBodies = 32;
    private const string AttributeDeletion = "deletion marked by an attribute holds wording not marked deleted: ";

    [TestMethod]
    public async Task Content3OverRun15IsMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                "Set LEX_LU_CONTENT3_PROBE=1 and LEX_LU_CONTENT3_PROBE_ROOT for the read-only run-15 measurement.");
        }

        var root = Environment.GetEnvironmentVariable(RootVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(root), RootVariable + " names the probe inputs.");
        var custody = (string)JsonNode.Parse(File.ReadAllText(Path.Combine(root, "probe.json")))!["custody"]!;
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
        using (var articles = new StreamWriter(Path.Combine(root, "csharp_content3.jsonl"), false, new UTF8Encoding(false)))
        {
            var ordered = inventory.Outcomes.OrderBy(static outcome => outcome.Input.ObjectOrdinal).ToArray();
            for (var start = 0; start < ordered.Length; start += ChunkBodies)
            {
                var chunk = ordered.Skip(start).Take(ChunkBodies).ToArray();
                var part = new LuxembourgAknArticleInventoryPopulation(
                    population, chunk, LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256);
                var two = await new LuxembourgAknLegalContentProfile2Producer(store).RunAsync(part, CancellationToken.None);
                var three = await new LuxembourgAknLegalContentProfile3Producer(store).RunAsync(part, CancellationToken.None);
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
                    var threeOutcomes = three.Outcomes.Where(outcome => ReferenceEquals(outcome.SourceInventoryOutcome, body)).ToArray();
                    Assert.HasCount(twoOutcomes.Length, threeOutcomes, rootIri);

                    // The S6 comparison: a body admitted today whose state index /9 holds (it has an article /2 admits).
                    var heldToday = group == "today" && twoOutcomes.Any(static outcome => Admits(outcome.Disposition));
                    for (var index = 0; index < twoOutcomes.Length; index++)
                    {
                        var second = twoOutcomes[index];
                        var third = threeOutcomes[index];
                        var twoAdmits = Admits(second.Disposition);
                        var threeAdmits = Admits(third.Disposition);
                        var shape = twoAdmits ? "admitted" : Shape(second);
                        var why = threeAdmits ? "admitted" : Shape(third);
                        var scopes = new List<string> { "all|all", kind + "|all", kind + "|" + group };
                        if (heldToday)
                        {
                            scopes.Add("held_today|all");
                        }
                        foreach (var scope in scopes)
                        {
                            Add(tallies, scope + "|articles");
                            Add(tallies, scope + "|content2_" + (twoAdmits ? "admitted" : "refused"));
                            Add(tallies, scope + "|content3_" + (threeAdmits ? "admitted" : "refused"));
                            if (!twoAdmits && threeAdmits)
                            {
                                Add(tallies, scope + "|recovered|" + shape);
                                Add(tallies, scope + "|recovered");
                            }
                            if (!twoAdmits && !threeAdmits)
                            {
                                Add(tallies, scope + "|still_refused|" + why);
                                Add(tallies, scope + "|still_refused_by_content2_shape|" + shape);
                                Add(tallies, scope + "|still_refused");
                            }
                            if (twoAdmits && !threeAdmits)
                            {
                                Add(tallies, scope + "|lost|" + why);
                            }
                        }
                        // The one shape /3 refuses that /2 admits: wording an attribute marks deleted, which /2 reads as in
                        // force (the review of #981, F2). Counted apart; any other difference is a disagreement.
                        var withdrawn = twoAdmits && !threeAdmits &&
                            (third.Detail ?? "").StartsWith(AttributeDeletion, StringComparison.Ordinal);
                        if (withdrawn)
                        {
                            Add(tallies, "all|all|withdrawn_attribute_deletion");
                        }
                        else if (twoAdmits && (second.Disposition != third.Disposition || !string.Equals(
                                LuxembourgIndexBuilder.TokensJson(second.Article!.Tokens),
                                LuxembourgIndexBuilder.TokensJson(third.Article!.Tokens),
                                StringComparison.Ordinal)))
                        {
                            disagreements.Add(rootIri + " " + third.Coordinate!.PublisherId + " | /2 admitted, /3 differs: " + third.Detail);
                        }

                        var tokens = third.Article?.Tokens;
                        articles.Write(new JsonObject
                        {
                            ["root"] = rootIri,
                            ["id"] = third.Coordinate!.PublisherId,
                            ["kind"] = kind,
                            ["group"] = group,
                            ["held_today"] = heldToday,
                            ["v2"] = Verdict(second),
                            ["v3"] = Verdict(third),
                            ["searchable_sha256"] = tokens is null ? null : Sha(string.Concat(tokens
                                .Where(static token => token.Kind is LuxembourgAknLegalContentTokenKind.Text or LuxembourgAknLegalContentTokenKind.Reference)
                                .Select(static token => token.Text))),
                            ["deleted_sha256"] = tokens is null ? null : Sha(string.Concat(tokens
                                .Where(static token => token.Kind == LuxembourgAknLegalContentTokenKind.DeletedText)
                                .Select(static token => token.Text))),
                            ["quotations"] = tokens?.Count(static token => token.Kind == LuxembourgAknLegalContentTokenKind.QuotationStart),
                            ["unmarked"] = tokens?.Count(static token => token.Unmarked),
                        }.ToJsonString());
                        articles.Write('\n');
                    }
                }
            }
        }
        var measured = clock.Elapsed;

        var summary = new JsonObject
        {
            ["schema"] = "lex-v3-lu-content3-run15-probe/1",
            ["inventory_4"] = inventory.RuleProfileSha256,
            ["content_2"] = LuxembourgAknLegalContentProfile2Producer.RuleProfileSha256,
            ["content_3"] = LuxembourgAknLegalContentProfile3Producer.RuleProfileSha256,
            ["held_inputs"] = population.Inputs.Count,
            ["disagreements"] = disagreements.Count,
            ["disagreement_examples"] = new JsonArray(disagreements.Take(20).Select(static value => (JsonNode)value!).ToArray()),
            ["seconds_inventory_4"] = Math.Round(inventoried.TotalSeconds, 1),
            ["seconds_content_2_and_3"] = Math.Round((measured - inventoried).TotalSeconds, 1),
            ["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024),
        };
        var counts = new JsonObject();
        foreach (var (key, value) in tallies)
        {
            counts[key] = value;
        }
        summary["counts"] = counts;
        File.WriteAllText(Path.Combine(root, "csharp_content3.summary.json"),
            summary.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine(summary.ToJsonString());

        Assert.AreEqual(0, disagreements.Count, "articles /2 admits that /3 does not read to the same tokens:\n" +
            string.Join("\n", disagreements.Take(40)));
    }

    private static bool Admits(LuxembourgAknLegalContentDisposition disposition) =>
        disposition is LuxembourgAknLegalContentDisposition.Admitted or LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence;

    private static string Verdict(LuxembourgAknLegalContentOutcome outcome) => outcome.Disposition switch
    {
        LuxembourgAknLegalContentDisposition.Admitted => "admitted",
        LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence => "marker_only",
        LuxembourgAknLegalContentDisposition.UnsupportedContentShape => outcome.Detail ?? "",
        _ => ContractWire.NameOf(outcome.Disposition),
    };

    /// <summary>A refusal's shape: the element or rule its detail names, in a short form.</summary>
    private static string Shape(LuxembourgAknLegalContentOutcome outcome)
    {
        if (outcome.Disposition != LuxembourgAknLegalContentDisposition.UnsupportedContentShape)
        {
            return ContractWire.NameOf(outcome.Disposition);
        }
        var detail = outcome.Detail ?? "";
        foreach (var prefix in new[]
                 {
                     "unsupported legal-content element: ", "unsupported content inside deleted wording: ",
                     "span of an unreviewed class: ", "deleted wording of an unreviewed class: ",
                     "publisher note bodies hold no quotation or deleted wording: ",
                 })
        {
            if (detail.StartsWith(prefix, StringComparison.Ordinal))
            {
                return prefix.TrimEnd(':', ' ').Replace(' ', '_') + ":" + detail[prefix.Length..];
            }
        }
        if (detail.StartsWith("modification end has no start in this article: ", StringComparison.Ordinal))
        {
            return "modification_end_without_start";
        }
        if (detail.StartsWith("article ends with an unclosed modification span: ", StringComparison.Ordinal))
        {
            return "unclosed_modification_span";
        }
        if (detail.StartsWith("duplicate open modification span: ", StringComparison.Ordinal))
        {
            return "duplicate_modification_span";
        }
        return detail.Replace(' ', '_');
    }

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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
