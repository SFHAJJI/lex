using System.Security.Cryptography;
using Lex.V3.Contracts;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Api;

internal enum SyntheticResolutionDisposition
{
    Held,
    CandidateOnly,
}

internal sealed record SyntheticResolvedRow(
    SyntheticResolutionDisposition Disposition,
    string? EvidenceBasis,
    string Publisher,
    string CanonicalIdentifier,
    string Title,
    string VersionKey,
    string SourceSha256,
    string DerivedSha256,
    string Anchor,
    long Ordinal,
    string BlobSha256,
    string MediaType,
    byte[] Body);

internal sealed class SyntheticIndexResolver : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly SyntheticSliceBlobDescriptor sourceDescriptor;
    private readonly SyntheticSliceBlobDescriptor derivedDescriptor;
    private readonly SemaphoreSlim queryLock = new(1, 1);

    private SyntheticIndexResolver(
        SqliteConnection connection,
        SyntheticSliceBlobDescriptor sourceDescriptor,
        SyntheticSliceBlobDescriptor derivedDescriptor)
    {
        this.connection = connection;
        this.sourceDescriptor = sourceDescriptor;
        this.derivedDescriptor = derivedDescriptor;
    }

    public static SyntheticIndexResolver Open(
        string sqlitePath,
        SyntheticSliceControl control,
        bool immutableCustody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlitePath);
        ArgumentNullException.ThrowIfNull(control);
        var descriptor = control.Blobs.Single(static blob => blob.Kind == SyntheticSliceBlobKind.SqliteIndex);
        var fullPath = Path.GetFullPath(sqlitePath);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length != descriptor.Bytes)
        {
            throw new InvalidDataException("The admitted SQLite file has the wrong size.");
        }

        using (var stream = new FileStream(
                   fullPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   bufferSize: 64 * 1024,
                   FileOptions.SequentialScan))
        {
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actual, descriptor.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The admitted SQLite file digest changed before open.");
            }
        }

        var dataSource = immutableCustody
            ? new Uri(fullPath).AbsoluteUri + "?immutable=1"
            : fullPath;
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            VerifyIntegrity(connection);
            VerifyStamp(connection, control.IndexStamp);
            return new SyntheticIndexResolver(
                connection,
                control.Blobs.Single(static blob => blob.Kind == SyntheticSliceBlobKind.SourceTransport),
                control.Blobs.Single(static blob => blob.Kind == SyntheticSliceBlobKind.DerivedText));
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async ValueTask<SyntheticResolvedRow?> ResolveAsync(
        string family,
        string coordinate,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinate);
        await queryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var identifierPresent = await RequireIdentifierStateAsync(
                family,
                coordinate,
                cancellationToken).ConfigureAwait(false);
            if (!identifierPresent)
            {
                return null;
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                  i.disposition,i.evidence_basis,w.publisher,w.canonical_identifier,w.title,
                  v.version_key,v.source_sha256,v.derived_sha256,
                  p.anchor,p.ordinal,b.sha256,b.media_type,b.byte_count,b.content
                FROM identifiers AS i
                JOIN works AS w ON w.work_id=i.work_id
                JOIN versions AS v ON v.work_id=w.work_id
                JOIN provisions AS p ON p.version_id=v.version_id
                JOIN blobs AS b ON b.blob_id=p.blob_id
                WHERE i.family=$family AND i.coordinate=$coordinate
                ORDER BY v.version_id,p.ordinal
                """;
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$coordinate", coordinate);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    "The admitted synthetic identifier has an incomplete relational projection.");
            }

            var body = reader.GetFieldValue<byte[]>(13);
            var row = new SyntheticResolvedRow(
                ParseDisposition(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt64(9),
                reader.GetString(10),
                reader.GetString(11),
                body);
            var declaredBytes = reader.GetInt64(12);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
                declaredBytes != body.LongLength)
            {
                throw new InvalidDataException("The admitted synthetic identifier is not one exact row.");
            }

            ValidateRow(row, sourceDescriptor, derivedDescriptor);
            return row;
        }
        finally
        {
            queryLock.Release();
        }
    }

    private async ValueTask<bool> RequireIdentifierStateAsync(
        string family,
        string coordinate,
        CancellationToken cancellationToken)
    {
        var heldRequest = string.Equals(family, "eli", StringComparison.Ordinal) &&
            string.Equals(coordinate, "eli/synthetic-preview", StringComparison.Ordinal);
        var historicalRequest = string.Equals(family, "historical_legal_id", StringComparison.Ordinal) &&
            string.Equals(
                coordinate,
                "historical_legal_id:synthetic-preview",
                StringComparison.Ordinal);
        if (!heldRequest && !historicalRequest)
        {
            throw new InvalidDataException("The resolver request is outside the closed synthetic contract.");
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT disposition,evidence_basis FROM identifiers " +
            "WHERE family=$family AND coordinate=$coordinate ORDER BY identifier_id";
        command.Parameters.AddWithValue("$family", family);
        command.Parameters.AddWithValue("$coordinate", coordinate);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (historicalRequest)
            {
                return false;
            }

            throw new InvalidDataException("The required held identifier is missing.");
        }

        var disposition = reader.GetString(0);
        var evidenceBasis = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
            (heldRequest &&
             (!string.Equals(disposition, "held", StringComparison.Ordinal) || evidenceBasis is not null)) ||
            (historicalRequest &&
             (!string.Equals(disposition, "candidate_only", StringComparison.Ordinal) ||
              !string.Equals(
                  evidenceBasis,
                  "synthetic_fixture_declared_mapping",
                  StringComparison.Ordinal))))
        {
            throw new InvalidDataException("The admitted identifier state is not the closed synthetic state.");
        }

        return true;
    }

    public void Dispose()
    {
        connection.Dispose();
        queryLock.Dispose();
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        var result = command.ExecuteScalar() as string;
        if (!string.Equals(result, "ok", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The admitted SQLite file failed integrity_check.");
        }

        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check";
        using var reader = foreignKeys.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidDataException("The admitted SQLite file failed foreign_key_check.");
        }
    }

    private static void VerifyStamp(SqliteConnection connection, SyntheticSliceIndexStamp expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT schema_identity,ddl_sha256,sqlite_version,sqlite_source_id,
                   compile_options_sha256,logical_rows_sha256,scope_sha256,build_identity
            FROM stamp WHERE stamp_id=1
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read() ||
            !string.Equals(reader.GetString(0), expected.Schema, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), expected.DdlSha256, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(2), expected.SqliteVersion, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(3), expected.SqliteSourceId, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(4), expected.CompileOptionsSha256, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(5), expected.LogicalRowsSha256, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(6), expected.ScopeSha256, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(7), expected.BuildId, StringComparison.Ordinal) ||
            reader.Read())
        {
            throw new InvalidDataException("The admitted SQLite stamp does not match the signed control.");
        }
    }

    private static SyntheticResolutionDisposition ParseDisposition(string value) => value switch
    {
        "held" => SyntheticResolutionDisposition.Held,
        "candidate_only" => SyntheticResolutionDisposition.CandidateOnly,
        _ => throw new InvalidDataException("The admitted identifier disposition is unknown."),
    };

    private static void ValidateRow(
        SyntheticResolvedRow row,
        SyntheticSliceBlobDescriptor expectedSource,
        SyntheticSliceBlobDescriptor expectedDerived)
    {
        if (!string.Equals(row.Publisher, "lu-legilux", StringComparison.Ordinal) ||
            !string.Equals(row.CanonicalIdentifier, "eli/synthetic-preview", StringComparison.Ordinal) ||
            !string.Equals(row.Title, "Synthetic preview instrument", StringComparison.Ordinal) ||
            !string.Equals(row.VersionKey, "synthetic-v1", StringComparison.Ordinal) ||
            !string.Equals(row.SourceSha256, expectedSource.Sha256, StringComparison.Ordinal) ||
            !string.Equals(row.DerivedSha256, expectedDerived.Sha256, StringComparison.Ordinal) ||
            !string.Equals(row.Anchor, "article-1", StringComparison.Ordinal) ||
            row.Ordinal != 1 ||
            !string.Equals(row.DerivedSha256, row.BlobSha256, StringComparison.Ordinal) ||
            row.Body.LongLength != expectedDerived.Bytes ||
            !string.Equals(row.MediaType, "text/plain;charset=utf-8", StringComparison.Ordinal) ||
            !string.Equals(
                row.BlobSha256,
                Convert.ToHexString(SHA256.HashData(row.Body)).ToLowerInvariant(),
                StringComparison.Ordinal) ||
            (row.Disposition == SyntheticResolutionDisposition.Held && row.EvidenceBasis is not null) ||
            (row.Disposition == SyntheticResolutionDisposition.CandidateOnly &&
             !string.Equals(
                 row.EvidenceBasis,
                 "synthetic_fixture_declared_mapping",
                 StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The admitted synthetic row violates the closed projection contract.");
        }
    }
}
