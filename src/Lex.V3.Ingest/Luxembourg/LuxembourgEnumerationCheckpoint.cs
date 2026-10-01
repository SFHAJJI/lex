using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Retains LU enumeration inputs and independently regenerates their template requests offline.</summary>
public static class LuxembourgEnumerationCheckpoint
{
    private const string Schema = "lex-lu-enumeration-checkpoint/1";

    public static async Task<SourceArtifactRef> WriteAsync(ICustodyStore store,
        RepeatedEnumerationDeliveryReceipt receipt, LuxembourgPartitionRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(request);
        var delivery = receipt.Delivery;
        var planRef = LuxembourgQueryPlanIdentity.Create(request.InvariantPlanResourceId, request.InvariantPlan);
        var profile = request.InvariantPlan.CreateDeliveryProfile(request.InvariantPlanResourceId, request.SetId);
        RepeatedEnumerationInterpretationProfileIdentity.Validate(delivery.InterpretationProfileRef, profile);
        if (delivery.PartitionKey != request.Partition.PartitionId)
            throw new ArgumentException("The request and receipt name different partitions.", nameof(request));
        var planWireSha256 = await HoldAsync(store, LuxembourgQueryPlan.GetWireBytes(request.InvariantPlan),
            cancellationToken).ConfigureAwait(false);
        var document = new Document(Schema, planRef, planWireSha256, request.SetId, request.RendererSource.Reference,
            delivery.InterpretationProfileRef, delivery.RunIdentity, delivery.SourceProfileRef, delivery.PartitionKey,
            delivery.CountA, delivery.PagesA, delivery.CountB, delivery.PagesB);
        var digest = await HoldAsync(store, Encode(document), cancellationToken).ConfigureAwait(false);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", digest);
    }

