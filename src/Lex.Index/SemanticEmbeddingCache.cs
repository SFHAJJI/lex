using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lex.Index;

/// <summary>
/// Durable content-addressed cache for final quantized vector records. A cache entry is scoped to
/// every input that can change its bytes, so interrupted builds and later corpus expansions reuse
/// work without mixing models, runtimes, execution providers, tokenizers, or vector formats.
/// </summary>
internal sealed class SemanticEmbeddingCache : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _read;
    private readonly SqliteCommand _write;
    private readonly string _profile;
    private readonly int _recordBytes;

    public SemanticEmbeddingCache(string path, SemanticBuildOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _profile = Profile(options);
        _recordBytes = SemanticVectorWriter.RecordBytes(options.Encoder.Dimensions);
        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        using (var setup = _connection.CreateCommand())
        {
            setup.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS embeddings(
                  profile TEXT NOT NULL,
                  chunk_sha TEXT NOT NULL,
                  record BLOB NOT NULL,
                  record_sha TEXT NOT NULL,
                  PRIMARY KEY(profile, chunk_sha));
                """;
            setup.ExecuteNonQuery();
        }
        _read = _connection.CreateCommand();
        _read.CommandText = "SELECT record,record_sha FROM embeddings WHERE profile=$profile AND chunk_sha=$sha";
        _read.Parameters.AddWithValue("$profile", _profile);
        _read.Parameters.Add("$sha", SqliteType.Text);
        _read.Prepare();
        _write = _connection.CreateCommand();
        _write.CommandText = """
            INSERT INTO embeddings(profile,chunk_sha,record,record_sha) VALUES ($profile,$sha,$record,$record_sha)
            ON CONFLICT(profile,chunk_sha) DO NOTHING
            """;
        _write.Parameters.AddWithValue("$profile", _profile);
        _write.Parameters.Add("$sha", SqliteType.Text);
        _write.Parameters.Add("$record", SqliteType.Blob);
        _write.Parameters.Add("$record_sha", SqliteType.Text);
        _write.Prepare();
    }

    public bool TryRead(string chunkSha, out byte[] record)
    {
        _read.Parameters["$sha"].Value = chunkSha;
        using var result = _read.ExecuteReader();
        if (!result.Read())
        {
            record = [];
            return false;
        }
        var bytes = (byte[])result[0];
        if (bytes.Length != _recordBytes)
            throw new InvalidDataException(
                $"Embedding cache record for '{chunkSha}' has {bytes.Length} bytes; expected {_recordBytes}.");
        var actualSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!actualSha.Equals(result.GetString(1), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Embedding cache record for '{chunkSha}' failed its SHA-256 check.");
        record = bytes;
        return true;
    }

    public void Store(IReadOnlyList<(string ChunkSha, byte[] Record)> records)
    {
        if (records.Count == 0) return;
        using var transaction = _connection.BeginTransaction();
        _write.Transaction = transaction;
        foreach (var (chunkSha, record) in records)
        {
            if (record.Length != _recordBytes)
                throw new InvalidDataException("Embedding cache write has the wrong vector dimension.");
            _write.Parameters["$sha"].Value = chunkSha;
            _write.Parameters["$record"].Value = record;
            _write.Parameters["$record_sha"].Value = Convert.ToHexStringLower(SHA256.HashData(record));
            _write.ExecuteNonQuery();
        }
        transaction.Commit();
        _write.Transaction = null;
    }

    private static string Profile(SemanticBuildOptions options)
    {
        var canonical = string.Join('\n',
            options.Encoder.ModelId,
            options.Encoder.ModelRevision,
            options.ModelSha256,
            options.TokenizerSha256,
            options.VectorFormat,
            options.EmbeddingProfile,
            options.ExecutionProvider,
            options.Encoder.Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public void Dispose()
    {
        _write.Dispose();
        _read.Dispose();
        _connection.Dispose();
    }
}
