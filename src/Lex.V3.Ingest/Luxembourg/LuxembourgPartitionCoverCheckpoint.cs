using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Retains the actual split history and leaf observations of a producer's tiled cover.</summary>
/// <remarks>
/// A cover of one run is written as <c>lex-lu-partition-cover-checkpoint/1</c>, exactly as before. A cover that outlasted a
/// robots generation spans several runs, each a contiguous block of leaves; it is written as <c>/2</c>, which names those
/// blocks in order (<c>segments</c>: each run and how many leaves it proved), and every leaf is restored under its own
/// block's run. Readers take both.
/// </remarks>
public static class LuxembourgPartitionCoverCheckpoint
{
    private const string Schema = "lex-lu-partition-cover-checkpoint/1";

    internal static async Task<SourceArtifactRef> WriteAsync(ICustodyStore store, LuxembourgPartitionCover cover,
        IReadOnlyList<LuxembourgEnumerationRunResult> results, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(cover);
        ArgumentNullException.ThrowIfNull(results);
        if (cover.Basis != LuxembourgPartitionCoverBasis.LeafTilingOnly || results.Count != cover.LeafReceipts.Count)
            throw new ArgumentException("The producer checkpoint requires the complete tiled leaf results.");
        var checkpoints = new List<SourceArtifactRef>(results.Count);
        for (var i = 0; i < results.Count; i++)
        {
            if (!ReferenceEquals(results[i].Receipt, cover.LeafReceipts[i]) || results[i].CheckpointRef is not { } checkpoint)
                throw new CustodyIntegrityException("Cover checkpoint inputs are not the producer's own delivered leaves.");
            await ValidateRangeAsync(store, cover.LeafReceipts[i], cover.Chain.Leaves[i], cancellationToken).ConfigureAwait(false);
            checkpoints.Add(checkpoint);
        }
        var segments = new List<Segment>();
        foreach (var leafReceipt in cover.LeafReceipts)
        {
            if (segments.Count > 0 && segments[^1].Run == leafReceipt.Delivery.RunIdentity)
                segments[^1] = segments[^1] with { LeafCount = segments[^1].LeafCount + 1 };
            else
                segments.Add(new Segment(leafReceipt.Delivery.RunIdentity, 1));
        }

        var bytes = segments.Count == 1
            ? Encode(new Document(Schema, cover.Chain.RootRange, cover.Chain.SplitHistory, checkpoints,
                cover.RunIdentity, cover.InterpretationProfileRef))
            : Encoding.UTF8.GetBytes(ContractJson.Serialize(new SegmentedDocument(SegmentedDocument.SchemaId, cover.Chain.RootRange,
                cover.Chain.SplitHistory, checkpoints, segments, cover.InterpretationProfileRef)));
        var (receipt, failure) = await CustodyHold.TryHoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Cover checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Cover checkpoint receipt names different bytes.");
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
    }

