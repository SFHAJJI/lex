using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest;

namespace Lex.V3.Ingest.Luxembourg;

public enum LuxembourgEnumerationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("robots_bootstrap_refused")]
    RobotsBootstrapRefused = 1,

    /// <summary>
    /// The acquisition run's own bootstrap artifacts are not held under an enforced floor, so this
    /// store cannot produce a payload receipt Source/Core will bind. Observed from the session's
    /// membership after robots and before the first product request, so a deployment that cannot
    /// mint a proof spends nothing finding out, and writes nothing to find out.
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
}

public sealed class LuxembourgEnumerationRefusalDetail
{
    internal LuxembourgEnumerationRefusalDetail(
        LuxembourgEnumerationRefusal code,
        ulong? requestOrdinal,
        ulong? attemptOrdinalReached,
        int? terminalStatus,
        string? responseBodySha256,
        string? observedMediaType,
        long? observedCount,
        IReadOnlyList<string> unenforcedDigests,
        string? coreRefusalDetail)
    {
        if (code == LuxembourgEnumerationRefusal.None)
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
        UnenforcedDigests = unenforcedDigests;
        CoreRefusalDetail = coreRefusalDetail;
    }

    public LuxembourgEnumerationRefusal Code { get; }

    public ulong? RequestOrdinal { get; }

    public ulong? AttemptOrdinalReached { get; }

    public int? TerminalStatus { get; }

    /// <summary>
    /// The custody digest of the body we refused on. The session has already written it under
    /// NightlyFloor90d, so an unclassified publisher refusal is readable out of custody by a person
    /// rather than guessed at by a predicate.
    /// </summary>
    public string? ResponseBodySha256 { get; }

    public string? ObservedMediaType { get; }

    public long? ObservedCount { get; }

    /// <summary>Digests the run wrote whose store published no enforcement.</summary>
    public IReadOnlyList<string> UnenforcedDigests { get; }

    /// <summary>Source/Core's own refusal message, verbatim, never a paraphrase.</summary>
    public string? CoreRefusalDetail { get; }
}

/// <summary>Delivered or refused, never both and never neither.</summary>
public sealed class LuxembourgEnumerationRunResult
{
    private LuxembourgEnumerationRunResult(
        LuxembourgEnumerationDeliveryReceipt? receipt,
        LuxembourgEnumerationRefusalDetail? refusal,
        int productRequestCount)
    {
        if ((receipt is null) == (refusal is null))
        {
            throw new ArgumentException("A run result is delivered or refused, never both and never neither.");
        }

        if (productRequestCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productRequestCount));
        }

        Receipt = receipt;
        Refusal = refusal;
        ProductRequestCount = productRequestCount;
    }

    public static LuxembourgEnumerationRunResult Delivered(
        LuxembourgEnumerationDeliveryReceipt receipt, int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new(receipt, null, productRequestCount);
    }

    public static LuxembourgEnumerationRunResult Refused(
        LuxembourgEnumerationRefusalDetail detail, int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new(null, detail, productRequestCount);
    }

    public LuxembourgEnumerationDeliveryReceipt? Receipt { get; }

    public LuxembourgEnumerationRefusalDetail? Refusal { get; }

    /// <summary>Publisher requests this run spent, robots excluded. Always populated.</summary>
    public int ProductRequestCount { get; }
}

/// <summary>
/// Page budget derived from the plan's own pass limits and the pass's observed pre-count. There is
/// no caller-supplied page count, page limit or offset anywhere in this executor.
/// </summary>
public sealed class LuxembourgEnumerationBudget
{
    private readonly uint _pass1Limit;
    private readonly uint _pass2Limit;

    private LuxembourgEnumerationBudget(uint pass1Limit, uint pass2Limit)
    {
        _pass1Limit = pass1Limit;
        _pass2Limit = pass2Limit;
    }

    public static LuxembourgEnumerationBudget FromPlan(LuxembourgQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(plan.Pass1PageLimit, plan.Pass2PageLimit);
    }

