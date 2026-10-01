using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

public sealed partial class LuxembourgFirstMountAcquisition
{
    private const string VocabularyCheckpointSchema = "lex-lu-vocabulary-checkpoint/1";

    /// <summary>Reopens the original observed vocabulary through all four enumeration proofs and the source-profile gate.</summary>
    public static async Task<VerifiedLuxembourgSourceProfile> ReopenVocabularyAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, SourceArtifactRef expectedObservation, MachineQueryRendererSource queryRenderer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedObservation);
        ArgumentNullException.ThrowIfNull(queryRenderer);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<VocabularyCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Schema != VocabularyCheckpointSchema || !bytes.Span.SequenceEqual(EncodeVocabulary(document)) ||
                document.Observation != expectedObservation || document.Renderer != queryRenderer.Reference ||
                document.Families is null || document.Families.Length != VocabularyFamilies.Length ||
                document.Families.Any(static family => family is null || family.Checkpoint is null || family.Run is null ||
                    family.Profile is null || family.ProductRequestCount < 0) ||
                !document.Families.Select(static family => family.Family).SequenceEqual(VocabularyFamilies))
                throw new CustodyIntegrityException("Vocabulary checkpoint framing or original caller binding disagrees.");
            var planBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.PlanWireSha256, cancellationToken).ConfigureAwait(false);
            var plan = LuxembourgQueryPlan.ParseAndVerify(document.Plan, planBytes.Span);
            var observed = new List<ObservedVocabulary>();
            foreach (var family in document.Families)
            {
                var receipt = await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(store, family.Checkpoint,
                    family.Run, family.Profile, cancellationToken).ConfigureAwait(false);
                observed.AddRange(await ReopenVocabularyRowsAsync(store, plan, document.Plan.ResourceId, family.Family,
                    queryRenderer, receipt, cancellationToken).ConfigureAwait(false));
            }
            var vocabulary = observed.Where(static value => value.Kind is not null)
                .Select(static value => new LuxembourgIriVocabularyValue(value.Kind!.Value, value.Iri)).Distinct().ToArray();
            var missing = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary.Except(vocabulary).ToArray();
            var reproduced = VocabularyObservationBytes(document.Families.Select(static family =>
                new VocabularyMeasurement(family.Family, family.ProductRequestCount)).ToArray(), observed, missing);
            var original = await CustodyRestore.ReadByDigestCheckedAsync(store, expectedObservation.Sha256, cancellationToken).ConfigureAwait(false);
            if (!original.Span.SequenceEqual(reproduced))
                throw new CustodyIntegrityException("Vocabulary source rows do not reproduce the original observation bytes.");
            var profile = VerifiedLuxembourgSourceProfile.TryOpen(new LuxembourgVocabularySnapshot(
                expectedObservation, expectedObservation, vocabulary, []), out var refusal);
            return profile ?? throw new CustodyIntegrityException("Restored vocabulary refuses the source profile: " + refusal?.Code);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Vocabulary checkpoint failed independent verification.", exception);
        }
    }

    private async Task<SourceArtifactRef> RetainVocabularyCheckpointAsync(LuxembourgQueryPlan plan, string planId,
        MachineQueryRendererSource renderer, SourceArtifactRef observation, IReadOnlyList<VocabularyFamilyCheckpoint> families,
        CancellationToken cancellationToken)
    {
        var wireDigest = CustodyDigest.Of(LuxembourgQueryPlan.GetWireBytes(plan));
        _ = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, wireDigest, cancellationToken).ConfigureAwait(false);
        var bytes = EncodeVocabulary(new VocabularyCheckpoint(VocabularyCheckpointSchema,
            LuxembourgQueryPlanIdentity.Create(planId, plan), wireDigest, renderer.Reference, observation, families.ToArray()));
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Vocabulary checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Vocabulary checkpoint receipt names different bytes.");
        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }

    private static async Task<IReadOnlyList<ObservedVocabulary>> ReopenVocabularyRowsAsync(ICustodyStore store,
        LuxembourgQueryPlan plan, string planId, string family, MachineQueryRendererSource renderer,
        RepeatedEnumerationDeliveryReceipt receipt, CancellationToken cancellationToken)
    {
        var range = family == "O" ? Range("vocabulary-o", CreativeCommonsRangeStart, CreativeCommonsRangeEnd)
            : Range("vocabulary-" + family.ToLowerInvariant(), string.Empty, EndOfKeySpace);
        if (receipt.Delivery.PartitionKey != range.PartitionId)
            throw new CustodyIntegrityException("Vocabulary enumeration partition differs from the required range.");
        var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
        var counts = new[] { receipt.Delivery.CountA, receipt.Delivery.CountB };
        for (var index = 0; index < counts.Length; index++)
        {
            var count = counts[index];
            var rebound = plan.BindCount(planId, count.QueryPlanRef.ResourceId, count.QueryInputRef.ResourceId,
                family, index == 0 ? LuxembourgQueryPass.Pass1 : LuxembourgQueryPass.Pass2, range, renderer);
            MachineQueryPlanIdentity.Validate(count.QueryPlanRef, rebound.MachinePlan);
            if (rebound.InputArtifact.ArtifactRef != count.QueryInputRef)
                throw new CustodyIntegrityException("Vocabulary count differs from its required family, range or pass.");
        }
        var proof = AbsenceFamilyEnumerationProof.TryCreate(range.PartitionId, receipt.Delivery, receipt.RetainedFloor, out var proofRefusal)
            ?? throw new CustodyIntegrityException("Vocabulary enumeration proof refused: " + proofRefusal);
        var interpretation = plan.CreateDeliveryProfile(planId, family);
        var pages = new List<RepeatedEnumerationResolvedEvidence>();
        foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static page => page.Ordinal))
            pages.Add(await glue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken).ConfigureAwait(false));
        var rows = VerifiedRepeatedEnumerationRows.TryOpen(proof, receipt.Delivery, interpretation,
            receipt.Delivery.InterpretationProfileRef, receipt.Delivery.CountA.HttpEvidenceRef, pages, out var rowRefusal)
            ?? throw new CustodyIntegrityException("Vocabulary reopened rows refused: " + rowRefusal);
        var keyOrdinal = interpretation.ProjectionVariables.ToList().IndexOf("key_1");
        var observed = new List<ObservedVocabulary>();
        foreach (var row in rows)
        {
            var value = row.Terms[keyOrdinal].Value ?? throw new CustodyIntegrityException("Vocabulary row carries no key_1.");
            observed.AddRange(ClassifyVocabulary(family, value));
        }
        return observed;
    }

    private static byte[] VocabularyObservationBytes(IEnumerable<VocabularyMeasurement> measured,
        IEnumerable<ObservedVocabulary> observed, IEnumerable<LuxembourgIriVocabularyValue> missing) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "lex-lu-vocabulary-observation/1",
            measured = measured.Select(static value => new { family = value.Family, value.ProductRequestCount, refusal = (string?)null }),
            observed = observed.Select(static value => new { value.Family, kind = value.Kind?.ToString(), value.Iri, value.Disposition }),
            missing = missing.Select(static value => new { kind = value.Kind.ToString(), value.FullIri }),
            limitation = "Observed publisher values only; required values are expectations, never a source of observed values.",
        }, EvidenceJson);
    private static byte[] EncodeVocabulary(VocabularyCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record VocabularyMeasurement(string Family, int ProductRequestCount);
    private sealed record VocabularyFamilyCheckpoint(string Family, SourceArtifactRef Checkpoint, SourceArtifactRef Run,
        SourceArtifactRef Profile, int ProductRequestCount);
    private sealed record VocabularyCheckpoint(string Schema, SourceArtifactRef Plan, string PlanWireSha256,
        SourceArtifactRef Renderer, SourceArtifactRef Observation, VocabularyFamilyCheckpoint[] Families);
}
