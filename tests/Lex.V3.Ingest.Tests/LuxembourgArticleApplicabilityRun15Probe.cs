using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1's measurement: every article-level <c>dateApplicability</c> value of Luxembourg run 15 under Release 1's rules
/// (inventory /4, content /3, rights as acquired), counted by form, and what the index builder makes of each. Never a CI
/// test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like S3b's <see cref="LuxembourgAknContent3Run15Probe"/>, over the same inputs. It runs only
/// with <c>LEX_LU_APPLICABILITY_PROBE=1</c>, and <c>LEX_LU_APPLICABILITY_PROBE_ROOT</c> names a directory holding
/// <c>run15-held.jsonl</c>, <c>run15-date-applicability.json</c> and a <c>probe.json</c> naming the run's custody root.
/// Custody is read by digest through S2's read-only decorator and never written; nothing goes over the network and R0
/// does not run. It writes one file into the probe root, <c>article-applicability.summary.json</c>, which quotes up to
/// three publisher values of each form (dates, never wording).
/// </para>
/// <para>
/// WHAT IT COUNTS. The value is the one inventory /4 retains on each main-body article's coordinate
/// (<see cref="LuxembourgAknArticleCoordinate.PublisherApplicability"/>): the trimmed text of the article's own
/// <c>scl:jolux scl:name="dateApplicability"</c>. Scope <c>inventoried</c> is every main-body article of every body
/// inventory /4 inventoried, whatever its rights; scope <c>derive</c> is the articles the index build reads: content /3
/// admitted them (or kept them as marker-only evidence) in a body whose rights are <c>agreed_same_run_cc_by</c>, the
/// acquisition's own resolution, which Release 1 keeps. Each form is the value with every digit written 9; each carries its
/// count, its bodies by kind and three examples, and a value in a form other than <c>yyyy-MM-dd</c> is quoted with its
/// neighbours' values and its body's most frequent ones. For every value the builder's reading
/// (<see cref="LuxembourgIndexBuilder.ArticleApplicability"/>) is applied and counted.
/// </para>
/// <para>
/// WHAT FAILS IT. A value the derive reads that the builder gives no date (<c>not_a_calendar_date</c>): the index would serve
/// that article as stating none, and saying it as unclear needs the value as written in the index (index /10). Run it over an
/// acquisition before its derive. Over run 15 (2026-10-26) it finds 229,711 values written <c>yyyy-MM-dd</c> and 3 written
/// <c>yyyyMMdd</c> (2 in the derive), all read.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgArticleApplicabilityRun15Probe
{
    private const string EnableVariable = "LEX_LU_APPLICABILITY_PROBE";
    private const string RootVariable = "LEX_LU_APPLICABILITY_PROBE_ROOT";
    private const int ChunkBodies = 32;
    private const string AcquiredRights = "agreed_same_run_cc_by";

    [TestMethod]
    public async Task ArticleApplicabilityValuesOverRun15AreMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                "Set LEX_LU_APPLICABILITY_PROBE=1 and LEX_LU_APPLICABILITY_PROBE_ROOT for the read-only run-15 measurement.");
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
        var rightsByRoot = new Dictionary<string, string>(StringComparer.Ordinal);
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
            rightsByRoot[(string)row["root_iri"]!] = (string)row["rights_disposition"]!;
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

        var inventory = await new LuxembourgAknArticleInventoryProducerV4(store).RunAsync(
            population, LuxembourgAknArticleInventoryProducerV4.CatalogueStarts(facts), CancellationToken.None);
        var inventoried = clock.Elapsed;

        var scopes = new SortedDictionary<string, Scope>(StringComparer.Ordinal)
        {
            ["inventoried"] = new(),
            ["derive"] = new(),
        };
        var unreadInDerive = new List<string>();
        void Count(string scopeName, string rootIri, string articleId, string? value)
        {
            var scope = scopes[scopeName];
            scope.Articles++;
            if (value is null)
            {
                scope.WithoutValue++;
                return;
            }

            var form = Form(value);
            if (!scope.Forms.TryGetValue(form, out var tally))
            {
                scope.Forms[form] = tally = new Tally();
            }
            tally.Articles++;
            tally.Bodies.Add(rootIri);
            if (tally.Examples.Count < 3)
            {
                tally.Examples.Add($"{rootIri} {articleId} \"{value}\"");
            }

            var read = LuxembourgIndexBuilder.ArticleApplicability(value);
            var outcome = read.Date is null ? "no_date:" + read.Reading : "date:" + read.Reading;
            tally.Readings[outcome] = tally.Readings.GetValueOrDefault(outcome) + 1;
            if (read.Date is null && scopeName == "derive" && unreadInDerive.Count < 40)
            {
                unreadInDerive.Add($"{rootIri} {articleId} \"{value}\": {read.Reading}");
            }
        }

        var acquiredInventoried = new List<LuxembourgAknArticleInventoryOutcome>();
        var contexts = new List<string>();
        var bodiesInventoried = 0;
        foreach (var body in inventory.Outcomes.OrderBy(static outcome => outcome.Input.ObjectOrdinal))
        {
            if (body.Disposition != LuxembourgAknArticleInventoryDisposition.Inventoried)
            {
                continue;
            }

            bodiesInventoried++;
            var rootIri = body.Input.CorpusRecord.ObjectRef.PublisherUri;
            var coordinates = body.Inventory!.Articles;
            for (var index = 0; index < coordinates.Count; index++)
            {
                var article = coordinates[index];
                Count("inventoried", rootIri, article.PublisherId, article.PublisherApplicability);
                if (article.PublisherApplicability is { } value &&
                    LuxembourgIndexBuilder.ArticleApplicability(value).Reading != LuxembourgIndexBuilder.ArticleDateExtended &&
                    contexts.Count < 40)
                {
                    // Where a value in another form sits: its neighbours' values and the body's most frequent values.
                    var common = coordinates
                        .Where(static other => other.PublisherApplicability is not null)
                        .GroupBy(static other => other.PublisherApplicability!, StringComparer.Ordinal)
                        .OrderByDescending(static group => group.Count()).ThenBy(static group => group.Key, StringComparer.Ordinal)
                        .Take(3)
                        .Select(static group => $"\"{group.Key}\"x{group.Count()}");
                    contexts.Add(
                        $"{rootIri} {article.PublisherId} \"{value}\"; before: " +
                        (index > 0 ? $"{coordinates[index - 1].PublisherId} \"{coordinates[index - 1].PublisherApplicability}\"" : "-") +
                        "; after: " +
                        (index + 1 < coordinates.Count ? $"{coordinates[index + 1].PublisherId} \"{coordinates[index + 1].PublisherApplicability}\"" : "-") +
                        $"; the body's {coordinates.Count} articles' most frequent values: {string.Join(", ", common)}");
                }
            }
            if (string.Equals(rightsByRoot[rootIri], AcquiredRights, StringComparison.Ordinal))
            {
                acquiredInventoried.Add(body);
            }
        }

        var deriveDispositions = new SortedDictionary<string, long>(StringComparer.Ordinal);
        for (var start = 0; start < acquiredInventoried.Count; start += ChunkBodies)
        {
            var chunk = acquiredInventoried.Skip(start).Take(ChunkBodies).ToArray();
            var part = new LuxembourgAknArticleInventoryPopulation(
                population, chunk, LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256);
            var three = await new LuxembourgAknLegalContentProfile3Producer(store).RunAsync(part, CancellationToken.None);
            foreach (var outcome in three.Outcomes)
            {
                var name = ContractWire.NameOf(outcome.Disposition);
                deriveDispositions[name] = deriveDispositions.GetValueOrDefault(name) + 1;
                if (outcome.Disposition is not (LuxembourgAknLegalContentDisposition.Admitted or
                    LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence))
                {
                    continue;
                }

                Count(
                    "derive",
                    outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef.PublisherUri,
                    outcome.Article!.Coordinate.PublisherId,
                    outcome.Article.Coordinate.PublisherApplicability);
            }
        }
        var measured = clock.Elapsed;

        var summary = new JsonObject
        {
            ["schema"] = "lex-v3-lu-article-applicability-run15-probe/1",
            ["inventory_4"] = inventory.RuleProfileSha256,
            ["content_3"] = LuxembourgAknLegalContentProfile3Producer.RuleProfileSha256,
            ["held_inputs"] = population.Inputs.Count,
            ["bodies_inventoried"] = bodiesInventoried,
            ["acquired_bodies_inventoried"] = acquiredInventoried.Count,
            ["content_3_dispositions_in_acquired_bodies"] = new JsonObject(deriveDispositions
                .Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
            ["unread_in_derive_scope"] = new JsonArray(unreadInDerive.Select(static value => (JsonNode?)value).ToArray()),
            ["values_in_another_form_in_context"] = new JsonArray(contexts.Select(static value => (JsonNode?)value).ToArray()),
            ["seconds_inventory_4"] = Math.Round(inventoried.TotalSeconds, 1),
            ["seconds_content_3"] = Math.Round((measured - inventoried).TotalSeconds, 1),
            ["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024),
        };
        foreach (var (name, scope) in scopes)
        {
            summary[name] = new JsonObject
            {
                ["articles"] = scope.Articles,
                ["articles_without_value"] = scope.WithoutValue,
                ["articles_with_value"] = scope.Articles - scope.WithoutValue,
                ["forms"] = new JsonObject(scope.Forms.Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)new JsonObject
                {
                    ["articles"] = pair.Value.Articles,
                    ["bodies"] = pair.Value.Bodies.Count,
                    ["bodies_by_kind"] = new JsonObject(pair.Value.Bodies
                        .GroupBy(Kind, StringComparer.Ordinal)
                        .OrderBy(static group => group.Key, StringComparer.Ordinal)
                        .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count()))),
                    ["first_bodies"] = new JsonArray(pair.Value.Bodies.Order(StringComparer.Ordinal).Take(10)
                        .Select(static value => (JsonNode?)value).ToArray()),
                    ["examples"] = new JsonArray(pair.Value.Examples.Select(static value => (JsonNode?)value).ToArray()),
                    ["builder_readings"] = new JsonObject(pair.Value.Readings
                        .OrderBy(static reading => reading.Key, StringComparer.Ordinal)
                        .Select(static reading => KeyValuePair.Create(reading.Key, (JsonNode?)reading.Value))),
                }))),
            };
        }
        File.WriteAllText(Path.Combine(root, "article-applicability.summary.json"),
            summary.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine(summary.ToJsonString());

        Assert.IsEmpty(unreadInDerive, "values the derive reads that the index gives no date (index /10's trigger):\n" +
            string.Join("\n", unreadInDerive));
    }

    /// <summary>A value's form: every ASCII digit written 9, everything else verbatim.</summary>
    private static string Form(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character is >= '0' and <= '9' ? '9' : character);
        }
        return builder.ToString();
    }

    private static string Kind(string rootIri) => LuxembourgEliState.KindOf(rootIri) switch
    {
        "act" => "jo",
        "consolidation" => "consolide",
        "code" => "code",
        _ => "other",
    };

    private sealed class Scope
    {
        public long Articles { get; set; }

        public long WithoutValue { get; set; }

        public SortedDictionary<string, Tally> Forms { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Tally
    {
        public long Articles { get; set; }

        public HashSet<string> Bodies { get; } = new(StringComparer.Ordinal);

        public List<string> Examples { get; } = [];

        public SortedDictionary<string, long> Readings { get; } = new(StringComparer.Ordinal);
    }
}
