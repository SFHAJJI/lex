using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Custody.Probe;

/// <summary>
/// Reopens an explicitly pinned input population. This is byte/provenance verification, not
/// admission, publisher authentication, completeness, or a current retention attestation.
/// </summary>
internal static class FactCustodyReplay
{
    private const int MaximumDocuments = 1_000;
    private const int MaximumObservations = 10_000;
    private const int MaximumMetadataBytes = 8 * 1024 * 1024;
    private const long MaximumReplayBytes = 512L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static async Task<string> RunAsync(
        ICustodyStore store, string inputSha256, CancellationToken cancellationToken)
    {
        long reopenedBytes = 0;
        var input = Parse<ReplayInput>(await ReadMetadata(inputSha256).ConfigureAwait(false));
        if (input.Schema != "lex-v3-custody-replay-input/1")
            throw new CustodyIntegrityException("Unknown replay input schema.");
        ValidatePopulation(input.Facts, requireNonempty: true);
        ValidatePopulation(input.Routes, requireNonempty: true);

        var observations = new Dictionary<string, ReplayedObservation>(StringComparer.Ordinal);
        foreach (var routeDigest in input.Routes)
        {
            var bytes = await ReadMetadata(routeDigest).ConfigureAwait(false);
            RoutedHttpEvidence route;
            try { route = RoutedHttpEvidence.ParseAndVerify(bytes.Span); }
            catch (ArgumentException exception)
            {
                throw new CustodyIntegrityException("Replay route is not canonical HTTP evidence.", exception);
            }
            foreach (var hop in route.Hops)
            {
                if (observations.Count >= MaximumObservations || observations.ContainsKey(hop.ObservationId))
                    throw new CustodyIntegrityException("Replay observation identity is ambiguous or exceeds its bound.");
                var receipt = Parse<DurableBlobWriteReceipt>(
                    await ReadMetadata(hop.DurableWriteReceiptSha256).ConfigureAwait(false));
                if (DurableBlobWriteReceiptDigest.Of(receipt) != hop.DurableWriteReceiptSha256
                    || receipt.Reference.ContentSha256 != hop.Sha256
                    || checked((ulong)receipt.Reference.ByteLength) != hop.Length)
                    throw new CustodyIntegrityException("Replay receipt does not bind the observation's exact bytes.");
                Charge(receipt.Reference.ByteLength);
                _ = await CustodyRestore.ReadCheckedAsync(store, receipt.Reference, cancellationToken)
                    .ConfigureAwait(false);
                observations.Add(hop.ObservationId, new ReplayedObservation(
                    hop.ObservationId, routeDigest, hop.DurableWriteReceiptSha256,
                    hop.Status, hop.StatusDisposition, hop.Completion is not IncompleteHttpCompletion,
                    route.Outcome is CompleteHttpRouteOutcome
                        && hop.StatusDisposition == HttpStatusDisposition.DerivableStatus
                        && hop.Completion is not IncompleteHttpCompletion,
                    receipt));
            }
        }

        var facts = new List<ReplayedFact>();
        foreach (var factDigest in input.Facts)
        {
            var bytes = await ReadMetadata(factDigest).ConfigureAwait(false);
            var provenance = new List<Provenance>();
            var scopes = new List<string>();
            var schema = ReadFact(bytes, provenance, scopes);
            if (provenance.Count > MaximumObservations || scopes.Count > MaximumDocuments)
                throw new CustodyIntegrityException("Replay Fact provenance exceeds its bound.");
            foreach (var link in provenance)
            {
                if (!observations.TryGetValue(link.ObservationId, out var observation))
                    throw new CustodyIntegrityException("A Fact provenance member has no unique retained observation.");
                if (!observation.DerivableTransport)
                    throw new CustodyIntegrityException("A Fact provenance member names non-derivable transport evidence.");
            }
            foreach (var scope in scopes)
            {
                _ = await ReadMetadata(scope).ConfigureAwait(false);
            }
            facts.Add(new ReplayedFact(factDigest, schema, provenance, scopes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ContractJson.Serialize(new
        {
            Schema = "lex-v3-custody-replay-result/1",
            InputSha256 = inputSha256,
            AcceptanceEstablished = false,
            PopulationCompletenessEstablished = false,
            CurrentRetentionEstablished = false,
            Facts = facts,
            Observations = observations.Values.ToArray(),
            ReopenedBytes = reopenedBytes,
        });

        async Task<ReadOnlyMemory<byte>> ReadMetadata(string digest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CustodyDigest.IsLowercaseSha256(digest))
                throw new CustodyIntegrityException("Replay requires lowercase SHA-256 content addresses.");
            // The store API returns whole objects (bounded by CustodyBounds); this tighter bound
            // limits decoding and cumulative replay work, not the provider's first allocation.
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, digest, cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length > MaximumMetadataBytes)
                throw new CustodyIntegrityException("Replay metadata exceeds its decoding bound.");
            Charge(bytes.Length);
            return bytes;
        }

        void Charge(long length)
        {
            reopenedBytes = checked(reopenedBytes + length);
            if (reopenedBytes > MaximumReplayBytes)
                throw new CustodyIntegrityException("Replay exceeds its cumulative byte bound.");
        }
    }

    private static void ValidatePopulation(string[] digests, bool requireNonempty)
    {
        if (digests is null || (requireNonempty && digests.Length == 0)
            || digests.Length > MaximumDocuments
            || digests.Any(static digest => !CustodyDigest.IsLowercaseSha256(digest))
            || digests.Distinct(StringComparer.Ordinal).Count() != digests.Length)
            throw new CustodyIntegrityException("Replay population must be bounded, distinct content addresses.");
    }

    private static T Parse<T>(ReadOnlyMemory<byte> bytes)
    {
        try { return ContractJson.Deserialize<T>(StrictUtf8.GetString(bytes.Span)); }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Replay input violates its typed contract.", exception);
        }
    }

