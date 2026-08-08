using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Lex.RetrievalExperiment;

public static class EnrichmentWorkbench
{
    public static void Create(IReadOnlyList<string> indexPaths, string outputPath)
    {
        if (indexPaths.Count == 0) throw new ArgumentException("At least one source index is required.");
        if (File.Exists(outputPath)) throw new IOException($"Refusing to overwrite workbench: {outputPath}");
        var parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (parent is null) throw new ArgumentException("Output must include a directory.", nameof(outputPath));
        Directory.CreateDirectory(parent);

        using var workbench = new SqliteConnection($"Data Source={outputPath};Pooling=False" );
        workbench.Open();
        using (var schema = workbench.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA journal_mode=DELETE;
                PRAGMA foreign_keys=ON;
                CREATE TABLE source_indexes(
                  path TEXT PRIMARY KEY,
                  sha256 TEXT NOT NULL
                );
                CREATE TABLE publisher_works(
                  work TEXT NOT NULL,
                  language TEXT NOT NULL,
                  collection TEXT NOT NULL,
                  group_key TEXT NOT NULL,
                  group_identifier TEXT NOT NULL,
                  title TEXT,
                  title_short TEXT,
                  latest_valid_from TEXT NOT NULL,
                  kind TEXT,
                  hierarchy TEXT,
                  domains TEXT,
                  act_form TEXT,
                  binding_status TEXT,
                  consolidation_status TEXT,
                  source_uri TEXT,
                  source_index TEXT NOT NULL REFERENCES source_indexes(path),
                  PRIMARY KEY(work, language)
                );
                CREATE TABLE proposals(
                  proposal_id INTEGER PRIMARY KEY,
                  raw_json TEXT NOT NULL,
                  status TEXT NOT NULL,
                  reason TEXT
                );
                CREATE TABLE accepted_enrichment(
                  work TEXT NOT NULL,
                  language TEXT NOT NULL,
                  kind TEXT NOT NULL,
                  value TEXT NOT NULL,
                  normalized_value TEXT NOT NULL,
                  rank_tier TEXT NOT NULL,
                  provenance TEXT NOT NULL,
                  accepted_json TEXT NOT NULL,
                  PRIMARY KEY(work, language, kind, normalized_value),
                  FOREIGN KEY(work, language) REFERENCES publisher_works(work, language)
                );
                """;
            schema.ExecuteNonQuery();
        }

        using var transaction = workbench.BeginTransaction();
        foreach (var sourcePath in indexPaths.Select(Path.GetFullPath).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source index not found.", sourcePath);
            using (var sourceStream = File.OpenRead(sourcePath))
            using (var insertSource = workbench.CreateCommand())
            {
                insertSource.Transaction = transaction;
                insertSource.CommandText = "INSERT INTO source_indexes VALUES ($path,$sha)";
                insertSource.Parameters.AddWithValue("$path", sourcePath);
                insertSource.Parameters.AddWithValue("$sha",
                    Convert.ToHexStringLower(SHA256.HashData(sourceStream)));
                insertSource.ExecuteNonQuery();
            }

            using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False");
            source.Open();
            using var select = source.CreateCommand();
            select.CommandText = """
                WITH latest AS (
                  SELECT collection,group_key,group_identifier,language,valid_from,title,title_short,
                         kind,hierarchy,domains,act_form,binding_status,consolidation_status,source_uri,
                         ROW_NUMBER() OVER (
                           PARTITION BY collection,group_key,language
                           ORDER BY valid_from DESC
                         ) AS position
                  FROM docs
                  WHERE withdrawn=0
                )
                SELECT collection,group_key,group_identifier,language,valid_from,title,title_short,
                       kind,hierarchy,domains,act_form,binding_status,consolidation_status,source_uri
                FROM latest WHERE position=1
                ORDER BY collection,group_key,language
                """;
            using var rows = select.ExecuteReader();
            while (rows.Read())
            {
                using var insert = workbench.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO publisher_works(
                      work,language,collection,group_key,group_identifier,title,title_short,
                      latest_valid_from,kind,hierarchy,domains,act_form,binding_status,
                      consolidation_status,source_uri,source_index
                    ) VALUES (
                      $work,$language,$collection,$group_key,$group_identifier,$title,$title_short,
                      $latest_valid_from,$kind,$hierarchy,$domains,$act_form,$binding_status,
                      $consolidation_status,$source_uri,$source_index
                    )
                    """;
                var collection = rows.GetString(0);
                Add(insert, "$work", collection + ":" + rows.GetString(1));
                Add(insert, "$language", rows.GetString(3));
                Add(insert, "$collection", collection);
                Add(insert, "$group_key", rows.GetString(1));
                Add(insert, "$group_identifier", rows.GetString(2));
                Add(insert, "$title", Value(rows, 5));
                Add(insert, "$title_short", Value(rows, 6));
                Add(insert, "$latest_valid_from", rows.GetString(4));
                Add(insert, "$kind", Value(rows, 7));
                Add(insert, "$hierarchy", Value(rows, 8));
                Add(insert, "$domains", Value(rows, 9));
                Add(insert, "$act_form", Value(rows, 10));
                Add(insert, "$binding_status", Value(rows, 11));
                Add(insert, "$consolidation_status", Value(rows, 12));
                Add(insert, "$source_uri", Value(rows, 13));
                Add(insert, "$source_index", sourcePath);
                insert.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    private static object Value(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? DBNull.Value : reader.GetValue(ordinal);

    private static void Add(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
