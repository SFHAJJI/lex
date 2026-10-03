using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFirstMountAcquisition
{
    /// <summary>
    /// Reopens a complete retained EU population for a new mixed acquisition. Only the rights
    /// notice is fetched again, once for this build; the original population observations remain
    /// historical. All checkpoint inputs and the current document-fetch renderer bytes verify
    /// before the first publisher request.
    /// </summary>
    /// <remarks>
    /// A population acquired across an interruption keeps saying so: the renewed catalog carries the
    /// reused catalog's resumption forward. A resumed build that reuses a population without one (the
    /// catalog its interrupted run journaled, or a retained catalog it names) records its own, so a
    /// mount it builds never reads as observed in one window.
    /// </remarks>
    public async Task<EuFirstMountAcquisitionResult> ReuseAsync(SourceArtifactRef checkpoint,
        IReadOnlyList<string> expectedSeeds, ReadOnlyMemory<byte> currentDocumentFetchSource,
        WireRequestBudget wireBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedSeeds);
        ArgumentNullException.ThrowIfNull(wireBudget);
        var currentRendererSha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(currentDocumentFetchSource.Span));
        var seeds = expectedSeeds.Order(StringComparer.Ordinal).ToArray();
        var original = await ReopenAsync(_custodyStore, checkpoint, seeds, cancellationToken).ConfigureAwait(false);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        var catalog = ReadCatalog(bytes.Span, out _);
        var sources = await EuRendererSources.FromCustodyAsync(_custodyStore,
            catalog.Renderers.ToDictionary(static role => role.File, static role => role.Reference, StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(currentRendererSha256, sources.DocumentFetch.Reference.Sha256, StringComparison.Ordinal))
            throw new CustodyIntegrityException("The current document-fetch renderer source differs from the retained EU renderer; reuse cannot send a new rights request under historical source bytes.");
        var rights = await new EuLegalNoticeRouteProducer(_custodyStore, _timeProvider, _testHandlerOverride)
            .RunAsync(catalog.CorpusRun, sources.DocumentFetch, wireBudget, cancellationToken).ConfigureAwait(false);
        if (rights.Route is null)
            return EuFirstMountAcquisitionResult.Refused(EuFirstMountAcquisitionRefusal.LegalNoticeRefused,
                $"{rights.Refusal}: {rights.Detail}", original.Run, original.Formex);
        try
        {
            var journaled = _replay?.Catalog == checkpoint;
            var resumption = original.Resumption ?? _resume?.Summarize(
                journaled
                    ? new[] { new AcquisitionResumedPhase(AcquisitionJournal.EuropeCatalogPhase, 1, 0) }
                    : Array.Empty<AcquisitionResumedPhase>(),
                wireBudget.Spent, _timeProvider.GetUtcNow());
            var renewed = await RetainAcquisitionAsync(seeds, sources, original.Run!, original.Formex!, rights,
                resumption, cancellationToken).ConfigureAwait(false);
            if (_journal is not null)
                await _journal.AppendAsync(AcquisitionJournal.EuropeCatalogPhase, CatalogKey,
                    AcquisitionJournal.Payload(renewed), [renewed.Sha256]).ConfigureAwait(false);
            return EuFirstMountAcquisitionResult.Success(original.Run!, original.Formex!, rights).WithCheckpoint(renewed, resumption);
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return EuFirstMountAcquisitionResult.Refused(EuFirstMountAcquisitionRefusal.AcquisitionCheckpointNotRetained,
                exception.Message, original.Run, original.Formex);
        }
    }
}
