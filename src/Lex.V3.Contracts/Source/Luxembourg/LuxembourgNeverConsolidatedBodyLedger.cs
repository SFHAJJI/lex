using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>Why a population's body ledger could not be assembled at all. Closed.</summary>
public enum LuxembourgNeverConsolidatedBodyLedgerRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>A body set names an act the coverage does not count. Not this population's body.</summary>
    [JsonStringEnumMemberName("set_for_act_not_in_population")]
    SetForActNotInPopulation = 1,

    /// <summary>Two body sets name one act. Two answers about one act are a disagreement, not a merge.</summary>
    [JsonStringEnumMemberName("set_delivered_twice")]
    SetDeliveredTwice = 2,
}

/// <summary>What one population act's Gazette bodies came to. Closed.</summary>
public enum LuxembourgNeverConsolidatedBodyActOutcome
{
    /// <summary>A set was delivered and every listed body carries its typed outcome.</summary>
    [JsonStringEnumMemberName("bodies_disposed")]
    BodiesDisposed = 1,

    /// <summary>A set was delivered and the act has no Gazette-PDF body, for the set's named reason.</summary>
    [JsonStringEnumMemberName("act_gap")]
    ActGap = 2,

    /// <summary>No set was delivered: nobody looked. Distinct from looking and finding nothing.</summary>
    [JsonStringEnumMemberName("body_not_discovered")]
    BodyNotDiscovered = 3,
}

/// <summary>One population act's line in the ledger: its placement, and its bodies or the reason it has none.</summary>
public sealed class LuxembourgNeverConsolidatedBodyEntry
{
    internal LuxembourgNeverConsolidatedBodyEntry(
        LuxembourgNeverConsolidatedPlacement placement,
        LuxembourgGazetteBodySet? set,
        LuxembourgNeverConsolidatedBodyActOutcome outcome)
    {
        Placement = placement;
        Set = set;
        Outcome = outcome;
    }

    /// <summary>The act's placement in the count (slice 5): a population act, never an exclusion.</summary>
    public LuxembourgNeverConsolidatedPlacement Placement { get; }

    /// <summary>The delivered set. Null exactly when nobody looked.</summary>
    public LuxembourgGazetteBodySet? Set { get; }

    public LuxembourgNeverConsolidatedBodyActOutcome Outcome { get; }

    public string PublisherActIri => Placement.PublisherActIri;

    /// <summary>Present exactly when the outcome is an act gap.</summary>
    public LuxembourgGazetteActGapReason? ActGap => Set?.ActGap;
}

/// <summary>
/// Every population act of a slice-5 coverage with exactly one body outcome: its Gazette bodies,
/// each typed; or the typed reason it has none; or the fact that nobody looked. #419 slice 6a.
/// </summary>
/// <remarks>
/// <para>
/// ONE LINE PER POPULATION ACT, NONE FOR AN EXCLUSION. The ledger walks the coverage's counted
/// placements (the manifest's counting boundary decides, as in slice 5) and gives each exactly one
/// outcome. A set delivered for an act the coverage does not count - an exclusion, or an act the
/// frame never held - refuses the whole ledger rather than being dropped, and two sets for one act
/// refuse it rather than being merged, because either is a disagreement to resolve upstream.
/// </para>
/// <para>
/// "NOT DISCOVERED" IS NOT "NONE FOUND". An act with no delivered set is a body nobody looked for,
/// and it stays distinguishable from an act whose set says the publisher lists no Gazette PDF.
/// <see cref="AllPopulationActsDisposed"/> is true only when every act has a set; it says nothing
/// about whether the bodies found were admitted.
/// </para>
/// <para>
/// This is S7-A02's "every discovered body has one typed outcome" and S3-A01's Gazette-PDF
/// admitted / rejected / typed-gap outcomes, over E10's counted population. It claims nothing about
/// whether that population is complete; that remains the live-acceptance question.
/// </para>
/// <para>
/// DETERMINISTIC. Entries follow the coverage's ordinal act order; a refusal names every offender in
/// ordinal order.
/// </para>
/// </remarks>
public sealed class LuxembourgNeverConsolidatedBodyLedger
{
    private readonly Dictionary<string, LuxembourgNeverConsolidatedBodyEntry> _byAct;

