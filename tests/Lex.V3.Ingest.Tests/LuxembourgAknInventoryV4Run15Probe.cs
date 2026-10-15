using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S2's real-data acceptance: inventory /4 over every held body of Luxembourg run 15, compared body by
/// body with the reference implementation the slice's decisions were measured with.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY. It runs only with <c>LEX_LU_INVENTORY_V4_PROBE=1</c>, and
/// <c>LEX_LU_INVENTORY_V4_PROBE_ROOT</c> names a directory holding the probe inputs
/// (<c>run15-held.jsonl</c>, <c>run15-date-applicability.json</c>, <c>ref_v4.jsonl</c>, optionally
/// <c>acceptance-counts.json</c>) and a <c>probe.json</c> naming the run's custody root. The custody
/// store is wrapped so that a write throws; every body is read by digest, re-hashed, and never
/// copied anywhere. It sends nothing over the network and does not run R0, so it writes only its
/// two result files into the probe root.
/// </para>
/// <para>
/// WHAT IT COMPARES. Per root: the disposition token, the identity class, the named states, the
/// in-file legal resource and date, the main-body and excluded article counts and every finding;
/// the detail of a rejection; and, beside them, the wording digest the checks compare, re-read
/// per inventoried body, because a text-rule divergence between the two implementations would
/// otherwise show only where a check happens to compare wordings. The aggregates it prints are the
/// slice's headline counts and are compared with <c>acceptance-counts.json</c> when it is present.
/// The admission classes come from the reference rows: they are the design's rights classes, an
/// input to the grouping and not something /4 computes.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgAknInventoryV4Run15Probe
{
    private const string EnableVariable = "LEX_LU_INVENTORY_V4_PROBE";
    private const string RootVariable = "LEX_LU_INVENTORY_V4_PROBE_ROOT";
    private static readonly string[] Checks =
        ["shared_file", "later_state", "other_work", "date_ahead", "copied_state", "copied_act"];
    private static readonly string[] Groups = ["today", "policy_A", "L2", "L1", "L1b", "never", "all"];

    [TestMethod]
    public async Task InventoryV4OverRun15MatchesTheReference()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                "Set LEX_LU_INVENTORY_V4_PROBE=1 and LEX_LU_INVENTORY_V4_PROBE_ROOT for the read-only run-15 probe.");
        }

        var root = Environment.GetEnvironmentVariable(RootVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(root), RootVariable + " names the probe inputs.");
        var custody = (string)JsonNode.Parse(File.ReadAllText(Path.Combine(root, "probe.json")))!["custody"]!;
        var store = new ReadOnlyCustody(FileSystemCustodyStore.WithBrotliCompression(custody));
        var clock = Stopwatch.StartNew();

        // Receipts that name the run's own custody objects by digest; nothing is written.
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

        // The catalogue as the run's typed assertions carry it: every (subject, raw value) pair.
        var facts = new List<(string SubjectIri, string RawLexicalValue)>();
        var catalogueFacts = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "run15-date-applicability.json")))!;
        foreach (var (subject, values) in catalogueFacts["date_applicability"]!.AsObject())
        {
            foreach (var value in values!.AsArray())
            {
                facts.Add((subject, (string)value!));
            }
        }
        var catalogue = LuxembourgAknArticleInventoryProducerV4.CatalogueStarts(facts);
        var built = clock.Elapsed;

        var result = await new LuxembourgAknArticleInventoryProducerV4(store)
            .RunAsync(population, catalogue, CancellationToken.None);
        var inventoried = clock.Elapsed;

        var wordings = new Dictionary<int, string?>();
        foreach (var outcome in result.Outcomes)
        {
            if (outcome.Inventory is not null)
            {
                var bytes = await CustodyRestore.ReadCheckedAsync(
                    store, outcome.TransportReceipt.Reference, CancellationToken.None);
                wordings[outcome.Input.ObjectOrdinal] = LuxembourgAknArticleInventoryProducerV4.WordingSha256(bytes);
            }
        }
        var worded = clock.Elapsed;

        var references = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(root, "ref_v4.jsonl")))
        {
            var row = JsonNode.Parse(line)!;
            references.Add((string)row["root"]!, row);
        }

        var rows = new List<JsonObject>(result.Outcomes.Count);
        var mismatches = new List<string>();
        var wordingDifferences = new List<string>();
        using (var output = new StreamWriter(Path.Combine(root, "csharp_v4.jsonl"), false, new UTF8Encoding(false)))
        {
            foreach (var outcome in result.Outcomes)
            {
                var row = Row(outcome, wordings);
                rows.Add(row);
                output.Write(row.ToJsonString());
                output.Write('\n');
                var rootIri = (string)row["root"]!;
                if (!references.TryGetValue(rootIri, out var reference))
                {
                    mismatches.Add(rootIri + " | no reference row");
                    continue;
                }

                Compare(row, reference, mismatches);
                if (row["wording"] is not null &&
                    !string.Equals((string?)row["wording"], (string?)reference["wording"], StringComparison.Ordinal))
                {
                    wordingDifferences.Add(rootIri);
                }
            }
        }

        var summary = new StringBuilder();
        summary.Append("inputs ").Append(rows.Count).Append(" | references ").Append(references.Count)
            .Append(" | catalogue starts ").Append(catalogue.Count).Append('\n');
        summary.Append("mismatches ").Append(mismatches.Count).Append(" | wording digests compared ")
            .Append(wordings.Count).Append(", differing ").Append(wordingDifferences.Count).Append('\n');
        foreach (var mismatch in mismatches.Take(40))
        {
            summary.Append("  MISMATCH ").Append(mismatch).Append('\n');
        }
        foreach (var difference in wordingDifferences.Take(40))
        {
            summary.Append("  WORDING ").Append(difference).Append('\n');
        }

        var groups = new JsonObject();
        foreach (var group in Groups)
        {
            var members = rows.Where(row => group == "all" || Group(references, row) == group).ToArray();
            groups[group] = Aggregate(members);
        }
        foreach (var group in Groups)
        {
            summary.Append('[').Append(group).Append("] ").Append(Canonical(groups[group]!)).Append('\n');
        }

        var refused = rows
            .Where(static row => (string?)row["disp"] == "publisher_file_names_other_state")
            .Select(row => new JsonObject
            {
                ["root"] = (string?)row["root"],
                ["group"] = Group(references, row),
                ["class"] = (string?)row["cls"],
                ["findings"] = row["findings"]!.DeepClone(),
            })
            .ToArray();
        foreach (var row in refused)
        {
            summary.Append("REFUSED ").Append(Canonical(row)).Append('\n');
        }
        summary.Append("time: build ").Append(built.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" s, inventory /4 ").Append((inventoried - built).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" s, wording re-read ").Append((worded - inventoried).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" s; peak working set ")
            .Append((Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)).ToString(CultureInfo.InvariantCulture))
            .Append(" MB\n");
        summary.Append("population identity ").Append(result.IdentitySha256).Append(" | rule ")
            .Append(result.RuleProfileSha256).Append('\n');
        File.WriteAllText(Path.Combine(root, "csharp_v4.summary.txt"), summary.ToString(), new UTF8Encoding(false));
        Console.WriteLine(summary.ToString());

        Assert.AreEqual(0, mismatches.Count, "per-body mismatches against the reference:\n" +
            string.Join("\n", mismatches.Take(40)));
        Assert.AreEqual(0, wordingDifferences.Count, "wording digests differing from the reference:\n" +
            string.Join("\n", wordingDifferences.Take(40)));

        var acceptancePath = Path.Combine(root, "acceptance-counts.json");
        if (File.Exists(acceptancePath))
        {
            var acceptance = JsonNode.Parse(File.ReadAllText(acceptancePath))!;
            foreach (var group in Groups)
            {
                Assert.AreEqual(
                    Canonical(acceptance["groups"]![group]!), Canonical(groups[group]!), "group " + group);
            }
            CollectionAssert.AreEquivalent(
                acceptance["refused"]!.AsArray().Select(static row => Canonical(row!)).ToArray(),
                refused.Select(static row => Canonical(row)).ToArray(),
                "the refused bodies");
        }
    }

    /// <summary>One outcome as the reference names its fields.</summary>
    private static JsonObject Row(LuxembourgAknArticleInventoryOutcome outcome, Dictionary<int, string?> wordings)
    {
        var rootIri = outcome.Input.CorpusRecord.ObjectRef.PublisherUri;
        var row = new JsonObject
        {
            ["ord"] = outcome.Input.ObjectOrdinal,
            ["root"] = rootIri,
            ["kind"] = Kind(rootIri),
            ["disp"] = ContractWire.NameOf(outcome.Disposition),
        };
        if (outcome.Inventory?.Evidence is not { } evidence)
        {
            row["detail"] = outcome.Detail;
            return row;
        }

        row["cls"] = ContractWire.NameOf(evidence.IdentityClass);
        row["named"] = new JsonArray(evidence.NamedStates.Select(static state => (JsonNode?)state).ToArray());
        row["lr"] = evidence.InFileLegalResourceIri;
        row["in_date"] = evidence.InFileApplicabilityDate;
        row["n_articles"] = outcome.Inventory.Articles.Count;
        row["excluded"] = evidence.ExcludedArticleCount;
        row["non_ascii_ids"] = outcome.Inventory.Articles.Count(static article =>
            article.PublisherId.Any(static character => character > '~'));
        row["findings"] = new JsonArray(outcome.ConsistencyFindings
            .Select(static finding => (JsonNode?)new JsonArray(ContractWire.NameOf(finding.Check), finding.NamedState))
            .ToArray());
        row["detail"] = outcome.Detail;
        row["wording"] = wordings[outcome.Input.ObjectOrdinal];
        return row;
    }

    private static void Compare(JsonObject row, JsonNode reference, List<string> mismatches)
    {
        var fields = (string?)row["disp"] switch
        {
            "inventoried" or "publisher_file_names_other_state" =>
                new[] { "disp", "cls", "named", "lr", "in_date", "n_articles", "excluded", "findings" },
            _ => new[] { "disp", "detail" },
        };
        foreach (var field in fields)
        {
            var mine = row[field]?.ToJsonString() ?? "null";
            var theirs = reference[field]?.ToJsonString() ?? "null";
            if (!string.Equals(mine, theirs, StringComparison.Ordinal))
            {
                mismatches.Add((string?)row["root"] + " | " + field + " | C# " + mine + " | reference " + theirs);
            }
        }
    }

    /// <summary>The design's admission class, read from the reference row: an input to the grouping.</summary>
    private static string Group(Dictionary<string, JsonNode> references, JsonObject row) =>
        references.TryGetValue((string)row["root"]!, out var reference) &&
        (string?)reference["candidate"] is { } candidate
            ? candidate
            : "never";

    /// <summary>The slice's headline counts for one group of rows, in <c>acceptance-counts.json</c>'s shape.</summary>
    private static JsonObject Aggregate(IReadOnlyList<JsonObject> rows)
    {
        var inventoried = rows.Where(static row => (string?)row["disp"] == "inventoried").ToArray();
        var refused = rows.Where(static row => (string?)row["disp"] == "publisher_file_names_other_state").ToArray();
        var structural = inventoried.Concat(refused).ToArray();
        var checks = new JsonObject();
        foreach (var check in Checks)
        {
            checks[check] = refused.Count(row => row["findings"]!.AsArray()
                .Any(finding => (string?)finding![0] == check));
        }

        return new JsonObject
        {
            ["inputs"] = rows.Count,
            ["dispositions"] = CountBy(rows, static row => (string)row["disp"]!),
            ["inventoried_by_kind"] = CountBy(inventoried, static row => (string)row["kind"]!),
            ["refused_by_kind"] = CountBy(refused, static row => (string)row["kind"]!),
            ["rejected_details"] = CountBy(
                rows.Where(static row => (string?)row["disp"] == "xml_rejected").ToArray(),
                static row => (string)row["detail"]!),
            ["classes_inventoried_or_refused"] = CountBy(structural, static row => (string)row["cls"]!),
            ["main_body_articles_inventoried"] = inventoried.Sum(static row => (int)row["n_articles"]!),
            ["main_body_articles_inventoried_by_kind"] = SumBy(inventoried, "n_articles"),
            ["main_body_articles_in_refused"] = refused.Sum(static row => (int)row["n_articles"]!),
            ["excluded_articles_inventoried_or_refused"] = structural.Sum(static row => (int)row["excluded"]!),
            ["bodies_with_excluded_articles"] = structural.Count(static row => (int)row["excluded"]! != 0),
            ["non_ascii_main_body_ids"] = structural.Sum(static row => (int)row["non_ascii_ids"]!),
            ["in_file_date_present"] = structural.Count(static row => row["in_date"] is not null),
            ["checks"] = checks,
        };
    }

    private static JsonObject CountBy(IReadOnlyList<JsonObject> rows, Func<JsonObject, string> key)
    {
        var counts = new JsonObject();
        foreach (var group in rows.GroupBy(key).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            counts[group.Key] = group.Count();
        }
        return counts;
    }

    private static JsonObject SumBy(IReadOnlyList<JsonObject> rows, string field)
    {
        var sums = new JsonObject();
        foreach (var group in rows.GroupBy(static row => (string)row["kind"]!).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            sums[group.Key] = group.Sum(row => (int)row[field]!);
        }
        return sums;
    }

    /// <summary>The reference's kind names: <c>jo</c>, <c>consolide</c>, <c>code</c> or <c>other</c>.</summary>
    private static string Kind(string rootIri) => LuxembourgEliState.KindOf(rootIri) switch
    {
        "act" => "jo",
        "consolidation" => "consolide",
        "code" => "code",
        _ => "other",
    };

    /// <summary>JSON with every object's keys in ordinal order, so two documents compare as text.</summary>
    private static string Canonical(JsonNode node)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, node);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var (name, child) in value.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    Write(writer, child);
                }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var child in value)
                {
                    Write(writer, child);
                }
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// The run's custody, read and never written. Internal so that S3a's content probe
    /// (<see cref="LuxembourgAknContent2Run15Probe"/>) reads through this same decorator.
    /// </summary>
    internal sealed class ReadOnlyCustody(ICustodyStore inner) : ICustodyStore
    {
        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The run-15 probe never writes custody.");

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }
}
