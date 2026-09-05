using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Absence;

/// <summary>Why a stable absence subject was refused. Closed.</summary>
public enum AbsenceSubjectRefusal
{
    /// <summary>No refusal: the subject was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The publisher authority is not a declared member.</summary>
    [JsonStringEnumMemberName("publisher_undefined")]
    PublisherUndefined = 1,

    /// <summary>The canonical publisher URI is not an exact absolute publisher identity.</summary>
    [JsonStringEnumMemberName("canonical_publisher_uri_invalid")]
    CanonicalPublisherUriInvalid = 2,

    /// <summary>The parent identity uses a different entity-kind registry from the child.</summary>
    [JsonStringEnumMemberName("parent_registry_mismatch")]
    ParentRegistryMismatch = 3,

    /// <summary>The parent identity is the subject itself.</summary>
    [JsonStringEnumMemberName("parent_is_self")]
    ParentIsSelf = 4,
}

/// <summary>
/// The stable absence subject: publisher, entity kind, canonical publisher URI, parent or null.
/// </summary>
/// <remarks>
/// <para>
/// R3.3 names exactly these four members, and this type carries exactly them. In particular it
/// carries no configuration digest and no generation, because the subject is the thing whose
/// history survives every configuration change; a subject that moved when a query digest moved
/// would make the append-only ledger unaddressable across the transition it exists to record.
/// </para>
/// <para>
/// Membership is decided on the canonical publisher URI because R1 defines the complete observed
/// root set of a cut as every canonical publisher URI the frozen root definition returns. No
/// second key projection is introduced here.
/// </para>
/// </remarks>
public sealed class AbsenceSubject
{
    private AbsenceSubject(
        SourceAuthority publisher,
        SourceRegistryMemberRef entityKind,
        string canonicalPublisherUri,
        SourceObjectKeyRef? parentIdentity)
    {
        Publisher = publisher;
        EntityKind = entityKind;
        CanonicalPublisherUri = canonicalPublisherUri;
        ParentIdentity = parentIdentity;
    }

    public SourceAuthority Publisher { get; }

    public SourceRegistryMemberRef EntityKind { get; }

    public string CanonicalPublisherUri { get; }

    /// <summary>The parent identity, or null for a subject that has no parent.</summary>
    public SourceObjectKeyRef? ParentIdentity { get; }

    /// <summary>
    /// The only path that mints a subject. Returns null with a typed refusal, because an
    /// unaddressable subject is a reviewable input rather than a programming error.
    /// </summary>
    public static AbsenceSubject? TryCreate(
        SourceAuthority publisher,
        SourceRegistryMemberRef entityKind,
        string canonicalPublisherUri,
        SourceObjectKeyRef? parentIdentity,
        out AbsenceSubjectRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(entityKind);
        ArgumentNullException.ThrowIfNull(canonicalPublisherUri);

        if (!Enum.IsDefined(publisher))
        {
            refusal = AbsenceSubjectRefusal.PublisherUndefined;
            return null;
        }

        if (!AbsenceValidation.IsPublisherUri(canonicalPublisherUri))
        {
            refusal = AbsenceSubjectRefusal.CanonicalPublisherUriInvalid;
            return null;
        }

        if (parentIdentity is not null)
        {
            if (parentIdentity.EntityKind.RegistryRef != entityKind.RegistryRef)
            {
                refusal = AbsenceSubjectRefusal.ParentRegistryMismatch;
                return null;
            }

            if (parentIdentity.EntityKind == entityKind &&
                string.Equals(parentIdentity.PublisherUri, canonicalPublisherUri, StringComparison.Ordinal))
            {
                refusal = AbsenceSubjectRefusal.ParentIsSelf;
                return null;
            }
        }

        refusal = AbsenceSubjectRefusal.None;
        return new AbsenceSubject(publisher, entityKind, canonicalPublisherUri, parentIdentity);
    }

    /// <summary>
    /// The four subject members of the absence key tuple, one per line, in R3.3's order.
    /// </summary>
    public string CanonicalProjection() =>
        string.Join('\n',
        [
            "publisher=" + ContractWire.NameOf(Publisher),
            "entity_kind=" + Describe(EntityKind),
            "canonical_publisher_uri=" + CanonicalPublisherUri,
            "parent_identity_or_null=" + (ParentIdentity is null
                ? "null"
                : Describe(ParentIdentity.EntityKind)
                    + "|" + ParentIdentity.PublisherUri
                    + "|" + ParentIdentity.CanonicalKeySha256),
        ]);

    /// <summary>The subject digest. Used as an address, never as a generation identity.</summary>
    public string Sha256() => AbsenceDigest.Of(CanonicalProjection());

    private static string Describe(SourceRegistryMemberRef reference) =>
        reference.RegistryRef.ResourceId + "|" + reference.RegistryRef.Sha256 + "|" + reference.MemberKey;
}

