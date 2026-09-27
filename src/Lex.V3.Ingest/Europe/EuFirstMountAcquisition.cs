using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// The six renderer-source artifacts an EU acquisition binds its requests through, one per renderer
/// this assembly's Europe producers send with. Each names the exact bytes of the source file that
/// holds the renderer, retained in the run's own custody, so a reopened request can be traced to the
/// code that rendered it (Decision 75: a run holds what it depends on).
/// </summary>
/// <remarks>
/// Until this type, every renderer source in the repository was minted by a test fixture from
/// placeholder bytes, and the one live Luxembourg canary read the renderer's file from the checkout.
/// <see cref="FromCheckoutAsync"/> is that canary's pattern made production: it reads the six files
/// named in <see cref="RendererFiles"/> from a checkout root, holds each in custody and opens it.
/// A deployed build without a checkout will need the same bytes from another carrier (an embedded
/// resource is the obvious one); that is a release-pipeline concern, recorded in STATUS.md.
/// </remarks>
public sealed class EuRendererSources
{
    /// <summary>The renderer files, relative to the checkout root, in the order the constructor takes them.</summary>
    public static IReadOnlyList<string> RendererFiles { get; } = Array.AsReadOnly(new[]
    {
        "src/Lex.V3.Contracts/Source/Europe/EuConsolidationDiscovery.cs",
        "src/Lex.V3.Contracts/Source/Europe/EuObjectFactsDiscoveryPlan.cs",
        "src/Lex.V3.Contracts/Source/Europe/EuWatermarkWitnessPlan.cs",
        "src/Lex.V3.Contracts/Source/Europe/EuDocumentFetchPlan.cs",
        "src/Lex.V3.Contracts/Source/Europe/EuFormexManifestationDiscoveryPlan.cs",
        "src/Lex.V3.Contracts/Source/Europe/EuLegalNoticePlan.cs",
    });

    public EuRendererSources(
        MachineQueryRendererSource census,
        MachineQueryRendererSource objectFacts,
        MachineQueryRendererSource witness,
        MachineQueryRendererSource documentFetch,
        MachineQueryRendererSource formexManifestation,
        MachineQueryRendererSource legalNotice)
    {
        Census = census ?? throw new ArgumentNullException(nameof(census));
        ObjectFacts = objectFacts ?? throw new ArgumentNullException(nameof(objectFacts));
        Witness = witness ?? throw new ArgumentNullException(nameof(witness));
        DocumentFetch = documentFetch ?? throw new ArgumentNullException(nameof(documentFetch));
        FormexManifestation = formexManifestation ?? throw new ArgumentNullException(nameof(formexManifestation));
        LegalNotice = legalNotice ?? throw new ArgumentNullException(nameof(legalNotice));
    }

    public MachineQueryRendererSource Census { get; }

    public MachineQueryRendererSource ObjectFacts { get; }

    public MachineQueryRendererSource Witness { get; }

    public MachineQueryRendererSource DocumentFetch { get; }

    public MachineQueryRendererSource FormexManifestation { get; }

    public MachineQueryRendererSource LegalNotice { get; }

    /// <summary>
    /// Reads the six renderer files under <paramref name="checkoutRoot"/>, holds each in
    /// <paramref name="custodyStore"/> and opens it as a renderer source whose reference carries a
    /// fresh resource id and the held bytes' digest. Throws when a file is missing or custody refuses
    /// the hold: neither is a publisher outcome, both mean the build cannot start.
    /// </summary>
    public static async Task<EuRendererSources> FromCheckoutAsync(
        ICustodyStore custodyStore,
        string checkoutRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutRoot);
        var sources = new MachineQueryRendererSource[RendererFiles.Count];
        for (var index = 0; index < RendererFiles.Count; index++)
        {
            var path = Path.Combine(checkoutRoot, RendererFiles[index]);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Renderer source file '{RendererFiles[index]}' is not under the checkout root.", path);
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var (receipt, failure) = await CustodyHold.TryHoldAsync(custodyStore, bytes, cancellationToken)
                .ConfigureAwait(false);
            if (receipt is null)
            {
                throw new InvalidOperationException(
                    $"Custody refused to hold renderer source '{RendererFiles[index]}': {failure}");
            }

            sources[index] = MachineQueryRendererSource.Open(
                new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256),
                bytes);
        }

        return new EuRendererSources(sources[0], sources[1], sources[2], sources[3], sources[4], sources[5]);
    }
}

/// <summary>Which step of the EU acquisition refused. Closed; the detail carries that step's own reason.</summary>
public enum EuFirstMountAcquisitionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The CELEX could not be bound into the run's witnesses, or the adapter run refused.</summary>
    [JsonStringEnumMemberName("run_refused")]
    RunRefused = 1,

    /// <summary>The run completed but its Formex population did not close.</summary>
    [JsonStringEnumMemberName("formex_refused")]
    FormexRefused = 2,

    /// <summary>The run and its Formex population closed but the legal-notice route was refused.</summary>
    [JsonStringEnumMemberName("legal_notice_refused")]
    LegalNoticeRefused = 3,
}

