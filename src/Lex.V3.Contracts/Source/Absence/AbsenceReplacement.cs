using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Absence;

/// <summary>What kind of publisher field a replacement coordinate is built from. Closed.</summary>
public enum AbsenceCoordinateFieldKind
{
    /// <summary>A stable publisher field.</summary>
    [JsonStringEnumMemberName("stable_publisher_field")]
    StablePublisherField = 1,

    /// <summary>A family rule.</summary>
    [JsonStringEnumMemberName("family_rule")]
    FamilyRule = 2,

    /// <summary>
    /// A publisher date. Admitted as a component, never as the whole coordinate: R3.3 states that
    /// a date alone is never a coordinate.
    /// </summary>
    [JsonStringEnumMemberName("publisher_date")]
    PublisherDate = 3,
}

/// <summary>One field of a frozen replacement-coordinate profile.</summary>
public readonly record struct AbsenceCoordinateField(string Name, AbsenceCoordinateFieldKind Kind);

/// <summary>Why a replacement-coordinate profile was refused. Closed.</summary>
public enum AbsenceReplacementCoordinateProfileRefusal
{
    /// <summary>No refusal: the profile was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The profile digest is not a SHA-256 value.</summary>
    [JsonStringEnumMemberName("profile_digest_not_sha256")]
    ProfileDigestNotSha256 = 1,

    /// <summary>The profile declares no field.</summary>
    [JsonStringEnumMemberName("fields_empty")]
    FieldsEmpty = 2,

    /// <summary>A field name is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("field_name_invalid")]
    FieldNameInvalid = 3,

    /// <summary>Two fields share a name.</summary>
    [JsonStringEnumMemberName("duplicate_field_name")]
    DuplicateFieldName = 4,

    /// <summary>A field kind is not a member of the closed vocabulary.</summary>
    [JsonStringEnumMemberName("field_kind_undefined")]
    FieldKindUndefined = 5,

    /// <summary>
    /// Every field is a publisher date, so the coordinate is a date and nothing else. R3.3 refuses
    /// that outright, because dates cluster and a date-only coordinate pairs unrelated identities.
    /// </summary>
    [JsonStringEnumMemberName("coordinate_is_date_alone")]
    CoordinateIsDateAlone = 6,
}

/// <summary>
/// A publisher's frozen replacement-coordinate profile.
/// </summary>
public sealed class AbsenceReplacementCoordinateProfile
{
    private AbsenceReplacementCoordinateProfile(
        string profileDigest,
        IReadOnlyList<AbsenceCoordinateField> fields)
    {
        ProfileDigest = profileDigest;
        Fields = fields;
    }

    public string ProfileDigest { get; }

    public IReadOnlyList<AbsenceCoordinateField> Fields { get; }

    /// <summary>The only path that mints a profile.</summary>
    public static AbsenceReplacementCoordinateProfile? TryCreate(
        string profileDigest,
        IReadOnlyList<AbsenceCoordinateField> fields,
        out AbsenceReplacementCoordinateProfileRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(profileDigest);
        ArgumentNullException.ThrowIfNull(fields);

        if (!AbsenceValidation.IsSha256(profileDigest))
        {
            refusal = AbsenceReplacementCoordinateProfileRefusal.ProfileDigestNotSha256;
            return null;
        }

        if (fields.Count == 0)
        {
            refusal = AbsenceReplacementCoordinateProfileRefusal.FieldsEmpty;
            return null;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!Enum.IsDefined(field.Kind))
            {
                refusal = AbsenceReplacementCoordinateProfileRefusal.FieldKindUndefined;
                return null;
            }

            if (!AbsenceValidation.IsIdentifier(field.Name))
            {
                refusal = AbsenceReplacementCoordinateProfileRefusal.FieldNameInvalid;
                return null;
            }

            if (!names.Add(field.Name))
            {
                refusal = AbsenceReplacementCoordinateProfileRefusal.DuplicateFieldName;
                return null;
            }
        }

        if (fields.All(static field => field.Kind == AbsenceCoordinateFieldKind.PublisherDate))
        {
            refusal = AbsenceReplacementCoordinateProfileRefusal.CoordinateIsDateAlone;
            return null;
        }

        refusal = AbsenceReplacementCoordinateProfileRefusal.None;
        return new AbsenceReplacementCoordinateProfile(profileDigest, fields.ToArray());
    }
}

/// <summary>The total classification of one changed coordinate. Closed.</summary>
public enum AbsenceReplacementDisposition
{
    /// <summary>A and B are both empty.</summary>
    [JsonStringEnumMemberName("coordinate_unchanged")]
    CoordinateUnchanged = 1,

