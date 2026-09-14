using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why <see cref="EuCorrigendumTripwireProducer.RunAsync"/> delivered no tripwire set. Closed.</summary>
/// <remarks>
/// Four members, because this producer adds exactly four conditions of its own to the path it
/// composes: the delivery it cannot fold without, the inner production it composes over, the fold
/// itself, and the hold. Every condition the expression producer and the fold already name travels
/// as their own code in the detail rather than being flattened into a new vocabulary here.
/// </remarks>
public enum EuCorrigendumTripwireProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// No object-facts request was given. The tripwire reads its Corrects edges from that family's
    /// retained pages, so without it there is nothing to fold; refused before any traffic.
    /// </summary>
    [JsonStringEnumMemberName("object_facts_request_required")]
    ObjectFactsRequestRequired = 1,

    /// <summary>
    /// <see cref="EuLanguageScopedExpressionProducer.RunAsync"/> refused. Its own code and detail
    /// travel in the detail, and its refused result is carried so nothing it reported is lost.
    /// </summary>
    [JsonStringEnumMemberName("expression_production_refused")]
    ExpressionProductionRefused = 2,

    /// <summary>
    /// <see cref="EuCorrigendumTripwireSet.TryDerive"/> refused the two deliveries the run rebuilt.
    /// The contract's own code, detail and offending IRI travel in the detail.
    /// </summary>
    [JsonStringEnumMemberName("tripwire_refused")]
    TripwireRefused = 3,

    /// <summary>
    /// The custody store would not hold the set's canonical or lineage bytes, or would not prove
    /// the hold by reopening the digest it returned. The store's reason travels in the detail.
    /// </summary>
    [JsonStringEnumMemberName("tripwire_not_retained")]
    TripwireNotRetained = 4,
}

/// <summary>One governed production of the corrigendum tripwire set: delivered with its receipts, or refused by name.</summary>
public sealed class EuCorrigendumTripwireProductionResult
{
    private EuCorrigendumTripwireProductionResult(
        EuLanguageScopedExpressionProductionResult? expressions,
        EuCorrigendumTripwireSet? tripwireSet,
        DurableBlobWriteReceipt? retainedTripwire,
        DurableBlobWriteReceipt? retainedTripwireLineage,
        EuCorrigendumTripwireProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        Expressions = expressions;
        TripwireSet = tripwireSet;
        RetainedTripwire = retainedTripwire;
        RetainedTripwireLineage = retainedTripwireLineage;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    /// <summary>
    /// The inner expression production this run composed over: delivered, with its own retained
    /// derivation and episode, or refused. Null only when this producer refused before running it.
    /// </summary>
    public EuLanguageScopedExpressionProductionResult? Expressions { get; }

    /// <summary>The folded set, when delivered. Its <c>Derivation</c> is the inner run's, byte for byte.</summary>
    public EuCorrigendumTripwireSet? TripwireSet { get; }

    /// <summary>The store's receipt for the set's canonical bytes, held under their own digest.</summary>
    public DurableBlobWriteReceipt? RetainedTripwire { get; }

    /// <summary>The store's receipt for the set's lineage bytes, retained beside the canonical bytes.</summary>
    public DurableBlobWriteReceipt? RetainedTripwireLineage { get; }

    public EuCorrigendumTripwireProductionRefusal Refusal { get; }

    public string? Detail { get; }

    /// <summary>Every product request the inner run sent, reported whether this run delivered or refused.</summary>
    public int ProductRequestCount { get; }

    public bool Delivered => Refusal == EuCorrigendumTripwireProductionRefusal.None;

    internal static EuCorrigendumTripwireProductionResult Success(
        EuLanguageScopedExpressionProductionResult expressions,
        EuCorrigendumTripwireSet tripwireSet,
        DurableBlobWriteReceipt retainedTripwire,
        DurableBlobWriteReceipt retainedTripwireLineage,
        int productRequestCount)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(tripwireSet);
        ArgumentNullException.ThrowIfNull(retainedTripwire);
        ArgumentNullException.ThrowIfNull(retainedTripwireLineage);
        return new(
            expressions, tripwireSet, retainedTripwire, retainedTripwireLineage,
            EuCorrigendumTripwireProductionRefusal.None, null, productRequestCount);
    }

    internal static EuCorrigendumTripwireProductionResult Refused(
        EuCorrigendumTripwireProductionRefusal refusal,
        string? detail,
        EuLanguageScopedExpressionProductionResult? expressions,
        int productRequestCount)
    {
        if (refusal == EuCorrigendumTripwireProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal), "A refusal result requires a real refusal code.");
        }

        return new(expressions, null, null, null, refusal, detail, productRequestCount);
    }
}

