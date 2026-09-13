using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>One Gazette-PDF body's typed outcome. Closed.</summary>
/// <remarks>
/// S3-A01 asks the Gazette-PDF profile for explicit admitted, rejected and typed-gap outcomes;
/// S7-A02 asks that every discovered body carry exactly one. These are the three, and a body
/// carries one of them, never none and never two.
/// </remarks>
public enum LuxembourgGazetteBodyOutcome
{
    /// <summary>Structurally consistent, not marked non-reusable, and its transport bytes are retained.</summary>
    [JsonStringEnumMemberName("admitted")]
    Admitted = 1,

    /// <summary>The publisher marked the body not reusable (<c>licenceSCL</c>). Not held.</summary>
    [JsonStringEnumMemberName("rejected")]
    Rejected = 2,

    /// <summary>A body this build could not admit or reject, for a named reason.</summary>
    [JsonStringEnumMemberName("typed_gap")]
    TypedGap = 3,
}

/// <summary>Why one Gazette-PDF body is a typed gap. Closed.</summary>
public enum LuxembourgGazetteBodyGapReason
{
    /// <summary>The WEMI tuple that lists the body is quarantined; the listing is unservable.</summary>
    [JsonStringEnumMemberName("wemi_tuple_typed_quarantine")]
    WemiTupleTypedQuarantine = 1,

    /// <summary>The tuple was reached from another root; it is not this act's body.</summary>
    [JsonStringEnumMemberName("wemi_root_mismatch")]
    WemiRootMismatch = 2,

    /// <summary>Admissible, but its transport bytes were not retained under custody.</summary>
    [JsonStringEnumMemberName("body_not_retained")]
    BodyNotRetained = 3,
}

/// <summary>Why one act has no Gazette-PDF body to dispose at all. Closed.</summary>
public enum LuxembourgGazetteActGapReason
{
    /// <summary>The publisher's realization path from the act reached no WEMI tuple.</summary>
    [JsonStringEnumMemberName("realization_path_unproven")]
    RealizationPathUnproven = 1,

    /// <summary>Tuples exist, but none lists a <c>pdfa</c> or <c>pdf</c> manifestation.</summary>
    [JsonStringEnumMemberName("no_gazette_pdf_candidate")]
    NoGazettePdfCandidate = 2,
}

