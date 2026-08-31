using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Custody;

/// <summary>
/// A reference to durably held transport bytes, addressed by content and by nothing else.
/// </summary>
/// <remarks>
/// There is deliberately no account, container, bucket, region, URL or path field here, and a
/// test asserts that no member of a serialised custody document is named like one. A capability
/// contract that carries a provider locator has chosen a provider, and this one has not: the
/// physical store is selected where it is configured, not where the bytes are described.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DurableBlobRef
{
    [JsonConstructor]
    public DurableBlobRef(
        string schema,
        string contentSha256,
        long byteLength,
        CustodyClass custodyClass)
    {
        if (!string.Equals(schema, CustodySchemaIds.DurableBlobRef, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A durable blob reference must declare {CustodySchemaIds.DurableBlobRef}.",
                nameof(schema));
        }

        if (!IsLowercaseSha256(contentSha256))
        {
            throw new ArgumentException(
                "A content address must be a lowercase 64 character SHA-256.",
                nameof(contentSha256));
        }

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength), "A held object cannot have a negative length.");
        }

        Schema = schema;
        ContentSha256 = contentSha256;
        ByteLength = byteLength;
        CustodyClass = custodyClass;
    }

    public string Schema { get; }

    public string ContentSha256 { get; }

    public long ByteLength { get; }

    public CustodyClass CustodyClass { get; }

    /// <summary>
    /// Uppercase hexadecimal is refused rather than folded. Two spellings of one address are two
    /// object names in a content-addressed store, and the store cannot tell they are the same.
    /// </summary>
    internal static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}

/// <summary>
/// Evidence that exact bytes were held before anything read them.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DurableBlobWriteReceipt
{
    [JsonConstructor]
    public DurableBlobWriteReceipt(string schema, DurableBlobRef reference, DateTimeOffset writtenAt)
    {
        if (!string.Equals(schema, CustodySchemaIds.DurableBlobWriteReceipt, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A write receipt must declare {CustodySchemaIds.DurableBlobWriteReceipt}.",
                nameof(schema));
        }

        ArgumentNullException.ThrowIfNull(reference);

        if (writtenAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A write instant must be expressed in UTC.", nameof(writtenAt));
        }

        Schema = schema;
        Reference = reference;
        WrittenAt = writtenAt;
    }

    public string Schema { get; }

    public DurableBlobRef Reference { get; }

    public DateTimeOffset WrittenAt { get; }
}
