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
/// The typed evidence that one Gazette-PDF body was fetched from the publisher's own address and
/// retained under custody: the address, the exact first and terminal requests, the observed route,
/// and the receipt. Verified, not trusted, by <see cref="LuxembourgGazetteBodyDisposition.Create"/>.
/// </summary>
public sealed record LuxembourgGazetteBodyRetention
{
    public LuxembourgGazetteBodyRetention(
        LuxembourgDocumentFetchAddress officialAddress,
        HttpLogicalRequest officialRequest,
        HttpLogicalRequest terminalRequest,
        RoutedHttpEvidence sourceEvidence,
        DurableBlobWriteReceipt retainedTransportBytes)
    {
        OfficialAddress = officialAddress ?? throw new ArgumentNullException(nameof(officialAddress));
        OfficialRequest = officialRequest ?? throw new ArgumentNullException(nameof(officialRequest));
        TerminalRequest = terminalRequest ?? throw new ArgumentNullException(nameof(terminalRequest));
        SourceEvidence = sourceEvidence ?? throw new ArgumentNullException(nameof(sourceEvidence));
        RetainedTransportBytes = retainedTransportBytes
            ?? throw new ArgumentNullException(nameof(retainedTransportBytes));
    }

    /// <summary>The publisher's file address: the store file URI, its fetch URI, format, legal value and act page path.</summary>
    public LuxembourgDocumentFetchAddress OfficialAddress { get; }

    /// <summary>The exact request the first hop sent to the address's fetch URI.</summary>
    public HttpLogicalRequest OfficialRequest { get; }

    /// <summary>The exact request the terminal hop sent (the same one unless the publisher redirected).</summary>
    public HttpLogicalRequest TerminalRequest { get; }

    /// <summary>The observed route, hop by hop, each hop naming its custody receipt by digest.</summary>
    public RoutedHttpEvidence SourceEvidence { get; }

    /// <summary>The custody receipt for the terminal hop's transport bytes.</summary>
    public DurableBlobWriteReceipt RetainedTransportBytes { get; }
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
/// nothing. Structural blockers outrank the rights statement, because a listing that is not this
/// act's body is not something the publisher marked about this act.
/// </para>
/// <para>
/// ADMITTED MEANS THE BYTES WERE FETCHED FROM THE PUBLISHER'S ADDRESS AND RETAINED, ESTABLISHED,
/// NOT ASSERTED. The first head of this type admitted on a receipt plus any artifact reference;
/// review found that established nothing about where the bytes came from. <see cref="Create"/> now
/// takes the typed retention (<see cref="LuxembourgGazetteBodyRetention"/>) and verifies, the way
/// <c>EuAnnexBodyDisposition</c> does: the address names this listing's item, format and act; the
/// first hop sent the official request to the address's fetch URI; the terminal request is the one
/// the terminal hop actually sent; the observation is a complete derivable transfer of
/// <c>application/pdf</c>; and the terminal hop's receipt digest is the exact retained receipt,
/// whose content digest is the transport bytes' own. The observation is retained after
/// verification.
/// </para>
/// <para>
/// IDENTITY IS WHAT THE PUBLISHER SAID; LINEAGE IS WHEN IT WAS ASKED. S3-A04 requires both halves:
/// byte-stable identity across two independent executions, and retained source-observation and
/// transport-byte lineage. The second head of this type failed the first half the way this
/// repository had already learned once in <c>EuLanguageScopedExpressionDerivation</c>: it bound
/// run-minted references (the bound run identity, the channel enumeration refs, the observation
/// evidence refs) into the rights identity, so two runs over identical publisher facts addressed
/// one body two ways. Now <see cref="RightsClaimSha256"/> binds only the semantic claim - the
/// disposition, the manifestation, each channel's licence values and unrepresentable count, and
/// the licence rules that give those values their meaning - and that is what
/// <see cref="IdentitySha256"/> carries beside act, listing, byte sha and outcome. Every run-minted
/// reference lives in <see cref="RightsLineageSha256"/> and <see cref="EpisodeSha256"/>, retained
/// beside the identity, where varying is what they are for.
/// </para>
/// <para>
/// PER-FILE RIGHTS, CARRIED UNCHANGED. <see cref="RightsResolution"/> is the dual-channel resolution
/// exactly as the join produced it. Stated limitation: the in-file channel reads XML/AKN only, so a
/// PDF's second channel resolves to a typed quarantine or a pending state, never to
/// <c>agreed_same_run_cc_by</c>, until a second channel can read a PDF. Under the settled rule that
/// state does not withhold holding; whether the body may be served is the downstream rights gate's
/// question and is not widened here.
/// </para>
/// </remarks>
public sealed record LuxembourgGazetteBodyDisposition
{
    private const string GazetteMediaType = "application/pdf";

