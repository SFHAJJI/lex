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
        Assert.HasCount(76, documents);
        Assert.AreEqual(76, documents.Select(document => document.SchemaId).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(76, documents.Select(document => document.FileName).Distinct(StringComparer.Ordinal).Count());

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
        foreach (var code in registry.RefusalCodes)
        {
            StringAssert.Contains(canonical, $"\"payload_schema\":\"{registry.RefusalPayloadSchema(code)}\"");
            StringAssert.Contains(canonical, registry.RefusalPayloadSchemaSha256(code));
        }
    }

    /// <summary>
    /// Each refusal code's payload schema (the plan's item 6.10) is generated from the registry and held to it: it requires exactly
    /// the fields the registry mandates for its code, in the registry's order, declares a type for each and admits the fields its
    /// producers add; the refusal schema lists the registry's twenty codes and no other.
    /// </summary>
    [TestMethod]
    public void EveryRefusalCodesPayloadSchemaRequiresExactlyTheRegistrysMandatedFields()
    {
        var registry = V3OperationRegistry.Reviewed;
        Assert.HasCount(20, registry.RefusalCodes);
        foreach (var code in registry.RefusalCodes)
        {
            using var schema = System.Text.Json.JsonDocument.Parse(V3PlatformSchemaExporter.ExportRefusalPayloadUtf8(code));
            var root = schema.RootElement;
            Assert.AreEqual($"lex-v3-{code}-payload/1", root.GetProperty("$id").GetString(), code);
            Assert.AreEqual($"{code.Replace('_', '-')}-payload.schema.json", V3PlatformSchemaExporter.FileNameFor(registry.RefusalPayloadSchema(code)), code);
            Assert.IsTrue(root.GetProperty("additionalProperties").GetBoolean(), $"{code}: a producer may stand above the mandate");
            CollectionAssert.AreEqual(
                registry.MandatoryPayloadFields(code).ToArray(),
                root.GetProperty("required").EnumerateArray().Select(static field => field.GetString()).ToArray(),
                code);
            CollectionAssert.AreEqual(
                registry.MandatoryPayloadFields(code).ToArray(),
                root.GetProperty("properties").EnumerateObject().Select(static property => property.Name).ToArray(),
                $"{code}: every mandated field has a declared schema, and only those");
        }

        using var refusal = System.Text.Json.JsonDocument.Parse(V3PlatformSchemaExporter.ExportRefusalUtf8());
        CollectionAssert.AreEqual(
            registry.RefusalCodes.ToArray(),
            refusal.RootElement.GetProperty("properties").GetProperty("code").GetProperty("enum").EnumerateArray().Select(static code => code.GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "requested_identifier", "official_source", "reason", "who_to_ask" },
            registry.MandatoryPayloadFields("out_of_corpus_scope").ToArray(),
            "out_of_corpus_scope carries the four elements 36-ideal-evaluation.md §4 mandates");
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
