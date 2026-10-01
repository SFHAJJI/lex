using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuLanguageScopedExpressionProducer
{
    private const string CheckpointSchema = "lex-eu-expression-production-checkpoint/2";

    /// <summary>
    /// Reopens this producer's retained pairing and derives the original bytes again without
    /// acquiring publisher data. ProductRequestCount is zero for the restoration itself.
    /// </summary>
    public static async Task<EuLanguageScopedExpressionProductionResult> ReopenAsync(
        ICustodyStore store, SourceArtifactRef checkpoint, CancellationToken cancellationToken)
    {
        var (result, _, _) = await ReopenWithDeliveriesAsync(store, checkpoint, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // Outputs from this checked restoration only, matching the live producer's composition door.
    internal static async Task<(EuLanguageScopedExpressionProductionResult Result, EuProofBoundDelivery? ExpressionFacts,
        EuProofBoundDelivery? ObjectFacts)> ReopenWithDeliveriesAsync(
        ICustodyStore store, SourceArtifactRef checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<ProductionCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != CheckpointSchema || !bytes.Span.SequenceEqual(EncodeCheckpoint(document)))
                throw new CustodyIntegrityException("Expression checkpoint framing disagrees.");
            var budget = WireRequestBudget.OfWireRequests(2); // Shared contract witness; never spent by this path.
            var expression = await ReopenFamilyAsync(store, document.Expression, budget, cancellationToken)
                .ConfigureAwait(false);
            var objects = document.Objects is null ? null : await ReopenFamilyAsync(store, document.Objects, budget,
                cancellationToken).ConfigureAwait(false);
            if (RefuseBeforeTraffic(expression.Request, objects?.Request) is { } refused)
                return (refused, null, null);
            // The private derivation path accepts only these checked restored runs. Its object
            // callback cannot acquire data, and there is no delivery input on this public surface.
            var producer = new EuLanguageScopedExpressionProducer(store, TimeProvider.System);
            var restored = await producer.DeriveAndRetainAsync(expression.Request, expression.Run, 0,
                _ => Task.FromResult<(EuObjectFactsPartitionRunRequest Request, EuEnumerationRunResult Run, int AdditionalSpend)?>(
                    objects is null ? null : (objects.Request, objects.Run, 0)),
                cancellationToken, document, checkpoint).ConfigureAwait(false);
            if (budget.Spent != 0) throw new CustodyIntegrityException("Offline expression restoration spent publisher requests.");
            return restored;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Expression checkpoint failed independent restoration.", exception);
        }
    }

    private static async Task<RestoredFamily> ReopenFamilyAsync(ICustodyStore store, FamilyCheckpoint family,
        WireRequestBudget budget, CancellationToken cancellationToken)
    {
        var receipt = await EuEnumerationCheckpoint.RestoreReceiptAsync(store, family.Checkpoint, family.Run,
            family.Profile, cancellationToken).ConfigureAwait(false);
        var plan = EuObjectFactsDiscoveryPlan.Create();
        RepeatedEnumerationInterpretationProfileIdentity.Validate(family.Profile, plan.CreateDeliveryProfile(family.Set));
        if (receipt.Delivery.PartitionKey != EuObjectFactsDiscoveryPlan.PartitionKeyFor(family.Batch))
            throw new CustodyIntegrityException("Expression checkpoint batch does not match the observed partition.");
        var sourceBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, family.Renderer.Sha256, cancellationToken)
            .ConfigureAwait(false);
        var source = MachineQueryRendererSource.Open(family.Renderer, sourceBytes.Span);
        var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
        foreach (var count in new[] { receipt.Delivery.CountA, receipt.Delivery.CountB })
        {
            if (family.PlanResourceId != count.QueryPlanRef.ResourceId)
                throw new CustodyIntegrityException("Expression plan identity differs from the retained count.");
            var evidence = await glue.ReopenPageEvidenceAsync(count, cancellationToken).ConfigureAwait(false);
            var parameters = evidence.QueryInput.OrderedParameters.Where(value => value.Name == "pass_id").ToArray();
            if (parameters.Length != 1 || parameters[0].Kind != MachineQueryParameterKind.BoundedInteger ||
                parameters[0].IntegerValue is not { } pass || pass is not (1 or 2))
                throw new CustodyIntegrityException("Expression count must have exactly one bounded pass, 1 or 2.");
            var rebound = plan.BindCount(family.Set, family.Batch, (EuObjectFactsQueryPass)checked((int)pass),
                count.QueryPlanRef.ResourceId, count.QueryInputRef.ResourceId, source);
            MachineQueryPlanIdentity.Validate(count.QueryPlanRef, rebound.MachinePlan);
            if (rebound.InputArtifact.ArtifactRef != count.QueryInputRef)
                throw new CustodyIntegrityException("Expression batch parameters differ from the retained count.");
            var opened = MachineQueryBinder.OpenForSend(rebound.Request);
            var heldBody = await CustodyRestore.ReadByDigestCheckedAsync(store, evidence.LogicalRequest.Body.Sha256,
                cancellationToken).ConfigureAwait(false);
            if (!heldBody.Span.SequenceEqual(opened.CopyRequestBody()))
                throw new CustodyIntegrityException("Expression count request differs from its closed template.");
        }
        return new RestoredFamily(new EuObjectFactsPartitionRunRequest(plan, family.PlanResourceId, family.Set,
            family.Batch, source, budget), EuEnumerationRunResult.DeliveredWithCheckpoint(receipt, 0, family.Checkpoint));
    }

    private static FamilyCheckpoint DescribeFamily(EuObjectFactsPartitionRunRequest request, EuEnumerationRunResult run)
    {
        var receipt = run.Receipt ?? throw new CustodyIntegrityException("A production checkpoint needs a delivered family.");
        return new FamilyCheckpoint(run.CheckpointRef ?? throw new CustodyIntegrityException("Family checkpoint is absent."),
            receipt.Delivery.RunIdentity, receipt.Delivery.InterpretationProfileRef, request.PlanResourceId,
            request.Set, EuObjectFactsDiscoveryPlan.RequestedPartitionMembers(request.BatchObjects).ToArray(),
            request.RendererSource.Reference);
    }

    private async Task<SourceArtifactRef> RetainCheckpointAsync(ProductionCheckpoint document, CancellationToken cancellationToken)
    {
        var bytes = EncodeCheckpoint(document);
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Expression checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Expression checkpoint receipt names different bytes.");
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
    }

    private static byte[] EncodeCheckpoint(ProductionCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record ProductionCheckpoint(string Schema, FamilyCheckpoint Expression, FamilyCheckpoint? Objects,
        string DerivationSha256, string EpisodeSha256, string DerivationReceiptSha256, string EpisodeReceiptSha256);
    private sealed record FamilyCheckpoint(SourceArtifactRef Checkpoint, SourceArtifactRef Run, SourceArtifactRef Profile,
        string PlanResourceId, EuObjectFactsQuerySet Set, string[] Batch, SourceArtifactRef Renderer);
    private sealed record RestoredFamily(EuObjectFactsPartitionRunRequest Request, EuEnumerationRunResult Run);
}
