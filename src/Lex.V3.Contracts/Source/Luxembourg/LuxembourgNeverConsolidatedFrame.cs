using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// What this frame can honestly say about one act's consolidation history. Closed at four members.
/// </summary>
/// <remarks>
/// <para>
/// NEVER-CONSOLIDATED IS AN ABSENCE CLAIM, AND THIS REPOSITORY ALREADY KNOWS WHAT ABSENCE COSTS.
/// <see cref="LuxembourgDraftPropertyAbsenceReason.EnumeratedAndNotHeld"/> is documented as "this
/// batch's enumeration was proven whole and delivered no row for this pair", and Decision 64 exists
/// because an empty list and "we never asked" are indistinguishable to a reader. "This act was never
/// consolidated" is the same shape of claim about a whole population, so it carries the same cost:
/// only a proven-whole enumeration that delivered nothing can support it.
/// </para>
/// <para>
/// The member that does the work here is <see cref="EnumerationUnproven"/>. Without it, an act nobody
/// had checked would be indistinguishable from an act checked and found never consolidated - and the
/// second is a publishable legal claim while the first is a gap in our own coverage.
/// </para>
/// </remarks>
public enum LuxembourgNeverConsolidatedDisposition
{
    /// <summary>A proven-whole consolidation enumeration for this act delivered nothing.</summary>
    [JsonStringEnumMemberName("enumerated_and_never_consolidated")]
    EnumeratedAndNeverConsolidated = 1,

    /// <summary>A proven-whole enumeration delivered at least one consolidation.</summary>
    [JsonStringEnumMemberName("enumerated_and_consolidated")]
    EnumeratedAndConsolidated = 2,

    /// <summary>
    /// No completed enumeration exists for this act. Supports neither claim, and is the reason a
    /// population count can be refused rather than estimated.
    /// </summary>
    [JsonStringEnumMemberName("enumeration_unproven")]
    EnumerationUnproven = 3,

    /// <summary>
    /// The act is outside the class manifest this frame covers. A deliberate exclusion by rule, not
    /// a gap in coverage, and counted as neither.
    /// </summary>
    [JsonStringEnumMemberName("outside_class_manifest")]
    OutsideClassManifest = 4,
}

/// <summary>Why a population count was refused rather than produced. Closed.</summary>
public enum LuxembourgNeverConsolidatedCountRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// At least one act in the manifest has no completed enumeration, so the population is unknown
    /// rather than small.
    /// </summary>
    [JsonStringEnumMemberName("enumeration_incomplete")]
    EnumerationIncomplete = 1,

    /// <summary>
    /// At least one act in this frame's manifest has no disposition at all, so the sweep is
    /// incomplete in a way no entry in the frame can show.
    /// </summary>
    /// <remarks>
    /// This replaced <c>frame_empty</c>, which reported that the frame held nothing. That member
    /// existed only because the frame had no manifest to be measured against: with one, a manifest
    /// requires at least one member, so an empty frame is simply the case where every member is
    /// undispositioned and this name says so more precisely. A refusal that could only ever fire
    /// when there was nothing to compare against was not carrying its weight.
    /// </remarks>
    [JsonStringEnumMemberName("manifest_member_not_dispositioned")]
    ManifestMemberNotDispositioned = 2,
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
/// One act's place in the never-consolidated frame, bound to the evidence that put it there.
/// </summary>
/// <remarks>
/// The completion evidence is required for the two enumerated dispositions and forbidden for the two
/// that are not enumeration outcomes. That asymmetry is the contract: a claim that an enumeration
/// completed must name the evidence that it did, and a claim that nobody enumerated must not carry
/// evidence suggesting somebody had.
/// </remarks>
public sealed record LuxembourgNeverConsolidatedEntry
{
    public LuxembourgNeverConsolidatedEntry(
        string publisherActIri,
        LuxembourgActClassRef actClass,
        LuxembourgNeverConsolidatedDisposition disposition,
        SourceArtifactRef? enumerationCompletionEvidence)
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
            disposition is LuxembourgNeverConsolidatedDisposition.EnumeratedAndNeverConsolidated
                or LuxembourgNeverConsolidatedDisposition.EnumeratedAndConsolidated;
        if (enumerated != (enumerationCompletionEvidence is not null))
        {
            throw new ArgumentException(
                enumerated
                    ? "An enumerated disposition must name the evidence that the enumeration completed."
                    : "A disposition that is not an enumeration outcome must carry no completion evidence.",
                nameof(enumerationCompletionEvidence));
        }

