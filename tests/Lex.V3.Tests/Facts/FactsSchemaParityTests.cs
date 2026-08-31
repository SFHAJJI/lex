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
            "a Luxembourg identity carrying an EU CELEX",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["target"]!["identifiers"] = new JsonArray(
                new JsonObject { ["family"] = "celex", ["raw_value"] = "62019CJ0311" }),
            SchemaCanExpress: true),
        new Case(
            "a Luxembourg identity carrying a Cellar persistent identifier",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["target"]!["identifiers"] = new JsonArray(
                new JsonObject
                {
                    ["family"] = "cellar_psi_uri",
                    ["raw_value"] = FactsFixtures.CellarPsiUri,
                }),
            SchemaCanExpress: true),
        new Case(
            "a NIM-shaped CELEX outside sector 7",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![2]!["raw_value"] = "62019L1937LUX_202303892",
            SchemaCanExpress: true),
        new Case(
            "a consolidation CELEX naming the 31st of February",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![2]!["raw_value"] = "02016R0679-20160231",
            SchemaCanExpress: true),
        new Case(
            "an EU ELI with a trailing newline",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"] = new JsonArray(
                new JsonObject
                {
                    ["family"] = "eli",
                    ["raw_value"] = "http://data.europa.eu/eli/reg/2016/679/oj\n",
                }),
            SchemaCanExpress: true),
        new Case(
            // Draft 2020-12 has `uniqueItems` for whole items and nothing for uniqueness by a
            // sub-property, so this one is genuinely beyond the schema rather than merely unwritten.
            "one raw value may not repeat under two families in one set",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(FactsFixtures.PublisherRelation()),
            root => root["target"]!["identifiers"] = new JsonArray(
                new JsonObject { ["family"] = "memorial", ["raw_value"] = "A512" },
                new JsonObject { ["family"] = "historical_legal_id", ["raw_value"] = "A512" }),
            SchemaCanExpress: false),
        new Case(
            "an ordinary date with a timezone beyond the XSD ceiling",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.DayDate()),
            root => root["raw_lexical_value"] = "2019-07-15+99:99",
            SchemaCanExpress: true),
        new Case(
            "a Cellar persistent identifier carrying a query string",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![1]!["raw_value"]
                = FactsFixtures.CellarPsiUri + "?view=1",
            SchemaCanExpress: true),
        new Case(
            "the CELEX persistent identifier tagged as a Cellar work",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![0]!["raw_value"] = FactsFixtures.CellarPsiUri,
            SchemaCanExpress: true),
        new Case(
            "a control character inside an ECLI",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![3]!["raw_value"] = "ECLI:EU:C\n:2020:1042",
            SchemaCanExpress: true),
        new Case(
            "an EU identity carrying the Luxembourg ELI shape",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"] = new JsonArray(
                new JsonObject { ["family"] = "eli", ["raw_value"] = "eli/etat/leg/loi/2019/07/15/a512/jo" }),
            SchemaCanExpress: true),
        new Case(
            "an impossible consolidation date",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![2]!["raw_value"] = "02016R0679-20161301",
            SchemaCanExpress: true),
        new Case(
            "a timezone beyond the XSD ceiling on the open sentinel",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.OpenEndedDate()),
            root => root["raw_lexical_value"] = "9999-12-31+99:99",
            SchemaCanExpress: true),
        new Case(
            "a resource-level Cellar URI tagged as a work",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![0]!["raw_value"] =
                FactsFixtures.CellarWorkUri + "/DOC_1",
            SchemaCanExpress: true),
        new Case(
            "a CELEX with an invalid trailing suffix",
            FactsSchemaIds.PublisherRelation,
            () => ContractJson.Serialize(
                FactsFixtures.PublisherRelation(target: FactsFixtures.EuCaseWithEcli())),
            root => root["target"]!["identifiers"]![1]!["raw_value"] = "62019CJ0311XX",
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
            "derived inverse source equals the forward assertion target",
            FactsSchemaIds.DerivedInverseRelation,
            () => ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            root => root["source"] = root["derived_from"]!["source"]!.DeepClone(),
            SchemaCanExpress: false),
        new Case(
            "derived inverse target equals the forward assertion source",
            FactsSchemaIds.DerivedInverseRelation,
            () => ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            root => root["target"] = root["derived_from"]!["target"]!.DeepClone(),
            SchemaCanExpress: false),
        new Case(
            "derived inverse inverted predicate equals the forward assertion predicate",
            FactsSchemaIds.DerivedInverseRelation,
            () => ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            root => root["predicate_uri"] = root["derived_from"]!["predicate_uri"]!.DeepClone(),
            SchemaCanExpress: false),
        new Case(
            "authorizing axiom maps this forward predicate to this inverse predicate",
            FactsSchemaIds.DerivedInverseRelation,
            () => ContractJson.Serialize(FactsFixtures.DerivedInverse()),
            root => root["authorizing_axiom"]!["object_predicate_uri"]
                = "http://data.legilux.public.lu/resource/ontology/jolux#amends",
            SchemaCanExpress: false),
        new Case(
            "inbound view contributors all target the view target",
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
            root => root["target_ecli_state"] = "ecli_not_in_this_set",
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
            // Both halves are expressible after all. A leap year is a finite alternation over the
            // last two digits plus a century rule over the first two, so the schema decides
            // 2019-02-29 as well as 2019-02-30, and neither stays with the reader.
            "a common year has no 29 February",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.DayDate()),
            root => root["raw_lexical_value"] = "2019-02-29",
            SchemaCanExpress: true),
        new Case(
            "no month has a 30 February",
            FactsSchemaIds.PublisherDate,
            () => ContractJson.Serialize(FactsFixtures.DayDate()),
            root => root["raw_lexical_value"] = "2019-02-30",
            SchemaCanExpress: true),
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
        // This iterated the cases, so its name promised the registry and it proved only the
        // subset somebody had written a case for. Four of the eight registry entries had none,
        // and raw-value duplication escaped a green suite entirely.
        var readerOnly = Cases().Where(testCase => !testCase.SchemaCanExpress).ToArray();

        CollectionAssert.AreEquivalent(
            FactsSchemaHardener.ReaderOnlyInvariants.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            readerOnly.Select(c => c.Invariant).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            "the reader-only registry and the executable reader-only cases must be one set");

        foreach (var testCase in readerOnly)
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
