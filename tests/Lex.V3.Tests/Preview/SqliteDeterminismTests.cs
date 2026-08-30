using Lex.V3.Preview;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class SqliteDeterminismTests
{
    [TestMethod]
    public void DdlAndLogicalFramingHaveFrozenDigests()
    {
        using var root = new BuildTestDirectory();

        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);

        Assert.AreEqual(
            "4d7de0da7ffaf7b5d155a3c885fe3e02fdc76f4be9a6de0921058353179604dc",
            build.DdlSha256);
        Assert.AreEqual(
            "074d21a186d1f2978fe66c0042ec28d7befeff6e4eb9b4c7478119e47755c412",
            build.ScopeSha256);
        Assert.AreEqual(
            "97b9afa54441ac6ffb26a393cb2788c640c1280fa3a9bc029c30b79f6339c016",
            build.LogicalRowsSha256);
    }

    [TestMethod]
    public void RepeatedBuildsProduceIdenticalSqliteBytesAndIdentities()
    {
        using var firstRoot = new BuildTestDirectory();
        using var secondRoot = new BuildTestDirectory();

        var first = SyntheticPreviewBuilder.BuildCanonical(firstRoot.Path);
        var second = SyntheticPreviewBuilder.BuildCanonical(secondRoot.Path);

        CollectionAssert.AreEqual(File.ReadAllBytes(first.SqlitePath), File.ReadAllBytes(second.SqlitePath));
        Assert.AreEqual(first.LogicalRowsSha256, second.LogicalRowsSha256);
        Assert.AreEqual(first.ScopeSha256, second.ScopeSha256);
        Assert.AreEqual(first.BuildIdentity, second.BuildIdentity);
        Assert.AreEqual(first.SqliteSha256, second.SqliteSha256);
    }

    [TestMethod]
    public void DatabaseHasExactlyTheFrozenSixTablesAndPragmas()
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);
        using var connection = OpenReadOnly(build.SqlitePath);

        CollectionAssert.AreEqual(
            new[] { "blobs", "identifiers", "provisions", "stamp", "versions", "works" },
            ReadStrings(
                connection,
                "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name COLLATE BINARY"));
        Assert.AreEqual(4096L, ReadInt64(connection, "PRAGMA page_size"));
        Assert.AreEqual("UTF-8", ReadString(connection, "PRAGMA encoding"));
        Assert.AreEqual(0L, ReadInt64(connection, "PRAGMA auto_vacuum"));
        Assert.AreEqual("delete", ReadString(connection, "PRAGMA journal_mode"));
        Assert.AreEqual(0L, ReadInt64(connection, "SELECT count(*) FROM pragma_foreign_key_check"));
        Assert.AreEqual(0x4c563305L, ReadInt64(connection, "PRAGMA application_id"));
        Assert.AreEqual(1L, ReadInt64(connection, "PRAGMA user_version"));
        Assert.AreEqual("ok", ReadString(connection, "PRAGMA integrity_check"));
    }

    [TestMethod]
    public void CandidateOnlyIdentifierIsBoundToItsHeldWorkAndClosedEvidenceBasis()
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);
        using var connection = OpenReadOnly(build.SqlitePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT candidate.coordinate, candidate.disposition, candidate.evidence_basis,
                   held.coordinate, works.publisher
            FROM identifiers AS candidate
            JOIN works ON works.work_id = candidate.work_id
            JOIN identifiers AS held ON held.work_id = works.work_id AND held.disposition = 'held'
            WHERE candidate.family = 'historical_legal_id'
            """;
        using var reader = command.ExecuteReader();

        Assert.IsTrue(reader.Read());
        Assert.AreEqual("historical_legal_id:synthetic-preview", reader.GetString(0));
        Assert.AreEqual("candidate_only", reader.GetString(1));
        Assert.AreEqual("synthetic_fixture_declared_mapping", reader.GetString(2));
        Assert.AreEqual("eli/synthetic-preview", reader.GetString(3));
        Assert.AreEqual("lu-legilux", reader.GetString(4));
        Assert.IsFalse(reader.Read());
    }

    [TestMethod]
    public void StampStoresReadBackLogicalDigestAndBuildIdentityButNotFileDigest()
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);
        using var connection = OpenReadOnly(build.SqlitePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT logical_rows_sha256, build_identity FROM stamp";
        using var reader = command.ExecuteReader();

        Assert.IsTrue(reader.Read());
        Assert.AreEqual(build.LogicalRowsSha256, reader.GetString(0));
        Assert.AreEqual(build.BuildIdentity, reader.GetString(1));
        Assert.IsFalse(reader.Read());
        Assert.IsFalse(SyntheticSqliteIndex.Ddl.Contains("sqlite_sha256", StringComparison.Ordinal));
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string[] ReadStrings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static string ReadString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static long ReadInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
