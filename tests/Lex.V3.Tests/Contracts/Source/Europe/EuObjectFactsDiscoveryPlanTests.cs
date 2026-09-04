using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05c-1: the three new bounded row-set query-plan families (object-facts/P, expression-facts/X,
/// root-watermark/W), following the cursor-arity-matches-natural-key and unbound-branch template
/// shape <c>EuConsolidationDiscoveryPlan</c>'s own <c>Family</c>/<c>TemporalFacts</c> sets establish,
/// generalized to each family's own row shape: six parts for P, seven for X (parent-inclusive, design
/// fix two), five for W. SCOPE_RULING <c>lex-event-20260904T040718222Z-7e6f29af07024cf5b2cb716f94f288e3</c>.
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
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" },
            profile.CursorVariables.ToArray());
        CollectionAssert.AreEqual(EuObjectFactsDiscoveryPlan.BatchParameterNames().ToArray(),
            profile.SelectionParameterNames.ToArray());

        // Design fix one: a COUNT-over-the-padded-batch multiplicity column reports a
        // padding-dependent value for the batch's own greatest member; nothing reads it, so it
        // does not exist as a projected column at all.
        Assert.IsFalse(profile.ProjectionVariables.Contains("multiplicity"));
    }

    /// <summary>
    /// Design fix two (SCOPE_RULING review, family X). X's own SELECT groups by
    /// <c>?parent ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag</c> - seven
    /// columns, because one Expression can in principle belong to more than one Work within a
    /// single batch. A six-part cursor omitting <c>parent</c> could not distinguish two such rows,
    /// so the cursor is seven parts, with <c>key_7</c> carrying <c>parent</c>.
    /// </summary>
    [TestMethod]
    public void ExpressionFactsProjectionAndCursorCoverItsOwnSevenColumnGroupingKeyIncludingParent()
    {
        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.ExpressionFacts);
        CollectionAssert.AreEqual(
            new[]
            {
                "parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag",
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7" },
            profile.CursorVariables.ToArray());
        CollectionAssert.AreEqual(
            profile.CursorVariables.ToArray(), profile.CanonicalKeyVariables.ToArray());
        Assert.IsFalse(profile.ProjectionVariables.Contains("multiplicity"));
    }

    [TestMethod]
    public void RootWatermarkCursorIsFivePartNotSixBecauseThePredicateColumnWouldBeConstant()
    {
        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.RootWatermark);
        CollectionAssert.AreEqual(
            new[] { "object", "value", "value_kind", "datatype_iri", "language_tag",
                "key_1", "key_2", "key_3", "key_4", "key_5" },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5" },
            profile.CursorVariables.ToArray());
        Assert.IsFalse(profile.ProjectionVariables.Contains("multiplicity"));
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
            "SELECT ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag "
            + "?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 WHERE {");
        Assert.IsFalse(page.Contains("multiplicity", StringComparison.Ordinal));
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
        Assert.IsFalse(page.Contains("multiplicity", StringComparison.Ordinal));

        // Design fix two: X's own canonical key and cursor are seven parts (key_1..key_7), the
        // seventh carrying ?parent, since X groups by (parent, object, ...) rather than by object
        // alone.
        StringAssert.Contains(page,
            "SELECT ?parent ?object ?predicate ?value ?value_kind ?datatype_iri ?language_tag "
            + "?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 ?key_7 WHERE {");
        StringAssert.Contains(page, "BIND(STR(?parent) AS ?key_7)");
        StringAssert.Contains(page,
            "VALUES (?has_cursor ?last_key_1 ?last_key_2 ?last_key_3 ?last_key_4 ?last_key_5 "
            + "?last_key_6 ?last_key_7) {");
        StringAssert.Contains(page,
            "(?key_1 = ?last_key_1 && ?key_2 = ?last_key_2 && ?key_3 = ?last_key_3 && "
            + "?key_4 = ?last_key_4 && ?key_5 = ?last_key_5 && ?key_6 = ?last_key_6 && "
            + "?key_7 > ?last_key_7)");
        StringAssert.Contains(page,
            "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 ?key_7\nLIMIT {page_limit:uint}");
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
        Assert.IsFalse(page.Contains("multiplicity", StringComparison.Ordinal));
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

    /// <summary>
    /// Test fold-in: before this test, no fixture ever bound family X's or family W's count or
    /// page for pass 2 - every existing bind round trip used <see cref="EuObjectFactsQueryPass.Pass1"/>
    /// only, so pass 2's own rendering path (a different page limit, a different literal
    /// <c>pass_id</c> value) was never actually exercised for either family. Every expected
    /// substring here is a literal written in this test, not text the production renderer produced
    /// and handed back to itself.
    /// </summary>
    [TestMethod]
    public void ExpressionFactsBindsCountAndPageForBothPassesWithThePinnedPassIdAndLimitLiterally()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var countEvidence = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000b1",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([2])).ToLowerInvariant());

        var count1 = plan.BindCount(
            EuObjectFactsQuerySet.ExpressionFacts, [RootA], EuObjectFactsQueryPass.Pass1,
            "urn:uuid:00000000-0000-4000-8000-0000000000b2",
            "urn:uuid:00000000-0000-4000-8000-0000000000b3", RendererSource());
        var count1Body = System.Text.Encoding.UTF8.GetString(count1.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(count1Body, "VALUES ?lex_pass_id { 1 }");

        var count2 = plan.BindCount(
            EuObjectFactsQuerySet.ExpressionFacts, [RootA], EuObjectFactsQueryPass.Pass2,
            "urn:uuid:00000000-0000-4000-8000-0000000000b4",
            "urn:uuid:00000000-0000-4000-8000-0000000000b5", RendererSource());
        var count2Body = System.Text.Encoding.UTF8.GetString(count2.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(count2Body, "VALUES ?lex_pass_id { 2 }");

        var page1 = plan.BindPage(
            EuObjectFactsQuerySet.ExpressionFacts, [RootA], EuObjectFactsQueryPass.Pass1, null,
            0, countEvidence,
            "urn:uuid:00000000-0000-4000-8000-0000000000b6",
            "urn:uuid:00000000-0000-4000-8000-0000000000b7", RendererSource());
        var page1Body = System.Text.Encoding.UTF8.GetString(page1.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(page1Body, "VALUES ?lex_pass_id { 1 }");
        StringAssert.Contains(page1Body, "LIMIT 997");

        var page2 = plan.BindPage(
            EuObjectFactsQuerySet.ExpressionFacts, [RootA], EuObjectFactsQueryPass.Pass2, null,
            0, countEvidence,
            "urn:uuid:00000000-0000-4000-8000-0000000000b8",
            "urn:uuid:00000000-0000-4000-8000-0000000000b9", RendererSource());
        var page2Body = System.Text.Encoding.UTF8.GetString(page2.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(page2Body, "VALUES ?lex_pass_id { 2 }");
        StringAssert.Contains(page2Body, "LIMIT 613");

        // The no-cursor VALUES row is seven-wide for X (design fix two), not the six-wide shape
        // family P still uses.
        StringAssert.Contains(page1Body, "(0 \"\" \"\" \"\" \"\" \"\" \"\" \"\")");
    }

    /// <summary>Same test fold-in as the Expression-facts one above, for family W.</summary>
    [TestMethod]
    public void RootWatermarkBindsCountAndPageForBothPassesWithThePinnedPassIdAndLimitLiterally()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var countEvidence = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000c1",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([3])).ToLowerInvariant());

        var count1 = plan.BindCount(
            EuObjectFactsQuerySet.RootWatermark, [RootA], EuObjectFactsQueryPass.Pass1,
            "urn:uuid:00000000-0000-4000-8000-0000000000c2",
            "urn:uuid:00000000-0000-4000-8000-0000000000c3", RendererSource());
        var count1Body = System.Text.Encoding.UTF8.GetString(count1.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(count1Body, "VALUES ?lex_pass_id { 1 }");

        var count2 = plan.BindCount(
            EuObjectFactsQuerySet.RootWatermark, [RootA], EuObjectFactsQueryPass.Pass2,
            "urn:uuid:00000000-0000-4000-8000-0000000000c4",
            "urn:uuid:00000000-0000-4000-8000-0000000000c5", RendererSource());
        var count2Body = System.Text.Encoding.UTF8.GetString(count2.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(count2Body, "VALUES ?lex_pass_id { 2 }");

        var page1 = plan.BindPage(
            EuObjectFactsQuerySet.RootWatermark, [RootA], EuObjectFactsQueryPass.Pass1, null,
            0, countEvidence,
            "urn:uuid:00000000-0000-4000-8000-0000000000c6",
            "urn:uuid:00000000-0000-4000-8000-0000000000c7", RendererSource());
        var page1Body = System.Text.Encoding.UTF8.GetString(page1.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(page1Body, "VALUES ?lex_pass_id { 1 }");
        StringAssert.Contains(page1Body, "LIMIT 997");

        var page2 = plan.BindPage(
            EuObjectFactsQuerySet.RootWatermark, [RootA], EuObjectFactsQueryPass.Pass2, null,
            0, countEvidence,
            "urn:uuid:00000000-0000-4000-8000-0000000000c8",
            "urn:uuid:00000000-0000-4000-8000-0000000000c9", RendererSource());
        var page2Body = System.Text.Encoding.UTF8.GetString(page2.Request.CopyRequestBody().ToArray());
        StringAssert.Contains(page2Body, "VALUES ?lex_pass_id { 2 }");
        StringAssert.Contains(page2Body, "LIMIT 613");
    }

    /// <summary>
    /// Test fold-in (item seven): <see cref="ObjectValuesBlock"/> above builds its expected VALUES
    /// text by calling <see cref="EuObjectFactsDiscoveryPlan.BatchParameterNames"/> - the same
    /// production helper every other pin in this file verifies - so a name it got wrong would drop
    /// out of every comparison that only ever calls it a second time. This test breaks that
    /// self-reference for the first and last slot by writing their names as bare literals.
    /// </summary>
    [TestMethod]
    public void TheFirstAndLastBatchSlotNamesAreLiteralsNotDerivedFromTheProductionHelper()
    {
        var names = EuObjectFactsDiscoveryPlan.BatchParameterNames();
        Assert.AreEqual("requested_object_01", names[0]);
        Assert.AreEqual("requested_object_50", names[^1]);
        Assert.AreEqual(EuObjectFactsDiscoveryPlan.BatchCapacity, names.Count);

        var plan = EuObjectFactsDiscoveryPlan.Create();
        var page = plan.Definition(EuObjectFactsQuerySet.ObjectFacts).PageTemplate;
        StringAssert.Contains(page, "{requested_object_01:iri}");
        StringAssert.Contains(page, "{requested_object_50:iri}");
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

    // ---- Delivery-proof fixtures (test fold-in six). Each of P, X and W runs through the real
    // EnumerationDeliveryComparison.Create against the family's own real interpretation profile,
    // fed real SPARQL JSON count, page and terminal-page response bodies - not read by a human,
    // but decoded and cross-checked by the shared Core machinery, so the profile's cursor arity
    // and its uniqueness/ordering checks are proven by execution rather than by inspection. Every
    // batch below names far fewer objects than the 50-slot capacity, so every one of these three
    // is also a genuinely padded batch. Note: AddPass feeds the identical row bodies to pass 1 and
    // pass 2, so EqualSelections below follows by construction rather than from any real two-pass
    // agreement over independently observed data; the property these fixtures actually exercise is
    // EnumerationDeliveryComparison.VerifyPages's own decode, cursor-arity and ordering checks
    // against real rows, not cross-pass agreement itself. ----

    private const string FixtureXsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string FixtureXsdDateTime = "http://www.w3.org/2001/XMLSchema#dateTime";
    private const string FixtureEnglishLanguageAuthorityIri =
        "http://publications.europa.eu/resource/authority/language/ENG";
    private const string FixtureParentA =
        "http://publications.europa.eu/resource/cellar/11111111-1111-4111-8111-111111111111";
    private const string FixtureParentB =
        "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";
    private const string FixtureExprShared =
        "http://publications.europa.eu/resource/cellar/66666666-6666-4666-8666-666666666666.0001";

    [TestMethod]
    public void ObjectFactsDeliveryProvesItsProfileThroughTheRealCoreMachineryWithAPaddedBatch()
    {
        var celexIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIdCelex);
        var row = FixtureRow(
            ("object", FixtureTerm("uri", RootA)),
            ("predicate", FixtureTerm("uri", celexIri)),
            ("value", FixtureTerm("literal", "32016R0679", FixtureXsdString)),
            ("value_kind", FixtureTerm("literal", "literal")),
            ("datatype_iri", FixtureTerm("literal", FixtureXsdString)),
            ("language_tag", FixtureTerm("literal", string.Empty)),
            ("key_1", FixtureTerm("literal", RootA)),
            ("key_2", FixtureTerm("literal", celexIri)),
            ("key_3", FixtureTerm("literal", "literal")),
            ("key_4", FixtureTerm("literal", "32016R0679")),
            ("key_5", FixtureTerm("literal", FixtureXsdString)),
            ("key_6", FixtureTerm("literal", string.Empty)));
        var cursor = new[] { RootA, celexIri, "literal", "32016R0679", FixtureXsdString, string.Empty };

        var comparison = new EuObjectFactsDeliveryFixture(
            EuObjectFactsQuerySet.ObjectFacts, [RootA], [row], cursor, seedBase: 1_000).Create();

        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, comparison.Outcome);
        Assert.AreEqual(1, comparison.DeliveredRowCountA);
        Assert.AreEqual(1, comparison.DeliveredRowCountB);
        Assert.AreEqual(RepeatedEnumerationThresholdAssessment.BelowMaximum, comparison.ThresholdAssessment);
    }

    [TestMethod]
    public void RootWatermarkDeliveryProvesItsProfileThroughTheRealCoreMachineryWithAPaddedBatch()
    {
        var row = FixtureRow(
            ("object", FixtureTerm("uri", RootA)),
            ("value", FixtureTerm("literal", "2024-01-01T00:00:00Z", FixtureXsdDateTime)),
            ("value_kind", FixtureTerm("literal", "literal")),
            ("datatype_iri", FixtureTerm("literal", FixtureXsdDateTime)),
            ("language_tag", FixtureTerm("literal", string.Empty)),
            ("key_1", FixtureTerm("literal", RootA)),
            ("key_2", FixtureTerm("literal", "literal")),
            ("key_3", FixtureTerm("literal", "2024-01-01T00:00:00Z")),
            ("key_4", FixtureTerm("literal", FixtureXsdDateTime)),
            ("key_5", FixtureTerm("literal", string.Empty)));
        var cursor = new[] { RootA, "literal", "2024-01-01T00:00:00Z", FixtureXsdDateTime, string.Empty };

        var comparison = new EuObjectFactsDeliveryFixture(
            EuObjectFactsQuerySet.RootWatermark, [RootA], [row], cursor, seedBase: 2_000).Create();

        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, comparison.Outcome);
        Assert.AreEqual(1, comparison.DeliveredRowCountA);
        Assert.AreEqual(1, comparison.DeliveredRowCountB);
    }

    /// <summary>
    /// Design fix two's own regression test, run through the real Core delivery machinery rather
    /// than read: the same Expression (<see cref="FixtureExprShared"/>) observed with the same
    /// language under two different parent Works in one batch. Six of X's own seven key columns
    /// (<c>object</c>, <c>predicate</c>, <c>value_kind</c>, <c>value</c>,
    /// <c>datatype_iri</c>, <c>language_tag</c>) are identical between the two rows on purpose; only
    /// <c>parent</c> (X's own <c>key_7</c>) differs. Reverting <see cref="EuObjectFactsDiscoveryPlan"/>
    /// to a six-part cursor for X does not even reach <see cref="EnumerationDeliveryComparison.VerifyPages"/>'s
    /// own duplicate-key refusal: this test's own fixture cursor is still seven elements wide, so
    /// <see cref="EuObjectFactsDiscoveryPlan.BindPage"/>'s own cursor-arity check throws first
    /// (confirmed by performing that exact revert locally and re-running this test: it throws "A
    /// continuation cursor must have the exact query-set arity."). After the fix, the seventh part
    /// distinguishes the two rows and this succeeds.
    /// </summary>
    [TestMethod]
    public void ExpressionFactsDeliveryProvesTheSevenPartCursorWithOneExpressionUnderTwoParentWorksInOneBatch()
    {
        var usesLanguageIri = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
        string Row(string parent) => FixtureRow(
            ("parent", FixtureTerm("uri", parent)),
            ("object", FixtureTerm("uri", FixtureExprShared)),
            ("predicate", FixtureTerm("uri", usesLanguageIri)),
            ("value", FixtureTerm("uri", FixtureEnglishLanguageAuthorityIri)),
            ("value_kind", FixtureTerm("literal", "iri")),
            ("datatype_iri", FixtureTerm("literal", string.Empty)),
            ("language_tag", FixtureTerm("literal", string.Empty)),
            ("key_1", FixtureTerm("literal", FixtureExprShared)),
            ("key_2", FixtureTerm("literal", usesLanguageIri)),
            ("key_3", FixtureTerm("literal", "iri")),
            ("key_4", FixtureTerm("literal", FixtureEnglishLanguageAuthorityIri)),
            ("key_5", FixtureTerm("literal", string.Empty)),
            ("key_6", FixtureTerm("literal", string.Empty)),
            ("key_7", FixtureTerm("literal", parent)));

        // Ascending cursor order: key_1..key_6 tie between the two rows, so key_7 (parent) alone
        // decides the order, and FixtureParentA < FixtureParentB ordinally.
        var rows = new[] { Row(FixtureParentA), Row(FixtureParentB) };
        var cursor = new[]
        {
            FixtureExprShared, usesLanguageIri, "iri", FixtureEnglishLanguageAuthorityIri,
            string.Empty, string.Empty, FixtureParentB,
        };

        var comparison = new EuObjectFactsDeliveryFixture(
            EuObjectFactsQuerySet.ExpressionFacts, [FixtureParentA, FixtureParentB], rows, cursor,
            seedBase: 3_000).Create();

        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, comparison.Outcome);
        Assert.AreEqual(2, comparison.DeliveredRowCountA);
        Assert.AreEqual(2, comparison.DeliveredRowCountB);
    }

    private static string FixtureTerm(
        string type, string value, string? datatype = null, string? language = null) =>
        "{\"type\":" + JsonSerializer.Serialize(type) +
        ",\"value\":" + JsonSerializer.Serialize(value) +
        (datatype is null ? string.Empty : ",\"datatype\":" + JsonSerializer.Serialize(datatype)) +
        (language is null ? string.Empty : ",\"xml:lang\":" + JsonSerializer.Serialize(language)) + "}";

    private static string FixtureRow(params (string Var, string Term)[] fields) =>
        "{" + string.Join(',', fields.Select(
            static field => JsonSerializer.Serialize(field.Var) + ":" + field.Term)) + "}";

    /// <summary>
    /// Binds and resolves both passes of one query set's count, first page and empty terminal
    /// successor page, entirely through the real <see cref="EuObjectFactsDiscoveryPlan"/> bind and
    /// render path and the real <see cref="RoutedHttpEvidence"/>/<see cref="DurableBlobWriteReceipt"/>
    /// shapes <see cref="EnumerationDeliveryComparison.Create"/> itself verifies, mirroring
    /// EuConsolidationDiscoveryTests's own TemporalDeliveryFixture for the three new families.
    /// </summary>
    private sealed class EuObjectFactsDeliveryFixture : IRepeatedEnumerationEvidenceResolver
    {
        private const string ResponseMediaType = "application/sparql-results+json";
        private readonly Dictionary<SourceArtifactRef, RepeatedEnumerationResolvedEvidence> _evidence = [];
        private readonly EuObjectFactsDiscoveryPlan _plan = EuObjectFactsDiscoveryPlan.Create();
        private readonly EuObjectFactsQuerySet _set;
        private readonly IReadOnlyList<string> _batch;
        private readonly IReadOnlyList<string> _rows;
        private readonly IReadOnlyList<string> _lastRowCursor;
        private readonly MachineQueryRendererSource _rendererSource;
        private readonly SourceArtifactRef _runIdentity;
        private int _seed;
        private ulong _requestOrdinal;

        internal EuObjectFactsDeliveryFixture(
            EuObjectFactsQuerySet set,
            IReadOnlyList<string> batch,
            IReadOnlyList<string> rows,
            IReadOnlyList<string> lastRowCursor,
            int seedBase)
        {
            _set = set;
            _batch = batch;
            _rows = rows;
            _lastRowCursor = lastRowCursor;
            _seed = seedBase;
            var rendererSourceBytes = Encoding.UTF8.GetBytes(
                "eu-object-facts-delivery-fixture/1:" + set);
            _rendererSource = MachineQueryRendererSource.Open(
                Reference(++_seed, rendererSourceBytes), rendererSourceBytes);
            _runIdentity = Artifact(++_seed);
        }

        internal EnumerationDeliveryComparison Create()
        {
            var a = AddPass(EuObjectFactsQueryPass.Pass1);
            var b = AddPass(EuObjectFactsQueryPass.Pass2);
            var profile = _plan.CreateDeliveryProfile(_set);
            var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
                Artifact(++_seed).ResourceId, profile);
            return EnumerationDeliveryComparison.Create(
                profile, profileRef, a.Count, new(a.Pages), b.Count, new(b.Pages), this);
        }

        public RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs references) =>
            _evidence.TryGetValue(references.HttpEvidenceRef, out var value)
                ? value
                : throw new ArgumentException(
                    "The retained delivery-fixture evidence is missing.", nameof(references));

        private (RepeatedEnumerationEvidenceRefs Count, IReadOnlyList<RepeatedEnumerationPageRef> Pages)
            AddPass(EuObjectFactsQueryPass pass)
        {
            var countBound = _plan.BindCount(
                _set, _batch, pass, Artifact(++_seed).ResourceId, Artifact(++_seed).ResourceId,
                _rendererSource);
            var countRefs = Add(countBound, CountPayload(_rows.Count), isPage: false);
            var firstBound = _plan.BindPage(
                _set, _batch, pass, null, _rows.Count, countRefs.HttpEvidenceRef,
                Artifact(++_seed).ResourceId, Artifact(++_seed).ResourceId, _rendererSource);
            var firstRefs = Add(firstBound, RowsDocument(_rows), isPage: true);
            var successorBound = _plan.BindPage(
                _set, _batch, pass, _lastRowCursor, _rows.Count, countRefs.HttpEvidenceRef,
                Artifact(++_seed).ResourceId, Artifact(++_seed).ResourceId, _rendererSource);
            var successorRefs = Add(successorBound, RowsDocument([]), isPage: true);
            return (countRefs, new[]
            {
                new RepeatedEnumerationPageRef(0, firstRefs),
                new RepeatedEnumerationPageRef(1, successorRefs),
            });
        }

        private RepeatedEnumerationEvidenceRefs Add(
            EuObjectFactsBoundQuery bound, string payload, bool isPage)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var opened = MachineQueryBinder.OpenForSend(bound.Request);
            var receipt = opened.RenderReceipt;
            var receiptRef = MachineQueryRenderReceiptIdentity.Create(
                Artifact(++_seed).ResourceId, receipt);
            var sourceProfile = OfficialMachineQuerySourceProfiles.ResolveFor(opened);
            var requestBody = opened.CopyRequestBody();
            var logicalRequest = HttpLogicalRequest.Create(
                opened.RequestedUri,
                sourceProfile.Method,
                new[]
                {
                    new HttpLogicalRequestHeader("user-agent", sourceProfile.CrawlerUserAgent),
                    new HttpLogicalRequestHeader("accept", sourceProfile.Accept),
                    new HttpLogicalRequestHeader(
                        "content-type", $"{sourceProfile.RequestContentType}; charset=utf-8"),
                },
                new HttpLogicalRequestBody(checked((ulong)requestBody.LongLength), Sha(requestBody)),
                Artifact(++_seed).Sha256,
                Artifact(++_seed).Sha256);
            var logicalRequestRef = Reference(++_seed, logicalRequest.CopyCanonicalBytes());
            var digest = Sha(bytes);
            var blob = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, bytes.LongLength, CustodyClass.NightlyFloor90d);
            var instant = DateTimeOffset.UnixEpoch.AddSeconds(_seed);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                blob,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse($"00000000-0000-4000-8000-{_seed:D12}"),
                CustodyProtection.LockedTime,
                instant,
                instant.AddDays(91));
            var write = new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, blob, policy);
            var absent = new RoutedHttpAbsentHeader();
            var headers = new RoutedHttpResponseHeaders(
                new RoutedHttpSingleHeader(ResponseMediaType),
                new RoutedHttpSingleHeader(bytes.LongLength.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
            var observationId = Artifact(++_seed).ResourceId;
            var hop = RoutedHttpHop.Create(
                ordinal: 0,
                observationId,
                antecedentHopObservationId: null,
                logicalRequestRef.Sha256,
                logicalRequest.Uri,
                status: 200,
                headers,
                Timestamp(instant),
                Timestamp(instant.AddMilliseconds(1)),
                new DeclaredContentLengthHttpCompletion(checked((ulong)bytes.LongLength)),
                checked((ulong)bytes.LongLength),
                digest,
                Sha(Encoding.UTF8.GetBytes(ContractJson.Serialize(write))),
                checked((ulong)bytes.LongLength),
                digest);
            var httpEvidence = RoutedHttpEvidence.Create(
                _runIdentity,
                ++_requestOrdinal,
                attemptOrdinal: 0,
                new[] { hop },
                new CompleteHttpRouteOutcome(),
                new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal)
                {
                    [hop.ObservationId] = write,
                });
            var httpEvidenceRef = Reference(++_seed, httpEvidence.CopyCanonicalBytes());
            var renderer = new EuObjectFactsSparqlRenderer(
                _plan, _plan.Definition(_set), isPage, _rendererSource);
            var refs = new RepeatedEnumerationEvidenceRefs(
                bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, receiptRef, logicalRequestRef,
                httpEvidenceRef);
            _evidence.Add(httpEvidenceRef, new RepeatedEnumerationResolvedEvidence(
                bound.MachinePlan, bound.InputArtifact, receipt, renderer, logicalRequest, httpEvidence,
                write, bytes));
            return refs;
        }

        private static string CountPayload(long count) =>
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]}," +
            "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{" +
            "\"count\":" + FixtureTerm(
                "literal", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "http://www.w3.org/2001/XMLSchema#integer") +
            "}]}}";

        private string RowsDocument(IReadOnlyList<string> rows) =>
            "{\"head\":{\"link\":[],\"vars\":" +
            JsonSerializer.Serialize(_plan.Definition(_set).ProjectionVariables) + "}," +
            "\"results\":{\"distinct\":false,\"ordered\":true," +
            "\"bindings\":[" + string.Join(',', rows) + "]}}";

        private static SourceArtifactRef Artifact(int seed) => new(
            $"urn:uuid:00000000-0000-4000-8000-{seed:D12}",
            seed.ToString("x64", System.Globalization.CultureInfo.InvariantCulture));

        private static SourceArtifactRef Reference(int seed, ReadOnlySpan<byte> bytes) =>
            new(Artifact(seed).ResourceId, Sha(bytes));

        private static string Timestamp(DateTimeOffset value) =>
            value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

        private static string Sha(ReadOnlySpan<byte> value) =>
            Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    // ---- Construction surface (design fix three). Every public type this slice adds gets its own
    // pin, following AbsenceConstructionSurfaceTests's own precedent: print the real reflected
    // value from ConstructionSurface.Of, transcribe it literally, so a second producer added
    // tomorrow is a line in a diff rather than an unnoticed new door. ----

    private const string N = "Lex.V3.Contracts.Source.Europe.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";

    [TestMethod]
    public void ThePlanHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuObjectFactsDiscoveryPlan::.ctor() -> "
                    + N + "EuObjectFactsDiscoveryPlan",
                "constructor private static " + N + "EuObjectFactsDiscoveryPlan::.cctor() -> "
                    + N + "EuObjectFactsDiscoveryPlan",
                "method public static " + N + "EuObjectFactsDiscoveryPlan::Create() -> "
                    + N + "EuObjectFactsDiscoveryPlan",
            },
            ConstructionSurface.Of(typeof(EuObjectFactsDiscoveryPlan)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuObjectFactsDiscoveryPlan).Assembly, typeof(EuObjectFactsDiscoveryPlan), true).ToArray(),
            "nothing else in Contracts may hand out a plan it did not create");
    }

    [TestMethod]
    public void TheQuerySetEnumHasExactlyThreeMembers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuObjectFactsQuerySet::ExpressionFacts -> "
                    + N + "EuObjectFactsQuerySet",
                "field public static " + N + "EuObjectFactsQuerySet::ObjectFacts -> "
                    + N + "EuObjectFactsQuerySet",
                "field public static " + N + "EuObjectFactsQuerySet::RootWatermark -> "
                    + N + "EuObjectFactsQuerySet",
            },
            ConstructionSurface.Of(typeof(EuObjectFactsQuerySet)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuObjectFactsDiscoveryPlan::_definitions -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<" + N + "EuObjectFactsQuerySet, "
                    + N + "EuObjectFactsQueryDefinition>",
                "field private instance " + N + "EuObjectFactsQueryDefinition::<Set>k__BackingField -> "
                    + N + "EuObjectFactsQuerySet",
                "property internal instance " + N + "EuObjectFactsQueryDefinition::Set() -> "
                    + N + "EuObjectFactsQuerySet",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuObjectFactsDiscoveryPlan).Assembly, typeof(EuObjectFactsQuerySet), true).ToArray());
    }

    [TestMethod]
    public void TheQueryPassEnumHasExactlyTwoMembers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuObjectFactsQueryPass::Pass1 -> "
                    + N + "EuObjectFactsQueryPass",
                "field public static " + N + "EuObjectFactsQueryPass::Pass2 -> "
                    + N + "EuObjectFactsQueryPass",
            },
            ConstructionSurface.Of(typeof(EuObjectFactsQueryPass)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuObjectFactsDiscoveryPlan).Assembly, typeof(EuObjectFactsQueryPass), true).ToArray(),
            "nothing else in Contracts distinguishes a pass otherwise; only Set does, via the definitions map");
    }

    [TestMethod]
    public void TheBoundQueryRecordHasExactlyItsOwnPrimaryConstructorAndCopyDoors()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuObjectFactsBoundQuery::.ctor("
                    + N + "EuObjectFactsBoundQuery) -> " + N + "EuObjectFactsBoundQuery",
                "constructor public instance " + N + "EuObjectFactsBoundQuery::.ctor("
                    + Core + "MachineQueryPlan, " + Core + "SourceArtifactRef, "
                    + Core + "MachineQueryInputArtifact, " + Core + "BoundMachineRequest) -> "
                    + N + "EuObjectFactsBoundQuery",
                "method public instance " + N + "EuObjectFactsBoundQuery::<Clone>$() -> "
                    + N + "EuObjectFactsBoundQuery",
            },
            ConstructionSurface.Of(typeof(EuObjectFactsBoundQuery)).ToArray());

        // The plan's own private Bind is the one helper both public entry points route through -
        // a real external door the sweep is right to report alongside BindCount and BindPage,
        // not a shape to guess down to just the two public methods.
        CollectionAssert.AreEqual(
            new[]
            {
                "method private instance " + N + "EuObjectFactsDiscoveryPlan::Bind("
                    + N + "EuObjectFactsQueryDefinition, " + N + "EuObjectFactsQuerySet, System.Boolean, "
                    + "System.Collections.Generic.IReadOnlyList<System.String>, " + N + "EuObjectFactsQueryPass, "
                    + "System.Collections.Generic.IReadOnlyList<System.String>, "
                    + Core + "MachineResponseCardinality, System.String, System.String, "
                    + Core + "MachineQueryRendererSource) -> " + N + "EuObjectFactsBoundQuery",
                "method public instance " + N + "EuObjectFactsDiscoveryPlan::BindCount("
                    + N + "EuObjectFactsQuerySet, System.Collections.Generic.IReadOnlyList<System.String>, "
                    + N + "EuObjectFactsQueryPass, System.String, System.String, "
                    + Core + "MachineQueryRendererSource) -> " + N + "EuObjectFactsBoundQuery",
                "method public instance " + N + "EuObjectFactsDiscoveryPlan::BindPage("
                    + N + "EuObjectFactsQuerySet, System.Collections.Generic.IReadOnlyList<System.String>, "
                    + N + "EuObjectFactsQueryPass, System.Collections.Generic.IReadOnlyList<System.String>, "
                    + "System.Int64, " + Core + "SourceArtifactRef, System.String, System.String, "
                    + Core + "MachineQueryRendererSource) -> " + N + "EuObjectFactsBoundQuery",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuObjectFactsDiscoveryPlan).Assembly, typeof(EuObjectFactsBoundQuery), true).ToArray());
    }
}
