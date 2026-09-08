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
        Assert.IsFalse(plan.PageTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(plan.PageTemplate.Contains("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(
            "13e3f53d213751c83e38195585f1a7b0e6d26ea9775008de578967c670ababbd",
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
                "eli", "eli_kind", "multiplicity", "key_1", "key_2", "key_3", "key_4", "key_5",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5" },
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
        StringAssert.Contains(countText, "VALUES ?lex_pass_id { 1 }");
        Assert.IsFalse(countText.Contains("{pass_id:uint}", StringComparison.Ordinal));
    }
}
