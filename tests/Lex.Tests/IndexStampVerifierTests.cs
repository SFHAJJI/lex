using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using Lex.Index;
using Lex.Ingest;
using Microsoft.Data.Sqlite;

namespace Lex.Tests;

public sealed class IndexStampVerifierTests : IDisposable
{
    private readonly List<string> _files = [];
    private readonly List<string> _directories = [];

    [Fact]
    public void Corpus_stamp_uses_the_full_git_commit_required_by_strict_promotion()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null
               && !Directory.Exists(Path.Combine(root.FullName, ".git"))
               && !File.Exists(Path.Combine(root.FullName, ".git")))
            root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var expected = process.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
        process.WaitForExit(10_000);
        Assert.Equal(0, process.ExitCode);

        var method = typeof(IndexFromCorpus).GetMethod(
            "GitCommit", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var actual = Assert.IsType<string>(method.Invoke(null, [root.FullName]));

        Assert.Equal(40, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Index_build_commit_requires_and_normalizes_a_full_git_sha()
    {
        var method = typeof(IndexFromCorpus).GetMethod(
            "NormalizeCodeCommit", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var normalized = Assert.IsType<string>(method.Invoke(null, [new string('A', 40)]));
        Assert.Equal(new string('a', 40), normalized);

        var bad = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, ["abc"]));
        Assert.IsType<InvalidDataException>(bad.InnerException);
    }

    [Fact]
    public void Release_input_commit_must_match_a_clean_checkout()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-input-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        RunGit(directory, "init");
        RunGit(directory, "config", "user.name", "Lex Test");
        RunGit(directory, "config", "user.email", "lex@example.invalid");
        var input = Path.Combine(directory, "input.json");
        File.WriteAllText(input, "one", new UTF8Encoding(false));
        RunGit(directory, "add", "input.json");
        RunGit(directory, "commit", "-m", "one");
        var first = RunGit(directory, "rev-parse", "HEAD");

        File.WriteAllText(input, "two", new UTF8Encoding(false));
        RunGit(directory, "commit", "-am", "two");
        var second = RunGit(directory, "rev-parse", "HEAD");
        var method = typeof(IndexFromCorpus).GetMethod(
            "RequireCleanGitCheckout", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var wrong = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, [directory, first, "derived articles"]));
        Assert.IsType<InvalidDataException>(wrong.InnerException);

        RunGit(directory, "checkout", "--detach", first);
        Assert.Equal(first, method.Invoke(null, [directory, first, "derived articles"]));

        File.WriteAllText(input, "dirty", new UTF8Encoding(false));
        var dirty = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, [directory, first, "derived articles"]));
        Assert.IsType<InvalidDataException>(dirty.InnerException);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Strict_promotion_binds_collection_corpus_code_content_and_enrichment()
    {
        var enrichment = TempFile(".json");
        File.WriteAllText(enrichment, "reviewed enrichment", new UTF8Encoding(false));
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(enrichment)));
        var key = StampSigner.CreateKeyPem();
        var db = Build(key, digest);

        var valid = IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40), enrichment, new string('a', 40),
            new string('d', 40));
        Assert.True(valid.IsValid);
        Assert.Equal(0, valid.ExitCode);
        Assert.False(IndexStampVerifier.Verify(
            db, "lu-legilux", new string('c', 40), enrichment).CollectionMatches);
        Assert.False(IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('d', 40), enrichment).CorpusCommitMatches);
        var wrongCode = IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40), enrichment, new string('b', 40));
        Assert.False(wrongCode.CodeCommitMatches);
        Assert.Equal(5, wrongCode.ExitCode);
        var wrongArticles = IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40), enrichment, new string('a', 40),
            new string('e', 40));
        Assert.False(wrongArticles.ArticlesCommitMatches);
        Assert.Equal(5, wrongArticles.ExitCode);

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
            db, "eu-eurlex", new string('c', 40), enrichment, new string('a', 40));
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

    [Fact]
    public void Strict_promotion_rejects_an_artifact_without_bound_build_code()
    {
        var key = StampSigner.CreateKeyPem();
        var db = Build(key, new string('e', 64));
        using var connection = new SqliteConnection($"Data Source={db}");
        connection.Open();
        using (var remove = connection.CreateCommand())
        {
            remove.CommandText = "DELETE FROM stamp WHERE k='code_commit'";
            remove.ExecuteNonQuery();
        }
        Resign(connection, key);

        var verification = IndexStampVerifier.Verify(
            db, "eu-eurlex", new string('c', 40), expectedCodeCommit: new string('a', 40));
        Assert.Null(verification.CodeCommit);
        Assert.False(verification.CodeCommitMatches);
        Assert.Equal(5, verification.ExitCode);
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
            ["code_commit"] = new string('a', 40),
            ["articles_commit"] = new string('d', 40),
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

    private static string RunGit(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            Assert.Fail("Git test process timed out.");
        }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, error.Result);
        return output.Result.Trim().ToLowerInvariant();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in _files)
            try { File.Delete(file); } catch { }
        foreach (var directory in _directories)
            try { Directory.Delete(directory, true); } catch { }
    }
}
