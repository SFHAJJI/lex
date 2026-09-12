using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why one batch of the OpinionRequest graph produced no coverage. Closed.</summary>
public enum LuxembourgOpinionRequestGraphRefusal
{
    /// <summary>No refusal: the batch's matrix was completed.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The enumeration itself was refused before any row existed.</summary>
    [JsonStringEnumMemberName("enumeration_refused")]
    EnumerationRefused = 1,

    /// <summary>The run delivered but its whole enumeration was not proven.</summary>
    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 2,

    /// <summary>
    /// The proven pages would not reopen into verified rows.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="EnumerationProofRefused"/>: the enumeration was proven and the
    /// failure is later, in re-deriving each page's own rows from its retained bytes.
    /// </remarks>
    [JsonStringEnumMemberName("verified_rows_refused")]
    VerifiedRowsRefused = 3,

    /// <summary>
    /// The verified rows would not complete into a readable matrix.
    /// </summary>
    /// <remarks>
    /// The coverage door's own typed reason travels out on <see cref="LuxembourgOpinionRequestGraphResult.Detail"/>.
    /// This producer does not restate that reason as its own vocabulary: a second enum mirroring
    /// another door's is two places for one rule, and the one that drifts is the copy.
    /// </remarks>
    [JsonStringEnumMemberName("matrix_not_completed")]
    MatrixNotCompleted = 4,
}

/// <summary>
/// One batch's completed matrix, or one typed refusal. Never both.
/// </summary>
/// <remarks>
/// <para>
/// IT CLAIMS NOTHING ABOUT THE CLASS. This is one batch of an inventory-issued partition, and
/// whether the batches together are the class is <see cref="LuxembourgOpinionRequestBatchCover"/>'s
/// decision and no producer's. A result that reported "complete" for its own batch would be the
/// first step toward a sweep that reconstructed completeness from what came back.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestGraphResult
{
    private LuxembourgOpinionRequestGraphResult(
        LuxembourgOpinionRequestCoverage? coverage,
        LuxembourgOpinionRequestGraphRefusal refusal,
        string? detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget)
    {
        Coverage = coverage;
        Refusal = refusal;
        Detail = detail;
        ProductRequestCount = productRequestCount;
        WireBudget = wireBudget ?? throw new ArgumentNullException(nameof(wireBudget));
    }

    /// <summary>This batch's completed matrix, non-null exactly when <see cref="Delivered"/>.</summary>
    public LuxembourgOpinionRequestCoverage? Coverage { get; }

    public LuxembourgOpinionRequestGraphRefusal Refusal { get; }

    /// <summary>The reason, in the words of whichever door refused.</summary>
    public string? Detail { get; }

    public int ProductRequestCount { get; }

    /// <summary>
    /// What the shared wire budget stood at when this run ended. Always present.
    /// </summary>
    /// <remarks>
    /// REQUIRED ON EVERY OUTCOME, delivered or refused, which is why it is a constructor parameter
    /// rather than something a caller may set. A refused run still sent requests - often it refused
    /// BECAUSE it had - and a ceiling whose evidence only survives success cannot be reconciled on
    /// the runs that most need reconciling.
    /// </remarks>
    public WireBudgetSnapshot WireBudget { get; }

    public bool Delivered => Refusal == LuxembourgOpinionRequestGraphRefusal.None;

    internal static LuxembourgOpinionRequestGraphResult Completed(
        LuxembourgOpinionRequestCoverage coverage,
        int productRequestCount,
        WireBudgetSnapshot wireBudget) =>
        new(coverage, LuxembourgOpinionRequestGraphRefusal.None, null, productRequestCount, wireBudget);

    internal static LuxembourgOpinionRequestGraphResult Refused(
        LuxembourgOpinionRequestGraphRefusal refusal,
        string detail,
        int productRequestCount,
        WireBudgetSnapshot wireBudget) =>
        new(null, refusal, detail, productRequestCount, wireBudget);
}

