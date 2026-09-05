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

    [JsonStringEnumMemberName("observation_not_executed")]
    ObservationNotExecuted = 2,

    [JsonStringEnumMemberName("status_not_admitted")]
    StatusNotAdmitted = 3,

    [JsonStringEnumMemberName("media_type_not_admitted")]
    MediaTypeNotAdmitted = 4,

    [JsonStringEnumMemberName("count_not_one_nonnegative_integer")]
    CountNotOneNonNegativeInteger = 5,

    [JsonStringEnumMemberName("partition_required")]
    PartitionRequired = 6,

    [JsonStringEnumMemberName("delivered_key_not_representable")]
    DeliveredKeyNotRepresentable = 7,

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
    DeliveredRowOutsidePartition = 8,

    [JsonStringEnumMemberName("cursor_did_not_advance")]
    CursorDidNotAdvance = 9,

    [JsonStringEnumMemberName("page_budget_exhausted")]
    PageBudgetExhausted = 10,

    [JsonStringEnumMemberName("custody_member_missing")]
    CustodyMemberMissing = 11,

    [JsonStringEnumMemberName("delivery_proof_refused")]
    DeliveryProofRefused = 12,

    /// <summary>
    /// A page body admitted by status and media type is DEMONSTRABLY NOT what the interpretation
    /// profile promised: not JSON at all, JSON without the results/bindings array, or a binding that
    /// is not an object. The detail names the offending term or position.
    /// </summary>
    /// <remarks>
    /// Narrowed. This used to be the DEFAULT arm of the executor's page-parse classifier, so any
    /// parse failure it had no specific member for was reported as the publisher having sent bad
    /// bytes. That is the strongest possible wrong answer: it blames the office for our own decode
    /// and, worse, it hides the real cause behind a name a reader will not question. The EU canary
    /// refused a body of 39,498 bytes carrying 41 bindings against a count query that answered 41,
    /// valid and complete SPARQL JSON, under this member. Every remaining producer is an explicit
    /// throw that can point at the exact byte or term it rejected;
    /// <see cref="PageDecodeFailedOnOurSide"/> now carries everything else.
    /// </remarks>
    [JsonStringEnumMemberName("page_body_malformed")]
    PageBodyMalformed = 13,

    /// <summary>
    /// THIS EXECUTOR could not decode a page the interpretation profile admits. Ours, not the
    /// publisher's. The detail carries the exception type, its message and the page body's own
    /// digest, so the exact retained bytes can be reopened and read.
    /// </summary>
    /// <remarks>
    /// The default arm of the page-parse classifier, and the reason it exists is that a default arm
    /// must never name someone else. The first condition it names was found by the canary: SPARQL
    /// 1.1's JSON results format OMITS AN UNBOUND VARIABLE FROM A BINDING ENTIRELY, and this
    /// executor's cursor-key extraction required every projected variable to be present in every
    /// binding, so a spec-correct answer with an unbound term was rejected. That is a defect in the
    /// reader, and this member says so.
    /// </para>
    /// <para>
    /// AND THE TEMPLATE HAD ALREADY TRIED TO PREVENT IT, which is the part a reader must not be
    /// pointed away from. <see cref="EuObjectFactsDiscoveryPlan"/>'s page template bound that key as
    /// <c>IF(BOUND(?value), STR(?value), "")</c> specifically to make it total, and the publisher's
    /// engine DID NOT HONOUR that guard: it selects IF's branch correctly and evaluates the
    /// arguments eagerly, so STR on the unbound term raised and the erroring BIND left the key
    /// unbound anyway. D1-05f part two replaced the guard with COALESCE, which the same engine does
    /// honour. So this member's first condition was never a missing guard; it was a guard the engine
    /// ignored.
    /// </remarks>
    [JsonStringEnumMemberName("page_decode_failed_on_our_side")]
    PageDecodeFailedOnOurSide = 14,

    // WHEN A REFUSAL LOOKS WRONG, GO TO THE RETAINED BYTES, NOT THE CODE THAT PRODUCED THE MESSAGE.
    //
    // Every refusal in this vocabulary is honest about what it OBSERVED, and that is exactly what
    // makes a misattributing one hard to see: the text is accurate and still points away from the
    // cause, because the cause is a rule WE declared. Three in this slice wore that costume.
    // PageBodyMalformed named the publisher for a page of 41 correct bindings, when our reader
    // required a variable SPARQL legitimately omits. CursorDidNotAdvance named the publisher for
    // answering a successor request our own terminal-page policy obliged us to send and which could
    // establish nothing. And the witness traversal carried the same misattribution one path over,
    // unnoticed while the page path was being narrowed.
    //
    // The rule pays rather than exhorts: it has now caught those three, and a fourth thing that was
    // not a refusal at all. A mutation sweep reported five guards killed; the reds were an artifact
    // of a mutation left in the tree by a run killed mid-write, and the tell was that one test died
    // under mutations touching unrelated paths. A guard dies to the mutation aimed at it; a guard
    // that dies to everything is broken, not sensitive. In all four cases the answer was in the
    // retained bytes and in none of them was it in the message.
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
/// Why <see cref="EuRepeatedEnumerationExecutor.RunWitnessTraversalAsync"/> did not deliver a real
/// canonical entry set. Closed, and deliberately narrower than <see cref="EuEnumerationRefusal"/>:
/// the witness traversal drives none of that enum's two-pass, threshold or keyset-continuation shape
/// (<see cref="EuWatermarkWitnessPlan"/>'s own remarks: a witness has neither a pre-count nor a
/// post-count over a partition), so this names only what a single boundary-reread page traversal can
/// itself observe.
/// </summary>
public enum EuWitnessTraversalRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("robots_bootstrap_refused")]
    RobotsBootstrapRefused = 1,

    /// <summary><see cref="EuWatermarkWitnessPlan.TryBindPage"/> itself refused.</summary>
    [JsonStringEnumMemberName("bind_refused")]
    BindRefused = 2,

    [JsonStringEnumMemberName("observation_not_executed")]
    ObservationNotExecuted = 3,

    [JsonStringEnumMemberName("status_not_admitted")]
    StatusNotAdmitted = 4,

    [JsonStringEnumMemberName("media_type_not_admitted")]
    MediaTypeNotAdmitted = 5,

    /// <summary>
    /// The delivered witness page body is DEMONSTRABLY NOT what the profile promised: not JSON at
    /// all, JSON without the results/bindings array, or a binding that is not an object. The detail
    /// names the offending position.
    /// </summary>
    /// <remarks>
    /// Narrowed for the same reason and by the same rule as
    /// <see cref="EuEnumerationRefusal.PageBodyMalformed"/>. This was the catch-all for EVERY parse
    /// failure in the witness traversal, including the two that are OURS: a projected variable
    /// absent from a binding, which is SPARQL 1.1's own encoding of unbound, and a row this reader
    /// could not turn into a tie-safe cursor. Both were reported under the publisher's name with no
    /// position and no digest. <see cref="PageDecodeFailedOnOurSide"/> carries them now.
    /// </remarks>
    [JsonStringEnumMemberName("page_body_malformed")]
    PageBodyMalformed = 6,

    /// <summary>
    /// THIS EXECUTOR could not decode a witness page the profile admits. Ours, not the publisher's.
    /// The detail carries the exception type, its message and the page body's own digest.
    /// </summary>
    /// <remarks>
    /// The witness counterpart of <see cref="EuEnumerationRefusal.PageDecodeFailedOnOurSide"/>, added when
    /// the same misattribution was found one traversal over: the page path had been narrowed while
    /// this one still blamed the office for an unbound term or a cursor this reader would not mint.
    /// </remarks>
    [JsonStringEnumMemberName("page_decode_failed_on_our_side")]
    PageDecodeFailedOnOurSide = 11,

    /// <summary><see cref="EuBoundaryCrossing.TryCross"/> itself refused.</summary>
    [JsonStringEnumMemberName("crossing_refused")]
    CrossingRefused = 7,

    /// <summary><see cref="EuWatermarkTraversalStep.TryAdvance"/> itself refused.</summary>
    [JsonStringEnumMemberName("step_refused")]
    StepRefused = 8,

    /// <summary><see cref="EuFeedWatermarkEntrySet.TryClose"/> itself refused.</summary>
    [JsonStringEnumMemberName("entry_set_refused")]
    EntrySetRefused = 9,

    /// <summary>
    /// <see cref="EuRepeatedEnumerationExecutor.MaximumWitnessPageRequests"/> was spent without the
    /// traversal reaching a confirmed terminal page. A safety bound, not an expected outcome: a first
    /// cut's own boundary-reread traversal normally confirms termination within one or two requests.
    /// </summary>
    [JsonStringEnumMemberName("page_budget_exhausted")]
    PageBudgetExhausted = 10,
}

