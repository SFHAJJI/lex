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

    /// <summary>The frame holds no act at all, so there is no population to report.</summary>
    [JsonStringEnumMemberName("frame_empty")]
    FrameEmpty = 2,
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
        PublisherClassIri = ContractValidation.RequireIdentifier(
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
        PublisherActIri = ContractValidation.RequireIdentifier(
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

    public LuxembourgNeverConsolidatedFrame() => _exposed = _entries.AsReadOnly();

    /// <summary>Every act considered, in the order it was admitted.</summary>
    public IReadOnlyList<LuxembourgNeverConsolidatedEntry> Entries => _exposed;

    /// <summary>
    /// Admits one act's disposition, or refuses. Never replaces an admitted disposition.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the frame holds this act with this disposition afterwards,
    /// including when it already did. <see langword="false"/> only on a genuine disagreement.
    /// </returns>
    public bool TryAdmit(LuxembourgNeverConsolidatedEntry entry, out bool disagreed)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_byAct.TryGetValue(entry.PublisherActIri, out var held))
        {
            disagreed = held.Disposition != entry.Disposition;
            return !disagreed;
        }

        _byAct.Add(entry.PublisherActIri, entry);
        _entries.Add(entry);
        disagreed = false;
        return true;
    }

    /// <summary>
    /// The never-consolidated population, or a refusal naming why no count can be made.
    /// </summary>
    public bool TryCountNeverConsolidated(
        out int count, out LuxembourgNeverConsolidatedCountRefusal refusal)
    {
        count = 0;

        if (_entries.Count == 0)
        {
            refusal = LuxembourgNeverConsolidatedCountRefusal.FrameEmpty;
            return false;
        }

        if (_entries.Any(static entry =>
            entry.Disposition == LuxembourgNeverConsolidatedDisposition.EnumerationUnproven))
        {
            refusal = LuxembourgNeverConsolidatedCountRefusal.EnumerationIncomplete;
            return false;
        }

        count = _entries.Count(static entry =>
            entry.Disposition == LuxembourgNeverConsolidatedDisposition.EnumeratedAndNeverConsolidated);
        refusal = LuxembourgNeverConsolidatedCountRefusal.None;
        return true;
    }
}
