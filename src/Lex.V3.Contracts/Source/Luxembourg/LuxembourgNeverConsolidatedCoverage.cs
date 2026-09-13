using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// Where one act the frame holds sits in E10's count. Closed.
/// </summary>
/// <remarks>
/// The first three members are the population: an act whose class the manifest counts (LOI or
/// RGD), bucketed by its disposition. The fourth is not in the population and is named on purpose:
/// an act the build recognizes and the owner's ruling excludes is listed with its class rather
/// than dropped, so a reader can see what the count left out and why.
/// </remarks>
public enum LuxembourgNeverConsolidatedMembership
{
    /// <summary>In the population; the cited enumeration delivered no consolidation rows.</summary>
    [JsonStringEnumMemberName("never_consolidated")]
    NeverConsolidated = 1,

    /// <summary>
    /// In the population; the cited enumeration delivered rows. Still E10's: its as-published
    /// original is wanted even though a consolidated edition exists.
    /// </summary>
    [JsonStringEnumMemberName("consolidated")]
    Consolidated = 2,

    /// <summary>In the population; no enumeration was cited, so its disposition is unproven.</summary>
    [JsonStringEnumMemberName("enumeration_not_cited")]
    EnumerationNotCited = 3,

    /// <summary>Not in the population: a recognized class the ruling names out of scope.</summary>
    [JsonStringEnumMemberName("recognized_out_of_scope")]
    RecognizedOutOfScope = 4,
}

/// <summary>Why one population act is unresolved rather than counted. Closed.</summary>
public enum LuxembourgNeverConsolidatedGapReason
{
    /// <summary>
    /// The frame holds the act with no enumeration cited. Nobody checked; the act is in the
    /// population and in neither counted bucket until an enumeration is.
    /// </summary>
    [JsonStringEnumMemberName("enumeration_not_cited")]
    EnumerationNotCited = 1,
}

/// <summary>Why a frame could not be folded into a coverage at all. Closed.</summary>
public enum LuxembourgNeverConsolidatedCoverageRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// An act's class is not in the recognized vocabulary. Not a gap and not an exclusion: the
    /// owner's ruling is that an unknown code fails closed, and a count over a frame holding one is
    /// no count.
    /// </summary>
    [JsonStringEnumMemberName("unrecognized_act_class")]
    UnrecognizedActClass = 1,
}

/// <summary>One held act's placement in the count: its class as the manifest reads it, and its bucket.</summary>
public sealed class LuxembourgNeverConsolidatedPlacement
{
    internal LuxembourgNeverConsolidatedPlacement(
        string publisherActIri,
        string publisherClassIri,
        LuxembourgActClassScope scope,
        LuxembourgNeverConsolidatedMembership membership)
    {
        PublisherActIri = publisherActIri;
        PublisherClassIri = publisherClassIri;
        Scope = scope;
        Membership = membership;
    }

    /// <summary>The act, exactly as the frame holds it.</summary>
    public string PublisherActIri { get; }

    /// <summary>The class IRI exactly as the publisher states it, carried through unchanged.</summary>
    public string PublisherClassIri { get; }

    /// <summary>What the manifest says of that class.</summary>
    public LuxembourgActClassScope Scope { get; }

    public LuxembourgNeverConsolidatedMembership Membership { get; }
}

/// <summary>One population act the count could not settle, with the reason.</summary>
public sealed class LuxembourgNeverConsolidatedUnresolvedGap
{
    internal LuxembourgNeverConsolidatedUnresolvedGap(
        string publisherActIri, LuxembourgActClassScope scope, LuxembourgNeverConsolidatedGapReason reason)
    {
        PublisherActIri = publisherActIri;
        Scope = scope;
        Reason = reason;
    }

    public string PublisherActIri { get; }

    /// <summary>Always a counted scope: only population acts can be gaps.</summary>
    public LuxembourgActClassScope Scope { get; }

    public LuxembourgNeverConsolidatedGapReason Reason { get; }
}