    private LuxembourgNeverConsolidatedBodyLedger(List<LuxembourgNeverConsolidatedBodyEntry> entries)
    {
        Entries = new ReadOnlyCollection<LuxembourgNeverConsolidatedBodyEntry>(entries);
        _byAct = entries.ToDictionary(static e => e.PublisherActIri, StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            switch (entry.Outcome)
            {
                case LuxembourgNeverConsolidatedBodyActOutcome.BodiesDisposed:
                    ActsWithBodiesCount++;
                    AdmittedBodyCount += entry.Set!.AdmittedCount;
                    RejectedBodyCount += entry.Set.RejectedCount;
                    GapBodyCount += entry.Set.GapCount;
                    break;
                case LuxembourgNeverConsolidatedBodyActOutcome.ActGap:
                    ActGapCount++;
                    break;
                case LuxembourgNeverConsolidatedBodyActOutcome.BodyNotDiscovered:
                    NotDiscoveredCount++;
                    break;
                default:
                    throw new InvalidOperationException($"{entry.Outcome} is not an outcome this ledger mints.");
            }
        }
    }

    /// <summary>One line per population act, in the coverage's ordinal act order.</summary>
    public IReadOnlyList<LuxembourgNeverConsolidatedBodyEntry> Entries { get; }

    public int PopulationActCount => Entries.Count;

    public int ActsWithBodiesCount { get; }

    public int ActGapCount { get; }

    public int NotDiscoveredCount { get; }

    public int AdmittedBodyCount { get; }

    public int RejectedBodyCount { get; }

    public int GapBodyCount { get; }

    public int BodyCount => AdmittedBodyCount + RejectedBodyCount + GapBodyCount;

    /// <summary>True only when every population act has a delivered set. Says nothing about admission.</summary>
    public bool AllPopulationActsDisposed => NotDiscoveredCount == 0;

    /// <summary>
    /// Assembles the ledger, or refuses by name. Every offender is named, in ordinal order.
    /// </summary>
    public static LuxembourgNeverConsolidatedBodyLedger? TryComplete(
        LuxembourgNeverConsolidatedCoverage coverage,
        IReadOnlyList<LuxembourgGazetteBodySet> sets,
        out LuxembourgNeverConsolidatedBodyLedgerRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(sets);
        detail = null;

        // THE COUNTING BOUNDARY DECIDES WHO IS IN, exactly as the coverage did.
        var population = coverage.Placements
            .Where(static p => LuxembourgActClassManifest.IsCounted(p.Scope))
            .ToArray();
        var counted = population.Select(static p => p.PublisherActIri).ToHashSet(StringComparer.Ordinal);

        var byAct = new Dictionary<string, LuxembourgGazetteBodySet>(StringComparer.Ordinal);
        var outside = new SortedSet<string>(StringComparer.Ordinal);
        var twice = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            ArgumentNullException.ThrowIfNull(set, nameof(sets));
            if (!counted.Contains(set.PublisherActIri))
            {
                outside.Add(set.PublisherActIri);
                continue;
            }

            if (!byAct.TryAdd(set.PublisherActIri, set))
            {
                twice.Add(set.PublisherActIri);
            }
        }

        if (outside.Count > 0)
        {
            refusal = LuxembourgNeverConsolidatedBodyLedgerRefusal.SetForActNotInPopulation;
            detail = "Body sets name acts the coverage does not count: " + string.Join("; ", outside);
            return null;
        }

        if (twice.Count > 0)
        {
            refusal = LuxembourgNeverConsolidatedBodyLedgerRefusal.SetDeliveredTwice;
            detail = "Two body sets name one act: " + string.Join("; ", twice);
            return null;
        }

        var entries = new List<LuxembourgNeverConsolidatedBodyEntry>(population.Length);
        foreach (var placement in population)
        {
            if (!byAct.TryGetValue(placement.PublisherActIri, out var set))
            {
                entries.Add(new LuxembourgNeverConsolidatedBodyEntry(
                    placement, null, LuxembourgNeverConsolidatedBodyActOutcome.BodyNotDiscovered));
                continue;
            }

            entries.Add(new LuxembourgNeverConsolidatedBodyEntry(
                placement,
                set,
                set.ActGap is null
                    ? LuxembourgNeverConsolidatedBodyActOutcome.BodiesDisposed
                    : LuxembourgNeverConsolidatedBodyActOutcome.ActGap));
        }

        refusal = LuxembourgNeverConsolidatedBodyLedgerRefusal.None;
        return new LuxembourgNeverConsolidatedBodyLedger(entries);
    }

    /// <summary>The line for one population act, or null when the coverage does not count it.</summary>
    public LuxembourgNeverConsolidatedBodyEntry? EntryFor(string publisherActIri)
    {
        ArgumentNullException.ThrowIfNull(publisherActIri);
        return _byAct.GetValueOrDefault(publisherActIri);
    }

    public string Describe() =>
        $"population={PopulationActCount} acts_with_bodies={ActsWithBodiesCount} act_gaps={ActGapCount} "
        + $"not_discovered={NotDiscoveredCount} bodies={BodyCount} (admitted={AdmittedBodyCount} "
        + $"rejected={RejectedBodyCount} gaps={GapBodyCount})";
}
