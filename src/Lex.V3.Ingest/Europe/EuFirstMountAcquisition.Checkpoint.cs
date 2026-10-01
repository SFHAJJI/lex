using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFirstMountAcquisition
{
    private const string AcquisitionSchema = "lex-eu-first-mount-acquisition/1";

    /// <summary>
    /// Restores one query run and supplies that same instance to Formex reconciliation. The caller
    /// pins the retained catalog and its original sorted seed scope. No publisher session is opened.
    /// </summary>
    public static async Task<EuFirstMountAcquisitionResult> ReopenAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, IReadOnlyList<string> expectedSeeds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedSeeds);
        var seeds = expectedSeeds.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<AcquisitionCatalog>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != AcquisitionSchema || !bytes.Span.SequenceEqual(EncodeAcquisition(document)) ||
                document.Seeds is null || document.Query is null || document.Formex is null || document.Rights is null ||
                document.CorpusRun is null || document.Renderers is null || document.Renderers.Length != EuRendererSources.RendererFiles.Count ||
                document.Renderers.Any(static role => role is null || role.Reference is null) ||
                document.Seeds.Length == 0 || document.Seeds.Any(string.IsNullOrWhiteSpace) ||
                !document.Seeds.SequenceEqual(document.Seeds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !seeds.SequenceEqual(document.Seeds, StringComparer.Ordinal) ||
                !document.Renderers.Select(static role => role.File).SequenceEqual(EuRendererSources.RendererFiles, StringComparer.Ordinal))
                throw new CustodyIntegrityException("EU acquisition catalog framing or requested scope disagrees.");
            var sources = await EuRendererSources.FromCustodyAsync(store,
                document.Renderers.ToDictionary(static role => role.File, static role => role.Reference, StringComparer.Ordinal),
                cancellationToken).ConfigureAwait(false);
            await EuQueryExecutionAdapter.VerifyRendererBindingsAsync(store, document.Query, sources, cancellationToken).ConfigureAwait(false);
            var run = await EuQueryExecutionAdapter.ReopenAsync(store, document.Query, seeds, cancellationToken).ConfigureAwait(false);
            var records = run.CorpusRecordSet?.Set.Records;
            if (records is null || records.Count == 0 || records.Any(record => record.RunIdentity != document.CorpusRun))
                throw new CustodyIntegrityException("EU acquisition catalog names a different corpus run.");
            var formex = await EuFormexPackagePopulationProducer.ReopenAsync(store, document.Formex, run,
                sources.FormexManifestation, sources.DocumentFetch, workCelex: null, cancellationToken).ConfigureAwait(false);
            var rights = await EuLegalNoticeRouteProducer.ReopenAsync(store, document.Rights, document.CorpusRun,
                cancellationToken).ConfigureAwait(false);
            if (!formex.Delivered || !ReferenceEquals(formex.Reconciliation?.Run, run) || rights.Route is null)
                throw new CustodyIntegrityException("EU acquisition components did not restore their original association: " + rights.Detail);
            return EuFirstMountAcquisitionResult.Success(run, formex, rights).WithCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("EU acquisition catalog failed independent verification.", exception);
        }
    }

    private async Task<SourceArtifactRef> RetainAcquisitionAsync(string[] seeds, EuRendererSources sources,
        EuQueryExecutionResult run, EuFormexPackagePopulationResult formex, EuLegalNoticeRouteResult rights,
        CancellationToken cancellationToken)
    {
        if (run.AcquisitionCheckpointRef is null || formex.CheckpointRef is null || rights.Route is null ||
            !ReferenceEquals(formex.Reconciliation?.Run, run))
            throw new CustodyIntegrityException("A successful EU acquisition omitted its checked component association.");
        var all = new[] { sources.Census, sources.ObjectFacts, sources.Witness, sources.DocumentFetch,
            sources.FormexManifestation, sources.LegalNotice };
        // Retain even the legacy legal-notice role, which the current producer does not execute.
        // This completes the source map without manufacturing an observation of that renderer.
        foreach (var renderer in all)
        {
            var sourceBytes = renderer.CopyBytes();
            var (held, failure) = await CustodyHold.TryHoldAsync(_custodyStore, sourceBytes, cancellationToken).ConfigureAwait(false);
            if (held is null) throw new CustodyRequiredException("EU acquisition source retention refused: " + failure);
            if (held.Reference.ContentSha256 != renderer.Reference.Sha256 || held.Reference.ByteLength != sourceBytes.Length)
                throw new CustodyIntegrityException("EU acquisition source receipt names different bytes.");
        }
        var records = run.CorpusRecordSet!.Set.Records;
        var corpusRun = records[0].RunIdentity;
        if (records.Any(record => record.RunIdentity != corpusRun) || rights.Route.RunIdentity != corpusRun)
            throw new CustodyIntegrityException("EU acquisition rights and records belong to different runs.");
        var routeBytes = rights.Route.CopyCanonicalBytes();
        var routeDigest = CustodyDigest.Of(routeBytes);
        var heldRoute = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, routeDigest, cancellationToken).ConfigureAwait(false);
        if (!heldRoute.Span.SequenceEqual(routeBytes))
            throw new CustodyIntegrityException("EU acquisition rights route was not retained exactly.");
        var document = new AcquisitionCatalog(AcquisitionSchema, seeds.ToArray(), run.AcquisitionCheckpointRef,
            formex.CheckpointRef, new SourceArtifactRef(NewUrn(), routeDigest), corpusRun,
            all.Select((renderer, index) => new RendererRole(EuRendererSources.RendererFiles[index], renderer.Reference)).ToArray());
        var bytes = EncodeAcquisition(document);
        var (receipt, detail) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("EU acquisition catalog retention refused: " + detail);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("EU acquisition catalog receipt names different bytes.");
        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }

    private static byte[] EncodeAcquisition(AcquisitionCatalog document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record AcquisitionCatalog(string Schema, string[] Seeds, SourceArtifactRef Query,
        SourceArtifactRef Formex, SourceArtifactRef Rights, SourceArtifactRef CorpusRun, RendererRole[] Renderers);
    private sealed record RendererRole(string File, SourceArtifactRef Reference);
}
