using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// D1-05c-2 precision one: the EU executor owns only its own pass loop, cursor handling and the EU
/// dialect count parse. Retention, reopening and the receipt itself come from item 19's shared glue
/// (<see cref="RepeatedEnumerationDeliveryReopenGlue"/>) and Core (<see cref="EnumerationDeliveryComparison"/>,
/// <see cref="RepeatedEnumerationDeliveryReceipt"/>), never copied. Modeled file-for-file on
/// <c>Lex.V3.Ingest.Luxembourg.LuxembourgRepeatedEnumerationExecutor</c>; the differences are exactly
/// the ones proposal B names: the cursor type (a raw ordered string tuple here rather than a typed
/// six-field cursor, because the four EU query sets this executor drives carry four different cursor
/// arities: one part for the census family's <c>state_key</c>, five for the root-watermark family,
/// six for object-facts, seven for expression-facts), the partition identity (a VALUES-bound batch of
/// canonical object IRIs for the three D1-05c-1 families, so "row outside partition" is set
/// membership over the batch rather than a numeric range), and the count wire shape (the EU Cellar
/// SPARQL endpoint's Virtuoso instance answers a COUNT with a plain <c>"literal"</c> term carrying an
/// explicit <c>xsd:integer</c> datatype, never LU's <c>"typed-literal"</c> token -- confirmed against
/// <see cref="EnumerationDeliveryComparison"/>'s own dialect-keyed <c>ParseCount</c>, which already
/// requires exactly this split for <see cref="RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso"/>
/// vs <see cref="RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso"/>).
/// </summary>
public enum EuEnumerationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("robots_bootstrap_refused")]
    RobotsBootstrapRefused = 1,

    /// <summary>
    /// The acquisition run's own bootstrap artifacts are not held under an enforced floor, so this
    /// store cannot produce a payload receipt Source/Core will bind. Observed from the session's
    /// membership after robots and before the first product request.
    /// </summary>
    [JsonStringEnumMemberName("custody_floor_not_observed")]
    CustodyFloorNotObserved = 2,

    [JsonStringEnumMemberName("observation_not_executed")]
    ObservationNotExecuted = 3,

    [JsonStringEnumMemberName("status_not_admitted")]
    StatusNotAdmitted = 4,

    [JsonStringEnumMemberName("media_type_not_admitted")]
    MediaTypeNotAdmitted = 5,

    [JsonStringEnumMemberName("count_not_one_nonnegative_integer")]
    CountNotOneNonNegativeInteger = 6,

    [JsonStringEnumMemberName("partition_required")]
    PartitionRequired = 7,

    [JsonStringEnumMemberName("delivered_key_not_representable")]
    DeliveredKeyNotRepresentable = 8,

    /// <summary>
    /// A delivered page row named a batch-selection term (the object-facts/root-watermark families'
    /// own <c>?object</c>, or the expression-facts family's own <c>?parent</c>) outside the exact
    /// canonical object set this partition's VALUES clause named, or, for the census family, a
    /// <c>base_celex</c> outside the exact requested seed. This is the EU counterpart of LU's
    /// row-outside-partition check: the query's own VALUES/BIND clause should make this
    /// unreachable, but the executor still checks the delivered shape rather than trusting it,
    /// exactly as the range check does for a keyset partition.
    /// </summary>
    [JsonStringEnumMemberName("delivered_row_outside_partition")]
    DeliveredRowOutsidePartition = 9,

    [JsonStringEnumMemberName("cursor_did_not_advance")]
    CursorDidNotAdvance = 10,

    [JsonStringEnumMemberName("page_budget_exhausted")]
    PageBudgetExhausted = 11,

    [JsonStringEnumMemberName("custody_member_missing")]
    CustodyMemberMissing = 12,

    [JsonStringEnumMemberName("delivery_proof_refused")]
    DeliveryProofRefused = 13,

    /// <summary>
    /// A page body admitted by status and media type is not a SPARQL results document this executor
    /// can read at all: not JSON, or JSON without the results/bindings array.
    /// </summary>
    [JsonStringEnumMemberName("page_body_malformed")]
    PageBodyMalformed = 14,
}

public sealed class EuEnumerationRefusalDetail
{
    internal EuEnumerationRefusalDetail(
        EuEnumerationRefusal code,
        ulong? requestOrdinal,
        ulong? attemptOrdinalReached,
        int? terminalStatus,
        string? responseBodySha256,
        string? observedMediaType,
        long? observedCount,
        string? offendingKey,
        string? coreRefusalDetail)
    {
        if (code == EuEnumerationRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "A refusal detail requires a real refusal code.");
        }

