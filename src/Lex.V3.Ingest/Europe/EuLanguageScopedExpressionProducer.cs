using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why a run produced no retained language-scoped expression derivation. Closed.</summary>
public enum EuLanguageScopedExpressionProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The request offered as the Expression-facts partition names a different query family.
    /// </summary>
    /// <remarks>
    /// A CALLER CONTRACT VIOLATION THAT REFUSES RATHER THAN THROWS, because it is a data
    /// disagreement a reviewer can reach: every object-facts family shares one request type and
    /// differs only by its <c>Set</c>, so handing this producer family M where family X belongs is
    /// an ordinary mistake rather than a broken program. Without this check the run would proceed,
    /// enumerate manifestation facts, and hand the decoder rows it would refuse for the wrong
    /// reason -- reporting a decode problem where the actual fault was the family asked for.
    /// </remarks>
    [JsonStringEnumMemberName("expression_facts_request_is_not_the_expression_family")]
    ExpressionFactsRequestIsNotTheExpressionFamily = 1,

    /// <summary>The request offered as the object-facts partition names a different query family.</summary>
    [JsonStringEnumMemberName("object_facts_request_is_not_the_object_family")]
    ObjectFactsRequestIsNotTheObjectFamily = 2,

    /// <summary>The Expression-facts enumeration itself refused. Its code travels in the detail.</summary>
    [JsonStringEnumMemberName("expression_facts_enumeration_refused")]
    ExpressionFactsEnumerationRefused = 3,

    /// <summary>The object-facts enumeration itself refused. Its code travels in the detail.</summary>
    [JsonStringEnumMemberName("object_facts_enumeration_refused")]
    ObjectFactsEnumerationRefused = 4,

    /// <summary>A delivered receipt would not prove its own family enumeration.</summary>
    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 5,

    /// <summary>
    /// <see cref="EuLanguageScopedExpressionDerivation.TryDerive"/> refused. Both its own refusal and
    /// the decoder's travel in the detail, because collapsing them loses which layer objected.
    /// </summary>
    [JsonStringEnumMemberName("derivation_refused")]
    DerivationRefused = 6,

    /// <summary>
    /// The object-facts batch does not cover every object the Expression-facts batch asks about, so
    /// a missing date would be indistinguishable from a date nobody asked for.
    /// </summary>
    /// <remarks>
    /// FOUND IN REVIEW, AND IT IS THIS FILE'S OWN RULE BEING BROKEN. The decoder filters family P
    /// rows to the works family X delivered Expressions of, so a P batch over a disjoint object set
    /// enumerates cleanly, contributes nothing, and every expression arrives with no date. The
    /// derivation then carries a non-null <c>ObjectFactsProof</c>, whose own documentation says that
    /// means a date delivery WAS consulted - so a reader concludes "asked, and the publisher stated
    /// none". P never asked. That is exactly the confusion
    /// <see cref="EuLanguageScopedExpressionProductionResult.ExpressionsOf"/> refuses a few lines
    /// away, reintroduced at the door that pairs the two families.
    /// <para>
    /// Coverage is required rather than equality: a P batch asking about MORE objects than X is
    /// harmless, because every work X delivered was still asked about. A P batch asking about fewer
    /// is not.
    /// </para>
    /// </remarks>
    [JsonStringEnumMemberName("object_facts_batch_does_not_cover_the_expression_batch")]
    ObjectFactsBatchDoesNotCoverTheExpressionBatch = 8,

    /// <summary>
    /// The derivation was produced but the store could not retain it, or handed back bytes its own
    /// digest does not name.
    /// </summary>
    /// <remarks>
    /// A DERIVATION THAT IS NOT HELD IS NOT DELIVERED. The whole point of this producer over the
    /// decoder it wraps is that the result survives the process that computed it, so a failed hold
    /// refuses the production rather than returning expressions with a null reference beside them.
    /// </remarks>
    [JsonStringEnumMemberName("derivation_not_retained")]
    DerivationNotRetained = 7,
}

