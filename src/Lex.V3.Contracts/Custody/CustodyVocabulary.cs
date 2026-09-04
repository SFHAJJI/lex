using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Custody;

public static class CustodySchemaIds
{
    public const string DurableBlobRef = "lex-v3-durable-blob-ref/1";
    public const string DurableBlobWriteReceipt = "lex-v3-durable-blob-write-receipt/2";
    public const string CustodyPolicyEvidence = "lex-v3-custody-policy-evidence/1";
}

/// <summary>
/// The protection lane requested for a held object. A lane name is intent; policy evidence proves
/// what protection was actually observed on the exact object.
/// </summary>
public enum CustodyClass
{
    /// <summary>A nightly transport body, held under a proven ninety-day floor.</summary>
    [JsonStringEnumMemberName("nightly_floor_90d")]
    NightlyFloor90d,

    /// <summary>Release evidence whose current physical copy must be under an active legal hold.</summary>
    [JsonStringEnumMemberName("legal_hold_evidence")]
    LegalHoldEvidence,
}

/// <summary>
/// The provider-neutral verification behavior that produced a policy observation.
/// </summary>
public enum CustodyVerificationProfile
{
    [JsonStringEnumMemberName("filesystem_unenforced/1")]
    FileSystemUnenforced1,

    [JsonStringEnumMemberName("immutable_object_store/1")]
    ImmutableObject1,
}

/// <summary>The retention control observed on the exact held object.</summary>
public enum CustodyProtection
{
    [JsonStringEnumMemberName("not_enforced")]
    NotEnforced,

    [JsonStringEnumMemberName("locked_time")]
    LockedTime,

    [JsonStringEnumMemberName("active_legal_hold")]
    ActiveLegalHold,
}

/// <summary>
/// Raised when a decode was attempted without durable custody of the bytes it would decode.
/// </summary>
public sealed class CustodyRequiredException : Exception
{
    public CustodyRequiredException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Raised when a store returns bytes that are not the bytes its content address names.
/// </summary>
public sealed class CustodyIntegrityException : Exception
{
    public CustodyIntegrityException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Raised when exact bytes exist but the observed protection does not satisfy its claimed lane.
/// </summary>
public sealed class CustodyPolicyException : Exception
{
    public CustodyPolicyException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// What a run can say about a dependency it holds. Decision 71.
/// </summary>
/// <remarks>
/// A read establishes presence and digest and says nothing about protection, so it is never
/// membership. A receipt says where the bytes are held; only the immutable-object profile
/// validates a class floor, and the filesystem adapter publishes no enforcement for any class, so
/// a filesystem deployment holds no floored member and must say so rather than claim durability.
/// </remarks>
public enum CustodyMembership
{
    /// <summary>
    /// No receipt, and so no custody claim: presence and digest without proof of where or how the
    /// bytes are held. <see cref="CustodyMembershipClassifier"/> never answers this value, because
    /// it classifies only a real write receipt and every write receipt implies one of the other two
    /// members; a repeated-enumeration executor that once verified evidence it read but did not
    /// retain was the reader this member described, and that read-without-write path was deleted.
    /// The member is reserved for a caller-supplied membership claim built some other way. Nothing
    /// in this codebase currently builds one: every consumer that receives a claimed membership
    /// (for example <c>Lex.V3.Contracts.Source.Core.RepeatedEnumerationReceiptRefusal
    /// .MembershipIsNotReceiptDerived</c>) refuses this value outright rather than accept it as a
    /// floor, because a membership that establishes no custody at all has no defensible floor
    /// answer.
    /// </summary>
    [JsonStringEnumMemberName("read_once")]
    ReadOnce = 0,

    /// <summary>Written and receipted, with the store enforcing no protection.</summary>
    [JsonStringEnumMemberName("retained_unenforced")]
    RetainedUnenforced = 1,

    /// <summary>Written and receipted under a profile that validates the class floor.</summary>
    [JsonStringEnumMemberName("floored")]
    Floored = 2,
}

/// <summary>
/// What a receipt establishes about where the bytes are held. The one rule, in one place, so a
/// reader and every acquisition session classify identically and cannot drift apart.
/// </summary>
/// <remarks>
/// Keyed on the observed protection rather than the profile that implies it: the policy-evidence
/// constructor already proved the class floor for every protection except <see
/// cref="CustodyProtection.NotEnforced"/> (see the switch in <see cref="CustodyPolicyEvidence"/>'s
/// constructor), so a verification profile added later cannot misclassify through this switch.
/// </remarks>
public static class CustodyMembershipClassifier
{
    public static CustodyMembership Classify(DurableBlobWriteReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return receipt.PolicyEvidence.Protection == CustodyProtection.NotEnforced
            ? CustodyMembership.RetainedUnenforced
            : CustodyMembership.Floored;
    }
}
