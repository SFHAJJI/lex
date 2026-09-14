using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Corpus;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why one run's object population could not be closed.</summary>
public enum EuObjectPopulationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The run refused, or did not carry every artifact a complete run carries.</summary>
    [JsonStringEnumMemberName("run_not_complete")]
    RunNotComplete = 1,

    /// <summary>Two corpus records claim one object ordinal, so the population has no single shape.</summary>
    [JsonStringEnumMemberName("object_claimed_twice")]
    ObjectClaimedTwice = 2,

    /// <summary>A minted fetch row names an ordinal no corpus record holds.</summary>
    [JsonStringEnumMemberName("minted_row_outside_population")]
    MintedRowOutsidePopulation = 3,

    /// <summary>An acquisition outcome names an ordinal no corpus record holds.</summary>
    [JsonStringEnumMemberName("outcome_outside_population")]
    OutcomeOutsidePopulation = 4,

    /// <summary>An acquisition outcome names an ordinal that minted no fetch row.</summary>
    [JsonStringEnumMemberName("outcome_without_minted_row")]
    OutcomeWithoutMintedRow = 5,

    /// <summary>
    /// The corpus record set holds a different number of objects than the run says it observed, so
    /// the population is not the run's own population.
    /// </summary>
    [JsonStringEnumMemberName("population_is_not_every_observed_object")]
    PopulationIsNotEveryObservedObject = 6,
}

/// <summary>
/// One EU run's object population, closed against the run's own evidence.
/// </summary>
/// <remarks>
/// <para>
/// THE EXPECTATION IS THE OBSERVED OBJECTS, NOT THE MINTED ROWS, and that is the whole of this type.
/// A run mints a fetch address only for an object whose listed formats reach the body ladder, so an
/// object the ladder cannot address mints nothing. A population keyed on minted rows would therefore
/// drop precisely the objects whose formats this build does not serve, and would call the remainder
/// complete. One corpus record is written per observed object unconditionally, so the corpus set is
/// the honest expectation and an unaddressable object stays a member carrying its own disposition.
/// </para>
/// <para>
/// WHAT IT PROVES, AND NOT MORE. Every observed object appears exactly once. Every minted row and
/// every acquisition outcome names a member, and every outcome names a row that was minted. It does
/// NOT claim every minted row was fetched: a row can be excluded on the body axis after minting, so
/// requiring an outcome per row would refuse runs that are correct. The rule is that nothing appears
/// that the population does not contain, and nothing in the population appears twice.
/// </para>
/// <para>
/// It is minted from the run and takes no collection from a caller, so there is no list anyone can
/// shorten. That is the difference between a population and an assertion about one. Taking no list
/// was not on its own enough: the run itself is assembled through a public door that accepts the
/// observed count and the corpus record set as independent arguments, so a valid, verified, SHORTER
/// set could be handed in beside the original count and would have been read as the whole
/// population. The size of the population is therefore checked against the run's own count rather
/// than taken from the set, which is the same rule applied one level further back.
/// </para>
/// </remarks>
public sealed class EuObjectPopulationCompletion
{
    private EuObjectPopulationCompletion(
        IReadOnlyList<CorpusRecord> members,
        int mintedRowCount,
        int acquisitionOutcomeCount)
    {
        Members = members;
        MintedRowCount = mintedRowCount;
        AcquisitionOutcomeCount = acquisitionOutcomeCount;
    }

    /// <summary>Every observed object of this run, once each, in the run's own ordinal order.</summary>
    public IReadOnlyList<CorpusRecord> Members { get; }

    /// <summary>How many members minted a fetch row. Never more than <see cref="Members"/>.</summary>
    public int MintedRowCount { get; }

    /// <summary>How many members carry an acquisition outcome. Never more than <see cref="MintedRowCount"/>.</summary>
    public int AcquisitionOutcomeCount { get; }

    /// <summary>The population size. A count reported, never a count accepted.</summary>
    public int ObjectCount => Members.Count;

    /// <summary>
    /// Closes the population of a delivered run, or refuses by name and says which ordinal offended.
    /// </summary>
    public static EuObjectPopulationCompletion? TryClose(
        EuQueryExecutionResult run,
        out EuObjectPopulationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(run);
        refusal = EuObjectPopulationRefusal.None;
        detail = null;

        // A refused run has no population to close, and a delivered one that is missing any of these
        // cannot be joined at all. Say so here rather than reporting an empty population as complete.
        if (run.Refusal is not null
            || run.CorpusRecordSet is null
            || run.MintedRowsByOrdinal is null
            || run.DocumentAcquisitionOutcomesByOrdinal is null)
        {
            refusal = EuObjectPopulationRefusal.RunNotComplete;
            detail = run.Refusal is { } refused
                ? $"the run refused: {refused.Code}"
                : "a delivered run must carry its corpus record set, minted rows and acquisition outcomes";
            return null;
        }

        var members = run.CorpusRecordSet.Set.Records;

        // THE SET IS VERIFIED, WHICH IS NOT THE SAME AS BEING THIS RUN'S WHOLE POPULATION. A
        // shortened set is still internally valid: it is strictly ordered, names no object twice,
        // and canonicalizes to its own digest. What it cannot do is agree with the count the run
        // states separately. EuQueryExecutionResult.Delivered is public and takes the observed count
        // and the record set as independent arguments, so without this the one door that could
        // shorten the population is the door that hands it over. Both directions refuse: a set
        // larger than the run observed is no more this run's population than a smaller one.
        if (members.Count != run.ObservedObjectCount)
        {
            refusal = EuObjectPopulationRefusal.PopulationIsNotEveryObservedObject;
            detail = $"the run observed {run.ObservedObjectCount} objects but its corpus record set "
                + $"holds {members.Count}";
            return null;
        }

        var byOrdinal = new Dictionary<int, CorpusRecord>();
        foreach (var member in members)
        {
            if (!byOrdinal.TryAdd(member.ObjectOrdinal, member))
            {
                refusal = EuObjectPopulationRefusal.ObjectClaimedTwice;
                detail = $"two corpus records claim object ordinal {member.ObjectOrdinal}";
                return null;
            }
        }

        foreach (var ordinal in run.MintedRowsByOrdinal.Keys.OrderBy(static ordinal => ordinal))
        {
            if (!byOrdinal.ContainsKey(ordinal))
            {
                refusal = EuObjectPopulationRefusal.MintedRowOutsidePopulation;
                detail = $"a fetch row was minted for ordinal {ordinal}, which no corpus record holds";
                return null;
            }
        }

        foreach (var ordinal in run.DocumentAcquisitionOutcomesByOrdinal.Keys
            .OrderBy(static ordinal => ordinal))
        {
            if (!byOrdinal.ContainsKey(ordinal))
            {
                refusal = EuObjectPopulationRefusal.OutcomeOutsidePopulation;
                detail = $"an acquisition outcome names ordinal {ordinal}, which no corpus record holds";
                return null;
            }

            // An outcome without a minted row would mean this run fetched something it never
            // addressed, which no evidence in the run could account for.
            if (!run.MintedRowsByOrdinal.ContainsKey(ordinal))
            {
                refusal = EuObjectPopulationRefusal.OutcomeWithoutMintedRow;
                detail = $"an acquisition outcome names ordinal {ordinal}, which minted no fetch row";
                return null;
            }
        }

        return new EuObjectPopulationCompletion(
            members,
            run.MintedRowsByOrdinal.Count,
            run.DocumentAcquisitionOutcomesByOrdinal.Count);
    }
}
