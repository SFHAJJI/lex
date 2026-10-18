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

/// <summary>
/// One file of the mount directory: its name, its length, and where its bytes are. The corpus and the two capability
/// manifests are held; each index is a file the build staged (<see cref="V3BuiltIndexFile"/>), never held in memory (slice
/// S4b), which the writer moves into the mount.
/// </summary>
public sealed class V3CorpusMountFile
{
    internal V3CorpusMountFile(string name, ReadOnlyMemory<byte> held)
    {
        Name = name;
        Held = held;
        ByteLength = held.Length;
    }

    internal V3CorpusMountFile(string name, V3BuiltIndexFile index)
    {
        Name = name;
        Index = index;
        ByteLength = index.ByteLength;
    }

    public string Name { get; }

    public long ByteLength { get; }

    /// <summary>The held bytes (the corpus, a capability manifest); empty for an index.</summary>
    internal ReadOnlyMemory<byte> Held { get; }

    /// <summary>The staged or written index file; null for a held file.</summary>
    internal V3BuiltIndexFile? Index { get; }

    /// <summary>
    /// The file's bytes: the held ones, or the index file's, read from where it is now (the build's staging directory, or the
    /// mount it was written to). For checks of a build; the writer never reads an index into memory.
    /// </summary>
    public byte[] ReadAllBytes() => Index is null ? Held.ToArray() : File.ReadAllBytes(Index.Path);
}

/// <summary>
/// A build whose Luxembourg derivations come from a re-derivation of the acquisition's custody: the record that names it,
/// and when the derivation is stated to run (<paramref name="DerivedAt"/>, never an observation time). The build report
/// states both.
/// </summary>
public sealed record V3LuxembourgDerivation(LuxembourgRederivationRecord Record, DateTimeOffset DerivedAt);

/// <summary>
/// The five artefacts <c>Lex.V3.Api</c> mounts, each built twice and equal, or one typed refusal. A delivered build owns the
/// staging directory its two index files are in until the writer moves them into a mount; disposing it removes whatever is
/// left there, so a build that is never written leaves nothing behind.
/// </summary>
public sealed class V3FirstMountBuildResult : IDisposable
{
    /// <summary>The file names the API's mount expects under its <c>v3-corpus</c> directory.</summary>
    public const string CorpusFileName = "lex-corpus-6.json";
    public const string LuxembourgIndexFileName = "luxembourg-index.sqlite3";
    public const string LuxembourgCapabilityManifestFileName = "luxembourg-capability-manifest.json";
    public const string EuropeIndexFileName = "europe-index.sqlite3";
    public const string EuropeCapabilityManifestFileName = "europe-capability-manifest.json";

    private readonly V3MountStaging? _staging;

    private V3FirstMountBuildResult(
        LexCorpus6BuildResult? corpus,
        V3BuiltIndexFile? luxembourgIndex,
        V3BuiltIndexFile? europeIndex,
        V3FirstMountBuildRefusal? refusal,
        string? detail,
        AcquisitionResumption? europeResumption = null,
        AcquisitionResumption? luxembourgResumption = null,
        V3LuxembourgDerivation? luxembourgDerivation = null,
        V3MountStaging? staging = null)
    {
        _staging = staging;
        Corpus = corpus;
        LuxembourgIndex = luxembourgIndex;
        EuropeIndex = europeIndex;
        Refusal = refusal;
        Detail = detail;
        EuropeResumption = europeResumption;
        LuxembourgResumption = luxembourgResumption;
        LuxembourgDerivation = luxembourgDerivation;
    }

    /// <summary>The re-derivation the Luxembourg derivations come from, carried to the build report; null when they are the acquisition's own.</summary>
    public V3LuxembourgDerivation? LuxembourgDerivation { get; }

    public LexCorpus6BuildResult? Corpus { get; }

    /// <summary>The Luxembourg index, built twice to files whose SHA-256 agreed: the first file, staged until written.</summary>
    public V3BuiltIndexFile? LuxembourgIndex { get; }

