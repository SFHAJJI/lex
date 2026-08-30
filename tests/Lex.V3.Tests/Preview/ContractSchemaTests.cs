using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class ContractSchemaTests
{
    [TestMethod]
    public void SyntheticSliceSchemaGraphOwnsThreeResourcesAndReusesThreePreviewResources()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                V3SchemaIds.SyntheticSliceArtifact,
                V3SchemaIds.SyntheticSliceControl,
                V3SchemaIds.SyntheticResolveEnvelope,
            },
            SyntheticSliceSchemaGraph.OwnedSchemaIds.ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                V3SchemaIds.SyntheticSliceArtifact,
                V3SchemaIds.SyntheticSliceControl,
                V3SchemaIds.SyntheticResolveEnvelope,
                V3SchemaIds.PreviewOperationCatalog,
                V3SchemaIds.PreviewRefusalRegistry,
                V3SchemaIds.PreviewObjectSet,
            },
            SyntheticSliceSchemaGraph.SchemaIds.ToArray());
    }

    [TestMethod]
    public void ExportedSchemaTableBindsTheExactSixGeneratedResources()
    {
        var table = SyntheticSliceSchemaExporter.ExportSchemaTable();

        Assert.HasCount(6, table.Members);
        CollectionAssert.AreEqual(
            SyntheticSliceSchemaGraph.SchemaIds.ToArray(),
            table.Members.Select(static member => member.Schema).ToArray());

        foreach (var member in table.Members)
        {
            var bytes = SyntheticSliceSchemaGraph.OwnedSchemaIds.Contains(member.Schema)
                ? SyntheticSliceSchemaExporter.ExportUtf8(member.Schema)
                : PreviewSchemaExporter.ExportUtf8(member.Schema);
            Assert.AreEqual(
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                member.Sha256,
                member.Schema);
            Assert.AreEqual(bytes.LongLength, member.Bytes, member.Schema);
            Assert.AreEqual(V3SchemaResourceIds.ForWireSchema(member.Schema), member.SchemaResource);
        }
    }

    [TestMethod]
    public void NormalizationProfileDigestBindsTheExactOrderedDescriptor()
    {
        var profile = SyntheticNormalizationProfile.PlainV1;
        var bytes = Encoding.UTF8.GetBytes(
            "lex-v3-profile-descriptor\0" + profile.Descriptor);
        var independentlyComputed = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.AreEqual("synthetic-plain/1", profile.ProfileId);
        Assert.AreEqual(independentlyComputed, profile.Sha256);
        CollectionAssert.AreEqual(
            new[]
            {
                "strict_utf8_without_replacement",
                "crlf_to_lf",
                "lone_cr_to_lf",
                "unicode_nfc",
                "preserve_other_scalars_and_whitespace",
                "require_visible_non_whitespace",
                "utf8_without_bom",
            },
            profile.Descriptor.Split('\n'));
    }

    [TestMethod]
    public void OwnedSchemasAreStrictUtf8WithExactRetrievalIdentities()
    {
        foreach (var schemaId in SyntheticSliceSchemaGraph.OwnedSchemaIds)
        {
            var bytes = SyntheticSliceSchemaExporter.ExportUtf8(schemaId);
            Assert.IsGreaterThan(0, bytes.Length, schemaId);
            Assert.AreNotEqual((byte)0xef, bytes[0], schemaId);
            Assert.AreEqual((byte)'\n', bytes[^1], schemaId);
            Assert.IsFalse(bytes.AsSpan().Contains((byte)'\r'), schemaId);

            using var document = JsonDocument.Parse(bytes);
            Assert.AreEqual(
                V3SchemaResourceIds.ForWireSchema(schemaId),
                document.RootElement.GetProperty("$id").GetString(),
                schemaId);
        }
    }

    [TestMethod]
    public void CheckedOwnedSchemasAreTheExactExporterOutputAndCompileAsDraft202012()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        foreach (var schemaId in SyntheticSliceSchemaGraph.OwnedSchemaIds)
        {
            var exported = SyntheticSliceSchemaExporter.ExportUtf8(schemaId);
            var checkedPath = Path.Combine(
                repositoryRoot,
                "schemas",
                "v3-synthetic-preview",
                SyntheticSliceSchemaExporter.FileNameFor(schemaId));

            CollectionAssert.AreEqual(exported, File.ReadAllBytes(checkedPath), schemaId);
            _ = JsonSchema.FromText(Encoding.UTF8.GetString(exported));
        }
    }

    [TestMethod]
    public void SchemaTableRejectsMissingExtraReorderedAndOverBudgetGraphs()
    {
        var table = SyntheticSliceSchemaExporter.ExportSchemaTable();
        var members = table.Members.ToArray();

        Assert.ThrowsExactly<ArgumentException>(() =>
            new SyntheticSliceSchemaTable(members[..^1]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new SyntheticSliceSchemaTable(members.Append(members[0]).ToArray()));

        (members[0], members[1]) = (members[1], members[0]);
        Assert.ThrowsExactly<ArgumentException>(() => new SyntheticSliceSchemaTable(members));

        var oversizedTotal = SyntheticSliceSchemaGraph.SchemaIds
            .Select(schemaId => new SyntheticSliceSchemaMember(
                schemaId,
                V3SchemaResourceIds.ForWireSchema(schemaId),
                new string('a', 64),
                SyntheticSliceContractLimits.MaximumSchemaBytes))
            .ToArray();
        Assert.ThrowsExactly<ArgumentException>(() =>
            new SyntheticSliceSchemaTable(oversizedTotal));
    }

    [TestMethod]
    public void SqliteStampIsAnInternalControlDefinitionNotASeventhSchema()
    {
        using var document = JsonDocument.Parse(
            SyntheticSliceSchemaExporter.ExportUtf8(V3SchemaIds.SyntheticSliceControl));
        var root = document.RootElement;

        Assert.AreEqual(
            "#/$defs/index_stamp",
            root.GetProperty("properties").GetProperty("index_stamp").GetProperty("$ref").GetString());
        Assert.AreEqual(
            SyntheticSliceIndexStamp.SchemaIdentity,
            root.GetProperty("$defs")
                .GetProperty("index_stamp")
                .GetProperty("properties")
                .GetProperty("schema")
                .GetProperty("const")
                .GetString());
    }
}
