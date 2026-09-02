using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Preview;

public sealed record SyntheticSqliteProvenance(
    string Version,
    string SourceId,
    string CompileOptionsSha256);

internal sealed record SyntheticSqliteBuildResult(
    string SqlitePath,
    string SqliteSha256,
    long SqliteBytes,
    string DdlSha256,
    string ScopeCanonicalJson,
    string ScopeSha256,
    string LogicalRowsCanonicalJson,
    string LogicalRowsSha256,
    string BuildIdentity,
    SyntheticSqliteProvenance Provenance);

internal static class SyntheticSqliteIndex
{
    internal const string Ddl =
        "CREATE TABLE stamp (\n" +
        "  stamp_id INTEGER NOT NULL PRIMARY KEY CHECK (stamp_id = 1),\n" +
        "  schema_identity TEXT COLLATE BINARY NOT NULL,\n" +
        "  ddl_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(ddl_sha256) = 64),\n" +
        "  sqlite_version TEXT COLLATE BINARY NOT NULL,\n" +
        "  sqlite_source_id TEXT COLLATE BINARY NOT NULL,\n" +
        "  compile_options_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(compile_options_sha256) = 64),\n" +
        "  profile_identity TEXT COLLATE BINARY NOT NULL,\n" +
        "  profile_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(profile_sha256) = 64),\n" +
        "  logical_rows_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(logical_rows_sha256) = 64),\n" +
        "  scope_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(scope_sha256) = 64),\n" +
        "  build_identity TEXT COLLATE BINARY NOT NULL CHECK (length(build_identity) = 64)\n" +
        ") STRICT;\n" +
        "CREATE TABLE works (\n" +
        "  work_id INTEGER NOT NULL PRIMARY KEY,\n" +
        "  publisher TEXT COLLATE BINARY NOT NULL,\n" +
        "  canonical_identifier TEXT COLLATE BINARY NOT NULL UNIQUE,\n" +
        "  title TEXT COLLATE BINARY NOT NULL,\n" +
        "  synthetic INTEGER NOT NULL CHECK (synthetic = 1)\n" +
        ") STRICT;\n" +
        "CREATE TABLE versions (\n" +
        "  version_id INTEGER NOT NULL PRIMARY KEY,\n" +
        "  work_id INTEGER NOT NULL REFERENCES works(work_id),\n" +
        "  version_key TEXT COLLATE BINARY NOT NULL,\n" +
        "  source_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(source_sha256) = 64),\n" +
        "  derived_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(derived_sha256) = 64),\n" +
        "  UNIQUE (work_id, version_key)\n" +
        ") STRICT;\n" +
        "CREATE TABLE provisions (\n" +
        "  provision_id INTEGER NOT NULL PRIMARY KEY,\n" +
        "  version_id INTEGER NOT NULL REFERENCES versions(version_id),\n" +
        "  anchor TEXT COLLATE BINARY NOT NULL,\n" +
        "  ordinal INTEGER NOT NULL CHECK (ordinal > 0),\n" +
        "  blob_id INTEGER NOT NULL REFERENCES blobs(blob_id),\n" +
        "  synthetic INTEGER NOT NULL CHECK (synthetic = 1),\n" +
        "  UNIQUE (version_id, anchor)\n" +
        ") STRICT;\n" +
        "CREATE TABLE blobs (\n" +
        "  blob_id INTEGER NOT NULL PRIMARY KEY,\n" +
        "  sha256 TEXT COLLATE BINARY NOT NULL UNIQUE CHECK (length(sha256) = 64),\n" +
        "  media_type TEXT COLLATE BINARY NOT NULL,\n" +
        "  byte_count INTEGER NOT NULL CHECK (byte_count >= 0),\n" +
        "  content BLOB NOT NULL CHECK (length(content) = byte_count)\n" +
        ") STRICT;\n" +
        "CREATE TABLE identifiers (\n" +
        "  identifier_id INTEGER NOT NULL PRIMARY KEY,\n" +
        "  work_id INTEGER NOT NULL REFERENCES works(work_id),\n" +
        "  family TEXT COLLATE BINARY NOT NULL,\n" +
        "  coordinate TEXT COLLATE BINARY NOT NULL,\n" +
        "  disposition TEXT COLLATE BINARY NOT NULL CHECK (disposition IN ('held', 'candidate_only')),\n" +
        "  evidence_basis TEXT COLLATE BINARY,\n" +
        "  CHECK ((disposition = 'held' AND evidence_basis IS NULL) OR\n" +
        "         (disposition = 'candidate_only' AND evidence_basis = 'synthetic_fixture_declared_mapping')),\n" +
        "  UNIQUE (family, coordinate)\n" +
        ") STRICT;\n";

