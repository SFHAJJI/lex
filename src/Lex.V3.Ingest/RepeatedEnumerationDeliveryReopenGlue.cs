using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest;

/// <summary>
/// Which of the three failures <see cref="RepeatedEnumerationDeliveryReopenGlue.ObserveAsync"/>
/// itself can report. Closed, and deliberately narrower than any one executor's own full refusal
/// vocabulary (for example <c>Lex.V3.Ingest.Luxembourg.LuxembourgEnumerationRefusal</c>, which also
/// covers cursor continuation, delivery-proof and page-parsing refusals this glue never sees): this
/// enum names exactly the outcomes the shared HTTP-attempt loop below produces, so a caller
/// translates it into its own richer, publisher-specific refusal vocabulary rather than this glue
/// naming one publisher's vocabulary in its own signature.
/// </summary>
public enum ObservationAttemptFailureKind
{
    /// <summary>Every retryable attempt was spent, or the failure was not retryable at all.</summary>
    NotExecuted = 1,

    /// <summary>The terminal hop's status was not a derivable 200.</summary>
    StatusNotAdmitted = 2,

    /// <summary>The terminal hop's media type did not equal the profile's expected media type.</summary>
    MediaTypeNotAdmitted = 3,
}

/// <summary>
/// The raw data behind one <see cref="ObservationAttemptFailureKind"/>, carrying exactly the fields
/// a caller needs to reconstruct its own typed refusal detail byte-for-byte.
/// </summary>
public sealed record ObservationAttemptFailure(
    ObservationAttemptFailureKind Kind,
    ulong? AttemptOrdinalReached,
    int? TerminalStatus,
    string? ResponseBodySha256,
    string? ObservedMediaType,
    string? OperationalDetail);

/// <summary>One HTTP observation attempt's outcome: delivered transport, or a typed failure.</summary>
public sealed record ObservationAttemptOutcome(
    RepeatedEnumerationObservedTransport? Transport,
    ulong? RequestOrdinal,
    ObservationAttemptFailure? Failure);

/// <summary>
/// Queue item 19: the publisher-neutral delivery-reopen glue D1-04b's own Luxembourg adapter first
/// proved works, extracted here so a future EU executor (D1-05c-2) can reuse it instead of
/// duplicating it. Carries Decision 78 retention (a run holds what it depends on): both methods read
/// artifacts back out of the same <see cref="ICustodyStore"/> this glue is constructed with, never a
/// second, independently-held store.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ObserveAsync"/> moved here, unchanged in behavior, from
/// <c>Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor</c>'s own private method of the
/// same name: executes one bound request's attempt loop, admits only a terminal derivable 200 with
/// the expected media type, reads the logical request/write receipt/payload back out of custody
/// (never from memory), writes the HTTP evidence document, and reopens it by the digest the store
/// returned. Its only publisher-specific residue was the refusal detail it constructed on failure
/// (<c>LuxembourgEnumerationRefusalDetail</c>, whose full 14-member vocabulary spans cursor,
/// delivery-proof and page-parsing concerns this glue never touches); that is now
/// <see cref="ObservationAttemptFailure"/>, and the Luxembourg executor maps it back into its own
/// unchanged refusal type at the two call sites that used to build it directly, so nothing about
/// what a Luxembourg caller observes has changed.
/// </para>
/// <para>
/// <see cref="ReopenPageEvidenceAsync"/> moved here, unchanged in behavior, from
/// <c>Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionAdapter</c>'s own private static method of the
/// same name, mirroring the reopen
/// <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgDeliveryEvidenceSet.ResolveOneAsync</c> performs
/// for the executor's own in-process delivery. It was already publisher-neutral in signature; the
/// only change is dropping the <c>renderer</c> parameter the adapter used to satisfy with
/// <c>ResourceObservationPageRenderer</c>, a stub that only ever threw if called. Queue item 19's own
/// scope ruling resolved that without a stub: <see cref="RepeatedEnumerationResolvedEvidence.Renderer"/>
/// is now nullable, and a page reopened from custody after the fact carries a null one -- exactly
/// this method's own case, since <see cref="EnumerationDeliveryComparison.VerifyPages"/> (the only
/// check <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> runs over a page this method returns)
/// never reads <c>Renderer</c> at all.
/// </para>
/// </remarks>
public sealed class RepeatedEnumerationDeliveryReopenGlue
{
    private readonly ICustodyStore _custodyStore;

