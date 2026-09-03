using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Absence;

/// <summary>What appending a cut did to a subject's absence accounting. Closed.</summary>
public enum AbsenceAppendDisposition
{
    /// <summary>The cut counted. Every gate passed under the current tuple.</summary>
    [JsonStringEnumMemberName("streak_advanced")]
    StreakAdvanced = 1,

    /// <summary>
    /// The run observed the subject. Every open streak closed and a new generation opened, whether
    /// the run was complete or partial.
    /// </summary>
    [JsonStringEnumMemberName("presence_break_recorded")]
    PresenceBreakRecorded = 2,

    /// <summary>
    /// A partial run that did not observe the subject. It neither advances nor breaks, because an
    /// unenumerated key is not an absent key.
    /// </summary>
    [JsonStringEnumMemberName("partial_run_no_effect")]
    PartialRunNoEffect = 3,

    /// <summary>The twenty hour floor against the preceding advancing cut was not met.</summary>
    [JsonStringEnumMemberName("separation_floor_not_met")]
    SeparationFloorNotMet = 4,

    /// <summary>The named UTC clock sources changed between advancing cuts.</summary>
    [JsonStringEnumMemberName("clock_source_changed")]
    ClockSourceChanged = 5,

    /// <summary>Replacement review froze this subject, so no cut can advance it.</summary>
    [JsonStringEnumMemberName("frozen_pending_replacement_review")]
    FrozenPendingReplacementReview = 6,
}

/// <summary>Why the ledger refused an operation. Closed.</summary>
public enum AbsenceLedgerRefusal
{
    /// <summary>No refusal: the operation was accepted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The declared applicable-set axis is not a member of the closed vocabulary.</summary>
    [JsonStringEnumMemberName("applicable_set_undefined")]
    ApplicableSetUndefined = 1,

    /// <summary>An event identity is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("event_id_invalid")]
    EventIdInvalid = 2,

    /// <summary>An event identity already occurs in this ledger.</summary>
    [JsonStringEnumMemberName("event_id_reused")]
    EventIdReused = 3,

    /// <summary>
    /// The proposed policy is the configuration already in force. R3.3 opens a generation on a
    /// change, and an unrelated manifest edit that leaves every applicable member identical is not
    /// a change to this subject.
    /// </summary>
    [JsonStringEnumMemberName("comparison_policy_unchanged")]
    ComparisonPolicyUnchanged = 4,

    /// <summary>A run identity already occurs in this ledger.</summary>
    [JsonStringEnumMemberName("run_id_reused")]
    RunIdReused = 5,

    /// <summary>An observation identity already occurs in this ledger.</summary>
    [JsonStringEnumMemberName("observation_id_reused")]
    ObservationIdReused = 6,

    /// <summary>
    /// The cut decides a different applicable set from the one this subject is tracked on. A
    /// normalized family set cannot answer for a root target, or the reverse.
    /// </summary>
    [JsonStringEnumMemberName("cut_axis_not_applicable")]
    CutAxisNotApplicable = 7,

    /// <summary>The classification names neither equivalence class containing this subject.</summary>
    [JsonStringEnumMemberName("classification_outside_this_subject")]
    ClassificationOutsideThisSubject = 8,
}

/// <summary>
/// The append-only absence history of one stable subject.
/// </summary>
/// <remarks>
/// <para>
/// A ledger holds one subject and one applicable-set axis. Generations, receipts and recorded
/// replacement classifications are only ever appended; nothing here removes or rewrites an entry,
/// which is what makes "reappearance never deletes the earlier absence receipts" a property of the
/// data structure rather than a rule someone has to keep.
/// </para>
/// <para>
/// The streak is derived from the retained receipts by exact match on the fourteen member absence
/// key, not counted in a field. That is deliberate and it is where the configuration ABA defense
/// actually lives. A counter would have to be reset by whoever changes the generation, and a
/// forgotten reset is invisible; a derived count cannot see a receipt whose key differs by so much
/// as the generation identity, so a return to a byte-identical earlier configuration reaches a
/// generation with a new identity, a new key projection, and no receipts of its own.
/// </para>
/// </remarks>
public sealed class AbsenceHistoryLedger
{
    private readonly List<Generation> _generations = [];
    private readonly List<CutReceipt> _receipts = [];
    private readonly List<AbsenceReplacementClassification> _classifications = [];

