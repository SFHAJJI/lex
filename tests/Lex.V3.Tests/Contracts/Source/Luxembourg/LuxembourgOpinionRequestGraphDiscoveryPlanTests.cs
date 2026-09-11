using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// What the OpinionRequest graph asks Legilux, pinned at the query text it actually sends.
/// </summary>
/// <remarks>
/// A producer test cannot stand in for this: producers are fed authored pages, so nothing else in
/// the suite executes this SPARQL.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestGraphDiscoveryPlanTests
{
    private static readonly string[] Batch =
    [
        "http://data.legilux.public.lu/eli/dl/pl/2000/119/evenement/sace/1",
        "http://data.legilux.public.lu/eli/dl/pc/2002/215/evenement/sace/1",
    ];

    /// <summary>
    /// The rendered question is the proven draft-graph question, under three substitutions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE MIRROR, PINNED AS A MIRROR. This plan was generated from
    /// <see cref="LuxembourgDraftGraphDiscoveryPlan"/> by substitution, because its batched
    /// seven-column shape is the only property sweep in this family measured running to completion
    /// against this engine — thirteen batch runs in the retained acceptance packet, and again in the
    /// codec validation of 2026-09-11T15:36:49Z which delivered 115 rows over five subjects. What
    /// Legilux refused with <c>Virtuoso SR319</c> was that family's UNBOUNDED sweep, class-wide and
    /// seven columns wide, and this is not that.
    /// </para>
    /// <para>
    /// Whole-text equality, not expected fragments. The same assertion on the inventory plan caught
    /// a one-clause change to a continuation <c>FILTER</c> that ten fragment tests passed over, and
    /// a generated file is exactly where that drift is cheapest to introduce and hardest to see.
    /// Three substitutions are allowed and no more: the class IRI, the subject variable, and the
    /// batch parameter prefix. Anything else has to be argued for rather than inherited.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheRenderedQuestionIsTheProvenDraftGraphQuestionUnderThreeSubstitutions()
    {
        var mine = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var proven = LuxembourgDraftGraphDiscoveryPlan.Create();

        static string Fold(string template, string classIri, string subject, string batchPrefix) =>
            template
                .Replace(classIri, "{class}", StringComparison.Ordinal)
                .Replace(subject, "{subject}", StringComparison.Ordinal)
                .Replace(batchPrefix, "{batch}", StringComparison.Ordinal);

        static string Mine(string template) => Fold(
            template, LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri,
            "?request", "batch_request_");

        static string Proven(string template) => Fold(
            template, LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri,
            "?draft", "batch_draft_");

        Assert.AreEqual(
            Proven(proven.CountTemplate), Mine(mine.CountTemplate),
            "the count is the proven count, or this family asks something unmeasured.");
        Assert.AreEqual(
            Proven(proven.PageTemplate), Mine(mine.PageTemplate),
            "and so is the page, including every bind, filter, order and limit slot.");

        // The substitutions are real: a template that never mentioned its own class, subject or
        // batch parameters could otherwise pass by having nothing to substitute.
        StringAssert.Contains(mine.PageTemplate, "?request");
        StringAssert.Contains(mine.PageTemplate, "batch_request_");
        StringAssert.Contains(
            mine.PageTemplate, LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri);
        Assert.IsFalse(
            mine.PageTemplate.Contains("?draft", StringComparison.Ordinal),
            "the generated template must not still bind the subject it was copied from.");
        Assert.IsFalse(
            mine.PageTemplate.Contains("batch_draft_", StringComparison.Ordinal),
            "nor bind the other family's batch parameters.");
    }

    /// <summary>
    /// The class is the accepted coordinate, asserted on rendered output rather than on a constant.
    /// </summary>
    /// <remarks>
    /// The inventory plan restated its class IRI and a reviewer changed it to a different, valid
    /// class with all nine of its tests still passing, because every expectation derived from the
    /// value under test. Comparing an alias to its owner is compile-time true and the analyzer
    /// rejects it, so the pins here are on the template and the identity bytes, both built at run
    /// time.
    /// </remarks>
    [TestMethod]
    public void TheClassIsTheAcceptedCoordinateAndNotThisFamilysOwnSpelling()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var identity = Encoding.UTF8.GetString(plan.CopyCanonicalIdentityBytes());
        const string Accepted = "http://data.legilux.public.lu/resource/ontology/jolux#OpinionRequest";

        StringAssert.Contains(plan.PageTemplate, "?request a <" + Accepted + "> .");
        StringAssert.Contains(plan.CountTemplate, "?request a <" + Accepted + "> .");
        StringAssert.Contains(identity, "request_class=" + Accepted);
        StringAssert.Contains(
            plan.PageTemplate,
            "<" + LuxembourgDraftGraphDiscoveryPlan.OpinionRequestClassIri + ">");

        foreach (var neighbour in new[]
        {
            LuxembourgOpinionLinkOnlyVocabulary.OpinionConseilEtatClassIri,
            LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri,
        })
        {
            Assert.IsFalse(
                plan.PageTemplate.Contains("<" + neighbour + ">", StringComparison.Ordinal),
                $"the rendered question must not ask about {neighbour}.");
            Assert.IsFalse(
                identity.Contains("request_class=" + neighbour, StringComparison.Ordinal),
                $"and the identity must not claim {neighbour}.");
        }

        // referralDate is the one accepted predicate, and it is the accepted coordinate too.
        StringAssert.Contains(
            identity,
            "asked_predicates=" + LuxembourgDraftGraphDiscoveryPlan.ReferralDatePredicateIri);
    }

    /// <summary>The publisher is asked for everything, and admission happens locally.</summary>
    /// <remarks>
    /// The mirrored family was measured returning zero <c>parliamentDraftUrl</c> rows for fifty
    /// drafts that hold them while a predicate <c>VALUES</c> block sat in front of its triple. A
    /// filter reintroduced here would be that defect, in a family whose accepted predicate is the
    /// one thing E8 needs from this class.
    /// </remarks>
    [TestMethod]
    public void TheQueryFiltersNoPredicateAndMaterialisesNoAbsence()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            Assert.IsFalse(
                template.Contains("VALUES ?predicate", StringComparison.Ordinal),
                "a predicate filter is how rows the publisher holds stop arriving.");
            Assert.IsFalse(
                template.Contains("OPTIONAL", StringComparison.Ordinal),
                "the absent case is derived locally, never requested.");
            Assert.IsFalse(
                template.Contains("UNION", StringComparison.Ordinal),
                "and there is no absence branch to union in.");
            Assert.IsFalse(
                template.Contains("OFFSET", StringComparison.OrdinalIgnoreCase),
                "pagination is by keyset; OFFSET over an unstable order skips and repeats rows.");
        }

        // referralDate must not appear in the QUERY: it is what this family admits, not what it asks.
        Assert.IsFalse(
            plan.PageTemplate.Contains("referralDate", StringComparison.Ordinal),
            "asking only for referralDate would be the predicate filter under another name.");
    }

    /// <summary>No coordinate is shared with the family this was generated from.</summary>
    [TestMethod]
    public void NoCoordinateIsSharedWithTheFamilyItWasGeneratedFrom()
    {
        var mine = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var proven = LuxembourgDraftGraphDiscoveryPlan.Create();

        Assert.AreNotEqual(proven.ArtifactRef.ResourceId, mine.ArtifactRef.ResourceId);
        Assert.AreNotEqual(proven.ArtifactRef.Sha256, mine.ArtifactRef.Sha256);
        Assert.AreNotEqual(proven.CountQueryFamilyRef.MemberKey, mine.CountQueryFamilyRef.MemberKey);
        Assert.AreNotEqual(proven.PageQueryFamilyRef.MemberKey, mine.PageQueryFamilyRef.MemberKey);

        foreach (var pass in Enum.GetValues<LuxembourgQueryPass>())
        {
            Assert.AreNotEqual(
                LuxembourgDraftGraphDiscoveryPlan.PageLimit(pass),
                LuxembourgOpinionRequestGraphDiscoveryPlan.PageLimit(pass),
                $"{pass}: independent boundaries make a cross-family cursor mix-up visible.");
        }

        // The partition keys are prefixed per family, checked through a bound request rather than
        // through two literals.
        var source = RendererSource();
        Assert.AreNotEqual(
            proven.BindCount(
                    LuxembourgQueryPass.Pass1,
                    ["http://data.legilux.public.lu/eli/dl/pl/2000/119"],
                    NewUrn(), NewUrn(), source)
                .InputArtifact.PartitionBinding.MemberKey,
            mine.BindCount(LuxembourgQueryPass.Pass1, Batch, NewUrn(), NewUrn(), source)
                .InputArtifact.PartitionBinding.MemberKey,
            "a shared partition key lets a citation bind the other family's proof.");
    }

    [TestMethod]
    public void TheKeysetOrdersByAllOfItselfAndTheProfilePinsThePublisherCoordinates()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();

        CollectionAssert.AreEqual(
            new[]
            {
                "request", "request_kind", "predicate", "value", "value_kind", "datatype_iri",
                "language_tag", "multiplicity",
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7" },
            profile.CanonicalKeyVariables.ToArray());
        CollectionAssert.AreEqual(
            profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());

        StringAssert.Contains(
            plan.PageTemplate, "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 ?key_7");
        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, profile.Dialect);
        Assert.AreEqual(
            RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
        Assert.HasCount(
            LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity,
            profile.SelectionParameterNames,
            "the selection binds one slot per batch member, before pass_id.");
    }

    /// <summary>
    /// <c>key_4</c> asks the endpoint for its own digest and the producer recomputes it locally.
    /// </summary>
    /// <remarks>
    /// The query asks for <c>SHA256</c> because that is the function this endpoint exposes; what it
    /// returns is not the SPARQL 1.1 function of that name. The plan's remarks say so and the
    /// producer recomputes through <see cref="LuxembourgPublisherCursorCodec"/>, so this asserts the
    /// query shape rather than the key's meaning.
    /// </remarks>
    [TestMethod]
    public void TheValueKeyIsADigestOfTheValueAndTheRawValueIsStillProjected()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();

        StringAssert.Contains(plan.PageTemplate, "SHA256(COALESCE(STR(?value), \"\")) AS ?key_4");
        StringAssert.Contains(
            plan.PageTemplate, "?value",
            "the complete raw value stays projected; the digest keys the row, it does not replace it.");
        Assert.IsTrue(
            plan.CreateDeliveryProfile().ProjectionVariables.Contains("value"),
            "and the delivery profile carries it.");
    }

    [TestMethod]
    public void BindingProducesClosedCountAndPageRequestsAndRefusesAPartialCursor()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        var source = RendererSource();

        var count = plan.BindCount(LuxembourgQueryPass.Pass1, Batch, NewUrn(), NewUrn(), source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2, Batch, null, 0, count.InputArtifact.ArtifactRef,
            NewUrn(), NewUrn(), source);

        StringAssert.StartsWith(
            count.InputArtifact.PartitionBinding.MemberKey, "legilux-opinion-request-graph-batch-");
        Assert.HasCount(
            LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity + 1,
            count.InputArtifact.OrderedParameters,
            "every batch slot plus pass_id, so a short batch is the same input role as a full one.");
        Assert.HasCount(
            LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity + 2,
            page.InputArtifact.OrderedParameters);

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindPage(
            LuxembourgQueryPass.Pass1, Batch, ["only-one-part"], 0,
            count.InputArtifact.ArtifactRef, NewUrn(), NewUrn(), source));
        Assert.ThrowsExactly<ArgumentException>(
            () => plan.BindCount(LuxembourgQueryPass.Pass1, [], NewUrn(), NewUrn(), source),
            "an empty batch asks about nothing and must not bind.");
    }

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-opinion-request-graph-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
