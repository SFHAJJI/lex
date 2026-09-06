using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Custody.Probe;
using Lex.V3.Tests.Facts;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class FactCustodyReplayTests
{
    private const string Observation = "urn:uuid:11111111-1111-4111-8111-111111111111";
    private const string OntologyObservation = "urn:uuid:22222222-2222-4222-8222-222222222222";

    [TestMethod]
    public async Task ReplayReopensPinnedFactsAndExactTransportBytesWithoutAcceptanceClaims()
    {
        var fixture = new Fixture();
        var route = fixture.Route(Observation);
        var fact = fixture.Hold(Encoding.UTF8.GetBytes(
            ContractJson.Serialize(FactsFixtures.DateFact()).Replace(FactsFixtures.ObservationId, Observation, StringComparison.Ordinal)));
        var output = await fixture.Run([fact], [route]);
        using var result = JsonDocument.Parse(output);
        Assert.AreEqual(1, result.RootElement.GetProperty("facts").GetArrayLength());
        Assert.AreEqual(1, result.RootElement.GetProperty("observations").GetArrayLength());
        Assert.IsFalse(result.RootElement.GetProperty("acceptance_established").GetBoolean());
        Assert.IsFalse(result.RootElement.GetProperty("population_completeness_established").GetBoolean());
        Assert.AreEqual(1, fixture.Store.BodyReads);
    }

    [TestMethod]
    [DataRow("missing_observation")]
    [DataRow("duplicate_observation")]
    [DataRow("missing_body")]
    [DataRow("corrupt_body")]
    [DataRow("substituted_receipt")]
    [DataRow("rejected_observation")]
    [DataRow("partial_observation")]
    [DataRow("empty_population")]
    public async Task ReplayRefusesBrokenPopulationLinks(string defect)
    {
        var fixture = new Fixture();
        var route = fixture.Route(Observation, status: defect == "rejected_observation" ? 403 : 200,
            partial: defect == "partial_observation");
        var fact = fixture.Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(
            FactsFixtures.PublisherRelation(sourceObservationId: defect == "missing_observation" ? OntologyObservation : Observation))));
        var routes = new List<string> { route };
        if (defect == "duplicate_observation")
        {
            routes.Add(fixture.Route(Observation, requestOrdinal: 8));
        }
        if (defect == "missing_body") fixture.Store.Objects.Remove(fixture.BodyDigest);
        if (defect == "corrupt_body") fixture.Store.Objects[fixture.BodyDigest] = [9, 8, 7];
        if (defect == "substituted_receipt") fixture.Store.Objects[fixture.ReceiptDigest] = Encoding.UTF8.GetBytes("{}");
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            fixture.Run(defect == "empty_population" ? [] : [fact], routes.ToArray()));
    }

    [TestMethod]
    [DataRow("canonical")]
    [DataRow("digest")]
    [DataRow("length")]
    public async Task IndependentlyHeldReceiptMustAgreeWithHop(string defect)
    {
        var fixture = new Fixture();
        var route = fixture.Route(Observation, receiptDefect: defect);
        var fact = fixture.Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(
            FactsFixtures.PublisherRelation(sourceObservationId: Observation))));
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => fixture.Run([fact], [route]));
        Assert.AreEqual(0, fixture.Store.BodyReads);
    }

    [TestMethod]
    public async Task UnreferencedRejectedAndPartialObservationsAreReopenedWithoutDerivation()
    {
        var fixture = new Fixture();
        var good = fixture.Route(Observation);
        var rejected = fixture.Route(OntologyObservation, requestOrdinal: 8, status: 403);
        var partial = fixture.Route("urn:uuid:44444444-4444-4444-8444-444444444444", requestOrdinal: 9, partial: true);
        var fact = fixture.Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(
            FactsFixtures.PublisherRelation(sourceObservationId: Observation))));
        using var result = JsonDocument.Parse(await fixture.Run([fact], [good, rejected, partial]));
        var observations = result.RootElement.GetProperty("observations");
        Assert.AreEqual(3, fixture.Store.BodyReads);
        Assert.IsFalse(observations[1].GetProperty("derivable_transport").GetBoolean());
        Assert.IsFalse(observations[2].GetProperty("derivable_transport").GetBoolean());
        Assert.IsFalse(observations[2].GetProperty("complete_body").GetBoolean());
        Assert.IsFalse(result.RootElement.GetProperty("current_retention_established").GetBoolean());
    }

    [TestMethod]
    [DataRow("unknown_schema")]
    [DataRow("duplicate_property")]
    [DataRow("extra_property")]
    [DataRow("bad_utf8")]
    public async Task FactDocumentsUseStrictExistingReaders(string defect)
    {
        var fixture = new Fixture();
        var route = fixture.Route(Observation);
        var json = ContractJson.Serialize(FactsFixtures.PublisherRelation(sourceObservationId: Observation));
        json = defect switch
        {
            "unknown_schema" => json.Replace(FactsSchemaIds.PublisherRelation, "invented/1", StringComparison.Ordinal),
            "duplicate_property" => json.Insert(1, "\"source_observation_id\":\"ignored\","),
            "extra_property" => json.Insert(1, "\"unaccepted\":true,"),
            _ => json,
        };
        var fact = fixture.Hold(defect == "bad_utf8" ? [0xff, 0xfe] : Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => fixture.Run([fact], [route]));
    }

    [TestMethod]
    public async Task InverseRequiresBothForwardAndOntologyObservations()
    {
        var fixture = new Fixture();
        var forward = FactsFixtures.PublisherRelation(sourceObservationId: Observation);
        var inverse = new DerivedInverseRelation(FactsSchemaIds.DerivedInverseRelation,
            forward.Target, forward.Source, FactsFixtures.ConsolidatedByPredicate,
            forward.PredicateUri, new ObservedInverseAxiom(FactsFixtures.JoluxOntology,
                FactsFixtures.OntologyVersion, forward.PredicateUri, FactsFixtures.ConsolidatedByPredicate,
                OntologyObservation), forward);
        var fact = fixture.Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(inverse)));
        var route = fixture.Route(Observation);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => fixture.Run([fact], [route]));
        using var result = JsonDocument.Parse(await fixture.Run([fact], [route, fixture.Route(OntologyObservation, requestOrdinal: 8)]));
        Assert.AreEqual(2, result.RootElement.GetProperty("facts")[0].GetProperty("provenance").GetArrayLength());
    }

    [TestMethod]
    public async Task InboundViewRequiresEveryContributorAndItsScopeBytes()
    {
        var fixture = new Fixture();
        var scopeDigest = fixture.Hold("scope evidence"u8.ToArray());
        var view = new LocalInboundView(FactsSchemaIds.LocalInboundView, FactsFixtures.LuTarget(),
            FactsFixtures.ConsolidatesPredicate, false, scopeDigest,
            [FactsFixtures.PublisherRelation(sourceObservationId: Observation),
             FactsFixtures.PublisherRelation(sourceObservationId: OntologyObservation)]);
        var fact = fixture.Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(view)));
        var routes = new[] { fixture.Route(Observation), fixture.Route(OntologyObservation, requestOrdinal: 8) };
        using var result = JsonDocument.Parse(await fixture.Run([fact], routes));
        Assert.AreEqual(2, result.RootElement.GetProperty("facts")[0].GetProperty("provenance").GetArrayLength());
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => fixture.Run([fact], [routes[0]]));
        fixture.Store.Objects.Remove(scopeDigest);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => fixture.Run([fact], routes));
    }

    [TestMethod]
    [DataRow("publisher")]
    [DataRow("inverse")]
    [DataRow("inbound")]
    public async Task RelationWrapperReplaysItsActualCarriedShape(string shape)
    {
        var fixture = new Fixture();
        var relation = new RelationFact(FactsSchemaIds.RelationFact,
            shape == "publisher" ? RelationAssertionKind.PublisherAsserted
                : shape == "inverse" ? RelationAssertionKind.OntologyAuthorizedInverse : RelationAssertionKind.LocalInboundView,
            TargetBodyScope.BodyInScopeNotHeld, EcliState.EcliNotApplicable,
            shape == "publisher" ? FactsFixtures.PublisherRelation() : null,
            shape == "inverse" ? FactsFixtures.DerivedInverse() : null,
            shape == "inbound" ? FactsFixtures.InboundView() : null);
        var scope = fixture.Hold("scope descriptor"u8.ToArray());
        var fact = fixture.Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(relation)
            .Replace(FactsFixtures.ObservationId, Observation, StringComparison.Ordinal)
            .Replace(FactsFixtures.ScopeDigest, scope, StringComparison.Ordinal)));
        using var result = JsonDocument.Parse(await fixture.Run([fact], [fixture.Route(Observation)]));
        Assert.AreEqual(shape == "inverse" ? 2 : 1,
            result.RootElement.GetProperty("facts")[0].GetProperty("provenance").GetArrayLength());
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("too_many")]
    [DataRow("invalid_digest")]
    public async Task ManifestPopulationIsValidatedBeforeOpeningRoutes(string defect)
    {
        var fixture = new Fixture();
        var digest = new string('a', 64);
        var facts = defect == "too_many"
            ? Enumerable.Range(0, 1001).Select(static index => index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture)).ToArray()
            : defect == "duplicate" ? new[] { digest, digest } : ["not-a-digest"];
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => fixture.Run(facts, [digest]));
        StringAssert.Contains(exception.Message, "population");
        Assert.AreEqual(0, fixture.Store.BodyReads);
    }

    [TestMethod]
    public async Task OversizedMetadataIsRefusedBeforeParsing()
    {
        var fixture = new Fixture();
        var fact = fixture.Hold(new byte[8 * 1024 * 1024 + 1]);
        var exception = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => fixture.Run([fact], [fixture.Route(Observation)]));
        StringAssert.Contains(exception.Message, "decoding bound");
    }

    [TestMethod]
    public async Task CustodyFailuresRemainTypedAndFailureWritesNoPartialReport()
    {
        foreach (var failure in new Exception[]
        {
            new CustodyRequiredException("unavailable"),
            new CustodyPolicyException("retention refusal"),
            new CustodyIntegrityException("substitution"),
        })
        {
            var fixture = new Fixture();
            var fact = fixture.Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(
                FactsFixtures.PublisherRelation(sourceObservationId: Observation))));
            var route = fixture.Route(Observation);
            fixture.Store.BodyFailure = failure;
            var exception = await Assert.ThrowsAsync<Exception>(() => fixture.Run([fact], [route]));
            Assert.AreSame(failure, exception);
            Assert.AreEqual("", fixture.LastOutput.ToString());
        }
    }

    private sealed class Fixture
    {
        internal ReplayStore Store { get; } = new(new Dictionary<string, byte[]>(StringComparer.Ordinal));
        internal string BodyDigest { get; private set; } = "";
        internal string ReceiptDigest { get; private set; } = "";
        internal StringWriter LastOutput { get; private set; } = new(System.Globalization.CultureInfo.InvariantCulture);

        internal string Hold(byte[] bytes)
        {
            var digest = CustodyDigest.Of(bytes);
            Store.Objects[digest] = bytes;
            return digest;
        }

        internal string Route(string observation, ulong requestOrdinal = 7, int status = 200,
            bool partial = false, string? receiptDefect = null)
        {
            byte[] body = [1, 2, 3];
            BodyDigest = Hold(body);
            var reference = new DurableBlobRef(CustodySchemaIds.DurableBlobRef,
                receiptDefect == "digest" ? new string('a', 64) : BodyDigest,
                receiptDefect == "length" ? 4 : body.Length, CustodyClass.NightlyFloor90d);
            var receipt = new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference,
                new CustodyPolicyEvidence(CustodySchemaIds.CustodyPolicyEvidence, reference,
                    CustodyVerificationProfile.FileSystemUnenforced1, null, CustodyProtection.NotEnforced,
                    new DateTimeOffset(2026, 9, 2, 19, 0, 0, TimeSpan.Zero), null));
            var receiptBytes = DurableBlobWriteReceiptDigest.CanonicalBytes(receipt);
            ReceiptDigest = Hold(receiptDefect == "canonical" ? [.. receiptBytes, (byte)' '] : receiptBytes);
            var absent = new RoutedHttpAbsentHeader();
            var headers = new RoutedHttpResponseHeaders(absent, new RoutedHttpSingleHeader(partial ? "4" : "3"),
                absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
            var hop = RoutedHttpHop.Create(0, observation, null, new string('9', 64),
                "https://publications.europa.eu/resource/cellar", status, headers,
                "2026-09-02T20:00:00.0000000Z", "2026-09-02T20:00:01.0000000Z",
                partial ? new IncompleteHttpCompletion(HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.BodyReadFailure))
                    : new DeclaredContentLengthHttpCompletion(3), 3, BodyDigest, ReceiptDigest, 3, BodyDigest);
            // The internal parser construction path permits deliberately independent receipt
            // fixtures; production Create would reject the fault before the replay consumer sees it.
            var route = RoutedHttpEvidence.CreateFromVerifiedHops(new SourceArtifactRef(
                "urn:uuid:33333333-3333-4333-8333-333333333333", new string('1', 64)),
                requestOrdinal, 2, [hop], partial
                    ? new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete)
                    : new CompleteHttpRouteOutcome());
            return Hold(route.CopyCanonicalBytes());
        }

        internal async Task<string> Run(string[] facts, string[] routes)
        {
            var manifest = Hold(Encoding.UTF8.GetBytes(ContractJson.Serialize(new
            {
                Schema = "lex-v3-custody-replay-input/1", Facts = facts, Routes = routes,
            })));
            var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            LastOutput = output;
            await CustodyProbeApplication.RunAsync(["replay", manifest], TextReader.Null, output,
                Environment(), _ => Store, CancellationToken.None);
            return output.ToString();
        }

        private static Dictionary<string, string?> Environment() => new(StringComparer.Ordinal)
        {
            ["LEX_V3_CUSTODY_SERVICE_URI"] = "https://stlexv3custody.blob.core.windows.net/",
            ["LEX_V3_CUSTODY_STAGING_CONTAINER"] = "staging",
            ["LEX_V3_CUSTODY_NIGHTLY_CONTAINER"] = "nightly",
            ["LEX_V3_CUSTODY_LEGAL_HOLD_CONTAINER"] = "legal-hold",
            ["LEX_V3_CUSTODY_MANAGED_IDENTITY_CLIENT_ID"] = "66ba38aa-74fa-42c4-ab19-e4e41b9ae01b",
            ["LEX_V3_CUSTODY_NIGHTLY_POLICY_KEY"] = "ff52fe20-4b11-4ca2-9542-22249d5c4c06",
            ["LEX_V3_CUSTODY_LEGAL_HOLD_POLICY_KEY"] = "b3eb07d3-9159-4673-a4b2-0f4b3ca86293",
            ["LEX_V3_CUSTODY_SUBSCRIPTION_ID"] = "37000e0e-4444-4f9a-95f9-3a786b4ddd30",
            ["LEX_V3_CUSTODY_RESOURCE_GROUP"] = "resource-group",
            ["IDENTITY_ENDPOINT"] = "http://127.0.0.1:42356/msi/token",
            ["IDENTITY_HEADER"] = "platform-rotated-header",
        };
    }

    // Deliberately violates restoration invariants to exercise the consumer's independent guards.
    private sealed class ReplayStore(Dictionary<string, byte[]> objects) : ICustodyStore
    {
        internal Dictionary<string, byte[]> Objects { get; } = objects;
        internal int BodyReads { get; private set; }
        internal Exception? BodyFailure { get; set; }
        public Task<DurableBlobWriteReceipt> CreateAsync(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(Objects.TryGetValue(contentSha256, out var bytes) ? bytes : throw new FileNotFoundException());
        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken)
        {
            BodyReads++;
            if (BodyFailure is { } failure) throw failure;
            return ReadByDigestAsync(reference.ContentSha256, cancellationToken);
        }
    }
}
