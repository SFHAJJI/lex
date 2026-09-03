using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Absence;

/// <summary>Why a family enumeration proof was refused. Closed.</summary>
public enum AbsenceFamilyEnumerationProofRefusal
{
    /// <summary>No refusal: the proof was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The family key is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("family_key_invalid")]
    FamilyKeyInvalid = 1,

    /// <summary>
    /// The delivery proves a different partition. A proof of some other family's enumeration says
    /// nothing about this one, and pairing them would be the whole defect restated.
    /// </summary>
    [JsonStringEnumMemberName("partition_is_not_this_family")]
    PartitionIsNotThisFamily = 2,

    /// <summary>
    /// The two enumeration passes did not deliver the same selection, so no set of rows is the
    /// enumeration of this family.
    /// </summary>
    [JsonStringEnumMemberName("passes_delivered_different_selections")]
    PassesDeliveredDifferentSelections = 3,

    /// <summary>
    /// The selection reached the endpoint's maximum deliverable row count, so agreement between
    /// the passes is agreement about a truncation rather than about a whole enumeration.
    /// </summary>
    [JsonStringEnumMemberName("selection_reached_the_row_cap")]
    SelectionReachedTheRowCap = 4,
}

/// <summary>
/// Evidence that one family's enumeration was delivered whole inside one acquisition run.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because <see cref="AbsenceRunCompletion.EnumerationComplete"/> used to be a
/// value a caller declared. R3.3 makes advancement conditional on a cut that proves complete
/// enumeration, and a declared completeness is the claim this project keeps catching: an
/// incomplete enumeration could advance an absence history and nothing would report it. There is
/// no boolean here and no setter. The only input is an
/// <see cref="EnumerationDeliveryComparison"/>, which cannot itself be minted without resolving
/// and verifying the retained query plans, request artifacts, render receipts, logical requests
/// and routed HTTP evidence of every count and every page of two passes.
/// </para>
/// <para>
/// What the delivery comparison establishes, and therefore what this proof carries:
/// </para>
/// <list type="bullet">
/// <item>
/// Two passes over the same partition, in the same acquisition run, under the same official
/// source profile and the same selection parameters, using <em>different page limits</em>, agreed
/// exactly: the same row digest, the same canonical-key digest and the same cursor digest.
/// Different page limits matter, because it is pagination that loses rows, and two runs of the
/// same pagination would lose the same rows and agree.
/// </item>
/// <item>
/// Each pass delivered exactly as many rows as its own count query selected, over a page chain
/// whose cursors strictly increase, whose canonical keys are unique, and whose last page
/// satisfies the interpretation profile's terminal-page policy. A chain that stopped early is not
/// a chain that terminated.
/// </item>
/// <item>
/// The selection was strictly below the endpoint's maximum deliverable row count. This is a
/// separate condition and not implied by the first: an endpoint that silently truncates at an
/// exact row cap truncates both passes identically, so both counts and both deliveries agree and
/// the comparison is <see cref="EnumerationDeliveryOutcome.EqualSelections"/> over a truncated
/// selection. Both publisher endpoints in this project do exactly that.
/// </item>
/// </list>
/// <para>
/// What it does not establish, stated here rather than left for a reader to assume. It says
/// nothing about which families make up an applicable set: that is the scope manifest's question,
/// and no artifact reachable from this comparison answers it. It does not bind the delivered rows
/// to a cut's observed keys, because the comparison retains canonical keys only as a digest over
/// RDF term tuples under a canonicalization private to <c>Source.Core</c>, which no list of
/// canonical publisher URIs can be checked against from here. And it does not bind the family
/// observation's declared timestamp to the HTTP request instants of the proven enumeration, which
/// are stated on a different clock with no reconciliation policy this contract can cite.
/// </para>
/// <para>
/// The family key must be the delivery's partition member key exactly. The alternative, a
/// caller-supplied map from family key to partition key, was rejected: that map is a declared
/// value of precisely the kind this type exists to remove, and a wrong entry would attach a real
/// proof to the wrong family. The cost is that a cut's family keys are now machine partition
/// member keys, which are a strict subset of bounded identifiers.
/// </para>
/// </remarks>
public sealed class AbsenceFamilyEnumerationProof
{
    private AbsenceFamilyEnumerationProof(
        string familyKey,
        SourceArtifactRef acquisitionRunRef,
        SourceArtifactRef interpretationProfileRef,
        SourceArtifactRef sourceProfileRef,
        long deliveredRowCount,
        string canonicalKeyDigest)
    {
        FamilyKey = familyKey;
        AcquisitionRunRef = acquisitionRunRef;
        InterpretationProfileRef = interpretationProfileRef;
        SourceProfileRef = sourceProfileRef;
        DeliveredRowCount = deliveredRowCount;
        CanonicalKeyDigest = canonicalKeyDigest;
    }

    /// <summary>The family whose enumeration this proves. The delivery's partition member key.</summary>
    public string FamilyKey { get; }

    /// <summary>
    /// The one acquisition run every count and every page of both passes belongs to. A cut is one
    /// run, so this is what its proofs must agree on.
    /// </summary>
    public SourceArtifactRef AcquisitionRunRef { get; }

    /// <summary>The interpretation profile the delivery was read under. Evidence.</summary>
    public SourceArtifactRef InterpretationProfileRef { get; }

    /// <summary>The official publisher source profile every request derived. Evidence.</summary>
    public SourceArtifactRef SourceProfileRef { get; }

    /// <summary>
    /// The number of rows both passes delivered, which is also the number each pass's own count
    /// query selected. Equal across the passes by the admission rule.
    /// </summary>
    public long DeliveredRowCount { get; }

    /// <summary>
    /// The canonical-key digest both passes produced. Retained as evidence, never compared against
    /// a cut's observed keys: see the type remarks for why that comparison is not available here.
    /// </summary>
    public string CanonicalKeyDigest { get; }

    /// <summary>
    /// The only path that mints a family enumeration proof. Returns null with a typed refusal,
    /// because a delivery that does not demonstrate a whole enumeration is a reviewable input
    /// rather than a programming error.
    /// </summary>
    public static AbsenceFamilyEnumerationProof? TryCreate(
        string familyKey,
        EnumerationDeliveryComparison delivery,
        out AbsenceFamilyEnumerationProofRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(familyKey);
        ArgumentNullException.ThrowIfNull(delivery);

        if (!AbsenceValidation.IsIdentifier(familyKey))
        {
            refusal = AbsenceFamilyEnumerationProofRefusal.FamilyKeyInvalid;
            return null;
        }

        if (!string.Equals(delivery.PartitionKey, familyKey, StringComparison.Ordinal))
        {
            refusal = AbsenceFamilyEnumerationProofRefusal.PartitionIsNotThisFamily;
            return null;
        }

        if (delivery.Outcome != EnumerationDeliveryOutcome.EqualSelections)
        {
            refusal = AbsenceFamilyEnumerationProofRefusal.PassesDeliveredDifferentSelections;
            return null;
        }

        if (delivery.ThresholdAssessment != RepeatedEnumerationThresholdAssessment.BelowMaximum)
        {
            refusal = AbsenceFamilyEnumerationProofRefusal.SelectionReachedTheRowCap;
            return null;
        }

        refusal = AbsenceFamilyEnumerationProofRefusal.None;
        return new AbsenceFamilyEnumerationProof(
            familyKey,
            delivery.RunIdentity,
            delivery.InterpretationProfileRef,
            delivery.SourceProfileRef,
            delivery.DeliveredRowCountA,
            delivery.CanonicalKeyDigestA);
    }
}
