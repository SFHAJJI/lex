using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// What one record says: which enumeration was cited for an act, and what that enumeration
/// delivered. Closed at three members.
/// </summary>
/// <remarks>
/// <para>
/// THESE MEMBERS NAME THE RECORD, NOT A PROVEN FACT ABOUT THE ACT, AND THE RENAME IS THE WHOLE
/// POINT. They used to read <c>EnumeratedAndNeverConsolidated</c> / <c>EnumeratedAndConsolidated</c>,
/// documented as "a proven-whole consolidation enumeration FOR THIS ACT delivered nothing". Nothing
/// in this build can establish that: an <see cref="AbsenceFamilyEnumerationProof"/> carries a
/// caller-chosen <c>FamilyKey</c> and no partition bounds, so a zero-row proof of an unrelated
/// family satisfies every check here. Recording that limitation in remarks - which an earlier head
/// did - does not unmake a claim the member name is still making. The names now say exactly what is
/// established: an enumeration was cited, and it delivered this many rows.
/// </para>
/// <para>
/// NEVER-CONSOLIDATED IS AN ABSENCE CLAIM, AND THIS REPOSITORY ALREADY KNOWS WHAT ABSENCE COSTS.
/// <see cref="LuxembourgDraftPropertyAbsenceReason.EnumeratedAndNotHeld"/> is documented as "this
/// batch's enumeration was proven whole and delivered no row for this pair", and Decision 64 exists
/// because an empty list and "we never asked" are indistinguishable to a reader. The absence claim
/// E10 wants is exactly that shape, and it is not available until an enumeration's scope can be tied
/// to an act. #419 carries that work.
/// </para>
/// <para>
/// The member that does the work here is <see cref="NoEnumerationCited"/>. Without it, an act nobody
/// had checked would be indistinguishable from an act checked and found nothing - and the two must
/// never merge, whatever the first one is eventually allowed to claim.
/// </para>
/// <para>
/// <c>OutsideClassManifest</c> was retired with the class manifest itself. It named membership of a
/// population this contract no longer claims to know.
/// </para>
/// </remarks>
public enum LuxembourgNeverConsolidatedDisposition
{
    /// <summary>
    /// The enumeration cited for this act delivered no consolidation. This says what that
    /// enumeration returned; it does not establish that the enumeration was scoped to this act.
    /// </summary>
    [JsonStringEnumMemberName("cited_enumeration_delivered_no_consolidation")]
    CitedEnumerationDeliveredNoConsolidation = 1,

    /// <summary>
    /// The enumeration cited for this act delivered at least one consolidation. Same limit as
    /// above: it says what came back, not what it was asked about.
    /// </summary>
    [JsonStringEnumMemberName("cited_enumeration_delivered_consolidations")]
    CitedEnumerationDeliveredConsolidations = 2,

    /// <summary>
    /// No enumeration is cited for this act at all. Supports nothing, and is how a reader tells a
    /// gap in our coverage from a finding about the publisher.
    /// </summary>
    [JsonStringEnumMemberName("no_enumeration_cited")]
    NoEnumerationCited = 3,
}

/// <summary>
/// The publisher's own act class, carried exactly as the publisher states it.
/// </summary>
/// <remarks>
/// <para>
/// NOT A CLOSED ENUM, AND THE REASON IS A MISTAKE ALREADY MADE ONCE IN THIS BUILD. E9's language axis
/// was nearly closed at the twenty-four official languages while the publisher emits ninety-four, and
/// a closed enum there would have silently erased the 385 corrigenda that exist in no other language.
/// An act class is the same kind of thing: Luxembourg decides what classes exist, this build does
/// not, and E10's own wording asks for the <em>maximum-scope</em> class manifest. A class this build
/// has no member for must survive being read, so the class travels as the publisher's IRI.
/// </para>
/// <para>
/// LOI and RGD are therefore recognised, never privileged: they are the classes E10 names, but an act
/// of some third class is admitted to the frame with its own class intact rather than refused or
/// folded into the nearest member.
/// </para>
/// </remarks>
public sealed record LuxembourgActClassRef
{
    public LuxembourgActClassRef(string publisherClassIri)
    {
        // AN IRI, NOT AN IDENTIFIER. RequireIdentifier admits any bounded printable ASCII, so
        // "LOI" and "act-1" passed it while the public contract above says this field carries the
        // class IRI exactly as the publisher states it. Those are not near-misses; they are values
        // the publisher never emits, and admitting them makes the verbatim-carriage promise
        // unfalsifiable. RequirePublisherUri is the bound this repository already uses for every
        // other publisher identity: an exact absolute HTTP(S) URI with no userinfo, query or
        // fragment.
        PublisherClassIri = SourceCoreValidation.RequirePublisherUri(
            publisherClassIri, nameof(publisherClassIri));
    }

