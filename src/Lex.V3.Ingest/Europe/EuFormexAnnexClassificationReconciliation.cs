using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why acquired Formex inventories could not be reconciled with annex classifications.</summary>
public enum EuFormexAnnexClassificationReconciliationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("acquired_inventory_claimed_twice")]
    AcquiredInventoryClaimedTwice = 1,

    [JsonStringEnumMemberName("classification_outside_acquired_population")]
    ClassificationOutsideAcquiredPopulation = 2,

    [JsonStringEnumMemberName("classification_inventory_disagrees")]
    ClassificationInventoryDisagrees = 3,

    [JsonStringEnumMemberName("classification_supplied_twice")]
    ClassificationSuppliedTwice = 4,

    [JsonStringEnumMemberName("classification_missing")]
    ClassificationMissing = 5,
}

/// <summary>
/// Proves that every acquired inventory in a total Formex run reconciliation has exactly one
/// complete bound-annex classification, and that no other Formex outcome has one.
/// </summary>
public sealed class EuFormexAnnexClassificationReconciliation
{
    private EuFormexAnnexClassificationReconciliation(
        EuFormexRunOutcomeReconciliation formex,
        IReadOnlyList<EuBoundAnnexBodyClassification> classifications)
    {
        Formex = formex;
        Classifications = Array.AsReadOnly(classifications.ToArray());
    }

    public EuFormexRunOutcomeReconciliation Formex { get; }

    /// <summary>One classification per acquired inventory, in Formex outcome order.</summary>
    public IReadOnlyList<EuBoundAnnexBodyClassification> Classifications { get; }

    public static EuFormexAnnexClassificationReconciliation? TryClose(
        EuFormexRunOutcomeReconciliation formex,
        IReadOnlyList<EuBoundAnnexBodyClassification> classifications,
        out EuFormexAnnexClassificationReconciliationRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(formex);
        ArgumentNullException.ThrowIfNull(classifications);
        refusal = EuFormexAnnexClassificationReconciliationRefusal.None;
        detail = null;

        var expected = new Dictionary<string, EuFormexAnnexInventory>(StringComparer.Ordinal);
        foreach (var outcome in formex.Outcomes)
        {
            if (outcome.Kind != EuFormexPackageOutcomeKind.Acquired)
            {
                continue;
            }

            var inventory = outcome.AcquiredInventory!;
            if (!expected.TryAdd(inventory.IdentitySha256, inventory))
            {
                refusal = EuFormexAnnexClassificationReconciliationRefusal.AcquiredInventoryClaimedTwice;
                detail = inventory.IdentitySha256;
                return null;
            }
        }

        var delivered = new Dictionary<string, EuBoundAnnexBodyClassification>(StringComparer.Ordinal);
        foreach (var classification in classifications)
        {
            ArgumentNullException.ThrowIfNull(classification, nameof(classifications));
            var inventoryIdentity = classification.Binding.FormexInventoryIdentitySha256;
            if (!expected.TryGetValue(inventoryIdentity, out var inventory))
            {
                refusal = EuFormexAnnexClassificationReconciliationRefusal.ClassificationOutsideAcquiredPopulation;
                detail = inventoryIdentity;
                return null;
            }

            if (!BelongsTo(classification, inventory))
            {
                refusal = EuFormexAnnexClassificationReconciliationRefusal.ClassificationInventoryDisagrees;
                detail = inventoryIdentity;
                return null;
            }

            if (!delivered.TryAdd(inventoryIdentity, classification))
            {
                refusal = EuFormexAnnexClassificationReconciliationRefusal.ClassificationSuppliedTwice;
                detail = inventoryIdentity;
                return null;
            }
        }

        foreach (var outcome in formex.Outcomes)
        {
            if (outcome.Kind != EuFormexPackageOutcomeKind.Acquired)
            {
                continue;
            }

            var inventoryIdentity = outcome.AcquiredInventory!.IdentitySha256;
            if (!delivered.ContainsKey(inventoryIdentity))
            {
                refusal = EuFormexAnnexClassificationReconciliationRefusal.ClassificationMissing;
                detail = inventoryIdentity;
                return null;
            }
        }

        var ordered = new List<EuBoundAnnexBodyClassification>(expected.Count);
        foreach (var outcome in formex.Outcomes)
        {
            if (outcome.Kind == EuFormexPackageOutcomeKind.Acquired)
            {
                ordered.Add(delivered[outcome.AcquiredInventory!.IdentitySha256]);
            }
        }

        return new EuFormexAnnexClassificationReconciliation(formex, ordered);
    }

    private static bool BelongsTo(
        EuBoundAnnexBodyClassification classification,
        EuFormexAnnexInventory inventory)
    {
        var binding = classification.Binding;
        return binding.Expression == inventory.TransportBinding.Expression
            && binding.FormexBody == inventory.TransportBinding.FormexBody
            && binding.FormexProfileRef == inventory.ProfileRef
            && string.Equals(
                DurableBlobWriteReceiptDigest.Of(binding.FormexSourceReceipt),
                DurableBlobWriteReceiptDigest.Of(inventory.SourceReceipt),
                StringComparison.Ordinal)
            && binding.Members.Select(static member => member.Formex)
                .SequenceEqual(inventory.Members);
    }
}