/// <summary>
/// The governed production of the EU corrigendum tripwire set: one acquisition through the
/// accepted expression producer, one fold over the two proof-bound deliveries it rebuilt, and the
/// set's canonical and lineage bytes held in custody.
/// </summary>
/// <remarks>
/// <para>
/// A COMPOSITION, NOT A SECOND ACQUISITION PATH. <see cref="EuLanguageScopedExpressionProducer"/>
/// already drives families X and P under one wire budget, refuses a family mix-up and an object
/// batch that does not cover the expression batch before any traffic, rebuilds both deliveries
/// from the run receipts through the reopen glue, derives, and retains derivation and episode.
/// <see cref="EuCorrigendumTripwireSet.TryDerive"/> takes exactly those two deliveries. Running the
/// enumerations a second time here would double the publisher traffic for the same rows, and
/// rebuilding the deliveries here would be a second copy of a reviewed path; this producer does
/// neither. It runs the accepted producer's internal core once, which hands back the deliveries it
/// rebuilt beside its result - outputs of that one call, never inputs to any factory - folds over
/// them, and holds what the fold produced.
/// </para>
/// <para>
/// THE OBJECT-FACTS DELIVERY IS REQUIRED HERE, OPTIONAL THERE. The expression producer can derive
/// expressions without dates; the tripwire cannot exist without the Corrects edges family P
/// carries. A run without an object-facts request is refused before any traffic rather than
/// producing an empty set that would read as "no corrigendum".
/// </para>
/// <para>
/// WHAT IS RETAINED, AND WHY TWICE. The set's canonical bytes are byte-stable across executions
/// (S3-A04), so two runs over identical publisher rows hold one tripwire artifact at one address.
/// The lineage bytes name the derivation's episode and every page digest and therefore differ
/// per run, which is what they are for. Both go through <see cref="CustodyHold.TryHoldAsync"/>,
/// the one door that proves a hold by reopening the digest the store returned. The inner run's
/// derivation and episode receipts travel on the inner result; this producer does not retain the
/// derivation a second time, and the set's own <c>Derivation</c> is that retained derivation byte
/// for byte, because the same two deliveries derive the same bytes.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DO. It does not join the adapter's E-family run; when and how the two
/// producers join it is a later bounded slice, and any live run is owner-gated traffic. It does
/// not bind into the shared envelope or corpus/6, and it does not render anything.
/// </para>
/// </remarks>
public sealed class EuCorrigendumTripwireProducer
{
    private readonly ICustodyStore _custodyStore;
    private readonly EuLanguageScopedExpressionProducer _expressions;

    public EuCorrigendumTripwireProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal EuCorrigendumTripwireProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _custodyStore = custodyStore;
        _expressions = new EuLanguageScopedExpressionProducer(custodyStore, timeProvider, testHandlerOverride);
    }

    /// <summary>Runs one acquisition, folds the tripwire set, and holds it; or refuses by name.</summary>
    /// <param name="expressionFactsRequest">The family X request, as the expression producer takes it.</param>
    /// <param name="objectFactsRequest">The family P request. Required; a null refuses before traffic.</param>
    /// <param name="sourceWitness">The bound source witness the expression producer takes.</param>
    public async Task<EuCorrigendumTripwireProductionResult> RunAsync(
        EuObjectFactsPartitionRunRequest expressionFactsRequest,
        EuObjectFactsPartitionRunRequest? objectFactsRequest,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expressionFactsRequest);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        // BEFORE ANY TRAFFIC. Without the object-facts family there are no Corrects edges to read,
        // and a set folded from nothing would read as "no corrigendum".
        if (objectFactsRequest is null)
        {
            return EuCorrigendumTripwireProductionResult.Refused(
                EuCorrigendumTripwireProductionRefusal.ObjectFactsRequestRequired,
                "the tripwire reads its Corrects edges from the object-facts delivery; without one there is nothing to fold.",
                expressions: null,
                productRequestCount: 0);
        }

        var (expressions, expressionFacts, objectFacts) = await _expressions
            .RunWithDeliveriesAsync(expressionFactsRequest, objectFactsRequest, sourceWitness, cancellationToken)
            .ConfigureAwait(false);
        if (!expressions.Delivered)
        {
            return EuCorrigendumTripwireProductionResult.Refused(
                EuCorrigendumTripwireProductionRefusal.ExpressionProductionRefused,
                $"{expressions.Refusal}: {expressions.Detail}",
                expressions,
                expressions.ProductRequestCount);
        }

        // The two deliveries the inner run rebuilt from its own receipts, handed back by the same
        // call that delivered - outputs of that run, never inputs from anywhere. Both are non-null
        // on a delivered run with an object-facts request, which this one always has.
        var set = EuCorrigendumTripwireSet.TryDerive(
            expressionFacts!,
            objectFacts!,
            out var refusal,
            out var detail,
            out var offendingIri);
        if (set is null)
        {
            return EuCorrigendumTripwireProductionResult.Refused(
                EuCorrigendumTripwireProductionRefusal.TripwireRefused,
                $"{refusal} detail={detail} offendingIri={offendingIri}",
                expressions,
                expressions.ProductRequestCount);
        }

        var (tripwireReceipt, tripwireHoldFailure) = await CustodyHold
            .TryHoldAsync(_custodyStore, set.CanonicalBytes, cancellationToken)
            .ConfigureAwait(false);
        if (tripwireReceipt is null)
        {
            return EuCorrigendumTripwireProductionResult.Refused(
                EuCorrigendumTripwireProductionRefusal.TripwireNotRetained,
                "canonical: " + tripwireHoldFailure,
                expressions,
                expressions.ProductRequestCount);
        }

        var (lineageReceipt, lineageHoldFailure) = await CustodyHold
            .TryHoldAsync(_custodyStore, set.LineageBytes, cancellationToken)
            .ConfigureAwait(false);
        if (lineageReceipt is null)
        {
            return EuCorrigendumTripwireProductionResult.Refused(
                EuCorrigendumTripwireProductionRefusal.TripwireNotRetained,
                "lineage: " + lineageHoldFailure,
                expressions,
                expressions.ProductRequestCount);
        }

        return EuCorrigendumTripwireProductionResult.Success(
            expressions, set, tripwireReceipt, lineageReceipt, expressions.ProductRequestCount);
    }
}