    /// <summary>A is nonempty and B is empty. Each member of A proceeds to its own absence.</summary>
    [JsonStringEnumMemberName("ordinary_coordinate_disappearance")]
    OrdinaryCoordinateDisappearance = 2,

    /// <summary>A is empty and B is nonempty. No absence event is created.</summary>
    [JsonStringEnumMemberName("ordinary_coordinate_addition")]
    OrdinaryCoordinateAddition = 3,

    /// <summary>Exactly one out, exactly one in, nothing retained. Frozen pending review.</summary>
    [JsonStringEnumMemberName("replacement_candidate_one_to_one")]
    ReplacementCandidateOneToOne = 4,

    /// <summary>Every remaining case with both A and B nonempty. All counters frozen.</summary>
    [JsonStringEnumMemberName("replacement_collision_full_set")]
    ReplacementCollisionFullSet = 5,
}

/// <summary>What a classification says about one identity.</summary>
public enum AbsenceReplacementEffect
{
    /// <summary>The identity is in neither equivalence class, so this classification is silent.</summary>
    [JsonStringEnumMemberName("outside_this_coordinate")]
    OutsideThisCoordinate = 1,

    /// <summary>The identity may proceed to its own absence evaluation.</summary>
    [JsonStringEnumMemberName("may_proceed_to_absence")]
    MayProceedToAbsence = 2,

    /// <summary>The identity is retained or added, so no absence event arises from it.</summary>
    [JsonStringEnumMemberName("no_absence_event")]
    NoAbsenceEvent = 3,

    /// <summary>The identity's counters are frozen pending review.</summary>
    [JsonStringEnumMemberName("frozen_pending_review")]
    FrozenPendingReview = 4,
}

/// <summary>Why a classification was refused. Closed.</summary>
public enum AbsenceReplacementClassificationRefusal
{
    /// <summary>No refusal: the classification was produced.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>A cut identity is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("cut_id_invalid")]
    CutIdInvalid = 1,

    /// <summary>
    /// The two cut identities are the same. A coordinate compared against itself across one cut is
    /// not evidence of anything, and every disposition it could produce would be an artifact.
    /// </summary>
    [JsonStringEnumMemberName("cut_ids_identical")]
    CutIdsIdentical = 2,

    /// <summary>A member of an equivalence class is not an exact canonical publisher URI.</summary>
    [JsonStringEnumMemberName("class_member_invalid")]
    ClassMemberInvalid = 3,

    /// <summary>A member appears twice inside one equivalence class.</summary>
    [JsonStringEnumMemberName("duplicate_class_member")]
    DuplicateClassMember = 4,
}

/// <summary>
/// The complete classification of one changed replacement coordinate across two cuts.
/// </summary>
/// <remarks>
/// <para>
/// The disposition is computed from the complete old and new equivalence classes and can never be
/// supplied. R3.3 forbids greedy pairing, and a supplied disposition is the shape greedy pairing
/// takes in practice: someone looks at one disappearance beside one appearance and writes down
/// "replacement". Here the sets decide, so <c>O = {K}</c> with <c>N = {}</c> is an ordinary
/// disappearance whatever else changed in the same run, and K is never frozen for it.
/// </para>
/// <para>
/// Two conditions R3.3 states for the one-to-one case are deliberately not implemented as checks.
/// With <c>|A| = 1</c>, <c>|B| = 1</c> and <c>R</c> empty, A and B are disjoint by construction, so
/// "identities differ" cannot be false; and a mapping between two singletons is injective for the
/// same reason. Written as guards they would be unreachable code claiming coverage they do not
/// have, so the reason they hold is recorded here instead.
/// </para>
/// </remarks>
public sealed class AbsenceReplacementClassification
{
    private readonly HashSet<string> _old;
    private readonly HashSet<string> _new;
    private readonly HashSet<string> _gone;
    private readonly HashSet<string> _arrived;
    private readonly HashSet<string> _retained;

    private AbsenceReplacementClassification(
        AbsenceReplacementCoordinateProfile profile,
        string oldCutId,
        string newCutId,
        HashSet<string> oldClass,
        HashSet<string> newClass,
        HashSet<string> gone,
        HashSet<string> arrived,
        HashSet<string> retained,
        AbsenceReplacementDisposition disposition)
    {
        Profile = profile;
        OldCutId = oldCutId;
        NewCutId = newCutId;
        _old = oldClass;
        _new = newClass;
        _gone = gone;
        _arrived = arrived;
        _retained = retained;
        Disposition = disposition;
    }

    public AbsenceReplacementCoordinateProfile Profile { get; }

    public string OldCutId { get; }

    public string NewCutId { get; }

    public AbsenceReplacementDisposition Disposition { get; }

    /// <summary>The complete old equivalence class, ordinally sorted.</summary>
    public IReadOnlyList<string> OldClass() => Sorted(_old);

