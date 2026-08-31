using System.Text;
using Lex.V3.Preview;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class BuildOutputTests
{
    [TestMethod]
    public void SourceSetDigestBindsOrdinalNamesLengthsAndCanonicalText()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(Path.Combine(root.Path, "A.cs"), "class A {}\n", new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(root.Path, "Lex.V3.Preview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root.Path, "packages.lock.json"), "{}\n", new UTF8Encoding(false));

        var initial = SyntheticPreviewSourceDigest.Compute(root.Path);

        Assert.AreEqual(
            "a21be451fcf9fdf436890998c3f5c5261541a78632c13ac1566631683ee1287e",
            initial);
        File.Move(Path.Combine(root.Path, "A.cs"), Path.Combine(root.Path, "Z.cs"));
        Assert.AreNotEqual(initial, SyntheticPreviewSourceDigest.Compute(root.Path));
        File.Move(Path.Combine(root.Path, "Z.cs"), Path.Combine(root.Path, "A.cs"));
        File.AppendAllText(Path.Combine(root.Path, "A.cs"), "// changed\n", new UTF8Encoding(false));
        Assert.AreNotEqual(initial, SyntheticPreviewSourceDigest.Compute(root.Path));
    }

    [TestMethod]
    public void SourceSetDigestTreatsCheckoutLineEndingsAsTheSameRepositoryText()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        var sourcePath = Path.Combine(root.Path, "A.cs");
        var projectPath = Path.Combine(root.Path, "Lex.V3.Preview.csproj");
        var lockPath = Path.Combine(root.Path, "packages.lock.json");
        File.WriteAllText(sourcePath, "class A {}\n", new UTF8Encoding(false));
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n", new UTF8Encoding(false));
        File.WriteAllText(lockPath, "{}\n", new UTF8Encoding(false));
        var lfDigest = SyntheticPreviewSourceDigest.Compute(root.Path);

        File.WriteAllText(sourcePath, "class A {}\r\n", new UTF8Encoding(false));
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />\r\n", new UTF8Encoding(false));
        File.WriteAllText(lockPath, "{}\r\n", new UTF8Encoding(false));

        Assert.AreEqual(lfDigest, SyntheticPreviewSourceDigest.Compute(root.Path));
    }

    [TestMethod]
    public void SourceSetDigestCanonicalizesCrLfSplitAcrossReadBuffers()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        var sourcePath = Path.Combine(root.Path, "A.cs");
        File.WriteAllText(
            Path.Combine(root.Path, "Lex.V3.Preview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root.Path, "packages.lock.json"), "{}\n", new UTF8Encoding(false));
        var prefix = new string('a', 81_919);
        File.WriteAllText(sourcePath, prefix + "\nx\n", new UTF8Encoding(false));
        var lfDigest = SyntheticPreviewSourceDigest.Compute(root.Path);

        File.WriteAllText(sourcePath, prefix + "\r\nx\n", new UTF8Encoding(false));

        Assert.AreEqual(lfDigest, SyntheticPreviewSourceDigest.Compute(root.Path));
    }

    [TestMethod]
    public void SourceSetDigestCanonicalizesOnlyCrLfAndPreservesLoneCarriageReturns()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        var sourcePath = Path.Combine(root.Path, "A.cs");
        File.WriteAllText(
            Path.Combine(root.Path, "Lex.V3.Preview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root.Path, "packages.lock.json"), "{}\n", new UTF8Encoding(false));
        File.WriteAllText(sourcePath, "a\nb\nc\n", new UTF8Encoding(false));
        var lfDigest = SyntheticPreviewSourceDigest.Compute(root.Path);

        File.WriteAllText(sourcePath, "a\r\nb\nc\r\n", new UTF8Encoding(false));
        Assert.AreEqual(lfDigest, SyntheticPreviewSourceDigest.Compute(root.Path));

        File.WriteAllText(sourcePath, "a\rb\nc\n", new UTF8Encoding(false));
        Assert.AreNotEqual(lfDigest, SyntheticPreviewSourceDigest.Compute(root.Path));
    }

    [TestMethod]
    public void SourceSetDigestRejectsInvalidUtf8()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(
            Path.Combine(root.Path, "Lex.V3.Preview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root.Path, "packages.lock.json"), "{}\n", new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(root.Path, "A.cs"), [0x61, 0xff, 0x62]);

        Assert.ThrowsExactly<DecoderFallbackException>(() => SyntheticPreviewSourceDigest.Compute(root.Path));
    }

    [TestMethod]
    public void SourceSetDigestRejectsAnOversizedMemberBeforeReadingIt()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(
            Path.Combine(root.Path, "Lex.V3.Preview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root.Path, "packages.lock.json"), "{}\n", new UTF8Encoding(false));
        using (var oversized = File.Create(Path.Combine(root.Path, "A.cs")))
        {
            oversized.SetLength(1_048_577);
        }

        Assert.ThrowsExactly<InvalidDataException>(() => SyntheticPreviewSourceDigest.Compute(root.Path));
    }

    [TestMethod]
    public void SourceSetDigestRejectsTooManyMembers()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(
            Path.Combine(root.Path, "Lex.V3.Preview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root.Path, "packages.lock.json"), "{}\n", new UTF8Encoding(false));
        for (var index = 0; index < 255; index++)
        {
            File.WriteAllText(
                Path.Combine(root.Path, $"Source{index:D3}.cs"),
                "// bounded\n",
                new UTF8Encoding(false));
        }

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => SyntheticPreviewSourceDigest.Compute(root.Path));
        Assert.AreEqual("Preview source set contains too many members.", exception.Message);
    }

    [TestMethod]
    public void SourceSetDigestRejectsAnOversizedAggregate()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(
            Path.Combine(root.Path, "Lex.V3.Preview.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root.Path, "packages.lock.json"), "{}\n", new UTF8Encoding(false));
        for (var index = 0; index < 9; index++)
        {
            using var source = File.Create(Path.Combine(root.Path, $"Source{index:D2}.cs"));
            source.SetLength(1_000_000);
        }

        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => SyntheticPreviewSourceDigest.Compute(root.Path));
        Assert.AreEqual("Preview source bytes exceed their bound.", exception.Message);
    }

    [TestMethod]
    public void CanonicalBuildPublishesOnlyContentAddressedSuccessfulOutputs()
    {
        using var root = new BuildTestDirectory();

        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);

        StringAssert.EndsWith(build.SourcePath, $"source.{build.SourceSha256}.bin");
        StringAssert.EndsWith(build.DerivedPath, $"derived.{build.DerivedSha256}.txt");
        StringAssert.EndsWith(build.SqlitePath, $"index.{build.SqliteSha256}.sqlite");
        CollectionAssert.AreEqual(SyntheticPreviewBuildContract.CanonicalSourceUtf8.ToArray(), File.ReadAllBytes(build.SourcePath));
        CollectionAssert.AreEqual(SyntheticPreviewBuildContract.CanonicalSourceUtf8.ToArray(), File.ReadAllBytes(build.DerivedPath));
        Assert.IsFalse(File.Exists(build.SqlitePath + "-journal"));
        Assert.IsFalse(File.Exists(build.SqlitePath + "-wal"));
        Assert.IsFalse(File.Exists(build.SqlitePath + "-shm"));
    }

    [TestMethod]
    public void AlternatePersistedSourceChangesDerivedDigestSqlRowAndIndexTogether()
    {
        using var canonicalRoot = new BuildTestDirectory();
        using var changedRoot = new BuildTestDirectory();
        var changedSource = Encoding.UTF8.GetBytes(
            "LEX V3 SYNTHETIC PREVIEW\r\nArticle 1\r\nChanged synthetic detail.\r\n" +
            "This text is synthetic and has no legal authority.\r\n");

        var canonical = SyntheticPreviewBuilder.BuildCanonical(canonicalRoot.Path);
        var changed = SyntheticPreviewBuilder.Build(changedRoot.Path, changedSource);
        var sqlBody = ReadBlob(changed.SqlitePath);

        Assert.AreNotEqual(canonical.SourceSha256, changed.SourceSha256);
        Assert.AreNotEqual(canonical.DerivedSha256, changed.DerivedSha256);
        Assert.AreNotEqual(canonical.SqliteSha256, changed.SqliteSha256);
        Assert.AreNotEqual(canonical.BuildIdentity, changed.BuildIdentity);
        CollectionAssert.AreEqual(File.ReadAllBytes(changed.DerivedPath), sqlBody);
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes(
                "LEX V3 SYNTHETIC PREVIEW\nArticle 1\nChanged synthetic detail.\n" +
                "This text is synthetic and has no legal authority.\n"),
            sqlBody);
    }

    private static byte[] ReadBlob(string sqlitePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM blobs";
        return (byte[])command.ExecuteScalar()!;
    }
}