    /// <summary>
    /// Exact, not a guess. Under EmptySuccessorAfterShortPage a non-terminal short page requires an
    /// empty successor and nothing may follow an empty page, so the page count is determined: 1
    /// when selected is 0, selected/limit + 1 when it divides, else selected/limit + 2.
    /// </summary>
    public int MaximumPagesFor(LuxembourgQueryPass pass, long selectedRowCount)
    {
        if (selectedRowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedRowCount));
        }

        var limit = pass switch
        {
            LuxembourgQueryPass.Pass1 => _pass1Limit,
            LuxembourgQueryPass.Pass2 => _pass2Limit,
            _ => throw new ArgumentOutOfRangeException(nameof(pass)),
        };

        if (selectedRowCount == 0)
        {
            return 1;
        }

        var quotient = selectedRowCount / limit;
        var remainder = selectedRowCount % limit;
        return checked((int)(remainder == 0 ? quotient + 1 : quotient + 2));
    }
}

/// <summary>
/// One resource id for the invariant plan, used for both CreateDeliveryProfile and every bind. Two
/// ids that must agree would be a check; one id is an invariant.
/// </summary>
public sealed record LuxembourgPartitionRunRequest(
    LuxembourgQueryPlan InvariantPlan,
    string InvariantPlanResourceId,
    string SetId,
    LuxembourgQueryPartitionRange Partition,
    MachineQueryRendererSource RendererSource);

public sealed class LuxembourgRepeatedEnumerationExecutor
{
    private readonly ICustodyStore _custodyStore;
    private readonly TimeProvider _timeProvider;
    private readonly System.Net.Http.HttpMessageHandler? _testHandlerOverride;

    public LuxembourgRepeatedEnumerationExecutor(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, testHandlerOverride: null)
    {
    }

    /// <summary>
    /// Test-only seam: when supplied, the run's transport uses this handler instead of the
    /// production pinned one, through <see cref="RoutedHttpAcquisitionSession"/>'s matching
    /// internal overload. Production code only ever calls the public constructor, which always
    /// leaves this null and so always takes the real network path.
    /// </summary>
    internal LuxembourgRepeatedEnumerationExecutor(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHandlerOverride = testHandlerOverride;
    }

