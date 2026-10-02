using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest;

/// <summary>Preserves historical receipt identity only after the current hold verifies the same bytes and membership.</summary>
internal static class RetainedCustodyReceipt
{
    internal static async Task<string> HoldAsync(ICustodyStore store, DurableBlobWriteReceipt original,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(original));
        var (held, failure) = await CustodyHold.TryHoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
        if (held is null) throw new CustodyRequiredException("Original receipt cannot be retained: " + failure);
        var digest = CustodyDigest.Of(bytes);
        if (held.Reference.ContentSha256 != digest || held.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Retained receipt bytes differ from their hold.");
        return digest;
    }

    internal static async Task<DurableBlobWriteReceipt> ReopenAsync(ICustodyStore store, string digest,
        DurableBlobWriteReceipt current, CancellationToken cancellationToken)
    {
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, digest, cancellationToken).ConfigureAwait(false);
        try
        {
            var original = ContractJson.Deserialize<DurableBlobWriteReceipt>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (!bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(original))) ||
                original.Reference.ContentSha256 != current.Reference.ContentSha256 || original.Reference.ByteLength != current.Reference.ByteLength ||
                CustodyMembershipClassifier.Classify(original) != CustodyMembershipClassifier.Classify(current))
                throw new CustodyIntegrityException("Original receipt differs from the current verified object or membership.");
            return original;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Original receipt cannot be independently verified.", exception);
        }
    }
}
