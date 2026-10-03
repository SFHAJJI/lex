using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFirstMountAcquisition
{
    private const string AdapterKey = "query";
    private const string CatalogKey = "europe";

    /// <summary>
    /// Reopens, before any request, everything the interrupted run's journal names for the EU half, each unit through its
    /// own checked reader, and refuses a journal that does not describe this acquisition.
    /// </summary>
    /// <returns>
    /// The EU catalog the interrupted run retained, which the caller reuses through <see cref="ReuseAsync"/> (that reopen
    /// is its admission); null when <see cref="RunAsync(IReadOnlyList{string}, EuRendererSources, WireRequestBudget, CancellationToken)"/>
    /// acquires the population, replaying what this reopened and acquiring the rest live.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The journaled adapter run reopens through <see cref="EuQueryExecutionAdapter.ReopenAsync"/>, after its renderer
    /// bindings are held to the renderer sources this run reopened from the journal, and keeps the interrupted run's
    /// identity: every journaled Formex unit names it. The Formex units then reopen through
    /// <see cref="EuFormexPackagePopulationProducer.PrepareResumeAsync"/>, which refuses any unit the population walk
    /// would not reach or that belongs to another run's input.
    /// </para>
    /// <para>
    /// A journaled catalog covers the adapter run and the Formex units journaled before it. They are admitted through
    /// the catalog's own reopen instead, but only when they are the catalog's: its adapter run must be the journaled
    /// one, and a retained catalog this invocation names (<paramref name="retainedCheckpoint"/>) must hold the same
    /// population. A run that reused a retained population journals no EU unit but its catalog, so with
    /// <paramref name="retainedCheckpoint"/> any other EU unit refuses.
    /// </para>
    /// <para>
    /// Throws <see cref="CustodyIntegrityException"/> or <see cref="CustodyRequiredException"/> on any fault; nothing has
    /// been requested from a publisher when it does.
    /// </para>
    /// </remarks>
    public async Task<SourceArtifactRef?> PrepareResumeAsync(IReadOnlyList<string> celexes, EuRendererSources? rendererSources,
        SourceArtifactRef? retainedCheckpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(celexes);
        var resume = _resume ?? throw new InvalidOperationException("Only a resumed acquisition prepares a replay.");
        if (_replay is not null) throw new InvalidOperationException("A resumed acquisition prepares its replay once.");
        var seeds = celexes.Order(StringComparer.Ordinal).ToArray();
        try
        {
            if (resume.TryTake(AcquisitionJournal.EuropeCatalogPhase, CatalogKey, out var catalogPayload))
            {
                var journaled = ContractJson.Deserialize<SourceArtifactRef>(catalogPayload.GetRawText());
                var catalogBytes = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, journaled.Sha256, cancellationToken)
                    .ConfigureAwait(false);
                var catalog = ReadCatalog(catalogBytes.Span, out _);
                if (resume.TryTake(AcquisitionJournal.EuropeAdapterPhase, AdapterKey, out var adapterPayload) &&
                    ContractJson.Deserialize<SourceArtifactRef>(adapterPayload.GetRawText()) != catalog.Query)
                    throw new CustodyIntegrityException("The journaled EU catalog names another adapter run than the journal does.");
                if (retainedCheckpoint is not null)
                {
                    var retainedBytes = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, retainedCheckpoint.Sha256,
                        cancellationToken).ConfigureAwait(false);
                    var retained = ReadCatalog(retainedBytes.Span, out _);
                    if (retained.Query != catalog.Query || retained.Formex != catalog.Formex || retained.CorpusRun != catalog.CorpusRun)
                        throw new CustodyIntegrityException("The journaled EU catalog holds another population than the retained catalog this build reuses.");
                }

                resume.Settle(AcquisitionJournal.EuropeFormexEnumerationPhase);
                resume.Settle(AcquisitionJournal.EuropeFormexPackagePhase);
                _replay = new ResumeReplay(null, new EuFormexPopulationProgress(_journal), journaled);
                return journaled;
            }

            var progress = new EuFormexPopulationProgress(_journal);
            EuQueryExecutionResult? run = null;
            if (retainedCheckpoint is null && rendererSources is not null &&
                resume.TryTake(AcquisitionJournal.EuropeAdapterPhase, AdapterKey, out var queryPayload))
            {
                var query = ContractJson.Deserialize<SourceArtifactRef>(queryPayload.GetRawText());
                await EuQueryExecutionAdapter.VerifyRendererBindingsAsync(_custodyStore, query, rendererSources, cancellationToken)
                    .ConfigureAwait(false);
                run = await EuQueryExecutionAdapter.ReopenAsync(_custodyStore, query, seeds, cancellationToken).ConfigureAwait(false);
                await EuFormexPackagePopulationProducer.PrepareResumeAsync(_custodyStore, run, rendererSources.DocumentFetch,
                    resume, progress, cancellationToken).ConfigureAwait(false);
            }

            // What is left untaken describes another acquisition: an adapter run beside a reused population, or a Formex
            // unit without the adapter run it belongs to.
            resume.RequireTaken(AcquisitionJournal.EuropeAdapterPhase, AcquisitionJournal.EuropeFormexEnumerationPhase,
                AcquisitionJournal.EuropeFormexPackagePhase);
            _replay = new ResumeReplay(run, progress, null);
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("A journaled EU unit does not read.", exception);
        }
    }

    /// <summary>
    /// What a resumed acquisition reopened before its first request: the journaled adapter run, the Formex units the
    /// population walk takes, and the journaled catalog when the interrupted run got that far.
    /// </summary>
    private sealed class ResumeReplay(EuQueryExecutionResult? run, EuFormexPopulationProgress formex, SourceArtifactRef? catalog)
    {
        internal EuQueryExecutionResult? Run { get; } = run;

        internal EuFormexPopulationProgress Formex { get; } = formex;

        internal SourceArtifactRef? Catalog { get; } = catalog;
    }
}