    /// <summary>One partition, one session, two passes.</summary>
    public async Task<LuxembourgEnumerationRunResult> RunPartitionAsync(
        LuxembourgPartitionRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var start = _testHandlerOverride is null
            ? await RoutedHttpAcquisitionSession.StartAsync(sourceWitness, _custodyStore, cancellationToken)
                .ConfigureAwait(false)
            : await StartWithTestHandlerAsync(sourceWitness, cancellationToken).ConfigureAwait(false);
        if (start.Kind != OfficialHttpAcquisitionOutcomeKind.ExecutedObservation || start.Session is null)
        {
            return LuxembourgEnumerationRunResult.Refused(
                new LuxembourgEnumerationRefusalDetail(
                    LuxembourgEnumerationRefusal.RobotsBootstrapRefused,
                    null, null, null, null, null, null, [], null),
                productRequestCount: 0);
        }

        var runner = start.Session;
        try
        {
            return await RunPartitionOnSessionAsync(request, runner, sharedProfileRef: null, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            runner.Dispose();
        }
    }

    /// <summary>
    /// The body of one partition's run against a session that is already bootstrapped and owned by
    /// the caller: <see cref="RunPartitionAsync"/> for a single partition, <see cref="RunCoverAsync"/>
    /// for every leaf of one chain sharing one session. Splitting this out (rather than having
    /// <see cref="RunCoverAsync"/> call <see cref="RunPartitionAsync"/> per leaf) is what makes the
    /// cover's one-run requirement hold by construction: <see cref="RoutedHttpAcquisitionSession"/>
    /// mints one <c>RunIdentity</c> at construction and stamps it on every evidence document it
    /// writes, so starting a fresh session per leaf would give every leaf a different
    /// <c>RunIdentity</c> and <see cref="LuxembourgPartitionCover.TryCreate"/> would refuse
    /// <c>leaf_run_identity_differs</c> on every multi-leaf cover. The product-request count is
    /// local to this call, so each leaf reports its own send count, not a running total across
    /// leaves that happen to share a session.
    /// </summary>
    /// <param name="sharedProfileRef">
    /// Null for a standalone <see cref="RunPartitionAsync"/> call, which mints its own. Supplied by
    /// <see cref="RunCoverAsync"/>, minted once outside the leaf loop: the interpretation profile's
    /// content is identical across every leaf of one chain (it depends only on the invariant plan,
    /// its resource id and the set id, never the partition), but <see cref="SourceArtifactRef"/>
    /// equality is structural over the resource id too, so minting a fresh id per leaf would make
    /// every leaf's <c>InterpretationProfileRef</c> compare unequal and
    /// <see cref="LuxembourgPartitionCover.TryCreate"/> would refuse <c>leaf_profile_differs</c> on
    /// every multi-leaf cover, exactly as reusing one session was required for <c>RunIdentity</c>
    /// above. Re-validated against this leaf's own independently-derived profile rather than
    /// trusted blindly, so a caller error surfaces as an exception, not a silently wrong label.
    /// </param>
    private async Task<LuxembourgEnumerationRunResult> RunPartitionOnSessionAsync(
        LuxembourgPartitionRunRequest request,
        RoutedHttpAcquisitionSession runner,
        SourceArtifactRef? sharedProfileRef,
        CancellationToken cancellationToken)
    {
        var budget = LuxembourgEnumerationBudget.FromPlan(request.InvariantPlan);
        var profile = request.InvariantPlan.CreateDeliveryProfile(request.InvariantPlanResourceId, request.SetId);
        SourceArtifactRef profileRef;
        if (sharedProfileRef is null)
        {
            profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(NewUrn(), profile);
        }
        else
        {
            RepeatedEnumerationInterpretationProfileIdentity.Validate(sharedProfileRef, profile);
            profileRef = sharedProfileRef;
        }

        var productRequestCount = 0;
        try
        {
            // Step 3: the custody floor is observed, not assumed, from the session's own
            // cumulative membership (robots bootstrap, plus every earlier leaf on this same
            // session). Zero extra writes, zero extra requests.
            var bootstrapMembership = runner.CopyArtifactMembership();
            var unenforced = bootstrapMembership
                .Where(static entry => entry.Value != CustodyMembership.Floored)
                .Select(static entry => entry.Key)
                .ToArray();
            if (unenforced.Length > 0)
            {
                return LuxembourgEnumerationRunResult.Refused(
                    new LuxembourgEnumerationRefusalDetail(
                        LuxembourgEnumerationRefusal.CustodyFloorNotObserved,
                        null, null, null, null, null, null, unenforced, null),
                    productRequestCount: 0);
            }

            var executorWrittenMembership = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
            LuxembourgDeliveryPass? passA = null;
            LuxembourgDeliveryPass? passB = null;

            foreach (var pass in new[] { LuxembourgQueryPass.Pass1, LuxembourgQueryPass.Pass2 })
            {
                var passResult = await RunPassAsync(
                        runner, request, profile, pass, budget, executorWrittenMembership,
                        () => productRequestCount, count => productRequestCount = count,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (passResult.Refusal is not null)
                {
                    return LuxembourgEnumerationRunResult.Refused(passResult.Refusal, productRequestCount);
                }

                if (pass == LuxembourgQueryPass.Pass1)
                {
                    passA = passResult.Pass;
                }
                else
                {
                    passB = passResult.Pass;
                }
            }

            var set = await LuxembourgDeliveryEvidenceSet.MaterializeAsync(
                    profile, profileRef, request.InvariantPlan, request.InvariantPlanResourceId, request.SetId,
                    request.RendererSource, passA!, passB!, _custodyStore, cancellationToken)
                .ConfigureAwait(false);

            var receipt = set.TryCompareAndReceipt(
                runner.CopyArtifactMembership(), executorWrittenMembership, out var receiptRefusal);
            if (receipt is null)
            {
                return LuxembourgEnumerationRunResult.Refused(
                    new LuxembourgEnumerationRefusalDetail(
                        LuxembourgEnumerationRefusal.DeliveryProofRefused,
                        null, null, null, null, null, null, [], set.LastCoreRefusalMessage),
                    productRequestCount);
            }

            return LuxembourgEnumerationRunResult.Delivered(receipt, productRequestCount);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
        {
            return LuxembourgEnumerationRunResult.Refused(
                new LuxembourgEnumerationRefusalDetail(
                    LuxembourgEnumerationRefusal.CustodyMemberMissing,
                    null, null, null, null, null, null, [], exception.Message),
                productRequestCount);
        }
    }

    /// <summary>
    /// Reaches the session's own private constructor and private <c>BootstrapRobotsAsync</c> by
    /// reflection, exactly as the session's existing tests already do (see
    /// RoutedHttpRequestPolicyAuditTests.Session/BootstrapAsync), rather than adding a new named
    /// overload to RoutedHttpAcquisitionSession itself: that surface is deliberately pinned closed
    /// by RoutedHttpAcquisitionSessionTests.ProductionSurfaceAcceptsNoCallerAuthoredTransportFacts,
    /// which forbids any non-private member of the session from accepting a caller-supplied
    /// HttpMessageHandler or TimeProvider. This path is reachable only when
    /// <see cref="_testHandlerOverride"/> is non-null, which the public constructor can never set.
    /// </summary>
    private async Task<RoutedHttpAcquisitionSession.StartResult> StartWithTestHandlerAsync(
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        var constructor = typeof(RoutedHttpAcquisitionSession).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var session = (RoutedHttpAcquisitionSession)constructor.Invoke(
            [sourceWitness, _custodyStore, _testHandlerOverride, _timeProvider, false]);
        var bootstrap = (Task<RoutedHttpAcquisitionSession.StartResult>)(typeof(RoutedHttpAcquisitionSession)
                .GetMethod(
                    "BootstrapRobotsAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(session, [cancellationToken])
            ?? throw new InvalidOperationException("The session's robots bootstrap returned no task."));
        return await bootstrap.ConfigureAwait(false);
    }

    /// <summary>
    /// Every leaf of one chain in ONE session, so the cover's one-run requirement holds by
    /// construction rather than by a check across results.
    /// </summary>
    public async Task<IReadOnlyList<LuxembourgEnumerationRunResult>> RunCoverAsync(
        LuxembourgPartitionRunRequest rootRequest,
        LuxembourgPartitionChain chain,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rootRequest);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        // One session for every leaf (see RunPartitionOnSessionAsync's doc comment for why): the
        // bootstrap itself is not per leaf, so a bootstrap refusal here is reported once per
        // intended leaf rather than as a single result, keeping results.Count == chain.Leaves.Count
        // true on every path, not only the delivered one.
        var start = _testHandlerOverride is null
            ? await RoutedHttpAcquisitionSession.StartAsync(sourceWitness, _custodyStore, cancellationToken)
                .ConfigureAwait(false)
            : await StartWithTestHandlerAsync(sourceWitness, cancellationToken).ConfigureAwait(false);
        if (start.Kind != OfficialHttpAcquisitionOutcomeKind.ExecutedObservation || start.Session is null)
        {
            return chain.Leaves
                .Select(static _ => LuxembourgEnumerationRunResult.Refused(
                    new LuxembourgEnumerationRefusalDetail(
                        LuxembourgEnumerationRefusal.RobotsBootstrapRefused,
                        null, null, null, null, null, null, [], null),
                    productRequestCount: 0))
                .ToArray();
        }

        var runner = start.Session;
        try
        {
            // Minted once, outside the loop: see RunPartitionOnSessionAsync's sharedProfileRef doc
            // comment for why every leaf must reference this exact ref rather than each minting its
            // own equal-content one. CreateDeliveryProfile depends only on the invariant plan, its
            // resource id and the set id, none of which the per-leaf Partition override touches, so
            // deriving it once from rootRequest is exact, not an approximation.
            var sharedProfile = rootRequest.InvariantPlan.CreateDeliveryProfile(
                rootRequest.InvariantPlanResourceId, rootRequest.SetId);
            var sharedProfileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(NewUrn(), sharedProfile);

            var results = new List<LuxembourgEnumerationRunResult>(chain.Leaves.Count);
            foreach (var leaf in chain.Leaves)
            {
                var leafRequest = rootRequest with { Partition = leaf };
                results.Add(await RunPartitionOnSessionAsync(leafRequest, runner, sharedProfileRef, cancellationToken)
                    .ConfigureAwait(false));
            }

            return results;
        }
        finally
        {
            runner.Dispose();
        }
    }

    private sealed record PassOutcome(LuxembourgDeliveryPass? Pass, LuxembourgEnumerationRefusalDetail? Refusal);

    private async Task<PassOutcome> RunPassAsync(
        RoutedHttpAcquisitionSession session,
        LuxembourgPartitionRunRequest request,
        RepeatedEnumerationInterpretationProfile profile,
        LuxembourgQueryPass pass,
        LuxembourgEnumerationBudget budget,
        Dictionary<string, CustodyMembership> executorWrittenMembership,
        Func<int> currentCount,
        Action<int> setCount,
        CancellationToken cancellationToken)
    {
        var countBound = request.InvariantPlan.BindCount(
            request.InvariantPlanResourceId, NewUrn(), NewUrn(), request.SetId, pass, request.Partition,
            request.RendererSource);
        var countIdentity = LuxembourgObservationIdentity.NewObservation();
        var countOutcome = await ObserveAsync(
                session, countBound.Request, profile, executorWrittenMembership, currentCount, setCount,
                cancellationToken)
            .ConfigureAwait(false);
        if (countOutcome.Refusal is not null)
        {
            return new PassOutcome(null, countOutcome.Refusal);
        }

        long selected;
        try
        {
            selected = ParseStrictCount(countOutcome.Transport!.RetainedPayloadBytes.Span);
        }
        catch (FormatException)
        {
            return new PassOutcome(
                null,
                new LuxembourgEnumerationRefusalDetail(
                    LuxembourgEnumerationRefusal.CountNotOneNonNegativeInteger,
                    countOutcome.RequestOrdinal, null,
                    countOutcome.Transport!.HttpEvidence.Hops[0].Status,
                    countOutcome.Transport.HttpEvidence.Hops[0].Sha256,
                    null, null, [], null));
        }

        if (EnumerationDeliveryComparison.AssessThreshold(selected, profile) ==
            RepeatedEnumerationThresholdAssessment.PartitionRequired)
        {
            return new PassOutcome(
                null,
                new LuxembourgEnumerationRefusalDetail(
                    LuxembourgEnumerationRefusal.PartitionRequired,
                    countOutcome.RequestOrdinal, null, null, null, null, selected, [], null));
        }

        var countObservation = LuxembourgDeliveryObservation.ForCount(
            countBound, countIdentity, countOutcome.Transport, profile);
        var deliveryPass = LuxembourgDeliveryPass.BeginWithCount(countObservation, selected);

        LuxembourgQueryCursor? cursor = null;
        while (true)
        {
            // Checked before this page is bound or sent: the budget is exact (design section 1.5),
            // so a page that would exceed it is never requested, not sent and then disowned.
            if (deliveryPass.Pages.Count >= budget.MaximumPagesFor(pass, selected))
            {
                return new PassOutcome(
                    null,
                    new LuxembourgEnumerationRefusalDetail(
                        LuxembourgEnumerationRefusal.PageBudgetExhausted,
                        null, null, null, null, null, null, [], null));
            }

            var pageBound = request.InvariantPlan.BindPage(
                request.InvariantPlanResourceId, NewUrn(), NewUrn(), request.SetId, pass, request.Partition,
                cursor, selected, countObservation.HttpEvidenceRef, request.RendererSource);
            var pageOutcome = await ObserveAsync(
                    session, pageBound.Request, profile, executorWrittenMembership, currentCount, setCount,
                    cancellationToken)
                .ConfigureAwait(false);
            if (pageOutcome.Refusal is not null)
            {
                return new PassOutcome(null, pageOutcome.Refusal);
            }

            var transport = pageOutcome.Transport!;
            IReadOnlyList<LuxembourgQueryCursor> rows;
            try
            {
                rows = ParseStrictRows(transport.RetainedPayloadBytes.Span);
            }
            catch (FormatException exception) when (exception.Data.Contains("oversizedKey"))
            {
                return new PassOutcome(
                    null,
                    new LuxembourgEnumerationRefusalDetail(
                        LuxembourgEnumerationRefusal.DeliveredKeyNotRepresentable,
                        pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                        transport.HttpEvidence.Hops[0].Sha256, null, null, [], null));
            }

            if (rows.Count > 0)
            {
                for (var index = 1; index < rows.Count; index++)
                {
                    if (rows[index - 1].CompareTo(rows[index]) >= 0)
                    {
                        return new PassOutcome(
                            null,
                            new LuxembourgEnumerationRefusalDetail(
                                LuxembourgEnumerationRefusal.CursorDidNotAdvance,
                                pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                                transport.HttpEvidence.Hops[0].Sha256, null, null, [], null));
                    }
                }

                var candidate = rows[^1];
                if (cursor is not null && cursor.CompareTo(candidate) >= 0)
                {
                    return new PassOutcome(
                        null,
                        new LuxembourgEnumerationRefusalDetail(
                            LuxembourgEnumerationRefusal.CursorDidNotAdvance,
                            pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                            transport.HttpEvidence.Hops[0].Sha256, null, null, [], null));
                }

                if (candidate.CompareTo(request.Partition.StartInclusive) < 0 ||
                    candidate.CompareTo(request.Partition.EndExclusive) >= 0)
                {
                    return new PassOutcome(
                        null,
                        new LuxembourgEnumerationRefusalDetail(
                            LuxembourgEnumerationRefusal.DeliveredRowOutsidePartition,
                            pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                            transport.HttpEvidence.Hops[0].Sha256, null, null, [], null));
                }

                cursor = candidate;
            }

            // The empty terminal page belongs in the pass too: EmptySuccessorAfterShortPage
            // requires Core to see it as the pass's own last page, not an event this executor
            // merely noticed and stopped on.
            var pageObservation = LuxembourgDeliveryObservation.ForPage(
                pageBound, LuxembourgObservationIdentity.NewObservation(), transport, profile);
            deliveryPass = deliveryPass.WithPage(pageObservation);

            if (rows.Count == 0)
            {
                break;
            }
        }

        return new PassOutcome(deliveryPass, null);
    }

    private sealed record ObserveOutcome(
        LuxembourgObservedTransport? Transport,
        ulong? RequestOrdinal,
        LuxembourgEnumerationRefusalDetail? Refusal);

    /// <summary>
    /// The shared per-observation routine (design section 2, step 9). Executes the plan item,
    /// admits only a terminal derivable 200 with the expected media type, reads the logical
    /// request/write receipt/payload back out of custody (never from memory), writes the HTTP
    /// evidence document, and reopens it by the digest the store returned.
    /// </summary>
    private async Task<ObserveOutcome> ObserveAsync(
        RoutedHttpAcquisitionSession session,
        BoundMachineRequest request,
        RepeatedEnumerationInterpretationProfile profile,
        Dictionary<string, CustodyMembership> executorWrittenMembership,
        Func<int> currentCount,
        Action<int> setCount,
        CancellationToken cancellationToken)
    {
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
            // throw InvalidOperationException from ExecuteNextAttemptAsync itself, which design
            // section 2 step 9.2 requires never happens; this predicate is why it never does.
            var retryable = attempt.PreHeaderFailureClass is
                HttpPreHeaderFailureClass.HeaderDeadline or HttpPreHeaderFailureClass.TransportBeforeHeaders;
            if (!retryable || attemptOrdinal >= maximumAttempts)
            {
                return new ObserveOutcome(
                    null,
                    item.RequestOrdinal,
                    new LuxembourgEnumerationRefusalDetail(
                        LuxembourgEnumerationRefusal.ObservationNotExecuted,
                        item.RequestOrdinal, (ulong)attemptOrdinal, null, null, null, null, [],
                        $"{attempt.OperationalReason}/{attempt.PreHeaderFailureClass}"));
            }
        }

        var evidence = attempt.Evidence!;
        var terminal = evidence.Hops[^1];
        if (terminal.Status != 200 || terminal.StatusDisposition != HttpStatusDisposition.DerivableStatus)
        {
            return new ObserveOutcome(
                null,
                item.RequestOrdinal,
                new LuxembourgEnumerationRefusalDetail(
                    LuxembourgEnumerationRefusal.StatusNotAdmitted,
                    item.RequestOrdinal, null, terminal.Status, terminal.Sha256, null, null, [], null));
        }

        var observedMediaType = terminal.Headers.ContentType is RoutedHttpSingleHeader single ? single.Value : null;
        if (observedMediaType != profile.ExpectedMediaType)
        {
            return new ObserveOutcome(
                null,
                item.RequestOrdinal,
                new LuxembourgEnumerationRefusalDetail(
                    LuxembourgEnumerationRefusal.MediaTypeNotAdmitted,
                    item.RequestOrdinal, null, terminal.Status, terminal.Sha256, observedMediaType, null, [], null));
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

        var transport = new LuxembourgObservedTransport(logicalRequest, reopenedEvidence, writeReceipt, payload);
        return new ObserveOutcome(transport, item.RequestOrdinal, null);
    }

    private static long ParseStrictCount(ReadOnlySpan<byte> bytes)
    {
        using var document = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) ||
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
        if (!term.TryGetProperty("type", out var type) ||
            type.GetString() != "typed-literal" ||
            !term.TryGetProperty("datatype", out var datatype) ||
            datatype.GetString() != "http://www.w3.org/2001/XMLSchema#integer" ||
            !term.TryGetProperty("value", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new FormatException("The count term is not one typed nonnegative integer literal.");
        }

        if (!long.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var count) ||
            count < 0)
        {
            throw new FormatException("The count value is not one nonnegative integer.");
        }

        return count;
    }

    private static IReadOnlyList<LuxembourgQueryCursor> ParseStrictRows(ReadOnlySpan<byte> bytes)
    {
        using var document = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) ||
            !results.TryGetProperty("bindings", out var bindings) ||
            bindings.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new FormatException("The page response has no binding array.");
        }

        var rows = new List<LuxembourgQueryCursor>();
        foreach (var binding in bindings.EnumerateArray())
        {
            var parts = new string[6];
            for (var index = 0; index < 6; index++)
            {
                var name = $"key_{index + 1}";
                if (!binding.TryGetProperty(name, out var term) ||
                    !term.TryGetProperty("value", out var value) ||
                    value.ValueKind != System.Text.Json.JsonValueKind.String)
                {
                    throw new FormatException($"The page response row is missing {name}.");
                }

                parts[index] = value.GetString()!;
            }

            try
            {
                rows.Add(new LuxembourgQueryCursor(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]));
            }
            catch (ArgumentException exception)
            {
                var formatted = new FormatException("A delivered key component is not representable.", exception);
                formatted.Data["oversizedKey"] = true;
                throw formatted;
            }
        }

        return rows;
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