/// <summary>
/// Runs one inventory-issued batch of the OpinionRequest graph and completes its matrix.
/// </summary>
/// <remarks>
/// <para>
/// THE PRODUCER OWNS THE RUN: it drives the executor, requires a receipt, proves the enumeration,
/// reopens each page's retained bytes and passes them through
/// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> before any row is read.
/// </para>
/// <para>
/// IT DECODES NOTHING ITSELF, and that is the shape rather than an omission. The coverage door reads
/// each row's terms and requires every one to describe that row's own proof-covered key before it is
/// read at all - subject, kind, predicate, the publisher's digest of the value, the value's kind,
/// datatype and language. A second decoder here would be a second interpretation of the same rows,
/// and the one that drifts is whichever is not the one concluding from them. The draft graph's
/// producer decodes because its coverage takes caller projections; this family's does not, because
/// #556 removed them.
/// </para>
/// <para>
/// AND IT CLAIMS NO COMPLETENESS. One batch's matrix is all it produces. Whether the batches are the
/// class belongs to <see cref="LuxembourgOpinionRequestBatchCover"/>, which derives its expected
/// batches from the inventory rather than from what came back.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionRequestGraphProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public LuxembourgOpinionRequestGraphProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal LuxembourgOpinionRequestGraphProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>Runs one batch and completes its matrix. The only public way to obtain a coverage.</summary>
    public async Task<LuxembourgOpinionRequestGraphResult> RunAsync(
        LuxembourgOpinionRequestGraphRunRequest request,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var run = await _executor.RunLuxembourgOpinionRequestGraphAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);

        // READ ONCE, HERE. Everything below this line reads custody, not the wire, so this is the
        // last moment the budget changes on this run's account and the first moment it is complete.
        // Reading it at each return instead would be the same number by luck rather than by rule.
        var wireBudget = WireBudgetSnapshot.Of(request.WireBudget);
        if (run.Receipt is not { } receipt)
        {
            // THE PUBLISHER'S OWN REASON IS CARRIED, not just this seat's word for it. A refusal
            // reading only "EnumerationRefused" throws away the one thing that makes a live failure
            // debuggable without asking the publisher again.
            return LuxembourgOpinionRequestGraphResult.Refused(
                LuxembourgOpinionRequestGraphRefusal.EnumerationRefused,
                run.Refusal is { } refusal
                    ? refusal.Code + (refusal.CoreRefusalDetail is { Length: > 0 } detail
                        ? ": " + detail
                        : string.Empty)
                    : "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount,
                wireBudget);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return LuxembourgOpinionRequestGraphResult.Refused(
                LuxembourgOpinionRequestGraphRefusal.EnumerationProofRefused,
                proofRefusal.ToString(),
                run.ProductRequestCount,
                wireBudget);
        }

        var pages = new List<RepeatedEnumerationResolvedEvidence>(receipt.Delivery.PagesA.Pages.Count);
        foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static value => value.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken)
                .ConfigureAwait(false));
        }

        var profile = request.Plan.CreateDeliveryProfile();
        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof,
            receipt.Delivery,
            profile,
            receipt.Delivery.InterpretationProfileRef,
            receipt.Delivery.CountA.HttpEvidenceRef,
            pages,
            out var rowRefusal);
        if (rows is null)
        {
            return LuxembourgOpinionRequestGraphResult.Refused(
                LuxembourgOpinionRequestGraphRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount,
                wireBudget);
        }

        // THE PROOF, THE ROWS IT PROVES, AND THE BATCH THE INVENTORY ISSUED. Nothing else is passed
        // and there is nothing else to pass: the coverage door mints its own citation, derives the
        // admitted and retained halves from the plan, and takes its predicate set from the plan too.
        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            proof, rows, request.Assignment, out var coverageRefusal, out var coverageDetail);

        return coverage is null
            ? LuxembourgOpinionRequestGraphResult.Refused(
                LuxembourgOpinionRequestGraphRefusal.MatrixNotCompleted,
                coverageRefusal + (coverageDetail is { Length: > 0 }
                    ? ": " + coverageDetail
                    : string.Empty),
                run.ProductRequestCount,
                wireBudget)
            : LuxembourgOpinionRequestGraphResult.Completed(
                coverage, run.ProductRequestCount, wireBudget);
    }
}
