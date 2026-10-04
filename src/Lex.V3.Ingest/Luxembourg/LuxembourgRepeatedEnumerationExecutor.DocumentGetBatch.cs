using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Luxembourg;

public sealed partial class LuxembourgRepeatedEnumerationExecutor
{
    /// <summary>A document phase's shared session: see <see cref="DocumentGetBatch"/>.</summary>
    internal DocumentGetBatch OpenDocumentGetBatch() => new(this);

    /// <summary>
    /// One document phase's GETs (the selected documents, then the Gazette listings) through one session at a time,
    /// rather than one session, and so one robots fetch, per document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY URL IS STILL EVALUATED LITERALLY (Decision 83). The session's bootstrap evaluates the first document's own
    /// URL; every later one is evaluated by the session, against the robots bytes its bootstrap fetched and held, before
    /// any request ordinal is allocated (<c>OpenPlanItemAdmittedByRobots</c>). A path the policy disallows is that
    /// document's own refusal and sends nothing; a policy that cannot be interpreted for it is a run-level refusal, as at
    /// a bootstrap.
    /// </para>
    /// <para>
    /// THE ROBOTS FETCH IS RETAINED. Before a session's first document, its robots route is written to custody and
    /// reopened by digest, and every result of that session names it, so the verdict each GET relied on is re-derived
    /// offline from the retained robots bytes (<see cref="LuxembourgDocumentFetchRouteReader"/>). A per-document session
    /// never retained its robots fetch.
    /// </para>
    /// <para>
    /// A SESSION IS REPLACED EVERY HOUR, far inside the 24-hour robots age its send gate enforces, so a phase of tens of
    /// thousands of documents never meets that gate and the policy any GET relied on is never more than an hour old.
    /// One sequential caller only; not thread-safe.
    /// </para>
    /// </remarks>
    internal sealed class DocumentGetBatch : IDisposable
    {
        private readonly LuxembourgRepeatedEnumerationExecutor _executor;
        private RoutedHttpAcquisitionSession? _session;
        private SourceArtifactRef? _robotsRoute;
        private long _startedAt;

        public DocumentGetBatch(LuxembourgRepeatedEnumerationExecutor executor)
        {
            _executor = executor;
        }