/// <summary>
/// One production run's result: a retained derivation, or a named refusal. Never both, never neither.
/// </summary>
public sealed class EuLanguageScopedExpressionProductionResult
{
    private EuLanguageScopedExpressionProductionResult(
        EuLanguageScopedExpressionDerivation? derivation,
        DurableBlobWriteReceipt? retainedDerivation,
        DurableBlobWriteReceipt? retainedEpisode,
        IReadOnlySet<string>? objectsAskedAbout,
        EuLanguageScopedExpressionProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        Derivation = derivation;
        RetainedDerivation = retainedDerivation;
        RetainedEpisode = retainedEpisode;
        ObjectsAskedAbout = objectsAskedAbout;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
    }

    /// <summary>
    /// How many product requests this production actually sent, robots excluded, across both
    /// families. Carried on a refusal as well as on a success, because a refused run still spent
    /// what it spent and a ceiling report that only counted successes would understate traffic.
    /// </summary>
    public int ProductRequestCount { get; }

    /// <summary>The derivation, when this production delivered one.</summary>
    public EuLanguageScopedExpressionDerivation? Derivation { get; }

    /// <summary>
    /// The custody receipt for the retained derivation bytes, proven by reopening the digest the
    /// store returned rather than by trusting the write.
    /// </summary>
    public DurableBlobWriteReceipt? RetainedDerivation { get; }

    /// <summary>
    /// The objects this run asked family X about, in the plan's own canonical form -- the form the
    /// publisher was asked in and therefore answers in.
    /// </summary>
    /// <remarks>
    /// Taken from the request's own batch, never from a second caller-supplied list that could
    /// disagree with what was actually sent. It is what makes
    /// <see cref="ExpressionsOf"/> able to distinguish "no expression for this object" from
    /// "this object was never asked about".
    /// </remarks>
    /// <summary>
    /// The custody receipt for the retained episode record: which run observed this derivation.
    /// </summary>
    public DurableBlobWriteReceipt? RetainedEpisode { get; }

    public IReadOnlySet<string>? ObjectsAskedAbout { get; }

    public EuLanguageScopedExpressionProductionRefusal Refusal { get; }

    public string? Detail { get; }

    public bool Delivered => Refusal == EuLanguageScopedExpressionProductionRefusal.None;

    /// <summary>
    /// Every expression this run derived for one publisher work, or a throw when this run never
    /// asked about it.
    /// </summary>
    /// <remarks>
    /// AN EMPTY LIST HERE MEANS "ASKED, AND THE PUBLISHER STATED NONE". It never means "not asked",
    /// because not-asked throws. Decision 64's absence doctrine: an empty list and "we never asked"
    /// are indistinguishable unless something refuses to answer the second, so this refuses.
    /// </remarks>
    public IReadOnlyList<Lex.V3.Contracts.Derivation.LanguageScopedExpression> ExpressionsOf(string publisherWorkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherWorkId);
        if (!Delivered || Derivation is null || ObjectsAskedAbout is null)
        {
            throw new InvalidOperationException(
                "A refused expression production has no derived expressions.");
        }

        if (!ObjectsAskedAbout.Contains(publisherWorkId))
        {
            throw new InvalidOperationException(
                $"This run never asked about '{publisherWorkId}', so it states nothing about it.");
        }

        return [.. Derivation.Expressions.Where(
            expression => string.Equals(
                expression.Identity.PublisherWorkId, publisherWorkId, StringComparison.Ordinal))];
    }

    internal static EuLanguageScopedExpressionProductionResult Success(
        EuLanguageScopedExpressionDerivation derivation,
        DurableBlobWriteReceipt retainedDerivation,
        DurableBlobWriteReceipt retainedEpisode,
        IReadOnlySet<string> objectsAskedAbout,
        int productRequestCount) =>
        new(derivation, retainedDerivation, retainedEpisode, objectsAskedAbout,
            EuLanguageScopedExpressionProductionRefusal.None, null, productRequestCount);

    internal static EuLanguageScopedExpressionProductionResult Refused(
        EuLanguageScopedExpressionProductionRefusal refusal,
        string? detail,
        int productRequestCount)
    {
        if (refusal == EuLanguageScopedExpressionProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal), "A refusal result requires a real refusal code.");
        }

        return new(null, null, null, null, refusal, detail, productRequestCount);
    }
}