/// <summary>
/// The nine configuration identities R3.3 lists in the absence key beside the subject and the
/// generation. Closed, and total: a policy must decide every member.
/// </summary>
public enum AbsenceComparisonPolicyMember
{
    [JsonStringEnumMemberName("root_definition_digest")]
    RootDefinitionDigest = 1,

    [JsonStringEnumMemberName("applicable_scope_policy_digest")]
    ApplicableScopePolicyDigest = 2,

    [JsonStringEnumMemberName("discovery_query_digest")]
    DiscoveryQueryDigest = 3,

    [JsonStringEnumMemberName("selection_query_digest")]
    SelectionQueryDigest = 4,

    [JsonStringEnumMemberName("adapter_digest")]
    AdapterDigest = 5,

    [JsonStringEnumMemberName("request_policy_digest")]
    RequestPolicyDigest = 6,

    [JsonStringEnumMemberName("execution_policy_digest")]
    ExecutionPolicyDigest = 7,

    [JsonStringEnumMemberName("robots_policy_profile_digest")]
    RobotsPolicyProfileDigest = 8,

    [JsonStringEnumMemberName("replacement_coordinate_profile_digest")]
    ReplacementCoordinateProfileDigest = 9,
}

/// <summary>
/// One decided member of the comparison-policy tuple.
/// </summary>
/// <remarks>
/// A plain carrier with no invariant of its own, and deliberately so: the digest of a single member
/// is not a validity question, because what makes a comparison policy usable is that every member
/// is decided exactly once. That question belongs to <see cref="AbsenceComparisonPolicy"/>, and
/// splitting it across two types would let a caller assemble nine individually valid rows into an
/// unusable tuple.
/// </remarks>
public readonly record struct AbsenceComparisonPolicyDigest(
    AbsenceComparisonPolicyMember Member,
    string Sha256);

/// <summary>Why a comparison policy was refused. Closed.</summary>
public enum AbsenceComparisonPolicyRefusal
{
    /// <summary>No refusal: the policy was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>Two rows decide the same member.</summary>
    [JsonStringEnumMemberName("duplicate_member")]
    DuplicateMember = 1,

    /// <summary>A member of the closed tuple has no row.</summary>
    [JsonStringEnumMemberName("member_undecided")]
    MemberUndecided = 2,

    /// <summary>A row's value is not a SHA-256 digest.</summary>
    [JsonStringEnumMemberName("digest_not_sha256")]
    DigestNotSha256 = 3,

    /// <summary>A row names a member outside the closed tuple.</summary>
    [JsonStringEnumMemberName("member_undefined")]
    MemberUndefined = 4,
}

/// <summary>
/// The complete comparison-policy tuple for one subject.
/// </summary>
/// <remarks>
/// <para>
/// Totality is enforced against <see cref="Enum.GetValues{TEnum}"/> rather than a written count of
/// nine. A tenth configuration identity added to the vocabulary makes every previously complete
/// policy refuse as <see cref="AbsenceComparisonPolicyRefusal.MemberUndecided"/> until someone
/// decides it, which is the only safe direction: a comparison tuple that silently ignores a new
/// dimension would compare two cuts as identical across a change it cannot see, and that is
/// precisely how an absence advances through a configuration transition it should have reset.
/// </para>
/// <para>
/// The full scope manifest digest and the cut-specific observed-set digest are deliberately not
/// members. R3.3 keeps them as evidence and excludes them from comparability, so that an ordinary
/// membership change elsewhere in the corpus cannot reset an unrelated subject's history.
/// </para>
/// </remarks>
public sealed class AbsenceComparisonPolicy
{
    private readonly Dictionary<AbsenceComparisonPolicyMember, string> _digests;
    private readonly string _canonicalProjection;

    private AbsenceComparisonPolicy(
        Dictionary<AbsenceComparisonPolicyMember, string> digests,
        string canonicalProjection)
    {
        _digests = digests;
        _canonicalProjection = canonicalProjection;
    }

    /// <summary>The decided digest for one member. Present for every member.</summary>
    public string For(AbsenceComparisonPolicyMember member) =>
        _digests[ContractValidation.RequireDefined(member, nameof(member))];

    /// <summary>
    /// The only path that mints a comparison policy.
    /// </summary>
    public static AbsenceComparisonPolicy? TryCreate(
        IReadOnlyList<AbsenceComparisonPolicyDigest> rows,
        out AbsenceComparisonPolicyRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var byMember = new Dictionary<AbsenceComparisonPolicyMember, string>();
        foreach (var row in rows)
        {
            if (!Enum.IsDefined(row.Member))
            {
                refusal = AbsenceComparisonPolicyRefusal.MemberUndefined;
                return null;
            }

            if (!AbsenceValidation.IsSha256(row.Sha256))
            {
                refusal = AbsenceComparisonPolicyRefusal.DigestNotSha256;
                return null;
            }

            if (!byMember.TryAdd(row.Member, row.Sha256))
            {
                refusal = AbsenceComparisonPolicyRefusal.DuplicateMember;
                return null;
            }
        }

        foreach (var member in Enum.GetValues<AbsenceComparisonPolicyMember>())
        {
            if (!byMember.ContainsKey(member))
            {
                refusal = AbsenceComparisonPolicyRefusal.MemberUndecided;
                return null;
            }
        }

        var projection = string.Join('\n', Enum.GetValues<AbsenceComparisonPolicyMember>()
            .Select(member => ContractWire.NameOf(member) + "=" + byMember[member]));

        refusal = AbsenceComparisonPolicyRefusal.None;
        return new AbsenceComparisonPolicy(byMember, projection);
    }

