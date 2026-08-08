using Microsoft.Data.Sqlite;

namespace Lex.RetrievalExperiment;

public sealed record WorkSearchSource(
    string Work,
    string Language,
    string GroupKey,
    string GroupIdentifier,
    string? Title,
    string? TitleShort,
    string? Hierarchy,
    string? Domains,
    string? ActForm,
    string? DocumentRole = null);

public sealed record WorkDiscovery(string Work, string Language, string Kind, string Value);

public sealed record ReviewedWorkAlias(
    string Work,
    string Language,
    string Value,
    string ReviewedBy);

public sealed record WorkSearchHit(
    string Work,
    string Language,
    string? Title,
    string Reason,
    double Score);

public static class WorkSearchIndex
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "and", "avec", "aux", "dans", "des", "du", "elle", "est", "et", "for", "les",
        "leur", "leurs", "par", "pour", "que", "qui", "sur", "the", "une", "vers",
    };

    public static void Create(
        string outputPath,
        IReadOnlyList<WorkSearchSource> sources,
        IReadOnlyList<WorkDiscovery> discoveries,
        IReadOnlyList<ReviewedWorkAlias> aliases,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (File.Exists(outputPath)) throw new IOException($"Refusing to overwrite work search index: {outputPath}");
        var sourceKeys = sources.Select(source => (source.Work, source.Language)).ToHashSet();
        if (sourceKeys.Count != sources.Count) throw new InvalidDataException("Duplicate work-language source record.");
        if (discoveries.Any(item => !sourceKeys.Contains((item.Work, item.Language))))
            throw new InvalidDataException("Discovery metadata references an unknown work-language record.");
        if (aliases.Any(item => !sourceKeys.Contains((item.Work, item.Language))
                                || string.IsNullOrWhiteSpace(item.ReviewedBy)))
            throw new InvalidDataException("Reviewed alias is unreviewed or references an unknown work-language record.");
        var collision = aliases.GroupBy(alias => (alias.Language, EnrichmentContract.Normalize(alias.Value)))
            .FirstOrDefault(group => group.Select(alias => alias.Work).Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (collision is not null)
            throw new InvalidDataException($"Reviewed alias collision for '{collision.Key.Item2}' ({collision.Key.Language}).");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? throw new ArgumentException("Output path must include a directory.", nameof(outputPath)));
        using var database = new SqliteConnection($"Data Source={Path.GetFullPath(outputPath)};Pooling=False");
        database.Open();
        using (var schema = database.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA page_size=4096;
                PRAGMA journal_mode=DELETE;
                CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                CREATE TABLE work_records(
                  work TEXT NOT NULL,
                  language TEXT NOT NULL,
                  title TEXT,
                  act_form TEXT,
                  document_role TEXT,
                  PRIMARY KEY(work,language)
                );
                CREATE TABLE work_names(
                  work TEXT NOT NULL,
                  language TEXT NOT NULL,
                  kind TEXT NOT NULL,
                  value TEXT NOT NULL,
                  normalized TEXT NOT NULL,
                  reviewed_by TEXT,
                  UNIQUE(work,language,kind,normalized)
                );
                CREATE INDEX work_names_exact ON work_names(language,normalized,kind,work);
                CREATE TABLE work_discovery(
                  work TEXT NOT NULL,
                  language TEXT NOT NULL,
                  kind TEXT NOT NULL,
                  value TEXT NOT NULL,
                  normalized TEXT NOT NULL,
                  UNIQUE(work,language,kind,normalized)
                );
                CREATE VIRTUAL TABLE work_fts USING fts5(
                  work UNINDEXED,
                  language UNINDEXED,
                  identifiers,
                  aliases,
                  titles,
                  facets,
                  discovery,
                  tokenize='unicode61 remove_diacritics 2'
                );
                """;
            schema.ExecuteNonQuery();
        }

        using var transaction = database.BeginTransaction();
        foreach (var item in (metadata ?? new Dictionary<string, string>()).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            using var insertMetadata = database.CreateCommand();
            insertMetadata.Transaction = transaction;
            insertMetadata.CommandText = "INSERT INTO metadata VALUES ($key,$value)";
            Add(insertMetadata, "$key", item.Key);
            Add(insertMetadata, "$value", item.Value);
            insertMetadata.ExecuteNonQuery();
        }
        foreach (var source in sources.OrderBy(source => source.Work, StringComparer.Ordinal)
                     .ThenBy(source => source.Language, StringComparer.Ordinal))
        {
            var workAliases = aliases.Where(alias => alias.Work == source.Work && alias.Language == source.Language)
                .OrderBy(alias => EnrichmentContract.Normalize(alias.Value), StringComparer.Ordinal).ToArray();
            var workDiscovery = discoveries.Where(item => item.Work == source.Work && item.Language == source.Language)
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => EnrichmentContract.Normalize(item.Value), StringComparer.Ordinal).ToArray();
            InsertRecord(database, transaction, source);
            InsertName(database, transaction, source, "official_identifier", source.GroupIdentifier, null);
            InsertName(database, transaction, source, "official_identifier", source.GroupKey, null);
            InsertName(database, transaction, source, "official_identifier", source.Work, null);
            InsertName(database, transaction, source, "official_title", source.Title, null);
            InsertName(database, transaction, source, "official_title", source.TitleShort, null);
            foreach (var alias in workAliases)
                InsertName(database, transaction, source, "reviewed_alias", alias.Value, alias.ReviewedBy);
            foreach (var discovery in workDiscovery)
            {
                using var insertDiscovery = database.CreateCommand();
                insertDiscovery.Transaction = transaction;
                insertDiscovery.CommandText = "INSERT INTO work_discovery VALUES ($work,$language,$kind,$value,$normalized)";
                Add(insertDiscovery, "$work", discovery.Work);
                Add(insertDiscovery, "$language", discovery.Language);
                Add(insertDiscovery, "$kind", discovery.Kind);
                Add(insertDiscovery, "$value", discovery.Value);
                Add(insertDiscovery, "$normalized", EnrichmentContract.Normalize(discovery.Value));
                insertDiscovery.ExecuteNonQuery();
            }

            using var fts = database.CreateCommand();
            fts.Transaction = transaction;
            fts.CommandText = "INSERT INTO work_fts VALUES ($work,$language,$identifiers,$aliases,$titles,$facets,$discovery)";
            Add(fts, "$work", source.Work);
            Add(fts, "$language", source.Language);
            Add(fts, "$identifiers", string.Join(' ', new[] { source.GroupIdentifier, source.GroupKey, source.Work }));
            Add(fts, "$aliases", string.Join(' ', workAliases.Select(alias => alias.Value)));
            Add(fts, "$titles", string.Join(' ', new[] { source.Title, source.TitleShort }.Where(value => value is not null)));
            Add(fts, "$facets", string.Join(' ', new[] { source.Hierarchy, source.Domains, source.ActForm }
                .Where(value => value is not null)));
            Add(fts, "$discovery", string.Join(' ', workDiscovery.Select(item => item.Value)));
            fts.ExecuteNonQuery();
        }
        transaction.Commit();
        using var optimize = database.CreateCommand();
        optimize.CommandText = "INSERT INTO work_fts(work_fts) VALUES('optimize'); VACUUM;";
        optimize.ExecuteNonQuery();
    }

    public static IReadOnlyList<WorkSearchHit> Search(
        string indexPath,
        string query,
        string? language,
        int limit)
    {
        if (limit <= 0) return [];
        var normalized = EnrichmentContract.Normalize(query);
        if (normalized.Length == 0) return [];
        var requestedRole = WorkQueryIntent.DocumentRole(normalized);
        using var database = new SqliteConnection(
            $"Data Source={Path.GetFullPath(indexPath)};Mode=ReadOnly;Pooling=False");
        database.Open();
        var hits = new List<WorkSearchHit>();
        var seen = new HashSet<(string Work, string Language)>();
        using (var exact = database.CreateCommand())
        {
            exact.CommandText = """
                SELECT n.work,n.language,r.title,n.kind
                FROM work_names n
                JOIN work_records r ON r.work=n.work AND r.language=n.language
                WHERE n.normalized=$normalized AND ($language IS NULL OR n.language=$language)
                ORDER BY CASE n.kind
                  WHEN 'official_identifier' THEN 0
                  WHEN 'reviewed_alias' THEN 1
                  ELSE 2 END,n.work,n.language
                """;
            Add(exact, "$normalized", normalized);
            Add(exact, "$language", language);
            using var rows = exact.ExecuteReader();
            while (rows.Read() && hits.Count < limit)
            {
                var key = (rows.GetString(0), rows.GetString(1));
                if (!seen.Add(key)) continue;
                var kind = rows.GetString(3);
                hits.Add(new WorkSearchHit(key.Item1, key.Item2, rows.IsDBNull(2) ? null : rows.GetString(2),
                    kind switch
                    {
                        "official_identifier" => "exact_identifier",
                        "reviewed_alias" => "exact_alias",
                        _ => "exact_title",
                    }, -1000 + hits.Count));
            }
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => (token.Length >= 3 || token.All(char.IsDigit)) && !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (tokens.Length == 0 || hits.Count >= limit) return hits;
        var ftsQuery = string.Join(" OR ", tokens.Select(token => $"\"{token.Replace("\"", "\"\"")}\""));
        using var search = database.CreateCommand();
        search.CommandText = """
            SELECT f.work,f.language,r.title,bm25(work_fts,0,0,12,10,8,3,2) AS score
            FROM work_fts f
            JOIN work_records r ON r.work=f.work AND r.language=f.language
            WHERE work_fts MATCH $query
              AND ($language IS NULL OR f.language=$language)
              AND ($document_role IS NULL OR r.document_role=$document_role)
            ORDER BY score,f.work,f.language
            LIMIT $limit
            """;
        Add(search, "$query", ftsQuery);
        Add(search, "$language", language);
        Add(search, "$document_role", requestedRole);
        Add(search, "$limit", Math.Max(limit * 2, 20));
        using var matches = search.ExecuteReader();
        while (matches.Read() && hits.Count < limit)
        {
            var key = (matches.GetString(0), matches.GetString(1));
            if (!seen.Add(key)) continue;
            hits.Add(new WorkSearchHit(key.Item1, key.Item2,
                matches.IsDBNull(2) ? null : matches.GetString(2), "work_fts", matches.GetDouble(3)));
        }
        return hits;
    }

    private static void InsertRecord(SqliteConnection database, SqliteTransaction transaction, WorkSearchSource source)
    {
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO work_records VALUES ($work,$language,$title,$act_form,$document_role)";
        Add(command, "$work", source.Work);
        Add(command, "$language", source.Language);
        Add(command, "$title", source.Title);
        Add(command, "$act_form", source.ActForm);
        Add(command, "$document_role", source.DocumentRole is null
            ? null : EnrichmentContract.Normalize(source.DocumentRole));
        command.ExecuteNonQuery();
    }

    private static void InsertName(
        SqliteConnection database,
        SqliteTransaction transaction,
        WorkSearchSource source,
        string kind,
        string? value,
        string? reviewedBy)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO work_names VALUES ($work,$language,$kind,$value,$normalized,$reviewed_by)";
        Add(command, "$work", source.Work);
        Add(command, "$language", source.Language);
        Add(command, "$kind", kind);
        Add(command, "$value", value);
        Add(command, "$normalized", EnrichmentContract.Normalize(value));
        Add(command, "$reviewed_by", reviewedBy);
        command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}

public static class WorkQueryIntent
{
    public static string? DocumentRole(string normalizedQuery)
    {
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        return tokens.Overlaps(["rectificatif", "rectificatifs", "corrigendum", "corrigenda"])
            ? "corrigendum"
            : null;
    }
}
