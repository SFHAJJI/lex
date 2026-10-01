using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuCorrigendumTripwireProducer
{
    private const string CheckpointSchema = "lex-eu-tripwire-production-checkpoint/1";

    /// <summary>Rebuilds the original tripwire and lineage from its checked expression pairing without publisher traffic.</summary>
    public static async Task<EuCorrigendumTripwireProductionResult> ReopenAsync(
        ICustodyStore store, SourceArtifactRef checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<TripwireCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Schema != CheckpointSchema || !bytes.Span.SequenceEqual(EncodeCheckpoint(document)))
                throw new CustodyIntegrityException("Tripwire checkpoint framing disagrees.");
            var (expressions, expressionFacts, objectFacts) = await EuLanguageScopedExpressionProducer
                .ReopenWithDeliveriesAsync(store, document.Expressions, cancellationToken).ConfigureAwait(false);
            if (RefuseUnlessDelivered(expressions) is { } refused) return refused;
            if (objectFacts is null)
                return EuCorrigendumTripwireProductionResult.Refused(
                    EuCorrigendumTripwireProductionRefusal.ObjectFactsRequestRequired,
                    "The retained expression production asked for no object-facts family.", expressions, 0);
            var set = EuCorrigendumTripwireSet.TryDerive(expressionFacts!, objectFacts,
                out var refusal, out var detail, out var offendingIri);
            if (set is null) return RefuseFold(expressions, refusal, detail, offendingIri);
            var producer = new EuCorrigendumTripwireProducer(store, TimeProvider.System);
            return await producer.RetainAsync(expressions, set, cancellationToken, document, checkpoint).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Tripwire checkpoint failed independent restoration.", exception);
        }
    }

    private async Task<SourceArtifactRef> RetainCheckpointAsync(TripwireCheckpoint document, CancellationToken cancellationToken)
    {
        var bytes = EncodeCheckpoint(document);
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Tripwire checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Tripwire checkpoint receipt names different bytes.");
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
    }

    private static byte[] EncodeCheckpoint(TripwireCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record TripwireCheckpoint(string Schema, SourceArtifactRef Expressions, string CanonicalSha256, string LineageSha256);
}
