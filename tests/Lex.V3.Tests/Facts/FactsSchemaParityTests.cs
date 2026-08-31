using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Two-way parity between the generated schemas and the C# reader.
/// </summary>
/// <remarks>
/// <para>
/// Byte equality between exporter and committed file proves only that the two agree with each
/// other. Codex showed what that misses: a document carrying the wrong contract identity was
/// accepted by the generated schema and rejected by the reader, because the generator emits
/// shapes and knows nothing about a version constant.
/// </para>
/// <para>
/// Every invariant below is stated once, with the verdict each side must return. Where the
/// schema can express the invariant, both must reject. Where draft 2020-12 cannot express it,
/// the case is marked reader-only and the test asserts the divergence **in both directions**:
/// the reader rejects and the schema accepts. That turns an unknown gap into a declared one, so
/// nobody later mistakes schema validation for contract validation.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class FactsSchemaParityTests
{
    private sealed record Case(
        string Invariant,
        string SchemaId,
        Func<string> Build,
        Action<JsonObject> Break,
        bool SchemaCanExpress);

    private static IEnumerable<Case> Cases() =>
    [
        new Case(
            "contract version constant",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["schema"] = "lex-v3-publisher-relation/2",
            SchemaCanExpress: true),
        new Case(
            "one family cannot appear twice with different values",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["target"]!["identifiers"] = new JsonArray(
                new JsonObject { ["family"] = "eli", ["raw_value"] = "eli/a/b/c" },
                new JsonObject { ["family"] = "eli", ["raw_value"] = "eli/d/e/f" }),
            SchemaCanExpress: true),
        new Case(
            "a non-URI value tagged as a Cellar URI",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![0]!["raw_value"] = "62019CJ0311",
            SchemaCanExpress: true),
        new Case(
            "an http parsing authority where the reader requires https",
            FactsSchemaIds.PublisherDateFact,
            () => ContractJson.Serialize(FactsFixtures.DateFact()),
            root => root["parsed_by_authority"] = "http://example.invalid/authority",
            SchemaCanExpress: true),
        new Case(
            "a zoned sentinel marked not_open",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.OpenEndedDate()),
            root =>
            {
                root["raw_lexical_value"] = "9999-12-31Z";
                root["open_sentinel"] = "not_open";
            },
            SchemaCanExpress: true),
        new Case(
            "a publisher deadline carrying transposition evidence",
            FactsSchemaIds.PublisherDateFact,
            () => ContractJson.Serialize(
                FactsFixtures.DateFact(FactsFixtures.DayDate(), DateSemanticRole.PublisherDeadline)),
            root => root["transposition_evidence"] = "nim_record",
            SchemaCanExpress: true),
        new Case(
            "nested contract version constant",
            FactsSchemaIds.RelationFact,
            () => ContractJson.Serialize(FactsFixtures.AssertedFact()),
            root => root["publisher_asserted"]!["schema"] = "lex-v3-publisher-relation/2",
            SchemaCanExpress: true),
        new Case(
            "nested date datatype",
            FactsSchemaIds.PublisherDateFact,
            () => ContractJson.Serialize(FactsFixtures.DateFact()),
            root => root["date"]!["datatype_uri"] = "http://www.w3.org/2001/XMLSchema#dateTime",
            SchemaCanExpress: true),
        new Case(
            "nested transport digest grammar",
            FactsSchemaIds.RelationFact,
            () => ContractJson.Serialize(FactsFixtures.AssertedFact()),
            root => root["publisher_asserted"]!["observation"]!["transport_bytes"]!["content_sha256"]
                = "nothex",
            SchemaCanExpress: true),
        new Case(
            "transport digest grammar",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["observation"]!["transport_bytes"]!["content_sha256"] = "nothex",
            SchemaCanExpress: true),
        new Case(
            "negative transport length",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["observation"]!["transport_bytes"]!["byte_length"] = -1,
            SchemaCanExpress: true),
        new Case(
            "predicate is an absolute URI",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["predicate_uri"] = "consolidates",
            SchemaCanExpress: true),
        new Case(
            "scope digest grammar",
            FactsSchemaIds.LocalInboundView,
            () => ContractJson.Serialize(FactsFixtures.InboundView()),
            root => root["scope_descriptor_sha256"] = "abc",
            SchemaCanExpress: true),
        new Case(
            "identity set is not empty",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["target"]!["identifiers"] = new JsonArray(),
            SchemaCanExpress: true),
        new Case(
            "accepted date datatype",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.YearOnlyDate()),
            root => root["datatype_uri"] = "http://www.w3.org/2001/XMLSchema#dateTime",
            SchemaCanExpress: true),
        new Case(
            "parsing authority is an absolute URI",
            FactsSchemaIds.PublisherDateFact,
            () => ContractJson.Serialize(FactsFixtures.DateFact()),
            root => root["parsed_by_authority"] = "lex-lu-date-reader/1",
            SchemaCanExpress: true),
        new Case(
            "unknown member",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.YearOnlyDate()),
            root => root["smuggled"] = true,
            SchemaCanExpress: true),
        new Case(
            "unknown enum term",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.YearOnlyDate()),
            root => root["precision"] = "century",
            SchemaCanExpress: true),

        // Cross-field equalities. Draft 2020-12 has no way to compare one instance location
        // against another, so these are enforced by the reader alone and asserted as divergent.
        new Case(
            "derived inverse endpoints match the forward assertion",
            FactsSchemaIds.DerivedInverseRelation,
            () => ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            root =>
            {
                var source = root["source"]!.DeepClone();
                root["source"] = root["target"]!.DeepClone();
                root["target"] = source;
            },
            SchemaCanExpress: false),
        new Case(
            "inbound view contributors target the view target",
            FactsSchemaIds.LocalInboundView,
            () => ContractJson.Serialize(FactsFixtures.InboundView()),
            root => root["contributing_assertions"] = new JsonArray(
                JsonNode.Parse(ContractJson.Serialize(
                    FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())))!),
            SchemaCanExpress: false),
        new Case(
            "ecli state agrees with the target identity set",
            FactsSchemaIds.RelationFact,
            () => ContractJson.Serialize(FactsFixtures.CaseFactWithEcli()),
            root => root["target_ecli_state"] = "ecli_missing",
            SchemaCanExpress: false),
        new Case(
            "declared kind matches the carried payload",
            FactsSchemaIds.RelationFact,
            () => ContractJson.Serialize(FactsFixtures.AssertedFact()),
            root => root["kind"] = "local_inbound_view",
            SchemaCanExpress: true),
        new Case(
            "open sentinel is only the documented lexical value",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.OpenEndedDate()),
            root => root["raw_lexical_value"] = "1970-01-01",
            SchemaCanExpress: true),
        new Case(
            "lexical value is a real calendar date",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.DayDate()),
            root => root["raw_lexical_value"] = "2019-02-30",
            SchemaCanExpress: false),
        new Case(
            "precision matches the declared datatype",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.YearOnlyDate()),
            root =>
            {
                root["datatype_uri"] = PublisherDate.Date;
                root["raw_lexical_value"] = "2019";
            },
            SchemaCanExpress: true),
        new Case(
            "drift admitted terms are exactly the named vocabulary",
            FactsSchemaIds.VocabularyDrift,
            () => ContractJson.Serialize(FactsFixtures.Drift()),
            root => root["vocabulary"] = "date_precision",
            SchemaCanExpress: true),
    ];

    [TestMethod]
    public void EveryCleanDocumentIsAcceptedByBothSides()
    {
        foreach (var testCase in Cases())
        {
            var json = testCase.Build();
            Assert.IsTrue(
                Evaluate(testCase.SchemaId, json),
                $"{testCase.Invariant}: the schema rejects a document its own contract produced");
            Assert.IsTrue(
                ReaderAccepts(testCase.SchemaId, json),
                $"{testCase.Invariant}: the reader rejects a document it produced");
        }
    }

    [TestMethod]
    public void TheReaderRejectsEveryNamedInvariantViolation()
    {
        foreach (var testCase in Cases())
        {
            var broken = Break(testCase);
            Assert.IsFalse(
                ReaderAccepts(testCase.SchemaId, broken),
                $"{testCase.Invariant}: the reader accepted a violation");
        }
    }

    [TestMethod]
    public void TheSchemaRejectsEveryInvariantItCanExpress()
    {
        foreach (var testCase in Cases().Where(c => c.SchemaCanExpress))
        {
            Assert.IsFalse(
                Evaluate(testCase.SchemaId, Break(testCase)),
                $"{testCase.Invariant}: the schema accepted a violation it should express");
        }
    }

    /// <summary>
    /// The declared divergence, asserted rather than assumed. If a future schema keyword lets one
    /// of these be expressed, this test fails and the case must move to the expressible set.
    /// </summary>
    [TestMethod]
    public void EveryReaderOnlyInvariantIsGenuinelyBeyondTheSchema()
    {
        foreach (var testCase in Cases().Where(c => !c.SchemaCanExpress))
        {
            Assert.IsTrue(
                Evaluate(testCase.SchemaId, Break(testCase)),
                $"{testCase.Invariant}: the schema now expresses this, so it is no longer reader-only");
        }
    }

    [TestMethod]
    public void EveryInvariantIsCoveredExactlyOnce()
    {
        var names = Cases().Select(c => c.Invariant).ToArray();
        Assert.AreEqual(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.IsGreaterThan(15, names.Length);
    }

    private static string Break(Case testCase)
    {
        var root = JsonNode.Parse(testCase.Build())!.AsObject();
        testCase.Break(root);
        return root.ToJsonString();
    }

    private static bool ReaderAccepts(string schemaId, string json)
    {
        try
        {
            switch (schemaId)
            {
                case FactsSchemaIds.PublisherRelation:
                    ContractJson.Deserialize<PublisherRelation>(json);
                    return true;
                case FactsSchemaIds.DerivedInverseRelation:
                    ContractJson.Deserialize<DerivedInverseRelation>(json);
                    return true;
                case FactsSchemaIds.LocalInboundView:
                    ContractJson.Deserialize<LocalInboundView>(json);
                    return true;
                case FactsSchemaIds.RelationFact:
                    ContractJson.Deserialize<RelationFact>(json);
                    return true;
                case FactsSchemaIds.PublisherDate:
                    ContractJson.Deserialize<PublisherDate>(json);
                    return true;
                case FactsSchemaIds.PublisherDateFact:
                    ContractJson.Deserialize<PublisherDateFact>(json);
                    return true;
                case FactsSchemaIds.VocabularyDrift:
                    ContractJson.Deserialize<VocabularyDrift>(json);
                    return true;
                default:
                    throw new ArgumentException("Unknown schema identity.", nameof(schemaId));
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Evaluate(string schemaId, string json)
    {
        using var document = JsonDocument.Parse(json);
        return FactsSchemas.For(schemaId)
            .Evaluate(document.RootElement.Clone(), FactsSchemas.Options())
            .IsValid;
    }
}