/// <summary>
/// The governed production path for #418's language-scoped corrigendum expressions: it runs the EU
/// query families, proves their deliveries, decodes them through the reviewed decoder, and retains
/// the result.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. <see cref="EuLanguageScopedExpressionDecode"/> was integrated as an offline
/// decoder reachable only from tests, and #418's completion boundary says in terms that this is not
/// ingestion: "A public or internal decoder called only by tests is a partial slice". Before this
/// file, <c>LanguageScopedExpression</c> appeared in no file under <c>src/Lex.V3.Ingest/</c> at all.
/// This is the path that reaches it from retained, custody-verified publisher rows.
/// </para>
/// <para>
/// ROWS ARE NEVER ACCEPTED, AND NO NEW DECODE DOOR IS OPENED. The shape is
/// <c>EuProcedureEventProducer.RunAsync</c>'s exactly: drive the executor, take the receipt, prove
/// the family enumeration from it, reopen every page's evidence in ordinal order, and hand those to
/// the door that reopens rows from bytes. Every input the decoder receives is rebuilt here from the
/// run's own receipt, so there is no argument by which a caller could name evidence the run did not
/// produce.
/// </para>
/// <para>
/// THE TWO FAMILIES ARE TWO SESSIONS, AND THAT IS NOT A CHOICE MADE HERE. Each object-facts
/// partition runs in its own <c>RoutedHttpAcquisitionSession</c>, which mints its own run identity.
/// So the Expression-facts and object-facts deliveries this producer pairs carry different
/// acquisition run references, necessarily and always; see
/// <see cref="EuLanguageScopedExpressionDerivation"/>'s remarks for why the obvious check against
/// that is not available and is not faked.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DO. It does not touch <c>EuCellarObjectDecode</c>'s single-language fold, and
/// it does not write into corpus/6 records. The derivation is retained as its own additive artifact
/// that a corpus record can later reference; widening <c>CorpusRecord</c> belongs to the seat
/// building that surface.
/// </para>
/// </remarks>
public sealed class EuLanguageScopedExpressionProducer
{
    private readonly ICustodyStore _custodyStore;
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public EuLanguageScopedExpressionProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal EuLanguageScopedExpressionProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _custodyStore = custodyStore;
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>
    /// Runs family X, optionally family P, derives the expressions and retains them. The only public
    /// way to obtain a derivation.
    /// </summary>
    /// <param name="expressionFactsRequest">
    /// The Expression-facts (family X) partition. Its <c>Set</c> must be
    /// <see cref="EuObjectFactsQuerySet.ExpressionFacts"/>.
    /// </param>
    /// <param name="objectFactsRequest">
    /// The object-facts (family P) partition the corrigendum dates are read from, or <c>null</c> to
    /// derive without dates. When present its <c>Set</c> must be
    /// <see cref="EuObjectFactsQuerySet.ObjectFacts"/>.
    /// </param>
    /// <param name="sourceWitness">The bound robots-negotiation witness each session starts from.</param>
    public async Task<EuLanguageScopedExpressionProductionResult> RunAsync(
        EuObjectFactsPartitionRunRequest expressionFactsRequest,
        EuObjectFactsPartitionRunRequest? objectFactsRequest,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expressionFactsRequest);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        // BEFORE ANY TRAFFIC. A family mix-up costs two sessions' worth of requests if it is found
        // after the run rather than before it.
        if (expressionFactsRequest.Set != EuObjectFactsQuerySet.ExpressionFacts)
        {
            return EuLanguageScopedExpressionProductionResult.Refused(
                EuLanguageScopedExpressionProductionRefusal.ExpressionFactsRequestIsNotTheExpressionFamily,
                $"the Expression-facts slot was given family {expressionFactsRequest.Set}.",
                productRequestCount: 0);
        }

