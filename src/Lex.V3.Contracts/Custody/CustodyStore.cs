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
    /// Cancellation may be observed after a remote create committed. No receipt is issued in that
    /// call; a retry must read back the bytes and protection before reporting idempotent success.
    /// </summary>
    Task<DurableBlobWriteReceipt> CreateAsync(
        ReadOnlyMemory<byte> bytes,
        CustodyClass custodyClass,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores the exact bytes named by a durable reference. No bytes are returned until the
    /// implementation has verified both the declared length and content digest.
    /// </summary>
    Task<ReadOnlyMemory<byte>> ReadAsync(
        DurableBlobRef reference,
        CancellationToken cancellationToken);
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

    public static string Of(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int chunkSize = 64 * 1024;
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(bytes.Slice(offset, Math.Min(chunkSize, bytes.Length - offset)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>Canonical verification for every object restored from a custody adapter.</summary>
public static class CustodyRestore
{
    public static async Task<ReadOnlyMemory<byte>> ReadCheckedAsync(
        ICustodyStore store,
        DurableBlobRef reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlyMemory<byte> returned;
        try
        {
            returned = await store.ReadAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is not (CustodyRequiredException
                or CustodyIntegrityException
                or CustodyPolicyException))
        {
            throw new CustodyRequiredException(
                "The retained object could not be restored.", exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (returned.Length != reference.ByteLength)
        {
            throw new CustodyIntegrityException(
                "The restored length does not match its durable reference.");
        }

        // A store may return memory backed by a mutable provider buffer. Freeze once, then verify
        // and return only that exact copy so callers cannot observe bytes other than those hashed.
        var verified = returned.ToArray();
        if (!string.Equals(
                CustodyDigest.Of(verified, cancellationToken),
                reference.ContentSha256,
                StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                "The restored bytes do not match their durable reference.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return verified;
    }
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
    public static async Task<CustodiedDecode<T>> DecodeAsync<T>(
        ReadOnlyMemory<byte> transportBytes,
        CustodyClass custodyClass,
        ICustodyStore store,
        Func<ReadOnlyMemory<byte>, T> decode,
        long maxObjectBytes = CustodyBounds.MaxObjectBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(decode);

        if (maxObjectBytes <= 0 || maxObjectBytes > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxObjectBytes),
                "A caller may lower the admitted bound for a lane, never raise it.");
        }

        if (!Enum.IsDefined(custodyClass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(custodyClass), custodyClass,
                "An undefined custody class is refused before any store or decoder is reached.");
        }

        if (transportBytes.Length > maxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transportBytes),
                $"A transport body above {maxObjectBytes} bytes is refused before any store is touched.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The caller may still own the backing array. Everything below verifies a digest and then
        // hands the same memory to an untrusted callback, so without a copy the bytes that were
        // held, the bytes that were hashed and the bytes that were decoded are three claims about
        // memory somebody else can still write to. One bounded copy makes them one claim.
        var frozen = new ReadOnlyMemory<byte>(transportBytes.ToArray());

        DurableBlobWriteReceipt receipt;
        try
        {
            receipt = await store.CreateAsync(frozen, custodyClass, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is not (CustodyRequiredException
                or CustodyIntegrityException
                or CustodyPolicyException))
        {
            // Only a signalled caller token proves withdrawal. A provider timeout may also throw
            // OperationCanceledException while that token remains live, and is unavailability.
            // Integrity and policy contradictions retain their distinct incident types.
            throw new CustodyRequiredException(
                "The transport bytes were not held, so nothing may decode them.", exception);
        }

        if (receipt is null)
        {
            throw new CustodyIntegrityException("The custody store returned no write receipt.");
        }

        var expected = CustodyDigest.Of(frozen.Span, cancellationToken);
        if (!string.Equals(receipt.Reference.ContentSha256, expected, StringComparison.Ordinal)
            || receipt.Reference.ByteLength != frozen.Length)
        {
            throw new CustodyIntegrityException(
                "The receipt does not describe the bytes that were presented.");
        }

        if (receipt.Reference.CustodyClass != custodyClass)
        {
            throw new CustodyIntegrityException(
                "The receipt holds the bytes under a different custody class than was requested.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new CustodiedDecode<T>(receipt, decode(frozen));
    }
}

/// <summary>A decoded value and the evidence that its bytes were held first.</summary>
public sealed record CustodiedDecode<T>(DurableBlobWriteReceipt Receipt, T Value);
