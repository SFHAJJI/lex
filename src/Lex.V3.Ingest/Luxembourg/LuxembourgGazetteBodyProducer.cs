using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why one act's Gazette bodies could not be produced. Closed.</summary>
public enum LuxembourgGazetteBodyProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>An acquisition names a body the act's join does not list as a Gazette PDF.</summary>
    [JsonStringEnumMemberName("acquisition_for_unlisted_body")]
    AcquisitionForUnlistedBody = 1,

    /// <summary>Two acquisitions name one listing. Two answers about one body are a disagreement, not a merge.</summary>
    [JsonStringEnumMemberName("acquisition_delivered_twice")]
    AcquisitionDeliveredTwice = 2,

    /// <summary>The receipt's bytes could not be read back from custody, or are not the bytes it names.</summary>
    [JsonStringEnumMemberName("retained_bytes_unavailable")]
    RetainedBytesUnavailable = 3,

    /// <summary>
    /// The acquisition does not establish that these bytes were fetched from this listing's
    /// publisher address - or it fetched a body the rules withhold. Named, never downgraded to
    /// "not retained".
    /// </summary>
    [JsonStringEnumMemberName("retention_not_established")]
    RetentionNotEstablished = 4,
}

/// <summary>
/// What the document-fetch loop has in hand when it holds one Gazette-PDF body: the listing it
/// fetched, the address the fetch was bound from, the exact requests, the observed route, and the
/// custody receipt. The producer's input; verified by the disposition, never trusted.
/// </summary>
public sealed record LuxembourgGazetteBodyAcquisition
{
    public LuxembourgGazetteBodyAcquisition(
        string manifestationIri,
        string itemIri,
        LuxembourgDocumentFetchAddress officialAddress,
        HttpLogicalRequest officialRequest,
        HttpLogicalRequest terminalRequest,
        RoutedHttpEvidence sourceEvidence,
        DurableBlobWriteReceipt retainedTransportBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestationIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemIri);
        ManifestationIri = manifestationIri;
        ItemIri = itemIri;
        OfficialAddress = officialAddress ?? throw new ArgumentNullException(nameof(officialAddress));
        OfficialRequest = officialRequest ?? throw new ArgumentNullException(nameof(officialRequest));
        TerminalRequest = terminalRequest ?? throw new ArgumentNullException(nameof(terminalRequest));
        SourceEvidence = sourceEvidence ?? throw new ArgumentNullException(nameof(sourceEvidence));
        RetainedTransportBytes = retainedTransportBytes
            ?? throw new ArgumentNullException(nameof(retainedTransportBytes));
    }

    public string ManifestationIri { get; }

    public string ItemIri { get; }

    public LuxembourgDocumentFetchAddress OfficialAddress { get; }

    public HttpLogicalRequest OfficialRequest { get; }

    public HttpLogicalRequest TerminalRequest { get; }

    public RoutedHttpEvidence SourceEvidence { get; }

    public DurableBlobWriteReceipt RetainedTransportBytes { get; }

    /// <summary>The typed retention the disposition verifies.</summary>
    public LuxembourgGazetteBodyRetention ToRetention() =>
        new(OfficialAddress, OfficialRequest, TerminalRequest, SourceEvidence, RetainedTransportBytes);
}

/// <summary>One act's produced Gazette body set, or a typed refusal with detail.</summary>
public sealed class LuxembourgGazetteBodyProductionResult
{
    private LuxembourgGazetteBodyProductionResult(
        LuxembourgGazetteBodySet? set,
        LuxembourgGazetteBodyProductionRefusal refusal,
        string? detail)
    {
        Set = set;
        Refusal = refusal;
        Detail = detail;
    }

    public LuxembourgGazetteBodySet? Set { get; }

    public LuxembourgGazetteBodyProductionRefusal Refusal { get; }

    public string? Detail { get; }

    public bool Produced => Refusal == LuxembourgGazetteBodyProductionRefusal.None;

    internal static LuxembourgGazetteBodyProductionResult Success(LuxembourgGazetteBodySet set) =>
        new(set, LuxembourgGazetteBodyProductionRefusal.None, null);

    internal static LuxembourgGazetteBodyProductionResult Refused(
        LuxembourgGazetteBodyProductionRefusal refusal, string detail) => new(null, refusal, detail);
}