    public RepeatedEnumerationDeliveryReopenGlue(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    /// <summary>
    /// The shared per-observation routine. Executes the plan item, admits only a terminal derivable
    /// 200 with the expected media type, reads the logical request/write receipt/payload back out of
    /// custody (never from memory), writes the HTTP evidence document, and reopens it by the digest
    /// the store returned.
    /// </summary>
    /// <remarks>
    /// Internal, not public: <see cref="RoutedHttpAcquisitionSession"/> itself is <c>internal</c> to
    /// this assembly, so a public method could never accept one from outside it anyway. Every caller
    /// this glue exists for -- the Luxembourg executor today, a future EU executor under D1-05c-2 --
    /// lives in this same assembly (<c>Lex.V3.Ingest</c>), so internal visibility is exactly as reusable
    /// as public would be, without exposing a signature nothing outside this assembly could ever call.
    /// </remarks>
    internal Task<ObservationAttemptOutcome> ObserveAsync(
        RoutedHttpAcquisitionSession session,
        BoundMachineRequest request,
        RepeatedEnumerationInterpretationProfile profile,
        Dictionary<string, CustodyMembership> executorWrittenMembership,
        Func<int> currentCount,
        Action<int> setCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ObserveAsync(
            session, request, profile.ExpectedMediaType, executorWrittenMembership, currentCount, setCount,
            cancellationToken);
    }

    /// <summary>
    /// The same attempt loop as the <see cref="RepeatedEnumerationInterpretationProfile"/> overload
    /// above, taking the expected media type directly rather than a whole profile. Added for
    /// <c>Lex.V3.Ingest.Europe.EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync</c> (D1-05c-2
    /// defect 3's own fix): the witness's own <c>EuWatermarkWitnessPlan</c> deliberately does not bind
    /// to <see cref="RepeatedEnumerationInterpretationProfile"/> at all (that type requires a
    /// pre-count and a post-count over a partition, which a witness has neither), so a caller sending
    /// the witness's real HTTP request through this shared attempt loop cannot supply one. The
    /// original overload above is unchanged in behavior for every existing caller: it now simply
    /// forwards <c>profile.ExpectedMediaType</c>, the only field of <paramref name="profile"/> this
    /// method ever read.
    /// </summary>
    internal async Task<ObservationAttemptOutcome> ObserveAsync(
        RoutedHttpAcquisitionSession session,
        BoundMachineRequest request,
        string expectedMediaType,
        Dictionary<string, CustodyMembership> executorWrittenMembership,
        Func<int> currentCount,
        Action<int> setCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(expectedMediaType);
        ArgumentNullException.ThrowIfNull(executorWrittenMembership);
        ArgumentNullException.ThrowIfNull(currentCount);
        ArgumentNullException.ThrowIfNull(setCount);

        var item = session.OpenPlanItem(request);
        var maximumAttempts = session.SourceProfile.MaximumAttempts;
        var attemptOrdinal = 0;
        RoutedHttpAcquisitionSession.AttemptResult attempt;
        while (true)
        {
            attempt = await item.ExecuteNextAttemptAsync(cancellationToken).ConfigureAwait(false);
            attemptOrdinal++;
            setCount(currentCount() + 1);
            if (attempt.Kind == OfficialHttpAcquisitionOutcomeKind.ExecutedObservation)
            {
                break;
            }

            // Mirrors the one condition under which the session's own PlanItem allows another
            // attempt after a non-executed outcome (RoutedHttpAcquisitionSession.cs, PlanItem
            // .IsRetryable's pre-header branch): a failure before headers completed. Calling again
            // when this does not hold, or once the session's own attempt budget is spent, would
            // throw InvalidOperationException from ExecuteNextAttemptAsync itself; this predicate is
            // why it never does.
            var retryable = attempt.PreHeaderFailureClass is
                HttpPreHeaderFailureClass.HeaderDeadline or HttpPreHeaderFailureClass.TransportBeforeHeaders;
            if (!retryable || attemptOrdinal >= maximumAttempts)
            {
                return new ObservationAttemptOutcome(
                    null,
                    item.RequestOrdinal,
                    new ObservationAttemptFailure(
                        ObservationAttemptFailureKind.NotExecuted,
                        (ulong)attemptOrdinal, null, null, null,
                        $"{attempt.OperationalReason}/{attempt.PreHeaderFailureClass}"));
            }
        }

        var evidence = attempt.Evidence!;
        var terminal = evidence.Hops[^1];
        if (terminal.Status != 200 || terminal.StatusDisposition != HttpStatusDisposition.DerivableStatus)
        {
            return new ObservationAttemptOutcome(
                null,
                item.RequestOrdinal,
                new ObservationAttemptFailure(
                    ObservationAttemptFailureKind.StatusNotAdmitted,
                    null, terminal.Status, terminal.Sha256, null, null));
        }

        var observedMediaType = terminal.Headers.ContentType is RoutedHttpSingleHeader single ? single.Value : null;
        if (observedMediaType != expectedMediaType)
        {
            return new ObservationAttemptOutcome(
                null,
                item.RequestOrdinal,
                new ObservationAttemptFailure(
                    ObservationAttemptFailureKind.MediaTypeNotAdmitted,
                    null, terminal.Status, terminal.Sha256, observedMediaType, null));
        }

        var logicalRequestBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, terminal.LogicalRequestSha256, cancellationToken)
            .ConfigureAwait(false);
        var logicalRequest = HttpLogicalRequest.ParseAndVerify(logicalRequestBytes.Span);

        var writeReceiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, terminal.DurableWriteReceiptSha256, cancellationToken)
            .ConfigureAwait(false);
        var writeReceipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(
                new UTF8Encoding(false, true).GetString(writeReceiptBytes.Span))
            ?? throw new CustodyIntegrityException("The retained write receipt decoded to nothing.");