    /// <summary>The complete new equivalence class, ordinally sorted.</summary>
    public IReadOnlyList<string> NewClass() => Sorted(_new);

    /// <summary>A, the members of the old class absent from the new one.</summary>
    public IReadOnlyList<string> Gone() => Sorted(_gone);

    /// <summary>B, the members of the new class absent from the old one.</summary>
    public IReadOnlyList<string> Arrived() => Sorted(_arrived);

    /// <summary>R, the members present in both classes.</summary>
    public IReadOnlyList<string> Retained() => Sorted(_retained);

    /// <summary>The only path that produces a classification.</summary>
    public static AbsenceReplacementClassification? TryClassify(
        AbsenceReplacementCoordinateProfile profile,
        string oldCutId,
        string newCutId,
        IReadOnlyList<string> oldClass,
        IReadOnlyList<string> newClass,
        out AbsenceReplacementClassificationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(oldCutId);
        ArgumentNullException.ThrowIfNull(newCutId);
        ArgumentNullException.ThrowIfNull(oldClass);
        ArgumentNullException.ThrowIfNull(newClass);

        if (!AbsenceValidation.IsIdentifier(oldCutId) || !AbsenceValidation.IsIdentifier(newCutId))
        {
            refusal = AbsenceReplacementClassificationRefusal.CutIdInvalid;
            return null;
        }

        if (string.Equals(oldCutId, newCutId, StringComparison.Ordinal))
        {
            refusal = AbsenceReplacementClassificationRefusal.CutIdsIdentical;
            return null;
        }

        var oldSet = TryAsSet(oldClass, out refusal);
        if (oldSet is null)
        {
            return null;
        }

        var newSet = TryAsSet(newClass, out refusal);
        if (newSet is null)
        {
            return null;
        }

        var gone = new HashSet<string>(oldSet, StringComparer.Ordinal);
        gone.ExceptWith(newSet);
        var arrived = new HashSet<string>(newSet, StringComparer.Ordinal);
        arrived.ExceptWith(oldSet);
        var retained = new HashSet<string>(oldSet, StringComparer.Ordinal);
        retained.IntersectWith(newSet);

        var disposition = (gone.Count, arrived.Count) switch
        {
            (0, 0) => AbsenceReplacementDisposition.CoordinateUnchanged,
            (> 0, 0) => AbsenceReplacementDisposition.OrdinaryCoordinateDisappearance,
            (0, > 0) => AbsenceReplacementDisposition.OrdinaryCoordinateAddition,
            (1, 1) when retained.Count == 0 =>
                AbsenceReplacementDisposition.ReplacementCandidateOneToOne,
            _ => AbsenceReplacementDisposition.ReplacementCollisionFullSet,
        };

        refusal = AbsenceReplacementClassificationRefusal.None;
        return new AbsenceReplacementClassification(
            profile, oldCutId, newCutId, oldSet, newSet, gone, arrived, retained, disposition);
    }

    /// <summary>True when this classification freezes the counters of the identities it names.</summary>
    public bool FreezesAbsence() =>
        Disposition is AbsenceReplacementDisposition.ReplacementCandidateOneToOne
            or AbsenceReplacementDisposition.ReplacementCollisionFullSet;

    /// <summary>What this classification says about one identity.</summary>
    public AbsenceReplacementEffect EffectOn(string canonicalPublisherUri)
    {
        ArgumentNullException.ThrowIfNull(canonicalPublisherUri);
        if (!_old.Contains(canonicalPublisherUri) && !_new.Contains(canonicalPublisherUri))
        {
            return AbsenceReplacementEffect.OutsideThisCoordinate;
        }

        if (FreezesAbsence())
        {
            return AbsenceReplacementEffect.FrozenPendingReview;
        }

        return _gone.Contains(canonicalPublisherUri)
            ? AbsenceReplacementEffect.MayProceedToAbsence
            : AbsenceReplacementEffect.NoAbsenceEvent;
    }

    private static HashSet<string>? TryAsSet(
        IReadOnlyList<string> members,
        out AbsenceReplacementClassificationRefusal refusal)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            ArgumentNullException.ThrowIfNull(member);
            if (!AbsenceValidation.IsPublisherUri(member))
            {
                refusal = AbsenceReplacementClassificationRefusal.ClassMemberInvalid;
                return null;
            }

            if (!set.Add(member))
            {
                refusal = AbsenceReplacementClassificationRefusal.DuplicateClassMember;
                return null;
            }
        }

        refusal = AbsenceReplacementClassificationRefusal.None;
        return set;
    }

    private static IReadOnlyList<string> Sorted(HashSet<string> set) =>
        set.OrderBy(static member => member, StringComparer.Ordinal).ToArray();
}