    /// <summary>The nine members, one per line, in the vocabulary's declared order.</summary>
    public string CanonicalProjection() => _canonicalProjection;

    /// <summary>
    /// True when every member of the tuple carries the same digest. This is the comparison R3.3
    /// means by "byte-identical prior configuration": two policies that agree here are the same
    /// configuration, and returning to one is exactly the A to B to A case the ledger must not
    /// let reconnect a streak.
    /// </summary>
    public bool SameConfigurationAs(AbsenceComparisonPolicy other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(_canonicalProjection, other._canonicalProjection, StringComparison.Ordinal);
    }
}

/// <summary>Why a generation identity was refused. Closed.</summary>
public enum AbsenceHistoryGenerationIdRefusal
{
    /// <summary>No refusal: the identity was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The ordinal is not a positive number.</summary>
    [JsonStringEnumMemberName("ordinal_not_positive")]
    OrdinalNotPositive = 1,

    /// <summary>The opening event identity is not a bounded identifier.</summary>
    [JsonStringEnumMemberName("opening_event_id_invalid")]
    OpeningEventIdInvalid = 2,
}

/// <summary>
/// A generation identity: unique within its subject, and never a function of the configuration.
/// </summary>
/// <remarks>
/// <para>
/// R3.3 requires a generation ID to be unique and "never content-addressed solely by configuration
/// digests". That requirement is met structurally here rather than by a check: no comparison policy
/// is an input to this factory, so no policy value can influence the identity it produces. A return
/// to a byte-identical earlier configuration cannot reproduce an earlier identity, because the
/// ordinal that separates them comes from the subject's own append-only ledger and only increases.
/// </para>
/// <para>
/// A test pins the factory's parameter list for that reason. The property is invisible in a value
/// comparison, since the absence of an input cannot be observed by feeding inputs.
/// </para>
/// </remarks>
public sealed class AbsenceHistoryGenerationId : IEquatable<AbsenceHistoryGenerationId>
{
    private AbsenceHistoryGenerationId(string value)
    {
        Value = value;
    }

    /// <summary>The exact identity string carried in receipts and in the absence key tuple.</summary>
    public string Value { get; }

    /// <summary>
    /// The only path that mints a generation identity. The ordinal belongs to the ledger.
    /// </summary>
    public static AbsenceHistoryGenerationId? TryCreate(
        AbsenceSubject subject,
        int ordinal,
        string openingEventId,
        out AbsenceHistoryGenerationIdRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(openingEventId);

        if (ordinal < 1)
        {
            refusal = AbsenceHistoryGenerationIdRefusal.OrdinalNotPositive;
            return null;
        }

        if (!AbsenceValidation.IsIdentifier(openingEventId))
        {
            refusal = AbsenceHistoryGenerationIdRefusal.OpeningEventIdInvalid;
            return null;
        }

        refusal = AbsenceHistoryGenerationIdRefusal.None;
        return new AbsenceHistoryGenerationId(
            "absence_history_generation/1:"
            + subject.Sha256()
            + ":" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + openingEventId);
    }

    public bool Equals(AbsenceHistoryGenerationId? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as AbsenceHistoryGenerationId);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

/// <summary>
/// The complete fourteen-member absence key of R3.3.
/// </summary>
/// <remarks>
/// A key is never assembled from parts by a caller. It is projected from a generation, which
/// already binds its subject and its comparison policy, so a key whose generation disagrees with
/// its policy is not a state this contract can represent and needs no guard against.
/// </remarks>
public static class AbsenceKey
{
    /// <summary>The number of members R3.3 lists in the immutable absence key tuple.</summary>
    public const int MemberCount = 14;

    /// <summary>
    /// The fourteen members, one per line: the four subject members, the generation identity, then
    /// the nine configuration identities in vocabulary order.
    /// </summary>
    public static string Projection(
        AbsenceSubject subject,
        AbsenceHistoryGenerationId generationId,
        AbsenceComparisonPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(generationId);
        ArgumentNullException.ThrowIfNull(policy);

        return subject.CanonicalProjection()
            + "\nabsence_history_generation_id=" + generationId.Value
            + "\n" + policy.CanonicalProjection();
    }
}

/// <summary>Canonical SHA-256 over a canonical projection.</summary>
internal static class AbsenceDigest
{
    public static string Of(string canonicalText)
    {
        ArgumentNullException.ThrowIfNull(canonicalText);
        return Convert.ToHexStringLower(
            SHA256.HashData(AbsenceValidation.CanonicalBytes(canonicalText)));
    }
}