        Code = code;
        RequestOrdinal = requestOrdinal;
        AttemptOrdinalReached = attemptOrdinalReached;
        TerminalStatus = terminalStatus;
        ResponseBodySha256 = responseBodySha256;
        ObservedMediaType = observedMediaType;
        ObservedCount = observedCount;
        OffendingKey = offendingKey;
        CoreRefusalDetail = coreRefusalDetail;
    }

    public EuEnumerationRefusal Code { get; }

    public ulong? RequestOrdinal { get; }

    public ulong? AttemptOrdinalReached { get; }

    public int? TerminalStatus { get; }

    /// <summary>The custody digest of the body this run refused on, when one was retained.</summary>
    public string? ResponseBodySha256 { get; }

    public string? ObservedMediaType { get; }

    public long? ObservedCount { get; }

    /// <summary>The offending delivered key, for <see cref="EuEnumerationRefusal.DeliveredRowOutsidePartition"/>.</summary>
    public string? OffendingKey { get; }

    /// <summary>Source/Core's own refusal message, verbatim, never a paraphrase.</summary>
    public string? CoreRefusalDetail { get; }
}

/// <summary>Delivered or refused, never both and never neither.</summary>
public sealed class EuEnumerationRunResult
{
    private EuEnumerationRunResult(
        RepeatedEnumerationDeliveryReceipt? receipt,
        EuEnumerationRefusalDetail? refusal,
        int productRequestCount)
    {
        if (productRequestCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productRequestCount));
        }

        Receipt = receipt;
        Refusal = refusal;
        ProductRequestCount = productRequestCount;
    }

    public static EuEnumerationRunResult Delivered(RepeatedEnumerationDeliveryReceipt receipt, int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new(receipt, null, productRequestCount);
    }

    public static EuEnumerationRunResult Refused(EuEnumerationRefusalDetail detail, int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new(null, detail, productRequestCount);
    }

    public RepeatedEnumerationDeliveryReceipt? Receipt { get; }

    public EuEnumerationRefusalDetail? Refusal { get; }

    /// <summary>Publisher requests this run spent, robots excluded. Always populated.</summary>
    public int ProductRequestCount { get; }
}

/// <summary>
/// One request to enumerate D1-05a's own census family (<see cref="EuConsolidationQuerySet.Family"/>)
/// for exactly one admitted Appendix A seed CELEX. Reused unchanged, never re-queried, per D1-05c-1's
/// own decode contract.
/// </summary>
public sealed record EuCensusPartitionRunRequest(
    EuConsolidationDiscoveryPlan Plan,
    string PlanResourceId,
    string RequestedCelex,
    MachineQueryRendererSource RendererSource);

/// <summary>
/// One request to enumerate one of the three D1-05c-1 object-facts families
/// (<see cref="EuObjectFactsQuerySet"/>) for exactly one VALUES-bound batch of canonical object IRIs.
/// </summary>
public sealed record EuObjectFactsPartitionRunRequest(
    EuObjectFactsDiscoveryPlan Plan,
    string PlanResourceId,
    EuObjectFactsQuerySet Set,
    IReadOnlyList<string> BatchObjects,
    MachineQueryRendererSource RendererSource);

/// <summary>
/// D1-05c-2: the EU repeated-enumeration executor. See the type's own summary above for exactly what
/// it owns and what it deliberately reuses from item 19 and Core rather than reimplementing.
/// </summary>
public sealed class EuRepeatedEnumerationExecutor
{
    private readonly ICustodyStore _custodyStore;
    private readonly TimeProvider _timeProvider;
    private readonly System.Net.Http.HttpMessageHandler? _testHandlerOverride;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public EuRepeatedEnumerationExecutor(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, testHandlerOverride: null)
    {
    }

    /// <summary>
    /// Test-only seam, mirroring the identical seam on the Luxembourg executor: when supplied, the
    /// run's transport uses this handler instead of the real network. Production code only ever calls
    /// the public constructor.
    /// </summary>
    internal EuRepeatedEnumerationExecutor(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHandlerOverride = testHandlerOverride;
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(_custodyStore);
    }

