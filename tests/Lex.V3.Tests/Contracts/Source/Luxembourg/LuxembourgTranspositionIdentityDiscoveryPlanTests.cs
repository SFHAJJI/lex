using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgTranspositionIdentityDiscoveryPlanTests
{
    [TestMethod]
    public void ThePlanKeepsTheOriginalLegiluxTargetAndItsOptionalEuIdentityEvidence()
    {
        var plan = LuxembourgTranspositionIdentityDiscoveryPlan.Create();

        Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, plan.PublisherEndpoint);
        StringAssert.Contains(plan.PageTemplate, LuxembourgTranspositionIdentityDiscoveryPlan.TransposesPredicateIri);
        StringAssert.Contains(plan.PageTemplate, LuxembourgTranspositionIdentityDiscoveryPlan.SameAsPredicateIri);
        StringAssert.Contains(plan.PageTemplate, LuxembourgTranspositionIdentityDiscoveryPlan.EuDirectiveClassIri);
        Assert.AreEqual(2, plan.PageTemplate.Split("OPTIONAL {", StringSplitOptions.None).Length - 1,
            "Missing EU ELI or directive class must remain a delivered row for typed refusal.");
        StringAssert.Contains(plan.PageTemplate, "COALESCE(STR(?eu_eli), \"\") AS ?key_3");
        StringAssert.Contains(plan.PageTemplate, "COALESCE(STR(?eu_work_kind), \"\") AS ?key_4");
        Assert.IsFalse(plan.PageTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(plan.PageTemplate.Contains("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TheDeliveryProfilePinsEveryPublisherCoordinateAndHasNoCallerSelection()
    {
        var profile = LuxembourgTranspositionIdentityDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, profile.Dialect);
        CollectionAssert.AreEqual(Array.Empty<string>(), profile.SelectionParameterNames.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "measure", "local_eu_work", "eu_eli", "eu_work_kind", "multiplicity",
                "key_1", "key_2", "key_3", "key_4",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4" },
            profile.CanonicalKeyVariables.ToArray());
        CollectionAssert.AreEqual(profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());
        Assert.AreEqual(RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
    }

    [TestMethod]
    public void BindingProducesClosedCountAndPageRequests()
    {
        var plan = LuxembourgTranspositionIdentityDiscoveryPlan.Create();
        var sourceBytes = Encoding.UTF8.GetBytes("lu-transposition-identity-renderer-source/1\n");
        var source = MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:5f29a10c-cdb7-4e27-85e9-dce5e57b3739",
                Convert.ToHexStringLower(SHA256.HashData(sourceBytes))),
            sourceBytes);

        var count = plan.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:b236c5e5-afcb-4442-b0bc-bcda4ed23c38",
            "urn:uuid:babe695c-a794-4bc5-a2e5-1d0773855026",
            source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2,
            null,
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:e49dcba1-35d0-4c50-bbf8-41dd103d2eec",
            "urn:uuid:76079bbb-58cf-4806-a723-a57da3f01149",
            source);

        Assert.AreEqual("legilux-transposition-target-identities", count.InputArtifact.PartitionBinding.MemberKey);
        Assert.AreEqual(1, count.InputArtifact.OrderedParameters.Count);
        Assert.AreEqual(2, page.InputArtifact.OrderedParameters.Count);
        var body = Encoding.UTF8.GetString(count.Request.CopyRequestBody());
        StringAssert.StartsWith(body, "query=");
        StringAssert.Contains(Uri.UnescapeDataString(body[6..]), "VALUES ?lex_pass_id { 1 }");
    }
}
