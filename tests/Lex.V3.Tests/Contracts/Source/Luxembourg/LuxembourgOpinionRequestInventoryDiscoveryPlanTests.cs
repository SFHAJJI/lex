using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// What the OpinionRequest inventory asks Legilux, pinned at the query text it actually sends.
/// </summary>
/// <remarks>
/// A producer test cannot stand in for this: producers are fed authored pages, so nothing else in
/// the suite executes this SPARQL. The sibling draft-graph plan went in with no template test and
/// the gap was only found when a repair to its cursor keys passed the whole repository in both
/// directions.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestInventoryDiscoveryPlanTests
{
    [TestMethod]
    public void TheFamilyAsksForTheClassAndNothingElse()
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();

        Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, plan.PublisherEndpoint);

        var membership =
            "?request a <" + LuxembourgOpinionRequestInventoryDiscoveryPlan.OpinionRequestClassIri + "> .";
        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, membership);
            Assert.AreEqual(
                1,
                template.Split(membership, StringSplitOptions.None).Length - 1,
                "each template wraps exactly one instance of the membership question.");
        }

        // NARROW MEANS NARROW. This family asks which subjects exist and must not acquire a property
        // on the way. referralDate is the reason this class matters, and asking for it HERE would
        // make this a second request graph and reintroduce the width the draft sweep was refused for.
        Assert.IsFalse(
            plan.PageTemplate.Contains("referralDate", StringComparison.Ordinal),
            "the inventory enumerates subjects; the request graph asks what they hold.");
        Assert.IsFalse(
            plan.PageTemplate.Contains("VALUES ?predicate", StringComparison.Ordinal),
            "the inventory asks about no property.");
        Assert.IsFalse(
            plan.PageTemplate.Contains("hasOpinion", StringComparison.Ordinal),
            "the draft relationship is not an input here: this family is request-scoped and the "
                + "traversal belongs to a composite reconciliation citing both proof-bound outputs.");
        Assert.IsFalse(
            plan.PageTemplate.Contains("OPTIONAL", StringComparison.Ordinal),
            "there is nothing optional to ask for.");
        Assert.IsFalse(
            plan.PageTemplate.Contains("UNION", StringComparison.Ordinal),
            "a class member that is not there is not a row, so there is no absence branch.");
        Assert.IsFalse(
            plan.PageTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase),
            "pagination is by keyset; OFFSET over an unstable order would skip and repeat rows.");
    }

    /// <summary>
    /// Exactly one column is grouped, and it is the one the publisher answers.
    /// </summary>
    /// <remarks>
    /// Inherited discipline rather than a measurement of this class: Legilux refused the
    /// five-predicate draft graph at the pass-one count with <c>Virtuoso SR319</c> twice, grouping
    /// seven columns and then three. Whether it would refuse a broad sweep of THIS class is
    /// unmeasured, and this family does not find out by widening.
    /// </remarks>
    [TestMethod]
    public void OnlyThePublisherAnsweredSubjectIsGrouped()
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, "GROUP BY ?request");
            Assert.AreEqual(
                1,
                template.Split("GROUP BY", StringSplitOptions.None).Length - 1,
                "one grouping, over the subject alone.");
        }
    }

    /// <summary>A blank-node member is marked, never filtered away.</summary>
    [TestMethod]
    public void ANonAddressableMemberIsMarkedRatherThanFilteredOut()
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();

        Assert.IsFalse(
            plan.PageTemplate.Contains("isIRI(?request))", StringComparison.Ordinal) &&
            plan.PageTemplate.Contains("FILTER(isIRI", StringComparison.Ordinal),
            "a subject the publisher holds must not vanish from an inventory calling itself complete.");
        StringAssert.Contains(
            plan.PageTemplate, LuxembourgOpinionRequestInventoryDiscoveryPlan.UnsupportedBlankNodeKind);
    }

    /// <summary>
    /// Every derived key is totalised, because this engine evaluates IF's arguments eagerly.
    /// </summary>
    /// <remarks>
    /// A dereference of a term the engine will not accept raises, the erroring BIND leaves the key
    /// unbound, and SPARQL's JSON omits the column from the binding entirely - so an untotalised
    /// key does not arrive wrong, it does not arrive at all.
    /// </remarks>
    [TestMethod]
    public void EveryDerivedKeyIsTotalised()
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();

        foreach (var bind in new[] { "AS ?request_kind)", "AS ?key_1)" })
        {
            var index = plan.PageTemplate.IndexOf(bind, StringComparison.Ordinal);
            Assert.IsGreaterThan(-1, index, bind);
            var start = plan.PageTemplate.LastIndexOf("BIND(", index, StringComparison.Ordinal);
            StringAssert.Contains(
                plan.PageTemplate[start..(index + bind.Length)], "COALESCE(",
                $"{bind} must survive an eager dereference.");
        }
    }

    [TestMethod]
    public void TheKeysetOrdersByAllOfItselfAndTheProfilePinsThePublisherCoordinates()
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();

        CollectionAssert.AreEqual(
            new[] { "request", "request_kind", "multiplicity", "key_1", "key_2" },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(new[] { "key_1", "key_2" }, profile.CanonicalKeyVariables.ToArray());
        CollectionAssert.AreEqual(
            profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());
        CollectionAssert.AreEqual(
            profile.CanonicalKeyVariables.Select(static key => "last_" + key).ToArray(),
            profile.CursorParameterNames.ToArray());

        StringAssert.Contains(plan.PageTemplate, "ORDER BY ?key_1 ?key_2");
        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, profile.Dialect);
        Assert.AreEqual("application/sparql-results+json", profile.ExpectedMediaType);
        Assert.AreEqual(RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
        Assert.AreEqual("count", profile.CountVariable);
        Assert.AreEqual("pass_id", profile.PassParameterName);
        Assert.AreEqual("has_cursor", profile.HasCursorParameterName);

        // EMPTY, and that is the decision this family exists to make. A caller who could select
        // would be a caller who could narrow what "the whole class" means.
        Assert.IsEmpty(profile.SelectionParameterNames);
    }

    [TestMethod]
    public void TheTwoPassesDifferInPageSizeAndNotInQuestion()
    {
        var limits = Enum.GetValues<LuxembourgQueryPass>()
            .Select(LuxembourgOpinionRequestInventoryDiscoveryPlan.PageLimit)
            .ToArray();

        Assert.HasCount(
            limits.Distinct().Count(), limits,
            "identical limits would let a page-boundary defect reproduce itself and agree.");
        Assert.AreEqual(
            1,
            LuxembourgOpinionRequestInventoryDiscoveryPlan.Create().PageTemplate
                .Split("LIMIT {page_limit:uint}", StringSplitOptions.None).Length - 1,
            "the limit is a bound parameter, so both passes send the same text.");
    }

    /// <summary>
    /// This family cannot be mistaken for, or silently share state with, the draft inventory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SIBLING IS A NEAR-COPY, which is exactly the hazard. This plan was written by mirroring
    /// <see cref="LuxembourgInitialDraftInventoryDiscoveryPlan"/>, and a mirrored file that kept one
    /// of the original's coordinates would be a family that proves the wrong thing while looking
    /// right: a shared partition key would let a citation bind a proof from the other family, and a
    /// shared resource id would give two different questions one artifact identity.
    /// </para>
    /// <para>
    /// The page limits are included because they are the one coordinate a reader would think
    /// cosmetic. Two families sharing them is not a correctness failure by itself, but it removes
    /// the independence that makes a cross-family cursor mix-up show up as a boundary disagreement.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void NoCoordinateIsSharedWithTheDraftInventoryItWasMirroredFrom()
    {
        var mine = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();
        var sibling = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();

        // Through a BOUND request rather than the constants, so this states what is actually sent
        // and not what a pair of literals says about itself.
        var source = RendererSource();
        var minePartition = mine.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:1f47d6b0-3c82-4e95-a61d-70b4e8c295f3",
            "urn:uuid:6d015b93-84a7-4c2e-9f38-b2705ae61c4d",
            source).InputArtifact.PartitionBinding.MemberKey;
        var siblingPartition = sibling.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:53e2907c-6b41-4d38-85af-c9130b7e4d26",
            "urn:uuid:0c9b5d41-7e26-4a93-b8f0-15d4c6270e8a",
            source).InputArtifact.PartitionBinding.MemberKey;

        Assert.AreNotEqual(
            siblingPartition, minePartition,
            "a shared partition key lets a citation bind the other family's proof.");
        Assert.AreNotEqual(
            sibling.ArtifactRef.ResourceId, mine.ArtifactRef.ResourceId,
            "two questions must not share one artifact identity.");
        Assert.AreNotEqual(
            sibling.ArtifactRef.Sha256, mine.ArtifactRef.Sha256,
            "and their canonical identities must differ, because the questions differ.");
        Assert.AreNotEqual(
            sibling.CountQueryFamilyRef.MemberKey, mine.CountQueryFamilyRef.MemberKey);
        Assert.AreNotEqual(
            sibling.PageQueryFamilyRef.MemberKey, mine.PageQueryFamilyRef.MemberKey);

        foreach (var pass in Enum.GetValues<LuxembourgQueryPass>())
        {
            Assert.AreNotEqual(
                LuxembourgInitialDraftInventoryDiscoveryPlan.PageLimit(pass),
                LuxembourgOpinionRequestInventoryDiscoveryPlan.PageLimit(pass),
                $"{pass}: independent boundaries are what make a cross-family cursor mix-up visible.");
        }

        Assert.IsFalse(
            mine.PageTemplate.Contains(
                LuxembourgInitialDraftInventoryDiscoveryPlan.InitialDraftClassIri, StringComparison.Ordinal),
            "the mirrored template must not still ask about the class it was copied from.");
    }

    [TestMethod]
    public void ThePlanIdentityCoversTheQuestionItAsks()
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();
        var identity = Encoding.UTF8.GetString(plan.CopyCanonicalIdentityBytes());

        StringAssert.Contains(identity, plan.CountTemplate.Trim());
        StringAssert.Contains(identity, plan.PageTemplate.Trim());
        StringAssert.Contains(
            identity, LuxembourgOpinionRequestInventoryDiscoveryPlan.OpinionRequestClassIri);
        StringAssert.Contains(identity, "canonical_keys=key_1,key_2");
        StringAssert.Contains(identity, "terminal_page_policy=short_page_terminal");
        Assert.AreEqual(
            plan.ArtifactRef.Sha256,
            Convert.ToHexStringLower(SHA256.HashData(plan.CopyCanonicalIdentityBytes())),
            "the artifact digest is over the identity bytes, so widening the question changes it.");
    }

    [TestMethod]
    public void BindingProducesClosedCountAndPageRequestsAndRefusesAPartialCursor()
    {
        var plan = LuxembourgOpinionRequestInventoryDiscoveryPlan.Create();
        var source = RendererSource();

        var count = plan.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:8c4d1e97-2a63-4f05-b8e1-3d70f9a5c264",
            "urn:uuid:61b0f3a8-5d92-4c47-ae03-7f28d15be940",
            source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2,
            null,
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:2e7a95c3-4b18-4d60-9f27-c0a83b165e7d",
            "urn:uuid:47d29f16-8e35-4a02-b1c9-5603ea7d8f41",
            source);

        Assert.AreEqual(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKey,
            count.InputArtifact.PartitionBinding.MemberKey);
        Assert.HasCount(1, count.InputArtifact.OrderedParameters);
        Assert.AreEqual("pass_id", count.InputArtifact.OrderedParameters[0].Name);
        Assert.HasCount(2, page.InputArtifact.OrderedParameters);
        Assert.AreEqual("has_cursor", page.InputArtifact.OrderedParameters[1].Name);

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindPage(
            LuxembourgQueryPass.Pass1,
            ["only-one-part"],
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:93f5b027-6c41-4e89-a350-1d84f7c26b0e",
            "urn:uuid:0a86d4f2-71b9-4c53-8e27-b5f309c14a6d",
            source));
    }

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-opinion-request-inventory-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:7b3e08c5-9d47-4a16-bf20-6c85e1d39074",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }
}