    private LuxembourgGazetteBodyDisposition(
        LuxembourgBodyCandidateResolution candidate,
        LuxembourgUserFormatToken format,
        LuxembourgDocumentFetchAddress? officialAddress,
        RepresentationChainObservation? sourceObservation,
        SourceArtifactRef? sourceEvidenceRunIdentity,
        DurableBlobWriteReceipt? retainedTransportBytes,
        LuxembourgGazetteBodyOutcome outcome,
        LuxembourgGazetteBodyGapReason? gapReason)
    {
        Candidate = candidate;
        Format = format;
        OfficialAddress = officialAddress;
        SourceObservation = sourceObservation;
        SourceEvidenceRunIdentity = sourceEvidenceRunIdentity;
        RetainedTransportBytes = retainedTransportBytes;
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
        RightsClaimSha256 = RightsClaimDigest(candidate.RightsResolution);
        RightsLineageSha256 = RightsLineageDigest(candidate.RightsResolution);
        IdentitySha256 = Digest(string.Join(
            '\n',
            PublisherActIri,
            ManifestationIri,
            ItemIri,
            TransportByteSha256 ?? string.Empty,
            Invariant((int)outcome),
            gapReason is { } reason ? Invariant((int)reason) : string.Empty,
            RightsClaimSha256));
        EpisodeSha256 = Digest(string.Join(
            '\n',
            RightsLineageSha256,
            sourceObservation?.ObservationId ?? string.Empty,
            sourceEvidenceRunIdentity is null ? string.Empty : Ref(sourceEvidenceRunIdentity)));
    }

    /// <summary>The listing: the WEMI tuple and the dual-channel rights resolution, as the join produced them.</summary>
    public LuxembourgBodyCandidateResolution Candidate { get; }

    /// <summary>The Gazette-PDF format token the listing parses to: <c>pdfa</c> or <c>pdf</c>.</summary>
    public LuxembourgUserFormatToken Format { get; }

    /// <summary>The publisher's file address the bytes were fetched from. Present exactly when admitted.</summary>
    public LuxembourgDocumentFetchAddress? OfficialAddress { get; }

    /// <summary>The verified terminal observation of the fetch. Present exactly when admitted.</summary>
    public RepresentationChainObservation? SourceObservation { get; }

    /// <summary>The run that produced the route evidence. Present exactly when admitted; lineage, not identity.</summary>
    public SourceArtifactRef? SourceEvidenceRunIdentity { get; }

    /// <summary>The custody receipt for the transport bytes. Present exactly when admitted.</summary>
    public DurableBlobWriteReceipt? RetainedTransportBytes { get; }

    public LuxembourgGazetteBodyOutcome Outcome { get; }

    /// <summary>Present exactly when the outcome is a typed gap.</summary>
    public LuxembourgGazetteBodyGapReason? GapReason { get; }

    public string ReasonCode { get; }

    /// <summary>
    /// The stable digest of the semantic rights claim: the disposition, the selected manifestation,
    /// each channel's licence values and unrepresentable count, and the licence rules that give the
    /// values their meaning. Two executions over one publisher fact agree.
    /// </summary>
    public string RightsClaimSha256 { get; }

    /// <summary>
    /// The digest of which execution observed the rights: the bound run identity, both channel
    /// enumeration refs, and each present observation's run and evidence refs. Two executions
    /// differ, by design.
    /// </summary>
    public string RightsLineageSha256 { get; }

    /// <summary>
    /// What was disposed: act, manifestation, item, transport-byte digest, outcome, gap reason and
    /// the rights claim. Byte-stable across two independent executions over one publisher fact.
    /// </summary>
    public string IdentitySha256 { get; }