/// <summary>
/// One Gazette-PDF body of one as-published act, with its typed outcome and its per-file rights.
/// #419 slice 6a.
/// </summary>
/// <remarks>
/// <para>
/// ONE OUTCOME PER BODY, NOT ONE BODY PER ACT. The publisher may list several PDF manifestations for
/// one act (a <c>pdfa</c> and a <c>pdf</c>, or one per language expression). Each is a discovered
/// body and gets its own outcome; nothing here ranks or chooses among them, because choosing would
/// be a serving decision this contract does not own.
/// </para>
/// <para>
/// THE RULES ARE THE BODY JOIN'S, RESTATED PER BODY. <see cref="LuxembourgBodyJoin"/> already rules
/// that a quarantined or root-mismatched tuple is an unservable listing and that the ONLY rights
/// state withholding a body is the publisher marking it not reusable; every other rights state is
/// recorded on the resolution for the answer layer's Decision 58(a) disclosure and withholds
/// nothing. This type adds one requirement the join cannot see: an admitted body has its transport
/// bytes retained under custody, with the fetch evidence that produced them. Structural blockers
/// outrank the rights statement, because a listing that is not this act's body is not something the
/// publisher marked about this act.
/// </para>
/// <para>
/// PER-FILE RIGHTS, CARRIED UNCHANGED AND BOUND INTO IDENTITY. <see cref="RightsResolution"/> is the
/// dual-channel resolution exactly as the join produced it, and <see cref="IdentitySha256"/> binds
/// its canonical digest (<see cref="RightsIdentitySha256"/>): several rights states deliberately do
/// not withhold holding, so two admitted dispositions with one act, one listing and one byte sha
/// can differ only in what the rights channels said - and an identity that did not say which was
/// retained would make the per-file rights lineage unauditable (Codex's pre-freeze correction; the
/// merged EU annex disposition binds its outcome-producing profile digest for the same reason).
/// Stated limitation: the in-file channel reads XML/AKN only, so a PDF's second channel resolves to
/// a typed quarantine or a pending state, never to <c>agreed_same_run_cc_by</c>, until a second
/// channel can read a PDF. Under the settled rule that state does not withhold holding; whether the
/// body may be served is the downstream rights gate's question and is not widened here.
/// </para>
/// </remarks>
public sealed record LuxembourgGazetteBodyDisposition
{
    private LuxembourgGazetteBodyDisposition(
        LuxembourgBodyCandidateResolution candidate,
        LuxembourgUserFormatToken format,
        DurableBlobWriteReceipt? retainedTransportBytes,
        SourceArtifactRef? fetchEvidenceRef,
        LuxembourgGazetteBodyOutcome outcome,
        LuxembourgGazetteBodyGapReason? gapReason)
    {
        Candidate = candidate;
        Format = format;
        RetainedTransportBytes = retainedTransportBytes;
        FetchEvidenceRef = fetchEvidenceRef;
        Outcome = outcome;
        GapReason = gapReason;
        // A TYPED GAP CARRIES ITS REASON, AND NOTHING ELSE DOES. Every caller is consistent today;
        // this keeps a future one from minting a gap with no reason or a reason with no gap, which
        // the reason-code switch below would otherwise paper over with its fallback.
        if ((gapReason is null) != (outcome != LuxembourgGazetteBodyOutcome.TypedGap))
        {
            throw new InvalidOperationException(
                $"{outcome} with gap reason '{gapReason}' is not a disposition this type mints.");
        }

        ReasonCode = outcome switch
        {
            LuxembourgGazetteBodyOutcome.Admitted => "gazette_body_admitted",
            LuxembourgGazetteBodyOutcome.Rejected => "gazette_body_publisher_marked_not_reusable",
            _ => gapReason switch
            {
                LuxembourgGazetteBodyGapReason.WemiTupleTypedQuarantine => "gazette_gap_wemi_tuple_typed_quarantine",
                LuxembourgGazetteBodyGapReason.WemiRootMismatch => "gazette_gap_wemi_root_mismatch",
                _ => "gazette_gap_body_not_retained",
            },
        };
        RightsIdentitySha256 = RightsResolutionIdentitySha256(candidate.RightsResolution);
        IdentitySha256 = ComputeIdentitySha256(this);
    }

    /// <summary>The listing: the WEMI tuple and the dual-channel rights resolution, as the join produced them.</summary>
    public LuxembourgBodyCandidateResolution Candidate { get; }

    /// <summary>The Gazette-PDF format token the listing parses to: <c>pdfa</c> or <c>pdf</c>.</summary>
    public LuxembourgUserFormatToken Format { get; }

    /// <summary>The custody receipt for the transport bytes. Present exactly when admitted.</summary>
    public DurableBlobWriteReceipt? RetainedTransportBytes { get; }

    /// <summary>The fetch evidence that produced the retained bytes. Present exactly when admitted.</summary>
    public SourceArtifactRef? FetchEvidenceRef { get; }

    public LuxembourgGazetteBodyOutcome Outcome { get; }

    /// <summary>Present exactly when the outcome is a typed gap.</summary>
    public LuxembourgGazetteBodyGapReason? GapReason { get; }

    public string ReasonCode { get; }

    /// <summary>
    /// The canonical digest of the carried rights resolution: its disposition, the selected
    /// manifestation, the bound run identity, both channel enumeration refs, and for each present
    /// observation its evidence ref, unrepresentable-assertion count and licence IRIs in ordinal
    /// order. Same claim, same digest, whatever order the channels listed the licences in.
    /// </summary>
    public string RightsIdentitySha256 { get; }

    /// <summary>Binds act, manifestation, item, byte sha, outcome, gap reason and the rights identity.</summary>
    public string IdentitySha256 { get; }

    public string PublisherActIri => Candidate.WemiCandidate.RootIri;

    public string ManifestationIri => Candidate.WemiCandidate.ManifestationIri;

    public string ItemIri => Candidate.WemiCandidate.ItemIri;