    // One namespace for every identity this ledger has consumed. R3.3 forbids an observation ID,
    // family receipt, wrapper, retry or run identity used by one advancing cut from occurring in
    // another; keeping the roles in one set also refuses the cross-role collision, which no honest
    // producer emits and which would otherwise make two distinct events indistinguishable.
    private readonly HashSet<string> _usedIdentities = new(StringComparer.Ordinal);

    private AbsenceHistoryLedger(AbsenceSubject subject, AbsenceApplicableSet axis, Generation first)
    {
        Subject = subject;
        Axis = axis;
        _generations.Add(first);
        _usedIdentities.Add(first.OpeningEventId);
    }

    public AbsenceSubject Subject { get; }

    /// <summary>
    /// Which complete set decides this subject's membership. Implied by the entity kind, so it is
    /// not a member of the absence key tuple; carried here so a cut on the wrong axis is refused.
    /// </summary>
    public AbsenceApplicableSet Axis { get; }

    /// <summary>Every generation, oldest first. Append only.</summary>
    public IReadOnlyList<Generation> Generations => _generations;

    /// <summary>The generation in force.</summary>
    public Generation CurrentGeneration => _generations[^1];

    /// <summary>
    /// Every appended cut, oldest first, advancing or not. Each retains its complete scope-manifest
    /// and observed-set identities and digests, which is R3.3's retention requirement.
    /// </summary>
    public IReadOnlyList<CutReceipt> Receipts => _receipts;

    /// <summary>Every recorded replacement classification, oldest first.</summary>
    public IReadOnlyList<AbsenceReplacementClassification> ReplacementClassifications => _classifications;

    /// <summary>
    /// The only path that opens a ledger. The first generation's cause is
    /// <see cref="AbsenceHistoryGenerationCause.InitialTracking"/> and its predecessor is null.
    /// </summary>
    public static AbsenceHistoryLedger? TryOpen(
        AbsenceSubject subject,
        AbsenceApplicableSet axis,
        AbsenceComparisonPolicy policy,
        string trackingEventId,
        out AbsenceLedgerRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(trackingEventId);

        if (!Enum.IsDefined(axis))
        {
            refusal = AbsenceLedgerRefusal.ApplicableSetUndefined;
            return null;
        }

        if (!AbsenceValidation.IsIdentifier(trackingEventId))
        {
            refusal = AbsenceLedgerRefusal.EventIdInvalid;
            return null;
        }

        var first = Generation.Open(
            subject,
            predecessor: null,
            ordinal: 1,
            policy,
            AbsenceGenerationOpeningEventKind.TrackingStarted,
            trackingEventId,
            AbsenceHistoryGenerationCause.InitialTracking);

        refusal = AbsenceLedgerRefusal.None;
        return new AbsenceHistoryLedger(subject, axis, first);
    }

    /// <summary>
    /// Opens a generation because a member of the comparison-policy tuple applicable to this
    /// subject changed. Returns null with a typed refusal when nothing changed.
    /// </summary>
    public Generation? TryTransitionComparisonPolicy(
        AbsenceComparisonPolicy policy,
        string eventId,
        out AbsenceLedgerRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(eventId);

        if (!AbsenceValidation.IsIdentifier(eventId))
        {
            refusal = AbsenceLedgerRefusal.EventIdInvalid;
            return null;
        }

        if (_usedIdentities.Contains(eventId))
        {
            refusal = AbsenceLedgerRefusal.EventIdReused;
            return null;
        }

        if (CurrentGeneration.Policy.SameConfigurationAs(policy))
        {
            refusal = AbsenceLedgerRefusal.ComparisonPolicyUnchanged;
            return null;
        }

        var opened = Generation.Open(
            Subject,
            CurrentGeneration,
            CurrentGeneration.Ordinal + 1,
            policy,
            AbsenceGenerationOpeningEventKind.ComparisonPolicyTransition,
            eventId,
            AbsenceHistoryGenerationCause.ComparisonPolicyChanged);

        _generations.Add(opened);
        _usedIdentities.Add(eventId);
        refusal = AbsenceLedgerRefusal.None;
        return opened;
    }

