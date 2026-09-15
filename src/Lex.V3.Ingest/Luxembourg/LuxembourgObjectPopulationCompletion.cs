using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Corpus;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why one Luxembourg run's object population could not be closed.</summary>
public enum LuxembourgObjectPopulationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The run refused, or did not carry every artifact a complete run carries.</summary>
    [JsonStringEnumMemberName("run_not_complete")]
    RunNotComplete = 1,

    /// <summary>
    /// A corpus record names a publisher URI this run never derived an observation for, so the
    /// population contains an object the run cannot account for.
    /// </summary>
    [JsonStringEnumMemberName("member_is_not_an_observed_subject")]
    MemberIsNotAnObservedSubject = 2,

    /// <summary>
    /// This run derived an observation for a subject no corpus record holds, so the population is
    /// not every object the run observed.
    /// </summary>
    [JsonStringEnumMemberName("observed_subject_has_no_member")]
    ObservedSubjectHasNoMember = 3,

    /// <summary>Two corpus records claim one observed subject, so the population has no single shape.</summary>
    [JsonStringEnumMemberName("subject_claimed_twice")]
    SubjectClaimedTwice = 4,

    /// <summary>
    /// One subject appears twice in this run's own derived subject list. The population would then
    /// have no definite size, and collapsing the duplicate silently would decide that question
    /// rather than state it.
    /// </summary>
    [JsonStringEnumMemberName("observed_subject_delivered_twice")]
    ObservedSubjectDeliveredTwice = 5,

    /// <summary>An acquisition outcome names an ordinal no corpus record holds.</summary>
    [JsonStringEnumMemberName("outcome_outside_population")]
    OutcomeOutsidePopulation = 6,
}

/// <summary>
/// One Luxembourg run's object population, closed against the run's own evidence.
/// </summary>
/// <remarks>
/// <para>
/// THE EXPECTATION IS THE DERIVED SUBJECTS, AND THEY ARE NOT A RESTATEMENT OF THE POPULATION.
/// <c>LuxembourgQueryExecutionResult.ResourceObservationSubjects</c> is the exact set of publisher
/// URIs <c>BuildResourceObservations</c> derived one observation for, built from the census family's
/// own delivered rows <em>before any scope manifest exists</em>. The corpus record set arrives by a
/// different road entirely: resolve, reduce, write, and reopen from custody. Comparing the two is
/// therefore a genuine end-to-end totality check across that whole chain, not a value checked
/// against a copy of itself.
/// </para>
/// <para>
/// WHY THE COMPARISON IS EXACT RATHER THAN A COUNT. The Union closer can only compare sizes, because
/// its run carries a count and not the observed objects themselves. Luxembourg carries the subjects,
/// so this closer matches them by identity: every member names an observed subject, every observed
/// subject is named by a member, and no subject is named twice. A count check passes for a
/// population that swapped one object for another; this does not.
/// </para>
/// <para>
/// One observation becomes one object and none is dropped on the way, which is what makes the
/// matching exact rather than approximate: <c>LuxembourgScopeResolver.Resolve</c> maps its ordered
/// observations through a plain projection and allocates its resolutions and scope inputs at exactly
/// that length, and a duplicate publisher URI or a structural failure fails the whole resolution
/// rather than quietly shrinking it.
/// </para>
/// <para>
/// WHAT IT PROVES, AND NOT MORE. It says nothing about minted fetch rows: Luxembourg has no
/// equivalent of the Union's per-ordinal minted-row accounting, and inventing one here to make the
/// two closers look symmetric would be asserting a fact this run does not carry. It also does not
/// claim every member was fetched -- an outcome is required to name a member, never the reverse.
/// </para>
/// <para>
/// It is minted from the run and takes no collection from a caller, so there is no list anyone can
/// shorten. That matters here for the same reason it matters on the Union side:
/// <c>LuxembourgQueryExecutionResult.Delivered</c> is public and accepts the subject list and the
/// corpus record set as independent arguments, so a valid, verified, shorter set handed in beside
/// the original subjects would otherwise have been read as the whole population.
/// </para>
/// </remarks>
public sealed class LuxembourgObjectPopulationCompletion
{
    private LuxembourgObjectPopulationCompletion(
        IReadOnlyList<CorpusRecord> members,
        int acquisitionOutcomeCount)
    {
        Members = members;
        AcquisitionOutcomeCount = acquisitionOutcomeCount;
    }

