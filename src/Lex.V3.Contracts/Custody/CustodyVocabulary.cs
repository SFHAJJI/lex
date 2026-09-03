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
    /// Reopened by digest and verified. No receipt, and so no custody claim. The session produces
    /// none of these, because every reopen it performs now goes through the retaining path; the
    /// member exists for a reader that opens a digest without writing it, which is what the
    /// repeated-enumeration executor does when it verifies evidence it did not retain.
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
