using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

/// <summary>
/// The append-only representation chain key of R3.4: the exact tuple
/// <c>(requested_uri, effective_uri, GET, representation_request_key_digest)</c>.
/// </summary>
/// <remarks>
/// <c>method</c> is not a settable member. R3.4 lists it as a fixed element of the tuple rather
/// than a choice a caller makes, so it is embedded as the literal <c>"GET"</c> in the canonical
/// projection instead of a parameter a test could pass a second value for and watch nothing
/// check it. A representation chain only ever tracks a GET; R3.4 defines no POST chain.
/// </remarks>
public sealed class RepresentationChainKey : IEquatable<RepresentationChainKey>
{
    private RepresentationChainKey(
        string requestedUri,
        string effectiveUri,
        string representationRequestKeyDigestSha256)
    {
        RequestedUri = requestedUri;
        EffectiveUri = effectiveUri;
        RepresentationRequestKeyDigestSha256 = representationRequestKeyDigestSha256;
    }

    public string RequestedUri { get; }

    public string EffectiveUri { get; }

    /// <summary>Fixed. A representation chain only ever tracks GET; see the type remarks.</summary>
    public string Method => "GET";

    /// <summary>
    /// The digest of the bounded R2 representation-request key: exact <c>Accept</c>,
    /// <c>Accept-Language</c>, accepted content-coding selection, and every recognized
    /// <c>Vary</c> selector. Computing that key from a request and a prior response's
    /// <c>Vary</c> header is outside this type; this is the closed identity it produces.
    /// </summary>
    public string RepresentationRequestKeyDigestSha256 { get; }

    public static RepresentationChainKey Create(
        string requestedUri,
        string effectiveUri,
        string representationRequestKeyDigestSha256)
    {
        requestedUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(requestedUri, nameof(requestedUri));
        effectiveUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(effectiveUri, nameof(effectiveUri));
        representationRequestKeyDigestSha256 = RoutedHttpValidation.RequireSha256(
            representationRequestKeyDigestSha256,
            nameof(representationRequestKeyDigestSha256));
        return new RepresentationChainKey(requestedUri, effectiveUri, representationRequestKeyDigestSha256);
    }

    /// <summary>The four tuple members, one per line, method included as the fixed literal.</summary>
    public string CanonicalProjection() =>
        "requested_uri=" + RequestedUri
        + "\neffective_uri=" + EffectiveUri
        + "\nmethod=" + Method
        + "\nrepresentation_request_key_digest=" + RepresentationRequestKeyDigestSha256;

    public bool Equals(RepresentationChainKey? other) =>
        other is not null
        && string.Equals(CanonicalProjection(), other.CanonicalProjection(), StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RepresentationChainKey);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CanonicalProjection());
}

/// <summary>
/// One observation offered to a representation chain: the R3.4-relevant projection of a routed
/// HTTP hop, and nothing else.
/// </summary>
/// <remarks>
/// The only path that mints one is <see cref="FromHop"/>, and it takes a real
/// <see cref="RoutedHttpHop"/>: a value that only exists after <see cref="RoutedHttpHop.Create"/>
/// has already run every framing, status and durability check in
/// <c>RoutedHttpValidation.RequireCompletionFacts</c>. An observation describing bytes that were
/// never actually retained therefore cannot reach this type; it can only restate a fact the hop
/// already proved.
/// </remarks>
public sealed class RepresentationChainObservation
{
    private RepresentationChainObservation(
        string observationId,
        string effectiveUri,
        HttpStatusDisposition statusDisposition,
        bool isCompleteBodyTransfer,
        ulong receivedEntityByteCount,
        string transportByteSha256,
        string observedAt)
    {
        ObservationId = observationId;
        EffectiveUri = effectiveUri;
        StatusDisposition = statusDisposition;
        IsCompleteBodyTransfer = isCompleteBodyTransfer;
        ReceivedEntityByteCount = receivedEntityByteCount;
        TransportByteSha256 = transportByteSha256;
        ObservedAt = observedAt;
    }

    public string ObservationId { get; }

    /// <summary>The URI this hop actually observed, i.e. its own <c>request_uri</c>.</summary>
    public string EffectiveUri { get; }

    public HttpStatusDisposition StatusDisposition { get; }

    /// <summary>
    /// True for R2's <c>response_complete_body</c> shape: a declared-length or chunked-EOF
    /// completion. False for a 304 reference, a semantic-no-entity outcome, or a partial or
    /// otherwise incomplete transfer, none of which frame a representation this chain can
    /// compare.
    /// </summary>
    public bool IsCompleteBodyTransfer { get; }

    public ulong ReceivedEntityByteCount { get; }

    public string TransportByteSha256 { get; }

    public string ObservedAt { get; }

