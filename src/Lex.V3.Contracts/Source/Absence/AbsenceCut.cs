using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Absence;

/// <summary>Why a family observation was refused. Closed.</summary>
public enum AbsenceFamilyObservationRefusal
{
    /// <summary>No refusal: the observation was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The observation identity is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("observation_id_invalid")]
    ObservationIdInvalid = 1,

    /// <summary>The family key is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("family_key_invalid")]
    FamilyKeyInvalid = 2,

    /// <summary>The timestamp is not stated at UTC.</summary>
    [JsonStringEnumMemberName("timestamp_not_utc")]
    TimestampNotUtc = 3,

    /// <summary>The declared precision is not a member of the closed vocabulary.</summary>
    [JsonStringEnumMemberName("precision_undefined")]
    PrecisionUndefined = 4,

    /// <summary>
    /// The timestamp carries detail finer than its declared precision, so the declaration is not a
    /// true statement about the value.
    /// </summary>
    [JsonStringEnumMemberName("timestamp_finer_than_declared_precision")]
    TimestampFinerThanDeclaredPrecision = 5,

    /// <summary>The clock source is not named.</summary>
    [JsonStringEnumMemberName("clock_source_invalid")]
    ClockSourceInvalid = 6,

    /// <summary>The provenance is not a member of the closed vocabulary.</summary>
    [JsonStringEnumMemberName("provenance_undefined")]
    ProvenanceUndefined = 7,

    /// <summary>The maximum admitted skew is negative.</summary>
    [JsonStringEnumMemberName("skew_negative")]
    SkewNegative = 8,

    /// <summary>
    /// The uncertainty interval this timestamp, precision and skew describe cannot be represented,
    /// so no comparison against it would be a true statement.
    /// </summary>
    [JsonStringEnumMemberName("uncertainty_interval_not_representable")]
    UncertaintyIntervalNotRepresentable = 9,

    /// <summary>
    /// The observation is a wrapper, a cache replay, a stale row or an incomplete row. R3.3 admits
    /// none of them into a cut, so none of them can become one.
    /// </summary>
    [JsonStringEnumMemberName("provenance_not_freshly_executed")]
    ProvenanceNotFreshlyExecuted = 10,
}

/// <summary>
/// One per-family observation inside a run, with the temporal evidence R3.3 requires of it.
/// </summary>
/// <remarks>
/// <para>
/// The uncertainty interval is derived rather than declared. A declared precision means the value
/// was truncated to that unit, so the true instant lies in <c>[t, t + width)</c>; a maximum
/// admitted clock skew widens that symmetrically. The interval is therefore
/// <c>[t - skew, t + width + skew]</c>, and both ends are closed so that two intervals meeting at
/// a single instant count as overlapping. That is the conservative direction: R3.3 requires
/// nonoverlapping intervals before a cut may advance, and refusing a touching pair costs a cut
/// while admitting one would let two possibly simultaneous cuts count as consecutive.
/// </para>
/// <para>
/// Precision is also checked against the value. Without that check a caller could declare
/// <c>hour</c> on a timestamp carrying seconds, which is exactly the compensation R3.3 forbids
/// identifiers from making.
/// </para>
/// </remarks>
public sealed class AbsenceFamilyObservation
{
    private AbsenceFamilyObservation(
        string observationId,
        string familyKey,
        DateTimeOffset observedAt,
        AbsenceTimestampPrecision precision,
        string clockSource,
        TimeSpan maximumAdmittedSkew)
    {
        ObservationId = observationId;
        FamilyKey = familyKey;
        ObservedAt = observedAt;
        Precision = precision;
        ClockSource = clockSource;
        MaximumAdmittedSkew = maximumAdmittedSkew;
    }

    public string ObservationId { get; }

    public string FamilyKey { get; }

    public DateTimeOffset ObservedAt { get; }

    public AbsenceTimestampPrecision Precision { get; }