    private static string ReadFact(ReadOnlyMemory<byte> bytes, List<Provenance> provenance, List<string> scopes)
    {
        string schema;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("schema", out var property)
                || property.ValueKind != JsonValueKind.String)
                throw new CustodyIntegrityException("Replay requires a typed Fact document.");
            schema = property.GetString()!;
        }
        catch (JsonException exception)
        {
            throw new CustodyIntegrityException("Replay Fact is not JSON.", exception);
        }
        switch (schema)
        {
            case FactsSchemaIds.PublisherDateFact:
                provenance.Add(new("source_observation_id", Parse<PublisherDateFact>(bytes).SourceObservationId));
                break;
            case FactsSchemaIds.PublisherRelation:
                AddPublisher(Parse<PublisherRelation>(bytes), "");
                break;
            case FactsSchemaIds.DerivedInverseRelation:
                AddInverse(Parse<DerivedInverseRelation>(bytes), "");
                break;
            case FactsSchemaIds.LocalInboundView:
                AddInbound(Parse<LocalInboundView>(bytes), "");
                break;
            case FactsSchemaIds.RelationFact:
                var fact = Parse<RelationFact>(bytes);
                if (fact.PublisherAsserted is { } publisher) AddPublisher(publisher, "publisher_asserted.");
                if (fact.OntologyAuthorizedInverse is { } inverse) AddInverse(inverse, "ontology_authorized_inverse.");
                if (fact.LocalInboundView is { } inbound) AddInbound(inbound, "local_inbound_view.");
                break;
            default:
                // PublisherDate alone lacks provenance; drift is diagnostic evidence, not a Fact.
                throw new CustodyIntegrityException("Replay does not recognize this as a provenance-bearing Fact contract.");
        }
        return schema;

        void AddPublisher(PublisherRelation relation, string path) =>
            provenance.Add(new(path + "source_observation_id", relation.SourceObservationId));
        void AddInverse(DerivedInverseRelation inverse, string path)
        {
            AddPublisher(inverse.DerivedFrom, path + "derived_from.");
            provenance.Add(new(path + "authorizing_axiom.source_observation_id", inverse.AuthorizingAxiom.SourceObservationId));
        }
        void AddInbound(LocalInboundView inbound, string path)
        {
            for (var index = 0; index < inbound.ContributingAssertions.Count; index++)
                AddPublisher(inbound.ContributingAssertions[index], path + $"contributing_assertions[{index}].");
            scopes.Add(inbound.ScopeDescriptorSha256);
        }
    }

    private sealed record ReplayInput(string Schema, string[] Facts, string[] Routes);
    private sealed record Provenance(string Member, string ObservationId);
    private sealed record ReplayedFact(string Sha256, string Schema, List<Provenance> Provenance, List<string> ScopeDigests);
    private sealed record ReplayedObservation(string ObservationId, string RouteSha256, string WriteReceiptSha256,
        int Status, HttpStatusDisposition StatusDisposition, bool CompleteBody, bool DerivableTransport,
        DurableBlobWriteReceipt RecordedWriteReceipt);
}