    /// <summary>The class IRI exactly as the publisher states it. Never normalized or mapped.</summary>
    public string PublisherClassIri { get; }
}

/// <summary>
/// One act's recorded disposition, and the proof of the enumeration the recorder cited for it.
/// </summary>
/// <remarks>
/// <para>
/// The completion proof is required for the two enumerated dispositions and forbidden for the one
/// that is not an enumeration outcome. That asymmetry is the contract: a claim that an enumeration
/// completed must name the proof that it did, and a claim that nobody enumerated must not carry
/// evidence suggesting somebody had.
/// </para>
/// <para>
/// A PROOF, NOT A REFERENCE, AND THE DISPOSITION MUST AGREE WITH IT.
/// <see cref="AbsenceFamilyEnumerationProof"/> can only be minted from an
/// <see cref="EnumerationDeliveryComparison"/> whose two independent passes agreed below the row cap,
/// which is what makes it a proof rather than a claim. And once it is a proof it says how many rows
/// that enumeration delivered, so the disposition becomes a statement the evidence either supports
/// or contradicts:
/// <see cref="LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoConsolidation"/>
/// requires a delivered row count of zero and
/// <see cref="LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredConsolidations"/> at
/// least one. A member that names what the cited enumeration returned, beside a proof that returned
/// something else, is a contradiction inside one argument list and refuses here.
/// </para>
/// <para>
/// WHAT THIS DOES NOT ESTABLISH, AND WHY THE MEMBER NAMES WERE CHANGED RATHER THAN ANNOTATED: that
/// the supplied proof enumerated THIS act. A proof carries <c>FamilyKey</c> - a caller-chosen
/// partition key - and no partition bounds, so nothing in this build can tie an enumeration's scope
/// to an act identity, and a zero-row proof of an unrelated family satisfies every check above. An
/// earlier head said exactly this in remarks while the members still read
/// "enumerated and never consolidated", which is an act-specific legal claim. A limitation written
/// beside a claim does not retract the claim; the names had to stop making it.
/// </para>
/// </remarks>
public sealed record LuxembourgNeverConsolidatedEntry
{
    public LuxembourgNeverConsolidatedEntry(
        string publisherActIri,
        LuxembourgActClassRef actClass,
        LuxembourgNeverConsolidatedDisposition disposition,
        AbsenceFamilyEnumerationProof? enumerationCompletionProof)
    {
        // See LuxembourgActClassRef's own remark: this is a publisher IRI, and RequireIdentifier
        // admitted values the publisher cannot emit. There is no narrower LU-specific canonical form
        // to apply here - LuxembourgFileUri pins the Legilux DATA host for manifestation file URIs,
        // which is a different axis from an act's ELI identity, and nothing in this build canonicalises
        // act IRIs - so the exact absolute publisher URI is the honest bound rather than an invented one.
        PublisherActIri = SourceCoreValidation.RequirePublisherUri(
            publisherActIri, nameof(publisherActIri));
        ActClass = actClass ?? throw new ArgumentNullException(nameof(actClass));
        Disposition = ContractValidation.RequireDefined(disposition, nameof(disposition));

        var enumerated =
            disposition is LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoConsolidation
                or LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredConsolidations;
        if (enumerated != (enumerationCompletionProof is not null))
        {
            throw new ArgumentException(
                enumerated
                    ? "An enumerated disposition must name the proof that the enumeration completed."
                    : "A disposition that is not an enumeration outcome must carry no completion proof.",
                nameof(enumerationCompletionProof));
        }

        // THE PROOF DECIDES WHICH OF THE TWO ENUMERATED DISPOSITIONS THIS IS. A proof that delivered
        // rows is a proof that this act WAS consolidated; a proof that delivered none is the absence
        // claim. A caller that states the opposite of what its own evidence says is not reporting a
        // disagreement between sources - it is contradicting itself in one argument list.
        if (enumerationCompletionProof is { } proof)
        {
            var deliveredNone =
                disposition == LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoConsolidation;
            if (deliveredNone != (proof.DeliveredRowCount == 0))
            {
                throw new ArgumentException(
                    deliveredNone
                        ? "This member says the cited enumeration delivered nothing; its proof delivered rows."
                        : "This member says the cited enumeration delivered rows; its proof delivered none.",
                    nameof(enumerationCompletionProof));
            }
        }

        EnumerationCompletionProof = enumerationCompletionProof;
    }

