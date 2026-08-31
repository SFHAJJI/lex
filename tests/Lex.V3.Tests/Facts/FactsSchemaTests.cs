using System.Text.Json;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

[TestClass]
public sealed class FactsSchemaTests
{
    /// <summary>
    /// Regenerates the committed schema files. Set <c>LEX_FACTS_SCHEMA_WRITE=1</c> to run it.
    /// </summary>
    /// <remarks>
    /// This is a writing tool, not a gate. It is skipped on every ordinary run so that a schema
    /// drifting from its contract fails <see cref="CheckedSchemaFilesAreExactRuntimeExports"/>
    /// rather than being silently rewritten by the suite that is supposed to catch it.
    /// </remarks>
    [TestMethod]
    public void RegenerateCheckedSchemaFilesWhenExplicitlyRequested()
    {
        if (Environment.GetEnvironmentVariable("LEX_FACTS_SCHEMA_WRITE") != "1")
        {
            return;
        }

        var directory = Path.Combine(FindRepositoryRoot(), "schemas", "v3-facts");
        Directory.CreateDirectory(directory);
        foreach (var schemaId in FactsSchemaExporter.AllSchemaIds)
        {
            File.WriteAllBytes(
                Path.Combine(directory, FactsSchemaExporter.FileNameFor(schemaId)),
                FactsSchemaExporter.ExportUtf8(schemaId));
        }
    }

    [TestMethod]
    public void ExporterEmitsEveryFactsSchemaAsStrictUtf8()
    {
        foreach (var schemaId in FactsSchemaExporter.AllSchemaIds)
        {
            var bytes = FactsSchemaExporter.ExportUtf8(schemaId);

            Assert.IsGreaterThan(0, bytes.Length, schemaId);
            Assert.AreNotEqual((byte)0xef, bytes[0], $"{schemaId} must not carry a UTF-8 BOM.");
            Assert.AreEqual((byte)'\n', bytes[^1], $"{schemaId} must end with one LF.");
            Assert.IsFalse(bytes.AsSpan().Contains((byte)'\r'), $"{schemaId} must use LF only.");

            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowDuplicateProperties = false,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            Assert.AreEqual(
                FactsSchemaResourceIds.ForWireSchema(schemaId),
                document.RootElement.GetProperty("$id").GetString(),
                schemaId);
        }
    }

    /// <summary>
    /// The exported bytes are non-trivial. An empty or stub schema would satisfy a byte
    /// comparison against an equally empty committed file forever.
    /// </summary>
    [TestMethod]
    public void EveryFactsSchemaIsSubstantiveRatherThanAStub()
    {
        foreach (var schemaId in FactsSchemaExporter.AllSchemaIds)
        {
            var bytes = FactsSchemaExporter.ExportUtf8(schemaId);
            Assert.IsGreaterThan(
                300,
                bytes.Length,
                $"{schemaId} is too small to be a real schema.");

            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var carriesShape =
                root.TryGetProperty("properties", out var properties) &&
                    properties.EnumerateObject().Any() ||
                root.TryGetProperty("$defs", out var defs) && defs.EnumerateObject().Any();
            Assert.IsTrue(carriesShape, $"{schemaId} declares no properties and no definitions.");
        }
    }

    [TestMethod]
    public void CheckedSchemaFilesAreExactRuntimeExports()
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var schemaId in FactsSchemaExporter.AllSchemaIds)
        {
            var path = Path.Combine(
                repositoryRoot,
                "schemas",
                "v3-facts",
                FactsSchemaExporter.FileNameFor(schemaId));
            Assert.IsTrue(File.Exists(path), $"{schemaId} has no committed schema file.");

            var trackedBytes = File.ReadAllBytes(path);
            var runtimeBytes = FactsSchemaExporter.ExportUtf8(schemaId);

            CollectionAssert.AreEqual(runtimeBytes, trackedBytes, schemaId);
        }
    }

    [TestMethod]
    public void EverySchemaIdentityHasADistinctResourceIdentityAndFileName()
    {
        var resourceIds = FactsSchemaExporter.AllSchemaIds
            .Select(FactsSchemaResourceIds.ForWireSchema)
            .ToArray();
        var fileNames = FactsSchemaExporter.AllSchemaIds
            .Select(FactsSchemaExporter.FileNameFor)
            .ToArray();

        Assert.HasCount(8, FactsSchemaExporter.AllSchemaIds);
        Assert.AreEqual(resourceIds.Length, resourceIds.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(fileNames.Length, fileNames.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void AnUnknownSchemaIdentityIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => FactsSchemaResourceIds.ForWireSchema("lex-v3-facts-common/2"));
        Assert.ThrowsExactly<ArgumentException>(
            () => FactsSchemaExporter.FileNameFor("lex-v3-not-a-facts-schema/1"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new AssertFailedException("Unable to find the V3 repository root.");
    }
}