    /// <summary>The EU index, built twice to files whose SHA-256 agreed: the first file, staged until written.</summary>
    public V3BuiltIndexFile? EuropeIndex { get; }

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
            new(LuxembourgIndexFileName, LuxembourgIndex!),
            new(LuxembourgCapabilityManifestFileName, LuxembourgIndex!.CapabilityManifestBytes),
            new(EuropeIndexFileName, EuropeIndex!),
            new(EuropeCapabilityManifestFileName, EuropeIndex!.CapabilityManifestBytes),
        ]
        : [];

    /// <summary>Removes the build's staging directory and whatever the writer has not moved out of it.</summary>
    public void Dispose() => _staging?.Dispose();

    /// <param name="staging">The build's staging directory, which holds the two index files and which the result owns.</param>
    /// <param name="europeResumption">The EU acquisition's resumption, carried to the build report.</param>
    /// <param name="luxembourgResumption">The Luxembourg acquisition's resumption, carried to the build report.</param>
    /// <param name="luxembourgDerivation">The re-derivation the Luxembourg derivations come from, carried to the build report;
    /// its record must be the one the corpus header names.</param>
    internal static V3FirstMountBuildResult Success(
        LexCorpus6BuildResult corpus,
        V3BuiltIndexFile luxembourgIndex,
        V3BuiltIndexFile europeIndex,
        V3MountStaging staging,
        AcquisitionResumption? europeResumption = null,
        AcquisitionResumption? luxembourgResumption = null,
        V3LuxembourgDerivation? luxembourgDerivation = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(luxembourgIndex);
        ArgumentNullException.ThrowIfNull(europeIndex);
        ArgumentNullException.ThrowIfNull(staging);
        if (!string.Equals(luxembourgDerivation?.Record.Sha256, corpus.VerifiedSet.Set.LuxembourgRederivation, StringComparison.Ordinal))
        {
            throw new ArgumentException("A build states the re-derivation its corpus header names, and only that one.", nameof(luxembourgDerivation));
        }

        return new(corpus, luxembourgIndex, europeIndex, null, null, europeResumption, luxembourgResumption, luxembourgDerivation, staging);
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
/// and the three builders, each run twice and compared (the corpus byte for byte, each index by its
/// file's SHA-256), in the one process that holds the acquisition results the envelope checks by reference.
/// </summary>
/// <remarks>
/// This is the chain the corpus builder's own reference test composes
/// (<c>LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync</c>), with production inputs in place of
/// its helpers: <see cref="EuFirstMountAcquisitionResult"/> supplies the run, the Formex population
/// and the notice route; <see cref="LuxembourgFirstMountAcquisitionResult"/> the run and the Akoma
/// Ntoso populations. The annex classifications close over the empty list, which is valid while no
/// Formex outcome is acquired (STATUS.md). The two builds of each artefact are compared here and a
/// differing pair refuses the whole build rather than writing files a second run would not reproduce
/// (byte stability is a launch promise).
/// <para>
/// MEMORY (slice S4b). Each index is built to a file in the build's own staging directory, and the two
/// builds compare the files' SHA-256, read in order from disk: no index is held in memory, where the
/// population's two (0.9 GB and 1.4 GB) were held twice each before. The second file is removed as soon
/// as it is compared, the first is the one the writer moves into the mount, and a refused build removes
/// the staging directory. Both builds of each index take the corpus built and compared here, rather than
/// building it again from the same envelope. The staging directory holds an owner file open until the build
/// is disposed, so that <see cref="SweepStaleStaging"/> removes what a stopped process left there and never
/// a live build's files (the review of #986).
/// </para>
/// </remarks>
public sealed class V3FirstMountBuild
{
    private readonly ICustodyStore _custodyStore;
    private readonly TimeProvider _clock;
    private readonly string _stagingRoot;

    /// <summary>
    /// A build over <paramref name="custodyStore"/>. <paramref name="clock"/> (the system clock unless given) gives the
    /// build time the Luxembourg index's event log records: read once per run, after the acquisitions and every
    /// derivation, rounded up to the next second, and passed to both builds of the index. A second build that must
    /// reproduce a first one byte for byte takes a clock that gives the first one's time.
    /// </summary>
    public V3FirstMountBuild(ICustodyStore custodyStore, TimeProvider? clock = null)
        : this(custodyStore, clock, Path.GetTempPath())
    {
    }

    /// <summary>
    /// For tests only (the review of #986): what the build takes the second build of an index to be, given the index's file
    /// name and the second build's file, so that a test can make the two builds differ and see the build refuse. Null in every
    /// other build, which compares the second build as it was built.
    /// </summary>
    internal Func<string, V3BuiltIndexFile, V3BuiltIndexFile>? SecondBuildForTest { get; init; }

    /// <summary>
    /// Removes the staging directories mount builds left under <paramref name="stagingRoot"/> (the temporary folder the
    /// builds use by default) when their processes ended without disposing them, as a derive the runner's guard stops does:
    /// only directories named <c>lex-v3-mount-build-</c> and 32 hex digits, last written before
    /// <paramref name="untouchedSince"/> (the sweeping process's start), whose owner file no live build holds open. A
    /// population build leaves up to about 4 GB there. Returns each removed directory with the bytes it held.
    /// </summary>
    public static IReadOnlyList<(string Path, long ByteLength)> SweepStaleStaging(string stagingRoot, DateTimeOffset untouchedSince) =>
        V3MountStaging.SweepStale(stagingRoot, untouchedSince);

    /// <summary>A build whose staging directories are made under <paramref name="stagingRoot"/> (the temporary folder by default).</summary>
    internal V3FirstMountBuild(ICustodyStore custodyStore, TimeProvider? clock, string stagingRoot)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _clock = clock ?? TimeProvider.System;
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        _stagingRoot = stagingRoot;
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
            return LuxembourgIndexPredecessor.TryReadFile(
                reference, Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), out refusal, out detail);
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

        // A re-derived Luxembourg population names its record in the envelope, so the corpus and both indexes built from it
        // carry the same header.
        if (luxembourg.Rederivation is { } rederivation)
        {
            profileEnvelope = profileEnvelope.WithLuxembourgRederivation(rederivation, out var rederivationDetail);
            if (profileEnvelope is null)
            {
                return V3FirstMountBuildResult.Refused(
                    V3FirstMountBuildRefusal.ProfileEnvelopeRefused, $"luxembourg_rederivation: {rederivationDetail}");
            }
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

        if (SecondCorpusBuildDiffers(profileEnvelope, corpus) is { } corpusDifference)
        {
            return V3FirstMountBuildResult.Refused(V3FirstMountBuildRefusal.NotByteStable, corpusDifference);
        }

        // Each index twice, to files in this build's own staging directory, compared by their SHA-256 (S4b). The first file is
        // read back by the reader; the second is not, because it is held to the first's digest: equal, it is the first's
        // verified bytes; different, the build refuses.
        var staging = V3MountStaging.Create(_stagingRoot);
        var delivered = false;
        try
        {
            var luxembourgIndex = await LuxembourgIndexBuilder.TryBuildFileAsync(
                profileEnvelope, corpus, predecessor, builtAt, luxembourg.DerivedAt,
                Path.Combine(staging.Path, V3FirstMountBuildResult.LuxembourgIndexFileName), cancellationToken).ConfigureAwait(false);
            if (luxembourgIndex.Index is null)
            {
                return V3FirstMountBuildResult.Refused(
                    V3FirstMountBuildRefusal.LuxembourgIndexRefused, $"{luxembourgIndex.Refusal}: {luxembourgIndex.Detail}");
            }

            var luxembourgIndexAgain = await LuxembourgIndexBuilder.TryBuildFileAsync(
                profileEnvelope, corpus, predecessor, builtAt, luxembourg.DerivedAt,
                Path.Combine(staging.Path, "second-" + V3FirstMountBuildResult.LuxembourgIndexFileName), cancellationToken, readBack: false)
                .ConfigureAwait(false);
            var luxembourgSecond = SecondBuild(V3FirstMountBuildResult.LuxembourgIndexFileName, luxembourgIndexAgain.Index);
            if (luxembourgSecond is null || !V3BuiltIndexFile.Same(luxembourgIndex.Index, luxembourgSecond))
            {
                return V3FirstMountBuildResult.Refused(
                    V3FirstMountBuildRefusal.NotByteStable,
                    luxembourgSecond is null
                        ? $"the second Luxembourg index build refused: {luxembourgIndexAgain.Refusal}: {luxembourgIndexAgain.Detail}"
                        : $"two builds of the Luxembourg index differ: {luxembourgIndex.Index.IndexRef.Sha256} then {luxembourgSecond.IndexRef.Sha256}");
            }

            File.Delete(luxembourgSecond.Path);
            var europeIndex = await EuropeIndexBuilder.TryBuildFileAsync(
                profileEnvelope, corpus, Path.Combine(staging.Path, V3FirstMountBuildResult.EuropeIndexFileName), cancellationToken).ConfigureAwait(false);
            if (europeIndex.Index is null)
            {
                return V3FirstMountBuildResult.Refused(
                    V3FirstMountBuildRefusal.EuropeIndexRefused, $"{europeIndex.Refusal}: {europeIndex.Detail}");
            }

            var europeIndexAgain = await EuropeIndexBuilder.TryBuildFileAsync(
                profileEnvelope, corpus, Path.Combine(staging.Path, "second-" + V3FirstMountBuildResult.EuropeIndexFileName), cancellationToken,
                readBack: false).ConfigureAwait(false);
            var europeSecond = SecondBuild(V3FirstMountBuildResult.EuropeIndexFileName, europeIndexAgain.Index);
            if (europeSecond is null || !V3BuiltIndexFile.Same(europeIndex.Index, europeSecond))
            {
                return V3FirstMountBuildResult.Refused(
                    V3FirstMountBuildRefusal.NotByteStable,
                    europeSecond is null
                        ? $"the second Europe index build refused: {europeIndexAgain.Refusal}: {europeIndexAgain.Detail}"
                        : $"two builds of the Europe index differ: {europeIndex.Index.IndexRef.Sha256} then {europeSecond.IndexRef.Sha256}");
            }

            File.Delete(europeSecond.Path);

            // A population acquired across an interruption says so in the build report (V3CorpusMountWriter). The catalogs
            // carry the resumption, so an offline derivation states it exactly as the acquiring build did.
            var result = V3FirstMountBuildResult.Success(corpus, luxembourgIndex.Index, europeIndex.Index, staging, europe.Resumption,
                luxembourg.Resumption, luxembourg.Rederivation is { } record ? new V3LuxembourgDerivation(record, luxembourg.DerivedAt!.Value) : null);
            delivered = true;
            return result;
        }
        finally
        {
            // A refused or failed build leaves nothing: the result owns the staging directory only once it is delivered.
            if (!delivered)
            {
                staging.Dispose();
            }
        }
    }

    /// <summary>The second build of an index as it was built, or as a test makes it (<see cref="SecondBuildForTest"/>).</summary>
    private V3BuiltIndexFile? SecondBuild(string fileName, V3BuiltIndexFile? built) =>
        built is not null && SecondBuildForTest is { } alter ? alter(fileName, built) : built;

    /// <summary>
    /// Why a second build of the corpus does not reproduce <paramref name="first"/> (its digest, then its bytes), or null when
    /// it does. The second build lives in this frame only, so it is released as soon as it is compared.
    /// </summary>
    private static string? SecondCorpusBuildDiffers(Stage3DerivationProfileEnvelope envelope, LexCorpus6BuildResult first)
    {
        var again = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        if (again is null)
        {
            return $"the second corpus build refused: {refusal}: {detail}";
        }

        return again.ArtifactRef != first.ArtifactRef || !again.CanonicalBytes.Span.SequenceEqual(first.CanonicalBytes.Span)
            ? $"two builds of the corpus differ: {first.ArtifactRef.Sha256} then {again.ArtifactRef.Sha256}"
            : null;
    }
}