    /// <summary>The per-file rights disposition, exactly as the dual-channel resolver produced it.</summary>
    public LuxembourgRightsChannelResolution RightsResolution => Candidate.RightsResolution;

    public string? TransportByteSha256 => RetainedTransportBytes?.Reference.ContentSha256;

    /// <summary>Whether a join candidate is a Gazette-PDF body at all.</summary>
    public static bool IsGazettePdf(LuxembourgBodyCandidateResolution candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return TryParseGazetteFormat(candidate) is not null;
    }

    /// <summary>
    /// Disposes one Gazette-PDF listing. Bytes and evidence are required together for an admissible
    /// body and forbidden for a body that is not held.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The candidate is not a Gazette-PDF listing; or bytes and evidence were given one without the
    /// other; or bytes were given for a body that is not held.
    /// </exception>
    public static LuxembourgGazetteBodyDisposition Create(
        LuxembourgBodyCandidateResolution candidate,
        DurableBlobWriteReceipt? retainedTransportBytes,
        SourceArtifactRef? fetchEvidenceRef)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var format = TryParseGazetteFormat(candidate)
            ?? throw new ArgumentException(
                $"{candidate.WemiCandidate.FormatIri} is not a Gazette-PDF listing; only pdfa and pdf bodies are disposed here.",
                nameof(candidate));

        // BOTH OR NEITHER. A receipt without the evidence that produced it, or evidence without the
        // bytes it claims to have produced, is a malformed claim rather than a partial one.
        if ((retainedTransportBytes is null) != (fetchEvidenceRef is null))
        {
            throw new ArgumentException(
                "Retained bytes and their fetch evidence are asserted together or not at all.",
                nameof(fetchEvidenceRef));
        }

        var retained = retainedTransportBytes is not null;
        var blockers = candidate.BlockerCodes;

        // STRUCTURE FIRST. A quarantined or root-mismatched tuple is not this act's body, so
        // nothing the publisher said about it, and no bytes anyone fetched for it, can be admitted.
        if (blockers.Contains(LuxembourgBodyBlockerCode.WemiTupleTypedQuarantine))
        {
            return NotHeld(LuxembourgGazetteBodyOutcome.TypedGap, LuxembourgGazetteBodyGapReason.WemiTupleTypedQuarantine);
        }

        if (blockers.Contains(LuxembourgBodyBlockerCode.WemiRootMismatch))
        {
            return NotHeld(LuxembourgGazetteBodyOutcome.TypedGap, LuxembourgGazetteBodyGapReason.WemiRootMismatch);
        }

        // THE ONE RIGHTS STATE THAT WITHHOLDS. The join's rule, kept: licenceSCL rejects; every other
        // rights state is recorded and withholds nothing.
        if (blockers.Contains(LuxembourgBodyBlockerCode.PublisherMarkedNotReusable))
        {
            return NotHeld(LuxembourgGazetteBodyOutcome.Rejected, null);
        }

        if (candidate.Disposition != LuxembourgBodyCandidateDisposition.AcceptedCandidate)
        {
            // The join admits exactly the blocker-free candidate, and the blockers above are the
            // only ones a pdfa/pdf listing can carry; anything else is a join this type does not know.
            throw new ArgumentException(
                "The candidate is withheld by a blocker this disposition does not recognize: "
                + string.Join(", ", blockers),
                nameof(candidate));
        }

        return retained
            ? new LuxembourgGazetteBodyDisposition(
                candidate, format, retainedTransportBytes, fetchEvidenceRef,
                LuxembourgGazetteBodyOutcome.Admitted, null)
            : new LuxembourgGazetteBodyDisposition(
                candidate, format, null, null,
                LuxembourgGazetteBodyOutcome.TypedGap, LuxembourgGazetteBodyGapReason.BodyNotRetained);

