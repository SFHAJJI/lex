using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Lex.RetrievalExperiment;

public static class WorkSearchExperiment
{
    public static JsonObject Build(
        string workbenchPath,
        string analysisPath,
        string aliasPath,
        double threshold,
        string outputPath)
    {
        var sources = LoadSources(workbenchPath);
        var discoveries = LoadDiscoveries(analysisPath, threshold);
        var aliases = LoadAliases(aliasPath);
        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["schema"] = "lex-work-search-experiment/1",
            ["workbench_sha256"] = Hash(workbenchPath),
            ["analysis_sha256"] = Hash(analysisPath),
            ["reviewed_aliases_sha256"] = Hash(aliasPath),
            ["semantic_consensus_minimum_cosine"] = threshold.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture),
        };
        WorkSearchIndex.Create(outputPath, sources, discoveries, aliases, metadata);
        return new JsonObject
        {
            ["schema"] = "lex-work-search-build-report/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["artifact"] = Path.GetFileName(outputPath),
            ["artifact_bytes"] = new FileInfo(outputPath).Length,
            ["artifact_sha256"] = Hash(outputPath),
            ["work_records"] = sources.Count,
            ["weak_discoveries"] = discoveries.Count,
            ["reviewed_aliases"] = aliases.Count,
            ["metadata"] = new JsonObject(metadata.Select(item =>
                new KeyValuePair<string, JsonNode?>(item.Key, JsonValue.Create(item.Value)))),
        };
    }

    public static JsonObject Evaluate(string indexPath, string scenarioPath)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(scenarioPath))?.AsObject()
            ?? throw new InvalidDataException("Scenario file is empty.");
        var results = new JsonArray();
        foreach (var scenario in root["cases"]?.AsArray().OfType<JsonObject>()
                     .Where(item => item["surface"]?.GetValue<string>() == "search") ?? [])
        {
            var id = scenario["id"]?.GetValue<string>() ?? throw new InvalidDataException("Scenario has no id.");
            var query = scenario["query"]?.GetValue<string>() ?? throw new InvalidDataException($"{id} has no query.");
            var language = scenario["language"]?.GetValue<string>();
            var expected = scenario["expected_work"]?.GetValue<string>();
            var stopwatch = Stopwatch.StartNew();
            var hits = WorkSearchIndex.Search(indexPath, query, language, 100);
            stopwatch.Stop();
            var rank = expected is null ? null : hits.Select((hit, index) => (hit, index))
                .Where(item => item.hit.Work == expected).Select(item => (int?)item.index + 1).FirstOrDefault();
            results.Add(new JsonObject
            {
                ["id"] = id,
                ["query"] = query,
                ["expected_work"] = expected,
                ["work_rank"] = rank,
                ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                ["hits"] = new JsonArray(hits.Take(10).Select(hit => (JsonNode)new JsonObject
                {
                    ["work"] = hit.Work,
                    ["language"] = hit.Language,
                    ["title"] = hit.Title,
                    ["reason"] = hit.Reason,
                    ["score"] = hit.Score,
                }).ToArray()),
            });
            Console.Error.WriteLine($"[work-search-eval] {id}: rank={rank?.ToString() ?? "missing"}");
        }
        return new JsonObject
        {
            ["schema"] = "lex-work-search-evaluation/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["index_sha256"] = Hash(indexPath),
            ["scenario_sha256"] = Hash(scenarioPath),
            ["results"] = results,
        };
    }

    public static JsonObject BuildVectors(string indexPath, string modelDirectory, string outputPath)
    {
        using var encoder = Lex.Index.MultilingualE5Encoder.Open(modelDirectory);
        var stopwatch = Stopwatch.StartNew();
        var build = WorkVectorIndex.Build(indexPath, outputPath, encoder);
        stopwatch.Stop();
        var input = WorkVectorIndex.InputDigest(indexPath);
        if (input.Count != build.Count)
            throw new InvalidDataException("Work vector input count changed during construction.");
        return new JsonObject
        {
            ["schema"] = "lex-work-vector-build-report/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source_index_sha256"] = Hash(indexPath),
            ["artifact"] = Path.GetFileName(outputPath),
            ["artifact_bytes"] = new FileInfo(outputPath).Length,
            ["artifact_sha256"] = Hash(outputPath),
            ["count"] = build.Count,
            ["vector_input_sha256"] = input.Sha256,
            ["dimensions"] = build.Dimensions,
            ["model_id"] = build.ModelId,
            ["model_revision"] = build.ModelRevision,
            ["model_sha256"] = encoder.ModelSha256,
            ["tokenizer_sha256"] = encoder.TokenizerSha256,
            ["execution_provider"] = encoder.ExecutionProvider,
            ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds,
        };
    }

    public static JsonObject EvaluateHybrid(
        string indexPath,
        string vectorPath,
        string modelDirectory,
        string scenarioPath)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(scenarioPath))?.AsObject()
            ?? throw new InvalidDataException("Scenario file is empty.");
        using var encoder = Lex.Index.MultilingualE5Encoder.Open(modelDirectory);
        var results = new JsonArray();
        foreach (var scenario in root["cases"]?.AsArray().OfType<JsonObject>()
                     .Where(item => item["surface"]?.GetValue<string>() == "search") ?? [])
        {
            var id = scenario["id"]?.GetValue<string>() ?? throw new InvalidDataException("Scenario has no id.");
            var query = scenario["query"]?.GetValue<string>() ?? throw new InvalidDataException($"{id} has no query.");
            var language = scenario["language"]?.GetValue<string>();
            var expected = scenario["expected_work"]?.GetValue<string>();
            var stopwatch = Stopwatch.StartNew();
            var lexical = WorkSearchIndex.Search(indexPath, query, language, 100);
            var semantic = WorkVectorIndex.Search(indexPath, vectorPath, encoder, query, language, 100);
            var hits = WorkHybridSearch.Fuse(lexical, semantic, 100);
            stopwatch.Stop();
            var lexicalRank = Rank(lexical.Select(hit => hit.Work), expected);
            var semanticRank = Rank(semantic.Select(hit => hit.Work), expected);
            var rank = expected is null ? null : hits.Select((hit, index) => (hit, index))
                .Where(item => item.hit.Work == expected).Select(item => (int?)item.index + 1).FirstOrDefault();
            results.Add(new JsonObject
            {
                ["id"] = id,
                ["query"] = query,
                ["expected_work"] = expected,
                ["lexical_work_rank"] = lexicalRank,
                ["semantic_work_rank"] = semanticRank,
                ["work_rank"] = rank,
                ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                ["hits"] = new JsonArray(hits.Take(10).Select(hit => (JsonNode)new JsonObject
                {
                    ["work"] = hit.Work,
                    ["language"] = hit.Language,
                    ["title"] = hit.Title,
                    ["reason"] = hit.Reason,
                    ["score"] = hit.Score,
                }).ToArray()),
            });
            Console.Error.WriteLine($"[work-hybrid-eval] {id}: rank={rank?.ToString() ?? "missing"} "
                + $"elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0}ms");
        }
        return new JsonObject
        {
            ["schema"] = "lex-work-hybrid-evaluation/1",
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["index_sha256"] = Hash(indexPath),
            ["vector_sha256"] = Hash(vectorPath),
            ["scenario_sha256"] = Hash(scenarioPath),
            ["model_sha256"] = encoder.ModelSha256,
            ["tokenizer_sha256"] = encoder.TokenizerSha256,
            ["execution_provider"] = encoder.ExecutionProvider,
            ["results"] = results,
        };
    }

    private static int? Rank(IEnumerable<string> works, string? expected)
    {
        if (expected is null) return null;
        var rank = 0;
        foreach (var work in works)
        {
            rank++;
            if (work == expected) return rank;
        }
        return null;
    }

    private static IReadOnlyList<WorkSearchSource> LoadSources(string path)
    {
        using var database = new SqliteConnection($"Data Source={Path.GetFullPath(path)};Mode=ReadOnly;Pooling=False");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT work,language,group_key,group_identifier,title,title_short,hierarchy,domains,act_form,kind
            FROM publisher_works ORDER BY work,language
            """;
        using var rows = command.ExecuteReader();
        var sources = new List<WorkSearchSource>();
        while (rows.Read())
            sources.Add(new WorkSearchSource(
                rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3),
                Value(rows, 4), Value(rows, 5), Value(rows, 6), Value(rows, 7), Value(rows, 8), Value(rows, 9)));
        return sources;
    }

    private static IReadOnlyList<WorkDiscovery> LoadDiscoveries(string path, double threshold)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(path))?.AsObject()
            ?? throw new InvalidDataException("Semantic analysis is empty.");
        var discoveries = new List<WorkDiscovery>();
        foreach (var work in root["works"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var workId = work["work"]?.GetValue<string>() ?? throw new InvalidDataException("Analysis work is missing.");
            var language = work["language"]?.GetValue<string>()
                ?? throw new InvalidDataException($"{workId} language is missing.");
            var selected = work["thresholds"]?.AsArray().OfType<JsonObject>().SingleOrDefault(item =>
                Math.Abs((item["minimum_cosine"]?.GetValue<double>() ?? -1) - threshold) < 0.000001)
                ?? throw new InvalidDataException($"{workId} has no semantic analysis at {threshold}.");
            foreach (var accepted in selected["accepted"]?.AsArray().OfType<JsonObject>() ?? [])
                discoveries.Add(new WorkDiscovery(workId, language,
                    accepted["kind"]?.GetValue<string>() ?? throw new InvalidDataException("Discovery kind is missing."),
                    accepted["value"]?.GetValue<string>() ?? throw new InvalidDataException("Discovery value is missing.")));
        }
        return discoveries;
    }

    private static IReadOnlyList<ReviewedWorkAlias> LoadAliases(string path)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(path))?.AsObject()
            ?? throw new InvalidDataException("Reviewed alias file is empty.");
        return root["aliases"]?.AsArray().OfType<JsonObject>().Select(item => new ReviewedWorkAlias(
            item["work"]?.GetValue<string>() ?? throw new InvalidDataException("Alias work is missing."),
            item["language"]?.GetValue<string>() ?? throw new InvalidDataException("Alias language is missing."),
            item["value"]?.GetValue<string>() ?? throw new InvalidDataException("Alias value is missing."),
            item["reviewed_by"]?.GetValue<string>() ?? throw new InvalidDataException("Alias reviewer is missing.")))
            .ToArray() ?? throw new InvalidDataException("Reviewed alias array is missing.");
    }

    private static string? Value(SqliteDataReader rows, int ordinal) =>
        rows.IsDBNull(ordinal) ? null : rows.GetString(ordinal);

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