/// <summary>
/// The three EU inputs the Stage 3 envelope takes for one work, or one typed refusal. On a refusal
/// the steps that did complete travel on the result, so a refused build can still be read.
/// </summary>
public sealed class EuFirstMountAcquisitionResult
{
    private EuFirstMountAcquisitionResult(
        EuQueryExecutionResult? run,
        EuFormexPackagePopulationResult? formex,
        EuLegalNoticeRouteResult? legalNotice,
        EuFirstMountAcquisitionRefusal? refusal,
        string? detail)
    {
        Run = run;
        Formex = formex;
        LegalNotice = legalNotice;
        Refusal = refusal;
        Detail = detail;
    }

    /// <summary>The complete adapter run over the one work. Present on success and after a later step refused.</summary>
    public EuQueryExecutionResult? Run { get; }

    /// <summary>The Formex population bound to <see cref="Run"/>. Present on success and after the notice refused.</summary>
    public EuFormexPackagePopulationResult? Formex { get; }

    /// <summary>The legal-notice route under the run's corpus identity, with its terminal request. Present iff delivered.</summary>
    public EuLegalNoticeRouteResult? LegalNotice { get; }

    public EuFirstMountAcquisitionRefusal? Refusal { get; }

    public string? Detail { get; }

    public bool Delivered => Refusal is null;

    public static EuFirstMountAcquisitionResult Success(
        EuQueryExecutionResult run,
        EuFormexPackagePopulationResult formex,
        EuLegalNoticeRouteResult legalNotice)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(formex);
        ArgumentNullException.ThrowIfNull(legalNotice);
        if (run.Refusal is not null || !formex.Delivered || legalNotice.Route is null)
        {
            throw new ArgumentException("A delivered EU acquisition carries a complete run, a closed Formex population and a delivered notice route.");
        }

        return new(run, formex, legalNotice, null, null);
    }

    public static EuFirstMountAcquisitionResult Refused(
        EuFirstMountAcquisitionRefusal refusal,
        string detail,
        EuQueryExecutionResult? run = null,
        EuFormexPackagePopulationResult? formex = null)
    {
        if (refusal == EuFirstMountAcquisitionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(run, formex, null, refusal, detail);
    }
}

/// <summary>
/// The EU half of the first real mount: everything the Stage 3 envelope needs from the Union side
/// for one Appendix A work, acquired live in one process under one wire ceiling, from production
/// plans, production renderer sources and publicly bound witnesses.
/// </summary>
/// <remarks>
/// <para>
/// Order: the census and object-facts run through <see cref="EuQueryExecutionAdapter"/> (the same
/// call the Stage 1 population run made, with the fixture placeholders replaced: the robots
/// witness is a real bound count query of this work's own census family, the document-fetch
/// witness a real bound GET of this work's XHTML), then <see cref="EuFormexPackagePopulationProducer"/>
/// over that run, then <see cref="EuLegalNoticeRouteProducer"/> under the run's corpus identity.
/// The three results are the arguments of
/// <c>Stage3EvidenceEnvelope.TryCreateWithEuropeLegalNoticeRouteAndFormexMainBody</c>, held in the
/// same process because the envelope checks them by reference.
/// </para>
/// <para>
/// One <see cref="WireRequestBudget"/> bounds all of it, robots included: the adapter refuses a
/// census request carrying another instance, and the two producers reserve from the same counter,
/// so the ceiling a person set for the run is the ceiling the run honours.
/// </para>
/// </remarks>
public sealed class EuFirstMountAcquisition
{
    private readonly ICustodyStore _custodyStore;
    private readonly TimeProvider _timeProvider;
    private readonly System.Net.Http.HttpMessageHandler? _testHandlerOverride;

    public EuFirstMountAcquisition(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, testHandlerOverride: null)
    {
    }

    /// <summary>Test-only seam, the same one every Europe producer declares; production code calls the public constructor.</summary>
    internal EuFirstMountAcquisition(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHandlerOverride = testHandlerOverride;
    }