    /// <summary>
    /// Replays the original splits, restores every leaf, checks its actual query bounds, and
    /// repeats the cover and leaf-proof gates. Coverage remains LeafTilingOnly; protection is current.
    /// </summary>
    public static async Task<LuxembourgPartitionCover> RestoreAsync(ICustodyStore store, SourceArtifactRef checkpoint,
        LuxembourgQueryPartitionRange expectedRoot, SourceArtifactRef expectedRun, SourceArtifactRef expectedProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedRoot);
        ArgumentNullException.ThrowIfNull(expectedRun);
        ArgumentNullException.ThrowIfNull(expectedProfile);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes.Span);
            string? schema;
            using (var framing = JsonDocument.Parse(bytes))
            {
                schema = framing.RootElement.ValueKind == JsonValueKind.Object &&
                    framing.RootElement.TryGetProperty("schema", out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : null;
            }

            Document document;
            IReadOnlyList<Segment> segments;
            if (schema == SegmentedDocument.SchemaId)
            {
                // A cover over several runs: its blocks, in order, each a run never seen before, the first the
                // caller's, covering every leaf.
                var segmented = ContractJson.Deserialize<SegmentedDocument>(text);
                if (segmented is null || segmented.Segments is null ||
                    !bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(segmented))) ||
                    segmented.Segments.Count < 2 || segmented.Segments.Any(static segment => segment is null || segment.Run is null || segment.LeafCount < 1) ||
                    segmented.Segments.Select(static segment => segment.Run).Distinct().Count() != segmented.Segments.Count ||
                    segmented.Segments[0].Run != expectedRun || segmented.Leaves is null ||
                    segmented.Segments.Sum(static segment => (long)segment.LeafCount) != segmented.Leaves.Count)
                    throw new CustodyIntegrityException("Cover checkpoint framing, run blocks or caller identity disagree.");
                document = new Document(Schema, segmented.Root, segmented.Splits, segmented.Leaves, expectedRun, segmented.Profile);
                segments = segmented.Segments;
            }
            else
            {
                document = ContractJson.Deserialize<Document>(text);
                if (document is null || document.Schema != Schema || !bytes.Span.SequenceEqual(Encode(document)))
                    throw new CustodyIntegrityException("Cover checkpoint framing or caller identity disagrees.");
                segments = document.Leaves is null ? [] : [new Segment(document.Run, document.Leaves.Count)];
            }

            if (document.Root != expectedRoot || document.Run != expectedRun || document.Profile != expectedProfile)
                throw new CustodyIntegrityException("Cover checkpoint framing or caller identity disagrees.");
            if (document.Splits is null || document.Leaves is null || document.Splits.Any(step => step is null) || document.Leaves.Any(leaf => leaf is null) ||
                document.Leaves.Distinct().Count() != document.Leaves.Count)
                throw new CustodyIntegrityException("Cover checkpoint contains absent or duplicate inputs.");
            var chain = LuxembourgPartitionChain.Root(document.Root);
            foreach (var step in document.Splits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                chain = chain.SplitLeaf(step.LeafPartitionId, step.Boundary, step.LeftPartitionId, step.RightPartitionId);
            }
            if (chain.Leaves.Count != document.Leaves.Count)
                throw new CustodyIntegrityException("Cover checkpoint does not name every replayed leaf.");
            var receipts = new List<RepeatedEnumerationDeliveryReceipt>(document.Leaves.Count);
            var block = 0;
            var leftInBlock = segments[0].LeafCount;
            for (var i = 0; i < document.Leaves.Count; i++)
            {
                if (leftInBlock == 0)
                {
                    block++;
                    leftInBlock = segments[block].LeafCount;
                }

                leftInBlock--;
                var receipt = await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(store, document.Leaves[i],
                    segments[block].Run, expectedProfile, cancellationToken).ConfigureAwait(false);
                await ValidateRangeAsync(store, receipt, chain.Leaves[i], cancellationToken).ConfigureAwait(false);
                if (receipt.TryProveFamilyEnumeration(chain.Leaves[i].PartitionId, out var refusal) is null)
                    throw new CustodyIntegrityException("Reopened cover leaf does not prove: " + refusal);
                receipts.Add(receipt);
            }
            return LuxembourgPartitionCover.TryCreate(chain, receipts, rootReceipt: null, out var coverRefusal)
                ?? throw new CustodyIntegrityException("Reopened cover does not reconcile: " + coverRefusal);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Cover checkpoint failed split-history verification.", exception);
        }
    }

    private static async Task ValidateRangeAsync(ICustodyStore store, RepeatedEnumerationDeliveryReceipt receipt,
        LuxembourgQueryPartitionRange leaf, CancellationToken cancellationToken)
    {
        if (receipt.Delivery.PartitionKey != leaf.PartitionId)
            throw new CustodyIntegrityException("Cover leaf identity differs from the retained query.");
        var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
        foreach (var count in new[] { receipt.Delivery.CountA, receipt.Delivery.CountB })
        {
            var evidence = await glue.ReopenPageEvidenceAsync(count, cancellationToken).ConfigureAwait(false);
            var parameters = evidence.QueryInput.OrderedParameters.ToDictionary(value => value.Name, StringComparer.Ordinal);
            Check("partition_start_", leaf.StartInclusive);
            Check("partition_end_", leaf.EndExclusive);
            void Check(string prefix, LuxembourgQueryCursor bound)
            {
                var keys = new[] { bound.Key1, bound.Key2, bound.Key3, bound.Key4, bound.Key5, bound.Key6 };
                for (var i = 0; i < keys.Length; i++)
                {
                    if (!parameters.TryGetValue(prefix + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), out var value) ||
                        value.Kind != MachineQueryParameterKind.PublisherCursor || value.TextValue is null ||
                        EnumerationCursorEnvelope.Decode(value.TextValue) != keys[i])
                        throw new CustodyIntegrityException("Cover leaf boundaries differ from the retained query.");
                }
            }
        }
    }

    private static byte[] Encode(Document document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record Document(string Schema, LuxembourgQueryPartitionRange Root,
        IReadOnlyList<LuxembourgPartitionSplitStep> Splits, IReadOnlyList<SourceArtifactRef> Leaves,
        SourceArtifactRef Run, SourceArtifactRef Profile);

    /// <summary>A cover over several runs (<c>/2</c>): the same fields, with the run blocks in leaf order in place of one run.</summary>
    private sealed record SegmentedDocument(string Schema, LuxembourgQueryPartitionRange Root,
        IReadOnlyList<LuxembourgPartitionSplitStep> Splits, IReadOnlyList<SourceArtifactRef> Leaves,
        IReadOnlyList<Segment> Segments, SourceArtifactRef Profile)
    {
        // Here rather than beside the /1 schema: a second constant on the static class would make it a vocabulary
        // registry the census pins.
        internal const string SchemaId = "lex-lu-partition-cover-checkpoint/2";
    }

    /// <summary>One block of a cover's leaves: the run that proved them and how many there are.</summary>
    private sealed record Segment(SourceArtifactRef Run, int LeafCount);
}