        EnumerationCompletionEvidence = enumerationCompletionEvidence;
    }

    /// <summary>The act, as the publisher identifies it.</summary>
    public string PublisherActIri { get; }

    /// <summary>The publisher's class for this act.</summary>
    public LuxembourgActClassRef ActClass { get; }

    /// <summary>What this frame can say about the act.</summary>
    public LuxembourgNeverConsolidatedDisposition Disposition { get; }

    /// <summary>
    /// The evidence that the consolidation enumeration completed, present exactly for the two
    /// enumerated dispositions.
    /// </summary>
    public SourceArtifactRef? EnumerationCompletionEvidence { get; }
}

/// <summary>Why a class manifest could not be built. Closed.</summary>
public enum LuxembourgActClassManifestRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>No class was admitted, so the manifest admits nothing and scopes nothing.</summary>
    [JsonStringEnumMemberName("no_admitted_class")]
    NoAdmittedClass = 1,

    /// <summary>No member act, so there is no population for anything to be counted over.</summary>
    [JsonStringEnumMemberName("no_member")]
    NoMember = 2,

    /// <summary>A member's class is not one this manifest admits, so the two contradict.</summary>
    [JsonStringEnumMemberName("member_class_not_admitted")]
    MemberClassNotAdmitted = 3,

    /// <summary>One act appears twice, so the manifest states two classes for it.</summary>
    [JsonStringEnumMemberName("duplicate_member")]
    DuplicateMember = 4,

    /// <summary>One class appears twice in the admitted set.</summary>
    [JsonStringEnumMemberName("duplicate_admitted_class")]
    DuplicateAdmittedClass = 5,
}

/// <summary>One act in the manifest, with the class the publisher gave it.</summary>
public sealed record LuxembourgActClassManifestMember
{
    public LuxembourgActClassManifestMember(string publisherActIri, LuxembourgActClassRef actClass)
    {
        PublisherActIri = SourceCoreValidation.RequirePublisherUri(
            publisherActIri, nameof(publisherActIri));
        ActClass = actClass ?? throw new ArgumentNullException(nameof(actClass));
    }

    /// <summary>The act, as the publisher identifies it.</summary>
    public string PublisherActIri { get; }

    /// <summary>The publisher's class for this act, as the manifest's own enumeration found it.</summary>
    public LuxembourgActClassRef ActClass { get; }
}

/// <summary>
/// The maximum-scope class manifest: which classes are in scope, which acts they contain, and the
/// evidence that this enumeration of them completed.
/// </summary>
/// <remarks>
/// <para>
/// WITHOUT THIS, "EVERY ENTRY HAS A TERMINAL DISPOSITION" WAS BEING READ AS "THE POPULATION WAS
/// SWEPT". Those are different claims and the frame could not tell them apart: it counted whatever
/// it had been handed, so one caller-minted never-consolidated entry made the population 1 while the
/// rest of the LOI/RGD universe was never supplied. The per-entry completion evidence did not close
/// that either - any structurally valid artifact reference satisfied it, and neither the entry nor
/// the frame bound it to an enumeration of that act or of the scope.
/// </para>
/// <para>
/// So the population is a property of THIS manifest, and a count is refused until every one of its
/// members has been dispositioned. The frame knows nothing about live acquisition; it knows the
/// exact member set it is answerable for.
/// </para>
/// <para>
/// MEMBERSHIP DECIDES INSIDE AND OUTSIDE, NOT A CALLER'S CHOICE OF DISPOSITION. Before this,
/// <c>OutsideClassManifest</c> was a freely chosen member independent of the act's own class, so a
/// LOI could be labelled outside and dropped from the count while an act of an unrecognised class
/// could be counted as part of the never-consolidated population. The type therefore could not state
/// which population its number described. It can now: the number describes exactly this manifest.
/// </para>
/// <para>
/// The raw class vocabulary stays open - Luxembourg decides what classes exist and E10 asks for the
/// maximum scope, so a class this build has no member for still travels verbatim. What is closed is
/// membership IN ONE COUNT, which is a different thing and must not be open-ended.
/// </para>
/// </remarks>
public sealed class LuxembourgActClassManifest
{
    private readonly ReadOnlyCollection<LuxembourgActClassRef> _admittedClasses;
    private readonly ReadOnlyCollection<LuxembourgActClassManifestMember> _members;
    private readonly Dictionary<string, LuxembourgActClassRef> _classByAct;

    private LuxembourgActClassManifest(
        IList<LuxembourgActClassRef> admittedClasses,
        IList<LuxembourgActClassManifestMember> members,
        Dictionary<string, LuxembourgActClassRef> classByAct,
        SourceArtifactRef completionEvidence)
    {
        _admittedClasses = new ReadOnlyCollection<LuxembourgActClassRef>(admittedClasses);
        _members = new ReadOnlyCollection<LuxembourgActClassManifestMember>(members);
        _classByAct = classByAct;
        CompletionEvidence = completionEvidence;
    }

