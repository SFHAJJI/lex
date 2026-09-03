using System.Text;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Absence;

/// <summary>
/// Why a generation was opened. Exactly one cause per generation, per R3.3.
/// </summary>
/// <remarks>
/// There is no <c>None</c> member. A generation with no cause is not a state this vocabulary can
/// describe: the ledger derives the cause from the opening event and refuses the event outright
/// when no cause applies, so an unset cause would be false coverage rather than a real value.
/// </remarks>
public enum AbsenceHistoryGenerationCause
{
    /// <summary>The first generation for a stable subject. Its predecessor is null.</summary>
    [JsonStringEnumMemberName("initial_tracking")]
    InitialTracking = 1,

    /// <summary>Any member of the comparison-policy tuple changed for this subject.</summary>
    [JsonStringEnumMemberName("comparison_policy_changed")]
    ComparisonPolicyChanged = 2,

    /// <summary>A trustworthy positive observation closed every open streak for the subject.</summary>
    [JsonStringEnumMemberName("presence_break")]
    PresenceBreak = 3,
}

/// <summary>
/// The event that opens a generation. Closed, because the cause is derived from it.
/// </summary>
public enum AbsenceGenerationOpeningEventKind
{
    /// <summary>The subject entered the ledger. Valid only for the first generation.</summary>
    [JsonStringEnumMemberName("tracking_started")]
    TrackingStarted = 1,

    /// <summary>A member of the comparison-policy tuple applicable to this subject changed.</summary>
    [JsonStringEnumMemberName("comparison_policy_transition")]
    ComparisonPolicyTransition = 2,

    /// <summary>A trustworthy fresh positive observation of the exact subject key.</summary>
    [JsonStringEnumMemberName("trustworthy_positive_observation")]
    TrustworthyPositiveObservation = 3,
}

/// <summary>
/// Which complete set decides membership for a target, per R3.3's root and non-root split.
/// </summary>
public enum AbsenceApplicableSet
{
    /// <summary>A root target key, decided by the cut's complete observed root set.</summary>
    [JsonStringEnumMemberName("observed_root_set")]
    ObservedRootSet = 1,

    /// <summary>A non-root target key, decided by its complete normalized family set.</summary>
    [JsonStringEnumMemberName("normalized_family_set")]
    NormalizedFamilySet = 2,
}

/// <summary>
/// Whether a run enumerated its applicable set completely. Only a complete run can advance absence.
/// </summary>
public enum AbsenceRunCompletion
{
    /// <summary>Every applicable family was enumerated completely under the completion gates.</summary>
    [JsonStringEnumMemberName("enumeration_complete")]
    EnumerationComplete = 1,

    /// <summary>
    /// A run that did not complete. Its positives are still trustworthy and still break a streak;
    /// its silences prove nothing, because an unenumerated key is not an absent key.
    /// </summary>
    [JsonStringEnumMemberName("partial")]
    Partial = 2,
}

/// <summary>
/// How an observation reached the cut. R3.3 admits only the first member into a cut.
/// </summary>
/// <remarks>
/// This is typed rather than a boolean "freshly executed" flag. A boolean records the writer's
/// conclusion and loses the reason, so a rejected observation cannot say what it was; the four
/// insufficient kinds are the four R3.3 names, kept as values so a refusal is a statement.
/// </remarks>
public enum AbsenceObservationProvenance
{
    /// <summary>A successfully completed current family response executed for this run.</summary>
    [JsonStringEnumMemberName("freshly_executed")]
    FreshlyExecuted = 1,

    /// <summary>A fresh envelope around an earlier observation.</summary>
    [JsonStringEnumMemberName("wrapper_around_earlier_observation")]
    WrapperAroundEarlierObservation = 2,

    /// <summary>A replay from a cache rather than a current response.</summary>
    [JsonStringEnumMemberName("cache_replay")]
    CacheReplay = 3,

    /// <summary>A row carried forward from an earlier response.</summary>
    [JsonStringEnumMemberName("stale_row")]
    StaleRow = 4,

    /// <summary>A row from a response that did not complete.</summary>
    [JsonStringEnumMemberName("incomplete_row")]
    IncompleteRow = 5,
}

/// <summary>
/// The declared precision of an observation timestamp. Closed, and each member has an exact width.
/// </summary>
/// <remarks>
/// Precision is declared, not inferred, but it is also checked: a value carrying digits finer than
/// its declared unit is refused. R3.3 forbids identifiers from compensating for insufficient
/// precision, so an unchecked declaration would be the one place the 20 hour floor could be
/// satisfied by arithmetic on a number that never had that resolution.
/// </remarks>
public enum AbsenceTimestampPrecision
{
    [JsonStringEnumMemberName("hour")]
    Hour = 1,

    [JsonStringEnumMemberName("minute")]
    Minute = 2,

    [JsonStringEnumMemberName("second")]
    Second = 3,

    [JsonStringEnumMemberName("millisecond")]
    Millisecond = 4,

    [JsonStringEnumMemberName("microsecond")]
    Microsecond = 5,
}