        LuxembourgGazetteBodyDisposition NotHeld(
            LuxembourgGazetteBodyOutcome outcome, LuxembourgGazetteBodyGapReason? reason)
        {
            if (retained)
            {
                throw new ArgumentException(
                    "A body that is not held carries no retained bytes; retaining it would hold what the rule withholds.",
                    nameof(retainedTransportBytes));
            }

            return new LuxembourgGazetteBodyDisposition(candidate, format, null, null, outcome, reason);
        }
    }

    private static LuxembourgUserFormatToken? TryParseGazetteFormat(LuxembourgBodyCandidateResolution candidate) =>
        LuxembourgAuthorityIri.TryParseUserFormat(candidate.WemiCandidate.FormatIri) switch
        {
            LuxembourgUserFormatToken.PdfA => LuxembourgUserFormatToken.PdfA,
            LuxembourgUserFormatToken.Pdf => LuxembourgUserFormatToken.Pdf,
            _ => null,
        };

    /// <summary>
    /// The canonical identity of one dual-channel rights resolution. Every evidence-bound input the
    /// resolution exposes is bound, and nothing order-dependent is: an observation's licence IRIs are
    /// already ordinal-sorted and unique by its own constructor, the channel collections are
    /// canonical by theirs, and an absent observation is a fixed marker rather than an omission.
    /// </summary>
    public static string RightsResolutionIdentitySha256(LuxembourgRightsChannelResolution rights)
    {
        ArgumentNullException.ThrowIfNull(rights);
        var canonical = string.Join(
            '\n',
            ((int)rights.Disposition).ToString(System.Globalization.CultureInfo.InvariantCulture),
            rights.SelectedManifestationIri,
            Ref(rights.BoundRunIdentity),
            Ref(rights.SparqlObservations.EnumerationRef),
            Ref(rights.InFileObservations.EnumerationRef),
            Observation(rights.SparqlObservation),
            Observation(rights.InFileObservation));
        return Digest(canonical);

        static string Ref(SourceArtifactRef reference) => reference.ResourceId + "|" + reference.Sha256;

        static string Observation(LuxembourgRightsChannelObservation? observation) =>
            observation is null
                ? "-"
                : Ref(observation.EvidenceRef)
                    + "|" + observation.UnrepresentableLicenceAssertions.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "|" + string.Join(' ', observation.LicenceIris);
    }

    private static string ComputeIdentitySha256(LuxembourgGazetteBodyDisposition disposition)
    {
        var canonical = string.Join(
            '\n',
            disposition.PublisherActIri,
            disposition.ManifestationIri,
            disposition.ItemIri,
            disposition.TransportByteSha256 ?? string.Empty,
            ((int)disposition.Outcome).ToString(System.Globalization.CultureInfo.InvariantCulture),
            disposition.GapReason is { } reason
                ? ((int)reason).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            disposition.RightsIdentitySha256);
        return Digest(canonical);
    }

    private static string Digest(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

/// <summary>
/// One as-published act's Gazette-PDF bodies, every listing disposed, or the typed reason the act
/// has none. #419 slice 6a.
/// </summary>
/// <remarks>
/// <para>
/// COMPLETE OVER THE JOIN, OR REFUSED. The set is created from the act's body join and the
/// dispositions a producer minted for it, and it requires exactly one disposition per Gazette-PDF
/// listing of that join: a listing without a disposition, a disposition for a listing the join
/// does not hold, or two for one listing, is a caller contract violation rather than a partial set.
/// That is what lets a ledger over the population say "every discovered body has one typed outcome"
/// without inspecting the joins itself.
/// </para>
/// <para>
/// MINTED FROM THIS JOIN'S OWN LISTING, NOT A LOOKALIKE. A listing key (manifestation, item) names
/// a body, but a candidate resolution also carries the disposition, blockers and rights the join
/// computed for it, and a separately built one with the same key can carry others - the lens showed
/// that a key-only check let a foreign disposition stand in for a real one at the right count. So
/// each disposition's candidate must be the very object this join lists: the set is assembled from
/// the join it was created against, which is also the only run identity the join actually binds.
/// </para>
/// </remarks>
public sealed record LuxembourgGazetteBodySet
{
    private LuxembourgGazetteBodySet(
        string publisherActIri,
        SourceArtifactRef observationRunRef,
        IReadOnlyList<LuxembourgGazetteBodyDisposition> bodies,
        LuxembourgGazetteActGapReason? actGap)
    {
        PublisherActIri = publisherActIri;
        ObservationRunRef = observationRunRef;
        Bodies = bodies;
        ActGap = actGap;
    }

    public string PublisherActIri { get; }

    public SourceArtifactRef ObservationRunRef { get; }

    /// <summary>Every Gazette-PDF body of the act, by ordinal manifestation then item IRI.</summary>
    public IReadOnlyList<LuxembourgGazetteBodyDisposition> Bodies { get; }

    /// <summary>Present exactly when the act has no Gazette-PDF body to dispose.</summary>
    public LuxembourgGazetteActGapReason? ActGap { get; }

    public int AdmittedCount => Bodies.Count(static b => b.Outcome == LuxembourgGazetteBodyOutcome.Admitted);

    public int RejectedCount => Bodies.Count(static b => b.Outcome == LuxembourgGazetteBodyOutcome.Rejected);

    public int GapCount => Bodies.Count(static b => b.Outcome == LuxembourgGazetteBodyOutcome.TypedGap);

    /// <summary>The Gazette-PDF listings of a join that a producer must dispose, in join order.</summary>
    public static IReadOnlyList<LuxembourgBodyCandidateResolution> GazetteCandidatesOf(LuxembourgBodyJoinResolution join)
    {
        ArgumentNullException.ThrowIfNull(join);
        return join.Candidates.Where(LuxembourgGazetteBodyDisposition.IsGazettePdf).ToArray();
    }

    /// <summary>
    /// Assembles one act's set from its join and the dispositions minted for its Gazette listings.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The dispositions are not exactly one per Gazette-PDF listing of the join, one names another
    /// act, or one was not minted from this join's own listing.
    /// </exception>
    public static LuxembourgGazetteBodySet Create(
        LuxembourgBodyJoinResolution join,
        IReadOnlyList<LuxembourgGazetteBodyDisposition> dispositions)
    {
        ArgumentNullException.ThrowIfNull(join);
        ArgumentNullException.ThrowIfNull(dispositions);

        var listings = GazetteCandidatesOf(join);
        var expected = listings
            .Select(static c => ListingKey(c.WemiCandidate.ManifestationIri, c.WemiCandidate.ItemIri))
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var disposition in dispositions)
        {
            ArgumentNullException.ThrowIfNull(disposition, nameof(dispositions));
            if (!string.Equals(disposition.PublisherActIri, join.RootIri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"A disposition names {disposition.PublisherActIri}, not this act {join.RootIri}.",
                    nameof(dispositions));
            }

            var key = ListingKey(disposition.ManifestationIri, disposition.ItemIri);
            if (!listings.Any(listing => ReferenceEquals(listing, disposition.Candidate)))
            {
                throw new ArgumentException(
                    $"A disposition was not minted from one of this join's own listings: {key}.",
                    nameof(dispositions));
            }

            if (!seen.Add(key))
            {
                throw new ArgumentException(
                    $"Two dispositions name one listing: {key}.", nameof(dispositions));
            }
        }

        if (seen.Count != expected.Count)
        {
            var missing = expected.Where(k => !seen.Contains(k)).OrderBy(static k => k, StringComparer.Ordinal);
            throw new ArgumentException(
                "Every Gazette-PDF listing needs its disposition; missing: " + string.Join("; ", missing),
                nameof(dispositions));
        }

        LuxembourgGazetteActGapReason? actGap = null;
        if (listings.Count == 0)
        {
            actGap = join.RootBlockerCodes.Contains(LuxembourgBodyRootBlockerCode.PublisherRealizationPathUnproven)
                ? LuxembourgGazetteActGapReason.RealizationPathUnproven
                : LuxembourgGazetteActGapReason.NoGazettePdfCandidate;
        }

        // CANONICAL ORDER, NOT DELIVERY ORDER: the same rule as the coverage, so two producers
        // disposing one act's listings in two orders assemble one set.
        var bodies = dispositions
            .OrderBy(static b => b.ManifestationIri, StringComparer.Ordinal)
            .ThenBy(static b => b.ItemIri, StringComparer.Ordinal)
            .ToArray();
        return new LuxembourgGazetteBodySet(join.RootIri, join.ObservationRunRef, bodies, actGap);
    }

    private static string ListingKey(string manifestationIri, string itemIri) => manifestationIri + "|" + itemIri;
}