    /// <summary>The classes this manifest covers, in the order supplied.</summary>
    public IReadOnlyList<LuxembourgActClassRef> AdmittedClasses => _admittedClasses;

    /// <summary>Every act in scope, in the order supplied.</summary>
    public IReadOnlyList<LuxembourgActClassManifestMember> Members => _members;

    /// <summary>The evidence that the enumeration establishing this member set completed.</summary>
    public SourceArtifactRef CompletionEvidence { get; }

    /// <summary>The class this manifest holds for one act, or nothing when it holds no such act.</summary>
    public bool TryGetMemberClass(string publisherActIri, out LuxembourgActClassRef actClass)
    {
        ArgumentNullException.ThrowIfNull(publisherActIri);
        return _classByAct.TryGetValue(publisherActIri, out actClass!);
    }

    /// <summary>Builds a manifest, or refuses by name. Never builds a partial one.</summary>
    public static LuxembourgActClassManifest? TryCreate(
        IReadOnlyList<LuxembourgActClassRef> admittedClasses,
        IReadOnlyList<LuxembourgActClassManifestMember> members,
        SourceArtifactRef completionEvidence,
        out LuxembourgActClassManifestRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(admittedClasses);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(completionEvidence);

        var admitted = admittedClasses.ToArray();
        var memberList = members.ToArray();
        if (admitted.Length == 0)
        {
            refusal = LuxembourgActClassManifestRefusal.NoAdmittedClass;
            return null;
        }

        if (memberList.Length == 0)
        {
            refusal = LuxembourgActClassManifestRefusal.NoMember;
            return null;
        }

        var admittedByIri = new HashSet<string>(StringComparer.Ordinal);
        foreach (var actClass in admitted)
        {
            ArgumentNullException.ThrowIfNull(actClass);
            if (!admittedByIri.Add(actClass.PublisherClassIri))
            {
                refusal = LuxembourgActClassManifestRefusal.DuplicateAdmittedClass;
                return null;
            }
        }

        var classByAct = new Dictionary<string, LuxembourgActClassRef>(StringComparer.Ordinal);
        foreach (var member in memberList)
        {
            ArgumentNullException.ThrowIfNull(member);
            if (!admittedByIri.Contains(member.ActClass.PublisherClassIri))
            {
                refusal = LuxembourgActClassManifestRefusal.MemberClassNotAdmitted;
                return null;
            }

            if (!classByAct.TryAdd(member.PublisherActIri, member.ActClass))
            {
                refusal = LuxembourgActClassManifestRefusal.DuplicateMember;
                return null;
            }
        }

        refusal = LuxembourgActClassManifestRefusal.None;
        return new LuxembourgActClassManifest(admitted, memberList, classByAct, completionEvidence);
    }
}

/// <summary>Why one act's disposition was not admitted to a frame. Closed.</summary>
/// <remarks>
/// A single boolean could not carry this. Before, <c>TryAdmit</c> reported only "disagreed", which
/// conflated two answers about one act with the two ways an entry can contradict the manifest it is
/// being admitted against - and it compared only the disposition, so re-presenting the same act with
/// its class changed returned success and silently kept the old class. Since class decides
/// membership, that was a material contradictory fact reported as an idempotent replay.
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

    /// <summary>This act is already held citing different completion evidence.</summary>
    [JsonStringEnumMemberName("completion_evidence_disagrees")]
    CompletionEvidenceDisagrees = 3,

    /// <summary>The manifest gives this act a different class than the entry states.</summary>
    [JsonStringEnumMemberName("act_class_contradicts_manifest")]
    ActClassContradictsManifest = 4,

    /// <summary>An act the manifest holds cannot be dispositioned as outside it.</summary>
    [JsonStringEnumMemberName("member_dispositioned_outside")]
    MemberDispositionedOutside = 5,

    /// <summary>An act the manifest does not hold can only be dispositioned as outside it.</summary>
    [JsonStringEnumMemberName("non_member_dispositioned_inside")]
    NonMemberDispositionedInside = 6,
}