    internal static string DdlSha256 { get; } = DigestFraming.Hash(Encoding.UTF8.GetBytes(Ddl));

    internal static SyntheticSqliteBuildResult Build(
        string buildRoot,
        SyntheticTransportResult transport,
        bool includeCandidate)
    {
        var root = Path.GetFullPath(buildRoot);
        var partialPath = Path.Combine(root, "index.partial.sqlite");
        if (File.Exists(partialPath))
        {
            throw new IOException("SQLite partial output already exists.");
        }

        var logicalRows = CreateLogicalRows(transport, includeCandidate);
        var logicalRowsJson = SerializeLogicalRows(logicalRows);
        var logicalRowsSha256 = DigestFraming.Hash("lex-v3-s0-05-logical-rows", logicalRowsJson);
        var scopeJson = Encoding.UTF8.GetBytes(SyntheticSliceScope.CompleteLu.CanonicalDescriptor());
        var scopeSha256 = SyntheticSliceScope.CompleteLu.Sha256;

        SyntheticSqliteProvenance provenance;
        string buildIdentity;
        using (var connection = OpenBuildConnection(partialPath))
        {
            ConfigureDatabase(connection);
            provenance = ReadProvenance(connection);
            buildIdentity = ComputeBuildIdentity(
                transport,
                scopeSha256,
                logicalRowsSha256,
                provenance);

            using (var transaction = connection.BeginTransaction())
            {
                ExecuteNonQuery(connection, transaction, Ddl);
                InsertLogicalRows(connection, transaction, logicalRows);
                InsertStamp(
                    connection,
                    transaction,
                    logicalRowsSha256,
                    scopeSha256,
                    buildIdentity,
                    provenance);
                transaction.Commit();
            }

            var readBackJson = SerializeLogicalRows(ReadLogicalRows(connection));
            if (!logicalRowsJson.AsSpan().SequenceEqual(readBackJson))
            {
                throw new InvalidDataException("SQLite logical rows differ after read-back.");
            }
        }

        AssertNoSidecars(partialPath);
        VerifyReadOnly(partialPath, logicalRowsSha256, buildIdentity);
        var sqliteBytes = new FileInfo(partialPath).Length;
        if (sqliteBytes > 1024 * 1024)
        {
            throw new InvalidDataException("Synthetic SQLite index exceeds 1 MiB.");
        }

        var sqliteSha256 = HashFile(partialPath);
        var sqlitePath = Path.Combine(root, $"index.{sqliteSha256}.sqlite");
        File.Move(partialPath, sqlitePath, overwrite: false);
        AssertNoSidecars(sqlitePath);
        return new SyntheticSqliteBuildResult(
            sqlitePath,
            sqliteSha256,
            sqliteBytes,
            DdlSha256,
            Encoding.UTF8.GetString(scopeJson),
            scopeSha256,
            Encoding.UTF8.GetString(logicalRowsJson),
            logicalRowsSha256,
            buildIdentity,
            provenance);
    }

