using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lex.Index;

public sealed record ReviewedWorkAliasRow(
    string Work,
    string Language,
    string Value,
    string ReviewedBy);

public sealed record WorkEvidenceAnchor(
    string Version,
    string Anchor,
    string TextSha256);

public sealed record WorkDiscoveryRow(
    string Work,
    string Language,
    string Kind,
    string Value,
    string ModelDeployment,
    string PromptSha256,
    string SchemaSha256,
    string GeneratedAt,
    double Confidence,
    int RepeatRuns,
    double AgreementRatio,
    IReadOnlyList<WorkEvidenceAnchor> Evidence);

public sealed record WorkSearchBuildOptions(
    IReadOnlyList<ReviewedWorkAliasRow> ReviewedAliases,
    IReadOnlyList<WorkDiscoveryRow> Discovery,
    string EnrichmentDigest);

public sealed record WorkSearchHit(
    DocRow Doc, string Reason, double Score, string? MatchedValue = null);

internal sealed record WorkMatch(
    long WorkId, string Reason, double Score, string? MatchedValue = null);

/// <summary>
/// Work identity and discovery metadata. This layer never stores or rewrites legal text.
/// </summary>
public static class WorkSearch
{
    private static readonly HashSet<string> WeakKinds = new(StringComparer.Ordinal)
    {
        "description", "concept", "search_synonym", "practice_area",
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "and", "avec", "aux", "dans", "des", "du", "elle", "est", "et", "for", "les",
        "leur", "leurs", "par", "pour", "que", "qui", "sur", "the", "une", "vers",
    };

    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && output.Length > 0) output.Append(' ');
                output.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else pendingSpace = output.Length > 0;
        }
        var normalized = output.ToString();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 1 && tokens.All(token => token.Length == 1 && char.IsLetter(token[0]))
            ? string.Concat(tokens)
            : normalized;
    }

    internal static void Populate(
        SqliteConnection connection,
        IReadOnlyList<DocRow> docs,
        IReadOnlyList<ProvisionRow> provisions,
        WorkSearchBuildOptions? options,
        SemanticBuildOptions? semantic,
        SemanticVectorWriter? vectorWriter,
        SemanticEmbeddingCache? embeddingCache)
    {
        var sources = docs.GroupBy(doc => (doc.GroupKey, doc.Language))
            .Select(group => new Source(
                group.Key.GroupKey,
                group.Key.Language,
                group.OrderByDescending(doc => doc.ValidFrom, StringComparer.Ordinal).First(),
                group.Select(doc => doc.GroupIdentifier).Where(NotBlank).Distinct(StringComparer.Ordinal).ToArray(),
                group.Select(doc => doc.Title).Where(NotBlank).Distinct(StringComparer.Ordinal).Cast<string>().ToArray(),
                group.Select(doc => doc.TitleShort).Where(NotBlank).Distinct(StringComparer.Ordinal).Cast<string>().ToArray()))
            .OrderBy(source => source.Work, StringComparer.Ordinal)
            .ThenBy(source => source.Language, StringComparer.Ordinal)
            .ToArray();
        var sourceKeys = sources.Select(source => (source.Work, source.Language)).ToHashSet();
        var aliases = options?.ReviewedAliases ?? [];
        var discovery = options?.Discovery ?? [];
        if (options is not null && !IsSha(options.EnrichmentDigest))
            throw new InvalidDataException("Enrichment digest must be a SHA-256 value.");
        Validate(sourceKeys, provisions, aliases, discovery);

        var vectorInputs = new List<(long WorkId, string Kind, string? Value, string Text)>();
        foreach (var source in sources)
        {
            var workAliases = aliases.Where(alias => alias.Work == source.Work
                    && alias.Language == source.Language)
                .OrderBy(alias => Normalize(alias.Value), StringComparer.Ordinal).ToArray();
            var workDiscovery = discovery.Where(item => item.Work == source.Work
                    && item.Language == source.Language)
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => Normalize(item.Value), StringComparer.Ordinal).ToArray();

            using var record = connection.CreateCommand();
            record.CommandText = """
                INSERT INTO work_records(
                  group_key,language,title,title_short,group_identifier,hierarchy,domains,act_form)
                VALUES ($work,$language,$title,$title_short,$identifier,$hierarchy,$domains,$act_form)
                """;
            Add(record, "$work", source.Work);
            Add(record, "$language", source.Language);
            Add(record, "$title", source.Latest.Title);
            Add(record, "$title_short", source.Latest.TitleShort);
            Add(record, "$identifier", source.Latest.GroupIdentifier);
            Add(record, "$hierarchy", source.Latest.Hierarchy);
            Add(record, "$domains", source.Latest.Domains);
            Add(record, "$act_form", source.Latest.ActForm);
            record.ExecuteNonQuery();
            using var identity = connection.CreateCommand();
            identity.CommandText = "SELECT last_insert_rowid()";
            var workId = Convert.ToInt64(identity.ExecuteScalar(), CultureInfo.InvariantCulture);

            foreach (var identifier in source.Identifiers.Append(source.Work).Distinct(StringComparer.Ordinal))
                InsertName(connection, workId, "official_identifier", identifier, null);
            foreach (var title in source.Titles.Concat(source.ShortTitles).Distinct(StringComparer.Ordinal))
                InsertName(connection, workId, "official_title", title, null);
            foreach (var alias in workAliases)
                InsertName(connection, workId, "reviewed_alias", alias.Value, alias.ReviewedBy);
            foreach (var item in workDiscovery)
                InsertDiscovery(connection, workId, item);

            using var fts = connection.CreateCommand();
            fts.CommandText = """
                INSERT INTO work_fts(
                  rowid,group_key,language,identifiers,aliases,titles,facets,discovery)
                VALUES ($id,$work,$language,$identifiers,$aliases,$titles,$facets,$discovery)
                """;
            Add(fts, "$id", workId);
            Add(fts, "$work", source.Work);
            Add(fts, "$language", source.Language);
            Add(fts, "$identifiers", string.Join(' ', source.Identifiers.Append(source.Work)));
            Add(fts, "$aliases", string.Join(' ', workAliases.Select(alias => alias.Value)));
            Add(fts, "$titles", string.Join(' ', source.Titles.Concat(source.ShortTitles)));
            Add(fts, "$facets", string.Join(' ', new[]
                { source.Latest.Hierarchy, source.Latest.Domains, source.Latest.ActForm }.Where(NotBlank)));
            Add(fts, "$discovery", string.Join(' ', workDiscovery.Select(item => item.Value)));
            fts.ExecuteNonQuery();

            vectorInputs.Add((workId, "work", null,
                "names: " + string.Join(' ', workAliases.Select(alias => alias.Value)
                    .Concat(source.Titles).Concat(source.ShortTitles))
                + "\nsubjects: " + string.Join(' ', new[]
                    { source.Latest.Hierarchy, source.Latest.Domains, source.Latest.ActForm }.Where(NotBlank))));
            vectorInputs.AddRange(workDiscovery.Select(item =>
                (workId, item.Kind, (string?)item.Value, $"legal {item.Kind}: {item.Value}")));
        }
        if (semantic is not null && vectorWriter is not null)
            InsertVectors(connection, semantic, vectorWriter, embeddingCache, vectorInputs);
    }

    internal static IReadOnlyList<WorkMatch> Find(
        SqliteConnection connection, string query, string? language, int limit,
        bool includeWeakDiscovery = false)
    {
        if (limit <= 0) return [];
        var normalized = Normalize(query);
        if (normalized.Length == 0) return [];
        var hits = new List<WorkMatch>();
        var seen = new HashSet<long>();

        using (var exact = connection.CreateCommand())
        {
            exact.CommandText = """
                SELECT n.work_id,n.kind,n.normalized
                FROM work_names n
                JOIN work_records r ON r.work_id=n.work_id
                WHERE n.normalized=$normalized AND ($language IS NULL OR r.language=$language)
                ORDER BY CASE n.kind
                  WHEN 'official_identifier' THEN 0
                  WHEN 'reviewed_alias' THEN 1
                  ELSE 2 END,n.work_id
                """;
            Add(exact, "$normalized", normalized);
            Add(exact, "$language", language);
            using var rows = exact.ExecuteReader();
            while (rows.Read() && hits.Count < limit)
                AddExact(rows.GetInt64(0), rows.GetString(1), rows.GetString(2), "exact");
        }

        using (var contained = connection.CreateCommand())
        {
            contained.CommandText = """
                SELECT n.work_id,n.kind,n.normalized,length(n.normalized) AS name_length
                FROM work_names n
                JOIN work_records r ON r.work_id=n.work_id
                WHERE instr(' ' || $normalized || ' ',' ' || n.normalized || ' ') > 0
                  AND length(n.normalized) >= 4
                  AND (n.kind='reviewed_alias'
                       OR (n.kind='official_identifier' AND n.normalized GLOB '*[0-9]*')
                       OR (n.kind='official_title' AND length(n.normalized) >= 12
                           AND instr(n.normalized,' ') > 0))
                  AND ($language IS NULL OR r.language=$language)
                ORDER BY CASE n.kind
                  WHEN 'official_identifier' THEN 0
                  WHEN 'reviewed_alias' THEN 1
                  ELSE 2 END,name_length DESC,n.work_id
                """;
            Add(contained, "$normalized", normalized);
            Add(contained, "$language", language);
            using var rows = contained.ExecuteReader();
            while (rows.Read() && hits.Count < limit)
                AddExact(rows.GetInt64(0), rows.GetString(1), rows.GetString(2), "contained");
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => (token.Length >= 3 || token.All(char.IsDigit)) && !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal).Take(12).ToArray();
        if (tokens.Length == 0 || hits.Count >= limit) return hits;
        var tokenQuery = string.Join(" OR ", tokens.Select(token => $"\"{token.Replace("\"", "\"\"")}\""));
        AddFtsMatches("{identifiers aliases titles facets} : (" + tokenQuery + ")", "work_metadata");
        if (includeWeakDiscovery)
            AddFtsMatches("discovery : (" + tokenQuery + ")", "work_discovery");
        return hits;

        void AddFtsMatches(string ftsQuery, string reason)
        {
            if (hits.Count >= limit) return;
            using var search = connection.CreateCommand();
            search.CommandText = """
                SELECT rowid,bm25(work_fts,0,0,12,10,8,3,2) AS score
                FROM work_fts
                WHERE work_fts MATCH $query AND ($language IS NULL OR language=$language)
                ORDER BY score,rowid
                LIMIT $limit
                """;
            Add(search, "$query", ftsQuery);
            Add(search, "$language", language);
            Add(search, "$limit", Math.Max(20, limit * 2));
            using var matches = search.ExecuteReader();
            while (matches.Read() && hits.Count < limit)
            {
                var workId = matches.GetInt64(0);
                if (!seen.Add(workId)) continue;
                hits.Add(new WorkMatch(workId, reason, matches.GetDouble(1)));
            }
        }

        void AddExact(long workId, string kind, string matchedValue, string prefix)
        {
            if (!seen.Add(workId)) return;
            var suffix = kind switch
            {
                "official_identifier" => "identifier",
                "reviewed_alias" => "alias",
                _ => "title",
            };
            hits.Add(new WorkMatch(
                workId, $"{prefix}_{suffix}", -1000 + hits.Count, matchedValue));
        }
    }

    internal static IReadOnlyList<WorkMatch> FindSemantic(
        SqliteConnection connection, SemanticVectorReader vectors, float[] queryVector,
        int limit, bool includeWeakDiscovery = false)
    {
        if (limit <= 0) return [];
        using (var table = connection.CreateCommand())
        {
            table.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='work_vectors'";
            if (table.ExecuteScalar() is null) return [];
        }
        using var range = connection.CreateCommand();
        range.CommandText = "SELECT MIN(vector_ordinal),MAX(vector_ordinal),COUNT(*) FROM work_vectors";
        using var rangeRow = range.ExecuteReader();
        if (!rangeRow.Read() || rangeRow.GetInt64(2) == 0) return [];
        var start = rangeRow.GetInt64(0);
        var end = rangeRow.GetInt64(1);
        var count = rangeRow.GetInt64(2);
        if (end - start + 1 != count)
            throw new InvalidDataException("Work vector ordinals are not contiguous.");
        var queryBits = SemanticVectorReader.Binary(queryVector);
        var queryInt8 = SemanticVectorReader.Int8(queryVector);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.work_id,v.evidence_kind,v.vector_ordinal
            FROM work_vectors v
            WHERE $include_weak=1 OR v.evidence_kind='work'
            """;
        Add(command, "$include_weak", includeWeakDiscovery ? 1 : 0);
        using var rows = command.ExecuteReader();
        var candidateLimit = Math.Max(500, limit * 20);
        var candidates = new PriorityQueue<
            (long WorkId, string Kind, long Ordinal, int Distance), long>();
        while (rows.Read())
        {
            var workId = rows.GetInt64(0);
            var ordinal = rows.GetInt64(2);
            var distance = vectors.HammingDistance(ordinal, queryBits);
            var candidate = (workId, rows.GetString(1), ordinal, distance);
            // The smallest priority leaves first. Negating the distance and ordinal removes
            // the worst retained candidate and makes the cutoff deterministic on ties.
            var priority = -(((long)distance << 32) | (uint)ordinal);
            candidates.Enqueue(candidate, priority);
            if (candidates.Count > candidateLimit) candidates.Dequeue();
        }
        return candidates.UnorderedItems.Select(item => item.Element)
            .OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Ordinal)
            .Select(candidate => new WorkMatch(
                candidate.WorkId,
                candidate.Kind == "work" ? "semantic_work" : "semantic_concept",
                vectors.Int8Dot(candidate.Ordinal, queryInt8) / (127d * 127d)))
            .GroupBy(candidate => candidate.WorkId)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.WorkId)
            .Take(limit)
            .ToArray();
    }

    private static void Validate(
        IReadOnlySet<(string Work, string Language)> sourceKeys,
        IReadOnlyList<ProvisionRow> provisions,
        IReadOnlyList<ReviewedWorkAliasRow> aliases,
        IReadOnlyList<WorkDiscoveryRow> discovery)
    {
        if (aliases.Any(alias => !sourceKeys.Contains((alias.Work, alias.Language))
                                 || string.IsNullOrWhiteSpace(alias.ReviewedBy)))
            throw new InvalidDataException("Reviewed alias is unreviewed or references an unknown work-language record.");
        var collision = aliases.GroupBy(alias => (alias.Language, Value: Normalize(alias.Value)))
            .FirstOrDefault(group => group.Select(alias => alias.Work)
                .Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (collision is not null)
            throw new InvalidDataException(
                $"Reviewed alias collision for '{collision.Key.Value}' ({collision.Key.Language}).");

        var heldEvidence = provisions.Select(provision =>
                (provision.ProvisionId[..provision.ProvisionId.LastIndexOf('#')],
                    provision.Anchor, provision.TextSha))
            .ToHashSet();
        foreach (var item in discovery)
        {
            if (!sourceKeys.Contains((item.Work, item.Language)))
                throw new InvalidDataException("Discovery metadata references an unknown work-language record.");
            if (!WeakKinds.Contains(item.Kind))
                throw new InvalidDataException($"Discovery kind '{item.Kind}' is not a weak search field.");
            if (Normalize(item.Value).Length < 2 || item.Confidence is < 0.7 or > 1
                || item.RepeatRuns < 2 || item.AgreementRatio is < 0.8 or > 1)
                throw new InvalidDataException("Discovery metadata did not pass repeatability and confidence gates.");
            if (!IsSha(item.PromptSha256) || !IsSha(item.SchemaSha256)
                || string.IsNullOrWhiteSpace(item.ModelDeployment)
                || !DateTimeOffset.TryParse(item.GeneratedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out _))
                throw new InvalidDataException("Discovery metadata has invalid model provenance.");
            if (item.Evidence.Count == 0 || item.Evidence.Any(evidence =>
                    !IsSha(evidence.TextSha256)
                    || !heldEvidence.Contains((evidence.Version, evidence.Anchor, evidence.TextSha256))))
                throw new InvalidDataException("Discovery metadata references evidence not held by this index build.");
        }
    }

    private static void InsertName(
        SqliteConnection connection, long workId, string kind, string value, string? reviewedBy)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO work_names(work_id,kind,value,normalized,reviewed_by)
            VALUES ($id,$kind,$value,$normalized,$reviewed_by)
            """;
        Add(command, "$id", workId);
        Add(command, "$kind", kind);
        Add(command, "$value", value.Trim());
        Add(command, "$normalized", Normalize(value));
        Add(command, "$reviewed_by", reviewedBy);
        command.ExecuteNonQuery();
    }

    private static void InsertDiscovery(SqliteConnection connection, long workId, WorkDiscoveryRow item)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_discovery(
              work_id,kind,value,normalized,model_deployment,prompt_sha256,schema_sha256,
              generated_at,confidence,repeat_runs,agreement_ratio,evidence_json)
            VALUES ($id,$kind,$value,$normalized,$model,$prompt,$schema,$at,$confidence,$runs,$agreement,$evidence)
            """;
        Add(command, "$id", workId);
        Add(command, "$kind", item.Kind);
        Add(command, "$value", item.Value.Trim());
        Add(command, "$normalized", Normalize(item.Value));
        Add(command, "$model", item.ModelDeployment);
        Add(command, "$prompt", item.PromptSha256);
        Add(command, "$schema", item.SchemaSha256);
        Add(command, "$at", item.GeneratedAt);
        Add(command, "$confidence", item.Confidence);
        Add(command, "$runs", item.RepeatRuns);
        Add(command, "$agreement", item.AgreementRatio);
        Add(command, "$evidence", JsonSerializer.Serialize(item.Evidence));
        command.ExecuteNonQuery();
    }

    private static void InsertVectors(
        SqliteConnection connection,
        SemanticBuildOptions semantic,
        SemanticVectorWriter writer,
        SemanticEmbeddingCache? cache,
        IReadOnlyList<(long WorkId, string Kind, string? Value, string Text)> inputs)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long completed = 0;
        semantic.Progress?.Invoke(new SemanticBuildProgress(
            0, inputs.Count, watch.Elapsed, null, SemanticBuildStage.WorkEmbeddings));
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_vectors(
              work_id,evidence_kind,evidence_value,vector_ordinal)
            VALUES ($work,$kind,$value,$ordinal)
            """;
        command.Parameters.Add(new SqliteParameter("$work", SqliteType.Integer));
        command.Parameters.Add(new SqliteParameter("$kind", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$value", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$ordinal", SqliteType.Integer));
        var buckets = inputs.Select((input, order) => new
        {
            Input = input,
            Order = order,
            PaddingTokens = IndexBuilder.EmbeddingTokenBucket(
                semantic.Encoder.CountTokens(input.Text)),
        }).GroupBy(item => item.PaddingTokens).OrderBy(group => group.Key);
        foreach (var bucket in buckets)
        {
            if (bucket.Key > semantic.MaxBatchTokens)
                throw new InvalidDataException("A work-vector input exceeds the embedding token budget.");
            var batchSize = Math.Max(1,
                Math.Min(Math.Min(32, semantic.BatchSize), semantic.MaxBatchTokens / bucket.Key));
            foreach (var items in bucket.OrderBy(item => item.Order).Chunk(batchSize))
            {
                var batch = items.Select(item => item.Input).ToArray();
                var hashes = batch.Select(input => Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(input.Text)))).ToArray();
                var records = new byte[batch.Length][];
                var missing = new List<int>();
                for (var index = 0; index < batch.Length; index++)
                    if (cache is null || !cache.TryRead(hashes[index], out records[index]!))
                        missing.Add(index);
                if (missing.Count > 0)
                {
                    var vectors = semantic.Encoder.EncodeBatch(
                        missing.Select(index => batch[index].Text).ToArray(),
                        EmbeddingInputKind.Passage, bucket.Key);
                    if (vectors.Count != missing.Count)
                        throw new InvalidDataException("Work embedding batch returned the wrong vector count.");
                    var additions = new List<(string ChunkSha, byte[] Record)>(missing.Count);
                    for (var index = 0; index < missing.Count; index++)
                    {
                        var batchIndex = missing[index];
                        records[batchIndex] = SemanticVectorWriter.Quantize(vectors[index]);
                        additions.Add((hashes[batchIndex], records[batchIndex]));
                    }
                    cache?.Store(additions);
                }
                for (var index = 0; index < batch.Length; index++)
                {
                    var input = batch[index];
                    command.Parameters["$work"].Value = input.WorkId;
                    command.Parameters["$kind"].Value = input.Kind;
                    command.Parameters["$value"].Value = (object?)input.Value ?? DBNull.Value;
                    command.Parameters["$ordinal"].Value = writer.WriteRecord(records[index]);
                    command.ExecuteNonQuery();
                }
                completed += batch.Length;
                var remaining = TimeSpan.FromTicks(
                    (long)(watch.Elapsed.Ticks * (inputs.Count - completed) / (double)completed));
                semantic.Progress?.Invoke(new SemanticBuildProgress(
                    completed, inputs.Count, watch.Elapsed, remaining,
                    SemanticBuildStage.WorkEmbeddings));
            }
        }
    }

    private static bool IsSha(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private sealed record Source(
        string Work,
        string Language,
        DocRow Latest,
        IReadOnlyList<string> Identifiers,
        IReadOnlyList<string> Titles,
        IReadOnlyList<string> ShortTitles);
}
