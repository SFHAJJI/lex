using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Custody;

public static class CustodySchemaIds
{
    public const string DurableBlobRef = "lex-v3-durable-blob-ref/1";
    public const string DurableBlobWriteReceipt = "lex-v3-durable-blob-write-receipt/1";
}

/// <summary>
/// What a held object is held under. The class decides the retention floor, not the caller.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CustodyClass>))]
public enum CustodyClass
{
    /// <summary>A nightly transport body, held under a proven ninety-day floor.</summary>
    [JsonStringEnumMemberName("nightly_floor_90d")]
    NightlyFloor90d,

    /// <summary>A body a release depends on, held indefinitely.</summary>
    [JsonStringEnumMemberName("evidence_indefinite")]
    EvidenceIndefinite,
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
    public CustodyIntegrityException(string message)
        : base(message)
    {
    }
}
