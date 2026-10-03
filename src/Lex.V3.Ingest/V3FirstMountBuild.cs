using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>Which step of the build refused. Closed; the detail carries that step's own reason.</summary>
public enum V3FirstMountBuildRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("europe_not_delivered")]
    EuropeNotDelivered = 1,

    [JsonStringEnumMemberName("luxembourg_not_delivered")]
    LuxembourgNotDelivered = 2,

    [JsonStringEnumMemberName("annex_classification_refused")]
    AnnexClassificationRefused = 3,

    [JsonStringEnumMemberName("fidelity_refused")]
    FidelityRefused = 4,

    [JsonStringEnumMemberName("envelope_refused")]
    EnvelopeRefused = 5,

    [JsonStringEnumMemberName("body_composition_refused")]
    BodyCompositionRefused = 6,

    [JsonStringEnumMemberName("pdf_layout_refused")]
    PdfLayoutRefused = 7,

    [JsonStringEnumMemberName("pdf_text_layer_refused")]
    PdfTextLayerRefused = 8,

    [JsonStringEnumMemberName("profile_envelope_refused")]
    ProfileEnvelopeRefused = 9,

    [JsonStringEnumMemberName("corpus_refused")]
    CorpusRefused = 10,

    [JsonStringEnumMemberName("luxembourg_index_refused")]
    LuxembourgIndexRefused = 11,

    [JsonStringEnumMemberName("europe_index_refused")]
    EuropeIndexRefused = 12,

    /// <summary>A second build of the same envelope produced different bytes; nothing is written from such a build.</summary>
    [JsonStringEnumMemberName("not_byte_stable")]
    NotByteStable = 13,
}

/// <summary>One file of the mount directory: its name and its exact bytes.</summary>
public sealed record V3CorpusMountFile(string Name, ReadOnlyMemory<byte> Bytes);

/// <summary>
/// The five artefacts <c>Lex.V3.Api</c> mounts, each built twice and equal, or one typed refusal.
/// </summary>
public sealed class V3FirstMountBuildResult
{
    /// <summary>The file names the API's mount expects under its <c>v3-corpus</c> directory.</summary>
    public const string CorpusFileName = "lex-corpus-6.json";
    public const string LuxembourgIndexFileName = "luxembourg-index.sqlite3";
    public const string LuxembourgCapabilityManifestFileName = "luxembourg-capability-manifest.json";
    public const string EuropeIndexFileName = "europe-index.sqlite3";
    public const string EuropeCapabilityManifestFileName = "europe-capability-manifest.json";

    private V3FirstMountBuildResult(
        LexCorpus6BuildResult? corpus,
        LuxembourgIndexBuildResult? luxembourgIndex,
        EuropeIndexBuildResult? europeIndex,
        V3FirstMountBuildRefusal? refusal,
        string? detail,
        AcquisitionResumption? europeResumption = null,
        AcquisitionResumption? luxembourgResumption = null)
    {
        Corpus = corpus;
        LuxembourgIndex = luxembourgIndex;
        EuropeIndex = europeIndex;
        Refusal = refusal;
        Detail = detail;
        EuropeResumption = europeResumption;
        LuxembourgResumption = luxembourgResumption;
    }

    public LexCorpus6BuildResult? Corpus { get; }

    public LuxembourgIndexBuildResult? LuxembourgIndex { get; }

    public EuropeIndexBuildResult? EuropeIndex { get; }

    /// <summary>What the EU catalog says about the resumed run that acquired its population; null for one observed in one run.</summary>
    public AcquisitionResumption? EuropeResumption { get; }

    /// <summary>What the Luxembourg catalog says about the resumed run that acquired its population; null for one observed in one run.</summary>
    public AcquisitionResumption? LuxembourgResumption { get; }

    public V3FirstMountBuildRefusal? Refusal { get; }

    public string? Detail { get; }

    public bool Delivered => Refusal is null;

    /// <summary>The five mount files in the order the writer writes them. Empty unless delivered.</summary>
    public IReadOnlyList<V3CorpusMountFile> Files => Delivered
        ?
        [
            new(CorpusFileName, Corpus!.CanonicalBytes),
            new(LuxembourgIndexFileName, LuxembourgIndex!.IndexBytes),
            new(LuxembourgCapabilityManifestFileName, LuxembourgIndex.CapabilityManifestBytes),
            new(EuropeIndexFileName, EuropeIndex!.IndexBytes),
            new(EuropeCapabilityManifestFileName, EuropeIndex.CapabilityManifestBytes),
        ]
        : [];