    /// <summary>
    /// Which execution disposed it: the rights lineage, the source observation and the evidence run.
    /// Retained beside the identity; two executions differ, by design.
    /// </summary>
    public string EpisodeSha256 { get; }

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
    /// Disposes one Gazette-PDF listing. A retention is required for an admitted body, verified
    /// against the listing and the route, and forbidden for a body that is not held.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The candidate is not a Gazette-PDF listing; a retention was given for a body that is not
    /// held; or the retention does not establish that these bytes were fetched from this listing's
    /// publisher address and retained.
    /// </exception>
    public static LuxembourgGazetteBodyDisposition Create(
        LuxembourgBodyCandidateResolution candidate,
        LuxembourgGazetteBodyRetention? retention)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var format = TryParseGazetteFormat(candidate)
            ?? throw new ArgumentException(
                $"{candidate.WemiCandidate.FormatIri} is not a Gazette-PDF listing; only pdfa and pdf bodies are disposed here.",
                nameof(candidate));

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

        if (retention is null)
        {
            return new LuxembourgGazetteBodyDisposition(
                candidate, format, null, null, null, null,
                LuxembourgGazetteBodyOutcome.TypedGap, LuxembourgGazetteBodyGapReason.BodyNotRetained);
        }

        var observation = VerifyRetention(candidate, format, retention);
        return new LuxembourgGazetteBodyDisposition(
            candidate,
            format,
            retention.OfficialAddress,
            observation,
            retention.SourceEvidence.RunIdentity,
            retention.RetainedTransportBytes,
            LuxembourgGazetteBodyOutcome.Admitted,
            null);

        LuxembourgGazetteBodyDisposition NotHeld(
            LuxembourgGazetteBodyOutcome outcome, LuxembourgGazetteBodyGapReason? reason)
        {
            if (retention is not null)
            {
                throw new ArgumentException(
                    "A body that is not held carries no retained bytes; retaining it would hold what the rule withholds.",
                    nameof(retention));
            }

            return new LuxembourgGazetteBodyDisposition(candidate, format, null, null, null, null, outcome, reason);
        }
    }

    /// <summary>
    /// Establishes that the retention's bytes were fetched from this listing's publisher address
    /// and retained. Every relation is checked; none is taken on the caller's word.
    /// </summary>
    private static RepresentationChainObservation VerifyRetention(
        LuxembourgBodyCandidateResolution candidate,
        LuxembourgUserFormatToken format,
        LuxembourgGazetteBodyRetention retention)
    {
        var address = retention.OfficialAddress;
        var wemi = candidate.WemiCandidate;

        // THE ADDRESS IS THIS LISTING'S: its store file URI is the listed item, its format is the
        // listed format, and its act page path is this act's. An address for another file, another
        // format of the same file, or another act's page is not evidence about this body.
        if (!string.Equals(address.StoreFileUri.Value.AbsoluteUri, wemi.ItemIri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The address names {address.StoreFileUri.Value.AbsoluteUri}, not this listing's item {wemi.ItemIri}.",
                nameof(retention));
        }

        if (address.UserFormatToken != format)
        {
            throw new ArgumentException(
                $"The address names format {address.UserFormatToken}, not this listing's {format}.",
                nameof(retention));
        }

        if (!string.Equals(address.ActEliPagePath, ActEliPagePathOf(wemi.RootIri), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The address names act page {address.ActEliPagePath}, not this act's.",
                nameof(retention));
        }

        // THE OFFICIAL REQUEST IS THE ADDRESS'S NON-NEGOTIATING FETCH, AND THE FIRST HOP SENT IT.
        // The address mints host and path with no Accept; a request that negotiated is not the
        // address's request. The hop binds by digest, so a request object that merely looks right
        // is not enough.
        var official = retention.OfficialRequest;
        var evidence = retention.SourceEvidence;
        var fetchUri = address.FetchUri.AbsoluteUri;
        if (official.Method != HttpRequestMethod.Get ||
            !string.Equals(official.Uri, fetchUri, StringComparison.Ordinal) ||
            HasHeader(official, "accept") ||
            HasHeader(retention.TerminalRequest, "accept") ||
            !string.Equals(evidence.Hops[0].RequestUri, fetchUri, StringComparison.Ordinal) ||
            !string.Equals(
                evidence.Hops[0].LogicalRequestSha256,
                Digest(official.CopyCanonicalBytes()),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The official request is not the address's non-negotiating fetch, or the first hop did not send it.",
                nameof(retention));
        }

        // THE TERMINAL REQUEST IS THE ONE THE TERMINAL HOP SENT: FromRoute binds it by digest and
        // refuses anything but a GET. Then the route must start at the address and the observation
        // must be a complete derivable transfer of the Gazette media type.
        var observation = RepresentationChainObservation.FromRoute(evidence, retention.TerminalRequest);
        if (!string.Equals(observation.RequestedUri, fetchUri, StringComparison.Ordinal) ||
            !string.Equals(observation.EffectiveUri, retention.TerminalRequest.Uri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The observed route does not start at the address and end at the terminal request.",
                nameof(retention));
        }

        if (!observation.QualifiesAsTrustedBaselineCandidate())
        {
            throw new ArgumentException(
                "The source observation is not a complete derivable body transfer.", nameof(retention));
        }

        var terminalHop = evidence.Hops[^1];
        if (terminalHop.Headers.ContentType is not RoutedHttpSingleHeader contentType ||
            !string.Equals(contentType.Value, GazetteMediaType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The terminal response is not application/pdf; a page that is not the Gazette PDF is not the body.",
                nameof(retention));
        }

        // THE RECEIPT IS THE TERMINAL HOP'S. The hop names its receipt by digest, and the exact
        // retained receipt must be that one. That the receipt names the bytes the hop transferred is
        // RoutedHttpEvidence's own invariant - it refuses a hop whose receipt names other bytes at
        // construction - so it is not re-checked here.
        var receipt = retention.RetainedTransportBytes;
        if (!string.Equals(terminalHop.DurableWriteReceiptSha256, DurableBlobWriteReceiptDigest.Of(receipt), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The retained receipt is not the exact receipt bound to the terminal hop.", nameof(retention));
        }

        return observation;
    }

    private static string ActEliPagePathOf(string actIri) => new Uri(actIri, UriKind.Absolute).AbsolutePath;

    private static bool HasHeader(HttpLogicalRequest request, string name) =>
        request.Headers.Any(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase));

