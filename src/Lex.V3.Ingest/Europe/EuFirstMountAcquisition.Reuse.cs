using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFirstMountAcquisition
{
    /// <summary>
    /// Reopens a complete retained EU population for a new mixed acquisition. Only the rights
    /// notice is fetched again, once for this build; the original population observations remain
    /// historical. All checkpoint inputs verify before the first publisher request.
    /// </summary>
    public async Task<EuFirstMountAcquisitionResult> ReuseAsync(SourceArtifactRef checkpoint,
        IReadOnlyList<string> expectedSeeds, WireRequestBudget wireBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedSeeds);
        ArgumentNullException.ThrowIfNull(wireBudget);
        var seeds = expectedSeeds.Order(StringComparer.Ordinal).ToArray();
        var original = await ReopenAsync(_custodyStore, checkpoint, seeds, cancellationToken).ConfigureAwait(false);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        var catalog = ContractJson.Deserialize<AcquisitionCatalog>(new UTF8Encoding(false, true).GetString(bytes.Span));
        var sources = await EuRendererSources.FromCustodyAsync(_custodyStore,
            catalog.Renderers.ToDictionary(static role => role.File, static role => role.Reference, StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
        var rights = await new EuLegalNoticeRouteProducer(_custodyStore, _timeProvider, _testHandlerOverride)
            .RunAsync(catalog.CorpusRun, sources.DocumentFetch, wireBudget, cancellationToken).ConfigureAwait(false);
        if (rights.Route is null)
            return EuFirstMountAcquisitionResult.Refused(EuFirstMountAcquisitionRefusal.LegalNoticeRefused,
                $"{rights.Refusal}: {rights.Detail}", original.Run, original.Formex);
        try
        {
            var renewed = await RetainAcquisitionAsync(seeds, sources, original.Run!, original.Formex!, rights,
                cancellationToken).ConfigureAwait(false);
            return EuFirstMountAcquisitionResult.Success(original.Run!, original.Formex!, rights).WithCheckpoint(renewed);
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return EuFirstMountAcquisitionResult.Refused(EuFirstMountAcquisitionRefusal.AcquisitionCheckpointNotRetained,
                exception.Message, original.Run, original.Formex);
        }
    }
}
