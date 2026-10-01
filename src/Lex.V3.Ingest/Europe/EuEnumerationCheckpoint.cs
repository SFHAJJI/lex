using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// Retains the inputs to one EU two-pass comparison and rechecks them offline. A checkpoint is
/// neither an acquisition completion nor a custody-floor claim. Its reader has no HTTP dependency.
/// </summary>
public static class EuEnumerationCheckpoint
{
    private const string Schema = "lex-eu-enumeration-checkpoint/1";

    public static async Task<SourceArtifactRef> WriteAsync(
        ICustodyStore store,
        EnumerationDeliveryComparison delivery,
        RepeatedEnumerationInterpretationProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(profile);
        RequireEurope(profile);
        RepeatedEnumerationInterpretationProfileIdentity.Validate(delivery.InterpretationProfileRef, profile);
        var document = new Document(Schema, profile, delivery.InterpretationProfileRef,
            delivery.RunIdentity, delivery.SourceProfileRef, delivery.PartitionKey,
            delivery.CountA, delivery.PagesA, delivery.CountB, delivery.PagesB);
        var bytes = Encode(document);
        var (receipt, failure) = await CustodyHold.TryHoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Checkpoint custody did not receipt the written bytes.");
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}",
            receipt.Reference.ContentSha256);
    }

    /// <summary>
    /// Reads all count/page artifacts by digest, replays the retained request bytes through the
    /// existing binder, and runs the existing complete two-pass comparison. Expected identities
    /// come from the containing acquisition checkpoint, never from an unverified success flag.
    /// </summary>
    public static async Task<EnumerationDeliveryComparison> OpenAsync(
        ICustodyStore store,
        SourceArtifactRef checkpoint,
        SourceArtifactRef expectedRun,
        SourceArtifactRef expectedInterpretationProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedRun);
        ArgumentNullException.ThrowIfNull(expectedInterpretationProfile);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<Document>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != Schema || !bytes.Span.SequenceEqual(Encode(document)) ||
                document.Run != expectedRun || document.ProfileRef != expectedInterpretationProfile)
                throw new CustodyIntegrityException("Enumeration checkpoint framing or expected identity disagrees.");
            if (document.PagesA.Pages.Any(p => p is null || p.Evidence is null) ||
                document.PagesB.Pages.Any(p => p is null || p.Evidence is null))
                throw new CustodyIntegrityException("Enumeration checkpoint contains an empty page reference.");
            RequireEurope(document.Profile);
            RepeatedEnumerationInterpretationProfileIdentity.Validate(document.ProfileRef, document.Profile);
            var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
            var resolved = new Dictionary<RepeatedEnumerationEvidenceRefs, RepeatedEnumerationResolvedEvidence>();
            var references = new[] { document.CountA }.Concat(document.PagesA.Pages.Select(p => p.Evidence))
                .Append(document.CountB).Concat(document.PagesB.Pages.Select(p => p.Evidence));
            foreach (var reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (resolved.ContainsKey(reference))
                    throw new CustodyIntegrityException("Enumeration checkpoint repeats an observation.");
                var evidence = await glue.ReopenPageEvidenceAsync(reference, cancellationToken).ConfigureAwait(false);
                var body = await CustodyRestore.ReadByDigestCheckedAsync(
                    store, evidence.LogicalRequest.Body.Sha256, cancellationToken).ConfigureAwait(false);
                if ((ulong)body.Length != evidence.LogicalRequest.Body.Length)
                    throw new CustodyIntegrityException("Retained query body length disagrees with its logical request.");
                resolved.Add(reference, evidence with
                {
                    Renderer = new EuReplayRenderer(evidence.QueryPlan.RendererProfileRef,
                        evidence.QueryPlan.RendererSourceRef, evidence.LogicalRequest.Uri, body.ToArray()),
                });
            }
            var comparison = EnumerationDeliveryComparison.Create(document.Profile, document.ProfileRef,
                document.CountA, document.PagesA, document.CountB, document.PagesB, new Resolver(resolved));
            if (comparison.RunIdentity != expectedRun || comparison.SourceProfileRef != document.SourceProfile ||
                comparison.PartitionKey != document.PartitionKey)
                throw new CustodyIntegrityException("Recomputed enumeration identity disagrees with its checkpoint.");
            cancellationToken.ThrowIfCancellationRequested();
            return comparison;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Retained enumeration checkpoint did not pass independent verification.", exception);
        }
    }

    /// <summary>
    /// Reconstructs the receipt through the existing checked factory, using current custody receipts
    /// for every dependency. Idempotent holds may write local custody; they never contact a publisher.
    /// The resulting floor describes the reopened store, not an archived assertion about retention.
    /// </summary>
    public static async Task<RepeatedEnumerationDeliveryReceipt> RestoreReceiptAsync(
        ICustodyStore store,
        SourceArtifactRef checkpoint,
        SourceArtifactRef expectedRun,
        SourceArtifactRef expectedInterpretationProfile,
        CancellationToken cancellationToken)
    {
        var comparison = await OpenAsync(store, checkpoint, expectedRun, expectedInterpretationProfile,
            cancellationToken).ConfigureAwait(false);
        return await RepeatedEnumerationReceiptRestore.RestoreAsync(store, comparison, cancellationToken)
            .ConfigureAwait(false);
    }
    private static byte[] Encode(Document document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));

    private static void RequireEurope(RepeatedEnumerationInterpretationProfile profile)
    {
        if (profile.Dialect != RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso)
            throw new ArgumentException("This checkpoint uses the EU retained-request replay contract.", nameof(profile));
    }

    private sealed record Document(string Schema, RepeatedEnumerationInterpretationProfile Profile,
        SourceArtifactRef ProfileRef, SourceArtifactRef Run, SourceArtifactRef SourceProfile, string PartitionKey,
        RepeatedEnumerationEvidenceRefs CountA, EnumerationPageSetRefs PagesA,
        RepeatedEnumerationEvidenceRefs CountB, EnumerationPageSetRefs PagesB);

    private sealed class Resolver(
        IReadOnlyDictionary<RepeatedEnumerationEvidenceRefs, RepeatedEnumerationResolvedEvidence> evidence)
        : IRepeatedEnumerationEvidenceResolver
    {
        public RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs references) => evidence[references];
    }
}