/// <summary>
/// The never-consolidated frame: every act considered, what was found, and whether a population can
/// honestly be counted yet.
/// </summary>
/// <remarks>
/// <para>
/// THE COUNTING RULE IS THE POINT OF THIS TYPE. E10 says to treat the 23,370 measurement as audit
/// context and never as a literal acceptance value. This makes that structural rather than advisory:
/// <see cref="TryCountNeverConsolidated"/> refuses while any act in the frame has no completed
/// enumeration. A population is not the number of acts we happened to confirm; it is a claim about
/// every act, and it cannot be made while some were never checked.
/// </para>
/// <para>
/// So a frame that has confirmed twenty thousand acts and never checked one more reports no count at
/// all, by name. That is the intended behaviour and not a limitation to be worked around: a number
/// published from a partial sweep would be indistinguishable, once written down, from one that swept
/// everything.
/// </para>
/// <para>
/// APPEND-ONLY, AND ONE DISPOSITION PER ACT. Re-presenting an act with the same disposition is
/// idempotent; re-presenting it with a different one refuses, because two answers about one act are
/// a disagreement to resolve upstream rather than something a set can average.
/// </para>
/// </remarks>
public sealed class LuxembourgNeverConsolidatedFrame
{
    private readonly List<LuxembourgNeverConsolidatedEntry> _entries = [];
    private readonly Dictionary<string, LuxembourgNeverConsolidatedEntry> _byAct =
        new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<LuxembourgNeverConsolidatedEntry> _exposed;

    /// <param name="manifest">
    /// The exact population this frame answers for. Required, and the reason a count from this frame
    /// is a claim about a scope rather than a tally of whatever arrived.
    /// </param>
    public LuxembourgNeverConsolidatedFrame(LuxembourgActClassManifest manifest)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _exposed = _entries.AsReadOnly();
    }

    /// <summary>The population this frame's count describes.</summary>
    public LuxembourgActClassManifest Manifest { get; }

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

        // INSIDE AND OUTSIDE ARE DERIVED FROM THE MANIFEST, NEVER CHOSEN BY THE CALLER. An act the
        // manifest holds is in this population and its class is whatever the manifest's own
        // enumeration found; an act the manifest does not hold is outside it and can be recorded as
        // nothing else. Before this, both directions were a free choice, so the frame could not say
        // which population its number described.
        var isMember = Manifest.TryGetMemberClass(entry.PublisherActIri, out var manifestClass);
        var claimsOutside =
            entry.Disposition == LuxembourgNeverConsolidatedDisposition.OutsideClassManifest;
        if (isMember && claimsOutside)
        {
            refusal = LuxembourgNeverConsolidatedAdmitRefusal.MemberDispositionedOutside;
            return false;
        }

        if (!isMember && !claimsOutside)
        {
            refusal = LuxembourgNeverConsolidatedAdmitRefusal.NonMemberDispositionedInside;
            return false;
        }

        if (isMember && manifestClass != entry.ActClass)
        {
            refusal = LuxembourgNeverConsolidatedAdmitRefusal.ActClassContradictsManifest;
            return false;
        }

        if (_byAct.TryGetValue(entry.PublisherActIri, out var held))
        {
            // EVERY ADMITTED FIELD, NOT THE DISPOSITION ALONE. Comparing only the disposition made
            // "the same act, reclassified" an idempotent replay that silently kept the old class -
            // and since class decides membership, a changed class is a material contradictory fact,
            // not a repeat. The completion evidence is compared for the same reason: two runs citing
            // different evidence for one act are two claims, and this set does not choose between
            // them.
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

            if (held.EnumerationCompletionEvidence != entry.EnumerationCompletionEvidence)
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
    /// The never-consolidated population of this frame's manifest, or a refusal naming why no count
    /// can be made.
    /// </summary>
    /// <remarks>
    /// The count is over MANIFEST MEMBERS, not over admitted entries. An act outside the manifest is
    /// held so the frame records that it was considered, and contributes to nothing.
    /// </remarks>
    public bool TryCountNeverConsolidated(
        out int count, out LuxembourgNeverConsolidatedCountRefusal refusal)
    {
        count = 0;

        // EVERY MEMBER, NOT EVERY ENTRY. "All entries in this list have a terminal disposition" was
        // being read as "the complete population was swept"; those are different claims, and only
        // this one is about the scope the number names.
        foreach (var member in Manifest.Members)
        {
            if (!_byAct.ContainsKey(member.PublisherActIri))
            {
                refusal = LuxembourgNeverConsolidatedCountRefusal.ManifestMemberNotDispositioned;
                return false;
            }
        }

        if (_entries.Any(static entry =>
            entry.Disposition == LuxembourgNeverConsolidatedDisposition.EnumerationUnproven))
        {
            refusal = LuxembourgNeverConsolidatedCountRefusal.EnumerationIncomplete;
            return false;
        }

        count = Manifest.Members.Count(member =>
            _byAct[member.PublisherActIri].Disposition
                == LuxembourgNeverConsolidatedDisposition.EnumeratedAndNeverConsolidated);
        refusal = LuxembourgNeverConsolidatedCountRefusal.None;
        return true;
    }
}