    public string ClockSource { get; }

    public TimeSpan MaximumAdmittedSkew { get; }

    /// <summary>
    /// The only path that mints an observation a cut can hold. Provenance is a parameter rather
    /// than a property because a cut admits exactly one provenance: an observation that is not
    /// freshly executed never becomes a member of one, so keeping the value would invite a reader
    /// to believe a cut can hold a replay.
    /// </summary>
    public static AbsenceFamilyObservation? TryCreate(
        string observationId,
        string familyKey,
        DateTimeOffset observedAt,
        AbsenceTimestampPrecision precision,
        string clockSource,
        TimeSpan maximumAdmittedSkew,
        AbsenceObservationProvenance provenance,
        out AbsenceFamilyObservationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(observationId);
        ArgumentNullException.ThrowIfNull(familyKey);
        ArgumentNullException.ThrowIfNull(clockSource);

        if (!AbsenceValidation.IsIdentifier(observationId))
        {
            refusal = AbsenceFamilyObservationRefusal.ObservationIdInvalid;
            return null;
        }

        if (!AbsenceValidation.IsIdentifier(familyKey))
        {
            refusal = AbsenceFamilyObservationRefusal.FamilyKeyInvalid;
            return null;
        }

        if (observedAt.Offset != TimeSpan.Zero)
        {
            refusal = AbsenceFamilyObservationRefusal.TimestampNotUtc;
            return null;
        }

        if (!Enum.IsDefined(precision))
        {
            refusal = AbsenceFamilyObservationRefusal.PrecisionUndefined;
            return null;
        }

        if (!Enum.IsDefined(provenance))
        {
            refusal = AbsenceFamilyObservationRefusal.ProvenanceUndefined;
            return null;
        }

        if (provenance != AbsenceObservationProvenance.FreshlyExecuted)
        {
            // Refused here rather than in the cut so that no path exists to build the object at
            // all. A cut that had to remember to check would be one forgotten call from admitting
            // a cache replay as a fresh family response.
            refusal = AbsenceFamilyObservationRefusal.ProvenanceNotFreshlyExecuted;
            return null;
        }

        if (!AbsenceValidation.IsIdentifier(clockSource))
        {
            refusal = AbsenceFamilyObservationRefusal.ClockSourceInvalid;
            return null;
        }

        if (maximumAdmittedSkew < TimeSpan.Zero)
        {
            refusal = AbsenceFamilyObservationRefusal.SkewNegative;
            return null;
        }

        var width = AbsenceTiming.WidthOf(precision);
        if (observedAt.UtcTicks % width.Ticks != 0)
        {
            refusal = AbsenceFamilyObservationRefusal.TimestampFinerThanDeclaredPrecision;
            return null;
        }

        if (observedAt.UtcTicks - DateTimeOffset.MinValue.UtcTicks < maximumAdmittedSkew.Ticks ||
            DateTimeOffset.MaxValue.UtcTicks - observedAt.UtcTicks < width.Ticks + maximumAdmittedSkew.Ticks)
        {
            refusal = AbsenceFamilyObservationRefusal.UncertaintyIntervalNotRepresentable;
            return null;
        }

        refusal = AbsenceFamilyObservationRefusal.None;
        return new AbsenceFamilyObservation(
            observationId, familyKey, observedAt, precision, clockSource, maximumAdmittedSkew);
    }

    /// <summary>The earliest instant the recorded value can denote.</summary>
    public DateTimeOffset EarliestPossibleInstant() => ObservedAt - MaximumAdmittedSkew;

    /// <summary>The latest instant the recorded value can denote.</summary>
    public DateTimeOffset LatestPossibleInstant() =>
        ObservedAt + AbsenceTiming.WidthOf(Precision) + MaximumAdmittedSkew;
}

/// <summary>Why a cut was refused. Closed.</summary>
public enum AbsenceCutRefusal
{
    /// <summary>No refusal: the cut was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The run identity is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("run_id_invalid")]
    RunIdInvalid = 1,