    /// <summary>The act, as the publisher identifies it.</summary>
    public string PublisherActIri { get; }

    /// <summary>The publisher's class for this act.</summary>
    public LuxembourgActClassRef ActClass { get; }

    /// <summary>What the recorder says about this act.</summary>
    public LuxembourgNeverConsolidatedDisposition Disposition { get; }

    /// <summary>
    /// The proof that the consolidation enumeration completed, present exactly for the two
    /// enumerated dispositions and agreeing with the one that is stated.
    /// </summary>
    public AbsenceFamilyEnumerationProof? EnumerationCompletionProof { get; }
}

/// <summary>Why one act's disposition was not admitted to a frame. Closed.</summary>
/// <remarks>
/// A single boolean could not carry this. An earlier head reported only "disagreed", which conflated
/// two answers about one act with the ways an entry can contradict what is already held - and it
/// compared only the disposition, so re-presenting the same act with its class changed returned
/// success and silently kept the old class.
/// </remarks>
public enum LuxembourgNeverConsolidatedAdmitRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>This act is already held with a different disposition.</summary>
    [JsonStringEnumMemberName("disposition_disagrees")]
    DispositionDisagrees = 1,

    /// <summary>This act is already held with a different class.</summary>
    [JsonStringEnumMemberName("act_class_disagrees")]
    ActClassDisagrees = 2,

    /// <summary>This act is already held citing a different enumeration.</summary>
    [JsonStringEnumMemberName("completion_evidence_disagrees")]
    CompletionEvidenceDisagrees = 3,
}

/// <summary>
/// An append-only record of what was found about each act's consolidation history. It counts no
/// population and claims no scope.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE USED TO PRODUCE A POPULATION COUNT, AND THE COUNT WAS NOT EVIDENCE-BOUND. It carried a
/// class manifest and refused to count until every manifest member had a terminal disposition, which
/// reads like a proven-whole sweep and was not one: the manifest's members were caller-supplied and
/// bound to a proof only by CARDINALITY, so any proven delivery of N unrelated rows authorised any N
/// caller-chosen act identities. Matching counts are tamper evidence; they do not turn unrelated
/// identities into the enumerated population. The manifest and the count are both removed rather
/// than relocated, because moving the same substitution behind another wrapper would leave the same
/// claim standing with a longer path to it.
/// </para>
/// <para>
/// WHAT IT IS NOW, STATED SO NOBODY HAS TO INFER IT. This is a recorder. It holds one disposition per
/// act, refuses two different answers for one act, and requires each enumerated disposition to agree
/// with the row count of the proof cited for it. Those are real properties and they are all it has.
/// It does not know which acts exist, does not know whether the set it holds is complete, and
/// produces no number that could be read as a population.
/// </para>
/// <para>
/// WHAT WOULD MAKE A COUNT HONEST, recorded here because the next slice has to build it rather than
/// rediscover it: an exact LOI/RGD class enumeration and a per-act consolidation enumeration, with
/// the members DECODED from the retained proof-bound rows instead of accepted alongside them - the
/// shape <c>Lex.V3.Contracts.Source.Europe.EuLanguageScopedExpressionDecode</c> already uses on the
/// EU side. That also needs something this build does not have: a way for a proof to carry the scope
/// it enumerated. <see cref="AbsenceFamilyEnumerationProof"/> exposes a caller-chosen
/// <c>FamilyKey</c> and no partition bounds, so today an enumeration's scope cannot be tied to an act
/// identity at all.
/// </para>
/// <para>
/// APPEND-ONLY, AND ONE DISPOSITION PER ACT. Re-presenting an act with the same entry is idempotent;
/// re-presenting it with a different disposition, class or enumeration refuses, because two answers
/// about one act are a disagreement to resolve upstream rather than something a set can average.
/// </para>
/// </remarks>
public sealed class LuxembourgNeverConsolidatedFrame
{
    private readonly List<LuxembourgNeverConsolidatedEntry> _entries = [];
    private readonly Dictionary<string, LuxembourgNeverConsolidatedEntry> _byAct =
        new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<LuxembourgNeverConsolidatedEntry> _exposed;

