using Lex.Index;
using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lex.RetrievalExperiment;

public sealed record WorkSemanticHit(
    string Work,
    string Language,
    string? Title,
    double Score,
    string? DocumentRole = null,
    string EvidenceKind = "work",
    string? EvidenceValue = null);

public sealed record WorkVectorBuildInfo(long Count, int Dimensions, string ModelId, string ModelRevision);

public static class WorkVectorIndex
{
    public static WorkVectorBuildInfo Build(string workSearchPath, string outputPath, ITextEncoder encoder)
    {
        if (File.Exists(outputPath)) throw new IOException($"Refusing to overwrite work vectors: {outputPath}");
        var records = ReadRecords(workSearchPath);
        using var writer = new SemanticVectorWriter(outputPath, encoder.Dimensions);
        const int batchSize = 32;
        for (var offset = 0; offset < records.Count; offset += batchSize)
        {
            var batch = records.Skip(offset).Take(batchSize).ToArray();
            var vectors = encoder.EncodeBatch(batch.Select(record => record.EmbeddingText).ToArray(),
                EmbeddingInputKind.Passage);
            foreach (var vector in vectors) writer.Write(vector);
            Console.Error.WriteLine($"[work-vectors] {Math.Min(offset + batch.Length, records.Count)}/{records.Count}");
        }
        return new WorkVectorBuildInfo(records.Count, encoder.Dimensions, encoder.ModelId, encoder.ModelRevision);
    }

