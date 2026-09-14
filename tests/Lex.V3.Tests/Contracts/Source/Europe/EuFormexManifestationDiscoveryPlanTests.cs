using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Tests.Contracts.Source.Europe;

[TestClass]
public sealed class EuFormexManifestationDiscoveryPlanTests
{
    private const string Work =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string Expression = Work + ".0024";
    private const string OtherExpression = Work + ".0001";
    private static LanguageScopedExpressionIdentity Identity(string expression = Expression) => new(Work, expression);

    [TestMethod]
    public void BothPassesAskForTypesOfTheExactExpressionAndProjectItsIdentity()
    {
        var plan = EuFormexManifestationDiscoveryPlan.Create();
        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, "VALUES (?work ?expression) { ({work_iri:iri} {expression_iri:iri}) }");
            StringAssert.Contains(template,
                "?expression <http://publications.europa.eu/ontology/cdm#expression_belongs_to_work> ?work .");
            StringAssert.Contains(template,
                "?manifestation <http://publications.europa.eu/ontology/cdm#manifestation_manifests_expression> ?expression .");
            StringAssert.Contains(template,
                "?manifestation <http://publications.europa.eu/ontology/cdm#manifestation_type> ?manifestation_type .");
            StringAssert.Contains(template, "SELECT ?work ?expression ?manifestation_type");
        }
    }

    [TestMethod]
    public void TheQuestionDoesNotFilterToFormexAndThereforeCanProveEligibility()
    {
        var plan = EuFormexManifestationDiscoveryPlan.Create();
        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            Assert.IsFalse(template.Contains("fmx4", StringComparison.Ordinal),
                "filtering to Formex would hide other types and weaken the complete per-expression answer.");
        }
    }

    [TestMethod]
    public void FamilyIdentityUsesBothExpressionIdentityComponentsAndTheWholeDigest()
    {
        var key = EuFormexManifestationDiscoveryPlan.PartitionKeyFor(Identity());
        var other = EuFormexManifestationDiscoveryPlan.PartitionKeyFor(Identity(OtherExpression));
        Assert.StartsWith(EuFormexManifestationDiscoveryPlan.PartitionKeyPrefix, key);
        Assert.AreEqual(64, key[EuFormexManifestationDiscoveryPlan.PartitionKeyPrefix.Length..].Length);
        Assert.AreNotEqual(key, other);
    }

    [TestMethod]
    public void AnExpressionMustBeTheExactNumericChildOfItsWork()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuFormexManifestationDiscoveryPlan.PartitionKeyFor(new(Work, Work + "/0024")));
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuFormexManifestationDiscoveryPlan.PartitionKeyFor(new(Work, Work + ".en")));
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuFormexManifestationDiscoveryPlan.PartitionKeyFor(new(Work, OtherExpression + "/../0024")));
    }

    [TestMethod]
    public void BoundRequestsCarryBothCoordinatesAndFixedPassLimits()
    {
        var plan = EuFormexManifestationDiscoveryPlan.Create();
        var source = Source();
        var count = plan.BindCount(Identity(), EuFormexManifestationQueryPass.Pass1,
            NewUrn(), NewUrn(), source);
        var page = plan.BindPage(Identity(), EuFormexManifestationQueryPass.Pass2, null, 0,
            new SourceArtifactRef(NewUrn(), new string('a', 64)), NewUrn(), NewUrn(), source);
        Assert.AreEqual(EuFormexManifestationDiscoveryPlan.PartitionKeyFor(Identity()),
            count.InputArtifact.PartitionBinding.MemberKey);
        CollectionAssert.AreEqual(
            new[] { "work_iri", "expression_iri", "pass_id" },
            count.InputArtifact.OrderedParameters.Select(static value => value.Name).ToArray());
        var countText = Encoding.UTF8.GetString(count.Request.CopyRequestBody());
        var pageText = Encoding.UTF8.GetString(page.Request.CopyRequestBody());
        StringAssert.Contains(countText, "<" + Work + "> <" + Expression + ">");
        StringAssert.Contains(pageText, "LIMIT 283");
    }

    [TestMethod]
    public void AValidPartitionPairedWithAnotherExpressionCannotRender()
    {
        var plan = EuFormexManifestationDiscoveryPlan.Create();
        var renderer = new EuFormexManifestationSparqlRenderer(plan, false, Source());
        var response = new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null);
        var input = MachineQueryInputArtifact.Create(
            NewUrn(), plan.CountQueryFamilyRef,
            EuFormexManifestationDiscoveryPlan.PartitionKeyFor(Identity()), response,
            [
                new("work_iri", MachineQueryParameterKind.PublisherLiteral, null, Work, plan.ArtifactRef),
                new("expression_iri", MachineQueryParameterKind.PublisherLiteral, null, OtherExpression, plan.ArtifactRef),
                new("pass_id", MachineQueryParameterKind.BoundedInteger, 1, null, plan.ArtifactRef),
            ]);
        Assert.ThrowsExactly<ArgumentException>(() => renderer.RenderInput(input, response));
    }

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("eu-formex-manifestation-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static string NewUrn() => "urn:uuid:" + Guid.NewGuid().ToString("D");
}