    /// <summary>The completion state is not a member of the closed vocabulary.</summary>
    [JsonStringEnumMemberName("completion_undefined")]
    CompletionUndefined = 2,

    /// <summary>The applicable-set axis is not a member of the closed vocabulary.</summary>
    [JsonStringEnumMemberName("applicable_set_undefined")]
    ApplicableSetUndefined = 3,

    /// <summary>The observation list is empty, so the cut has no temporal evidence at all.</summary>
    [JsonStringEnumMemberName("observations_empty")]
    ObservationsEmpty = 4,

    /// <summary>Two observations share an identity.</summary>
    [JsonStringEnumMemberName("duplicate_observation_id")]
    DuplicateObservationId = 5,

    /// <summary>Two observations cover the same family, so the list is not per-family.</summary>
    [JsonStringEnumMemberName("duplicate_family_key")]
    DuplicateFamilyKey = 6,

    /// <summary>An observed key is not an exact canonical publisher URI.</summary>
    [JsonStringEnumMemberName("observed_key_invalid")]
    ObservedKeyInvalid = 7,

    /// <summary>The same observed key appears twice.</summary>
    [JsonStringEnumMemberName("duplicate_observed_key")]
    DuplicateObservedKey = 8,
}

/// <summary>
/// One run over an applicable set, complete or partial, with the evidence R3.3 requires of a cut.
/// </summary>
/// <remarks>
/// <para>
/// A cut is not per subject. One run answers for every subject on its axis, which is what makes
/// the "membership changes must not reset unrelated histories" property testable: the same cut
/// object, carrying a different observed-key set and a different observed-set digest from the
/// previous one, still compares as the identical tuple for a subject neither set mentions.
/// </para>
/// <para>
/// <see cref="ScopeManifestRef"/> and <see cref="ObservedSetRef"/> are carried and retained but are
/// not part of any comparison. R3.3 states that directly: the full manifest and cut-specific
/// observed-set digests remain evidence and are deliberately absent from the comparability tuple.
/// </para>
/// <para>
/// <see cref="ObservedKeys"/> means the canonical publisher URIs positively observed by a
/// successfully completed fresh family response in this run. Under
/// <see cref="AbsenceRunCompletion.EnumerationComplete"/> it is the complete applicable set, so a
/// key outside it is absent. Under <see cref="AbsenceRunCompletion.Partial"/> it is only what was
/// seen, so a key outside it is unknown rather than absent. One field with a reading fixed by the
/// completion state, rather than two fields that could disagree.
/// </para>
/// </remarks>
public sealed class AbsenceCut
{
    private readonly HashSet<string> _observedKeys;

    private AbsenceCut(
        string runId,
        AbsenceRunCompletion completion,
        AbsenceApplicableSet applicableSet,
        IReadOnlyList<AbsenceFamilyObservation> observations,
        SourceArtifactRef scopeManifestRef,
        SourceArtifactRef observedSetRef,
        HashSet<string> observedKeys)
    {
        RunId = runId;
        Completion = completion;
        ApplicableSet = applicableSet;
        Observations = observations;
        ScopeManifestRef = scopeManifestRef;
        ObservedSetRef = observedSetRef;
        _observedKeys = observedKeys;
    }

    public string RunId { get; }

    public AbsenceRunCompletion Completion { get; }

    public AbsenceApplicableSet ApplicableSet { get; }

    public IReadOnlyList<AbsenceFamilyObservation> Observations { get; }

    /// <summary>The complete scope-manifest identity and digest. Evidence, never comparison.</summary>
    public SourceArtifactRef ScopeManifestRef { get; }

    /// <summary>The complete observed-set identity and digest. Evidence, never comparison.</summary>
    public SourceArtifactRef ObservedSetRef { get; }