    public static (int Count, string Sha256) InputDigest(string workSearchPath)
    {
        var records = ReadRecords(workSearchPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var record in records)
        {
            foreach (var value in new[] { record.Work, record.Language, record.EmbeddingText })
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
        }
        return (records.Count, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    public static IReadOnlyList<WorkSemanticHit> Search(
        string workSearchPath,
        string vectorPath,
        ITextEncoder encoder,
        string query,
        string? language,
        int limit)
    {
        if (limit <= 0) return [];
        var records = ReadRecords(workSearchPath);
        var requestedRole = WorkQueryIntent.DocumentRole(EnrichmentContract.Normalize(query));
        using var vectors = new SemanticVectorReader(vectorPath);
        if (vectors.Count != records.Count || vectors.Dimensions != encoder.Dimensions)
            throw new InvalidDataException("Work vector artifact does not match its ordered work catalog.");
        var queryVector = encoder.Encode(query, EmbeddingInputKind.Query);
        var queryBits = SemanticVectorReader.Binary(queryVector);
        var queryInt8 = SemanticVectorReader.Int8(queryVector);
        var scan = vectors.NearestByHamming(queryBits, Math.Min(records.Count, Math.Max(256, limit * 20)));
        return scan.Select(candidate =>
            {
                var record = records[checked((int)candidate.Ordinal)];
                return new WorkSemanticHit(record.Work, record.Language, record.Title,
                    vectors.Int8Dot(candidate.Ordinal, queryInt8) / (127d * 127d),
                    record.DocumentRole, record.EvidenceKind, record.EvidenceValue);
            })
            .Where(hit => (language is null || hit.Language == language)
                          && (requestedRole is null || hit.DocumentRole == requestedRole))
            .GroupBy(hit => (hit.Work, hit.Language))
            .Select(group => group.OrderByDescending(hit => hit.Score).First())
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Work, StringComparer.Ordinal)
            .ThenBy(hit => hit.Language, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static IReadOnlyList<VectorRecord> ReadRecords(string path)
    {
        using var database = new SqliteConnection($"Data Source={Path.GetFullPath(path)};Mode=ReadOnly;Pooling=False");
        database.Open();
        using var columns = database.CreateCommand();
        columns.CommandText = "SELECT 1 FROM pragma_table_info('work_records') WHERE name='document_role'";
        var hasDocumentRole = columns.ExecuteScalar() is not null;
        using var command = database.CreateCommand();
        command.CommandText = $"""
            SELECT f.work,f.language,r.title,f.aliases,f.titles,f.facets,
                   {(hasDocumentRole ? "r.document_role" : "NULL")} AS document_role
            FROM work_fts f
            JOIN work_records r ON r.work=f.work AND r.language=f.language
            ORDER BY f.work,f.language
            """;
        using var rows = command.ExecuteReader();
        var records = new List<VectorRecord>();
        while (rows.Read())
        {
            var aliases = rows.GetString(3);
            var titles = rows.GetString(4);
            var facets = rows.GetString(5);
            records.Add(new VectorRecord(
                rows.GetString(0), rows.GetString(1), rows.IsDBNull(2) ? null : rows.GetString(2),
                $"names: {aliases} {titles}\nsubjects: {facets}",
                rows.IsDBNull(6) ? null : rows.GetString(6),
                "work", null));
        }
        rows.Close();
        using var discovery = database.CreateCommand();
        discovery.CommandText = $"""
            SELECT d.work,d.language,r.title,d.kind,d.value,
                   {(hasDocumentRole ? "r.document_role" : "NULL")} AS document_role
            FROM work_discovery d
            JOIN work_records r ON r.work=d.work AND r.language=d.language
            ORDER BY d.work,d.language,d.kind,d.normalized
            """;
        using var discoveryRows = discovery.ExecuteReader();
        while (discoveryRows.Read())
            records.Add(new VectorRecord(
                discoveryRows.GetString(0), discoveryRows.GetString(1),
                discoveryRows.IsDBNull(2) ? null : discoveryRows.GetString(2),
                $"legal {discoveryRows.GetString(3)}: {discoveryRows.GetString(4)}",
                discoveryRows.IsDBNull(5) ? null : discoveryRows.GetString(5),
                discoveryRows.GetString(3), discoveryRows.GetString(4)));
        records = records.OrderBy(record => record.Work, StringComparer.Ordinal)
            .ThenBy(record => record.Language, StringComparer.Ordinal)
            .ThenBy(record => record.EmbeddingText, StringComparer.Ordinal).ToList();
        return records;
    }

    private sealed record VectorRecord(
        string Work,
        string Language,
        string? Title,
        string EmbeddingText,
        string? DocumentRole,
        string EvidenceKind,
        string? EvidenceValue);
}

public static class WorkHybridSearch
{
    public const double WorkSemanticWeight = 0.65;
    public const double DiscoverySemanticWeight = 0.60;

    public static IReadOnlyList<WorkSearchHit> Fuse(
        IReadOnlyList<WorkSearchHit> lexical,
        IReadOnlyList<WorkSemanticHit> semantic,
        int limit)
    {
        const int rrfK = 60;
        var candidates = new Dictionary<(string Work, string Language), Candidate>();
        for (var rank = 0; rank < lexical.Count; rank++)
        {
            var hit = lexical[rank];
            var key = (hit.Work, hit.Language);
            candidates[key] = new Candidate(hit.Title, 1d / (rrfK + rank + 1), hit.Reason, true, false,
                StrongName(hit.Reason));
        }
        for (var rank = 0; rank < semantic.Count; rank++)
        {
            var hit = semantic[rank];
            var key = (hit.Work, hit.Language);
            var semanticWeight = hit.EvidenceKind == "work"
                ? WorkSemanticWeight
                : DiscoverySemanticWeight;
            var semanticScore = semanticWeight / (rrfK + rank + 1);
            if (candidates.TryGetValue(key, out var candidate))
                candidates[key] = candidate with
                {
                    Score = candidate.Score + semanticScore,
                    Semantic = true,
                    SemanticReason = SemanticReason(hit),
                };
            else
                candidates[key] = new Candidate(
                    hit.Title, semanticScore, SemanticReason(hit), false, true, false, SemanticReason(hit));
        }
        return candidates.Select(item => new WorkSearchHit(
                item.Key.Work,
                item.Key.Language,
                item.Value.Title,
                item.Value.Exact ? item.Value.Reason
                    : item.Value.Lexical && item.Value.Semantic ? $"keyword+{item.Value.SemanticReason}"
                    : item.Value.Reason,
                item.Value.Score + (item.Value.Exact ? 1 : 0)))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Work, StringComparer.Ordinal)
            .ThenBy(hit => hit.Language, StringComparer.Ordinal)
            .Take(Math.Max(0, limit))
            .ToArray();
    }

    private sealed record Candidate(
        string? Title,
        double Score,
        string Reason,
        bool Lexical,
        bool Semantic,
        bool Exact,
        string SemanticReason = "semantic_work");

    private static bool StrongName(string reason) =>
        reason.StartsWith("exact_", StringComparison.Ordinal)
        || reason.StartsWith("contained_", StringComparison.Ordinal);

    private static string SemanticReason(WorkSemanticHit hit) =>
        hit.EvidenceKind == "work" ? "semantic_work" : "semantic_concept";
}