        var payload = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, terminal.Sha256, cancellationToken)
            .ConfigureAwait(false);

        // Write the evidence document, then take the digest FROM THE STORE'S OWN RECEIPT rather
        // than from a value this run computed itself, and reopen exactly that digest before
        // trusting it.
        var evidenceBytes = evidence.CopyCanonicalBytes();
        var evidenceReceipt = await _custodyStore.CreateAsync(
                evidenceBytes, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);
        var evidenceDigest = evidenceReceipt.Reference.ContentSha256;
        var reopenedEvidenceBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, evidenceDigest, cancellationToken)
            .ConfigureAwait(false);
        var reopenedEvidence = RoutedHttpEvidence.ParseAndVerify(reopenedEvidenceBytes.Span);
        executorWrittenMembership[evidenceDigest] = CustodyMembershipClassifier.Classify(evidenceReceipt);

        var transport = new RepeatedEnumerationObservedTransport(
            logicalRequest, reopenedEvidence, writeReceipt, payload);
        return new ObservationAttemptOutcome(transport, item.RequestOrdinal, null);
    }

    /// <summary>
    /// Reopens one page's full evidence tuple from custody by the digests <paramref name="refs"/>
    /// names. <see cref="RepeatedEnumerationResolvedEvidence.Renderer"/> is always null on the
    /// result: this door never has, and never needs, a real renderer (see the type remarks).
    /// </summary>
    public async Task<RepeatedEnumerationResolvedEvidence> ReopenPageEvidenceAsync(
        RepeatedEnumerationEvidenceRefs refs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refs);

        var planBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, refs.QueryPlanRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var queryPlan = DecodeCanonical<MachineQueryPlan>(
            planBytes.Span, MachineQueryPlanIdentity.CanonicalizationIdentity, "the machine query plan");
        try
        {
            MachineQueryPlanIdentity.Validate(refs.QueryPlanRef, queryPlan);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained machine query plan does not reproduce its own canonical bytes.", exception);
        }

        var inputBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, refs.QueryInputRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        MachineQueryInputArtifact queryInput;
        try
        {
            queryInput = MachineQueryInputArtifact.ParseAndVerify(refs.QueryInputRef, inputBytes.Span);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained machine query input does not bind its reference.", exception);
        }

        var receiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, refs.RenderReceiptRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var renderReceipt = DecodeCanonical<MachineQueryRenderReceipt>(
            receiptBytes.Span, MachineQueryRenderReceiptIdentity.CanonicalizationIdentity,
            "the machine query render receipt");
        try
        {
            MachineQueryRenderReceiptIdentity.Validate(refs.RenderReceiptRef, renderReceipt);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained render receipt does not reproduce its own canonical bytes.", exception);
        }

        var logicalRequestBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, refs.LogicalRequestRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        HttpLogicalRequest logicalRequest;
        try
        {
            logicalRequest = HttpLogicalRequest.ParseAndVerify(logicalRequestBytes.Span);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained logical request does not parse as its exact canonical form.", exception);
        }

        var httpEvidenceBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, refs.HttpEvidenceRef.Sha256, cancellationToken)
            .ConfigureAwait(false);
        RoutedHttpEvidence httpEvidence;
        try
        {
            httpEvidence = RoutedHttpEvidence.ParseAndVerify(httpEvidenceBytes.Span);
        }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException(
                "The retained HTTP evidence does not parse as its exact canonical form.", exception);
        }

        if (httpEvidence.Hops.Count != 1)
        {
            throw new CustodyIntegrityException("The retained HTTP evidence no longer names exactly one hop.");
        }

        var terminal = httpEvidence.Hops[0];
        var writeReceiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, terminal.DurableWriteReceiptSha256, cancellationToken)
            .ConfigureAwait(false);
        var writeReceipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(
                new UTF8Encoding(false, true).GetString(writeReceiptBytes.Span))
            ?? throw new CustodyIntegrityException("The retained write receipt decoded to nothing.");

        var payload = await CustodyRestore.ReadByDigestCheckedAsync(
                _custodyStore, terminal.Sha256, cancellationToken)
            .ConfigureAwait(false);

        return new RepeatedEnumerationResolvedEvidence(
            queryPlan, queryInput, renderReceipt, null, logicalRequest, httpEvidence, writeReceipt, payload);
    }

    /// <summary>
    /// Decodes bytes shaped like <c>MachineQueryPlanIdentity.GetCanonicalBytes</c> and
    /// <c>MachineQueryRenderReceiptIdentity.GetCanonicalBytes</c> produce: an ASCII canonicalization
    /// identity line, then the canonical JSON, then a trailing newline. The framing constants
    /// (<paramref name="canonicalizationIdentity"/>) are public; only the internal
    /// <c>ContractCanonicalizer</c> that originally wrote this shape is not, so this decodes the
    /// public envelope directly rather than reaching for that internal type.
    /// </summary>
    /// <remarks>
    /// Internal rather than private: <see cref="Lex.V3.Ingest.Europe.EuDeliveryEvidenceSet"/>'s own
    /// <c>ResolveOneAsync</c> (same assembly) reuses this exact decode rather than carrying its own
    /// duplicate, since the two calls decode the identical canonical-bytes shape over the identical
    /// artifact types (<c>MachineQueryPlan</c>, <c>MachineQueryRenderReceipt</c>). Not public: nothing
    /// outside this assembly needs it, and every caller this glue exists for already lives in
    /// <c>Lex.V3.Ingest</c>.
    /// </remarks>
    internal static T DecodeCanonical<T>(ReadOnlySpan<byte> bytes, string canonicalizationIdentity, string what)
    {
        string decoded;
        try
        {
            decoded = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CustodyIntegrityException($"The retained bytes for {what} are not valid UTF-8.", exception);
        }

        var prefix = canonicalizationIdentity + "\n";
        if (!decoded.StartsWith(prefix, StringComparison.Ordinal) ||
            !decoded.EndsWith('\n') ||
            decoded.Length < prefix.Length + 1)
        {
            throw new CustodyIntegrityException(
                $"The retained bytes for {what} do not carry their canonicalization identity.");
        }

        var json = decoded[prefix.Length..^1];
        try
        {
            return ContractJson.Deserialize<T>(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new CustodyIntegrityException($"The retained bytes are not {what}.", exception);
        }
    }
}