    /// <param name="celex">The work, which must be an Appendix A seed for the adapter to admit it.</param>
    /// <param name="rendererSources">The six renderer sources, held in this run's custody.</param>
    /// <param name="wireBudget">The one ceiling for the whole acquisition.</param>
    public async Task<EuFirstMountAcquisitionResult> RunAsync(
        string celex,
        EuRendererSources rendererSources,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(celex);
        ArgumentNullException.ThrowIfNull(rendererSources);
        ArgumentNullException.ThrowIfNull(wireBudget);

        // The work must be an Appendix A seed: the adapter admits nothing else, so refusing here
        // costs no traffic. The seed gives the work's Cellar root, which is what the document-fetch
        // witness is minted from: a CELEX can carry a slash (the treaties: 12012E/TXT) and is not an
        // admitted resource path, while the run's own fetches address Cellar keys.
        var seedIndex = -1;
        for (var index = 0; index < EuAppendixASeedMap.SeedsInCelexOrder.Count; index++)
        {
            if (string.Equals(EuAppendixASeedMap.SeedsInCelexOrder[index].Celex, celex, StringComparison.Ordinal))
            {
                seedIndex = index;
                break;
            }
        }

        if (seedIndex < 0)
        {
            return EuFirstMountAcquisitionResult.Refused(
                EuFirstMountAcquisitionRefusal.RunRefused,
                $"the CELEX '{celex}' is not an Appendix A seed, so the adapter would admit no census family for it");
        }

        var workRoot = EuPackRootCanonicalForm.TryCanonicalize(
            EuAppendixASeedMap.SeedsInCelexOrder[seedIndex].WorkRoot, out _);
        var cellarKey = workRoot?[(workRoot.LastIndexOf('/') + 1)..];
        if (string.IsNullOrEmpty(cellarKey))
        {
            return EuFirstMountAcquisitionResult.Refused(
                EuFirstMountAcquisitionRefusal.RunRefused,
                $"Appendix A's root for '{celex}' does not canonicalize to a Cellar key");
        }

        // Witnesses are real bound requests of this work, so robots is evaluated against a URL the
        // run actually sends (Decision 83), never a placeholder path.
        BoundMachineRequest sparqlWitness;
        BoundMachineRequest documentFetchWitness;
        var censusPlan = EuConsolidationDiscoveryPlan.Create();
        try
        {
            sparqlWitness = censusPlan.BindCount(
                EuConsolidationQuerySet.Family, celex, EuConsolidationQueryPass.Pass1,
                NewUrn(), NewUrn(), rendererSources.Census).Request;
            var address = EuDocumentFetchAddress.TryCreate(
                "cellar", cellarKey, EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng, out var addressRefusal);
            if (address is null)
            {
                return EuFirstMountAcquisitionResult.Refused(
                    EuFirstMountAcquisitionRefusal.RunRefused,
                    $"the work root of '{celex}' cannot be a document-fetch address: {addressRefusal}");
            }

            documentFetchWitness = new EuDocumentFetchPlan(address)
                .Bind(NewUrn(), NewUrn(), rendererSources.DocumentFetch).Request;
        }
        catch (ArgumentException exception)
        {
            return EuFirstMountAcquisitionResult.Refused(
                EuFirstMountAcquisitionRefusal.RunRefused,
                $"the CELEX '{celex}' cannot be bound into this run's witnesses: {exception.Message}");
        }

        var executor = new EuRepeatedEnumerationExecutor(_custodyStore, _timeProvider, _testHandlerOverride);
        var adapter = new EuQueryExecutionAdapter(_custodyStore, executor);
        var run = await adapter.RunAsync(
                [(new EuCensusPartitionRunRequest(censusPlan, NewUrn(), celex, rendererSources.Census, wireBudget), sparqlWitness)],
                new EuObjectFactsBatchPolicy(
                    EuObjectFactsDiscoveryPlan.Create(), NewUrn(), rendererSources.ObjectFacts, sparqlWitness),
                rendererSources.Witness,
                sparqlWitness,
                rendererSources.DocumentFetch,
                documentFetchWitness,
                wireBudget,
                cancellationToken)
            .ConfigureAwait(false);
        if (run.Refusal is { } runRefusal)
        {
            return EuFirstMountAcquisitionResult.Refused(
                EuFirstMountAcquisitionRefusal.RunRefused,
                $"{runRefusal.Code}: {runRefusal.Detail}",
                run);
        }

        var formex = await new EuFormexPackagePopulationProducer(_custodyStore, _timeProvider, _testHandlerOverride)
            .RunAsync(run, rendererSources.FormexManifestation, sparqlWitness, wireBudget, cancellationToken)
            .ConfigureAwait(false);
        if (!formex.Delivered)
        {
            return EuFirstMountAcquisitionResult.Refused(
                EuFirstMountAcquisitionRefusal.FormexRefused,
                $"{formex.Refusal}: {formex.Detail}",
                run,
                formex);
        }

        var records = run.CorpusRecordSet?.Set.Records;
        if (records is null || records.Count == 0)
        {
            return EuFirstMountAcquisitionResult.Refused(
                EuFirstMountAcquisitionRefusal.RunRefused,
                "the run completed without a corpus record set, so it has no run identity for the legal notice",
                run,
                formex);
        }

        var legalNotice = await new EuLegalNoticeRouteProducer(_custodyStore, _timeProvider, _testHandlerOverride)
            .RunAsync(records[0].RunIdentity, rendererSources.LegalNotice, wireBudget, cancellationToken)
            .ConfigureAwait(false);
        if (legalNotice.Route is null)
        {
            return EuFirstMountAcquisitionResult.Refused(
                EuFirstMountAcquisitionRefusal.LegalNoticeRefused,
                $"{legalNotice.Refusal}: {legalNotice.Detail}",
                run,
                formex);
        }

        return EuFirstMountAcquisitionResult.Success(run, formex, legalNotice);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
