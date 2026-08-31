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
        AssertValid(FactsSchemaIds.RelationFact, FactsFixtures.CaseFactWithEcli());
        AssertValid(FactsSchemaIds.RelationFact, FactsFixtures.CaseFactWithoutEcli());
        AssertValid(FactsSchemaIds.VocabularyDrift, FactsFixtures.Drift());
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
        node.Remove("source_observation_id");

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
                     "official_identifier",
                     "official_identity_set",
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
    /// The positive half of the leap-year rule. A pattern that refuses 2019-02-29 and also
    /// refuses 2020-02-29 would pass every refusal test in this suite and be wrong, so the real
    /// leap day is asserted against both the reader and the generated schema.
    /// </summary>
    [TestMethod]
    public void ARealLeapDayIsAdmittedByBothSides()
    {
        foreach (var admitted in new[] { "2020-02-29", "2000-02-29", "2024-02-29", "2019-02-28" })
        {
            AssertValid(
                FactsSchemaIds.PublisherDate,
                new PublisherDate(
                    FactsSchemaIds.PublisherDate, admitted, PublisherDate.Date,
                    DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen));
        }

        foreach (var refused in new[] { "2019-02-29", "1900-02-29", "2019-02-30", "2019-04-31" })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new PublisherDate(
                    FactsSchemaIds.PublisherDate, refused, PublisherDate.Date,
                    DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen),
                refused);
        }
    }

    /// <summary>
    /// The admitted half of the year grammar. A pattern that refuses year zero and also refuses
    /// 1804 would pass every refusal case in this suite and be wrong.
    /// </summary>
    [TestMethod]
    public void RealYearsAreAdmittedByBothSides()
    {
        foreach (var year in new[] { "1804", "0001", "2026", "9999" })
        {
            AssertValid(
                FactsSchemaIds.PublisherDate,
                new PublisherDate(
                    FactsSchemaIds.PublisherDate, year, PublisherDate.GYear,
                    DatePrecision.Year, DateOpenSentinel.NotOpen));
        }

        AssertValid(
            FactsSchemaIds.PublisherDate,
            new PublisherDate(
                FactsSchemaIds.PublisherDate, "1804-03", PublisherDate.GYearMonth,
                DatePrecision.YearMonth, DateOpenSentinel.NotOpen));

        AssertValid(
            FactsSchemaIds.PublisherDate,
            new PublisherDate(
                FactsSchemaIds.PublisherDate, "1804-03-21", PublisherDate.Date,
                DatePrecision.YearMonthDay, DateOpenSentinel.NotOpen));
    }

    /// <summary>An exact Cellar resource is still admitted after the query and fragment repair.</summary>
    [TestMethod]
    public void AnExactCellarResourceIsAdmittedByBothSides()
    {
        AssertValid(
            FactsSchemaIds.PublisherRelation,
            FactsFixtures.PublisherRelation(target: FactsFixtures.EuResource()));
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

    private static JsonSchema BuildSchema(string schemaId) => FactsSchemas.For(schemaId);

    private static EvaluationOptions Options() => FactsSchemas.Options();

    private static JsonElement ToElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
