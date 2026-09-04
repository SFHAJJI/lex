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

    /// <summary>
    /// A page body admitted by status and media type is not a SPARQL results document this
    /// executor can read at all: not JSON, or JSON without the results/bindings array. Distinct
    /// from <see cref="DeliveredKeyNotRepresentable"/> on purpose. That one says the publisher
    /// answered the question and one delivered key was too large to carry; this one says the
    /// publisher did not answer the question. Reading them as the same refusal would let a broken
    /// endpoint be reported as an oversized key, and the body digest carried on the refusal would
    /// then be looked at for the wrong reason.
    /// </summary>
    [JsonStringEnumMemberName("page_body_malformed")]
    PageBodyMalformed = 14,

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
        // Reachable, and driven: the constructor is internal, so same-assembly code and this
        // assembly's tests can both reach it, and a detail whose code says "none" would report a
        // refusal that did not happen. Driven by
        // ARefusalDetailCannotCarryTheNoneCode.
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
        RepeatedEnumerationDeliveryReceipt? receipt,
        LuxembourgEnumerationRefusalDetail? refusal,
        int productRequestCount)
    {
        // There is deliberately no "delivered or refused, never both and never neither" check
        // here. It cannot fail: this constructor is private and its only two callers are
        // Delivered and Refused below, each of which null-checks its one argument and passes null
        // for the other. A check that no caller can trip is not defense, it is a claim that reads
        // as defense; what actually holds the invariant is that no third door exists, which
        // LuxembourgConstructionSurfaceTests pins by making a third door a line in a diff.
        //
        // The count check below is different: both public factories take it from a caller.
        if (productRequestCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productRequestCount));
        }

        Receipt = receipt;
        Refusal = refusal;
        ProductRequestCount = productRequestCount;
    }

    public static LuxembourgEnumerationRunResult Delivered(
        RepeatedEnumerationDeliveryReceipt receipt, int productRequestCount)
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

    public RepeatedEnumerationDeliveryReceipt? Receipt { get; }

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
    /// The LIMIT the pass's own rendered query carries, and so the most rows one page of it may
    /// deliver. Read from the same two plan values <see cref="MaximumPagesFor"/> divides by, so
    /// the page count and the per-page ceiling cannot describe different pagings. Exposed for the
    /// tests that drive a publisher past it; the parser does not enforce it, Source/Core does.
    /// </summary>
    public uint PageRowLimitFor(LuxembourgQueryPass pass) => pass switch
    {
        LuxembourgQueryPass.Pass1 => _pass1Limit,
        LuxembourgQueryPass.Pass2 => _pass2Limit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

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

    /// <summary>
    /// Queue item 19: the publisher-neutral half of the per-observation routine, constructed from
    /// this executor's own <see cref="_custodyStore"/> so nothing here holds a second, independent
    /// custody dependency (Decision 78).
    /// </summary>
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

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
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(_custodyStore);
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
                // receiptRefusal was computed and thrown away here. It is carried now: Core's
                // verbatim message when there is one, the receipt refusal's own name when there
                // is not.
                //
                // Measured, not assumed: DeliveryComparisonRefused is the only one of the four
                // receipt refusals a well-behaved session can actually produce here, so the right
                // operand of the ?? is defensive rather than driven end to end, and no executor
                // test kills a mutation that removes it. The reason is that every input this
                // executor hands the receipt comes from one map per kind, and Source/Core's own
                // tuple check (RepeatedEnumerationDeliveryProof.cs, "The retained SPARQL evidence
                // tuple does not bind") already refuses any observation whose body receipt is not
                // ImmutableObject1 and LockedTime, so a body cannot reach the receipt carrying a
                // membership that would refuse there. The three refusals are driven where they
                // ARE reachable, on RepeatedEnumerationDeliveryReceipt.TryCreate itself, which
                // is public and takes caller-stated membership. Carrying the value costs one
                // string and stops "unreachable today" being written down as "unreachable".
                return LuxembourgEnumerationRunResult.Refused(
                    new LuxembourgEnumerationRefusalDetail(
                        LuxembourgEnumerationRefusal.DeliveryProofRefused,
                        null, null, null, null, null, null, [],
                        set.LastCoreRefusalMessage ?? receiptRefusal.ToString()),
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
    /// Starts a session on the injected test transport through the session's own internal door,
    /// <see cref="RoutedHttpAcquisitionSession.StartWithTestTransportAsync"/>. This used to reach
    /// the session's private constructor and private bootstrap by reflection, from production
    /// source, which meant renaming either one broke this file at run time rather than at compile
    /// time and no build could tell anyone. Reachable only when <see cref="_testHandlerOverride"/>
    /// is non-null, which the public constructor can never set.
    /// </summary>
    private Task<RoutedHttpAcquisitionSession.StartResult> StartWithTestHandlerAsync(
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken) =>
        RoutedHttpAcquisitionSession.StartWithTestTransportAsync(
            sourceWitness, _custodyStore, _testHandlerOverride!, _timeProvider, cancellationToken);

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
        var countIdentity = RepeatedEnumerationObservationIdentity.NewObservation();
        var countOutcome = ToObserveOutcome(await _reopenGlue.ObserveAsync(
                session, countBound.Request, profile, executorWrittenMembership, currentCount, setCount,
                cancellationToken)
            .ConfigureAwait(false));
        if (countOutcome.Refusal is not null)
        {
            return new PassOutcome(null, countOutcome.Refusal);
        }

        long selected;
        try
        {
            selected = ParseStrictCount(countOutcome.Transport!.RetainedPayloadBytes.Span);
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            // JsonException as well as FormatException: JsonDocument.Parse throws it on a body
            // that is not JSON at all, and it used to escape this method uncaught, out of
            // RunPassAsync and out of RunPartitionAsync, so a publisher serving an HTML error page
            // under a JSON content type crashed the run instead of refusing it. Both land on the
            // same refusal here because the count route has one bucket for the whole question:
            // this body did not deliver one nonnegative integer.
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
            var pageOutcome = ToObserveOutcome(await _reopenGlue.ObserveAsync(
                    session, pageBound.Request, profile, executorWrittenMembership, currentCount, setCount,
                    cancellationToken)
                .ConfigureAwait(false));
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
            catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
            {
                // The classification is decided INSIDE the catch, not in a `when` filter. It used
                // to be `catch (FormatException e) when (e.Data.Contains("oversizedKey"))`, and
                // every other refusal ParseStrictRows can raise therefore escaped the executor
                // entirely: a body that is not JSON (JsonException), a body with no bindings array,
                // and a row missing a key all left RunPartitionAsync as an exception rather than a
                // typed result. A filter that names one case silently promotes the rest to crashes.
                return new PassOutcome(
                    null,
                    new LuxembourgEnumerationRefusalDetail(
                        ClassifyPageParseFailure(exception),
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
                pageBound, RepeatedEnumerationObservationIdentity.NewObservation(), transport, profile);
            deliveryPass = deliveryPass.WithPage(pageObservation);

            if (rows.Count == 0)
            {
                break;
            }
        }

        return new PassOutcome(deliveryPass, null);
    }

    private sealed record ObserveOutcome(
        RepeatedEnumerationObservedTransport? Transport,
        ulong? RequestOrdinal,
        LuxembourgEnumerationRefusalDetail? Refusal);

    /// <summary>
    /// Queue item 19: the per-observation routine itself (design section 2, step 9) moved to the
    /// publisher-neutral <see cref="RepeatedEnumerationDeliveryReopenGlue.ObserveAsync"/>, extracted
    /// so a future EU executor can reuse it instead of duplicating it. This maps its narrower,
    /// neutral <see cref="ObservationAttemptFailure"/> back into this executor's own unchanged
    /// <see cref="LuxembourgEnumerationRefusalDetail"/>, reconstructing the exact same field values
    /// the executor used to build directly, so no behavior visible to a Luxembourg caller changed.
    /// </summary>
    private static ObserveOutcome ToObserveOutcome(ObservationAttemptOutcome outcome)
    {
        if (outcome.Failure is not { } failure)
        {
            return new ObserveOutcome(outcome.Transport, outcome.RequestOrdinal, null);
        }

        var code = failure.Kind switch
        {
            ObservationAttemptFailureKind.NotExecuted => LuxembourgEnumerationRefusal.ObservationNotExecuted,
            ObservationAttemptFailureKind.StatusNotAdmitted => LuxembourgEnumerationRefusal.StatusNotAdmitted,
            ObservationAttemptFailureKind.MediaTypeNotAdmitted => LuxembourgEnumerationRefusal.MediaTypeNotAdmitted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome), $"Unreachable: an unhandled {nameof(ObservationAttemptFailureKind)} '{failure.Kind}'."),
        };
        return new ObserveOutcome(
            null,
            outcome.RequestOrdinal,
            new LuxembourgEnumerationRefusalDetail(
                code,
                outcome.RequestOrdinal,
                failure.AttemptOrdinalReached,
                failure.TerminalStatus,
                failure.ResponseBodySha256,
                failure.ObservedMediaType,
                null,
                [],
                failure.OperationalDetail));
    }

    /// <summary>
    /// Which typed refusal a page-parse failure is. Keyed on the tag <see cref="ParseStrictRows"/>
    /// attaches at each throw site rather than on the message text, so a reworded message cannot
    /// silently reclassify a refusal.
    /// </summary>
    private static LuxembourgEnumerationRefusal ClassifyPageParseFailure(Exception exception) =>
        exception.Data[PageParseFailureKey] switch
        {
            nameof(LuxembourgEnumerationRefusal.DeliveredKeyNotRepresentable) =>
                LuxembourgEnumerationRefusal.DeliveredKeyNotRepresentable,

            // Untagged: either a JsonException from JsonDocument.Parse, or a shape failure this
            // parser raises about the document rather than about one delivered value.
            _ => LuxembourgEnumerationRefusal.PageBodyMalformed,
        };

    private const string PageParseFailureKey = "lu.pageParseFailure";

    private static FormatException PageFailure(
        string message, LuxembourgEnumerationRefusal refusal, Exception? inner = null)
    {
        var exception = new FormatException(message, inner);
        exception.Data[PageParseFailureKey] = refusal.ToString();
        return exception;
    }

    private static long ParseStrictCount(ReadOnlySpan<byte> bytes)
    {
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

        // ValueKind is checked before every TryGetProperty/GetString call on a value this parser
        // does not already know the shape of, including the results.TryGetProperty("bindings")
        // call above. JsonElement.TryGetProperty throws InvalidOperationException on a non-object
        // element, and GetString throws it on a non-string, non-null element; neither is a
        // FormatException or JsonException, so neither was caught by RunPassAsync's catch filter,
        // and all three used to escape RunPartitionAsync and RunCoverAsync as an unhandled
        // exception rather than a typed refusal. A count body whose "results" is itself not an
        // object (`{"results":5}` or `{"results":[]}`) hit the results.TryGetProperty("bindings")
        // call above; one whose one binding value is not itself an object (`{"count":"1"}`) hit the
        // first of these below; a term whose "type" or "datatype" exists but is not a string hit
        // the second.
        var term = binding.EnumerateObject().First().Value;
        if (term.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !term.TryGetProperty("type", out var type) ||
            type.ValueKind != System.Text.Json.JsonValueKind.String ||
            type.GetString() != "typed-literal" ||
            !term.TryGetProperty("datatype", out var datatype) ||
            datatype.ValueKind != System.Text.Json.JsonValueKind.String ||
            datatype.GetString() != "http://www.w3.org/2001/XMLSchema#integer" ||
            !term.TryGetProperty("value", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new FormatException("The count term is not one typed nonnegative integer literal.");
        }

        // NumberStyles.None admits no sign, so a successful parse is nonnegative by construction
        // and there is no "or count < 0" disjunct here. There used to be one; it could not fire,
        // and it made the strictness look like it came from a range check rather than from the
        // parse style, which is the thing a reader would need to preserve.
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
    /// The six-part cursor rows of one page, read only as far as the executor's own driving needs:
    /// enough to advance the cursor, bound the partition and decide termination.
    /// </summary>
    /// <remarks>
    /// It deliberately does NOT re-check what Source/Core already checks over the same retained
    /// bytes when it resolves them. Two of these were tried here and removed: the per-page row
    /// limit (<c>RepeatedEnumerationDeliveryProof.VerifyPages</c>, "The page exceeds its row
    /// limit") and the plain-literal cursor rule (same method, "Cursor projections must be plain
    /// literals matching the query comparator"). Both are real rules and both are enforced; a
    /// second copy here would be a second place for one invariant to drift, and it would have
    /// reclassified refusals Core owns into refusals this executor invented. What that costs is
    /// visible and accepted: a publisher breaking either rule is refused after the pass completes
    /// rather than mid-pass, so the run spends the remaining requests of that pass first.
    /// <para>
    /// Every failure raised here is tagged with the refusal it means, so the caller classifies
    /// without reading messages; nothing throws untagged except <c>JsonDocument.Parse</c> itself
    /// and the document-shape check, which are both the same answer: this is not a page.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<LuxembourgQueryCursor> ParseStrictRows(ReadOnlySpan<byte> bytes)
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

        var rows = new List<LuxembourgQueryCursor>();
        foreach (var binding in bindings.EnumerateArray())
        {
            // Checked before TryGetProperty is called on it: a bindings array element that is not
            // itself an object (a page body such as `"bindings":[[]]`) throws
            // InvalidOperationException out of binding.TryGetProperty rather than returning false,
            // and that exception is neither FormatException nor JsonException, so it used to escape
            // this parser, RunPassAsync's catch filter, and RunPartitionAsync/RunCoverAsync
            // entirely instead of becoming a typed refusal.
            if (binding.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new FormatException("The page response row is not an object.");
            }

            var parts = new string[6];
            for (var index = 0; index < 6; index++)
            {
                var name = $"key_{index + 1}";
                if (!binding.TryGetProperty(name, out var term) ||
                    term.ValueKind != System.Text.Json.JsonValueKind.Object ||
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
                throw PageFailure(
                    "A delivered key component is not representable.",
                    LuxembourgEnumerationRefusal.DeliveredKeyNotRepresentable,
                    exception);
            }
        }

        return rows;
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