    public LuxembourgNeverConsolidatedFrame() => _exposed = _entries.AsReadOnly();

    /// <summary>Every act considered, in the order it was admitted.</summary>
    public IReadOnlyList<LuxembourgNeverConsolidatedEntry> Entries => _exposed;

    /// <summary>
    /// Admits one act's disposition, or refuses by name. Never replaces an admitted disposition.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the frame holds this act with this entry afterwards, including
    /// when it already did. <see langword="false"/> only on a named refusal.
    /// </returns>
    public bool TryAdmit(
        LuxembourgNeverConsolidatedEntry entry, out LuxembourgNeverConsolidatedAdmitRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_byAct.TryGetValue(entry.PublisherActIri, out var held))
        {
            // EVERY ADMITTED FIELD, NOT THE DISPOSITION ALONE. Comparing only the disposition made
            // "the same act, reclassified" an idempotent replay that silently kept the old class.
            // The enumeration is compared for the same reason: two runs citing different
            // enumerations for one act are two claims, and this set does not choose between them.
            if (held.ActClass != entry.ActClass)
            {
                refusal = LuxembourgNeverConsolidatedAdmitRefusal.ActClassDisagrees;
                return false;
            }

            if (held.Disposition != entry.Disposition)
            {
                refusal = LuxembourgNeverConsolidatedAdmitRefusal.DispositionDisagrees;
                return false;
            }

            if (!SameEnumeration(held.EnumerationCompletionProof, entry.EnumerationCompletionProof))
            {
                refusal = LuxembourgNeverConsolidatedAdmitRefusal.CompletionEvidenceDisagrees;
                return false;
            }

            refusal = LuxembourgNeverConsolidatedAdmitRefusal.None;
            return true;
        }

        _byAct.Add(entry.PublisherActIri, entry);
        _entries.Add(entry);
        refusal = LuxembourgNeverConsolidatedAdmitRefusal.None;
        return true;
    }

    /// <summary>
    /// Whether two completion proofs are the same claim about the same enumeration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT REFERENCE EQUALITY, AND THE DIFFERENCE IS NOT ACADEMIC.
    /// <see cref="AbsenceFamilyEnumerationProof"/> is a class with no value equality, so two proofs
    /// minted from one enumeration by two callers are different objects. Comparing the objects would
    /// make re-presenting an act with its own evidence a DISAGREEMENT, which turns an idempotent
    /// replay into a refusal for no reason a reader could defend.
    /// </para>
    /// <para>
    /// EVERY FIELD OF THE CLAIM, NOT A CHOSEN FOUR. An earlier head compared family, run, row count
    /// and key digest, and omitted <see cref="AbsenceFamilyEnumerationProof.RetainedFloor"/> and the
    /// two profile references. Review minted two proofs from one delivery differing only in retention
    /// class and the weaker one replayed as identical, silently keeping the stronger. Retention class
    /// is part of what a proof asserts - <c>Floored</c> and <c>RetainedUnenforced</c> are different
    /// custody guarantees - so two proofs differing in it are two claims. The rule is now the whole
    /// public surface of the proof, and
    /// <c>LuxembourgNeverConsolidatedFrameTests.EveryProofFieldParticipatesInTheClaimComparison</c>
    /// fails if that surface grows a field this comparison does not read.
    /// </para>
    /// </remarks>
    private static bool SameEnumeration(
        AbsenceFamilyEnumerationProof? left, AbsenceFamilyEnumerationProof? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && string.Equals(left.FamilyKey, right.FamilyKey, StringComparison.Ordinal)
            && left.AcquisitionRunRef == right.AcquisitionRunRef
            && left.InterpretationProfileRef == right.InterpretationProfileRef
            && left.SourceProfileRef == right.SourceProfileRef
            && left.DeliveredRowCount == right.DeliveredRowCount
            && string.Equals(left.CanonicalKeyDigest, right.CanonicalKeyDigest, StringComparison.Ordinal)
            && left.RetainedFloor == right.RetainedFloor;
    }
}