    public static RepresentationChainObservation FromHop(RoutedHttpHop hop)
    {
        ArgumentNullException.ThrowIfNull(hop);
        return new RepresentationChainObservation(
            hop.ObservationId,
            hop.RequestUri,
            hop.StatusDisposition,
            hop.Completion is DeclaredContentLengthHttpCompletion or PinnedHandlerChunkedEofHttpCompletion,
            hop.Length,
            hop.Sha256,
            hop.TerminalObservedAt);
    }

    /// <summary>
    /// R3.4's trusted-baseline-candidate predicate.
    /// <see cref="HttpStatusDisposition.DerivableStatus"/> already implies exact HTTP 200 with no
    /// <c>Content-Range</c>, because <see cref="HttpStatusClassifier.Classify"/> routes any
    /// ranged 200 to <see cref="HttpStatusDisposition.RangeNotApproved"/> before a hop can carry
    /// this disposition at all, so this predicate leans on that upstream classification instead
    /// of re-deriving it from headers a second time. "One or more entity octets" is checked
    /// separately: a 200 with a zero-length declared body is still <c>derivable_status</c> and a
    /// complete transfer, and R3.4 names that zero-octet outcome as non-qualifying regardless.
    /// </summary>
    public bool QualifiesAsTrustedBaselineCandidate() =>
        StatusDisposition == HttpStatusDisposition.DerivableStatus
        && IsCompleteBodyTransfer
        && ReceivedEntityByteCount >= 1;
}

/// <summary>What appending an observation did to the chain's trusted baseline. Closed.</summary>
public enum RepresentationChainAppendDisposition
{
    /// <summary>
    /// The first trusted-baseline candidate in the chain. No replacement event: there was no
    /// predecessor baseline to differ from.
    /// </summary>
    [JsonStringEnumMemberName("baseline_established")]
    BaselineEstablished = 1,

    /// <summary>
    /// A later candidate whose byte count and digest equal the current trusted baseline.
    /// Appended to the history; the baseline does not move.
    /// </summary>
    [JsonStringEnumMemberName("baseline_confirmed_unchanged")]
    BaselineConfirmedUnchanged = 2,

    /// <summary>
    /// A later candidate that differs from the current trusted baseline. Exactly one
    /// <c>file_replaced</c> event is recorded and this observation becomes the new baseline.
    /// </summary>
    [JsonStringEnumMemberName("replacement_recorded")]
    ReplacementRecorded = 3,

    /// <summary>
    /// Not a trusted-baseline candidate: a 304, a partial or otherwise incomplete transfer, a
    /// zero-octet or semantic-no-entity outcome, a ranged or redirect response, any other
    /// non-derivable status, or any observation offered to a chain whose representation-request
    /// key is not closed. Appended to the history; the trusted baseline, if any, is untouched and
    /// no replacement event is recorded.
    /// </summary>
    [JsonStringEnumMemberName("appended_as_evidence_only")]
    AppendedAsEvidenceOnly = 4,
}

/// <summary>Why the chain refused to append an observation. Closed.</summary>
public enum RepresentationChainAppendRefusal
{
    /// <summary>No refusal: the observation was appended.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>An observation identity already occurs in this chain.</summary>
    [JsonStringEnumMemberName("observation_id_reused")]
    ObservationIdReused = 1,

    /// <summary>
    /// The observation's own effective URI does not match the chain it was offered to. Every
    /// observation offered to a chain must be one this exact effective URI actually produced;
    /// this is what stops a caller from filing an unrelated fetch's evidence under the wrong
    /// representation.
    /// </summary>
    [JsonStringEnumMemberName("effective_uri_mismatch")]
    EffectiveUriMismatch = 2,
}

/// <summary>
/// One append-only representation chain: R3.4's history of every observation for one exact
/// <c>(requested_uri, effective_uri, GET, representation_request_key_digest)</c> tuple, and the
/// trusted-baseline and <c>file_replaced</c> state that history carries.
/// </summary>
/// <remarks>
/// <para>
/// A chain holds one tuple. Nothing here enumerates or looks up chains by key; a caller that owns
/// many chains addresses them by <see cref="Key"/> itself, the same way one
/// <c>Lex.V3.Contracts.Source.Absence.AbsenceHistoryLedger</c> holds one absence subject rather
/// than a registry of subjects.
/// </para>
/// <para>
/// The trusted baseline is derived by construction, not tracked as an independently settable
/// field a caller could desynchronize from the history: <see cref="CurrentTrustedBaseline"/> only
/// ever changes inside <see cref="TryAppend"/>, to exactly the observation that append just
/// decided was the new baseline.
/// </para>
/// </remarks>
public sealed class RepresentationChain
{
    private readonly List<AppendedObservation> _history = [];
    private readonly List<FileReplacedEvent> _replacements = [];
    private readonly HashSet<string> _usedObservationIds = new(StringComparer.Ordinal);

    private RepresentationChain(RepresentationChainKey key, bool isClosedRepresentationRequestKey)
    {
        Key = key;
        IsClosedRepresentationRequestKey = isClosedRepresentationRequestKey;
    }

