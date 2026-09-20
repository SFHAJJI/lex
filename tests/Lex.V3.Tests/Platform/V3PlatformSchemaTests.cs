using System.Security.Cryptography;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

[TestClass]
public sealed class V3PlatformSchemaTests
{
    [TestMethod]
    public void EveryRegistryBindingHasAUniqueGeneratedTrackedDocument()
    {
        var documents = V3PlatformSchemaExporter.ExportReviewedDocuments();
        Assert.HasCount(56, documents);
        Assert.AreEqual(56, documents.Select(document => document.SchemaId).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(56, documents.Select(document => document.FileName).Distinct(StringComparer.Ordinal).Count());

        var root = Path.Combine(RepositoryRoot(), "schemas", "v3-platform");
        if (Environment.GetEnvironmentVariable("V3_RENDER_PLATFORM_SCHEMAS") == "1")
        {
            Directory.CreateDirectory(root);
            foreach (var document in documents)
            {
                File.WriteAllBytes(Path.Combine(root, document.FileName), document.Utf8.ToArray());
            }

            // A render is not a check: comparing the files with what was just written passes whatever they
            // held. So a regeneration writes and then fails, and only a run without the variable verifies.
            Assert.Fail("V3_RENDER_PLATFORM_SCHEMAS rendered the tracked schema documents and did not verify them: run again without the variable, which is the only run that checks anything.");
        }

        foreach (var document in documents)
        {
            CollectionAssert.AreEqual(
                document.Utf8.ToArray(),
                File.ReadAllBytes(Path.Combine(root, document.FileName)),
                document.SchemaId);
            Assert.AreEqual(
                document.Sha256,
                Convert.ToHexStringLower(SHA256.HashData(document.Utf8.Span)));
        }
    }

    [TestMethod]
    public void RegistryDigestCarriesEveryOperationSchemaDigest()
    {
        var registry = V3OperationRegistry.Reviewed;
        var canonical = System.Text.Encoding.UTF8.GetString(registry.CanonicalUtf8);
        foreach (var operation in registry.Operations)
        {
            StringAssert.Contains(canonical, operation.RequestSchemaSha256);
            StringAssert.Contains(canonical, operation.ResultSchemaSha256);
        }

        StringAssert.Contains(canonical, registry.EnvelopeSchemaSha256);
        StringAssert.Contains(canonical, registry.RefusalSchemaSha256);
    }

    [TestMethod]
    public void UnknownSchemaIdentityFailsClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            V3PlatformSchemaExporter.FileNameFor("unknown/1"));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
