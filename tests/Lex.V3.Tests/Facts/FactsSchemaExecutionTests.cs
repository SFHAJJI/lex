using System.Text.Json;
using Json.Schema;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Runs real documents against the generated schemas.
/// </summary>
/// <remarks>
/// Byte equality between the exporter and the committed files proves the two agree with each
/// other. It does not prove either one describes the documents the contracts actually produce:
/// an exporter emitting a structurally wrong schema would match its equally wrong committed copy
/// forever. These tests close that gap by evaluating serialized fixtures against the schemas.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class FactsSchemaExecutionTests
{
    [TestMethod]
    public void EveryContractDocumentValidatesAgainstItsOwnGeneratedSchema()
    {
        AssertValid(FactsSchemaIds.PublisherRelation, FactsFixtures.PublisherRelation());
        AssertValid(FactsSchemaIds.DerivedInverseRelation, FactsFixtures.DerivedInverse());
        AssertValid(FactsSchemaIds.LocalInboundView, FactsFixtures.InboundView());
        AssertValid(FactsSchemaIds.RelationFact, FactsFixtures.AssertedFact());
        AssertValid(FactsSchemaIds.RelationFact, FactsFixtures.CaseFactWithoutEcli());
        AssertValid(FactsSchemaIds.PublisherDate, FactsFixtures.YearOnlyDate());
        AssertValid(FactsSchemaIds.PublisherDate, FactsFixtures.OpenEndedDate());
        AssertValid(FactsSchemaIds.PublisherDateFact, FactsFixtures.DateFact());
    }

    /// <summary>
    /// The schema is load-bearing rather than permissive. A document with a required member
    /// removed must be rejected by the schema itself, not merely by the C# reader.
    /// </summary>
    [TestMethod]
    public void TheSchemaRejectsADocumentWithItsProvenanceRemoved()
    {
        var schema = BuildSchema(FactsSchemaIds.PublisherRelation);
        var node = System.Text.Json.Nodes.JsonNode
            .Parse(ContractJson.Serialize(FactsFixtures.PublisherRelation()))!
            .AsObject();
        node.Remove("observation");

        var result = schema.Evaluate(ToElement(node.ToJsonString()), Options());

        Assert.IsFalse(
            result.IsValid,
            "the publisher relation schema accepts a document with no observation");
    }

    [TestMethod]
    public void TheSchemaRejectsAnUnknownMember()
    {
        var schema = BuildSchema(FactsSchemaIds.PublisherDate);
        var node = System.Text.Json.Nodes.JsonNode
            .Parse(ContractJson.Serialize(FactsFixtures.YearOnlyDate()))!
            .AsObject();
        node["precision_note"] = "year only";

        var result = schema.Evaluate(ToElement(node.ToJsonString()), Options());

        Assert.IsFalse(result.IsValid, "the publisher date schema accepts an unknown member");
    }

    [TestMethod]
    public void TheSchemaRejectsAnUnknownEnumTerm()
    {
        var schema = BuildSchema(FactsSchemaIds.PublisherDate);
        var node = System.Text.Json.Nodes.JsonNode
            .Parse(ContractJson.Serialize(FactsFixtures.YearOnlyDate()))!
            .AsObject();
        node["precision"] = "century";

        var result = schema.Evaluate(ToElement(node.ToJsonString()), Options());

        Assert.IsFalse(result.IsValid, "the publisher date schema accepts an undeclared precision");
    }

    /// <summary>
    /// The shared definitions document publishes every value object the other schemas depend on.
    /// </summary>
    [TestMethod]
    public void TheCommonDefinitionsDocumentPublishesEveryValueObject()
    {
        using var document = JsonDocument.Parse(
            FactsSchemaExporter.ExportUtf8(FactsSchemaIds.FactsCommon));
        var defs = document.RootElement.GetProperty("$defs");

        foreach (var name in new[]
                 {
                     "transport_byte_reference",
                     "source_observation_reference",
                     "official_identity",
                     "axiom_qualifier",
                     "qualified_axiom",
                 })
        {
            Assert.IsTrue(defs.TryGetProperty(name, out var definition), $"missing {name}");
            Assert.IsTrue(
                definition.TryGetProperty("properties", out var properties) &&
                    properties.EnumerateObject().Any(),
                $"{name} declares no properties");
        }
    }

    /// <summary>
    /// A transport reference has nowhere to put a provider locator. The definition is checked
    /// directly so the guarantee is a property of the schema, not only of the fixtures.
    /// </summary>
    [TestMethod]
    public void TheTransportReferenceDefinitionAdmitsOnlyADigestAndALength()
    {
        using var document = JsonDocument.Parse(
            FactsSchemaExporter.ExportUtf8(FactsSchemaIds.FactsCommon));
        var properties = document.RootElement
            .GetProperty("$defs")
            .GetProperty("transport_byte_reference")
            .GetProperty("properties");

        var names = properties.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(new[] { "byte_length", "content_sha256" }, names);
    }

    private static void AssertValid<T>(string schemaId, T value)
    {
        var result = BuildSchema(schemaId).Evaluate(
            ToElement(ContractJson.Serialize(value)),
            Options());

        Assert.IsTrue(
            result.IsValid,
            $"{schemaId} rejects a document its own contract produced");
    }

    /// <summary>
    /// Each schema is built once. <c>JsonSchema.FromText</c> registers the document by its
    /// <c>$id</c> in a process-wide registry that refuses to overwrite, so rebuilding the same
    /// schema in a second test throws rather than returning a fresh instance.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonSchema>
        BuiltSchemas = new(StringComparer.Ordinal);

    private static JsonSchema BuildSchema(string schemaId) => BuiltSchemas.GetOrAdd(
        schemaId,
        static id => JsonSchema.FromText(
            System.Text.Encoding.UTF8.GetString(FactsSchemaExporter.ExportUtf8(id))));

    private static EvaluationOptions Options() => new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    private static JsonElement ToElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
