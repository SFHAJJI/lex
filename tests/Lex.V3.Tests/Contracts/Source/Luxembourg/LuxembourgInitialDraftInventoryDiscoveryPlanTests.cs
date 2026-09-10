using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// What the InitialDraft inventory asks Legilux, pinned at the query text it actually sends.
/// </summary>
/// <remarks>
/// The sibling draft-graph plan went in with no template test at all, and the gap was only found
/// when a repair to its cursor keys passed the whole repository in both directions. A producer test
/// cannot stand in for this: the producer is fed authored pages, so nothing in the suite executes
/// this SPARQL.
/// </remarks>
[TestClass]
public sealed class LuxembourgInitialDraftInventoryDiscoveryPlanTests
{
    /// <summary>
    /// The eager-guard form banned repository wide, matched on the BIND that carries it so this
    /// file's own prose does not match itself.
    /// </summary>
    private const string EagerGuardForm = "BIND(IF(BOUND(";

    [TestMethod]
    public void TheFamilyAsksForTheClassAndNothingElse()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();

        Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, plan.PublisherEndpoint);

        var membership = "?draft a <" + LuxembourgInitialDraftInventoryDiscoveryPlan.InitialDraftClassIri + "> .";
        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, membership);
            Assert.AreEqual(
                1,
                template.Split(membership, StringSplitOptions.None).Length - 1,
                "each template wraps exactly one instance of the membership question.");
        }

        // NARROW MEANS NARROW. This family asks which subjects exist and must not acquire a property
        // on the way: a predicate here would make it a second draft graph and reintroduce the width
        // and volume that Legilux refused.
        Assert.IsFalse(
            plan.PageTemplate.Contains("VALUES ?predicate", StringComparison.Ordinal),
            "the inventory asks about no property.");
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
    /// <para>
    /// This is the measurement, not a preference. Legilux refused the five-predicate draft graph at
    /// the pass-one count with <c>Virtuoso SR319: Max row length is exceeded</c>, twice — once
    /// grouping seven columns and once grouping three — so this family groups the subject alone and
    /// derives everything else afterwards. That is the smallest form the question has.
    /// </para>
    /// <para>
    /// A later edit that grouped a derived column back in would be reintroducing the shape the
    /// publisher refused, which is why the assertion is on the GROUP BY itself rather than on a
    /// count of columns somewhere.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void OnlyThePublisherAnsweredSubjectIsGrouped()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, "GROUP BY ?draft\n");
            Assert.AreEqual(
                1,
                template.Split("GROUP BY", StringSplitOptions.None).Length - 1,
                "one grouping, and it groups one column.");
        }

        foreach (var derived in new[] { "?draft_kind", "?key_1", "?key_2" })
        {
            StringAssert.Contains(
                plan.PageTemplate, "AS " + derived + ")",
                $"{derived} is bound in the page, after the grouping.");
            Assert.IsFalse(
                plan.PageTemplate.Contains("GROUP BY ?draft " + derived, StringComparison.Ordinal),
                $"{derived} must not be grouped: that is the shape the publisher refused.");
        }
    }

    /// <summary>
    /// A subject the publisher holds but a later batch cannot name is TYPED, never filtered.
    /// </summary>
    /// <remarks>
    /// The membership triple carries no <c>isIRI</c> guard, and that is the owner ruling's
    /// requirement rather than an omission. Filtering a blank-node member at the query would make a
    /// member the publisher holds vanish from an inventory calling itself complete, and would do it
    /// invisibly, because the count would agree with the pages. The marker is what makes it a typed
    /// gap for the batching stage instead.
    /// </remarks>
    [TestMethod]
    public void ANonAddressableMemberIsMarkedRatherThanFilteredOut()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();

        Assert.IsFalse(
            plan.CountTemplate.Contains("isIRI(?draft)", StringComparison.Ordinal),
            "the count must ask the same question as the page, and the page filters no member.");
        Assert.IsFalse(
            plan.PageTemplate.Contains("FILTER(isIRI(?draft))", StringComparison.Ordinal),
            "a blank-node member is carried and marked, not dropped.");
        StringAssert.Contains(
            plan.PageTemplate,
            "\"" + LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind + "\"",
            "the marker names what a non-IRI member is.");
    }

    /// <summary>Every derived key is total, against an engine that leaves its source unbound.</summary>
    [TestMethod]
    public void EveryDerivedKeyIsTotalised()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();

        StringAssert.Contains(plan.PageTemplate, "BIND(COALESCE(STR(?draft), \"\") AS ?key_1)");
        StringAssert.Contains(plan.PageTemplate, "BIND(COALESCE(IF(isIRI(?draft),");

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            Assert.IsFalse(
                template.Contains(EagerGuardForm, StringComparison.Ordinal),
                "this guard reads as present and does nothing on this publisher's engine.");
        }
    }

    [TestMethod]
    public void TheKeysetOrdersByAllOfItselfAndTheProfilePinsThePublisherCoordinates()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();

        CollectionAssert.AreEqual(
            new[] { "draft", "draft_kind", "multiplicity", "key_1", "key_2" },
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
        // would be a caller who could narrow what "the whole class" means. The BATCHING stage takes
        // its members from this run's proven output instead — and when a selection appears there,
        // RequireInputRoleShape compares SelectionParameterNames.Append(PassParameterName) by
        // sequence, so it binds BEFORE pass_id.
        Assert.IsEmpty(profile.SelectionParameterNames);
    }

    [TestMethod]
    public void TheTwoPassesDifferInPageSizeAndNotInQuestion()
    {
        var limits = Enum.GetValues<LuxembourgQueryPass>()
            .Select(LuxembourgInitialDraftInventoryDiscoveryPlan.PageLimit)
            .ToArray();

        Assert.HasCount(
            limits.Distinct().Count(), limits,
            "identical limits would let a page-boundary defect reproduce itself and agree.");
        Assert.AreEqual(
            1,
            LuxembourgInitialDraftInventoryDiscoveryPlan.Create().PageTemplate
                .Split("LIMIT {page_limit:uint}", StringSplitOptions.None).Length - 1,
            "the limit is a bound parameter, so both passes send the same text.");
    }

    [TestMethod]
    public void ThePlanIdentityCoversTheQuestionItAsks()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();
        var identity = Encoding.UTF8.GetString(plan.CopyCanonicalIdentityBytes());

        StringAssert.Contains(identity, plan.CountTemplate.Trim());
        StringAssert.Contains(identity, plan.PageTemplate.Trim());
        StringAssert.Contains(identity, LuxembourgInitialDraftInventoryDiscoveryPlan.InitialDraftClassIri);

        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(plan.CopyCanonicalIdentityBytes())),
            plan.ArtifactRef.Sha256,
            "the reference must digest the identity it claims to name.");

        // A DIFFERENT FAMILY, PROVABLY. Sharing a resource id or a member prefix with the draft
        // graph would let one family's evidence be read under the other's profile.
        var draftGraph = LuxembourgDraftGraphDiscoveryPlan.Create();
        Assert.AreNotEqual(draftGraph.ArtifactRef.ResourceId, plan.ArtifactRef.ResourceId);
        Assert.AreNotEqual(draftGraph.ArtifactRef.Sha256, plan.ArtifactRef.Sha256);
        Assert.AreNotEqual(
            draftGraph.CountQueryFamilyRef.MemberKey, plan.CountQueryFamilyRef.MemberKey);
    }

    [TestMethod]
    public void BindingProducesClosedCountAndPageRequestsAndRefusesAPartialCursor()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();
        var source = RendererSource();

        var count = plan.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:1d7f0a52-6b93-4c48-8e15-9240cb7f36a1",
            "urn:uuid:47ea9b16-0c85-4d37-a2f9-16b3e5407d28",
            source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2,
            null,
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:9b2e4c07-53a1-4f86-b70d-8c15dea9036f",
            "urn:uuid:3f60d84b-27c9-4a15-9e0d-58a17c2fb903",
            source);

        Assert.AreEqual(
            LuxembourgInitialDraftInventoryDiscoveryPlan.PartitionMemberKey,
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
            "urn:uuid:5c81fa73-0d26-4b98-8f41-6ac0d537e912",
            "urn:uuid:2a9b7e13-4d06-4c58-91f7-6e30ba5c8d40",
            source));
    }

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-initial-draft-inventory-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:0e5c3a71-8b24-4d96-a03f-71c8e2f5b940",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }
}