    /// <summary>One partition of D1-05a's census family, one session, two passes.</summary>
    public async Task<EuEnumerationRunResult> RunCensusPartitionAsync(
        EuCensusPartitionRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var session = await StartSessionAsync(sourceWitness, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return EuEnumerationRunResult.Refused(
                new EuEnumerationRefusalDetail(
                    EuEnumerationRefusal.RobotsBootstrapRefused, null, null, null, null, null, null, null, null),
                productRequestCount: 0);
        }

        try
        {
            var profile = request.Plan.CreateDeliveryProfile(EuConsolidationQuerySet.Family);
            var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(NewUrn(), profile);
            return await RunPassesAsync(
                    session,
                    profile,
                    profileRef,
                    pass => BindCensusCount(request, pass),
                    (pass, cursor, selected, evidenceRef) => BindCensusPage(request, pass, cursor, selected, evidenceRef),
                    batchObjects: null,
                    batchMembershipKeyOrdinal: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>One partition (one VALUES batch) of one D1-05c-1 object-facts family, one session, two passes.</summary>
    public async Task<EuEnumerationRunResult> RunObjectFactsPartitionAsync(
        EuObjectFactsPartitionRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var session = await StartSessionAsync(sourceWitness, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return EuEnumerationRunResult.Refused(
                new EuEnumerationRefusalDetail(
                    EuEnumerationRefusal.RobotsBootstrapRefused, null, null, null, null, null, null, null, null),
                productRequestCount: 0);
        }

        try
        {
            var profile = request.Plan.CreateDeliveryProfile(request.Set);
            var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(NewUrn(), profile);
            var batchMembershipOrdinal = BatchMembershipKeyOrdinal(profile, request.Set);
            return await RunPassesAsync(
                    session,
                    profile,
                    profileRef,
                    pass => BindObjectFactsCount(request, pass),
                    (pass, cursor, selected, evidenceRef) => BindObjectFactsPage(request, pass, cursor, selected, evidenceRef),
                    batchObjects: request.BatchObjects,
                    batchMembershipKeyOrdinal: batchMembershipOrdinal,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            session.Dispose();
        }
    }

    private async Task<RoutedHttpAcquisitionSession?> StartSessionAsync(
        BoundMachineRequest sourceWitness, CancellationToken cancellationToken)
    {
        var start = _testHandlerOverride is null
            ? await RoutedHttpAcquisitionSession.StartAsync(sourceWitness, _custodyStore, cancellationToken)
                .ConfigureAwait(false)
            : await RoutedHttpAcquisitionSession.StartWithTestTransportAsync(
                    sourceWitness, _custodyStore, _testHandlerOverride, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        return start.Kind == OfficialHttpAcquisitionOutcomeKind.ExecutedObservation ? start.Session : null;
    }

    /// <summary>
    /// The shared two-pass driving loop, generalized over the census family's single-CELEX selection
    /// and the object-facts families' VALUES-batch selection through the two binder delegates a
    /// caller supplies. This is the executor's one real pass loop; both public entry points above
    /// reduce to it.
    /// </summary>
    private async Task<EuEnumerationRunResult> RunPassesAsync(
        RoutedHttpAcquisitionSession session,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef profileRef,
        Func<int, EuBoundQueryParts> bindCount,
        Func<int, IReadOnlyList<string>?, long, SourceArtifactRef, EuBoundQueryParts> bindPage,
        IReadOnlyList<string>? batchObjects,
        int? batchMembershipKeyOrdinal,
        CancellationToken cancellationToken)
    {
        var productRequestCount = 0;
        try
        {
            var bootstrapMembership = session.CopyArtifactMembership();
            var unenforced = bootstrapMembership
                .Where(static entry => entry.Value != CustodyMembership.Floored)
                .Select(static entry => entry.Key)
                .ToArray();
            if (unenforced.Length > 0)
            {
                return EuEnumerationRunResult.Refused(
                    new EuEnumerationRefusalDetail(
                        EuEnumerationRefusal.CustodyFloorNotObserved,
                        null, null, null, null, null, null, null,
                        string.Join(",", unenforced)),
                    productRequestCount: 0);
            }

            var executorWrittenMembership = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
            EuDeliveryPass? passA = null;
            EuDeliveryPass? passB = null;
            string? partitionKey = null;

            for (var passOrdinal = 1; passOrdinal <= 2; passOrdinal++)
            {
                var passResult = await RunOnePassAsync(
                        session, profile, bindCount, bindPage, passOrdinal,
                        batchObjects, batchMembershipKeyOrdinal,
                        executorWrittenMembership,
                        () => productRequestCount, count => productRequestCount = count,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (passResult.Refusal is not null)
                {
                    return EuEnumerationRunResult.Refused(passResult.Refusal, productRequestCount);
                }

                partitionKey ??= passResult.PartitionKey;
                if (passOrdinal == 1)
                {
                    passA = passResult.Pass;
                }
                else
                {
                    passB = passResult.Pass;
                }
            }

            var evidenceSet = await EuDeliveryEvidenceSet.MaterializeAsync(
                    profile, profileRef, passA!, passB!, _custodyStore, cancellationToken)
                .ConfigureAwait(false);

            var receipt = evidenceSet.TryCompareAndReceipt(
                session.CopyArtifactMembership(), executorWrittenMembership, out var receiptRefusal);
            if (receipt is null)
            {
                return EuEnumerationRunResult.Refused(
                    new EuEnumerationRefusalDetail(
                        EuEnumerationRefusal.DeliveryProofRefused,
                        null, null, null, null, null, null, null,
                        evidenceSet.LastCoreRefusalMessage ?? receiptRefusal.ToString()),
                    productRequestCount);
            }

            return EuEnumerationRunResult.Delivered(receipt, productRequestCount);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
        {
            return EuEnumerationRunResult.Refused(
                new EuEnumerationRefusalDetail(
                    EuEnumerationRefusal.CustodyMemberMissing,
                    null, null, null, null, null, null, null, exception.Message),
                productRequestCount);
        }
    }

    private sealed record PassOutcome(EuDeliveryPass? Pass, string? PartitionKey, EuEnumerationRefusalDetail? Refusal);

    private async Task<PassOutcome> RunOnePassAsync(
        RoutedHttpAcquisitionSession session,
        RepeatedEnumerationInterpretationProfile profile,
        Func<int, EuBoundQueryParts> bindCount,
        Func<int, IReadOnlyList<string>?, long, SourceArtifactRef, EuBoundQueryParts> bindPage,
        int passOrdinal,
        IReadOnlyList<string>? batchObjects,
        int? batchMembershipKeyOrdinal,
        Dictionary<string, CustodyMembership> executorWrittenMembership,
        Func<int> currentCount,
        Action<int> setCount,
        CancellationToken cancellationToken)
    {
        var countBound = bindCount(passOrdinal);
        var partitionKey = countBound.PartitionKey;
        var countOutcome = ToObserveOutcome(await _reopenGlue.ObserveAsync(
                session, countBound.Request, profile, executorWrittenMembership, currentCount, setCount, cancellationToken)
            .ConfigureAwait(false));
        if (countOutcome.Refusal is not null)
        {
            return new PassOutcome(null, partitionKey, countOutcome.Refusal);
        }

        long selected;
        try
        {
            selected = ParseStrictEuCount(countOutcome.Transport!.RetainedPayloadBytes.Span, profile);
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            return new PassOutcome(
                null,
                partitionKey,
                new EuEnumerationRefusalDetail(
                    EuEnumerationRefusal.CountNotOneNonNegativeInteger,
                    countOutcome.RequestOrdinal, null,
                    countOutcome.Transport!.HttpEvidence.Hops[0].Status,
                    countOutcome.Transport.HttpEvidence.Hops[0].Sha256,
                    null, null, null, null));
        }

        if (EnumerationDeliveryComparison.AssessThreshold(selected, profile) ==
            RepeatedEnumerationThresholdAssessment.PartitionRequired)
        {
            return new PassOutcome(
                null,
                partitionKey,
                new EuEnumerationRefusalDetail(
                    EuEnumerationRefusal.PartitionRequired,
                    countOutcome.RequestOrdinal, null, null, null, null, selected, null, null));
        }

        var countIdentity = RepeatedEnumerationObservationIdentity.NewObservation();
        var countObservation = EuDeliveryObservation.ForRequest(
            countBound.MachinePlanRef, countBound.InputRef, countBound.Request, countIdentity,
            countOutcome.Transport, profile);
        var deliveryPass = EuDeliveryPass.BeginWithCount(countObservation, selected);

        IReadOnlyList<string>? cursor = null;
        long? pageLimit = null;
        while (true)
        {
            // The exact per-pass row limit is a plan-internal constant this path claim cannot read
            // directly (EuConsolidationDiscoveryPlan.Pass1PageLimit/Pass2PageLimit are internal to
            // Lex.V3.Contracts); it is instead read off the first bound page's own response
            // cardinality, exactly as Core's own VerifyPages derives its local `limit` lazily from
            // the first page it resolves rather than requiring it as a separate input. The budget
            // can never be exhausted before the first page (every pass sends at least one), so
            // deferring the check until the limit is known changes nothing observable.
            if (pageLimit is { } knownLimit && deliveryPass.Pages.Count >= MaximumPagesFor(knownLimit, selected))
            {
                return new PassOutcome(
                    null,
                    partitionKey,
                    new EuEnumerationRefusalDetail(
                        EuEnumerationRefusal.PageBudgetExhausted,
                        null, null, null, null, null, null, null, null));
            }

            var pageBound = bindPage(passOrdinal, cursor, selected, countObservation.HttpEvidenceRef);
            pageLimit ??= pageBound.PageRowLimit
                ?? throw new InvalidOperationException("A page-shaped bound query must carry a row limit.");
            var pageOutcome = ToObserveOutcome(await _reopenGlue.ObserveAsync(
                    session, pageBound.Request, profile, executorWrittenMembership, currentCount, setCount, cancellationToken)
                .ConfigureAwait(false));
            if (pageOutcome.Refusal is not null)
            {
                return new PassOutcome(null, partitionKey, pageOutcome.Refusal);
            }

            var transport = pageOutcome.Transport!;
            IReadOnlyList<string[]> rows;
            try
            {
                rows = ParseStrictRows(transport.RetainedPayloadBytes.Span, profile.CursorVariables);
            }
            catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
            {
                return new PassOutcome(
                    null,
                    partitionKey,
                    new EuEnumerationRefusalDetail(
                        ClassifyPageParseFailure(exception),
                        pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                        transport.HttpEvidence.Hops[0].Sha256, null, null, null, null));
            }

            if (rows.Count > 0)
            {
                for (var index = 1; index < rows.Count; index++)
                {
                    if (CompareKeys(rows[index - 1], rows[index]) >= 0)
                    {
                        return new PassOutcome(
                            null,
                            partitionKey,
                            new EuEnumerationRefusalDetail(
                                EuEnumerationRefusal.CursorDidNotAdvance,
                                pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                                transport.HttpEvidence.Hops[0].Sha256, null, null, null, null));
                    }
                }

                var candidate = rows[^1];
                if (cursor is not null && CompareKeys(cursor, candidate) >= 0)
                {
                    return new PassOutcome(
                        null,
                        partitionKey,
                        new EuEnumerationRefusalDetail(
                            EuEnumerationRefusal.CursorDidNotAdvance,
                            pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                            transport.HttpEvidence.Hops[0].Sha256, null, null, null, null));
                }

                // The EU counterpart of LU's partition-range check: a delivered row must name a
                // batch-selection term this partition actually requested. For the census family the
                // selection is the one requested seed CELEX (unreachable except through a malformed
                // publisher response, since every row is guarded by RequireCelex downstream too; kept
                // here as the executor's own defense, exactly mirroring LU's own range check).
                if (batchMembershipKeyOrdinal is { } ordinal)
                {
                    var member = candidate[ordinal];
                    if (batchObjects is not null && !batchObjects.Contains(member, StringComparer.Ordinal))
                    {
                        return new PassOutcome(
                            null,
                            partitionKey,
                            new EuEnumerationRefusalDetail(
                                EuEnumerationRefusal.DeliveredRowOutsidePartition,
                                pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                                transport.HttpEvidence.Hops[0].Sha256, null, null, member, null));
                    }
                }

                cursor = candidate;
            }

            var pageObservation = EuDeliveryObservation.ForRequest(
                pageBound.MachinePlanRef, pageBound.InputRef, pageBound.Request,
                RepeatedEnumerationObservationIdentity.NewObservation(), transport, profile);
            deliveryPass = deliveryPass.WithPage(pageObservation);

            if (rows.Count == 0)
            {
                break;
            }
        }

        return new PassOutcome(deliveryPass, partitionKey, null);
    }

    /// <summary>
    /// Which zero-based cursor part carries the family's own VALUES-bound selection term:
    /// <c>key_1</c> (<c>STR(?object)</c>) for object-facts and root-watermark, <c>key_7</c>
    /// (<c>STR(?parent)</c>) for expression-facts, since that family's own <c>?object</c>
    /// (<c>key_1</c>) is the discovered Expression, never a batch member itself.
    /// </summary>
    /// <remarks>
    /// Small fold-in. Looked up by NAME in <paramref name="profile"/>'s own
    /// <see cref="RepeatedEnumerationInterpretationProfile.CursorVariables"/> rather than as a
    /// hardcoded numeric index: the cursor-variable NAME (<c>key_1</c> or <c>key_7</c>) is the one
    /// fact this executor actually knows about which part carries the selection term: its own
    /// ordinal position within one query set's cursor is a property of
    /// <see cref="EuObjectFactsDiscoveryPlan"/>'s own (internal) cursor construction, not something
    /// this file should assume never shifts.
    /// </remarks>
    private static int BatchMembershipKeyOrdinal(
        RepeatedEnumerationInterpretationProfile profile, EuObjectFactsQuerySet set)
    {
        var variableName = set switch
        {
            EuObjectFactsQuerySet.ObjectFacts => "key_1",
            EuObjectFactsQuerySet.ExpressionFacts => "key_7",
            EuObjectFactsQuerySet.RootWatermark => "key_1",
            _ => throw new ArgumentOutOfRangeException(nameof(set)),
        };

        var cursorVariables = profile.CursorVariables;
        for (var index = 0; index < cursorVariables.Count; index++)
        {
            if (string.Equals(cursorVariables[index], variableName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new ArgumentException(
            $"'{variableName}' is not part of this profile's own cursor variables.", nameof(profile));
    }

    private static EuBoundQueryParts BindCensusCount(EuCensusPartitionRunRequest request, int passOrdinal)
    {
        var bound = request.Plan.BindCount(
            EuConsolidationQuerySet.Family,
            request.RequestedCelex,
            (EuConsolidationQueryPass)passOrdinal,
            request.PlanResourceId,
            NewUrn(),
            request.RendererSource);
        return new EuBoundQueryParts(
            bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request,
            bound.InputArtifact.PartitionBinding.MemberKey, null);
    }

    private static EuBoundQueryParts BindCensusPage(
        EuCensusPartitionRunRequest request,
        int passOrdinal,
        IReadOnlyList<string>? cursor,
        long selected,
        SourceArtifactRef countEvidenceRef)
    {
        var bound = request.Plan.BindPage(
            EuConsolidationQuerySet.Family,
            request.RequestedCelex,
            (EuConsolidationQueryPass)passOrdinal,
            cursor,
            selected,
            countEvidenceRef,
            request.PlanResourceId,
            NewUrn(),
            request.RendererSource);
        return new EuBoundQueryParts(
            bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request,
            bound.InputArtifact.PartitionBinding.MemberKey, bound.MachinePlan.ResponseCardinality.RowLimit);
    }

    private static EuBoundQueryParts BindObjectFactsCount(EuObjectFactsPartitionRunRequest request, int passOrdinal)
    {
        var bound = request.Plan.BindCount(
            request.Set,
            request.BatchObjects,
            (EuObjectFactsQueryPass)passOrdinal,
            request.PlanResourceId,
            NewUrn(),
            request.RendererSource);
        return new EuBoundQueryParts(
            bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request,
            bound.InputArtifact.PartitionBinding.MemberKey, null);
    }

    private static EuBoundQueryParts BindObjectFactsPage(
        EuObjectFactsPartitionRunRequest request,
        int passOrdinal,
        IReadOnlyList<string>? cursor,
        long selected,
        SourceArtifactRef countEvidenceRef)
    {
        var bound = request.Plan.BindPage(
            request.Set,
            request.BatchObjects,
            (EuObjectFactsQueryPass)passOrdinal,
            cursor,
            selected,
            countEvidenceRef,
            request.PlanResourceId,
            NewUrn(),
            request.RendererSource);
        return new EuBoundQueryParts(
            bound.MachinePlanRef, bound.InputArtifact.ArtifactRef, bound.Request,
            bound.InputArtifact.PartitionBinding.MemberKey, bound.MachinePlan.ResponseCardinality.RowLimit);
    }

    /// <summary>
    /// Exact, mirroring <c>LuxembourgEnumerationBudget.MaximumPagesFor</c>: under
    /// <c>EmptySuccessorAfterShortPage</c>, 1 when selected is 0, selected/limit + 1 when it divides,
    /// else selected/limit + 2.
    /// </summary>
    private static long MaximumPagesFor(long pageLimit, long selectedRowCount)
    {
        if (selectedRowCount == 0)
        {
            return 1;
        }

        var quotient = selectedRowCount / pageLimit;
        var remainder = selectedRowCount % pageLimit;
        return remainder == 0 ? quotient + 1 : quotient + 2;
    }

    private sealed record ObserveOutcome(
        RepeatedEnumerationObservedTransport? Transport,
        ulong? RequestOrdinal,
        EuEnumerationRefusalDetail? Refusal);

    private static ObserveOutcome ToObserveOutcome(ObservationAttemptOutcome outcome)
    {
        if (outcome.Failure is not { } failure)
        {
            return new ObserveOutcome(outcome.Transport, outcome.RequestOrdinal, null);
        }

        var code = failure.Kind switch
        {
            ObservationAttemptFailureKind.NotExecuted => EuEnumerationRefusal.ObservationNotExecuted,
            ObservationAttemptFailureKind.StatusNotAdmitted => EuEnumerationRefusal.StatusNotAdmitted,
            ObservationAttemptFailureKind.MediaTypeNotAdmitted => EuEnumerationRefusal.MediaTypeNotAdmitted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome), $"Unreachable: an unhandled {nameof(ObservationAttemptFailureKind)} '{failure.Kind}'."),
        };
        return new ObserveOutcome(
            null,
            outcome.RequestOrdinal,
            new EuEnumerationRefusalDetail(
                code,
                outcome.RequestOrdinal,
                failure.AttemptOrdinalReached,
                failure.TerminalStatus,
                failure.ResponseBodySha256,
                failure.ObservedMediaType,
                null,
                null,
                failure.OperationalDetail));
    }

    private const string PageParseFailureKey = "eu.pageParseFailure";

    private static EuEnumerationRefusal ClassifyPageParseFailure(Exception exception) =>
        exception.Data[PageParseFailureKey] switch
        {
            nameof(EuEnumerationRefusal.DeliveredKeyNotRepresentable) =>
                EuEnumerationRefusal.DeliveredKeyNotRepresentable,
            _ => EuEnumerationRefusal.PageBodyMalformed,
        };

    /// <summary>
    /// The EU dialect's own strict count parse (D1-05c-2 precision one). The EU Cellar SPARQL
    /// endpoint's Virtuoso instance answers a bounded COUNT with a plain <c>"literal"</c> term
    /// carrying an explicit <c>xsd:integer</c> datatype qualifier, never the <c>"typed-literal"</c>
    /// token the LU executor's own <c>ParseStrictCount</c> hard-codes.
    /// </summary>
    /// <remarks>
    /// Small fold-in. The expected wire-type token is read off <paramref name="profile"/>'s own
    /// <see cref="RepeatedEnumerationInterpretationProfile.Dialect"/> the exact same way
    /// <see cref="EnumerationDeliveryComparison"/>'s own dialect-keyed <c>ParseCount</c> derives it
    /// (<c>"typed-literal"</c> for <see cref="RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso"/>,
    /// <c>"literal"</c> for every other dialect), rather than as a second, independent literal that
    /// happens to agree with Core's own check only because this executor is never handed anything but
    /// an EU profile today.
    /// </remarks>
    private static long ParseStrictEuCount(ReadOnlySpan<byte> bytes, RepeatedEnumerationInterpretationProfile profile)
    {
        var expectedWireType = profile.Dialect == RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso
            ? "typed-literal"
            : "literal";

        using var document = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) ||
            results.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !results.TryGetProperty("bindings", out var bindings) ||
            bindings.ValueKind != System.Text.Json.JsonValueKind.Array ||
            bindings.GetArrayLength() != 1)
        {
            throw new FormatException("The count response is not exactly one binding row.");
        }

        var binding = bindings[0];
        if (binding.ValueKind != System.Text.Json.JsonValueKind.Object ||
            binding.EnumerateObject().Count() != 1)
        {
            throw new FormatException("The count response binding is not exactly one term.");
        }

        var term = binding.EnumerateObject().First().Value;
        if (term.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !term.TryGetProperty("type", out var type) ||
            type.ValueKind != System.Text.Json.JsonValueKind.String ||
            type.GetString() != expectedWireType ||
            !term.TryGetProperty("datatype", out var datatype) ||
            datatype.ValueKind != System.Text.Json.JsonValueKind.String ||
            datatype.GetString() != "http://www.w3.org/2001/XMLSchema#integer" ||
            !term.TryGetProperty("value", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new FormatException("The count term is not one typed nonnegative integer EU literal.");
        }

        if (!long.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var count))
        {
            throw new FormatException("The count value is not one nonnegative integer.");
        }

        return count;
    }

    /// <summary>
    /// Reads exactly <paramref name="cursorVariables"/>'s own string values from each delivered row,
    /// generically across the four cursors this executor drives (one for the census family's own
    /// <c>state_key</c>, five for root-watermark, six for object-facts, seven for expression-facts).
    /// Deliberately shallow, mirroring the LU executor's own <c>ParseStrictRows</c>: it reads only as
    /// far as the executor's own driving needs (cursor advance, partition-membership check,
    /// termination), and does not re-check what Source/Core already checks over the same retained
    /// bytes when it resolves them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by each cursor variable's own NAME (<paramref name="cursorVariables"/>, taken from
    /// <see cref="RepeatedEnumerationInterpretationProfile.CursorVariables"/>), never by a hardcoded
    /// <c>key_N</c> positional name: family P, X and W's own cursor variables happen to be literally
    /// named <c>key_1</c>.. (<see cref="EuObjectFactsDiscoveryPlan"/>'s own choice), but the census
    /// family's single cursor variable is genuinely named <c>state_key</c>
    /// (<see cref="EuConsolidationDiscoveryPlan"/>'s own page template projects no <c>key_1</c> at
    /// all) -- a real, previously undiscovered defect this fix closes: any nonzero census delivery
    /// was unparseable here before this change, throwing "row is missing key_1" on real data, because
    /// nothing in the test suite before this file's own new tests ever delivered a nonzero census
    /// page.
    /// </para>
    /// <para>
    /// Defect 5's own fix. Every delivered key part is checked for representability
    /// (<see cref="RequireRepresentableKeyPart"/>), ported from the LU executor's own
    /// <c>LuxembourgQueryCursor</c> constructor (through <c>LuxembourgQueryText.RequireKeyPart</c>,
    /// <c>internal</c> to <c>Lex.V3.Contracts</c> and so not reachable from here, hence this file's own
    /// small copy rather than a shared reference): bounded UTF-8 byte length, and valid strict UTF-8
    /// on re-encode. Investigated before porting rather than assumed: <c>System.Text.Json</c>'s own
    /// <c>GetString()</c> does not itself reject an unpaired UTF-16 surrogate inside a JSON string
    /// escape, so a delivered key value carrying one is exactly as reachable on the EU wire as on
    /// LU's, from equally untrusted publisher JSON; <see cref="EuEnumerationRefusal.DeliveredKeyNotRepresentable"/>
    /// was dead code only because nothing on this side ever checked for it, not because the condition
    /// cannot occur here.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string[]> ParseStrictRows(
        ReadOnlySpan<byte> bytes, IReadOnlyList<string> cursorVariables)
    {
        using var document = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) ||
            results.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !results.TryGetProperty("bindings", out var bindings) ||
            bindings.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new FormatException("The page response has no binding array.");
        }

        var rows = new List<string[]>();
        foreach (var binding in bindings.EnumerateArray())
        {
            if (binding.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new FormatException("The page response row is not an object.");
            }

            var parts = new string[cursorVariables.Count];
            for (var index = 0; index < cursorVariables.Count; index++)
            {
                var name = cursorVariables[index];
                if (!binding.TryGetProperty(name, out var term) ||
                    term.ValueKind != System.Text.Json.JsonValueKind.Object ||
                    !term.TryGetProperty("value", out var value) ||
                    value.ValueKind != System.Text.Json.JsonValueKind.String)
                {
                    throw new FormatException($"The page response row is missing {name}.");
                }

                parts[index] = RequireRepresentableKeyPart(value.GetString()!, name);
            }

            rows.Add(parts);
        }

        return rows;
    }

    /// <summary>
    /// The bound, mirroring the LU executor's own identical bound
    /// (<c>LuxembourgQueryText.MaximumKeyPartByteLength</c>): a delivered key part must be bounded,
    /// control-free, strictly UTF-8-representable text. Reused rather than independently chosen: it is
    /// already the reviewed bound for the identical shape of value (one delivered SPARQL results
    /// binding's own string), and there is no EU-specific reason a different number would be right.
    /// </summary>
    private const int MaximumKeyPartByteLength = 2047;

    private static readonly UTF8Encoding StrictKeyPartUtf8 = new(false, true);

    /// <summary>Defect 5: the EU counterpart of the LU executor's own key-part representability check.</summary>
    private static string RequireRepresentableKeyPart(string value, string partName)
    {
        byte[] bytes;
        try
        {
            bytes = StrictKeyPartUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw PageFailure(
                $"The delivered key part '{partName}' is not valid UTF-8 text.",
                EuEnumerationRefusal.DeliveredKeyNotRepresentable,
                exception);
        }

        if (bytes.Length > MaximumKeyPartByteLength)
        {
            throw PageFailure(
                $"The delivered key part '{partName}' exceeds {MaximumKeyPartByteLength} UTF-8 bytes.",
                EuEnumerationRefusal.DeliveredKeyNotRepresentable);
        }

        return value;
    }

    /// <summary>
    /// Tags an exception with the refusal <see cref="ClassifyPageParseFailure"/> should read back out
    /// of it, exactly mirroring the LU executor's own identically named private helper.
    /// </summary>
    private static FormatException PageFailure(
        string message, EuEnumerationRefusal refusal, Exception? inner = null)
    {
        var exception = new FormatException(message, inner);
        exception.Data[PageParseFailureKey] = refusal.ToString();
        return exception;
    }

    /// <summary>
    /// Ordinal, byte-level comparison over each key part in order, using Core's own
    /// <see cref="EnumerationCursorEnvelope.CompareRaw"/> -- the identical comparator
    /// <see cref="EnumerationDeliveryComparison"/>'s own private cursor comparison uses -- so the
    /// executor's own advance check and Core's own strict-ascending check can never disagree about
    /// what "greater" means for one delivered key.
    /// </summary>
    private static int CompareKeys(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            var comparison = EnumerationCursorEnvelope.CompareRaw(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}

/// <summary>
/// The three pieces of one bound count or page request this executor and
/// <see cref="EuDeliveryObservation"/> both need: the plan and input identities Core's own delivery
/// comparison binds against, the opaque send capability itself, and the partition/member key the
/// bind actually computed (read off the bound query rather than recomputed, since
/// <see cref="EuConsolidationDiscoveryPlan"/>'s own partition-key function is private).
/// </summary>
internal sealed record EuBoundQueryParts(
    SourceArtifactRef MachinePlanRef,
    SourceArtifactRef InputRef,
    BoundMachineRequest Request,
    string PartitionKey,
    long? PageRowLimit);
