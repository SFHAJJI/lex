using System.Text;

namespace Lex.V3.Contracts.Custody;

/// <summary>
/// The one definition of a write receipt's own digest: the lowercase SHA-256 of the receipt's
/// exact canonical JSON bytes.
/// </summary>
/// <remarks>
/// Decision 80's receipt gate depends on every party computing this identically. Before this type
/// existed the same expression was written out separately at
/// <c>RoutedHttpAcquisitionSession.HoldAsync</c> (the session that first mints a hop's claimed
/// <c>RoutedHttpHop.DurableWriteReceiptSha256</c>), at <c>RoutedHttpEvidence.Create</c>'s door check
/// (which recomputes it from a caller-supplied receipt to refuse one that does not reproduce the
/// hop's claim), and at <c>RepeatedEnumerationDeliveryProof</c>'s independent cross-check between a
/// resolver's separately returned <see cref="DurableBlobWriteReceipt"/> and the hop it is offered
/// for. Three independent reimplementations of one hash is exactly how they drift; this type lives
/// in Contracts, not Ingest, because Core must reach it too and Core cannot depend on Ingest.
/// </remarks>
public static class DurableBlobWriteReceiptDigest
{
    /// <summary>The receipt's exact canonical UTF-8 JSON bytes.</summary>
    public static byte[] CanonicalBytes(DurableBlobWriteReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return Encoding.UTF8.GetBytes(ContractJson.Serialize(receipt));
    }

    /// <summary>The receipt's canonical bytes together with their own lowercase SHA-256.</summary>
    public static (byte[] Bytes, string Sha256) Canonicalize(DurableBlobWriteReceipt receipt)
    {
        var bytes = CanonicalBytes(receipt);
        return (bytes, CustodyDigest.Of(bytes));
    }

    /// <summary>
    /// The lowercase SHA-256 a hop's claimed durable write receipt digest must reproduce.
    /// </summary>
    public static string Of(DurableBlobWriteReceipt receipt) => CustodyDigest.Of(CanonicalBytes(receipt));
}
