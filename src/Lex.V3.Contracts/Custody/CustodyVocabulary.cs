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
