using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// Why <see cref="EuLegalNoticeRouteProducer.RunAsync"/> did not deliver a legal-notice route under
/// the corpus run identity. Closed. The first three are the document-fetch door's own refusals
/// passed through unchanged; the last two are this producer's: the route executed but is not the
/// evidence R8 names (<see cref="EuLegalNoticeEvidence.FromRoute"/> refused it, with its reason as
/// the detail), or the route executed but a dependency could not be reopened or the
/// corpus-identity route could not be retained.
/// </summary>
public enum EuLegalNoticeRouteRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("robots_bootstrap_refused")]
    RobotsBootstrapRefused = 1,

    [JsonStringEnumMemberName("observation_not_executed")]
    ObservationNotExecuted = 2,

    [JsonStringEnumMemberName("wire_budget_exhausted")]
    WireBudgetExhausted = 3,

    /// <summary>
    /// The publisher answered, the bytes are retained, and the route is not legal-notice evidence:
    /// a non-200 terminal (a challenge page, a block, a redirect the route could not follow), a
    /// terminal that is not <c>text/html</c>, or a terminal off the pinned origin.
    /// </summary>
    [JsonStringEnumMemberName("notice_route_invalid")]
    NoticeRouteInvalid = 4,

    /// <summary>
    /// The route executed, but the terminal hop's logical request could not be reopened from
    /// custody by its digest, or the corpus-identity route could not be written and read back.
    /// </summary>
    [JsonStringEnumMemberName("route_not_retained")]
    RouteNotRetained = 5,
}

/// <summary>Delivered under the corpus run identity, or refused with one typed reason. Never both.</summary>
public sealed class EuLegalNoticeRouteResult
{
    private EuLegalNoticeRouteResult(
        RoutedHttpEvidence? route,
        HttpLogicalRequest? terminalRequest,
        EuLegalNoticeRouteRefusal? refusal,
        string? detail)
    {
        Route = route;
        TerminalRequest = terminalRequest;
        Refusal = refusal;
        Detail = detail;
    }

    /// <summary>
    /// The retained legal-notice route, re-presented under the corpus run identity and reopened
    /// from custody by digest. Present iff delivered. This is the value
    /// <c>Stage3EvidenceEnvelope.TryCreateWithEuropeLegalNoticeRoute</c> takes together with
    /// <see cref="TerminalRequest"/>.
    /// </summary>
    public RoutedHttpEvidence? Route { get; }

    /// <summary>The logical request the terminal hop actually sent, reopened from custody by its digest.</summary>
    public HttpLogicalRequest? TerminalRequest { get; }

    public EuLegalNoticeRouteRefusal? Refusal { get; }

    public string? Detail { get; }

    public static EuLegalNoticeRouteResult Delivered(RoutedHttpEvidence route, HttpLogicalRequest terminalRequest)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(terminalRequest);
        return new(route, terminalRequest, null, null);
    }

    public static EuLegalNoticeRouteResult Refused(EuLegalNoticeRouteRefusal refusal, string? detail)
    {
        if (refusal == EuLegalNoticeRouteRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, null, refusal, detail);
    }
}

/// <summary>
/// Produces the one legal-notice route a corpus run needs (Decision 88: one GET per run, as rights
/// evidence only), bound to the corpus run's own identity so that
/// <c>Stage3EvidenceEnvelope.TryCreateWithEuropeLegalNoticeRoute</c> accepts it beside the EU corpus
/// records.
/// </summary>
/// <remarks>
/// <para>
/// The GET is sent through the same session every other EU document fetch uses
/// (<see cref="EuRepeatedEnumerationExecutor.RunDocumentFetchAsync"/>): its own robots bootstrap
/// on <c>eur-lex.europa.eu</c>, the profile's pacing, retained transport bytes, and route evidence
/// sealed under the session's run identity. A session mints its own run identity; the corpus
/// records carry the adapter's. The two cannot be the same object, so this producer re-presents
/// the session's hops under the corpus identity through the public, receipt-checked
/// <see cref="RoutedHttpEvidence.Create"/> door, presenting the exact custody write receipts the
/// session held for every hop (<see cref="EuDocumentFetchAttemptResult.HopWriteReceiptsByObservationId"/>).
/// The request and attempt ordinals stay the session's: they are facts of the observation.
/// </para>
/// <para>
/// The route is validated here with <see cref="EuLegalNoticeEvidence.FromRoute"/> so that a
/// publisher challenge page, a block or an off-origin redirect is a typed refusal at production
/// time rather than an envelope refusal at build time. The evidence object itself is not carried:
/// the envelope mints it from the route and the request, through the one door Contracts pins.
/// </para>
/// </remarks>
public sealed class EuLegalNoticeRouteProducer
{
    private readonly ICustodyStore _custodyStore;
    private readonly EuRepeatedEnumerationExecutor _executor;

    public EuLegalNoticeRouteProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, testHandlerOverride: null)
    {
    }

    /// <summary>
    /// Test-only seam, the same one <see cref="EuRepeatedEnumerationExecutor"/> declares: when
    /// supplied, the session's transport is this handler instead of the real network.
    /// </summary>
    internal EuLegalNoticeRouteProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
    }

    /// <param name="corpusRunIdentity">
    /// The run identity the EU corpus records of this run carry; the delivered route names it.
    /// </param>
    /// <param name="rendererSource">The renderer-source artifact for <c>EuLegalNoticeRenderer</c>.</param>
    /// <param name="wireBudget">The run's enforced ceiling; charged for robots and every attempt.</param>
    public async Task<EuLegalNoticeRouteResult> RunAsync(
        SourceArtifactRef corpusRunIdentity,
        MachineQueryRendererSource rendererSource,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpusRunIdentity);
        ArgumentNullException.ThrowIfNull(rendererSource);
        ArgumentNullException.ThrowIfNull(wireBudget);

        var bound = new EuLegalNoticePlan().Bind(NewUrn(), NewUrn(), rendererSource);

        // The session starts from the request it is about to send (Decision 83): robots is
        // evaluated against the pinned notice path itself, never a placeholder.
        var attempt = await _executor.RunDocumentFetchAsync(
                bound.Request, bound.Request, wireBudget, cancellationToken)
            .ConfigureAwait(false);
        if (attempt.Evidence is null)
        {
            var refusal = attempt.Refusal switch
            {
                EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused =>
                    EuLegalNoticeRouteRefusal.RobotsBootstrapRefused,
                EuDocumentFetchAttemptRefusal.ObservationNotExecuted =>
                    EuLegalNoticeRouteRefusal.ObservationNotExecuted,
                EuDocumentFetchAttemptRefusal.WireBudgetExhausted =>
                    EuLegalNoticeRouteRefusal.WireBudgetExhausted,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(attempt),
                    $"Unreachable: a null-evidence attempt carrying '{attempt.Refusal}'."),
            };
            return EuLegalNoticeRouteResult.Refused(refusal, attempt.Detail);
        }

        var sessionRoute = attempt.Evidence;
        var receipts = attempt.HopWriteReceiptsByObservationId
            ?? throw new InvalidOperationException("An executed document fetch lost its hop write receipts.");

        HttpLogicalRequest terminalRequest;
        try
        {
            var requestBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore, sessionRoute.Hops[^1].LogicalRequestSha256, cancellationToken)
                .ConfigureAwait(false);
            terminalRequest = HttpLogicalRequest.ParseAndVerify(requestBytes.Span);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
        {
            return EuLegalNoticeRouteResult.Refused(EuLegalNoticeRouteRefusal.RouteNotRetained, exception.Message);
        }

        // Re-present the same hops under the corpus identity. Create re-verifies every receipt
        // against its hop, so a receipt naming other bytes is refused here, not trusted.
        var corpusRoute = RoutedHttpEvidence.Create(
            corpusRunIdentity,
            sessionRoute.RequestOrdinal,
            sessionRoute.AttemptOrdinal,
            sessionRoute.Hops,
            sessionRoute.Outcome,
            receipts);

        try
        {
            _ = EuLegalNoticeEvidence.FromRoute(corpusRoute, terminalRequest);
        }
        catch (ArgumentException exception)
        {
            return EuLegalNoticeRouteResult.Refused(EuLegalNoticeRouteRefusal.NoticeRouteInvalid, exception.Message);
        }

        try
        {
            // Decision 78: the corpus run holds the route it depends on, under the identity the
            // envelope will check, written and reopened by digest exactly as the session-identity
            // document already was.
            var routeBytes = corpusRoute.CopyCanonicalBytes();
            var routeReceipt = await _custodyStore.CreateAsync(
                    routeBytes, CustodyClass.NightlyFloor90d, cancellationToken)
                .ConfigureAwait(false);
            var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore, routeReceipt.Reference.ContentSha256, cancellationToken)
                .ConfigureAwait(false);
            return EuLegalNoticeRouteResult.Delivered(
                RoutedHttpEvidence.ParseAndVerify(reopened.Span), terminalRequest);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
        {
            return EuLegalNoticeRouteResult.Refused(EuLegalNoticeRouteRefusal.RouteNotRetained, exception.Message);
        }
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
