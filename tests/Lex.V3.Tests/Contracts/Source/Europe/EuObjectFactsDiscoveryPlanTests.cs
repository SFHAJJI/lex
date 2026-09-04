using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05c-1: the three new bounded row-set query-plan families (object-facts/P, expression-facts/X,
/// root-watermark/W), following the exact six-part (five for W) cursor and unbound-branch template
/// shape <c>EuConsolidationDiscoveryPlan</c>'s own <c>Family</c>/<c>TemporalFacts</c> sets establish.
/// SCOPE_RULING <c>lex-event-20260904T040718222Z-7e6f29af07024cf5b2cb716f94f288e3</c>.
/// </summary>
[TestClass]
public sealed class EuObjectFactsDiscoveryPlanTests
{
    private static readonly string ObjectValuesBlock = string.Join(
        '\n', EuObjectFactsDiscoveryPlan.BatchParameterNames().Select(static name => "        {" + name + ":iri}"));

    private const string RootA =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string RootB =
        "http://publications.europa.eu/resource/cellar/44444444-4444-4444-8444-444444444444";

    // ---- Family partition: P and X together close the thirteen CDM predicates exactly once each. ----

    [TestMethod]
    public void ObjectAndExpressionAuthorityPredicatesPartitionTheClosedThirteenExactly()
    {
        var union = EuObjectFactsDiscoveryPlan.ObjectAuthorityPredicates
            .Concat(EuObjectFactsDiscoveryPlan.ExpressionAuthorityPredicates)
            .ToArray();
        Assert.AreEqual(9, EuObjectFactsDiscoveryPlan.ObjectAuthorityPredicates.Count);
        Assert.AreEqual(4, EuObjectFactsDiscoveryPlan.ExpressionAuthorityPredicates.Count);
        Assert.AreEqual(EuScopeVocabulary.CdmPredicates.Count, union.Distinct().Count());
        CollectionAssert.AreEquivalent(EuScopeVocabulary.CdmPredicates.ToArray(), union);
    }

    // ---- Projection and cursor shapes. ----

    [TestMethod]
    public void ObjectFactsProjectionAndCursorMatchTheEstablishedSixPartPattern()
    {
        var profile = EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ObjectFacts);
        CollectionAssert.AreEqual(
            new[]
            {
                "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
                "multiplicity", "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" },
            profile.CursorVariables.ToArray());
        CollectionAssert.AreEqual(EuObjectFactsDiscoveryPlan.BatchParameterNames().ToArray(),
            profile.SelectionParameterNames.ToArray());
    }

    [TestMethod]
    public void ExpressionFactsProjectionCarriesParentAheadOfTheSameSixPartCursor()
    {
        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.ExpressionFacts);
        CollectionAssert.AreEqual(
            new[]
            {
                "parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
                "multiplicity", "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" },
            profile.CursorVariables.ToArray());
    }

    [TestMethod]
    public void RootWatermarkCursorIsFivePartNotSixBecauseThePredicateColumnWouldBeConstant()
    {
        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.RootWatermark);
        CollectionAssert.AreEqual(
            new[] { "object", "value", "value_kind", "datatype_iri", "language_tag", "multiplicity",
                "key_1", "key_2", "key_3", "key_4", "key_5" },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5" },
            profile.CursorVariables.ToArray());
    }

    [TestMethod]
    public void EveryFamilyUsesTheEuropeanUnionVirtuosoDialectAndTheSharedTerminalPagePolicy()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        foreach (var set in new[]
                 {
                     EuObjectFactsQuerySet.ObjectFacts, EuObjectFactsQuerySet.ExpressionFacts,
                     EuObjectFactsQuerySet.RootWatermark,
                 })
        {
            var profile = plan.CreateDeliveryProfile(set);
            Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso, profile.Dialect);
            Assert.AreEqual(
                RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage, profile.TerminalPagePolicy);
            Assert.AreEqual("pass_id", profile.PassParameterName);
            Assert.AreEqual("has_cursor", profile.HasCursorParameterName);
        }
    }

    // ---- The three SELECT templates, pinned by their exact literal SPARQL text. ----

    [TestMethod]
    public void TheObjectFactsPageTemplateIsPinnedByExactText()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var page = plan.Definition(EuObjectFactsQuerySet.ObjectFacts).PageTemplate;

        StringAssert.Contains(page,
            "SELECT ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag ?multiplicity "
            + "?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 WHERE {");
        StringAssert.Contains(page, "VALUES ?object {\n" + ObjectValuesBlock + "\n      }");
        StringAssert.Contains(page,
            "VALUES ?predicate {\n"
            + "        <http://publications.europa.eu/ontology/cdm#resource_legal_id_celex>\n"
            + "        <http://publications.europa.eu/ontology/cdm#resource_legal_type>\n"
            + "        <http://publications.europa.eu/ontology/cdm#work_has_resource-type>\n"
            + "        <http://publications.europa.eu/ontology/cdm#work_date_document>\n"
            + "        <http://publications.europa.eu/ontology/cdm#act_consolidated_date>\n"
            + "        <http://publications.europa.eu/ontology/cdm#date_creation_legacy>\n"
            + "        <http://publications.europa.eu/ontology/cdm#resource_legal_in-force>\n"
            + "        <http://publications.europa.eu/ontology/cdm#work_is_about_concept_eurovoc>\n"
            + "        <http://publications.europa.eu/ontology/cdm#resource_legal_is_about_concept_directory-code>\n"
            + "        <http://publications.europa.eu/ontology/cdm#resource_legal_amends_resource_legal>\n"
            + "        <http://publications.europa.eu/ontology/cdm#resource_legal_corrects_resource_legal>\n"
            + "        <http://publications.europa.eu/ontology/cdm#resource_legal_based_on_resource_legal>\n"
            + "        <http://publications.europa.eu/ontology/cdm#act_consolidated_based_on_resource_legal>\n"
            + "      }");
        StringAssert.Contains(page, "?object ?predicate ?value .");
        StringAssert.Contains(page,
            "BIND(IF(isIRI(?value), \"iri\", IF(isLiteral(?value), \"literal\", "
            + "\"unsupported_blank_node\")) AS ?value_kind)");
        StringAssert.Contains(page, "FILTER NOT EXISTS { ?object ?predicate ?missing_value }");
        StringAssert.Contains(page,
            "GROUP BY ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag");
        StringAssert.Contains(page, "BIND(STR(?object) AS ?key_1)");
        StringAssert.Contains(page, "BIND(STR(?predicate) AS ?key_2)");
        StringAssert.Contains(page, "BIND(IF(BOUND(?value), STR(?value), \"\") AS ?key_4)");
        StringAssert.Contains(page,
            "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6\nLIMIT {page_limit:uint}");
        Assert.IsFalse(page.Contains("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TheExpressionFactsPageTemplateJoinsThroughExpressionBelongsToWork()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var page = plan.Definition(EuObjectFactsQuerySet.ExpressionFacts).PageTemplate;

        StringAssert.Contains(page, "VALUES ?parent {\n" + ObjectValuesBlock + "\n      }");
        StringAssert.Contains(page,
            "?object <http://publications.europa.eu/ontology/cdm#expression_belongs_to_work> ?parent .");
        StringAssert.Contains(page,
            "VALUES ?predicate {\n"
            + "        <http://publications.europa.eu/ontology/cdm#expression_belongs_to_work>\n"
            + "        <http://publications.europa.eu/ontology/cdm#expression_uses_language>\n"
            + "        <http://publications.europa.eu/ontology/cdm#expression_title>\n"
            + "        <http://publications.europa.eu/ontology/cdm#expression_title_short>\n"
            + "      }");
        StringAssert.Contains(page,
            "GROUP BY ?parent ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag");
        StringAssert.Contains(page,
            "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6\nLIMIT {page_limit:uint}");
    }

    [TestMethod]
    public void TheRootWatermarkPageTemplateReadsTheFixedCmrLastModificationDatePredicate()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var page = plan.Definition(EuObjectFactsQuerySet.RootWatermark).PageTemplate;

        StringAssert.Contains(page, "VALUES ?object {\n" + ObjectValuesBlock + "\n      }");
        StringAssert.Contains(page,
            "?object <http://publications.europa.eu/ontology/cdm/cmr#lastModificationDate> ?value .");
        StringAssert.Contains(page,
            "FILTER NOT EXISTS { ?object "
            + "<http://publications.europa.eu/ontology/cdm/cmr#lastModificationDate> ?missing_value }");
        StringAssert.Contains(page, "GROUP BY ?object ?value ?value_kind ?datatype_iri ?language_tag");
        Assert.IsFalse(page.Contains("?predicate", StringComparison.Ordinal));
        StringAssert.Contains(page, "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5\nLIMIT {page_limit:uint}");
    }

    [TestMethod]
    public void EveryCountTemplateWrapsItsOwnPageRowShapeWithNoCursorMachinery()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        foreach (var set in new[]
                 {
                     EuObjectFactsQuerySet.ObjectFacts, EuObjectFactsQuerySet.ExpressionFacts,
                     EuObjectFactsQuerySet.RootWatermark,
                 })
        {
            var count = plan.Definition(set).CountTemplate;
            StringAssert.StartsWith(count, "SELECT (COUNT(*) AS ?count) WHERE {");
            Assert.IsFalse(count.Contains("has_cursor", StringComparison.Ordinal));
            Assert.IsFalse(count.Contains("ORDER BY", StringComparison.Ordinal));
        }
    }

    // ---- Batch validation, canonicalization, ordering and padding. ----

    [TestMethod]
    public void PartitionKeyIsStableAcrossInputOrderAndPadding()
    {
        var forward = EuObjectFactsDiscoveryPlan.PartitionKeyFor([RootA, RootB]);
        var reversed = EuObjectFactsDiscoveryPlan.PartitionKeyFor([RootB, RootA]);
        Assert.AreEqual(forward, reversed);
        StringAssert.StartsWith(forward, "eu-object-facts-batch-");
    }

    [TestMethod]
    public void DifferentBatchesMintDifferentPartitionKeys()
    {
        var a = EuObjectFactsDiscoveryPlan.PartitionKeyFor([RootA]);
        var b = EuObjectFactsDiscoveryPlan.PartitionKeyFor([RootB]);
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void AnEmptyBatchThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => EuObjectFactsDiscoveryPlan.PartitionKeyFor([]));
    }

    [TestMethod]
    public void ABatchOverCapacityThrows()
    {
        var oversized = Enumerable.Range(0, EuObjectFactsDiscoveryPlan.BatchCapacity + 1)
            .Select(i => $"http://publications.europa.eu/resource/cellar/{i:00000000}-0000-4000-8000-000000000000")
            .ToArray();
        Assert.ThrowsExactly<ArgumentException>(
            () => EuObjectFactsDiscoveryPlan.PartitionKeyFor(oversized));
    }

    [TestMethod]
    public void ANonCanonicalBatchMemberThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => EuObjectFactsDiscoveryPlan.PartitionKeyFor([RootA + "?x=1"]));
    }

    [TestMethod]
    public void ADuplicateCanonicalBatchMemberThrows()
    {
        var https = "https" + RootA["http".Length..];
        Assert.ThrowsExactly<ArgumentException>(
            () => EuObjectFactsDiscoveryPlan.PartitionKeyFor([RootA, https]));
    }

    // ---- Bind round trips. ----

    private static readonly byte[] RendererSourceBytes =
        System.Text.Encoding.UTF8.GetBytes("eu-object-facts-discovery-plan-tests/1");

    private static MachineQueryRendererSource RendererSource() =>
        MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:00000000-0000-4000-8000-0000000000aa",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(RendererSourceBytes))
                    .ToLowerInvariant()),
            RendererSourceBytes);

    [TestMethod]
    public void BindCountRendersEveryBatchMemberAsAnAbsoluteIriTerm()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var bound = plan.BindCount(
            EuObjectFactsQuerySet.ObjectFacts, [RootA, RootB], EuObjectFactsQueryPass.Pass1,
            "urn:uuid:00000000-0000-4000-8000-000000000001",
            "urn:uuid:00000000-0000-4000-8000-000000000002",
            RendererSource());

        var body = System.Text.Encoding.UTF8.GetString(bound.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(body, "<" + RootA + ">");
        StringAssert.Contains(body, "<" + RootB + ">");
        // The pad slots repeat the batch's own lexicographically-greatest member.
        var sorted = new[] { RootA, RootB }.OrderBy(static v => v, StringComparer.Ordinal).ToArray();
        var padCount = System.Text.RegularExpressions.Regex.Matches(
            body, System.Text.RegularExpressions.Regex.Escape("<" + sorted[^1] + ">")).Count;
        Assert.AreEqual(EuObjectFactsDiscoveryPlan.BatchCapacity - 1, padCount);
        Assert.AreEqual(EuObjectFactsDiscoveryPlan.PartitionKeyFor([RootA, RootB]), bound.InputArtifact.PartitionBinding.MemberKey);
    }

    [TestMethod]
    public void BindPageWithNoCursorRendersHasCursorZero()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var countEvidence = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-000000000003",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1])).ToLowerInvariant());
        var bound = plan.BindPage(
            EuObjectFactsQuerySet.RootWatermark, [RootA], EuObjectFactsQueryPass.Pass1, null,
            0, countEvidence,
            "urn:uuid:00000000-0000-4000-8000-000000000004",
            "urn:uuid:00000000-0000-4000-8000-000000000005",
            RendererSource());

        var body = System.Text.Encoding.UTF8.GetString(bound.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(body, "(0 \"\" \"\" \"\" \"\" \"\")");
        StringAssert.Contains(body, "LIMIT 997");
    }

    [TestMethod]
    public void ARootWatermarkBatchMemberOutsideAppendixARefuses()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var notASeed =
            "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000";
        Assert.ThrowsExactly<ArgumentException>(() => plan.BindCount(
            EuObjectFactsQuerySet.RootWatermark, [notASeed], EuObjectFactsQueryPass.Pass1,
            "urn:uuid:00000000-0000-4000-8000-000000000006",
            "urn:uuid:00000000-0000-4000-8000-000000000007",
            RendererSource()));
    }

    [TestMethod]
    public void AnObjectFactsBatchMemberOutsideAppendixAIsAcceptedBecauseOCanIncludeStates()
    {
        // Unlike RootWatermark, ObjectFacts and ExpressionFacts run over O = roots union discovered
        // states, so a well-formed Cellar Work IRI outside the 82-root pack is a legitimate state,
        // not a refusal.
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var state =
            "http://publications.europa.eu/resource/cellar/99999999-9999-4999-8999-999999999999";
        var bound = plan.BindCount(
            EuObjectFactsQuerySet.ObjectFacts, [state], EuObjectFactsQueryPass.Pass1,
            "urn:uuid:00000000-0000-4000-8000-000000000008",
            "urn:uuid:00000000-0000-4000-8000-000000000009",
            RendererSource());
        Assert.IsNotNull(bound);
    }
}