        if (objectFactsRequest is not null && objectFactsRequest.Set != EuObjectFactsQuerySet.ObjectFacts)
        {
            return EuLanguageScopedExpressionProductionResult.Refused(
                EuLanguageScopedExpressionProductionRefusal.ObjectFactsRequestIsNotTheObjectFamily,
                $"the object-facts slot was given family {objectFactsRequest.Set}.",
                productRequestCount: 0);
        }

        // ALSO BEFORE ANY TRAFFIC, and compared in the plan's own canonical form because that is the
        // form the publisher is asked in and therefore the only form in which two batches can be
        // said to name the same object.
        if (objectFactsRequest is not null)
        {
            var asked = new HashSet<string>(
                EuObjectFactsDiscoveryPlan.RequestedPartitionMembers(objectFactsRequest.BatchObjects),
                StringComparer.Ordinal);
            var uncovered = EuObjectFactsDiscoveryPlan
                .RequestedPartitionMembers(expressionFactsRequest.BatchObjects)
                .Where(member => !asked.Contains(member))
                .ToArray();
            if (uncovered.Length > 0)
            {
                return EuLanguageScopedExpressionProductionResult.Refused(
                    EuLanguageScopedExpressionProductionRefusal.ObjectFactsBatchDoesNotCoverTheExpressionBatch,
                    $"the date delivery never asked about {uncovered.Length} of this run's "
                    + $"object(s), the first being '{uncovered[0]}'.",
                    productRequestCount: 0);
            }
        }

        var spent = 0;

        var expressionRun = await _executor
            .RunObjectFactsPartitionAsync(expressionFactsRequest, sourceWitness, cancellationToken)
            .ConfigureAwait(false);
        spent += expressionRun.ProductRequestCount;
        if (expressionRun.Receipt is not { } expressionReceipt)
        {
            return EuLanguageScopedExpressionProductionResult.Refused(
                EuLanguageScopedExpressionProductionRefusal.ExpressionFactsEnumerationRefused,
                expressionRun.Refusal?.Code.ToString()
                    ?? "the Expression-facts enumeration returned neither a receipt nor a refusal",
                spent);
        }

