using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>One completed fixed-selection batch and the exact selection it answered.</summary>
public sealed record LuxembourgTranspositionIdentityCompletedBatch(
    IReadOnlyList<string> SelectedEuElis,
    LuxembourgTranspositionIdentityProductionResult Result);

/// <summary>
/// Closes several disjoint machine-input batches into one evidence-bound identity population.
/// Empty completed batches remain proved absences; a row outside its batch selection refuses.
/// </summary>
public sealed class LuxembourgTranspositionIdentityPopulationProducer
{
    private const string ReceiptSchema = "lex-luxembourg-transposition-identity-population/1";
    private readonly ICustodyStore _custodyStore;

    public LuxembourgTranspositionIdentityPopulationProducer(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<LuxembourgTranspositionIdentityProductionResult> ProduceAsync(
        IReadOnlyList<LuxembourgTranspositionIdentityCompletedBatch> batches,
        string populationResourceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentException.ThrowIfNullOrWhiteSpace(populationResourceId);
        _ = new SourceArtifactRef(populationResourceId, new string('0', 64));
        if (batches.Count == 0)
        {
            return Refused("A completed identity population requires at least one selected batch.", 0);
        }

        var selections = new HashSet<string>(StringComparer.Ordinal);
        var relations = new List<LuxembourgTranspositionIdentityRelation>();
        var receipts = new List<IdentityPopulationBatchReceipt>(batches.Count);
        var completionRefs = new HashSet<SourceArtifactRef>();
        var productRequestCount = 0;
        try
        {
            foreach (var batch in batches)
            {
                if (batch is null || batch.Result is null ||
                    !batch.Result.Delivered || batch.Result.Relations is null ||
                    batch.Result.CompletionEvidenceRef is not { } completion)
                {
                    var attempted = batch?.Result?.ProductRequestCount ?? 0;
                    return Refused(
                        "Every identity batch must be delivered with one completion proof.",
                        checked(productRequestCount + attempted));
                }

                var selected = LuxembourgTranspositionIdentityDiscoveryPlan
                    .CanonicalizeSelection(batch.SelectedEuElis);
                if (selected.Any(value => !selections.Add(value)))
                {
                    return Refused("Identity batch selections must be disjoint.", productRequestCount);
                }
                if (!completionRefs.Add(completion))
                {
                    return Refused("Each identity batch must carry a distinct completion proof.", productRequestCount);
                }

                var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
                foreach (var relation in batch.Result.Relations)
                {
                    if (!selectedSet.Contains(relation.EuEli) ||
                        !Equals(relation.CompletionEvidenceRef, completion))
                    {
                        return Refused(
                            "An identity row is outside its exact selection or carries another batch's proof.",
                            checked(productRequestCount + batch.Result.ProductRequestCount));
                    }
                    relations.Add(relation);
                }

                productRequestCount = checked(productRequestCount + batch.Result.ProductRequestCount);
                receipts.Add(new IdentityPopulationBatchReceipt(
                    selected,
                    completion,
                    batch.Result.Relations.Count,
                    batch.Result.ProductRequestCount));
            }
        }
        catch (ArgumentException exception)
        {
            return Refused(exception.Message, productRequestCount);
        }
        catch (OverflowException)
        {
            return Refused("The aggregate product request count exceeds the representable bound.", productRequestCount);
        }

        var ambiguous = relations
            .GroupBy(static relation => relation.LocalEuWorkUri, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(static relation => relation.EuEli)
                .Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (ambiguous is not null)
        {
            return Refused(
                $"The local EU work {ambiguous.Key} maps to more than one selected EU identity.",
                productRequestCount);
        }

        var document = new IdentityPopulationReceipt(
            ReceiptSchema,
            receipts.OrderBy(static receipt => receipt.SelectedEuElis[0], StringComparer.Ordinal).ToArray());
        var bytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
        var held = await CustodyHold.TryHoldAsync(
            _custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (held.Receipt is null)
        {
            return LuxembourgTranspositionIdentityProductionResult.Refused(
                LuxembourgTranspositionIdentityProductionRefusal.CompositeEvidenceNotHeld,
                $"The identity population receipt was not held: {held.Failure}",
                productRequestCount);
        }

        return LuxembourgTranspositionIdentityProductionResult.Success(
            relations.OrderBy(static relation => relation.EuEli, StringComparer.Ordinal)
                .ThenBy(static relation => relation.NationalMeasureUri, StringComparer.Ordinal)
                .ThenBy(static relation => relation.LocalEuWorkUri, StringComparer.Ordinal)
                .ToArray(),
            new SourceArtifactRef(
                populationResourceId,
                held.Receipt.Reference.ContentSha256),
            productRequestCount);
    }

    private static LuxembourgTranspositionIdentityProductionResult Refused(
        string detail,
        int productRequestCount) =>
        LuxembourgTranspositionIdentityProductionResult.Refused(
            LuxembourgTranspositionIdentityProductionRefusal.BatchPopulationRefused,
            detail,
            productRequestCount);

    private sealed record IdentityPopulationReceipt(
        string Schema,
        IReadOnlyList<IdentityPopulationBatchReceipt> Batches);

    private sealed record IdentityPopulationBatchReceipt(
        IReadOnlyList<string> SelectedEuElis,
        SourceArtifactRef CompletionEvidenceRef,
        int RelationCount,
        int ProductRequestCount);
}
