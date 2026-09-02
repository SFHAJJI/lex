using System.Globalization;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgQueryPlanTests
{
    [TestMethod]
    public void FactoryPinsThePublisherTraversalAndDeterministicTwoPassPolicy()
    {
        var sourceProfileRef = Artifact("33333333-3333-4333-8333-333333333333", '3');
        var scopeDefinitionRef = Artifact("44444444-4444-4444-8444-444444444444", '4');

        var plan = LuxembourgQueryPlan.CreateDefaultGraph(sourceProfileRef, scopeDefinitionRef);

        Assert.AreEqual(LuxembourgQueryPlan.SchemaId, plan.Schema);
        Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, plan.DatasetGraphIdentity.Endpoint);
        Assert.AreEqual(sourceProfileRef, plan.DatasetGraphIdentity.SourceProfileRef);
        Assert.AreEqual(scopeDefinitionRef, plan.DatasetGraphIdentity.ScopeDefinitionRef);
        Assert.IsNull(typeof(LuxembourgDatasetGraphIdentity).GetProperty("CompleteEnumerationRef"));
        Assert.AreEqual(LuxembourgDatasetGraphKind.DefaultGraph, plan.DatasetGraphIdentity.Kind);
        AssertSortedUnique(plan.SchemeRoots);
        AssertSortedUnique(plan.SelectorPredicates);
        AssertSortedUnique(plan.RelationPredicates);
        AssertSortedUnique(plan.SetDefinitions.Select(static value => value.SetId));
        AssertSortedUnique(plan.QueryTemplates.Select(static value => value.TemplateId));
        CollectionAssert.AreEqual(
            new[] { "A", "C", "E", "G", "M", "O", "P", "R", "S", "T" },
            plan.SetDefinitions.Select(static value => value.SetId).ToArray());
        Assert.AreEqual(9, plan.QueryTemplates.Count);
        Assert.AreEqual(997u, plan.Pass1PageLimit);
        Assert.AreEqual(613u, plan.Pass2PageLimit);
        Assert.AreEqual(997u, plan.PageLimitFor(LuxembourgQueryPass.Pass1));
        Assert.AreEqual(613u, plan.PageLimitFor(LuxembourgQueryPass.Pass2));
        Assert.AreEqual(6, plan.KeysetSuccessorRule.ComponentCount);
        Assert.AreEqual(900u, plan.PartitionRule.AccumulatedCompletedSliceThreshold);
        Assert.AreEqual(
            "accumulated_completed_slice_cardinality",
            plan.PartitionRule.CardinalityBasis);
        Assert.AreEqual(
            "next_utf8_byte_00_80_successor",
            plan.PartitionRule.SplitRuleIdentity);
        Assert.AreEqual(899u, plan.PartitionRule.TerminalChildMaximumRows);
        Assert.IsTrue(plan.PartitionRule.EmptyChildRangesRetained);
        Assert.IsTrue(plan.CompletionRule.SuccessorAfterFullPageRequired);
        Assert.IsTrue(plan.CompletionRule.EmptySuccessorAfterShortPageRequired);
        Assert.IsTrue(plan.CompletionRule.DuplicateKeyRejectsObservation);
        Assert.IsTrue(plan.CompletionRule.NonStrictOrderRejectsObservation);
        Assert.IsNull(typeof(LuxembourgQueryPlan).GetProperty("PageTraversalRule"));

        foreach (var template in plan.QueryTemplates)
        {
            Assert.AreEqual(1, Count(template.Utf8QueryTemplate, "{page_limit:uint}"));
            Assert.AreEqual(1, Count(template.Utf8QueryTemplate, "{pass_id:uint}"));
            Assert.AreEqual(1, Count(template.Utf8CountTemplate, "{pass_id:uint}"));
            for (var part = 1; part <= 6; part++)
            {
                Assert.AreEqual(1, Count(
                    template.Utf8QueryTemplate,
                    $"{{partition_start_{part}:sparql_string}}"));
                Assert.AreEqual(1, Count(
                    template.Utf8QueryTemplate,
                    $"{{partition_end_{part}:sparql_string}}"));
            }
            Assert.AreEqual(1, Count(template.Utf8QueryTemplate, "{has_cursor:uint}"));
            for (var part = 1; part <= 6; part++)
            {
                Assert.AreEqual(1, Count(
                    template.Utf8QueryTemplate,
                    $"{{last_key_{part}:sparql_string}}"));
            }

            Assert.IsTrue(template.Utf8QueryTemplate.Contains(
                "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6",
                StringComparison.Ordinal));
            Assert.IsTrue(template.Utf8QueryTemplate.Contains(
                "?key_1 > ?last_key_1",
                StringComparison.Ordinal));
            Assert.IsTrue(template.Utf8QueryTemplate.Contains(
                "?key_1 = ?last_key_1 && ?key_2 > ?last_key_2",
                StringComparison.Ordinal));
            Assert.IsTrue(template.Utf8QueryTemplate.Contains(
                "?key_5 = ?last_key_5 && ?key_6 > ?last_key_6",
                StringComparison.Ordinal));
            Assert.IsFalse(template.Utf8QueryTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void BoundPageCarriesExactPartitionInputReceiptAndRequestEvidence()
    {
        var plan = Plan();
        var partition = new LuxembourgQueryPartitionRange(
            "subjects-http",
            Cursor("http://data.legilux.public.lu/"),
            Cursor("http://data.legilux.public.lv/"));

        var page = plan.BindPage(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            "S",
            LuxembourgQueryPass.Pass1,
            partition,
            new LuxembourgQueryCursor(
                "http://data.legilux.public.lu/a", "same", "x", "9", "", ""),
            24,
            Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
            Artifact("88888888-8888-4888-8888-888888888888", '8'));

        Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, page.Request.RequestedUri);
        Assert.AreEqual(HttpRequestMethod.Post, page.MachinePlan.Method);
        Assert.AreEqual(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            page.MachinePlan.ResponseCardinality.Kind);
        Assert.AreEqual(24, page.MachinePlan.ResponseCardinality.ExpectedPartitionRowCount);
        Assert.AreEqual(page.MachinePlanRef, page.Request.RenderReceipt.QueryPlanRef);
        Assert.AreEqual(page.InputArtifact.ArtifactRef, page.Request.RenderReceipt.OrderedParameterSetRef);
        Assert.AreEqual(page.InputArtifact.PartitionBinding, page.MachinePlan.PartitionBinding);
        Assert.AreEqual(partition.PartitionId, page.InputArtifact.PartitionBinding.MemberKey);
        CollectionAssert.AreEqual(
            new[]
            {
                "partition_start_1", "partition_start_2", "partition_start_3",
                "partition_start_4", "partition_start_5", "partition_start_6",
                "partition_end_1", "partition_end_2", "partition_end_3", "partition_end_4",
                "partition_end_5", "partition_end_6", "pass_id", "has_cursor",
                "last_key_1", "last_key_2", "last_key_3", "last_key_4", "last_key_5",
                "last_key_6",
            },
            page.InputArtifact.OrderedParameters.Select(static value => value.Name).ToArray());
        Assert.IsFalse(page.InputArtifact.OrderedParameters.Any(
            static value => value.Name == "page_limit"));
        var body = Encoding.UTF8.GetString(page.Request.CopyVerifiedRequestBody());
        StringAssert.StartsWith(body, "query=");
        var query = Uri.UnescapeDataString(body["query=".Length..]);
        StringAssert.Contains(query, "LIMIT 997");
        StringAssert.Contains(query, "http://data.legilux.public.lu/");
        StringAssert.Contains(query, "http://data.legilux.public.lv/");
        Assert.IsFalse(query.Contains("{page_limit:uint}", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));

        Assert.IsNull(typeof(LuxembourgBoundQueryPage).GetMethod("CreateRequestEvidence"));
    }

    [TestMethod]
    public void BoundCountCarriesOnlyPassAndExactPartitionThroughTheSharedBinder()
    {
        var plan = Plan();
        var count = plan.BindCount(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            "A",
            LuxembourgQueryPass.Pass2,
            new LuxembourgQueryPartitionRange("assertions", Cursor("a"), Cursor("z")),
            Artifact("88888888-8888-4888-8888-888888888888", '8'));

        Assert.AreEqual(MachineResponseCardinalityKind.OpaqueBody, count.MachinePlan.ResponseCardinality.Kind);
        CollectionAssert.AreEqual(
            new[]
            {
                "partition_start_1", "partition_start_2", "partition_start_3",
                "partition_start_4", "partition_start_5", "partition_start_6",
                "partition_end_1", "partition_end_2", "partition_end_3", "partition_end_4",
                "partition_end_5", "partition_end_6", "pass_id",
            },
            count.InputArtifact.OrderedParameters.Select(static value => value.Name).ToArray());
        var query = Uri.UnescapeDataString(
            Encoding.UTF8.GetString(count.Request.CopyVerifiedRequestBody())["query=".Length..]);
        StringAssert.Contains(query, "SELECT (COUNT(*) AS ?count)");
        StringAssert.Contains(query, "VALUES ?lex_pass_id { 2 }");
        Assert.IsFalse(query.Contains("LIMIT", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("last_key_", StringComparison.Ordinal));
        Assert.IsFalse(count.InputArtifact.OrderedParameters.Any(
            static value => value.Name is "page_limit" or "has_cursor" ||
                value.Name.StartsWith("last_key_", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void EveryPublisherQueryBuildsTheSharedDeliveryProfileAndExactInputRoles()
    {
        const string planResourceId = "urn:uuid:55555555-5555-4555-8555-555555555555";
        var plan = Plan();
        var partition = new LuxembourgQueryPartitionRange(
            "delivery-profile",
            Cursor("a"),
            Cursor("z"));
        var expectedSelection = Enumerable.Range(1, 6)
            .Select(static index => $"partition_start_{index}")
            .Concat(Enumerable.Range(1, 6)
                .Select(static index => $"partition_end_{index}"))
            .ToArray();
        var cursorVariables = Enumerable.Range(1, 6)
            .Select(static index => $"key_{index}")
            .ToArray();
        var cursorParameters = Enumerable.Range(1, 6)
            .Select(static index => $"last_key_{index}")
            .ToArray();

        foreach (var definition in plan.SetDefinitions.Where(
                     static value => value.Acquisition == LuxembourgQuerySetAcquisition.PublisherQuery))
        {
            var profile = plan.CreateDeliveryProfile(planResourceId, definition.SetId);
            var count = plan.BindCount(
                planResourceId,
                "urn:uuid:66666666-6666-4666-8666-666666666666",
                "urn:uuid:77777777-7777-4777-8777-777777777777",
                definition.SetId,
                LuxembourgQueryPass.Pass1,
                partition,
                Artifact("88888888-8888-4888-8888-888888888888", '8'));
            var page = plan.BindPage(
                planResourceId,
                "urn:uuid:99999999-9999-4999-8999-999999999999",
                "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                definition.SetId,
                LuxembourgQueryPass.Pass1,
                partition,
                lastCursor: null,
                expectedPartitionRowCount: 0,
                Artifact("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", 'b'),
                Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'));
            var continuedPage = plan.BindPage(
                planResourceId,
                "urn:uuid:99999999-9999-4999-8999-999999999998",
                "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaab",
                definition.SetId,
                LuxembourgQueryPass.Pass1,
                partition,
                Cursor("m"),
                expectedPartitionRowCount: 1,
                Artifact("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbc", 'd'),
                Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccd", 'e'));

            Assert.AreEqual(
                RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso,
                profile.Dialect);
            Assert.AreEqual(LuxembourgQueryPlan.PublisherDeliveryCeilingRows,
                profile.MaximumDeliverableRows);
            Assert.AreEqual(
                RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage,
                profile.TerminalPagePolicy);
            Assert.AreEqual(count.MachinePlan.QueryFamilyRef, profile.CountQueryFamilyRef);
            Assert.AreEqual(page.MachinePlan.QueryFamilyRef, profile.PageQueryFamilyRef);
            CollectionAssert.AreEqual(expectedSelection, profile.SelectionParameterNames.ToArray());
            var expectedProjection = definition.TemplateId switch
            {
                "assertion-rows" => new[]
                {
                    "subject", "predicate", "object", "object_kind", "datatype_iri",
                    "language_tag",
                }.Concat(cursorVariables).ToArray(),
                "relation-assertions" => new[]
                {
                    "subject", "predicate", "object",
                }.Concat(cursorVariables).ToArray(),
                _ => cursorVariables,
            };
            CollectionAssert.AreEqual(expectedProjection, profile.ProjectionVariables.ToArray());
            CollectionAssert.AreEqual(
                profile.ProjectionVariables.ToArray(),
                profile.CanonicalKeyVariables.ToArray());
            CollectionAssert.AreEqual(cursorVariables, profile.CursorVariables.ToArray());
            CollectionAssert.AreEqual(cursorParameters, profile.CursorParameterNames.ToArray());
            CollectionAssert.AreEqual(
                expectedSelection.Append("pass_id").ToArray(),
                count.InputArtifact.OrderedParameters.Select(static value => value.Name).ToArray());
            CollectionAssert.AreEqual(
                expectedSelection.Append("pass_id").Append("has_cursor").ToArray(),
                page.InputArtifact.OrderedParameters.Select(static value => value.Name).ToArray());
            CollectionAssert.AreEqual(
                expectedSelection.Append("pass_id").Append("has_cursor")
                    .Concat(cursorParameters).ToArray(),
                continuedPage.InputArtifact.OrderedParameters
                    .Select(static value => value.Name).ToArray());
        }

        Assert.ThrowsExactly<ArgumentException>(() =>
            plan.CreateDeliveryProfile(planResourceId, "M"));
        Assert.IsNull(typeof(LuxembourgQueryText).GetMethod("EncodeHex"));
        Assert.IsNull(typeof(LuxembourgQueryText).GetMethod("DecodeHex"));
        Assert.IsNull(typeof(LuxembourgQueryText).GetMethod("CompareUtf8"));
    }

    [TestMethod]
    public void LuxembourgDeliveryProfilePartitionsAtThePublisherCeiling()
    {
        var profile = Plan().CreateDeliveryProfile(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            "A");

        Assert.AreEqual(
            RepeatedEnumerationThresholdAssessment.BelowMaximum,
            EnumerationDeliveryComparison.AssessThreshold(
                LuxembourgQueryPlan.PublisherDeliveryCeilingRows - 1,
                profile));
        Assert.AreEqual(
            RepeatedEnumerationThresholdAssessment.PartitionRequired,
            EnumerationDeliveryComparison.AssessThreshold(
                LuxembourgQueryPlan.PublisherDeliveryCeilingRows,
                profile));
    }

    [TestMethod]
    public void PlanAndWireBytesAreCultureStableAndEveryIdentityInputIsLoadBearing()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var plan = Plan();
            var first = LuxembourgQueryPlanIdentity.GetCanonicalBytes(plan);
            Assert.AreEqual(78_973, first.Length);
            Assert.AreEqual(
                "8537234c5be8db84c3c040318167eefe3991046a7d4132c1e751c9d520af5b98",
                Sha256(first));
            Assert.IsFalse(first.Contains((byte)'\r'));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var second = LuxembourgQueryPlanIdentity.GetCanonicalBytes(Plan());
            CollectionAssert.AreEqual(first, second);

            var changedProfile = LuxembourgQueryPlan.CreateDefaultGraph(
                Artifact("33333333-3333-4333-8333-333333333333", '9'),
                Artifact("44444444-4444-4444-8444-444444444444", '4'));
            var changedScope = LuxembourgQueryPlan.CreateDefaultGraph(
                Artifact("33333333-3333-4333-8333-333333333333", '3'),
                Artifact("44444444-4444-4444-8444-444444444444", '9'));
            Assert.AreNotEqual(
                LuxembourgQueryPlanIdentity.Create("urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", plan),
                LuxembourgQueryPlanIdentity.Create("urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", changedProfile));
            Assert.AreNotEqual(
                LuxembourgQueryPlanIdentity.Create("urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", plan),
                LuxembourgQueryPlanIdentity.Create("urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", changedScope));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void CompositeCursorIsTieSafeAndAssertionsRetainUnsupportedBlankNodes()
    {
        var unsupportedBlankNodeCursor = new LuxembourgQueryCursor(
            "", "", "unsupported_blank_node", "", "", "");
        Assert.AreEqual("", unsupportedBlankNodeCursor.Key1);
        Assert.IsTrue(new LuxembourgQueryCursor("a", "b", "", "", "", "")
            .CompareTo(new LuxembourgQueryCursor("a", "a", "z", "z", "z", "z")) > 0);
        Assert.IsTrue(new LuxembourgQueryCursor("b", "a", "", "", "", "")
            .CompareTo(new LuxembourgQueryCursor("a", "z", "z", "z", "z", "z")) > 0);
        Assert.IsTrue(Cursor("\U00010000").CompareTo(Cursor("\uE000")) > 0);

        var templates = Plan().QueryTemplates.ToDictionary(
            static value => value.TemplateId,
            StringComparer.Ordinal);
        StringAssert.Contains(templates["subjects"].Utf8QueryTemplate, "FILTER(isIRI(?subject))");
        StringAssert.Contains(templates["types"].Utf8QueryTemplate, "FILTER(isIRI(?type))");
        StringAssert.Contains(
            templates["typed-resources"].Utf8QueryTemplate,
            "FILTER(isIRI(?resource) && isIRI(?type))");
        StringAssert.Contains(
            templates["relation-endpoints"].Utf8QueryTemplate,
            "FILTER(isIRI(?endpoint))");
        StringAssert.Contains(
            templates["assertion-rows"].Utf8QueryTemplate,
            "\"unsupported_blank_node\"");
        Assert.IsFalse(templates["assertion-rows"].Utf8QueryTemplate.Contains(
            "FILTER(isIRI(?subject)",
            StringComparison.Ordinal));
        StringAssert.Contains(
            templates["assertion-rows"].Utf8QueryTemplate,
            "IF(isIRI(?object) || isLiteral(?object), STR(?object), \"\") AS ?key_4");
        StringAssert.Contains(
            templates["assertion-rows"].Utf8QueryTemplate,
            "BIND(?datatype_iri AS ?key_5) BIND(?language_tag AS ?key_6)");
        Assert.IsFalse(templates["assertion-rows"].Utf8QueryTemplate.Contains(
            "CONCAT(",
            StringComparison.Ordinal));
        Assert.IsFalse(templates["relation-endpoints"].Utf8QueryTemplate.Contains(
            "BIND(STR(?predicate) AS ?key_1)",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void PassLimitsBindOnlyAtExecutionAndMalformedOrLocalInputsFailClosed()
    {
        var plan = Plan();
        var range = new LuxembourgQueryPartitionRange(
            "subjects-a",
            Cursor("a"),
            Cursor("z"));
        var renderer = Artifact("88888888-8888-4888-8888-888888888888", '8');
        var first = plan.BindPage(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            "S", LuxembourgQueryPass.Pass1, range, null, 10,
            Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'), renderer);
        var second = plan.BindPage(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            "S", LuxembourgQueryPass.Pass2, range, null, 10,
            Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'), renderer);

        Assert.AreEqual(first.InvariantPlanRef, second.InvariantPlanRef);
        Assert.AreNotEqual(first.InputArtifact.ArtifactRef, second.InputArtifact.ArtifactRef);
        Assert.AreNotEqual(first.MachinePlanRef, second.MachinePlanRef);
        Assert.AreEqual(
            "60b35a4cdd339ce101015a0ee73e2e638bc9205efe148042625b4348085b7adc",
            first.Request.RenderReceipt.RequestBodySha256);
        Assert.AreEqual(
            "1498d1f6af9970e7dd6e6d94e6c73abfce281c6762e8d3e076301ac0e0720f34",
            second.Request.RenderReceipt.RequestBodySha256);
        StringAssert.EndsWith(
            Uri.UnescapeDataString(Encoding.UTF8.GetString(first.Request.CopyRequestBody())[6..]),
            "LIMIT 997");
        StringAssert.EndsWith(
            Uri.UnescapeDataString(Encoding.UTF8.GetString(second.Request.CopyRequestBody())[6..]),
            "LIMIT 613");
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgQueryPartitionRange(
                "p",
                Cursor("z"),
                Cursor("a")));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Cursor("\ud800"));
        Assert.ThrowsExactly<ArgumentException>(() => plan.BindPage(
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            "M", LuxembourgQueryPass.Pass1, range, null, 10,
            Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'), renderer));
    }

    [TestMethod]
    public void PersistedPlanSchemaIsDeterministicClosedAndExactlyCheckedIn()
    {
        var first = LuxembourgQueryPlanSchemaExporter.ExportUtf8();
        var second = LuxembourgQueryPlanSchemaExporter.ExportUtf8();
        CollectionAssert.AreEqual(first, second);
        Assert.AreEqual((byte)'\n', first[^1]);
        Assert.IsFalse(first.Contains((byte)'\r'));
        var checkedPath = Path.Combine(
            RepositoryRoot(),
            "schemas",
            "v3-source",
            LuxembourgQueryPlanSchemaExporter.FileName);
        CollectionAssert.AreEqual(first, File.ReadAllBytes(checkedPath));
    }

    [TestMethod]
    public void ClosedParserRejectsTemplatePredicateIdentityAndRepresentationDrift()
    {
        var plan = Plan();
        var planRef = LuxembourgQueryPlanIdentity.Create(
            "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            plan);
        var bytes = LuxembourgQueryPlan.GetWireBytes(plan);

        CollectionAssert.AreEqual(
            bytes,
            LuxembourgQueryPlan.GetWireBytes(LuxembourgQueryPlan.ParseAndVerify(planRef, bytes)));
        Assert.AreEqual(0, typeof(LuxembourgQueryPlan).GetConstructors().Length);
        var json = Encoding.UTF8.GetString(bytes);
        var predicateMutation = Encoding.UTF8.GetBytes(json.Replace(
            "http://data.legilux.public.lu/resource/ontology/jolux#typeDocument",
            "http://data.legilux.public.lu/resource/ontology/jolux#typeDocumentDrift",
            StringComparison.Ordinal));
        var templateMutation = Encoding.UTF8.GetBytes(json.Replace(
            "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6",
            "ORDER BY ?key_6 ?key_5 ?key_4 ?key_3 ?key_2 ?key_1",
            StringComparison.Ordinal));
        var passLimitMutation = Encoding.UTF8.GetBytes(json.Replace(
            "\"pass_1_page_limit\":997",
            "\"pass_1_page_limit\":998",
            StringComparison.Ordinal));
        var partitionMutation = Encoding.UTF8.GetBytes(json.Replace(
            "\"accumulated_completed_slice_threshold\":900",
            "\"accumulated_completed_slice_threshold\":899",
            StringComparison.Ordinal));
        var completionMutation = Encoding.UTF8.GetBytes(json.Replace(
            "\"empty_successor_after_short_page_required\":true",
            "\"empty_successor_after_short_page_required\":false",
            StringComparison.Ordinal));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgQueryPlan.ParseAndVerify(planRef, predicateMutation));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgQueryPlan.ParseAndVerify(planRef, templateMutation));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgQueryPlan.ParseAndVerify(planRef, passLimitMutation));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgQueryPlan.ParseAndVerify(planRef, partitionMutation));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgQueryPlan.ParseAndVerify(planRef, completionMutation));
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgQueryPlan.ParseAndVerify(
            Artifact("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", 'f'),
            bytes));
    }

    [TestMethod]
    public void CountTemplatesMatchEveryPublisherFamilyWithoutClaimingCompleteness()
    {
        var plan = Plan();
        var publisherTemplates = plan.SetDefinitions
            .Where(static value => value.Acquisition == LuxembourgQuerySetAcquisition.PublisherQuery)
            .Select(static value => value.TemplateId)
            .ToArray();
        CollectionAssert.AreEquivalent(
            plan.QueryTemplates.Select(static value => value.TemplateId).ToArray(),
            publisherTemplates);
        foreach (var template in plan.QueryTemplates)
        {
            StringAssert.Contains(template.Utf8CountTemplate, "SELECT (COUNT(*) AS ?count)");
            Assert.IsFalse(template.Utf8CountTemplate.Contains("{page_limit:uint}", StringComparison.Ordinal));
            Assert.IsFalse(template.Utf8CountTemplate.Contains("last_key_", StringComparison.Ordinal));
            Assert.IsFalse(template.Utf8CountTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
            for (var part = 1; part <= 6; part++)
            {
                Assert.AreEqual(1, Count(
                    template.Utf8CountTemplate,
                    $"{{partition_start_{part}:sparql_string}}"));
                Assert.AreEqual(1, Count(
                    template.Utf8CountTemplate,
                    $"{{partition_end_{part}:sparql_string}}"));
            }

            AssertCompositePartitionFilter(template.Utf8QueryTemplate);
            AssertCompositePartitionFilter(template.Utf8CountTemplate);
            StringAssert.Contains(
                template.Utf8QueryTemplate,
                "?key_5 = ?last_key_5 && ?key_6 > ?last_key_6");
        }

        var assertions = plan.QueryTemplates.Single(static value => value.TemplateId == "assertion-rows");
        StringAssert.Contains(assertions.Utf8QueryTemplate, "?subject ?predicate ?object ?object_kind");
        StringAssert.Contains(assertions.Utf8QueryTemplate, "?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6");
        var relations = plan.QueryTemplates.Single(static value => value.TemplateId == "relation-assertions");
        StringAssert.Contains(relations.Utf8QueryTemplate, "?subject ?predicate ?object");
        Assert.IsFalse(plan.SchemeRoots.Contains(
            "http://creativecommons.org/licenses/by/4.0/",
            StringComparer.Ordinal));
        Assert.AreEqual(26, plan.SelectorPredicates.Count);
        Assert.AreEqual(18, plan.RelationPredicates.Count);
        CollectionAssert.AreEqual(
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
                .Where(static value => value.Kind == LuxembourgVocabularyKind.AssertionPredicate)
                .Select(static value => value.FullIri)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            plan.SelectorPredicates.ToArray());
        CollectionAssert.AreEqual(
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
                .Where(static value => value.Kind == LuxembourgVocabularyKind.RelationPredicate)
                .Select(static value => value.FullIri)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            plan.RelationPredicates.ToArray());
    }

    [TestMethod]
    public void RenderedWireIsCultureIdenticalAndHostileTextCannotBreakItsSparqlString()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var range = new LuxembourgQueryPartitionRange(
                "hostile-range",
                Cursor("a\" ) } OFFSET 1 #\nnext\u001Ffield"),
                Cursor("z"));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var first = Bind(range);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var second = Bind(range);
            CollectionAssert.AreEqual(first.Request.CopyRequestBody(), second.Request.CopyRequestBody());
            var query = Uri.UnescapeDataString(Encoding.UTF8.GetString(first.Request.CopyRequestBody())[6..]);
            StringAssert.Contains(query, "\"a\\\" ) } OFFSET 1 #\\nnext\\u001Ffield\"");
            Assert.IsFalse(query.Contains("#\nnext", StringComparison.Ordinal));

            var unicodeRange = new LuxembourgQueryPartitionRange(
                "unicode-range",
                Cursor("\uE000"),
                Cursor("\U00010000"));
            var unicode = Bind(unicodeRange);
            var encodedStart = unicode.InputArtifact.OrderedParameters.Single(
                static value => value.Name == "partition_start_1");
            var encodedEnd = unicode.InputArtifact.OrderedParameters.Single(
                static value => value.Name == "partition_end_1");
            // 2026-09-02 publisher VALUES/ORDER BY calibration returned these in this order.
            Assert.AreEqual("hee8080", encodedStart.TextValue);
            Assert.AreEqual("hf0908080", encodedEnd.TextValue);
            Assert.ThrowsExactly<ArgumentException>(() =>
                EnumerationCursorEnvelope.Decode("hEE8080"));
            var unicodeQuery = Uri.UnescapeDataString(
                Encoding.UTF8.GetString(unicode.Request.CopyRequestBody())["query=".Length..]);
            StringAssert.Contains(unicodeQuery, "\"\uE000\"");
            StringAssert.Contains(unicodeQuery, "\"\U00010000\"");
            Assert.ThrowsExactly<ArgumentException>(() => Bind(
                new LuxembourgQueryPartitionRange(
                    "outside",
                    Cursor("m"),
                    Cursor("z")),
                Cursor("a")));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static LuxembourgQueryPlan Plan() => LuxembourgQueryPlan.CreateDefaultGraph(
        Artifact("33333333-3333-4333-8333-333333333333", '3'),
        Artifact("44444444-4444-4444-8444-444444444444", '4'));

    private static void AssertCompositePartitionFilter(string query)
    {
        StringAssert.Contains(
            query,
            "?key_5 = ?partition_start_5 && ?key_6 >= ?partition_start_6");
        StringAssert.Contains(
            query,
            "?key_5 = ?partition_end_5 && ?key_6 < ?partition_end_6");
    }

    private static LuxembourgQueryCursor Cursor(string key1) =>
        new(key1, "", "", "", "", "");

    private static LuxembourgBoundQueryPage Bind(
        LuxembourgQueryPartitionRange range,
        LuxembourgQueryCursor? cursor = null) => Plan().BindPage(
        "urn:uuid:55555555-5555-4555-8555-555555555555",
        "urn:uuid:66666666-6666-4666-8666-666666666666",
        "urn:uuid:77777777-7777-4777-8777-777777777777",
        "S",
        LuxembourgQueryPass.Pass1,
        range,
        cursor,
        10,
        Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
        Artifact("88888888-8888-4888-8888-888888888888", '8'));

    private static SourceArtifactRef Artifact(string id, char digest) =>
        new($"urn:uuid:{id}", new string(digest, 64));

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value)).ToLowerInvariant();

    private static void AssertSortedUnique(IEnumerable<string> values)
    {
        var actual = values.ToArray();
        var expected = actual.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }

    private static int Count(string value, string needle) =>
        value.Split(needle, StringSplitOptions.None).Length - 1;

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