    /// <summary>
    /// Rebinds the closed LU template using original artifact identities and retained parameters,
    /// repeats the complete comparison, and derives the receipt floor from current custody holds.
    /// No publisher transport or newly minted observation is involved.
    /// </summary>
    public static async Task<RepeatedEnumerationDeliveryReceipt> RestoreReceiptAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, SourceArtifactRef expectedRun, SourceArtifactRef expectedProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedRun);
        ArgumentNullException.ThrowIfNull(expectedProfile);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<Document>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != Schema || !bytes.Span.SequenceEqual(Encode(document)) ||
                document.Run != expectedRun || document.ProfileRef != expectedProfile)
                throw new CustodyIntegrityException("LU checkpoint framing or expected identity disagrees.");
            if (document.PagesA.Pages.Any(p => p is null || p.Evidence is null) ||
                document.PagesB.Pages.Any(p => p is null || p.Evidence is null))
                throw new CustodyIntegrityException("LU checkpoint contains an empty page reference.");
            var wireBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.PlanWireSha256, cancellationToken)
                .ConfigureAwait(false);
            var plan = LuxembourgQueryPlan.ParseAndVerify(document.PlanRef, wireBytes.Span);
            _ = await CustodyRestore.ReadByDigestCheckedAsync(store, document.PlanRef.Sha256, cancellationToken)
                .ConfigureAwait(false);
            var sourceBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.RendererSourceRef.Sha256,
                cancellationToken).ConfigureAwait(false);
            var source = MachineQueryRendererSource.Open(document.RendererSourceRef, sourceBytes.Span);
            var profile = plan.CreateDeliveryProfile(document.PlanRef.ResourceId, document.SetId);
            RepeatedEnumerationInterpretationProfileIdentity.Validate(document.ProfileRef, profile);
            var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
            var resolved = new Dictionary<RepeatedEnumerationEvidenceRefs, RepeatedEnumerationResolvedEvidence>();
            var references = new[] { document.CountA }.Concat(document.PagesA.Pages.Select(p => p.Evidence))
                .Append(document.CountB).Concat(document.PagesB.Pages.Select(p => p.Evidence));
            foreach (var reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (resolved.ContainsKey(reference)) throw new CustodyIntegrityException("LU checkpoint repeats an observation.");
                var evidence = await glue.ReopenPageEvidenceAsync(reference, cancellationToken).ConfigureAwait(false);
                var parameters = evidence.QueryInput.OrderedParameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
                long Integer(string name) => parameters.TryGetValue(name, out var value) &&
                    value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is { } integer
                    ? integer : throw new ArgumentException("Missing LU integer parameter: " + name);
                string Key(string name) => parameters.TryGetValue(name, out var value) &&
                    value.Kind == MachineQueryParameterKind.PublisherCursor && value.TextValue is { } cursor
                    ? EnumerationCursorEnvelope.Decode(cursor) : throw new ArgumentException("Missing LU cursor parameter: " + name);
                LuxembourgQueryCursor Cursor(string prefix) => new(Key(prefix + "1"), Key(prefix + "2"),
                    Key(prefix + "3"), Key(prefix + "4"), Key(prefix + "5"), Key(prefix + "6"));
                var partition = new LuxembourgQueryPartitionRange(evidence.QueryInput.PartitionBinding.MemberKey,
                    Cursor("partition_start_"), Cursor("partition_end_"));
                var pass = (LuxembourgQueryPass)checked((int)Integer("pass_id"));
                var isCount = reference == document.CountA || reference == document.CountB;
                MachineQueryPlan reboundPlan;
                MachineQueryInputArtifact reboundInput;
                BoundMachineRequest reboundRequest;
                if (isCount)
                {
                    var rebound = plan.BindCount(document.PlanRef.ResourceId, reference.QueryPlanRef.ResourceId,
                        reference.QueryInputRef.ResourceId, document.SetId, pass, partition, source);
                    reboundPlan = rebound.MachinePlan;
                    reboundInput = rebound.InputArtifact;
                    reboundRequest = rebound.Request;
                }
                else
                {
                    var hasCursor = Integer("has_cursor");
                    if (hasCursor is not (0 or 1)) throw new ArgumentException("Invalid LU cursor presence parameter.");
                    var cardinality = evidence.QueryPlan.ResponseCardinality;
                    var rebound = plan.BindPage(document.PlanRef.ResourceId, reference.QueryPlanRef.ResourceId,
                        reference.QueryInputRef.ResourceId, document.SetId, pass, partition,
                        hasCursor == 0 ? null : Cursor("last_key_"),
                        cardinality.ExpectedPartitionRowCount ?? throw new ArgumentException("LU page count is absent."),
                        cardinality.ExpectedPartitionRowCountEvidenceRef ?? throw new ArgumentException("LU page count evidence is absent."), source);
                    reboundPlan = rebound.MachinePlan;
                    reboundInput = rebound.InputArtifact;
                    reboundRequest = rebound.Request;
                }
                MachineQueryPlanIdentity.Validate(reference.QueryPlanRef, reboundPlan);
                if (reboundInput.ArtifactRef != reference.QueryInputRef)
                    throw new CustodyIntegrityException("LU closed-template input differs from retained input.");
                var opened = MachineQueryBinder.OpenForSend(reboundRequest);
                var requestBody = opened.CopyRequestBody();
                var retainedRequestBody = await CustodyRestore.ReadByDigestCheckedAsync(store,
                    evidence.LogicalRequest.Body.Sha256, cancellationToken).ConfigureAwait(false);
                if (!retainedRequestBody.Span.SequenceEqual(requestBody))
                    throw new CustodyIntegrityException("LU template output differs from the retained request bytes.");
                resolved.Add(reference, evidence with
                {
                    Renderer = new TemplateRenderer(reboundPlan.RendererProfileRef, source.Reference,
                        opened.RequestedUri, requestBody),
                });
            }
            var comparison = EnumerationDeliveryComparison.Create(profile, document.ProfileRef, document.CountA,
                document.PagesA, document.CountB, document.PagesB, new Resolver(resolved));
            if (comparison.RunIdentity != expectedRun || comparison.SourceProfileRef != document.SourceProfile ||
                comparison.PartitionKey != document.PartitionKey)
                throw new CustodyIntegrityException("LU comparison identity differs from its checkpoint.");
            return await RepeatedEnumerationReceiptRestore.RestoreAsync(store, comparison, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("LU checkpoint failed independent template verification.", exception);
        }
    }

    private static byte[] Encode(Document document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));

    private static async Task<string> HoldAsync(ICustodyStore store, byte[] bytes, CancellationToken cancellationToken)
    {
        var (receipt, failure) = await CustodyHold.TryHoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("LU checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("LU checkpoint receipt names different bytes.");
        return receipt.Reference.ContentSha256;
    }

    private sealed record Document(string Schema, SourceArtifactRef PlanRef, string PlanWireSha256, string SetId,
        SourceArtifactRef RendererSourceRef, SourceArtifactRef ProfileRef, SourceArtifactRef Run,
        SourceArtifactRef SourceProfile, string PartitionKey, RepeatedEnumerationEvidenceRefs CountA,
        EnumerationPageSetRefs PagesA, RepeatedEnumerationEvidenceRefs CountB, EnumerationPageSetRefs PagesB);

    private sealed class Resolver(IReadOnlyDictionary<RepeatedEnumerationEvidenceRefs, RepeatedEnumerationResolvedEvidence> evidence)
        : IRepeatedEnumerationEvidenceResolver
    {
        public RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs references) => evidence[references];
    }

    // These bytes come from a fresh closed-template bind above, never from the retained request body.
    private sealed class TemplateRenderer(SourceArtifactRef profile, SourceArtifactRef source, string uri, byte[] body)
        : IMachineQueryRenderer
    {
        public SourceArtifactRef RendererProfileRef => profile;
        public SourceArtifactRef RendererSourceRef => source;
        public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) => new(uri, body);
    }
}
