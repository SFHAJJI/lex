using System.Text;
using System.Security.Cryptography;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Tests.Contracts.Source.Europe;

[TestClass]
public sealed class EuNationalImplementingMeasureDiscoveryPlanTests
{
    [TestMethod]
    public void ThePlanIsLuxembourgOnlySectorSevenAndKeepsBothPublisherPredicates()
    {
        var plan = EuNationalImplementingMeasureDiscoveryPlan.Create();

        StringAssert.Contains(plan.PageTemplate, "measure_national_implementing");
        StringAssert.Contains(plan.PageTemplate, EuNationalImplementingMeasureDiscoveryPlan.LuxembourgCountryIri);
        StringAssert.Contains(plan.PageTemplate, "STRSTARTS(STR(?nim_celex), \"7\")");
        StringAssert.Contains(plan.PageTemplate, EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri);
        StringAssert.Contains(plan.PageTemplate, EuNationalImplementingMeasureDiscoveryPlan.LegacyImplementsDirectivePredicateIri);
        StringAssert.Contains(plan.PageTemplate, EuNationalImplementingMeasureDiscoveryPlan.EliPredicateIri);
        StringAssert.Contains(plan.PageTemplate, EuNationalImplementingMeasureDiscoveryPlan.EuWorkEliPredicateIri);
        StringAssert.Contains(plan.PageTemplate, EuNationalImplementingMeasureDiscoveryPlan.WorkHasResourceTypePredicateIri);
        Assert.AreEqual(2, plan.PageTemplate.Split("OPTIONAL {", StringSplitOptions.None).Length - 1,
            "Both the target ELI and its publisher resource type remain observable when absent.");
        Assert.IsFalse(plan.PageTemplate.Contains("?eu_work a ?eu_work_kind", StringComparison.Ordinal));
        StringAssert.Contains(plan.PageTemplate, "COALESCE(STR(?eu_work_eli), \"\") AS ?key_5");
        StringAssert.Contains(plan.PageTemplate, "COALESCE(STR(?eu_work_kind), \"\") AS ?key_6");
        StringAssert.Contains(plan.PageTemplate, "ENCODE_FOR_URI(STR(?nim))");
        StringAssert.Contains(plan.PageTemplate, "FILTER(?has_cursor = 0 || ?page_key > ?last_page_key)");
        StringAssert.Contains(plan.PageTemplate, "ORDER BY ?page_key");
        Assert.IsFalse(plan.PageTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(plan.PageTemplate.Contains("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(
            "b5629669e048954ef4d8e72d489ebb5509498d479fa1c9c0998889f47065232c",
            plan.ArtifactRef.Sha256);
    }

    [TestMethod]
    public void TheDeliveryProfilePinsTheWholeRowAndHasNoCallerChosenSelection()
    {
        var profile = EuNationalImplementingMeasureDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso, profile.Dialect);
        CollectionAssert.AreEqual(Array.Empty<string>(), profile.SelectionParameterNames.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "nim", "country", "nim_celex", "implements_predicate", "eu_work",
                "eu_work_eli", "eu_work_kind", "eli", "eli_kind", "multiplicity",
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7", "page_key",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "page_key" },
            profile.CanonicalKeyVariables.ToArray());
        CollectionAssert.AreEqual(profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());
        Assert.AreEqual(RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
    }

    [TestMethod]
    public void BindingProducesAClosedCountAndPageWithoutSelectionParameters()
    {
        var plan = EuNationalImplementingMeasureDiscoveryPlan.Create();
        var sourceBytes = Encoding.UTF8.GetBytes("eu-nim-renderer-source/1\n");
        var source = MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:517c57f4-0040-4ed3-9f69-45f456218e12",
                Convert.ToHexStringLower(SHA256.HashData(sourceBytes))),
            sourceBytes);

        var count = plan.BindCount(
            EuNationalImplementingMeasureQueryPass.Pass1,
            "urn:uuid:3c96a0a6-8822-474f-96e4-ec4640527cd3",
            "urn:uuid:4d19d7b7-d933-4244-83f5-5d5752545749",
            source);
        var page = plan.BindPage(
            EuNationalImplementingMeasureQueryPass.Pass2,
            null,
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:5c144bf5-22f2-4a62-b8e7-c18b65a225a4",
            "urn:uuid:6fb2158d-538e-419f-987f-0c98f90be26b",
            source);

        Assert.AreEqual("luxembourg-sector-7-national-implementing-measures", count.InputArtifact.PartitionBinding.MemberKey);
        Assert.AreEqual(1, count.InputArtifact.OrderedParameters.Count);
        Assert.AreEqual(2, page.InputArtifact.OrderedParameters.Count);
        var countText = Encoding.UTF8.GetString(count.Request.CopyRequestBody());
        var pageText = Encoding.UTF8.GetString(page.Request.CopyRequestBody());
        StringAssert.Contains(countText, "VALUES ?lex_pass_id { 1 }");
        Assert.IsFalse(countText.Contains("{pass_id:uint}", StringComparison.Ordinal));
        Assert.IsFalse(
            pageText.Contains("{last_key_", StringComparison.Ordinal),
            "Every cursor slot in the production page request must be rendered before send.");
        StringAssert.Contains(pageText, "(0 \"\")");
    }

    [TestMethod]
    public void PageAppliesTheCursorToPublisherRowsBeforeGrouping()
    {
        var page = EuNationalImplementingMeasureDiscoveryPlan.Create().PageTemplate;
        var cursor = page.IndexOf("VALUES (?has_cursor ?last_page_key)", StringComparison.Ordinal);
        var grouping = page.IndexOf("GROUP BY ?nim", StringComparison.Ordinal);
        var keyProjection = page.IndexOf("BIND(STR(?nim) AS ?key_1)", StringComparison.Ordinal);

        Assert.IsTrue(cursor >= 0 && cursor < grouping && grouping < keyProjection,
            "The cursor must exclude raw publisher rows before aggregation and key projection. " +
            "Applying it to aliases after GROUP BY lets Virtuoso repeat the tail of a page.");
        StringAssert.Contains(page, "ENCODE_FOR_URI(STR(?nim))");
        StringAssert.Contains(page, "ENCODE_FOR_URI(COALESCE(STR(?eli), \"\"))");
        StringAssert.Contains(page, "?page_key > ?last_page_key");
        Assert.IsFalse(page.Contains("?key_1 > ?last_key_1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MissingOrUnadmittedWorkKindsRemainObservableForTheProducerToRefuse()
    {
        var page = EuNationalImplementingMeasureDiscoveryPlan.Create().PageTemplate;
        var observedKind =
            $"OPTIONAL {{ ?eu_work <{EuNationalImplementingMeasureDiscoveryPlan.WorkHasResourceTypePredicateIri}> ?eu_work_kind . }}";

        StringAssert.Contains(
            page,
            observedKind,
            "The family must deliver a missing or unadmitted publisher type to the existing typed producer refusal.");
        Assert.IsFalse(
            page.Contains("VALUES ?eu_work_kind", StringComparison.Ordinal),
            "A closed query-side VALUES clause silently discards a real third publisher type such as DEC.");
    }
}
