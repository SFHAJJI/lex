using System.Globalization;
using System.Reflection;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A reader serves with SQLite's temporary storage in memory, so a request writes no file: over the population mount the
/// release's privacy probe found two SQLite temporary files (<c>etilqs_*</c>) written in the container's /tmp while each of
/// nine page probes was served (2026-10-13), a sort or temporary index spilled past SQLite's cache. The verification at
/// open keeps SQLite's default; only the connections that serve are set.
/// </summary>
[TestClass]
public sealed class V3ServingTemporaryStorageTests
{
    // PRAGMA temp_store: 0 is the build's default (a file), 2 is memory.
    private const long Memory = 2;

    [TestMethod]
    public async Task BothReadersServeFromConnectionsWhoseTemporaryStorageIsInMemory()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");

        var europe = EuropeIndexBuilder.TryBuild(envelope, out var europeRefusal, out var europeDetail);
        Assert.IsNotNull(europe, $"{europeRefusal}: {europeDetail}");
        using (var reader = EuropeIndexReader.OpenAndVerify(europe.IndexRef, europe.IndexBytes.Span, corpus.ArtifactRef, europe.CapabilityManifest))
        {
            Assert.AreEqual(Memory, TemporaryStorage(SharedConnection(reader)), "the EU reader's shared connection");
        }

        var luxembourg = LuxembourgIndexBuilder.TryBuild(envelope, LuxembourgIndexBuilderTests.BuiltAt, out var luxembourgRefusal, out var luxembourgDetail);
        Assert.IsNotNull(luxembourg, $"{luxembourgRefusal}: {luxembourgDetail}");
        using (var reader = LuxembourgIndexReader.OpenAndVerify(luxembourg.IndexRef, luxembourg.IndexBytes.Span, corpus.ArtifactRef, luxembourg.CapabilityManifest))
        {
            Assert.AreEqual(Memory, TemporaryStorage(SharedConnection(reader)), "the Luxembourg reader's shared connection");
        }
    }

    [TestMethod]
    public async Task TheConnectionsASearchOrTheCountsOpenAreSetAndAPlainOpenIsNot()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var europe = EuropeIndexBuilder.TryBuild(envelope, out _, out _)!;
        var luxembourg = LuxembourgIndexBuilder.TryBuild(envelope, LuxembourgIndexBuilderTests.BuiltAt, out _, out _)!;
        var directory = Directory.CreateTempSubdirectory("lex-serving-temp-").FullName;
        try
        {
            var europePath = Path.Combine(directory, "europe.sqlite");
            var luxembourgPath = Path.Combine(directory, "luxembourg.sqlite");
            File.WriteAllBytes(europePath, europe.IndexBytes.ToArray());
            File.WriteAllBytes(luxembourgPath, luxembourg.IndexBytes.ToArray());

            using (var serving = EuropeIndexBuilder.OpenForServing(europePath))
            using (var verifying = EuropeIndexBuilder.Open(europePath, SqliteOpenMode.ReadOnly))
            {
                Assert.AreEqual(Memory, TemporaryStorage(serving), "the EU search's own connection");
                Assert.AreNotEqual(Memory, TemporaryStorage(verifying), "a connection that verifies keeps the default");
            }

            using (var serving = LuxembourgIndexBuilder.OpenForServing(luxembourgPath))
            using (var verifying = LuxembourgIndexBuilder.Open(luxembourgPath, SqliteOpenMode.ReadOnly))
            {
                Assert.AreEqual(Memory, TemporaryStorage(serving), "the Luxembourg search's and the counts' own connection");
                Assert.AreNotEqual(Memory, TemporaryStorage(verifying), "a connection that verifies keeps the default");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }

        // Every connection a reader opens of its own to serve is opened for serving.
        foreach (var source in new[] { Path.Combine("Europe", "EuropeIndexBuilder.cs"), Path.Combine("Luxembourg", "LuxembourgIndexBuilder.cs") })
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Lex.V3.Ingest", source));
            Assert.DoesNotContain("Open(_path, SqliteOpenMode.ReadOnly)", text, source);
            StringAssert.Contains(text, "OpenForServing(_path)", source);
        }
    }

    private static SqliteConnection SharedConnection(object reader) =>
        (SqliteConnection)reader.GetType().GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;

    private static long TemporaryStorage(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA temp_store";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LAUNCH-CONTRACT.md")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root is not above the test output.");
    }
}