public sealed class EuWitnessTraversalRefusalDetail
{
    internal EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal code, string? detail)
    {
        if (code == EuWitnessTraversalRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "A refusal detail requires a real refusal code.");
        }

        Code = code;
        Detail = detail;
    }

    public EuWitnessTraversalRefusal Code { get; }

    public string? Detail { get; }
}

/// <summary>Delivered or refused, never both and never neither.</summary>
public sealed class EuWitnessTraversalResult
{
    private EuWitnessTraversalResult(
        EuFeedWatermarkEntrySet? entries,
        string? deliveryEvidenceSha256,
        int productRequestCount,
        EuWitnessTraversalRefusalDetail? refusal)
    {
        Entries = entries;
        DeliveryEvidenceSha256 = deliveryEvidenceSha256;
        ProductRequestCount = productRequestCount;
        Refusal = refusal;
    }

    public static EuWitnessTraversalResult Delivered(
        EuFeedWatermarkEntrySet entries, string deliveryEvidenceSha256, int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrEmpty(deliveryEvidenceSha256);
        return new(entries, deliveryEvidenceSha256, productRequestCount, null);
    }

    public static EuWitnessTraversalResult Refused(EuWitnessTraversalRefusalDetail refusal, int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new(null, null, productRequestCount, refusal);
    }