        EuProofBoundDelivery expressionDelivery;
        try
        {
            expressionDelivery = await BuildProofBoundDeliveryAsync(
                    expressionReceipt, expressionFactsRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EnumerationProofUnavailableException exception)
        {
            return EuLanguageScopedExpressionProductionResult.Refused(
                EuLanguageScopedExpressionProductionRefusal.EnumerationProofRefused,
                "expression facts: " + exception.Message,
                spent);
        }

        EuProofBoundDelivery? objectDelivery = null;
        if (objectFactsRequest is not null)
        {
            var objectRun = await _executor
                .RunObjectFactsPartitionAsync(objectFactsRequest, sourceWitness, cancellationToken)
                .ConfigureAwait(false);
            spent += objectRun.ProductRequestCount;
            if (objectRun.Receipt is not { } objectReceipt)
            {
                return EuLanguageScopedExpressionProductionResult.Refused(
                    EuLanguageScopedExpressionProductionRefusal.ObjectFactsEnumerationRefused,
                    objectRun.Refusal?.Code.ToString()
                        ?? "the object-facts enumeration returned neither a receipt nor a refusal",
                    spent);
            }

            try
            {
                objectDelivery = await BuildProofBoundDeliveryAsync(
                        objectReceipt, objectFactsRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (EnumerationProofUnavailableException exception)
            {
                return EuLanguageScopedExpressionProductionResult.Refused(
                    EuLanguageScopedExpressionProductionRefusal.EnumerationProofRefused,
                    "object facts: " + exception.Message,
                    spent);
            }
        }

        var derivation = EuLanguageScopedExpressionDerivation.TryDerive(
            expressionDelivery,
            objectDelivery,
            out var derivationRefusal,
            out var decodeRefusal,
            out var decodeDetail,
            out var offendingIri);
        if (derivation is null)
        {
            return EuLanguageScopedExpressionProductionResult.Refused(
                EuLanguageScopedExpressionProductionRefusal.DerivationRefused,
                $"derivation={derivationRefusal} decode={decodeRefusal} detail={decodeDetail} " +
                $"offendingIri={offendingIri}",
                spent);
        }

        // Decision 78 retention, through the one door that proves the hold by reopening the digest
        // the store returned rather than by trusting the write.
        //
        // TWO ARTIFACTS, AND THE SPLIT IS THE POINT. The derivation is byte-stable across executions
        // (S3-A04), so two runs over identical publisher rows hold ONE derivation blob at one
        // address. The episode record names which run observed it and is therefore different every
        // time, which is what it is for. Retaining only the first would drop provenance; retaining
        // them as one document would make the derivation's address move per run, which is the defect
        // review found.
        var (derivationReceipt, derivationHoldFailure) = await CustodyHold
            .TryHoldAsync(_custodyStore, derivation.DerivationBytes, cancellationToken)
            .ConfigureAwait(false);
        if (derivationReceipt is null)
        {
            return EuLanguageScopedExpressionProductionResult.Refused(
                EuLanguageScopedExpressionProductionRefusal.DerivationNotRetained,
                derivationHoldFailure,
                spent);
        }

        var (episodeReceipt, episodeHoldFailure) = await CustodyHold
            .TryHoldAsync(_custodyStore, derivation.EpisodeBytes, cancellationToken)
            .ConfigureAwait(false);
        if (episodeReceipt is null)
        {
            return EuLanguageScopedExpressionProductionResult.Refused(
                EuLanguageScopedExpressionProductionRefusal.DerivationNotRetained,
                episodeHoldFailure,
                spent);
        }

        return EuLanguageScopedExpressionProductionResult.Success(
            derivation,
            derivationReceipt,
            episodeReceipt,
            ObjectsAskedAbout(expressionFactsRequest),
            spent);
    }

    /// <summary>
    /// Rebuilds the decoder's proof-bound input from one run's own receipt. Nothing here is taken
    /// from a caller.
    /// </summary>
    private async Task<EuProofBoundDelivery> BuildProofBoundDeliveryAsync(
        RepeatedEnumerationDeliveryReceipt receipt,
        EuObjectFactsPartitionRunRequest request,
        CancellationToken cancellationToken)
    {
        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal)
            ?? throw new EnumerationProofUnavailableException(proofRefusal.ToString());

        var pages = new List<RepeatedEnumerationResolvedEvidence>(receipt.Delivery.PagesA.Pages.Count);
        foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static value => value.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken)
                .ConfigureAwait(false));
        }

        return new EuProofBoundDelivery(
            proof,
            receipt.Delivery,
            request.Plan.CreateDeliveryProfile(request.Set),
            receipt.Delivery.InterpretationProfileRef,
            receipt.Delivery.CountA.HttpEvidenceRef,
            pages);
    }

    private static IReadOnlySet<string> ObjectsAskedAbout(EuObjectFactsPartitionRunRequest request) =>
        new HashSet<string>(
            EuObjectFactsDiscoveryPlan.RequestedPartitionMembers(request.BatchObjects),
            StringComparer.Ordinal);

    /// <summary>
    /// A receipt that will not prove its own family enumeration, carried out of the page-reopen loop
    /// as a refusal rather than as a null nobody checks.
    /// </summary>
    private sealed class EnumerationProofUnavailableException(string message) : Exception(message);
}
