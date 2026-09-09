using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgTranspositionIdentityDiscoveryPlanTests
{
    private const string EuEli = "http://data.europa.eu/eli/dir/2020/284/oj";
    [TestMethod]
    public void ThePlanSelectsRequiredEuIdentitiesAndKeepsClassificationOptional()
    {
        var plan = LuxembourgTranspositionIdentityDiscoveryPlan.Create();

        Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, plan.PublisherEndpoint);
        StringAssert.Contains(plan.PageTemplate, LuxembourgTranspositionIdentityDiscoveryPlan.TransposesPredicateIri);
        StringAssert.Contains(plan.PageTemplate, LuxembourgTranspositionIdentityDiscoveryPlan.SameAsPredicateIri);
        StringAssert.Contains(plan.PageTemplate, LuxembourgTranspositionIdentityDiscoveryPlan.EuDirectiveClassIri);
        Assert.AreEqual(1, plan.PageTemplate.Split("OPTIONAL {", StringSplitOptions.None).Length - 1,
            "The selected EU identity is required; only Legilux's local directive classification is optional.");
        StringAssert.Contains(plan.PageTemplate, "VALUES ?eu_eli {");
        StringAssert.Contains(plan.PageTemplate, "COALESCE(STR(?eu_eli), \"\") AS ?key_3");
        StringAssert.Contains(plan.PageTemplate, "COALESCE(STR(?eu_work_kind), \"\") AS ?key_4");
        Assert.IsFalse(plan.PageTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, plan.PageTemplate.Split("SELECT DISTINCT ?eu_eli WHERE", StringSplitOptions.None).Length - 1,
            "Only the fixed selection is deduplicated; publisher relation rows retain multiplicity evidence.");

        var cursorFilter = plan.PageTemplate.IndexOf("FILTER(\n", StringComparison.Ordinal);
        var aggregation = plan.PageTemplate.IndexOf("GROUP BY", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, cursorFilter);
        Assert.IsTrue(cursorFilter < aggregation,
            "the cursor must restrict publisher rows before aggregation so an empty successor does not aggregate the full family.");
    }

    [TestMethod]
    public void TheDeliveryProfilePinsEveryPublisherCoordinateAndTheBoundedSelection()
    {
        var profile = LuxembourgTranspositionIdentityDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, profile.Dialect);
        Assert.AreEqual(LuxembourgTranspositionIdentityDiscoveryPlan.BatchCapacity,
            profile.SelectionParameterNames.Count);
        Assert.AreEqual("batch_eu_eli_000", profile.SelectionParameterNames[0]);
        Assert.AreEqual("batch_eu_eli_057", profile.SelectionParameterNames[^1]);
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
    public void AForeignSameAsTargetCannotEscapeTheExactSelectedEliSet()
    {
        var template = LuxembourgTranspositionIdentityDiscoveryPlan.Create().PageTemplate;
        var values = template.IndexOf("VALUES ?eu_eli {", StringComparison.Ordinal);
        var relation = template.IndexOf(
            "?local_eu_work <http://www.w3.org/2002/07/owl#sameAs> ?eu_eli",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, values);
        Assert.IsTrue(values < relation,
            "owl:sameAs must join the caller's exact admitted ELI selection rather than enumerate foreign targets.");
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
            [EuEli],
            "urn:uuid:b236c5e5-afcb-4442-b0bc-bcda4ed23c38",
            "urn:uuid:babe695c-a794-4bc5-a2e5-1d0773855026",
            source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2,
            [EuEli],
            null,
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:e49dcba1-35d0-4c50-bbf8-41dd103d2eec",
            "urn:uuid:76079bbb-58cf-4806-a723-a57da3f01149",
            source);

        Assert.AreEqual("legilux-transposition-target-identities", count.InputArtifact.PartitionBinding.MemberKey);
        Assert.AreEqual(1 + LuxembourgTranspositionIdentityDiscoveryPlan.BatchCapacity,
            count.InputArtifact.OrderedParameters.Count);
        Assert.AreEqual(2 + LuxembourgTranspositionIdentityDiscoveryPlan.BatchCapacity,
            page.InputArtifact.OrderedParameters.Count);
        Assert.IsTrue(count.InputArtifact.OrderedParameters.Take(
            LuxembourgTranspositionIdentityDiscoveryPlan.BatchCapacity).All(parameter =>
            parameter.Kind == MachineQueryParameterKind.PublisherLiteral &&
            parameter.TextValue == EuEli));
        Assert.AreEqual("pass_id", count.InputArtifact.OrderedParameters[^1].Name);
        var body = Encoding.UTF8.GetString(count.Request.CopyRequestBody());
        StringAssert.StartsWith(body, "query=");
        StringAssert.Contains(Uri.UnescapeDataString(body[6..]), "VALUES ?lex_pass_id { 1 }");
        StringAssert.Contains(Uri.UnescapeDataString(body[6..]), $"<{EuEli}>");
    }

    [TestMethod]
    public void TheSelectionRefusesNonDirectiveForeignAndRepeatedElis()
    {
        var plan = LuxembourgTranspositionIdentityDiscoveryPlan.Create();
        var sourceBytes = Encoding.UTF8.GetBytes("lu-transposition-identity-renderer-source/2\n");
        var source = MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:3c19e68d-4dc2-49d9-a69d-41f35ca7973e",
                Convert.ToHexStringLower(SHA256.HashData(sourceBytes))),
            sourceBytes);

        foreach (var inadmissible in new[]
                 {
                     "http://data.europa.eu/eli/reg/2020/284/oj",
                     "http://data.legilux.public.lu/eli/dir_ue/2020/284/jo",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => plan.BindCount(
                LuxembourgQueryPass.Pass1,
                [inadmissible],
                "urn:uuid:ad8e5af1-673e-4e96-8df0-1bd9aa3fe1f4",
                "urn:uuid:a67c0e3c-740c-482c-a822-913dfe7ef09a",
                source));
        }

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindCount(
            LuxembourgQueryPass.Pass1,
            [EuEli, EuEli],
            "urn:uuid:f9913e03-c22d-47b3-b811-02070a610155",
            "urn:uuid:90524b76-eb50-4373-9480-057897538fa8",
            source));
    }
}