    /// <summary>
    /// Records a replacement classification that names this subject. Replacement detection runs
    /// before absence, so a classification that freezes the subject stops every later advance.
    /// </summary>
    /// <remarks>
    /// There is no unfreeze. R3.3 says a one-to-one candidate freezes absence "pending review" and
    /// is "not a final withdrawal", but it does not define the review or its outcome, so this
    /// contract does not invent one. A frozen subject stays frozen here and the review lives above.
    /// </remarks>
    public bool TryRecordReplacementClassification(
        AbsenceReplacementClassification classification,
        out AbsenceLedgerRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(classification);

        if (classification.EffectOn(Subject.CanonicalPublisherUri)
            == AbsenceReplacementEffect.OutsideThisCoordinate)
        {
            refusal = AbsenceLedgerRefusal.ClassificationOutsideThisSubject;
            return false;
        }

        _classifications.Add(classification);
        refusal = AbsenceLedgerRefusal.None;
        return true;
    }

    /// <summary>True when a recorded classification freezes this subject's counters.</summary>
    public bool IsFrozenPendingReplacementReview() =>
        _classifications.Any(classification =>
            classification.EffectOn(Subject.CanonicalPublisherUri)
                == AbsenceReplacementEffect.FrozenPendingReview);

    /// <summary>
    /// Appends a cut and returns its receipt, or null with a typed refusal when the cut cannot
    /// enter this ledger at all. A cut that enters but does not advance is not a refusal: it is a
    /// receipt whose disposition says why, and it is retained as evidence either way.
    /// </summary>
    public CutReceipt? TryAppend(AbsenceCut cut, out AbsenceLedgerRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(cut);

        if (cut.ApplicableSet != Axis)
        {
            refusal = AbsenceLedgerRefusal.CutAxisNotApplicable;
            return null;
        }

        if (_usedIdentities.Contains(cut.RunId))
        {
            refusal = AbsenceLedgerRefusal.RunIdReused;
            return null;
        }

        foreach (var observationId in cut.ObservationIds())
        {
            if (_usedIdentities.Contains(observationId))
            {
                refusal = AbsenceLedgerRefusal.ObservationIdReused;
                return null;
            }
        }

        var generation = CurrentGeneration;
        var observed = cut.Observed(Subject.CanonicalPublisherUri);
        var complete = cut.Completion == AbsenceRunCompletion.EnumerationComplete;
        var precedingAdvancing = LastAdvancingReceipt(generation);

        var disposition = Decide(cut, observed, complete, precedingAdvancing);
        var advanced = disposition == AbsenceAppendDisposition.StreakAdvanced;

        var receipt = new CutReceipt(
            cut,
            generation,
            observed,
            advanced,
            disposition,
            PrecedingCompleteCutId(generation),
            PrecedingAbsentCutId(generation),
            precedingAdvancing?.Cut.RunId);

        _receipts.Add(receipt);
        _usedIdentities.Add(cut.RunId);
        foreach (var observationId in cut.ObservationIds())
        {
            _usedIdentities.Add(observationId);
        }

        if (observed)
        {
            // The positive closes every open streak for the stable subject, whichever generation
            // or comparison policy it was observed under, so the new generation is opened here
            // rather than left to a caller who might not.
            _generations.Add(Generation.Open(
                Subject,
                generation,
                generation.Ordinal + 1,
                generation.Policy,
                AbsenceGenerationOpeningEventKind.TrustworthyPositiveObservation,
                cut.RunId,
                AbsenceHistoryGenerationCause.PresenceBreak));
        }

        refusal = AbsenceLedgerRefusal.None;
        return receipt;
    }

    /// <summary>
    /// The number of advancing absent complete cuts recorded under the exact current key tuple.
    /// </summary>
    public int CurrentStreakLength()
    {
        var key = CurrentGeneration.KeyProjection();
        return _receipts.Count(receipt =>
            receipt.Advanced &&
            string.Equals(receipt.KeyProjection, key, StringComparison.Ordinal));
    }

    /// <summary>What the ledger says about this subject right now.</summary>
    public AbsenceState State()
    {
        if (IsFrozenPendingReplacementReview())
        {
            return AbsenceState.FrozenPendingReplacementReview;
        }

        var streak = CurrentStreakLength();
        if (streak >= AbsenceTiming.AdvancingCutsRequired)
        {
            return AbsenceState.AbsentConfirmed;
        }

        if (streak >= 1)
        {
            return AbsenceState.AbsentUnconfirmed;
        }

        if (_receipts.Count > 0 && _receipts[^1].Observed)
        {
            return AbsenceState.Present;
        }

        return AbsenceState.NoEvidenceUnderCurrentGeneration;
    }