    /// <summary>Every observed subject of this run, once each, in the set's own ordinal order.</summary>
    public IReadOnlyList<CorpusRecord> Members { get; }

    /// <summary>How many members carry an acquisition outcome. Never more than <see cref="ObjectCount"/>.</summary>
    public int AcquisitionOutcomeCount { get; }

    /// <summary>The population size. A count reported, never a count accepted.</summary>
    public int ObjectCount => Members.Count;

    /// <summary>
    /// Closes the population of a delivered run, or refuses by name and says which subject or
    /// ordinal offended.
    /// </summary>
    public static LuxembourgObjectPopulationCompletion? TryClose(
        LuxembourgQueryExecutionResult run,
        out LuxembourgObjectPopulationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(run);
        refusal = LuxembourgObjectPopulationRefusal.None;
        detail = null;

        // A refused run has no population to close, and a delivered one missing either of these
        // cannot be joined at all. Say so here rather than reporting an empty population as complete.
        if (run.Refusal is not null
            || run.CorpusRecordSet is null
            || run.DocumentAcquisitionOutcomesByOrdinal is null)
        {
            refusal = LuxembourgObjectPopulationRefusal.RunNotComplete;
            detail = run.Refusal is { } refused
                ? $"the run refused: {refused.Code}"
                : "a delivered run must carry its corpus record set and acquisition outcomes";
            return null;
        }

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subject in run.ResourceObservationSubjects)
        {
            if (!expected.Add(subject))
            {
                refusal = LuxembourgObjectPopulationRefusal.ObservedSubjectDeliveredTwice;
                detail = $"the run's derived subject list names '{subject}' twice";
                return null;
            }
        }

        var members = run.CorpusRecordSet.Set.Records;
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var ordinals = new HashSet<int>();
        foreach (var member in members)
        {
            var subject = member.ObjectRef.PublisherUri;
            if (!expected.Contains(subject))
            {
                refusal = LuxembourgObjectPopulationRefusal.MemberIsNotAnObservedSubject;
                detail = $"corpus record at ordinal {member.ObjectOrdinal} names '{subject}', "
                    + "which this run derived no observation for";
                return null;
            }

            // The set already refuses two records at one ordinal and two records naming one exact
            // object reference. Neither of those refuses two DIFFERENT object references that carry
            // the same publisher URI, which is the shape that would put one subject in the
            // population twice.
            if (!claimed.Add(subject))
            {
                refusal = LuxembourgObjectPopulationRefusal.SubjectClaimedTwice;
                detail = $"two corpus records claim subject '{subject}'";
                return null;
            }

            ordinals.Add(member.ObjectOrdinal);
        }

        foreach (var subject in run.ResourceObservationSubjects)
        {
            if (!claimed.Contains(subject))
            {
                refusal = LuxembourgObjectPopulationRefusal.ObservedSubjectHasNoMember;
                detail = $"this run observed '{subject}', which no corpus record holds";
                return null;
            }
        }

        foreach (var ordinal in run.DocumentAcquisitionOutcomesByOrdinal.Keys
            .OrderBy(static ordinal => ordinal))
        {
            if (!ordinals.Contains(ordinal))
            {
                refusal = LuxembourgObjectPopulationRefusal.OutcomeOutsidePopulation;
                detail = $"an acquisition outcome names ordinal {ordinal}, which no corpus record holds";
                return null;
            }
        }

        return new LuxembourgObjectPopulationCompletion(
            members, run.DocumentAcquisitionOutcomesByOrdinal.Count);
    }
}