    /// <summary>The traversal's real canonical entry set, actually observed. Present iff delivered.</summary>
    public EuFeedWatermarkEntrySet? Entries { get; }

    /// <summary>
    /// SHA-256 over the ordered, length-framed concatenation of every page's own retained HTTP
    /// evidence canonical bytes -- real evidence this run actually observed sending and receiving the
    /// witness query over HTTP, never a synthetic placeholder. Present iff delivered.
    /// </summary>
    public string? DeliveryEvidenceSha256 { get; }

    /// <summary>Publisher requests this traversal spent, robots excluded. Always populated.</summary>
    public int ProductRequestCount { get; }

    public EuWitnessTraversalRefusalDetail? Refusal { get; }
}

/// <summary>
/// Why <see cref="EuRepeatedEnumerationExecutor.RunDocumentFetchAsync"/> did not deliver real
/// evidence for a document-fetch GET. Closed and deliberately narrow: unlike
/// <see cref="EuWitnessTraversalRefusal"/> or <see cref="EuEnumerationRefusal"/>, this method never
/// classifies the status or media type of a completed response itself -- that is
/// <see cref="Lex.V3.Contracts.Source.Europe.EuDocumentFetchOutcome.Classify"/>'s own job, run by the
/// caller against the real <see cref="RoutedHttpEvidence"/> this type hands back on
/// <see cref="EuDocumentFetchAttemptResult.Evidence"/>. This vocabulary names only the two ways the
/// GET could not even be attempted for real: robots refused the session before any product request,
/// or every retryable attempt this run's own profile allows was spent without a header-complete
/// response.
/// </summary>
public enum EuDocumentFetchAttemptRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("robots_bootstrap_refused")]
    RobotsBootstrapRefused = 1,

    [JsonStringEnumMemberName("observation_not_executed")]
    ObservationNotExecuted = 2,
}

/// <summary>Executed for real (whatever the office answered), or refused before it ever sent. Never both, never neither.</summary>
public sealed class EuDocumentFetchAttemptResult
{
    private EuDocumentFetchAttemptResult(
        RoutedHttpEvidence? evidence, EuDocumentFetchAttemptRefusal? refusal, string? detail)
    {
        Evidence = evidence;
        Refusal = refusal;
        Detail = detail;
    }

    /// <summary>
    /// The real, retained route evidence for this one GET attempt. Present iff this result is
    /// <see cref="Executed"/>; the caller (<c>EuQueryExecutionAdapter</c>) classifies it through
    /// <see cref="Lex.V3.Contracts.Source.Europe.EuDocumentFetchOutcome.Classify"/>, since a completed
    /// 200, 400 or 404 are all equally "executed" here -- this type draws no line between them.
    /// </summary>
    public RoutedHttpEvidence? Evidence { get; }

    public EuDocumentFetchAttemptRefusal? Refusal { get; }

    public string? Detail { get; }

    public static EuDocumentFetchAttemptResult Executed(RoutedHttpEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new(evidence, null, null);
    }

