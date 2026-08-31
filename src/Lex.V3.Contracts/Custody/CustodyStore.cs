using System.Security.Cryptography;

namespace Lex.V3.Contracts.Custody;

/// <summary>
/// A create-only, content-addressed store for transport bytes.
/// </summary>
/// <remarks>
/// Create-only is the whole point. A store that can overwrite a content address can substitute
/// the bytes behind a name that every later reader resolves, and the name still matches its own
/// digest, so nothing downstream can tell. Implementations must refuse to replace an existing
/// object rather than replacing it quietly.
/// </remarks>
public interface ICustodyStore
{
    /// <summary>
    /// Holds the exact bytes and returns evidence. Creating an address that already holds the
    /// identical bytes is idempotent; creating one that holds different bytes is impossible for a
    /// correct store and must raise <see cref="CustodyIntegrityException"/> if observed.
    /// </summary>
    DurableBlobWriteReceipt Create(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass);
}

/// <summary>
/// The bound every create is checked against, locally, before any store is touched.
/// </summary>
public static class CustodyBounds
{
    /// <summary>256 MiB, the largest transport body this design admits.</summary>
    public const long MaxObjectBytes = 268_435_456;
}

public static class CustodyDigest
{
    public static string Of(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>
/// The property this package exists for: nothing decodes bytes that are not already held.
/// </summary>
/// <remarks>
/// <para>
/// The ordering is the contract. A decoder that runs first and stores afterwards loses the exact
/// transport bytes whenever it throws, which is precisely the case where they are needed, because
/// a body that cannot be decoded is the one whose original form has to be re-examined.
/// </para>
/// <para>
/// So the decoder is a callback rather than a caller. It is unreachable until a receipt exists,
/// and a test asserts it is never invoked when the store refuses, rather than asserting only that
/// the call throws.
/// </para>
/// </remarks>
public static class BytesBeforeDecode
{
    /// <param name="maxObjectBytes">
    /// The admitted bound, defaulting to <see cref="CustodyBounds.MaxObjectBytes"/>. It is a
    /// parameter so the refusal can be exercised without allocating a quarter of a gigabyte, which
    /// is the difference between a bound that is tested and one that is merely written down.
    /// </param>
    public static CustodiedDecode<T> Decode<T>(
        ReadOnlyMemory<byte> transportBytes,
        CustodyClass custodyClass,
        ICustodyStore store,
        Func<ReadOnlyMemory<byte>, T> decode,
        long maxObjectBytes = CustodyBounds.MaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(decode);

        if (maxObjectBytes <= 0 || maxObjectBytes > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxObjectBytes),
                "A caller may lower the admitted bound for a lane, never raise it.");
        }

        if (transportBytes.Length > maxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transportBytes),
                $"A transport body above {maxObjectBytes} bytes is refused before any store is touched.");
        }

        DurableBlobWriteReceipt receipt;
        try
        {
            receipt = store.Create(transportBytes, custodyClass);
        }
        catch (Exception exception)
            when (exception is not (CustodyRequiredException or OperationCanceledException))
        {
            // Cancellation is not a custody failure. Wrapping it would tell an operator the store
            // refused when the caller withdrew, and the two need different responses: one is an
            // incident and the other is a shutdown. Every other exception does mean the bytes may
            // not be held, so it fails closed and carries its cause.
            throw new CustodyRequiredException(
                "The transport bytes were not held, so nothing may decode them.", exception);
        }

        var expected = CustodyDigest.Of(transportBytes.Span);
        if (!string.Equals(receipt.Reference.ContentSha256, expected, StringComparison.Ordinal)
            || receipt.Reference.ByteLength != transportBytes.Length)
        {
            throw new CustodyIntegrityException(
                "The receipt does not describe the bytes that were presented.");
        }

        if (receipt.Reference.CustodyClass != custodyClass)
        {
            throw new CustodyIntegrityException(
                "The receipt holds the bytes under a different custody class than was requested.");
        }

        return new CustodiedDecode<T>(receipt, decode(transportBytes));
    }
}

/// <summary>A decoded value and the evidence that its bytes were held first.</summary>
public sealed record CustodiedDecode<T>(DurableBlobWriteReceipt Receipt, T Value);