/// <summary>
/// Produces one as-published act's Gazette-PDF body set from its body join and the bodies the
/// document-fetch loop held for it. #419 slice 6b. Offline: it fetches nothing.
/// </summary>
/// <remarks>
/// <para>
/// THE ACCEPTED PRODUCER SHAPE, KEPT. <c>EuImageOnlyAnnexProducer</c> (#592) does not fetch: it takes
/// the already-acquired route evidence and receipt, reads the bytes back from custody, and mints the
/// disposition. This producer does the same per Gazette listing, and leaves the fetching where it
/// is - the adapter's document-fetch loop, which holds exactly the pieces
/// <see cref="LuxembourgGazetteBodyAcquisition"/> carries when it holds a body.
/// </para>
/// <para>
/// EVERY LISTING GETS ITS OUTCOME; EVERY ACQUISITION MUST BE ONE OF THEM. A listing without an
/// acquisition is typed by the disposition (not retained, rejected, or a structural gap). An
/// acquisition naming a body the join does not list as a Gazette PDF, or a second one for one
/// listing, refuses the whole act - naming every offender in ordinal order - because a set of
/// bodies with a stray or doubled body in it is not this act's set.
/// </para>
/// <para>
/// THE BYTES ARE IN CUSTODY BEFORE THE CLAIM IS MINTED. Each acquisition's receipt is read back
/// checked (<see cref="CustodyRestore.ReadCheckedAsync"/>) before its disposition exists; bytes that
/// are missing or are not the bytes the receipt names refuse. And a retention the disposition cannot
/// establish - the wrong address, a route that did not fetch it, a receipt the terminal hop does not
/// name, or a fetch of a body the rules withhold - refuses by name rather than being downgraded to
/// "not retained": a producer that quietly recorded a broken proof as an absent one would hide the
/// very thing the proof exists to show.
/// </para>
/// <para>
/// DETERMINISTIC. Listings are disposed in join order and the set canonicalizes; two delivery orders
/// of the acquisitions produce one set, and a refusal names its offenders in ordinal order.
/// </para>
/// </remarks>
public sealed class LuxembourgGazetteBodyProducer
{
    private readonly ICustodyStore _custodyStore;

    public LuxembourgGazetteBodyProducer(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<LuxembourgGazetteBodyProductionResult> RunAsync(
        LuxembourgBodyJoinResolution join,
        IReadOnlyList<LuxembourgGazetteBodyAcquisition> acquisitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(join);
        ArgumentNullException.ThrowIfNull(acquisitions);
        cancellationToken.ThrowIfCancellationRequested();

        var listings = LuxembourgGazetteBodySet.GazetteCandidatesOf(join);
        var listed = listings
            .Select(static l => Key(l.WemiCandidate.ManifestationIri, l.WemiCandidate.ItemIri))
            .ToHashSet(StringComparer.Ordinal);

        // EVERY OFFENDER, IN ORDINAL ORDER: a refusal names the same bodies whichever order the
        // loop delivered them in.
        var byKey = new Dictionary<string, LuxembourgGazetteBodyAcquisition>(StringComparer.Ordinal);
        var unlisted = new SortedSet<string>(StringComparer.Ordinal);
        var twice = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var acquisition in acquisitions)
        {
            ArgumentNullException.ThrowIfNull(acquisition, nameof(acquisitions));
            var key = Key(acquisition.ManifestationIri, acquisition.ItemIri);
            if (!listed.Contains(key))
            {
                unlisted.Add(key);
                continue;
            }

            if (!byKey.TryAdd(key, acquisition))
            {
                twice.Add(key);
            }
        }

        if (unlisted.Count > 0)
        {
            return LuxembourgGazetteBodyProductionResult.Refused(
                LuxembourgGazetteBodyProductionRefusal.AcquisitionForUnlistedBody,
                $"Acquisitions name bodies {join.RootIri} does not list as Gazette PDFs: " + string.Join("; ", unlisted));
        }

        if (twice.Count > 0)
        {
            return LuxembourgGazetteBodyProductionResult.Refused(
                LuxembourgGazetteBodyProductionRefusal.AcquisitionDeliveredTwice,
                "Two acquisitions name one listing: " + string.Join("; ", twice));
        }

        var dispositions = new List<LuxembourgGazetteBodyDisposition>(listings.Count);
        foreach (var listing in listings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(listing.WemiCandidate.ManifestationIri, listing.WemiCandidate.ItemIri);
            if (!byKey.TryGetValue(key, out var acquisition))
            {
                dispositions.Add(LuxembourgGazetteBodyDisposition.Create(listing, null));
                continue;
            }

            // THE BYTES ARE IN CUSTODY, CHECKED, BEFORE THE CLAIM IS MINTED.
            try
            {
                await CustodyRestore.ReadCheckedAsync(
                        _custodyStore, acquisition.RetainedTransportBytes.Reference, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CustodyRequiredException
                or CustodyIntegrityException or CustodyPolicyException)
            {
                return LuxembourgGazetteBodyProductionResult.Refused(
                    LuxembourgGazetteBodyProductionRefusal.RetainedBytesUnavailable,
                    key + ": " + exception.Message);
            }

            // A RETENTION THE DISPOSITION CANNOT ESTABLISH REFUSES BY NAME, never downgraded.
            try
            {
                dispositions.Add(LuxembourgGazetteBodyDisposition.Create(listing, acquisition.ToRetention()));
            }
            catch (ArgumentException exception)
            {
                return LuxembourgGazetteBodyProductionResult.Refused(
                    LuxembourgGazetteBodyProductionRefusal.RetentionNotEstablished,
                    key + ": " + exception.Message);
            }
        }

        return LuxembourgGazetteBodyProductionResult.Success(LuxembourgGazetteBodySet.Create(join, dispositions));
    }

    private static string Key(string manifestationIri, string itemIri) => manifestationIri + "|" + itemIri;
}