    /// <param name="europeResumption">The EU acquisition's resumption, carried to the build report.</param>
    /// <param name="luxembourgResumption">The Luxembourg acquisition's resumption, carried to the build report.</param>
    public static V3FirstMountBuildResult Success(
        LexCorpus6BuildResult corpus,
        LuxembourgIndexBuildResult luxembourgIndex,
        EuropeIndexBuildResult europeIndex,
        AcquisitionResumption? europeResumption = null,
        AcquisitionResumption? luxembourgResumption = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(luxembourgIndex);
        ArgumentNullException.ThrowIfNull(europeIndex);
        return new(corpus, luxembourgIndex, europeIndex, null, null, europeResumption, luxembourgResumption);
    }

    public static V3FirstMountBuildResult Refused(V3FirstMountBuildRefusal refusal, string detail)
    {
        if (refusal == V3FirstMountBuildRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(null, null, null, refusal, detail);
    }
}

/// <summary>
/// The build of the first real mount from the two acquisitions: the Stage 3 evidence envelope, the
/// body composition, the Luxembourg publisher-PDF derivation chain, the derivation profile envelope,
/// and the three builders, each run twice and compared byte for byte, in the one process that holds
/// the acquisition results the envelope checks by reference.
/// </summary>
/// <remarks>
/// This is the chain the corpus builder's own reference test composes
/// (<c>LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync</c>), with production inputs in place of
/// its helpers: <see cref="EuFirstMountAcquisitionResult"/> supplies the run, the Formex population
/// and the notice route; <see cref="LuxembourgFirstMountAcquisitionResult"/> the run and the Akoma
/// Ntoso populations. The annex classifications close over the empty list, which is valid while no
/// Formex outcome is acquired (STATUS.md). The index builders rebuild the corpus internally, so the
/// two builds of each artefact are compared here and a differing pair refuses the whole build
/// rather than writing files a second run would not reproduce (byte stability is a launch promise).
/// </remarks>
public sealed class V3FirstMountBuild
{
    private readonly ICustodyStore _custodyStore;
    private readonly TimeProvider _clock;

    /// <summary>
    /// A build over <paramref name="custodyStore"/>. <paramref name="clock"/> (the system clock unless given) gives the
    /// build time the Luxembourg index's event log records: read once per run, after the acquisitions and every
    /// derivation, rounded up to the next second, and passed to both builds of the index. A second build that must
    /// reproduce a first one byte for byte takes a clock that gives the first one's time.
    /// </summary>
    public V3FirstMountBuild(ICustodyStore custodyStore, TimeProvider? clock = null)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>A time rounded up to the next whole UTC second (unchanged when it is one), so it stays an upper bound.</summary>
    internal static DateTimeOffset BuildTimeOf(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var whole = new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        return whole == utc ? whole : whole.AddSeconds(1);
    }

    public Task<V3FirstMountBuildResult> RunAsync(
        EuFirstMountAcquisitionResult europe,
        LuxembourgFirstMountAcquisitionResult luxembourg,
        CancellationToken cancellationToken) =>
        RunAsync(europe, luxembourg, predecessor: null, cancellationToken);

