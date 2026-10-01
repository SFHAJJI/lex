using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest;

/// <summary>Restores membership from actual current holds after a complete delivery comparison.</summary>
internal static class RepeatedEnumerationReceiptRestore
{
    internal static async Task<RepeatedEnumerationDeliveryReceipt> RestoreAsync(
        ICustodyStore store, EnumerationDeliveryComparison comparison, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(comparison);
        var membership = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
        var observations = new List<RepeatedEnumerationObservationCustody>();
        var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
        var references = new[] { comparison.CountA }.Concat(comparison.PagesA.Pages.Select(p => p.Evidence))
            .Append(comparison.CountB).Concat(comparison.PagesB.Pages.Select(p => p.Evidence));
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = await glue.ReopenPageEvidenceAsync(reference, cancellationToken).ConfigureAwait(false);
            var terminal = evidence.HttpEvidence.Hops[0];
            foreach (var digest in new[] { reference.QueryPlanRef.Sha256, reference.QueryInputRef.Sha256,
                reference.RenderReceiptRef.Sha256, reference.LogicalRequestRef.Sha256,
                reference.HttpEvidenceRef.Sha256, terminal.DurableWriteReceiptSha256, terminal.Sha256 })
            {
                if (membership.ContainsKey(digest)) continue;
                var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, digest, cancellationToken)
                    .ConfigureAwait(false);
                var (receipt, failure) = await CustodyHold.TryHoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
                if (receipt is null) throw new CustodyRequiredException("Reopened enumeration hold refused: " + failure);
                if (receipt.Reference.ContentSha256 != digest || receipt.Reference.ByteLength != bytes.Length)
                    throw new CustodyIntegrityException("Reopened custody receipt names different bytes.");
                membership.Add(digest, CustodyMembershipClassifier.Classify(receipt));
            }
            observations.Add(new RepeatedEnumerationObservationCustody(reference, terminal.Sha256,
                membership[terminal.Sha256], terminal.DurableWriteReceiptSha256));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return RepeatedEnumerationDeliveryReceipt.TryCreate(comparison, membership, membership, observations,
            out var refusal) ?? throw new CustodyIntegrityException("Reopened enumeration receipt refused: " + refusal);
    }
}
