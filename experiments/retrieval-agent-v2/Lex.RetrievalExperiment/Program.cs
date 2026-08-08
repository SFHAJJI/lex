using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Index;
using Lex.Mcp;
using Lex.RetrievalExperiment;
using Microsoft.Data.Sqlite;

var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (arguments.Length == 0)
{
    Usage();
    return 2;
}

string RequiredValue(string name)
{
    var index = Array.IndexOf(arguments, name);
    if (index < 0 || index + 1 >= arguments.Length)
        throw new ArgumentException($"{name} required");
    var value = arguments[index + 1];
    if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException($"{name} required");
    return value;
}

string RequiredPath(string name) => Path.GetFullPath(RequiredValue(name));

void Usage() => Console.Error.WriteLine("""
    usage:
      baseline --index-dir PATH --model-dir PATH --scenarios FILE --output FILE
      workbench-init --index-dir PATH --output FILE
      enrichment-sample --workbench FILE --endpoint URI --deployment NAME --works FILE --runs N --output FILE
      enrichment-analyze --report FILE --model-dir PATH --output FILE
    """);

if (arguments[0] == "workbench-init")
{
    var sourceDir = RequiredPath("--index-dir");
    var workbenchOutput = RequiredPath("--output");
    var sourceIndexes = Directory.EnumerateFiles(sourceDir, "index-*.db").Order().ToArray();
    EnrichmentWorkbench.Create(sourceIndexes, workbenchOutput);
    using var created = new SqliteConnection($"Data Source={workbenchOutput};Mode=ReadOnly;Pooling=False");
    created.Open();
    using var count = created.CreateCommand();
    count.CommandText = "SELECT COUNT(*) FROM publisher_works";
    Console.Error.WriteLine($"[workbench] wrote {count.ExecuteScalar()} work-language records to {workbenchOutput}");
    return 0;
}

if (arguments[0] == "enrichment-sample")
{
    var workbenchPath = RequiredPath("--workbench");
    var endpointValue = RequiredValue("--endpoint");
    if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
        || endpoint.Scheme != Uri.UriSchemeHttps)
        throw new ArgumentException("--endpoint must be an absolute HTTPS URI");
    var deployment = RequiredValue("--deployment");
    var worksPath = RequiredPath("--works");
    var enrichmentOutput = RequiredPath("--output");
    if (File.Exists(enrichmentOutput))
        throw new IOException($"Refusing to overwrite existing report: {enrichmentOutput}");
    var runArgument = Array.IndexOf(arguments, "--runs");
    var runCount = runArgument >= 0 && runArgument + 1 < arguments.Length
        && int.TryParse(arguments[runArgument + 1], out var parsedRuns) ? parsedRuns : 2;
    var worksRoot = JsonNode.Parse(File.ReadAllBytes(worksPath))?.AsObject()
        ?? throw new InvalidDataException("Fixture works file is empty.");
    var fixtures = worksRoot["works"]?.AsArray().OfType<JsonObject>().Select(item => (
        Work: item["work"]?.GetValue<string>() ?? throw new InvalidDataException("Fixture work is required."),
        Language: item["language"]?.GetValue<string>() ?? throw new InvalidDataException("Fixture language is required.")))
        .ToArray() ?? throw new InvalidDataException("Fixture works array is required.");
    var enrichmentReport = await EnrichmentSampleGenerator.RunAsync(
        workbenchPath, endpoint, deployment, fixtures, runCount, CancellationToken.None);
    Directory.CreateDirectory(Path.GetDirectoryName(enrichmentOutput)
        ?? throw new ArgumentException("Output must include a directory."));
    File.WriteAllBytes(enrichmentOutput, JsonSerializer.SerializeToUtf8Bytes(enrichmentReport,
        new JsonSerializerOptions { WriteIndented = true }));
    Console.Error.WriteLine($"[enrichment] wrote {fixtures.Length} work reports to {enrichmentOutput}");
    return 0;
}

if (arguments[0] == "enrichment-analyze")
{
    var reportPath = RequiredPath("--report");
    var analysisModelDir = RequiredPath("--model-dir");
    var analysisOutput = RequiredPath("--output");
    if (File.Exists(analysisOutput))
        throw new IOException($"Refusing to overwrite existing report: {analysisOutput}");
    var analysis = EnrichmentSampleAnalyzer.Analyze(reportPath, analysisModelDir);
    Directory.CreateDirectory(Path.GetDirectoryName(analysisOutput)
        ?? throw new ArgumentException("Output must include a directory."));
    File.WriteAllBytes(analysisOutput, JsonSerializer.SerializeToUtf8Bytes(analysis,
        new JsonSerializerOptions { WriteIndented = true }));
    Console.Error.WriteLine($"[enrichment-analysis] wrote {analysisOutput}");
    return 0;
}