/// <summary>
/// What the ledger says about a subject right now, including Decision 20's two absence states.
/// </summary>
public enum AbsenceState
{
    /// <summary>
    /// No absent cut has been counted under the current generation and the newest retained receipt
    /// is not a positive observation. Named for exactly that, because a subject in this state may
    /// well carry advancing receipts under an earlier generation which no longer compare.
    /// </summary>
    [JsonStringEnumMemberName("no_evidence_under_current_generation")]
    NoEvidenceUnderCurrentGeneration = 1,

    /// <summary>The newest retained receipt observed the subject.</summary>
    [JsonStringEnumMemberName("present")]
    Present = 2,

    /// <summary>
    /// Decision 20's surfaced state from the first miss: one or two advancing absent complete cuts
    /// under the current generation.
    /// </summary>
    [JsonStringEnumMemberName("absent_unconfirmed")]
    AbsentUnconfirmed = 3,

    /// <summary>
    /// Decision 20's three completed runs of absence, reached under one generation and one tuple.
    /// </summary>
    [JsonStringEnumMemberName("absent_confirmed")]
    AbsentConfirmed = 4,

    /// <summary>
    /// Replacement detection froze this subject. R3.3 runs replacement before absence, so a frozen
    /// subject cannot advance whatever its cuts say.
    /// </summary>
    [JsonStringEnumMemberName("frozen_pending_replacement_review")]
    FrozenPendingReplacementReview = 5,
}

/// <summary>
/// Constants R3.3 states as exact numbers rather than as policy.
/// </summary>
public static class AbsenceTiming
{
    /// <summary>
    /// The floor between two consecutive advancing absent cuts. Three advancing cuts therefore span
    /// at least forty hours from the first cut end to the third cut start.
    /// </summary>
    public static readonly TimeSpan MinimumSeparation = TimeSpan.FromHours(20);

    /// <summary>
    /// The number of advancing absent complete cuts absence needs. Decision 20.
    /// </summary>
    /// <remarks>
    /// A field rather than a const, so that a test comparing it with the literal three is a real
    /// runtime comparison. As a const the compiler folded both sides and the MSTest analyzer
    /// correctly refused the assertion as one whose condition is always true.
    /// </remarks>
    public static readonly int AdvancingCutsRequired = 3;

    /// <summary>The exact width of one declared precision unit.</summary>
    public static TimeSpan WidthOf(AbsenceTimestampPrecision precision) =>
        ContractValidation.RequireDefined(precision, nameof(precision)) switch
        {
            AbsenceTimestampPrecision.Hour => TimeSpan.FromHours(1),
            AbsenceTimestampPrecision.Minute => TimeSpan.FromMinutes(1),
            AbsenceTimestampPrecision.Second => TimeSpan.FromSeconds(1),
            AbsenceTimestampPrecision.Millisecond => TimeSpan.FromMilliseconds(1),
            AbsenceTimestampPrecision.Microsecond => TimeSpan.FromTicks(10),
            _ => throw new ArgumentOutOfRangeException(nameof(precision)),
        };
}

/// <summary>
/// The exact wire token of a closed absence vocabulary member.
/// </summary>
/// <remarks>
/// Canonical projections are built from these tokens rather than from CLR member names, so the
/// text a digest covers is the text the wire carries. Reading the attribute rather than restating
/// the tokens in a switch means a member added without a token fails loudly at its first use
/// instead of silently projecting its CLR spelling.
/// </remarks>
public static class AbsenceWire
{
    public static string NameOf<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        ContractValidation.RequireDefined(value, nameof(value));
        var name = Enum.GetName(value)
            ?? throw new ArgumentOutOfRangeException(nameof(value));
        var field = typeof(TEnum).GetField(name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new ArgumentOutOfRangeException(nameof(value));
        var token = field
            .GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), inherit: false)
            .OfType<JsonStringEnumMemberNameAttribute>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"{typeof(TEnum).Name}.{name} carries no wire token.");
        return token.Name;
    }
}

/// <summary>
/// Non-throwing adoptions of the Source.Core identity rules, plus the canonical text encoding.
/// </summary>
/// <remarks>
/// <para>
/// The rules themselves are not restated here. <c>SourceCoreValidation</c> owns what a SHA-256
/// value, a publisher URI and a bounded identifier are, and a second copy of those rules in this
/// namespace would drift the first time one of them is tightened. These wrappers convert its
/// throwing shape into the answer a typed refusal needs, and nothing else.
/// </para>
/// <para>
/// Every caller checks for null before calling, so the only <see cref="ArgumentException"/> these
/// can observe is the validation refusal they exist to convert.
/// </para>
/// </remarks>
internal static class AbsenceValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static bool IsSha256(string? value) =>
        value is not null && Accepts(() => Core.SourceCoreValidation.RequireSha256(value, nameof(value)));

    public static bool IsPublisherUri(string? value) =>
        value is not null && Accepts(() => Core.SourceCoreValidation.RequirePublisherUri(value, nameof(value)));

    public static bool IsIdentifier(string? value) =>
        value is not null && Accepts(() => ContractValidation.RequireIdentifier(value, nameof(value)));

    /// <summary>Strict UTF-8 bytes of a canonical projection, for digesting.</summary>
    public static byte[] CanonicalBytes(string canonicalText) => StrictUtf8.GetBytes(canonicalText);

    private static bool Accepts(Action rule)
    {
        try
        {
            rule();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