    private AbsenceAppendDisposition Decide(
        AbsenceCut cut,
        bool observed,
        bool complete,
        CutReceipt? precedingAdvancing)
    {
        if (observed)
        {
            return AbsenceAppendDisposition.PresenceBreakRecorded;
        }

        if (!complete)
        {
            return AbsenceAppendDisposition.PartialRunNoEffect;
        }

        if (IsFrozenPendingReplacementReview())
        {
            return AbsenceAppendDisposition.FrozenPendingReplacementReview;
        }

        if (precedingAdvancing is null)
        {
            return AbsenceAppendDisposition.StreakAdvanced;
        }

        var predecessor = precedingAdvancing.Cut;
        if (!predecessor.ClockSources().SequenceEqual(cut.ClockSources(), StringComparer.Ordinal))
        {
            // R3.3 refuses a clock-source change "outside policy" and no clock-source policy is
            // accepted anywhere this contract can cite. The provable narrowing is to refuse every
            // change, which errs toward not advancing absence and never toward advancing it.
            return AbsenceAppendDisposition.ClockSourceChanged;
        }

        // Both R3.3 conditions are written out because both are the rule, but only the second can
        // ever decide. EarliestPossibleStart is a minimum over the same observations CutStart takes
        // a minimum over, minus a non-negative skew, so it is never later than CutStart; and
        // LatestPossibleEnd is never earlier than CutEnd for the same reason. The second condition
        // therefore implies the first, and no fixture can fail one while passing the other. That is
        // stated here so nobody reads the first disjunct as separately tested; the two facts the
        // implication rests on are asserted in TheIntervalOfACutAlwaysContainsItsRawTimestampRange.
        // The nonoverlap requirement needs no third check either: these intervals are closed and
        // the floor is strictly positive, so a pair satisfying the second condition cannot overlap.
        // A separate overlap test could never fail here and would be false cover.
        // Only the interval condition is tested. The nominal one was here as a first disjunct and
        // is deleted: by the paragraph above it can never decide, so no fixture could reach it and
        // removing it changed no test, which is the proof it was dead rather than redundant.
        if (cut.EarliestPossibleStart() <
            predecessor.LatestPossibleEnd() + AbsenceTiming.MinimumSeparation)
        {
            return AbsenceAppendDisposition.SeparationFloorNotMet;
        }

        return AbsenceAppendDisposition.StreakAdvanced;
    }

    private CutReceipt? LastAdvancingReceipt(Generation generation) =>
        _receipts.LastOrDefault(receipt =>
            receipt.Advanced && receipt.GenerationId.Equals(generation.Id));

    private string? PrecedingCompleteCutId(Generation generation) =>
        _receipts.LastOrDefault(receipt =>
            receipt.GenerationId.Equals(generation.Id) &&
            receipt.Cut.Completion == AbsenceRunCompletion.EnumerationComplete)?.Cut.RunId;

    private string? PrecedingAbsentCutId(Generation generation) =>
        _receipts.LastOrDefault(receipt =>
            receipt.GenerationId.Equals(generation.Id) &&
            receipt.Cut.Completion == AbsenceRunCompletion.EnumerationComplete &&
            !receipt.Observed)?.Cut.RunId;

    /// <summary>
    /// One generation of a subject's absence history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constructor is private and the factory is internal, so no caller outside this assembly
    /// can mint a generation: the ledger is the only public route to one, and the ordinal that
    /// makes an identity unrepeatable is the ledger's to issue.
    /// </para>
    /// <para>
    /// Inside the assembly the factory is reachable, and that is stated rather than wished away.
    /// C# gives an enclosing type no access to a nested type's private members, so "only the ledger
    /// may call this" is not expressible in the language here. Visibility is not the control; the
    /// control is <c>AbsenceConstructionSurfaceTests</c>, which pins every producer of this type by
    /// exact signature and fails when a new one appears.
    /// </para>
    /// </remarks>
    public sealed class Generation
    {
        private readonly string _keyProjection;

        private Generation(
            AbsenceSubject subject,
            AbsenceHistoryGenerationId id,
            AbsenceHistoryGenerationId? predecessorId,
            int ordinal,
            AbsenceComparisonPolicy policy,
            AbsenceGenerationOpeningEventKind openingEventKind,
            string openingEventId,
            AbsenceHistoryGenerationCause cause)
        {
            Subject = subject;
            Id = id;
            PredecessorId = predecessorId;
            Ordinal = ordinal;
            Policy = policy;
            OpeningEventKind = openingEventKind;
            OpeningEventId = openingEventId;
            Cause = cause;
            _keyProjection = AbsenceKey.Projection(subject, id, policy);
        }

        public AbsenceSubject Subject { get; }

        public AbsenceHistoryGenerationId Id { get; }

