using Lex.V3.Api;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The index copies an earlier process left in a temporary folder that is the process's own are removed before the mount
/// opens, and nothing else is: a container's /tmp outlives a restart, and a second start of the population mount would
/// otherwise need 4.3 GB of the 4 GiB one vCPU has (the review of #949).
/// </summary>
[TestClass]
public sealed class V3StaleIndexCopiesTests
{
    [TestMethod]
    public void ThePrivateCopiesAnEarlierProcessLeftAreRemovedAndNothingElse()
    {
        var directory = Directory.CreateTempSubdirectory("lex-stale-copies-").FullName;
        try
        {
            string[] copies = ["lex-v3-lu-mount-0123.sqlite", "lex-v3-eu-mount-4567.sqlite", "lex-v3-eu-mount-4567.sqlite-journal"];
            string[] others = ["lex-v3-lu-index-89ab.sqlite", "other.sqlite", "lex-v3-lu-mount-notes.txt", "keep.json"];
            foreach (var name in copies.Concat(others))
            {
                File.WriteAllText(Path.Combine(directory, name), name);
            }

            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            File.WriteAllText(Path.Combine(directory, "nested", "lex-v3-lu-mount-cdef.sqlite"), "nested");

            Assert.AreEqual(3, V3StaleIndexCopies.Remove(directory));
            CollectionAssert.AreEquivalent(
                others,
                Directory.EnumerateFiles(directory).Select(Path.GetFileName).ToArray(),
                "only the readers' private copies go; the folder's other files and its subfolders stay");
            Assert.IsTrue(File.Exists(Path.Combine(directory, "nested", "lex-v3-lu-mount-cdef.sqlite")));
            Assert.AreEqual(0, V3StaleIndexCopies.Remove(Path.Combine(directory, "missing")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TheApiRemovesThemOnlyWhereTheDeploymentSaysItsTemporaryFolderIsItsOwn()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Lex.V3.Api", "Program.cs"));
        StringAssert.Contains(program,
            "if (Environment.GetEnvironmentVariable(V3StaleIndexCopies.PrivateTemporaryFolderVariable) == \"1\")");
        Assert.IsLessThan(program.IndexOf("V3CorpusMount.OpenAsync(", StringComparison.Ordinal),
            program.IndexOf("V3StaleIndexCopies.Remove(Path.GetTempPath())", StringComparison.Ordinal), "before the mount opens");
        Assert.AreEqual("LEX_V3_TEMP_IS_PRIVATE", V3StaleIndexCopies.PrivateTemporaryFolderVariable);
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
