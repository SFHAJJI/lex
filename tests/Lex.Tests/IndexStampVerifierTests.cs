using System.Security.Cryptography;
using System.Text;
using Lex.Index;
using Lex.Ingest;
using Microsoft.Data.Sqlite;

namespace Lex.Tests;

public sealed class IndexStampVerifierTests : IDisposable
{
    private readonly List<string> _files = [];

    [Fact]
    public void Strict_promotion_binds_collection_corpus_content_and_enrichment()
    {
        var enrichment = TempFile(".json");
        File.WriteAllText(enrichment, "reviewed enrichment", new UTF8Encoding(false));
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(enrichment)));
        var key = StampSigner.CreateKeyPem();
        var db = Build(key, digest);

        var valid = IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40), enrichment);
        Assert.True(valid.IsValid);
        Assert.Equal(0, valid.ExitCode);
        Assert.False(IndexStampVerifier.Verify(
            db, "lu-legilux", new string('c', 40), enrichment).CollectionMatches);
        Assert.False(IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('d', 40), enrichment).CorpusCommitMatches);

        var other = TempFile(".json");
        File.WriteAllText(other, "different enrichment", new UTF8Encoding(false));
        Assert.False(IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40), other).EnrichmentDigestMatches);

        using var connection = new SqliteConnection($"Data Source={db}");
        connection.Open();
        using (var remove = connection.CreateCommand())
        {
            remove.CommandText = "DELETE FROM stamp WHERE k='content_digest'";
            remove.ExecuteNonQuery();
        }
        Resign(connection, key);
        var missing = IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40), enrichment);
        Assert.False(missing.ContentDigestPresent);
        Assert.Equal(4, missing.ExitCode);
    }

    [Fact]
    public void Strict_promotion_recomputes_content_after_input_hash_validation()
    {
        var db = Build(StampSigner.CreateKeyPem(), new string('e', 64));
        using (var connection = new SqliteConnection($"Data Source={db}"))
        {
            connection.Open();
            using var tamper = connection.CreateCommand();
            tamper.CommandText = "UPDATE provisions SET text_sha=$sha";
            tamper.Parameters.AddWithValue("$sha", new string('b', 64));
            tamper.ExecuteNonQuery();
        }

        var verification = IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40));
        Assert.True(verification.SignatureValid);
        Assert.False(verification.ContentDigestMatches);
        Assert.Equal(4, verification.ExitCode);
    }

    private string Build(string key, string enrichmentDigest)
    {
        var db = TempFile(".db");
        var text = "Reporting obligations.";
        var doc = new DocRow("eu-eurlex:work:2024-01-01", "eu-eurlex", "work",
            "urn:work", "REG", "en", "2024-01-01", null, "publisher",
            "2026-08-09T00:00:00Z", false, true, true, "record", null,
            "https://example.invalid", "Official title", "Official title", null,
            "2024-01-01", null);
        var provision = new ProvisionRow($"{doc.Key}|en|2024-01-01", 0, "art_1",
            $"{doc.Key}#art_1", "article", "1", null, null, null, doc.Title, text,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        IndexBuilder.Build(db, new Dictionary<string, string>
        {
            ["collection"] = "eu-eurlex",
            ["corpus_commit"] = new string('c', 40),
            ["built_at"] = "2026-08-09T00:00:00Z",
        }, [doc], [provision], [], [], key,
            workSearch: new WorkSearchBuildOptions([], [], enrichmentDigest));
        return db;
    }

    private static void Resign(SqliteConnection connection, string key)
    {
        var stamp = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT k,v FROM stamp";
            using var rows = read.ExecuteReader();
            while (rows.Read()) stamp[rows.GetString(0)] = rows.GetString(1);
        }
        var (signature, publicKey) = StampSigner.Sign(stamp, key);
        foreach (var (name, value) in new[]
                 { (Name: "signature", Value: signature), (Name: "public_key", Value: publicKey) })
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE stamp SET v=$value WHERE k=$name";
            update.Parameters.AddWithValue("$name", name);
            update.Parameters.AddWithValue("$value", value);
            update.ExecuteNonQuery();
        }
    }

    private string TempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-stamp-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in _files)
            try { File.Delete(file); } catch { }
    }
}