/// <summary>
/// The fold of a <see cref="LuxembourgNeverConsolidatedFrame"/> through
/// <see cref="LuxembourgActClassManifest"/> into E10's counts and typed gaps. #419 slice 5.
/// </summary>
/// <remarks>
/// <para>
/// THE COUNTING RULES. An act's class is what the manifest says of the class IRI the frame holds for
/// it - the publisher's <c>jolux:typeDocument</c> value - and never what its ELI path says; an act
/// whose path reads <c>/loi/</c> and whose class is RGD counts as RGD. Exactly the classes the
/// manifest counts enter the population (LOI and RGD, per the owner's ruling of 2026-09-13); every
/// other recognized class is a named exclusion, listed with its class IRI and counted nowhere. Within
/// the population an act's disposition decides its bucket: a cited enumeration that delivered no rows
/// is never-consolidated; one that delivered rows is consolidated, and still E10's because its
/// as-published original is wanted; no enumeration cited is a typed gap. An act counts once, which
/// the frame guarantees by refusing a second answer about one act.
/// </para>
/// <para>
/// THE GAP RULES. A gap is a population act whose disposition is unproven. It is in
/// <see cref="PopulationSize"/> and in neither counted bucket, so
/// <c>PopulationSize == NeverConsolidatedCount + ConsolidatedCount + UnresolvedGaps.Count</c> always
/// holds, and <see cref="AllHeldActsSettled"/> is true only when there are none. An out-of-scope act
/// is never a gap, whatever its disposition: gaps are about the population. An act whose class the
/// manifest does not recognize is neither a gap nor an exclusion - the whole fold refuses
/// (<see cref="LuxembourgNeverConsolidatedCoverageRefusal.UnrecognizedActClass"/>), because a count
/// that quietly stepped over an unknown code would be the silent scope decision the ruling forbids.
/// </para>
/// <para>
/// WHAT THIS DOES NOT CLAIM. The coverage is exact over the acts the frame holds. Whether the frame
/// holds the publisher's whole current LOI/RGD population is the live-acceptance question, answered
/// by evidence this contract does not carry. The 23,370 measurement is audit context: no expected or
/// acceptance figure lives on this type, and its tests pin that none does.
/// </para>
/// <para>
/// DETERMINISTIC ACROSS ADMISSION ORDER. The frame exposes its entries as they were admitted, and
/// two independent enumerations can deliver one act set in two orders. A coverage that preserved
/// that order in its placements, gaps or refusal would make two executions disagree over identical
/// publisher facts, so every exposed collection is emitted by ordinal <c>PublisherActIri</c> - the
/// one key the frame guarantees unique - and two frames holding one act set render the same
/// <see cref="Describe"/> and the same member sequences whatever order admitted them.
/// </para>
/// </remarks>
public sealed class LuxembourgNeverConsolidatedCoverage
{
    private readonly Dictionary<string, LuxembourgNeverConsolidatedPlacement> _byAct;

    private LuxembourgNeverConsolidatedCoverage(
        List<LuxembourgNeverConsolidatedPlacement> placements,
        List<LuxembourgNeverConsolidatedUnresolvedGap> gaps)
    {
        Placements = new ReadOnlyCollection<LuxembourgNeverConsolidatedPlacement>(placements);
        UnresolvedGaps = new ReadOnlyCollection<LuxembourgNeverConsolidatedUnresolvedGap>(gaps);
        _byAct = placements.ToDictionary(static p => p.PublisherActIri, StringComparer.Ordinal);

        foreach (var placement in placements)
        {
            var loi = placement.Scope == LuxembourgActClassScope.InScopeLoi;
            switch (placement.Membership)
            {
                case LuxembourgNeverConsolidatedMembership.NeverConsolidated:
                    if (loi) NeverConsolidatedLoiCount++; else NeverConsolidatedRgdCount++;
                    break;
                case LuxembourgNeverConsolidatedMembership.Consolidated:
                    ConsolidatedCount++;
                    break;
                case LuxembourgNeverConsolidatedMembership.EnumerationNotCited:
                    break;
                case LuxembourgNeverConsolidatedMembership.RecognizedOutOfScope:
                    NamedExclusionCount++;
                    continue;
                default:
                    throw new InvalidOperationException($"{placement.Membership} is not a membership this fold mints.");
            }

            if (loi) PopulationLoiCount++; else PopulationRgdCount++;
        }
    }

    /// <summary>Every act the frame holds, by ordinal act IRI, including the named exclusions.</summary>
    public IReadOnlyList<LuxembourgNeverConsolidatedPlacement> Placements { get; }

    /// <summary>The population acts whose disposition is unproven, by ordinal act IRI.</summary>
    public IReadOnlyList<LuxembourgNeverConsolidatedUnresolvedGap> UnresolvedGaps { get; }

    /// <summary>Every act the frame holds, population or not.</summary>
    public int HeldActCount => Placements.Count;

    /// <summary>The LOI and RGD acts: counted buckets plus gaps.</summary>
    public int PopulationSize => PopulationLoiCount + PopulationRgdCount;

    public int PopulationLoiCount { get; }

    public int PopulationRgdCount { get; }

    public int NeverConsolidatedCount => NeverConsolidatedLoiCount + NeverConsolidatedRgdCount;

    public int NeverConsolidatedLoiCount { get; }