        internal async Task<LuxembourgDocumentGetAttemptResult> RunAsync(BoundMachineRequest boundRequest, WireRequestBudget wireBudget,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(boundRequest);
            ArgumentNullException.ThrowIfNull(wireBudget);
            if (_session is not null && _executor._timeProvider.GetElapsedTime(_startedAt) >= TimeSpan.FromHours(1))
                Close();

            if (_session is null)
            {
                // THE SESSION'S ROBOTS FETCH, RESERVED BEFORE THE SESSION EXISTS, once per session.
                if (!wireBudget.TryReserveAttempt())
                {
                    return LuxembourgDocumentGetAttemptResult.Refused(
                        LuxembourgDocumentGetAttemptRefusal.WireBudgetExhausted,
                        "the ceiling was reached before this phase's robots request.");
                }

                _startedAt = _executor._timeProvider.GetTimestamp();
                var start = _executor._testHandlerOverride is null
                    ? await RoutedHttpAcquisitionSession.StartAsync(boundRequest, _executor._custodyStore, wireBudget, cancellationToken)
                        .ConfigureAwait(false)
                    : await RoutedHttpAcquisitionSession.StartWithTestTransportAsync(boundRequest, _executor._custodyStore,
                        _executor._testHandlerOverride, _executor._timeProvider, wireBudget, cancellationToken).ConfigureAwait(false);
                if (start.Kind != OfficialHttpAcquisitionOutcomeKind.ExecutedObservation || start.Session is null)
                {
                    // A publisher denial is this first document's own refusal, by the retained policy that denied it;
                    // everything else is a run-level bootstrap failure that says nothing about this document.
                    if (start.Kind == OfficialHttpAcquisitionOutcomeKind.PublisherDenial &&
                        start.DeniedRequestPath is { } deniedPath && start.Evidence is { } denial)
                    {
                        try
                        {
                            return LuxembourgDocumentGetAttemptResult.RobotsRefused(deniedPath,
                                await _executor.HoldRobotsRouteAsync(denial, cancellationToken).ConfigureAwait(false));
                        }
                        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
                        {
                            return LuxembourgDocumentGetAttemptResult.Refused(
                                LuxembourgDocumentGetAttemptRefusal.RobotsBootstrapNotCompleted,
                                "the robots route that denied this document could not be retained: " + exception.Message);
                        }
                    }

                    return LuxembourgDocumentGetAttemptResult.Refused(
                        LuxembourgDocumentGetAttemptRefusal.RobotsBootstrapNotCompleted,
                        $"kind={start.Kind} safety={start.LocalSafetyReason} operational={start.OperationalReason}");
                }

                try
                {
                    _robotsRoute = await _executor.HoldRobotsRouteAsync(start.Evidence!, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
                {
                    start.Session.Dispose();
                    return LuxembourgDocumentGetAttemptResult.Refused(
                        LuxembourgDocumentGetAttemptRefusal.RobotsBootstrapNotCompleted,
                        "this phase's robots route could not be retained: " + exception.Message);
                }

                _session = start.Session;
            }

            var (item, verdict, path) = _session.OpenPlanItemAdmittedByRobots(boundRequest);
            if (verdict == RobotsPolicyEvaluationResult.Denied)
                return LuxembourgDocumentGetAttemptResult.RobotsRefused(path, _robotsRoute);
            if (item is null)
            {
                return LuxembourgDocumentGetAttemptResult.Refused(
                    LuxembourgDocumentGetAttemptRefusal.RobotsBootstrapNotCompleted,
                    $"this run's robots policy cannot be interpreted for '{path}'.");
            }

            return await _executor.RunOpenedDocumentAsync(_session, item, wireBudget, _robotsRoute, cancellationToken)
                .ConfigureAwait(false);
        }

        public void Dispose() => Close();

        private void Close()
        {
            _session?.Dispose();
            _session = null;
            _robotsRoute = null;
        }
    }

    // Declared here, after RunCoverCoreAsync in member order: the construction-surface pins name that method's lambdas by
    // its member ordinal, which a method declared before it would move.
    /// <summary>
    /// One document GET on a session that is already bootstrapped, through the plan item the caller opened: every
    /// attempt reserved from the wire budget first, a completed retryable status re-attempted within the profile's
    /// allowance, and the final route retained and reopened by digest. <paramref name="robotsRoute"/> is carried onto the
    /// result (a document phase's shared session; null for a session of its own).
    /// </summary>
    private async Task<LuxembourgDocumentGetAttemptResult> RunOpenedDocumentAsync(RoutedHttpAcquisitionSession session,
        RoutedHttpAcquisitionSession.IPlanItem item, WireRequestBudget wireBudget, SourceArtifactRef? robotsRoute,
        CancellationToken cancellationToken)
    {
        try
        {
            var maximumAttempts = session.SourceProfile.MaximumAttempts;
            var attemptCount = 0;
            RoutedHttpAcquisitionSession.AttemptResult attempt;
            while (true)
            {
                // EVERY ATTEMPT, NOT ONLY THE FIRST. This door re-attempts a COMPLETED response at a
                // retryable status - the one place this driver deliberately differs from the EU one,
                // documented below - so the retry loop is exactly where a body-fetch ceiling would
                // otherwise be lost. That the first request was charged says nothing about the sixth.
                if (!wireBudget.TryReserveAttempt())
                {
                    return LuxembourgDocumentGetAttemptResult.Refused(
                        LuxembourgDocumentGetAttemptRefusal.WireBudgetExhausted,
                        $"the ceiling was reached after {attemptCount} attempt(s).");
                }

                attempt = await item.ExecuteNextAttemptAsync(cancellationToken).ConfigureAwait(false);
                attemptCount++;

                // NO REDIRECT-HOP CEILING MAPPING HERE, AND THE ASYMMETRY WITH THE EU DOCUMENT
                // FETCH IS DELIBERATE. That door maps the session's own RedirectTargetNotSentWire-
                // BudgetExhausted outcome because the EU document profile admits a same-origin 303
                // chain. This publisher's document profile expects no redirect on this route at
                // all: a 303 here ends the route as SourceProfileStale before any successor could
                // be considered, so the session's hop gate is unreachable from this door. A mapping
                // for a case that cannot occur was drafted, measured against the profile, and
                // removed rather than left to claim a path that does not exist.
                if (attempt.Kind == OfficialHttpAcquisitionOutcomeKind.ExecutedObservation)
                {
                    // The one place this driver deliberately differs from the EU one: a completed
                    // response at a retryable status is re-attempted rather than returned at once.
                    // The session's own PlanItem.IsRetryable already admits exactly these six
                    // statuses, so without this loop the profile's retry allowance was declared and
                    // never spent, and a single 503 would still have been reported downstream as
                    // "retry exhausted". Now that name is earned or not claimed.
                    if (attemptCount >= maximumAttempts || !IsRetryableStatus(attempt))
                    {
                        break;
                    }

                    continue;
                }

                var retryable = attempt.PreHeaderFailureClass is
                    HttpPreHeaderFailureClass.HeaderDeadline or
                    HttpPreHeaderFailureClass.TransportBeforeHeaders;
                if (!retryable || attemptCount >= maximumAttempts)
                {
                    return LuxembourgDocumentGetAttemptResult.Refused(
                        LuxembourgDocumentGetAttemptRefusal.ObservationNotExecuted,
                        $"{attempt.OperationalReason}/{attempt.PreHeaderFailureClass}");
                }
            }

            var evidence = attempt.Evidence!;

            // Decision 78 retention: a run holds what it depends on. The evidence document is
            // written and reopened by digest exactly as every other channel already does, so a
            // document GET's own evidence is retained custody too, never left to live only in this
            // process's memory.
            var evidenceBytes = evidence.CopyCanonicalBytes();
            var evidenceReceipt = await _custodyStore.CreateAsync(
                    evidenceBytes, CustodyClass.NightlyFloor90d, cancellationToken)
                .ConfigureAwait(false);
            var reopenedEvidenceBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore, evidenceReceipt.Reference.ContentSha256, cancellationToken)
                .ConfigureAwait(false);
            var reopenedEvidence = RoutedHttpEvidence.ParseAndVerify(reopenedEvidenceBytes.Span);

            return LuxembourgDocumentGetAttemptResult.Executed(
                reopenedEvidence, attemptCount >= maximumAttempts && IsRetryableStatus(attempt), robotsRoute);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
        {
            return LuxembourgDocumentGetAttemptResult.Refused(
                LuxembourgDocumentGetAttemptRefusal.ObservationNotExecuted, exception.Message);
        }
    }

    // A robots route written to custody and reopened by digest before anything relies on it (Decision 78).
    private async Task<SourceArtifactRef> HoldRobotsRouteAsync(RoutedHttpEvidence route, CancellationToken cancellationToken)
    {
        var bytes = route.CopyCanonicalBytes();
        var receipt = await _custodyStore.CreateAsync(bytes, CustodyClass.NightlyFloor90d, cancellationToken).ConfigureAwait(false);
        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, receipt.Reference.ContentSha256, cancellationToken)
            .ConfigureAwait(false);
        if (!reopened.Span.SequenceEqual(bytes))
            throw new CustodyIntegrityException("The retained robots route differs from the route the session fetched.");
        _ = RoutedHttpEvidence.ParseAndVerify(reopened.Span);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
    }
}