    private static SqliteConnection OpenBuildConnection(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void ConfigureDatabase(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, null, "PRAGMA page_size=4096");
        ExecuteNonQuery(connection, null, "PRAGMA encoding='UTF-8'");
        ExecuteNonQuery(connection, null, "PRAGMA auto_vacuum=NONE");
        var journalMode = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode=DELETE"),
            System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("SQLite refused DELETE journal mode.");
        }

        ExecuteNonQuery(connection, null, "PRAGMA synchronous=FULL");
        ExecuteNonQuery(connection, null, "PRAGMA foreign_keys=ON");
        ExecuteNonQuery(connection, null, "PRAGMA application_id=0x4c563305");
        ExecuteNonQuery(connection, null, "PRAGMA user_version=1");
        if (Convert.ToInt64(ExecuteScalar(connection, "PRAGMA foreign_keys"),
                System.Globalization.CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidDataException("SQLite foreign-key enforcement is not active.");
        }
    }

    private static SyntheticSqliteProvenance ReadProvenance(SqliteConnection connection)
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT sqlite_version(), sqlite_source_id()";
        using var reader = versionCommand.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidDataException("SQLite provenance query returned no row.");
        }

        var version = reader.GetString(0);
        var sourceId = reader.GetString(1);
        if (reader.Read())
        {
            throw new InvalidDataException("SQLite provenance query returned extra rows.");
        }

        reader.Close();
        using var optionsCommand = connection.CreateCommand();
        optionsCommand.CommandText = "PRAGMA compile_options";
        using var optionsReader = optionsCommand.ExecuteReader();
        var options = new List<string>();
        while (optionsReader.Read())
        {
            options.Add(optionsReader.GetString(0));
        }

        options.Sort(StringComparer.Ordinal);
        var optionsJson = JsonSerializer.SerializeToUtf8Bytes(options);
        return new SyntheticSqliteProvenance(version, sourceId, DigestFraming.Hash(optionsJson));
    }

    private static IReadOnlyList<LogicalTable> CreateLogicalRows(
        SyntheticTransportResult transport,
        bool includeCandidate)
    {
        var identifiers = new List<object?[]>
        {
            new object?[] { 1L, 1L, "eli", SyntheticPreviewBuildContract.HeldCoordinate, "held", null },
        };
        if (includeCandidate)
        {
            identifiers.Add(new object?[]
            {
                2L,
                1L,
                "historical_legal_id",
                SyntheticPreviewBuildContract.CandidateCoordinate,
                "candidate_only",
                SyntheticPreviewBuildContract.CandidateEvidenceBasis,
            });
        }

        return new[]
        {
            new LogicalTable(
                "works",
                new[] { "work_id", "publisher", "canonical_identifier", "title", "synthetic" },
                new object?[][]
                {
                    new object?[]
                    {
                        1L,
                        SyntheticPreviewBuildContract.Publisher,
                        SyntheticPreviewBuildContract.HeldCoordinate,
                        "Synthetic preview instrument",
                        1L,
                    },
                }),
            new LogicalTable(
                "versions",
                new[] { "version_id", "work_id", "version_key", "source_sha256", "derived_sha256" },
                new object?[][]
                {
                    new object?[] { 1L, 1L, "synthetic-v1", transport.SourceSha256, transport.DerivedSha256 },
                }),
            new LogicalTable(
                "provisions",
                new[] { "provision_id", "version_id", "anchor", "ordinal", "blob_id", "synthetic" },
                new object?[][]
                {
                    new object?[] { 1L, 1L, "article-1", 1L, 1L, 1L },
                }),
            new LogicalTable(
                "blobs",
                new[] { "blob_id", "sha256", "media_type", "byte_count", "content" },
                new object?[][]
                {
                    new object?[]
                    {
                        1L,
                        transport.DerivedSha256,
                        "text/plain;charset=utf-8",
                        transport.DerivedBytes,
                        transport.DerivedUtf8,
                    },
                }),
            new LogicalTable(
                "identifiers",
                new[] { "identifier_id", "work_id", "family", "coordinate", "disposition", "evidence_basis" },
                identifiers.ToArray()),
        };
    }

    private static byte[] SerializeLogicalRows(IReadOnlyList<LogicalTable> tables)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("tables");
            foreach (var table in tables)
            {
                writer.WriteStartObject();
                writer.WriteString("name", table.Name);
                writer.WriteStartArray("columns");
                foreach (var column in table.Columns)
                {
                    writer.WriteStringValue(column);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("rows");
                foreach (var row in table.Rows)
                {
                    writer.WriteStartArray();
                    foreach (var value in row)
                    {
                        WriteLogicalValue(writer, value);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteLogicalValue(Utf8JsonWriter writer, object? value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case null:
                writer.WriteString("type", "null");
                break;
            case long integer:
                writer.WriteString("type", "integer");
                writer.WriteNumber("value", integer);
                break;
            case string text:
                writer.WriteString("type", "text");
                writer.WriteString("value", text);
                break;
            case byte[] blob:
                writer.WriteString("type", "blob");
                writer.WriteString("value", Convert.ToHexStringLower(blob));
                break;
            default:
                throw new InvalidDataException($"Unsupported logical SQLite type {value.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    private static string ComputeBuildIdentity(
        SyntheticTransportResult transport,
        string scopeSha256,
        string logicalRowsSha256,
        SyntheticSqliteProvenance provenance)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("source_sha256", transport.SourceSha256);
            writer.WriteString("derived_sha256", transport.DerivedSha256);
            writer.WriteString("profile_identity", SyntheticPreviewBuildContract.NormalizationProfileIdentity);
            writer.WriteString("profile_sha256", SyntheticPreviewBuildContract.NormalizationProfileSha256);
            writer.WriteString("publisher", SyntheticPreviewBuildContract.Publisher);
            writer.WriteString("scope_sha256", scopeSha256);
            writer.WriteString("schema_identity", SyntheticPreviewBuildContract.SqliteSchemaIdentity);
            writer.WriteString("ddl_sha256", DdlSha256);
            writer.WriteString("sqlite_version", provenance.Version);
            writer.WriteString("sqlite_source_id", provenance.SourceId);
            writer.WriteString("compile_options_sha256", provenance.CompileOptionsSha256);
            writer.WriteString("logical_rows_sha256", logicalRowsSha256);
            writer.WriteEndObject();
        }

        return DigestFraming.Hash("lex-v3-s0-05-build-id", stream.ToArray());
    }

    private static void InsertLogicalRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<LogicalTable> tables)
    {
        var byName = tables.ToDictionary(table => table.Name, StringComparer.Ordinal);
        InsertRow(connection, transaction,
            "INSERT INTO works(work_id,publisher,canonical_identifier,title,synthetic) VALUES($p0,$p1,$p2,$p3,$p4)",
            byName["works"].Rows[0]);
        InsertRow(connection, transaction,
            "INSERT INTO versions(version_id,work_id,version_key,source_sha256,derived_sha256) VALUES($p0,$p1,$p2,$p3,$p4)",
            byName["versions"].Rows[0]);
        InsertRow(connection, transaction,
            "INSERT INTO blobs(blob_id,sha256,media_type,byte_count,content) VALUES($p0,$p1,$p2,$p3,$p4)",
            byName["blobs"].Rows[0]);
        InsertRow(connection, transaction,
            "INSERT INTO provisions(provision_id,version_id,anchor,ordinal,blob_id,synthetic) VALUES($p0,$p1,$p2,$p3,$p4,$p5)",
            byName["provisions"].Rows[0]);
        foreach (var row in byName["identifiers"].Rows)
        {
            InsertRow(connection, transaction,
                "INSERT INTO identifiers(identifier_id,work_id,family,coordinate,disposition,evidence_basis) VALUES($p0,$p1,$p2,$p3,$p4,$p5)",
                row);
        }
    }

    private static void InsertStamp(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string logicalRowsSha256,
        string scopeSha256,
        string buildIdentity,
        SyntheticSqliteProvenance provenance)
    {
        InsertRow(
            connection,
            transaction,
            """
            INSERT INTO stamp(
              stamp_id,schema_identity,ddl_sha256,sqlite_version,sqlite_source_id,
              compile_options_sha256,profile_identity,profile_sha256,
              logical_rows_sha256,scope_sha256,build_identity)
            VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10)
            """,
            new object?[]
            {
                1L,
                SyntheticPreviewBuildContract.SqliteSchemaIdentity,
                DdlSha256,
                provenance.Version,
                provenance.SourceId,
                provenance.CompileOptionsSha256,
                SyntheticPreviewBuildContract.NormalizationProfileIdentity,
                SyntheticPreviewBuildContract.NormalizationProfileSha256,
                logicalRowsSha256,
                scopeSha256,
                buildIdentity,
            });
    }

    private static void InsertRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyList<object?> values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (var index = 0; index < values.Count; index++)
        {
            command.Parameters.AddWithValue($"$p{index}", values[index] ?? DBNull.Value);
        }

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidDataException("Synthetic SQLite insert did not affect exactly one row.");
        }
    }

    private static IReadOnlyList<LogicalTable> ReadLogicalRows(SqliteConnection connection) =>
        new[]
        {
            ReadTable(connection, "works",
                new[] { "work_id", "publisher", "canonical_identifier", "title", "synthetic" },
                "SELECT work_id,publisher,canonical_identifier,title,synthetic FROM works ORDER BY work_id"),
            ReadTable(connection, "versions",
                new[] { "version_id", "work_id", "version_key", "source_sha256", "derived_sha256" },
                "SELECT version_id,work_id,version_key,source_sha256,derived_sha256 FROM versions ORDER BY version_id"),
            ReadTable(connection, "provisions",
                new[] { "provision_id", "version_id", "anchor", "ordinal", "blob_id", "synthetic" },
                "SELECT provision_id,version_id,anchor,ordinal,blob_id,synthetic FROM provisions ORDER BY provision_id"),
            ReadTable(connection, "blobs",
                new[] { "blob_id", "sha256", "media_type", "byte_count", "content" },
                "SELECT blob_id,sha256,media_type,byte_count,content FROM blobs ORDER BY blob_id"),
            ReadTable(connection, "identifiers",
                new[] { "identifier_id", "work_id", "family", "coordinate", "disposition", "evidence_basis" },
                "SELECT identifier_id,work_id,family,coordinate,disposition,evidence_basis FROM identifiers ORDER BY identifier_id"),
        };

    private static LogicalTable ReadTable(
        SqliteConnection connection,
        string name,
        string[] columns,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var index = 0; index < row.Length; index++)
            {
                row[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return new LogicalTable(name, columns, rows.ToArray());
    }

    private static void VerifyReadOnly(string path, string logicalRowsSha256, string buildIdentity)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        if (!string.Equals(
                Convert.ToString(ExecuteScalar(connection, "PRAGMA integrity_check"),
                    System.Globalization.CultureInfo.InvariantCulture),
                "ok",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Synthetic SQLite integrity check failed.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT logical_rows_sha256,build_identity FROM stamp WHERE stamp_id=1";
        using var reader = command.ExecuteReader();
        if (!reader.Read() ||
            !string.Equals(reader.GetString(0), logicalRowsSha256, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), buildIdentity, StringComparison.Ordinal) ||
            reader.Read())
        {
            throw new InvalidDataException("Synthetic SQLite stamp failed read-only verification.");
        }
    }

    private static void AssertNoSidecars(string path)
    {
        foreach (var suffix in new[] { "-journal", "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                throw new InvalidDataException($"SQLite sidecar remained after finalization: {suffix}.");
            }
        }
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return DigestFraming.Hash(stream);
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private sealed record LogicalTable(string Name, string[] Columns, object?[][] Rows);
}