if (arguments[0] != "baseline")
{
    Usage();
    return 2;
}

var indexDir = RequiredPath("--index-dir");
var modelDir = RequiredPath("--model-dir");
var scenarioPath = RequiredPath("--scenarios");
var outputPath = RequiredPath("--output");
if (File.Exists(outputPath))
    throw new IOException($"Refusing to overwrite existing report: {outputPath}");

var scenarioRoot = JsonNode.Parse(File.ReadAllBytes(scenarioPath))?.AsObject()
    ?? throw new InvalidDataException("Scenario file is empty.");
var cases = scenarioRoot["cases"]?.AsArray()
    ?? throw new InvalidDataException("Scenario file has no cases array.");

JsonObject SnapshotProtectedTables(string dbPath)
{
    var tables = new[]
    {
        "docs", "text_blobs", "provisions", "provision_states", "anchor_events",
        "citations", "events", "obs_history",
    };
    using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
    connection.Open();
    var snapshots = new JsonArray();
    foreach (var table in tables)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        exists.Parameters.AddWithValue("$name", table);
        if (exists.ExecuteScalar() is null) continue;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid";
        using var reader = command.ExecuteReader();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long rows = 0;
        while (reader.Read())
        {
            rows++;
            for (var column = 0; column < reader.FieldCount; column++)
            {
                if (reader.IsDBNull(column))
                {
                    hash.AppendData([0]);
                    continue;
                }
                var value = reader.GetValue(column);
                var bytes = value switch
                {
                    byte[] blob => blob,
                    _ => Encoding.UTF8.GetBytes(Convert.ToString(value,
                        System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                };
                hash.AppendData([value is byte[] ? (byte)2 : (byte)1]);
                hash.AppendData(BitConverter.GetBytes(bytes.LongLength));
                hash.AppendData(bytes);
            }
        }
        snapshots.Add(new JsonObject
        {
            ["table"] = table,
            ["rows"] = rows,
            ["sha256"] = Convert.ToHexStringLower(hash.GetHashAndReset()),
        });
    }
    return new JsonObject
    {
        ["file"] = Path.GetFileName(dbPath),
        ["tables"] = snapshots,
    };
}

JsonObject EvidenceCall(McpCore core, string id, string tool, JsonObject arguments)
{
    var response = core.CallTool(tool, arguments);
    var bytes = Encoding.UTF8.GetBytes(response.ToJsonString());
    return new JsonObject
    {
        ["id"] = id,
        ["tool"] = tool,
        ["arguments"] = arguments.DeepClone(),
        ["response_sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        ["response"] = response,
    };
}

using var encoder = MultilingualE5Encoder.Open(modelDir);
var readers = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);
try
{
    foreach (var dbPath in Directory.EnumerateFiles(indexDir, "index-*.db").Order())
    {
        var vectorPath = Path.ChangeExtension(dbPath, ".vectors");
        if (!File.Exists(vectorPath))
            throw new FileNotFoundException("The baseline requires the matching vector file.", vectorPath);
        var reader = LexIndexReader.Open(dbPath, encoder, vectorPath);
        readers.Add(reader.Collection, reader);
    }

    var core = new McpCore(readers);
    var protectedSnapshots = new JsonArray(Directory.EnumerateFiles(indexDir, "index-*.db").Order()
        .Select(path => (JsonNode)SnapshotProtectedTables(path)).ToArray());
    var representativeEvidence = new JsonArray
    {
        EvidenceCall(core, "gdpr-article-33-2019", "as_of", new JsonObject
        {
            ["work"] = "eu-eurlex:32016r0679",
            ["date"] = "2019-03-15",
            ["language"] = "fr",
            ["mode"] = "select",
            ["anchors"] = "art_33",
        }),
        EvidenceCall(core, "gdpr-article-33-diff", "diff", new JsonObject
        {
            ["work"] = "eu-eurlex:32016r0679",
            ["from_date"] = "2019-03-15",
            ["to_date"] = "2025-01-01",
            ["language"] = "fr",
        }),
        EvidenceCall(core, "luxembourg-cnpd-law-current", "as_of", new JsonObject
        {
            ["work"] = "lu-legilux:loi-2018-08-01-a686",
            ["date"] = "2026-08-08",
            ["language"] = "fr",
            ["mode"] = "outline",
        }),
    };
    var results = new JsonArray();
    foreach (var scenario in cases.OfType<JsonObject>().Where(x => x["surface"]?.GetValue<string>() == "search"))
    {
        var query = scenario["query"]?.GetValue<string>()
            ?? throw new InvalidDataException($"Scenario {scenario["id"]} has no query.");
        var expected = scenario["expected_work"]?.GetValue<string>();
        var expectedParts = expected?.Split(':', 2);
        var expectedPublisher = expectedParts is { Length: 2 } ? expectedParts[0] : null;
        var expectedGroup = expectedParts is { Length: 2 } ? expectedParts[1] : null;
        var modeResults = new JsonArray();

        foreach (var mode in new[] { "keyword", "hybrid" })
        {
            var request = new JsonObject
            {
                ["query"] = query,
                ["language"] = scenario["language"]?.GetValue<string>(),
                ["retrieval_mode"] = mode,
                ["fuzzy"] = "auto",
                ["limit"] = 10,
            };
            if (expectedPublisher == "eu-eurlex") request["jurisdiction"] = "EU";
            if (expectedPublisher == "lu-legilux") request["jurisdiction"] = "LU";

            var stopwatch = Stopwatch.StartNew();
            var response = core.CallTool("search", request);
            stopwatch.Stop();
            var publisherResult = response.AsArray().OfType<JsonObject>().FirstOrDefault();
            var hits = publisherResult?["hits"]?.AsArray() ?? [];
            int? workRank = null;
            int? anchorRank = null;
            var expectedAnchors = scenario["expected_anchor_any"]?.AsArray()
                .Select(x => x?.GetValue<string>()).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal)
                ?? [];
            for (var hitIndex = 0; hitIndex < hits.Count; hitIndex++)
            {
                if (hits[hitIndex] is not JsonObject hit || hit["work"]?.GetValue<string>() != expectedGroup)
                    continue;
                workRank ??= hitIndex + 1;
                if (expectedAnchors.Count > 0 && hit["anchor"] is JsonNode anchor
                    && expectedAnchors.Contains(anchor.GetValue<string>()))
                    anchorRank ??= hitIndex + 1;
            }

            modeResults.Add(new JsonObject
            {
                ["mode"] = mode,
                ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                ["work_rank"] = workRank,
                ["anchor_rank"] = anchorRank,
                ["retrieval_mode_used"] = publisherResult?["retrieval_mode"]?.DeepClone(),
                ["query_expansions"] = publisherResult?["query_expansions"]?.DeepClone(),
                ["hits"] = hits.DeepClone(),
            });
        }

        results.Add(new JsonObject
        {
            ["id"] = scenario["id"]?.DeepClone(),
            ["query"] = query,
            ["expected_work"] = expected,
            ["modes"] = modeResults,
        });
        Console.Error.WriteLine($"[baseline] {scenario["id"]}");
    }

    var report = new JsonObject
    {
        ["schema"] = "lex-retrieval-agent-baseline/1",
        ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
        ["scenario_schema"] = scenarioRoot["schema"]?.DeepClone(),
        ["model"] = new JsonObject
        {
            ["id"] = encoder.ModelId,
            ["revision"] = encoder.ModelRevision,
            ["sha256"] = encoder.ModelSha256,
            ["tokenizer_sha256"] = encoder.TokenizerSha256,
            ["execution_provider"] = encoder.ExecutionProvider,
        },
        ["indexes"] = new JsonArray(readers.Values.Select(reader => (JsonNode)new JsonObject
        {
            ["collection"] = reader.Collection,
            ["schema"] = reader.Stamp.GetValueOrDefault("schema"),
            ["corpus_commit"] = reader.Stamp.GetValueOrDefault("corpus_commit"),
            ["built_at"] = reader.Stamp.GetValueOrDefault("built_at"),
            ["hybrid_ready"] = reader.HybridReady,
        }).ToArray()),
        ["protected_tables"] = protectedSnapshots,
        ["representative_evidence"] = representativeEvidence,
        ["results"] = results,
    };

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
        ?? throw new ArgumentException("Output must include a directory."));
    File.WriteAllBytes(outputPath, JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions
    {
        WriteIndented = true,
    }));
    Console.Error.WriteLine($"[baseline] wrote {results.Count} search cases to {outputPath}");
    return 0;
}
finally
{
    foreach (var reader in readers.Values) reader.Dispose();
}
