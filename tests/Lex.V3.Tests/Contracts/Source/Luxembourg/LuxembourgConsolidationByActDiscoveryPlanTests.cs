using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// #419's per-act consolidation family: the hostile cases the design review named as the minimum
/// before freeze.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS FAMILY HAS TO SURVIVE IS NOT "DOES IT RENDER". A digest-shaped partition key looks
/// exactly as authoritative when it is attached to the wrong question, and #584's review is the
/// reason this family exists at all: a zero-row proof of an unrelated family satisfied every check a
/// never-consolidated entry could perform. So the cases here are the ones where something plausible
/// is presented and must be refused, plus the two where a restriction is deleted and the family must
/// stop being about an act.
/// </para>
/// <para>
/// The sixth case the review listed — a returned row naming another act — is a DECODER case and is
/// not here, because this slice ships no row reader to test it against. It belongs to the decoding
/// slice and is named in that slice's own declaration rather than quietly dropped.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgConsolidationByActDiscoveryPlanTests
{
    private const string Act =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";

    private const string OtherAct =
        "http://data.legilux.public.lu/eli/etat/leg/rgd/2019/07/12/a512/jo";

    /// <summary>The key is the full digest of the exact act IRI, under a versioned prefix.</summary>
    /// <remarks>
    /// The digest is recomputed here from the IRI's own bytes rather than compared against a
    /// checked-in literal. A pinned literal would agree with whatever the implementation produced,
    /// including a truncation; this fails if the plan digests anything other than the act.
    /// </remarks>
    [TestMethod]
    public void ThePartitionKeyIsTheWholeDigestOfTheExactActIri()
    {
        var key = LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(Act);

        Assert.StartsWith(LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyPrefix, key);
        Assert.AreEqual(
            91, key.Length,
            "27 characters of versioned prefix and a whole 64-character digest, inside the "
            + "128-character machine-member limit.");

        var expected = Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(Act)));
        Assert.AreEqual(
            LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyPrefix + expected,
            key,
            "the key digests the act IRI's own bytes, whole and unnormalized.");
        Assert.AreEqual(
            64, expected.Length,
            "the premise: a whole SHA-256, so the assertion above is about a truncation too.");
    }

    /// <summary>HOSTILE CASE: another act's correctly formed family key is a different key.</summary>
    /// <remarks>
    /// The point is not that two digests differ — that is arithmetic. It is that the key another act
    /// legitimately mints is well-formed, prefix-correct and the right length, so nothing about its
    /// SHAPE distinguishes it. Only the digest does, which is why the digest is whole.
    /// </remarks>
    [TestMethod]
    public void AnotherActsCorrectlyFormedFamilyKeyIsNotThisActs()
    {
        var mine = LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(Act);
        var theirs = LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(OtherAct);

        Assert.AreNotEqual(mine, theirs);
        Assert.AreEqual(mine.Length, theirs.Length, "both are well formed; only the digest differs.");
        Assert.StartsWith(LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyPrefix, theirs);
    }

    /// <summary>HOSTILE CASE: the generic family name is not this family's key for any act.</summary>
    /// <remarks>
    /// <c>laws</c> is the family key the never-consolidated frame's own fixtures use, and #584's
    /// review established that such a key paired with an act proves nothing. It cannot be minted
    /// here for any act, and this says so against a real act rather than in prose.
    /// </remarks>
    [TestMethod]
    public void TheGenericLawsFamilyIsNotThisFamilysKeyForAnyAct()
    {
        Assert.AreNotEqual("laws", LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(Act));
        Assert.AreNotEqual("laws", LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(OtherAct));
        Assert.IsFalse(
            "laws".StartsWith(LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyPrefix, StringComparison.Ordinal),
            "a generic family name cannot even be mistaken for one of this family's keys.");
    }

    /// <summary>
    /// HOSTILE CASE: this act's key cannot be paired with another act's selection, because the key
    /// is derived from the selection rather than accepted beside it.
    /// </summary>
    /// <remarks>
    /// The design review asked for a builder that refuses a supplied key which is not the recomputed
    /// key for the parameter. This plan goes one step further and admits no supplied key at all:
    /// <c>Bind</c> computes the partition from the same validated act it binds as the selection
    /// parameter, so the mismatch has no argument list to live in. What can still be asserted — and
    /// is, here — is that the partition a bound input actually carries IS the key for the act that
    /// input selects, for two different acts.
    /// </remarks>
    [TestMethod]
    public void ABoundInputsPartitionIsTheKeyForTheActItSelects()
    {
        var plan = LuxembourgConsolidationByActDiscoveryPlan.Create();

        var mine = plan.BindCount(Act, LuxembourgQueryPass.Pass1, NewUrn(), NewUrn(), RendererSource());
        var theirs = plan.BindCount(OtherAct, LuxembourgQueryPass.Pass1, NewUrn(), NewUrn(), RendererSource());

        Assert.AreEqual(
            LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(Act),
            mine.InputArtifact.PartitionBinding.MemberKey);
        Assert.AreEqual(
            LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(OtherAct),
            theirs.InputArtifact.PartitionBinding.MemberKey);
        Assert.AreNotEqual(
            mine.InputArtifact.PartitionBinding.MemberKey,
            theirs.InputArtifact.PartitionBinding.MemberKey,
            "two acts cannot share a partition, or the key stops bounding anything.");
    }

    /// <summary>
    /// HOSTILE CASE: a truncated or hash-only implementation would drop the act from the canonical
    /// input. It is there, typed, with the exact bytes.
    /// </summary>
    /// <remarks>
    /// A digest cannot be reversed, so a key alone leaves a reader unable to check WHICH act an
    /// enumeration asked about — they could only check that some act hashes to it, which requires
    /// already knowing the answer. Retaining the exact IRI as a typed parameter is what makes the
    /// key auditable rather than merely unique.
    /// </remarks>
    [TestMethod]
    public void TheExactActSurvivesIntoTheCanonicalInputAsATypedParameter()
    {
        var plan = LuxembourgConsolidationByActDiscoveryPlan.Create();
        var count = plan.BindCount(Act, LuxembourgQueryPass.Pass1, NewUrn(), NewUrn(), RendererSource());
        var page = plan.BindPage(
            Act, LuxembourgQueryPass.Pass1, null, 0, count.InputArtifact.ArtifactRef,
            NewUrn(), NewUrn(), RendererSource());

        foreach (var bound in new[] { count, page })
        {
            var parameter = bound.InputArtifact.OrderedParameters.Single(
                value => value.Name == LuxembourgConsolidationByActDiscoveryPlan.ActSelectionParameterName);

            Assert.AreEqual(MachineQueryParameterKind.PublisherLiteral, parameter.Kind);
            Assert.AreEqual(Act, parameter.TextValue, "the exact act, not a digest of it.");
        }

        // AND IT BINDS FIRST. RequireInputRoleShape compares the profile's selection parameter names
        // appended with the pass parameter by SEQUENCE, so a selection bound after pass_id would be
        // a differently shaped input wearing the same names.
        Assert.AreEqual(
            LuxembourgConsolidationByActDiscoveryPlan.ActSelectionParameterName,
            count.InputArtifact.OrderedParameters[0].Name);
    }

    /// <summary>
    /// HOSTILE CASE: the act restriction is in the count template and in the page template, each
    /// exactly once.
    /// </summary>
    /// <remarks>
    /// Asserted on BOTH independently because they are deleted independently in mutation. A count
    /// restricted to the act beside a page that is not would deliver a page the count never
    /// described, and the two-pass comparison would then be reconciling two different questions.
    /// </remarks>
    [TestMethod]
    public void BothTemplatesRestrictTheRelationToTheSelectedAct()
    {
        var plan = LuxembourgConsolidationByActDiscoveryPlan.Create();
        var slot = "{" + LuxembourgConsolidationByActDiscoveryPlan.ActSelectionParameterName + ":iri}";
        var restriction =
            "<" + LuxembourgConsolidationByActDiscoveryPlan.ConsolidatesPredicateIri + "> " + slot;

        foreach (var (name, template) in new[]
        {
            ("count", plan.CountTemplate),
            ("page", plan.PageTemplate),
        })
        {
            Assert.AreEqual(
                1,
                template.Split(slot, StringSplitOptions.None).Length - 1,
                $"the {name} template must bind the act exactly once.");
            StringAssert.Contains(
                template, restriction,
                $"the {name} template must restrict the consolidation relation itself to the bound "
                + "act - a family name cannot supply that meaning.");
        }
    }

    /// <summary>
    /// The rendered query names the selected act and no other.
    /// </summary>
    /// <remarks>
    /// The templates are what the plan declares; this is what a publisher would actually receive.
    /// Both are asserted because a correct template rendered through a renderer that dropped the
    /// substitution would still pass the template test.
    /// </remarks>
    [TestMethod]
    public void TheRenderedQueryNamesTheSelectedActAndNoOther()
    {
        var plan = LuxembourgConsolidationByActDiscoveryPlan.Create();
        var count = plan.BindCount(Act, LuxembourgQueryPass.Pass1, NewUrn(), NewUrn(), RendererSource());
        var page = plan.BindPage(
            Act, LuxembourgQueryPass.Pass1, null, 0, count.InputArtifact.ArtifactRef,
            NewUrn(), NewUrn(), RendererSource());

        foreach (var bound in new[] { count, page })
        {
            var body = Uri.UnescapeDataString(
                Encoding.UTF8.GetString(bound.Request.CopyRequestBody()));

            StringAssert.Contains(body, "<" + Act + ">", "the selected act reaches the wire.");
            Assert.IsFalse(
                body.Contains(OtherAct, StringComparison.Ordinal),
                "and no other act does.");
            Assert.IsFalse(
                body.Contains(":iri}", StringComparison.Ordinal),
                "no unsubstituted selection slot may reach the wire.");
        }
    }

    /// <summary>
    /// A relative Legilux ELI is refused rather than normalized into the absolute one.
    /// </summary>
    /// <remarks>
    /// <c>OfficialIdentifier.EliMintedBy</c> admits both Legilux shapes, so admitting both here
    /// would give one act TWO family keys and split its evidence in half. Normalizing is the obvious
    /// alternative and is the one the design review forbids — "no normalization or alternate
    /// spelling introduced elsewhere" — so the relative form throws. The absolute form is the
    /// admitted one because it is the only one a SPARQL IRI term can carry.
    /// </remarks>
    [TestMethod]
    public void ARelativeLegiluxEliIsRefusedRatherThanNormalized()
    {
        const string Relative = "eli/etat/leg/loi/2017/03/14/a439/jo";

        // The premise: this really is the same act in the publisher's other admitted spelling, so
        // the refusal below is a choice about spellings rather than about a malformed value.
        Assert.AreEqual(
            PublisherId.LuLegilux,
            OfficialIdentifier.EliMintedBy(Relative),
            "the relative form is a Legilux ELI, which is exactly why refusing it is a decision.");

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(Relative));
    }

    /// <summary>An act outside the state-legislation branch is not an act this family answers about.</summary>
    [TestMethod]
    public void AnIriOutsideTheStateLegislationBranchIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(
                "http://data.legilux.public.lu/eli/etat/adm/amin/2020/01/01/a1/jo"));

        Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(
                "https://example.invalid/eli/etat/leg/loi/2017/03/14/a439/jo"));
    }

    /// <summary>The profile declares the act as its one selection parameter.</summary>
    /// <remarks>
    /// The profile is what a delivery is interpreted under, so a profile that did not name the
    /// selection would let a delivery be reconciled without one. Asserted against the plan's own
    /// constant rather than a literal, so a rename moves both together or fails here.
    /// </remarks>
    [TestMethod]
    public void TheProfileDeclaresTheActAsItsOnlySelectionParameter()
    {
        var profile = LuxembourgConsolidationByActDiscoveryPlan.Create().CreateDeliveryProfile();

        CollectionAssert.AreEqual(
            new[] { LuxembourgConsolidationByActDiscoveryPlan.ActSelectionParameterName },
            profile.SelectionParameterNames.ToArray());
    }

    private static string NewUrn() => "urn:uuid:" + Guid.NewGuid().ToString("D");

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-consolidation-by-act-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:2d7f91a6-3c08-4b52-9e14-8a6017c5df3b",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }
}