    public int NeverConsolidatedRgdCount { get; }

    public int ConsolidatedCount { get; }

    /// <summary>Recognized classes the ruling names out of scope. Listed, never counted.</summary>
    public int NamedExclusionCount { get; }

    /// <summary>True only when no population act is a gap. Says nothing about population completeness.</summary>
    public bool AllHeldActsSettled => UnresolvedGaps.Count == 0;

    /// <summary>
    /// Folds a frame, or refuses by name. A frame holding any act whose class the manifest does not
    /// recognize refuses as a whole, naming every such act in ordinal order.
    /// </summary>
    public static LuxembourgNeverConsolidatedCoverage? TryComplete(
        LuxembourgNeverConsolidatedFrame frame,
        out LuxembourgNeverConsolidatedCoverageRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(frame);
        detail = null;

        var placements = new List<LuxembourgNeverConsolidatedPlacement>(frame.Entries.Count);
        var gaps = new List<LuxembourgNeverConsolidatedUnresolvedGap>();
        var unrecognized = new List<string>();
        foreach (var entry in frame.Entries)
        {
            var classIri = entry.ActClass.PublisherClassIri;

            // THE MANIFEST DECIDES, AND AN UNKNOWN CODE STOPS THE FOLD. Not "skip it", not "call it
            // out of scope": the ruling's fail-closed direction, applied where a count would
            // otherwise absorb the unknown silently. Every offender is collected so the refusal
            // names the same set in the same order whichever way the frame was filled.
            if (!LuxembourgActClassManifest.TryClassify(classIri, out var scope))
            {
                unrecognized.Add($"{entry.PublisherActIri} carries class {classIri}");
                continue;
            }

            if (!LuxembourgActClassManifest.IsCounted(scope))
            {
                placements.Add(new LuxembourgNeverConsolidatedPlacement(
                    entry.PublisherActIri, classIri, scope,
                    LuxembourgNeverConsolidatedMembership.RecognizedOutOfScope));
                continue;
            }

            var membership = entry.Disposition switch
            {
                LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows =>
                    LuxembourgNeverConsolidatedMembership.NeverConsolidated,
                LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows =>
                    LuxembourgNeverConsolidatedMembership.Consolidated,
                LuxembourgNeverConsolidatedDisposition.NoEnumerationCited =>
                    LuxembourgNeverConsolidatedMembership.EnumerationNotCited,
                _ => throw new InvalidOperationException(
                    $"{entry.Disposition} is not a disposition the frame admits."),
            };

            if (membership == LuxembourgNeverConsolidatedMembership.EnumerationNotCited)
            {
                gaps.Add(new LuxembourgNeverConsolidatedUnresolvedGap(
                    entry.PublisherActIri, scope, LuxembourgNeverConsolidatedGapReason.EnumerationNotCited));
            }

            placements.Add(new LuxembourgNeverConsolidatedPlacement(
                entry.PublisherActIri, classIri, scope, membership));
        }

        if (unrecognized.Count > 0)
        {
            unrecognized.Sort(StringComparer.Ordinal);
            refusal = LuxembourgNeverConsolidatedCoverageRefusal.UnrecognizedActClass;
            detail = string.Join("; ", unrecognized) + ", which the manifest does not recognize.";
            return null;
        }

        // CANONICAL ORDER, NOT ADMISSION ORDER. See the type remark: ordinal act IRI, for every
        // exposed collection, so two frames holding one act set fold to one coverage.
        placements.Sort(static (a, b) => string.CompareOrdinal(a.PublisherActIri, b.PublisherActIri));
        gaps.Sort(static (a, b) => string.CompareOrdinal(a.PublisherActIri, b.PublisherActIri));

        refusal = LuxembourgNeverConsolidatedCoverageRefusal.None;
        return new LuxembourgNeverConsolidatedCoverage(placements, gaps);
    }

    /// <summary>The placement of one held act, or null when the frame never held it.</summary>
    public LuxembourgNeverConsolidatedPlacement? PlacementFor(string publisherActIri)
    {
        ArgumentNullException.ThrowIfNull(publisherActIri);
        return _byAct.GetValueOrDefault(publisherActIri);
    }

    public string Describe() =>
        $"held={HeldActCount} population={PopulationSize} (loi={PopulationLoiCount} rgd={PopulationRgdCount}) "
        + $"never_consolidated={NeverConsolidatedCount} (loi={NeverConsolidatedLoiCount} rgd={NeverConsolidatedRgdCount}) "
        + $"consolidated={ConsolidatedCount} unresolved_gaps={UnresolvedGaps.Count} "
        + $"named_exclusions={NamedExclusionCount}";
}