    /// <summary>
    /// The previous build's Luxembourg index, read from its v3-corpus directory for <see cref="RunAsync(EuFirstMountAcquisitionResult, LuxembourgFirstMountAcquisitionResult, LuxembourgIndexPredecessor?, CancellationToken)"/>:
    /// the index file, held to the digest the directory's build report names (<see cref="LuxembourgIndexPredecessor.TryRead"/>).
    /// </summary>
    public static LuxembourgIndexPredecessor? ReadPredecessor(
        string directory,
        out LuxembourgIndexBuildRefusal refusal,
        out string? detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "build-report.json")));
            var index = report.RootElement.GetProperty("luxembourgIndex");
            var reference = new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
                index.GetProperty("resourceId").GetString()!, index.GetProperty("Sha256").GetString()!);
            return LuxembourgIndexPredecessor.TryRead(
                reference, File.ReadAllBytes(Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName)), out refusal, out detail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                              or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            refusal = LuxembourgIndexBuildRefusal.PredecessorMismatch;
            detail = $"the predecessor directory holds no readable build report and Luxembourg index: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// The build, with the Luxembourg index's event log chained to <paramref name="predecessor"/> when one is given
    /// (<see cref="LuxembourgIndexBuilder.TryBuild(Stage3DerivationProfileEnvelope, LuxembourgIndexPredecessor?, DateTimeOffset, out LuxembourgIndexBuildRefusal, out string?)"/>):
    /// both builds of the index take the same predecessor and the same build time, so the twice-built comparison still holds.
    /// </summary>
    public async Task<V3FirstMountBuildResult> RunAsync(
        EuFirstMountAcquisitionResult europe,
        LuxembourgFirstMountAcquisitionResult luxembourg,
        LuxembourgIndexPredecessor? predecessor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(europe);
        ArgumentNullException.ThrowIfNull(luxembourg);
        if (!europe.Delivered)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.EuropeNotDelivered, $"{europe.Refusal}: {europe.Detail}");
        }

        if (!luxembourg.Delivered)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.LuxembourgNotDelivered, $"{luxembourg.Refusal}: {luxembourg.Detail}");
        }

        var formex = europe.Formex!.Reconciliation!;
        var formexMainBody = await new EuFormexMainBodyLegalContentProducer(_custodyStore)
            .RunAsync(formex, cancellationToken).ConfigureAwait(false);
        var classifications = EuFormexAnnexClassificationReconciliation.TryClose(
            formex, europe.Formex.AnnexClassifications, out var classificationRefusal, out var classificationDetail);
        if (classifications is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.AnnexClassificationRefused, $"{classificationRefusal}: {classificationDetail}");
        }

        var fidelity = Stage3FidelityPreservationReconciliation.TryCreate(
            europe.Run!, luxembourg.Run!, out var fidelityRefusal, out var fidelityDetail);
        if (fidelity is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.FidelityRefused, $"{fidelityRefusal}: {fidelityDetail}");
        }

        var evidence = Stage3EvidenceEnvelope.TryCreateWithEuropeLegalNoticeRouteAndFormexMainBody(
            europe.Run!, europe.LegalNotice!.Route!, europe.LegalNotice.TerminalRequest!, luxembourg.Run!,
            formex, formexMainBody, classifications, fidelity,
            luxembourg.AknInventory!, luxembourg.AknLegalContent,
            out var envelopeRefusal, out var envelopeDetail);
        if (evidence is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.EnvelopeRefused, $"{envelopeRefusal}: {envelopeDetail}");
        }

        var composition = Stage3BodyComposition.TryCreate(evidence, out var compositionRefusal, out var compositionDetail);
        if (composition is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.BodyCompositionRefused, $"{compositionRefusal}: {compositionDetail}");
        }

        var eligibility = LuxembourgPdfProfileEligibilityProducer.Produce(composition);
        var layout = await new LuxembourgPdfLayoutEvidenceProducer(_custodyStore)
            .RunAsync(eligibility, cancellationToken).ConfigureAwait(false);
        if (!layout.Produced)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.PdfLayoutRefused, $"{layout.Refusal}: {layout.Detail}");
        }

        var text = await new LuxembourgPublisherPdfTextLayerProfileProducer(_custodyStore)
            .RunAsync(layout.Population!, cancellationToken).ConfigureAwait(false);
        if (!text.Produced)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.PdfTextLayerRefused, $"{text.Refusal}: {text.Detail}");
        }

        var actScope = LuxembourgPublisherPdfActScopeProducer.Produce(text.Population!);
        var profileEnvelope = Stage3DerivationProfileEnvelope.TryCreate(
            actScope, out var profileRefusal, out var profileDetail);
        if (profileEnvelope is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.ProfileEnvelopeRefused, $"{profileRefusal}: {profileDetail}");
        }

        // ---- Three builders, each twice. A pair that differs refuses the build. ----
        // The build time is read here, after every fetch and derivation, once: both index builds record the same.
        var builtAt = BuildTimeOf(_clock.GetUtcNow());
        var corpus = LexCorpus6Builder.TryBuild(profileEnvelope, out var corpusRefusal, out var corpusDetail);
        if (corpus is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.CorpusRefused, $"{corpusRefusal}: {corpusDetail}");
        }

        var corpusAgain = LexCorpus6Builder.TryBuild(profileEnvelope, out corpusRefusal, out corpusDetail);
        if (corpusAgain is null || corpusAgain.ArtifactRef != corpus.ArtifactRef
            || !corpusAgain.CanonicalBytes.Span.SequenceEqual(corpus.CanonicalBytes.Span))
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.NotByteStable,
                corpusAgain is null
                    ? $"the second corpus build refused: {corpusRefusal}: {corpusDetail}"
                    : $"two builds of the corpus differ: {corpus.ArtifactRef.Sha256} then {corpusAgain.ArtifactRef.Sha256}");
        }

        var luxembourgIndex = LuxembourgIndexBuilder.TryBuild(profileEnvelope, predecessor, builtAt, out var luxembourgIndexRefusal, out var luxembourgIndexDetail);
        if (luxembourgIndex is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.LuxembourgIndexRefused, $"{luxembourgIndexRefusal}: {luxembourgIndexDetail}");
        }

        var luxembourgIndexAgain = LuxembourgIndexBuilder.TryBuild(profileEnvelope, predecessor, builtAt, out luxembourgIndexRefusal, out luxembourgIndexDetail);
        if (luxembourgIndexAgain is null
            || !SameArtefact(luxembourgIndex.IndexRef, luxembourgIndex.IndexBytes, luxembourgIndexAgain.IndexRef, luxembourgIndexAgain.IndexBytes)
            || !SameArtefact(luxembourgIndex.CapabilityManifestRef, luxembourgIndex.CapabilityManifestBytes,
                luxembourgIndexAgain.CapabilityManifestRef, luxembourgIndexAgain.CapabilityManifestBytes))
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.NotByteStable,
                luxembourgIndexAgain is null
                    ? $"the second Luxembourg index build refused: {luxembourgIndexRefusal}: {luxembourgIndexDetail}"
                    : $"two builds of the Luxembourg index differ: {luxembourgIndex.IndexRef.Sha256} then {luxembourgIndexAgain.IndexRef.Sha256}");
        }

        var europeIndex = EuropeIndexBuilder.TryBuild(profileEnvelope, out var europeIndexRefusal, out var europeIndexDetail);
        if (europeIndex is null)
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.EuropeIndexRefused, $"{europeIndexRefusal}: {europeIndexDetail}");
        }

        var europeIndexAgain = EuropeIndexBuilder.TryBuild(profileEnvelope, out europeIndexRefusal, out europeIndexDetail);
        if (europeIndexAgain is null
            || !SameArtefact(europeIndex.IndexRef, europeIndex.IndexBytes, europeIndexAgain.IndexRef, europeIndexAgain.IndexBytes)
            || !SameArtefact(europeIndex.CapabilityManifestRef, europeIndex.CapabilityManifestBytes,
                europeIndexAgain.CapabilityManifestRef, europeIndexAgain.CapabilityManifestBytes))
        {
            return V3FirstMountBuildResult.Refused(
                V3FirstMountBuildRefusal.NotByteStable,
                europeIndexAgain is null
                    ? $"the second Europe index build refused: {europeIndexRefusal}: {europeIndexDetail}"
                    : $"two builds of the Europe index differ: {europeIndex.IndexRef.Sha256} then {europeIndexAgain.IndexRef.Sha256}");
        }

        // A population acquired across an interruption says so in the build report (V3CorpusMountWriter). The catalogs
        // carry the resumption, so an offline derivation states it exactly as the acquiring build did.
        return V3FirstMountBuildResult.Success(corpus, luxembourgIndex, europeIndex, europe.Resumption);
    }

    private static bool SameArtefact(
        Lex.V3.Contracts.Source.Core.SourceArtifactRef firstRef, ReadOnlyMemory<byte> firstBytes,
        Lex.V3.Contracts.Source.Core.SourceArtifactRef secondRef, ReadOnlyMemory<byte> secondBytes) =>
        firstRef == secondRef && firstBytes.Span.SequenceEqual(secondBytes.Span);
}