    private static LuxembourgUserFormatToken? TryParseGazetteFormat(LuxembourgBodyCandidateResolution candidate) =>
        LuxembourgAuthorityIri.TryParseUserFormat(candidate.WemiCandidate.FormatIri) switch
        {
            LuxembourgUserFormatToken.PdfA => LuxembourgUserFormatToken.PdfA,
            LuxembourgUserFormatToken.Pdf => LuxembourgUserFormatToken.Pdf,
            _ => null,
        };

    /// <summary>
    /// The semantic rights claim, canonical: nothing run-minted, nothing order-dependent. An
    /// observation's licence IRIs are already ordinal-sorted and unique by its own constructor; an
    /// absent observation is a fixed marker rather than an omission; and the two licence rules that
    /// give a value its meaning are bound so a rule change is an identity change.
    /// </summary>
    public static string RightsClaimSha256Of(LuxembourgRightsChannelResolution rights)
    {
        ArgumentNullException.ThrowIfNull(rights);
        return RightsClaimDigest(rights);
    }

    private static string RightsClaimDigest(LuxembourgRightsChannelResolution rights) =>
        Digest(string.Join(
            '\n',
            Invariant((int)rights.Disposition),
            rights.SelectedManifestationIri,
            Claim(rights.SparqlObservation),
            Claim(rights.InFileObservation),
            VerifiedLuxembourgSourceProfile.AdmittingLicence,
            VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl));

    private static string Claim(LuxembourgRightsChannelObservation? observation) =>
        observation is null
            ? "-"
            : Invariant(observation.UnrepresentableLicenceAssertions) + "|" + string.Join(' ', observation.LicenceIris);

    private static string RightsLineageDigest(LuxembourgRightsChannelResolution rights) =>
        Digest(string.Join(
            '\n',
            Ref(rights.BoundRunIdentity),
            Ref(rights.SparqlObservations.EnumerationRef),
            Ref(rights.InFileObservations.EnumerationRef),
            Lineage(rights.SparqlObservation),
            Lineage(rights.InFileObservation)));

    private static string Lineage(LuxembourgRightsChannelObservation? observation) =>
        observation is null ? "-" : Ref(observation.RunIdentity) + "|" + Ref(observation.EvidenceRef);

    private static string Ref(SourceArtifactRef reference) => reference.ResourceId + "|" + reference.Sha256;

    private static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Digest(string canonical) => Digest(Encoding.UTF8.GetBytes(canonical));

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
