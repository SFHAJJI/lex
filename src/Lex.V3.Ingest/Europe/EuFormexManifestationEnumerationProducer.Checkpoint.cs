using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFormexManifestationEnumerationProducer
{
    private const string CheckpointSchema = "lex-eu-formex-enumeration-checkpoint/1";

    /// <summary>Reopens the original enumeration for the caller's checked expression without sending requests.</summary>
    public static async Task<EuFormexManifestationEnumerationResult> ReopenAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, LanguageScopedExpression expression, SourceArtifactRef expectedRun,
        SourceArtifactRef expectedProfile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(expectedRun);
        ArgumentNullException.ThrowIfNull(expectedProfile);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<Checkpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != CheckpointSchema || !bytes.Span.SequenceEqual(EncodeCheckpoint(document)) ||
                document.Run != expectedRun || document.Profile != expectedProfile ||
                document.ExpressionSha256 != expression.CanonicalContentSha256)
                throw new CustodyIntegrityException("Formex enumeration checkpoint framing or caller binding disagrees.");
            var receipt = await EuEnumerationCheckpoint.RestoreReceiptAsync(store, document.Enumeration,
                expectedRun, expectedProfile, cancellationToken).ConfigureAwait(false);
            var plan = EuFormexManifestationDiscoveryPlan.Create();
            RepeatedEnumerationInterpretationProfileIdentity.Validate(expectedProfile, plan.CreateDeliveryProfile());
            var sourceBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.Renderer.Sha256, cancellationToken).ConfigureAwait(false);
            var source = MachineQueryRendererSource.Open(document.Renderer, sourceBytes.Span);
            var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
            foreach (var count in new[] { receipt.Delivery.CountA, receipt.Delivery.CountB })
            {
                if (count.QueryPlanRef.ResourceId != document.PlanResourceId)
                    throw new CustodyIntegrityException("Formex count plan identity disagrees.");
                var evidence = await glue.ReopenPageEvidenceAsync(count, cancellationToken).ConfigureAwait(false);
                var parameters = evidence.QueryInput.OrderedParameters.Where(value => value.Name == "pass_id").ToArray();
                if (parameters.Length != 1 || parameters[0].Kind != MachineQueryParameterKind.BoundedInteger ||
                    parameters[0].IntegerValue is not { } pass || pass is not (1 or 2))
                    throw new CustodyIntegrityException("Formex count needs one bounded pass value in 1..2.");
                var rebound = plan.BindCount(expression.Identity, (EuFormexManifestationQueryPass)checked((int)pass),
                    count.QueryPlanRef.ResourceId, count.QueryInputRef.ResourceId, source);
                MachineQueryPlanIdentity.Validate(count.QueryPlanRef, rebound.MachinePlan);
                if (rebound.InputArtifact.ArtifactRef != count.QueryInputRef)
                    throw new CustodyIntegrityException("Formex expression differs from the original query.");
                var held = await CustodyRestore.ReadByDigestCheckedAsync(store, evidence.LogicalRequest.Body.Sha256, cancellationToken).ConfigureAwait(false);
                if (!held.Span.SequenceEqual(MachineQueryBinder.OpenForSend(rebound.Request).CopyRequestBody()))
                    throw new CustodyIntegrityException("Formex count request differs from its closed template.");
            }
            var budget = WireRequestBudget.OfWireRequests(2);
            var result = await DeriveAsync(glue, receipt, plan, expression, WireBudgetSnapshot.Of(budget), 0, cancellationToken).ConfigureAwait(false);
            if (!result.Delivered || TypesDigest(result) != document.TypesSha256)
                throw new CustodyIntegrityException("Reopened Formex enumeration differs from the retained result: " + result.Detail);
            return result.WithCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Formex enumeration checkpoint failed independent verification.", exception);
        }
    }

    private async Task<SourceArtifactRef> RetainCheckpointAsync(EuFormexManifestationRunRequest request,
        EuEnumerationRunResult run, EuFormexManifestationEnumerationResult result, CancellationToken cancellationToken)
    {
        var delivery = run.Receipt!.Delivery;
        var document = new Checkpoint(CheckpointSchema,
            run.CheckpointRef ?? throw new CustodyIntegrityException("Formex enumeration checkpoint is absent."),
            delivery.RunIdentity, delivery.InterpretationProfileRef, request.PlanResourceId,
            request.RendererSource.Reference, request.Expression.CanonicalContentSha256, TypesDigest(result));
        var bytes = EncodeCheckpoint(document);
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Formex checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Formex checkpoint receipt names different bytes.");
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
    }

    private static string TypesDigest(EuFormexManifestationEnumerationResult result) =>
        CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(result.ManifestationTypes)));
    private static byte[] EncodeCheckpoint(Checkpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record Checkpoint(string Schema, SourceArtifactRef Enumeration, SourceArtifactRef Run,
        SourceArtifactRef Profile, string PlanResourceId, SourceArtifactRef Renderer, string ExpressionSha256, string TypesSha256);
}