        /// <summary>The immediate predecessor generation, or null for the first.</summary>
        public AbsenceHistoryGenerationId? PredecessorId { get; }

        /// <summary>Position in the subject's ledger, from one. Never reissued.</summary>
        public int Ordinal { get; }

        /// <summary>The complete comparison-policy tuple this generation binds.</summary>
        public AbsenceComparisonPolicy Policy { get; }

        public AbsenceGenerationOpeningEventKind OpeningEventKind { get; }

        public string OpeningEventId { get; }

        /// <summary>
        /// Exactly one cause, derived from the opening event and the predecessor. It is never a
        /// parameter of any public call, so a caller cannot label a policy change as a presence
        /// break or the reverse.
        /// </summary>
        public AbsenceHistoryGenerationCause Cause { get; }

        /// <summary>The complete fourteen member absence key in force under this generation.</summary>
        public string KeyProjection() => _keyProjection;

        internal static Generation Open(
            AbsenceSubject subject,
            Generation? predecessor,
            int ordinal,
            AbsenceComparisonPolicy policy,
            AbsenceGenerationOpeningEventKind openingEventKind,
            string openingEventId,
            AbsenceHistoryGenerationCause cause)
        {
            // Both preconditions of the identity factory are already established by every caller:
            // the ordinal starts at one and only increases, and the event identity was validated
            // before the ledger reached this point. A null here would be a defect in this class,
            // not a refusal a caller could act on, so it is raised rather than returned.
            var id = AbsenceHistoryGenerationId.TryCreate(subject, ordinal, openingEventId, out var refused)
                ?? throw new InvalidOperationException(
                    $"The ledger proposed an unmintable generation identity: {refused}.");

            return new Generation(
                subject, id, predecessor?.Id, ordinal, policy, openingEventKind, openingEventId, cause);
        }
    }

    /// <summary>
    /// The retained record of one appended cut, with the pointers R3.3 requires of it.
    /// </summary>
    /// <remarks>
    /// The pointers are computed by the ledger from what it already holds rather than supplied by
    /// the caller. A cut that names its own predecessor can name the wrong one, and the whole point
    /// of the three cut rule is that the chain is checkable.
    /// </remarks>
    public sealed class CutReceipt
    {
        internal CutReceipt(
            AbsenceCut cut,
            Generation generation,
            bool observed,
            bool advanced,
            AbsenceAppendDisposition disposition,
            string? precedingCompleteCutId,
            string? precedingAbsentCutId,
            string? precedingAdvancingCutId)
        {
            Cut = cut;
            GenerationId = generation.Id;
            KeyProjection = generation.KeyProjection();
            Observed = observed;
            Advanced = advanced;
            Disposition = disposition;
            PrecedingCompleteCutId = precedingCompleteCutId;
            PrecedingAbsentCutId = precedingAbsentCutId;
            PrecedingAdvancingCutId = precedingAdvancingCutId;
        }

        public AbsenceCut Cut { get; }

        /// <summary>The generation in force when this cut was appended.</summary>
        public AbsenceHistoryGenerationId GenerationId { get; }

        /// <summary>The exact fourteen member key this cut counted under, if it counted.</summary>
        public string KeyProjection { get; }

        /// <summary>True when the run positively observed the subject.</summary>
        public bool Observed { get; }

        /// <summary>True when this cut counted toward the current streak.</summary>
        public bool Advanced { get; }

        public AbsenceAppendDisposition Disposition { get; }

        /// <summary>
        /// The immediately preceding complete cut in this generation, present or absent, or null.
        /// </summary>
        public string? PrecedingCompleteCutId { get; }

        /// <summary>The immediately preceding absent complete cut in this generation, or null.</summary>
        public string? PrecedingAbsentCutId { get; }

        /// <summary>
        /// The cut the twenty hour floor was measured against, or null when this was the first
        /// advancing candidate of its generation. Named separately from
        /// <see cref="PrecedingAbsentCutId"/> because a complete absent cut that was refused for
        /// timing is still the preceding absent cut and is still not what the next floor uses.
        /// </summary>
        public string? PrecedingAdvancingCutId { get; }

        /// <summary>The retained complete scope-manifest identity and digest.</summary>
        public SourceArtifactRef ScopeManifestRef => Cut.ScopeManifestRef;

        /// <summary>The retained complete observed-set identity and digest.</summary>
        public SourceArtifactRef ObservedSetRef => Cut.ObservedSetRef;
    }
}