    public static EuDocumentFetchAttemptResult Refused(EuDocumentFetchAttemptRefusal refusal, string? detail)
    {
        if (refusal == EuDocumentFetchAttemptRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, refusal, detail);
    }
}

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

    /// <summary>
    /// Below this a witness traversal refuses rather than looping forever. A first cut's own
    /// boundary-reread traversal normally confirms termination in one or two requests; this is a
    /// safety bound against a pathological or misbehaving endpoint, not an expected page count.
    /// </summary>
    internal const int MaximumWitnessPageRequests = 64;

    /// <summary>
    /// D1-05c-2 defect 3's own fix: actually runs the frozen witness plan's boundary-reread traversal
    /// over the real EU transport -- the identical <see cref="RoutedHttpAcquisitionSession"/>/
    /// <see cref="RepeatedEnumerationDeliveryReopenGlue"/> plumbing every other family already sends
    /// through -- rather than assuming an empty result. Not a family in D1-05c-1's own two-pass sense:
    /// the witness has no pre-count or post-count over a partition, so this drives
    /// <see cref="EuWatermarkWitnessPlan.TryBindPage"/>, <see cref="EuBoundaryCrossing.TryCross"/> and
    /// <see cref="EuWatermarkTraversalStep.TryAdvance"/> directly, one page at a time, rather than
    /// reusing <see cref="RunPassesAsync"/>'s own keyset pass loop, which the plan's own remarks say
    /// does not fit this shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The very first request's own crossing is built from the retained tie set Decision 81 already
    /// establishes: <see cref="EuWatermarkWitnessPlan.StartPosition"/> IS the one entry this run's
    /// own census already observed at that exact boundary (the tie-safe maximum
    /// <see cref="EuFirstCutWatermarkBootstrap"/> computed), so the retained tie set for the first
    /// page is exactly that one entry key. Every later page's own retained tie set is instead read
    /// off the PREVIOUS delivered page's own rows at its own next boundary watermark, the same "the
    /// earlier page retains the group sharing the boundary" rule <see cref="EuBoundaryCrossing"/>'s
    /// own remarks describe.
    /// </para>
    /// <para>
    /// Termination is confirmed, not merely observed: per <see cref="EuWatermarkTraversalStep"/>'s
    /// own remarks ("confirming a short page needs an empty successor request, which is the
    /// executor's business"), a page whose own <see cref="EuWatermarkTraversalStep.NextPosition"/> is
    /// null is requested again at the identical boundary once; if the confirming request also carries
    /// nothing beyond the boundary the traversal is done. If it instead finds something new (a row
    /// arrived at the boundary or beyond it since the first request), the loop folds that in and
    /// continues normally rather than stopping on stale information.
    /// </para>
    /// </remarks>
    /// <param name="plan">The frozen witness plan (<see cref="EuWatermarkWitnessPlan.TryFreeze"/>).</param>
    /// <param name="rendererSource">
    /// The renderer-source artifact naming <c>EuWatermarkWitnessSparqlRenderer</c>'s own code, held
    /// with its bytes exactly as every other Europe bind already requires.
    /// </param>
    /// <param name="sourceWitness">The bound robots-negotiation witness this session starts from.</param>
    public async Task<EuWitnessTraversalResult> RunWitnessTraversalAsync(
        EuWatermarkWitnessPlan plan,
        MachineQueryRendererSource rendererSource,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rendererSource);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var session = await StartSessionAsync(sourceWitness, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return EuWitnessTraversalResult.Refused(
                new EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal.RobotsBootstrapRefused, null),
                productRequestCount: 0);
        }

        var productRequestCount = 0;
        try
        {
            var executorWrittenMembership = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
            var evidenceBytesInOrder = new List<byte[]>();

            var position = plan.StartPosition;
            IReadOnlyList<string> retainedTieSet = new[] { position.CanonicalEntryKey };
            var steps = new List<EuWatermarkTraversalStep>();
            var consecutiveTerminalObservations = 0;

            while (true)
            {
                if (productRequestCount >= MaximumWitnessPageRequests)
                {
                    return EuWitnessTraversalResult.Refused(
                        new EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal.PageBudgetExhausted, null),
                        productRequestCount);
                }

                var bound = plan.TryBindPage(position, NewUrn(), NewUrn(), rendererSource, out var bindRefusal);
                if (bound is null)
                {
                    return EuWitnessTraversalResult.Refused(
                        new EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal.BindRefused, bindRefusal.ToString()),
                        productRequestCount);
                }

                var outcome = await _reopenGlue.ObserveAsync(
                        session, bound.Request, EuWatermarkWitnessPlan.ResponseMediaType, executorWrittenMembership,
                        () => productRequestCount, count => productRequestCount = count, cancellationToken)
                    .ConfigureAwait(false);
                if (outcome.Failure is { } failure)
                {
                    var code = failure.Kind switch
                    {
                        ObservationAttemptFailureKind.NotExecuted => EuWitnessTraversalRefusal.ObservationNotExecuted,
                        ObservationAttemptFailureKind.StatusNotAdmitted => EuWitnessTraversalRefusal.StatusNotAdmitted,
                        ObservationAttemptFailureKind.MediaTypeNotAdmitted => EuWitnessTraversalRefusal.MediaTypeNotAdmitted,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(outcome),
                            $"Unreachable: an unhandled {nameof(ObservationAttemptFailureKind)} '{failure.Kind}'."),
                    };
                    return EuWitnessTraversalResult.Refused(
                        new EuWitnessTraversalRefusalDetail(code, failure.OperationalDetail),
                        productRequestCount);
                }

                var transport = outcome.Transport!;
                evidenceBytesInOrder.Add(transport.HttpEvidence.CopyCanonicalBytes());

                IReadOnlyList<EuWatermarkCursor> deliveredPage;
                try
                {
                    deliveredPage = ParseWitnessRows(transport.RetainedPayloadBytes.Span);
                }
                catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
                {
                    // Same rule as the page path: only a tagged, positioned throw may name the
                    // publisher, and the default names us and carries enough to reopen the bytes.
                    var witnessBodySha256 = transport.HttpEvidence.Hops[0].Sha256;
                    return EuWitnessTraversalResult.Refused(
                        new EuWitnessTraversalRefusalDetail(
                            ClassifyWitnessParseFailure(exception),
                            $"{exception.GetType().Name}: {exception.Message} "
                            + $"(witness page body sha256 {witnessBodySha256})"),
                        productRequestCount);
                }

                var boundaryWatermark = position.WatermarkLexical;
                var rereadAtBoundary = deliveredPage
                    .Where(row => string.Equals(row.WatermarkLexical, boundaryWatermark, StringComparison.Ordinal))
                    .Select(static row => row.CanonicalEntryKey)
                    .ToArray();
                var firstBeyond = deliveredPage.FirstOrDefault(
                    row => !string.Equals(row.WatermarkLexical, boundaryWatermark, StringComparison.Ordinal));

                var crossing = EuBoundaryCrossing.TryCross(
                    position, retainedTieSet, rereadAtBoundary, firstBeyond, out var crossingRefusal);
                if (crossing is null)
                {
                    return EuWitnessTraversalResult.Refused(
                        new EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal.CrossingRefused, crossingRefusal.ToString()),
                        productRequestCount);
                }

                var step = EuWatermarkTraversalStep.TryAdvance(plan, crossing, deliveredPage, out var stepRefusal);
                if (step is null)
                {
                    return EuWitnessTraversalResult.Refused(
                        new EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal.StepRefused, stepRefusal.ToString()),
                        productRequestCount);
                }

                steps.Add(step);

                if (step.NextPosition is null)
                {
                    consecutiveTerminalObservations++;
                    if (consecutiveTerminalObservations >= 2)
                    {
                        break;
                    }

                    // Confirm with one empty-successor request at the identical boundary, per
                    // EuWatermarkTraversalStep's own remarks. The retained tie set for that repeat is
                    // the full boundary group this step just proved (crossing.RetainedTieSet plus
                    // whatever it carried forward), so a repeat of the identical publisher response
                    // reconciles cleanly and yields nothing newly delivered.
                    retainedTieSet = crossing.RetainedTieSet.Concat(crossing.CarriedForward).ToArray();
                    continue;
                }

                consecutiveTerminalObservations = 0;
                position = step.NextPosition;
                retainedTieSet = deliveredPage
                    .Where(row => string.Equals(row.WatermarkLexical, position.WatermarkLexical, StringComparison.Ordinal))
                    .Select(static row => row.CanonicalEntryKey)
                    .ToArray();
            }

            var entrySet = EuFeedWatermarkEntrySet.TryClose(steps, out var entrySetRefusal);
            if (entrySet is null)
            {
                return EuWitnessTraversalResult.Refused(
                    new EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal.EntrySetRefused, entrySetRefusal.ToString()),
                    productRequestCount);
            }

            return EuWitnessTraversalResult.Delivered(
                entrySet, CombinedSha256(evidenceBytesInOrder), productRequestCount);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
        {
            return EuWitnessTraversalResult.Refused(
                new EuWitnessTraversalRefusalDetail(EuWitnessTraversalRefusal.ObservationNotExecuted, exception.Message),
                productRequestCount);
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// D1-06c-EU defect 4 (SCOPE_RULING lex-event-20260904T130546972Z-c72fad2da5b34344af802c068d8fbf08
    /// item 4): sends one real document-fetch GET for <paramref name="boundRequest"/> (one
    /// <c>EuDocumentFetchPlan.Bind</c> result's own <c>Request</c>) and hands back the real, retained
    /// <see cref="RoutedHttpEvidence"/> for the caller to classify through
    /// <c>EuDocumentFetchOutcome.Classify</c>. This method itself never inspects the response's own
    /// status or media type, unlike <see cref="RunWitnessTraversalAsync"/>'s own use of the shared
    /// <see cref="RepeatedEnumerationDeliveryReopenGlue.ObserveAsync"/> door, which the document-fetch
    /// channel cannot reuse: that door admits only a byte-exact expected media type, and this route's
    /// own real responses append a charset parameter (<c>application/xhtml+xml;charset=UTF-8</c>) the
    /// address a GET was minted for never claims to equal (<c>EuDocumentFetchAddress.Accept</c> is
    /// exactly <c>application/xhtml+xml</c>, with no charset). One session per call, exactly the same
    /// pattern every other family here already uses (<see cref="RunCensusPartitionAsync"/>,
    /// <see cref="RunObjectFactsPartitionAsync"/> and <see cref="RunWitnessTraversalAsync"/> each start
    /// and dispose their own session too), so a document-fetch GET's own robots negotiation is neither
    /// shared with nor able to desynchronize from any other channel's.
    /// </summary>
    /// <param name="boundRequest">One <c>EuDocumentFetchPlan.Bind</c> result's own <c>Request</c>.</param>
    /// <param name="sourceWitness">The bound robots-negotiation witness this session starts from.</param>
    public async Task<EuDocumentFetchAttemptResult> RunDocumentFetchAsync(
        BoundMachineRequest boundRequest,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundRequest);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var session = await StartSessionAsync(sourceWitness, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return EuDocumentFetchAttemptResult.Refused(
                EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused, null);
        }

        try
        {
            var item = session.OpenPlanItem(boundRequest);
            var maximumAttempts = session.SourceProfile.MaximumAttempts;
            var attemptOrdinal = 0;
            RoutedHttpAcquisitionSession.AttemptResult attempt;
            while (true)
            {
                attempt = await item.ExecuteNextAttemptAsync(cancellationToken).ConfigureAwait(false);
                attemptOrdinal++;
                if (attempt.Kind == OfficialHttpAcquisitionOutcomeKind.ExecutedObservation)
                {
                    break;
                }

                // Mirrors RepeatedEnumerationDeliveryReopenGlue.ObserveAsync's identical retry
                // predicate: the one condition under which the session's own PlanItem allows another
                // attempt after a non-executed outcome (RoutedHttpAcquisitionSession.cs, PlanItem
                // .IsRetryable's pre-header branch) is a failure before headers completed.
                var retryable = attempt.PreHeaderFailureClass is
                    HttpPreHeaderFailureClass.HeaderDeadline or HttpPreHeaderFailureClass.TransportBeforeHeaders;
                if (!retryable || attemptOrdinal >= maximumAttempts)
                {
                    return EuDocumentFetchAttemptResult.Refused(
                        EuDocumentFetchAttemptRefusal.ObservationNotExecuted,
                        $"{attempt.OperationalReason}/{attempt.PreHeaderFailureClass}");
                }
            }

            var evidence = attempt.Evidence!;

            // Decision 78 retention: a run holds what it depends on. The evidence document is
            // written and reopened by digest exactly as RepeatedEnumerationDeliveryReopenGlue
            // .ObserveAsync already does for every other channel, so a document-fetch attempt's own
            // evidence is retained custody too, never left to live only in this process's memory.
            var evidenceBytes = evidence.CopyCanonicalBytes();
            var evidenceReceipt = await _custodyStore.CreateAsync(
                    evidenceBytes, CustodyClass.NightlyFloor90d, cancellationToken)
                .ConfigureAwait(false);
            var reopenedEvidenceBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore, evidenceReceipt.Reference.ContentSha256, cancellationToken)
                .ConfigureAwait(false);
            var reopenedEvidence = RoutedHttpEvidence.ParseAndVerify(reopenedEvidenceBytes.Span);

            return EuDocumentFetchAttemptResult.Executed(reopenedEvidence);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException)
        {
            return EuDocumentFetchAttemptResult.Refused(
                EuDocumentFetchAttemptRefusal.ObservationNotExecuted, exception.Message);
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Reads the witness page's own three-column projection (<c>?entry ?entry_key ?watermark</c>)
    /// into real <see cref="EuWatermarkCursor"/> rows. <c>?entry</c> itself is checked present (the
    /// query's own SELECT projects it) but not otherwise retained: the tie-safe position is the
    /// watermark and canonical entry key together (<see cref="EuWatermarkCursor"/>'s own remarks), and
    /// nothing in this traversal needs the raw entry IRI separately.
    /// </summary>
    private static IReadOnlyList<EuWatermarkCursor> ParseWitnessRows(ReadOnlySpan<byte> bytes)
    {
        System.Text.Json.JsonDocument document;
        try
        {
            document = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw WitnessPageFailure(
                "The witness page body is not JSON at all, at line " +
                $"{exception.LineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} byte " +
                $"{exception.BytePositionInLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}.",
                EuWitnessTraversalRefusal.PageBodyMalformed,
                exception);
        }

        using var _document = document;
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) ||
            results.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !results.TryGetProperty("bindings", out var bindings) ||
            bindings.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw WitnessPageFailure(
                $"The witness page body's root is {root.ValueKind} and carries no results/bindings array.",
                EuWitnessTraversalRefusal.PageBodyMalformed);
        }

        var rows = new List<EuWatermarkCursor>();
        var witnessOrdinal = 0;
        foreach (var binding in bindings.EnumerateArray())
        {
            if (binding.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw WitnessPageFailure(
                    $"The witness page body's binding at position {witnessOrdinal} is "
                    + $"{binding.ValueKind}, not an object.",
                    EuWitnessTraversalRefusal.PageBodyMalformed);
            }

            _ = RequireWitnessTermValue(binding, "entry");
            var entryKey = RequireWitnessTermValue(binding, "entry_key");
            var watermark = RequireWitnessTermValue(binding, "watermark");

            var cursor = EuWatermarkCursor.TryOpen(watermark, entryKey, out var cursorRefusal);
            if (cursor is null)
            {
                throw new FormatException($"The witness row could not become a tie-safe cursor: {cursorRefusal}.");
            }

            rows.Add(cursor);
            witnessOrdinal++;
        }

        return rows;
    }

    private const string WitnessParseFailureKey = "eu.witnessPageParseFailure";

    /// <summary>
    /// The refusal a witness page-parse failure carries. THE DEFAULT ARM NAMES US, exactly as the
    /// page path's own classifier does, and for the same reason: the two throws it covers are a
    /// projected variable absent from a binding, which is the publisher's correct encoding of an
    /// unbound term, and a row this reader would not mint a tie-safe cursor from. Neither is the
    /// office sending bad bytes.
    /// </summary>
    private static EuWitnessTraversalRefusal ClassifyWitnessParseFailure(Exception exception) =>
        exception.Data[WitnessParseFailureKey] switch
        {
            nameof(EuWitnessTraversalRefusal.PageBodyMalformed) =>
                EuWitnessTraversalRefusal.PageBodyMalformed,
            _ => EuWitnessTraversalRefusal.PageDecodeFailedOnOurSide,
        };

    /// <summary>Tags a witness page failure with the refusal its classifier should read back out.</summary>
    private static FormatException WitnessPageFailure(
        string message, EuWitnessTraversalRefusal refusal, Exception? inner = null)
    {
        var exception = new FormatException(message, inner);
        exception.Data[WitnessParseFailureKey] = refusal.ToString();
        return exception;
    }

    private static string RequireWitnessTermValue(System.Text.Json.JsonElement binding, string name)
    {
        if (!binding.TryGetProperty(name, out var term) ||
            term.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !term.TryGetProperty("value", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new FormatException($"The witness page response row is missing {name}.");
        }

        return value.GetString()!;
    }

    /// <summary>
    /// SHA-256 over the ordered, length-framed concatenation of every page's own retained HTTP
    /// evidence canonical bytes, mirroring <c>EuQueryExecutionAdapter.SingleMemberRegistrySha256</c>'s
    /// own length-prefixed incremental-hash convention.
    /// </summary>
    private static string CombinedSha256(IReadOnlyList<byte[]> orderedParts)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var part in orderedParts)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
            hash.AppendData(length);
            hash.AppendData(part);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
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
            // No bootstrap floor pre-emption here, per RULING
            // lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c. This used to refuse the whole run,
            // before its first product request, whenever any session artifact came back
            // RetainedUnenforced, which is why an EU run over a filesystem store could never reach a
            // publisher at all. The observed membership is RECORDED per artifact instead, and the
            // run continues: TryCompareAndReceipt below is handed this exact session map alongside
            // the executor's own, and RepeatedEnumerationDeliveryReceipt records every digest's
            // membership, carries the weakest of them as its RetainedFloor and names every
            // unenforced digest. Deleting the pre-emption therefore loses no fact; it stops
            // discarding one.
            //
            // Removed outright rather than re-conditioned onto a genuine custody failure, because at
            // THIS point every genuine failure already has a typed home. A MISSING custody record
            // for a hop is RepeatedEnumerationReceiptRefusal.SendClosureMemberNotHeld, raised by
            // that same builder: an artifact ABSENT from the map is not an artifact PRESENT with a
            // weak class, and the builder keeps those two apart. A digest mismatch or a failed write
            // surfaces as CustodyIntegrityException or CustodyRequiredException, which this method's
            // own catch turns into CustodyMemberMissing. Nothing was left for the removed member to
            // mean.
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
                // The detail is self-contained on purpose: exception type, its message, and the
                // page body's own digest, so a reader can reopen the exact retained bytes from the
                // refusal alone rather than correlating it against a store by hand. The digest is
                // also in its own field; repeating it here costs 64 characters and saves a lookup.
                var bodySha256 = transport.HttpEvidence.Hops[0].Sha256;
                return new PassOutcome(
                    null,
                    partitionKey,
                    new EuEnumerationRefusalDetail(
                        ClassifyPageParseFailure(exception),
                        pageOutcome.RequestOrdinal, null, transport.HttpEvidence.Hops[0].Status,
                        bodySha256, null, null, null,
                        $"{exception.GetType().Name}: {exception.Message} (page body sha256 {bodySha256})"));
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

            // ShortPageTerminal: a page carrying fewer rows than the limit IS the terminal page,
            // because ORDER BY plus LIMIT mean a short page has exhausted the result set. No
            // successor is requested, so the "did not advance" condition can no longer arise from a
            // request that had nothing to fetch. A page that fills the limit exactly still
            // continues, since a full page proves nothing about what follows it.
            if (rows.Count < pageLimit)
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
            // Family M's selection term is its own ?parent, which its five-part cursor carries at
            // key_1 -- unlike family X, whose key_1 is the discovered Expression.
            EuObjectFactsQuerySet.ManifestationFacts => "key_1",
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

    /// <summary>
    /// The refusal a page-parse failure carries. Every arm but the last is an explicit tag put on
    /// the exception by the throw site that knows what it rejected.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT ARM NAMES US, and that is the whole point of it. It used to answer
    /// <see cref="EuEnumerationRefusal.PageBodyMalformed"/>, so any failure without a specific
    /// member was reported as the publisher's bytes being bad, which both blamed the office for our
    /// decode and hid the real cause behind a name nobody would question.
    /// </remarks>
    private static EuEnumerationRefusal ClassifyPageParseFailure(Exception exception) =>
        exception.Data[PageParseFailureKey] switch
        {
            nameof(EuEnumerationRefusal.DeliveredKeyNotRepresentable) =>
                EuEnumerationRefusal.DeliveredKeyNotRepresentable,
            nameof(EuEnumerationRefusal.PageBodyMalformed) =>
                EuEnumerationRefusal.PageBodyMalformed,
            _ => EuEnumerationRefusal.PageDecodeFailedOnOurSide,
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
    /// <c>key_N</c> positional name: family P, X, W and M's own cursor variables happen to be
    /// literally named <c>key_1</c>.. (<see cref="EuObjectFactsDiscoveryPlan"/>'s own choice), but the census
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
        // Every throw below that can POINT AT the offending byte or term tags itself
        // PageBodyMalformed, which is the only thing that member may now mean. Anything else falls
        // through untagged to PageDecodeFailedOnOurSide, which names this executor rather than the office.
        System.Text.Json.JsonDocument document;
        try
        {
            document = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw PageFailure(
                "The page body is not JSON at all, at line " +
                $"{exception.LineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} byte " +
                $"{exception.BytePositionInLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}.",
                EuEnumerationRefusal.PageBodyMalformed,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !root.TryGetProperty("results", out var results) ||
                results.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !results.TryGetProperty("bindings", out var bindings) ||
                bindings.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                throw PageFailure(
                    $"The page body's root is {root.ValueKind} and carries no results/bindings array.",
                    EuEnumerationRefusal.PageBodyMalformed);
            }

            var rows = new List<string[]>();
            var ordinal = 0;
            foreach (var binding in bindings.EnumerateArray())
            {
                if (binding.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    throw PageFailure(
                        $"The page body's binding at position {ordinal} is {binding.ValueKind}, not an object.",
                        EuEnumerationRefusal.PageBodyMalformed);
                }

                var parts = new string[cursorVariables.Count];
                for (var index = 0; index < cursorVariables.Count; index++)
                {
                    var name = cursorVariables[index];

                    // Deliberately NOT tagged. A projected variable absent from a binding is
                    // SPARQL 1.1 JSON's own way of saying unbound, so refusing it is this reader's
                    // limitation and not a shape the publisher broke. It falls through to
                    // PageDecodeFailedOnOurSide until the cursor extraction learns to read an unbound term.
                    if (!binding.TryGetProperty(name, out var term) ||
                        term.ValueKind != System.Text.Json.JsonValueKind.Object ||
                        !term.TryGetProperty("value", out var value) ||
                        value.ValueKind != System.Text.Json.JsonValueKind.String)
                    {
                        throw new FormatException(
                            $"The page response row at position {ordinal} is missing {name}.");
                    }

                    parts[index] = RequireRepresentableKeyPart(value.GetString()!, name);
                }

                rows.Add(parts);
                ordinal++;
            }

            return rows;
        }
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