    public RepresentationChainKey Key { get; }

    /// <summary>
    /// False when <see cref="RepresentationChainKey.RepresentationRequestKeyDigestSha256"/> was
    /// computed over an unknown or unrestricted <c>Vary</c> selector. R3.4: "An unknown or
    /// unrestricted Vary field prevents a closed trusted chain; the observation remains evidence
    /// only." Decided once at <see cref="Open"/> because key closedness is a property of how the
    /// digest was computed, not of any one observation; while false, every append in this
    /// chain's lifetime is <see cref="RepresentationChainAppendDisposition.AppendedAsEvidenceOnly"/>,
    /// enforced structurally in <see cref="TryAppend"/> rather than left to a caller to remember
    /// on every call.
    /// </summary>
    public bool IsClosedRepresentationRequestKey { get; }

    /// <summary>Every appended observation, oldest first. Append only.</summary>
    public IReadOnlyList<AppendedObservation> History => _history;

    /// <summary>Every <c>file_replaced</c> event, oldest first. Append only.</summary>
    public IReadOnlyList<FileReplacedEvent> ReplacementEvents => _replacements;

    /// <summary>
    /// The observation currently trusted as this chain's baseline, or null before any candidate
    /// has qualified.
    /// </summary>
    public AppendedObservation? CurrentTrustedBaseline { get; private set; }

    public static RepresentationChain Open(RepresentationChainKey key, bool isClosedRepresentationRequestKey)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new RepresentationChain(key, isClosedRepresentationRequestKey);
    }

    /// <summary>
    /// Appends one observation. Every observation appends; a refusal here means the observation
    /// cannot enter this chain at all, never that the content it reports was rejected. R3.4 has
    /// no path by which an observation is simply discarded.
    /// </summary>
    public AppendedObservation? TryAppend(
        RepresentationChainObservation observation,
        out RepresentationChainAppendRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!string.Equals(observation.EffectiveUri, Key.EffectiveUri, StringComparison.Ordinal))
        {
            refusal = RepresentationChainAppendRefusal.EffectiveUriMismatch;
            return null;
        }

        if (_usedObservationIds.Contains(observation.ObservationId))
        {
            refusal = RepresentationChainAppendRefusal.ObservationIdReused;
            return null;
        }

        var isCandidate = IsClosedRepresentationRequestKey && observation.QualifiesAsTrustedBaselineCandidate();
        var baseline = CurrentTrustedBaseline;

        RepresentationChainAppendDisposition disposition;
        if (!isCandidate)
        {
            disposition = RepresentationChainAppendDisposition.AppendedAsEvidenceOnly;
        }
        else if (baseline is null)
        {
            disposition = RepresentationChainAppendDisposition.BaselineEstablished;
        }
        else if (baseline.ReceivedEntityByteCount == observation.ReceivedEntityByteCount
            && string.Equals(baseline.TransportByteSha256, observation.TransportByteSha256, StringComparison.Ordinal))
        {
            disposition = RepresentationChainAppendDisposition.BaselineConfirmedUnchanged;
        }
        else
        {
            disposition = RepresentationChainAppendDisposition.ReplacementRecorded;
        }

        var appended = new AppendedObservation(observation, disposition);
        _history.Add(appended);
        _usedObservationIds.Add(observation.ObservationId);

        if (disposition == RepresentationChainAppendDisposition.ReplacementRecorded)
        {
            _replacements.Add(new FileReplacedEvent(baseline!, appended));
        }

        if (disposition is RepresentationChainAppendDisposition.BaselineEstablished
            or RepresentationChainAppendDisposition.ReplacementRecorded)
        {
            CurrentTrustedBaseline = appended;
        }

        refusal = RepresentationChainAppendRefusal.None;
        return appended;
    }

    /// <summary>One retained observation together with what appending it decided.</summary>
    public sealed class AppendedObservation
    {
        internal AppendedObservation(
            RepresentationChainObservation observation,
            RepresentationChainAppendDisposition disposition)
        {
            Observation = observation;
            Disposition = disposition;
        }

        public RepresentationChainObservation Observation { get; }

        public RepresentationChainAppendDisposition Disposition { get; }

        public string ObservationId => Observation.ObservationId;

        public ulong ReceivedEntityByteCount => Observation.ReceivedEntityByteCount;

        public string TransportByteSha256 => Observation.TransportByteSha256;
    }

    /// <summary>
    /// One <c>file_replaced</c> event: the trusted baseline this chain held immediately before,
    /// and the observation that replaced it.
    /// </summary>
    public sealed class FileReplacedEvent
    {
        internal FileReplacedEvent(AppendedObservation predecessor, AppendedObservation replacement)
        {
            Predecessor = predecessor;
            Replacement = replacement;
        }

        public AppendedObservation Predecessor { get; }

        public AppendedObservation Replacement { get; }
    }
}
