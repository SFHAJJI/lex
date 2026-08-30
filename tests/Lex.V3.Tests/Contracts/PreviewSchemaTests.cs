using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class PreviewSchemaTests
{
    [TestMethod]
    public void ExporterEmitsEveryExactPreviewSchemaAsStrictUtf8()
    {
        foreach (var schemaId in PreviewSchemaGraph.SchemaIds)
        {
            var bytes = PreviewSchemaExporter.ExportUtf8(schemaId);

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
                schemaId,
                document.RootElement.GetProperty("$id").GetString(),
                schemaId);
        }
    }

    [TestMethod]
    public void CheckedSchemaFilesAreExactRuntimeExports()
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var schemaId in PreviewSchemaGraph.SchemaIds)
        {
            var path = Path.Combine(
                repositoryRoot,
                "schemas",
                "v3-preview",
                PreviewSchemaExporter.FileNameFor(schemaId));
            var trackedBytes = File.ReadAllBytes(path);
            var runtimeBytes = PreviewSchemaExporter.ExportUtf8(schemaId);

            CollectionAssert.AreEqual(runtimeBytes, trackedBytes, schemaId);
        }
    }

    [TestMethod]
    public void StageZeroPayloadAdvertisesNothingAndCarriesNoObjectsOrResponses()
    {
        var payload = PreviewPayload.CreateStageZero();

        Assert.AreEqual(V3SchemaIds.PreviewPayload, payload.Schema);
        Assert.HasCount(0, payload.OperationCatalog.Entries);
        Assert.HasCount(0, payload.ObjectSet.Objects);
        Assert.HasCount(0, payload.Envelopes);
        Assert.HasCount(1, payload.RefusalRegistry.Entries);
        Assert.AreEqual(RefusalCode.IdentifierUnknown, payload.RefusalRegistry.Entries[0].Code);
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