    /// <summary>The only path that mints a cut.</summary>
    public static AbsenceCut? TryCreate(
        string runId,
        AbsenceRunCompletion completion,
        AbsenceApplicableSet applicableSet,
        IReadOnlyList<AbsenceFamilyObservation> observations,
        SourceArtifactRef scopeManifestRef,
        SourceArtifactRef observedSetRef,
        IReadOnlyList<string> observedKeys,
        out AbsenceCutRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(scopeManifestRef);
        ArgumentNullException.ThrowIfNull(observedSetRef);
        ArgumentNullException.ThrowIfNull(observedKeys);

        if (!AbsenceValidation.IsIdentifier(runId))
        {
            refusal = AbsenceCutRefusal.RunIdInvalid;
            return null;
        }

        if (!Enum.IsDefined(completion))
        {
            refusal = AbsenceCutRefusal.CompletionUndefined;
            return null;
        }

        if (!Enum.IsDefined(applicableSet))
        {
            refusal = AbsenceCutRefusal.ApplicableSetUndefined;
            return null;
        }

        if (observations.Count == 0)
        {
            refusal = AbsenceCutRefusal.ObservationsEmpty;
            return null;
        }

        var seenObservationIds = new HashSet<string>(StringComparer.Ordinal);
        var seenFamilyKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            if (!seenObservationIds.Add(observation.ObservationId))
            {
                refusal = AbsenceCutRefusal.DuplicateObservationId;
                return null;
            }

            if (!seenFamilyKeys.Add(observation.FamilyKey))
            {
                refusal = AbsenceCutRefusal.DuplicateFamilyKey;
                return null;
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in observedKeys)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (!AbsenceValidation.IsPublisherUri(key))
            {
                refusal = AbsenceCutRefusal.ObservedKeyInvalid;
                return null;
            }

            if (!keys.Add(key))
            {
                refusal = AbsenceCutRefusal.DuplicateObservedKey;
                return null;
            }
        }

        refusal = AbsenceCutRefusal.None;
        return new AbsenceCut(
            runId,
            completion,
            applicableSet,
            observations.ToArray(),
            scopeManifestRef,
            observedSetRef,
            keys);
    }

    /// <summary>The minimum UTC timestamp among the run's fresh family observations.</summary>
    public DateTimeOffset CutStart() => Observations.Min(static o => o.ObservedAt);

    /// <summary>The maximum UTC timestamp among the run's fresh family observations.</summary>
    public DateTimeOffset CutEnd() => Observations.Max(static o => o.ObservedAt);

    /// <summary>
    /// The earliest instant the cut can have started. Taken over every observation rather than over
    /// the one holding the minimum timestamp, because two observations can share that minimum and
    /// R3.3 forbids an identifier from breaking an equal timestamp. The minimum over all of them
    /// needs no tie-break and is never later than the alternative.
    /// </summary>
    public DateTimeOffset EarliestPossibleStart() =>
        Observations.Min(static o => o.EarliestPossibleInstant());

    /// <summary>The latest instant the cut can have ended, by the same rule.</summary>
    public DateTimeOffset LatestPossibleEnd() =>
        Observations.Max(static o => o.LatestPossibleInstant());

    /// <summary>The named UTC clock sources this cut rests on, ordinally sorted.</summary>
    public IReadOnlyList<string> ClockSources() =>
        Observations.Select(static o => o.ClockSource)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Every observation identity in this run, ordinally sorted.</summary>
    public IReadOnlyList<string> ObservationIds() =>
        Observations.Select(static o => o.ObservationId)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The canonical publisher URIs this run positively observed.</summary>
    public IReadOnlyList<string> ObservedKeys() =>
        _observedKeys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();

    /// <summary>True when this run positively observed the exact key.</summary>
    public bool Observed(string canonicalPublisherUri)
    {
        ArgumentNullException.ThrowIfNull(canonicalPublisherUri);
        return _observedKeys.Contains(canonicalPublisherUri);
    }
}
