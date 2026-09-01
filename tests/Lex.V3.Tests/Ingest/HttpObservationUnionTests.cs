using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Ingest;

[TestClass]
public sealed class HttpObservationUnionTests
{
    [TestMethod]
    public void WireUnionIsExactlyTheSixR2Variants()
    {
        (HttpObservation Observation, string Kind, Type RuntimeType)[] cases =
        [
            (Complete(), "response_complete_body", typeof(ResponseCompleteBodyObservation)),
            (Partial(), "response_partial_body", typeof(ResponsePartialBodyObservation)),
            (Revalidation(), "revalidation_304", typeof(Revalidation304Observation)),
            (WithoutBody(), "response_without_body", typeof(ResponseWithoutBodyObservation)),
            (Failure(), "transport_failure_before_body", typeof(TransportFailureBeforeBodyObservation)),
            (Rejection(), "policy_rejection", typeof(PolicyRejectionObservation)),
        ];

        foreach (var (observation, kind, runtimeType) in cases)
        {
            var json = ContractJson.Serialize<HttpObservation>(observation);
            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(kind, document.RootElement.GetProperty("kind").GetString());
            Assert.AreEqual(HttpObservationSchemaIds.HttpObservation, document.RootElement.GetProperty("schema").GetString());
            CollectionAssert.AreEquivalent(
                ExpectedProperties(kind),
                document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray(),
                kind);
            Assert.AreEqual(runtimeType, ContractJson.Deserialize<HttpObservation>(json).GetType());
        }

        var valid = JsonNode.Parse(ContractJson.Serialize<HttpObservation>(Complete()))!.AsObject();
        valid["kind"] = "ResponseCompleteBody";
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<HttpObservation>(valid.ToJsonString()));
        valid["kind"] = 1;
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<HttpObservation>(valid.ToJsonString()));
        valid["kind"] = "unknown";
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<HttpObservation>(valid.ToJsonString()));
    }

    [TestMethod]
    public void ConcreteHttpLeafSerializationCannotBypassTheDiscriminator()
    {
        AssertConcretePath(Complete(), "response_complete_body");
        AssertConcretePath(Partial(), "response_partial_body");
        AssertConcretePath(Revalidation(), "revalidation_304");
        AssertConcretePath(WithoutBody(), "response_without_body");
        AssertConcretePath(Failure(), "transport_failure_before_body");
        AssertConcretePath(Rejection(), "policy_rejection");
    }

    [TestMethod]
    public void NoBodyReasonsAreExactlyTheTwoR2Members()
    {
        (HttpNoBodyReason Value, int Number, string Wire)[] expected =
        [
            (HttpNoBodyReason.SemanticNoEntity, 1, "semantic_no_entity"),
            (HttpNoBodyReason.CompleteZeroOctetEntity, 2, "complete_zero_octet_entity"),
        ];

        CollectionAssert.AreEqual(
            expected.Select(static row => row.Number).ToArray(),
            Enum.GetValues<HttpNoBodyReason>().Select(static value => (int)value).ToArray());
        Assert.IsFalse(Enum.IsDefined((HttpNoBodyReason)0));
        foreach (var (value, _, wire) in expected)
        {
            Assert.AreEqual($"\"{wire}\"", ContractJson.Serialize(value));
            Assert.AreEqual(value, ContractJson.Deserialize<HttpNoBodyReason>($"\"{wire}\""));
        }

        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpNoBodyReason>("1"));
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpNoBodyReason>("\"unknown\""));
    }

    [TestMethod]
    public void ValidatorEvidenceClosesTheTwoExactHeaderPairs()
    {
        var etag = Validator("If-None-Match", "ETag", "\"opaque\"");
        var lastModified = new HttpValidatorEvidence(
            RegistryMember("last_modified"),
            "If-Modified-Since",
            "Last-Modified",
            "Mon, 01 Sep 2026 00:00:00 GMT");
        Assert.AreEqual("etag", etag.ValidatorKind.MemberKey);
        Assert.AreEqual("last_modified", lastModified.ValidatorKind.MemberKey);

        Assert.ThrowsExactly<ArgumentException>(() => new HttpValidatorEvidence(
            RegistryMember("etag"), "If-Modified-Since", "Last-Modified", "value"));
        Assert.ThrowsExactly<ArgumentException>(() => new HttpValidatorEvidence(
            RegistryMember("unknown"), "If-None-Match", "ETag", "value"));
        Assert.ThrowsExactly<ArgumentException>(() => new HttpValidatorEvidence(
            RegistryMember("etag"), "If-None-Match", "ETag", "bad\r\nvalue"));
    }

    [TestMethod]
    public void CompleteBodyBindsExactNonemptyTransportBytes()
    {
        CollectionAssert.DoesNotContain(
            typeof(ResponseCompleteBodyObservation).GetProperties()
                .Select(static property => property.Name).ToArray(),
            "TransferComplete");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Complete(byteCount: 0));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            byteCount: 2,
            writeReceipt: WriteReceipt(1, 'a')));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            completionEvidence: CompletionEvidence(digestCharacter: 'b')));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            completionEvidence: CompletionEvidence(
                adapterExecutionIdentity: Artifact(
                    "urn:uuid:abababab-abab-4bab-8bab-abababababab", 'a'))));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            completionEvidence: CompletionEvidence(
                responseObservationId: "urn:uuid:abababab-abab-4bab-8bab-abababababab")));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            metadata: Metadata(contentLength: 2)));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(statusCode: 204));

        var rangeEvidence = Complete(
            statusCode: 206,
            statusDisposition: HttpStatusDisposition.RangeNotApproved,
            completionEvidence: new Http2EndStreamCompleteEvidence(
                TransferCompletionSchemaIds.TransferCompletionEvidence,
                Artifact("urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                new string('a', 64),
                1,
                Artifact("urn:uuid:14141414-1414-4414-8414-141414141414", '1')),
            metadata: Metadata(contentLength: 1, contentRange: "bytes 0-0/1"));
        Assert.AreEqual(HttpStatusDisposition.RangeNotApproved, rangeEvidence.StatusDisposition);

        var duplicateMetadata = new HttpResponseMetadata(
            new SingleHttpHeader("application/xml"),
            new SingleHttpHeader("utf-8"),
            new SingleHttpHeader("1"),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new MultipleHttpHeader(["\"one\"", "\"two\""]),
            new AbsentHttpHeader());
        Assert.ThrowsExactly<ArgumentException>(() => Complete(metadata: duplicateMetadata));
        var duplicateHeaderEvidence = Complete(
            statusDisposition: HttpStatusDisposition.NonDerivableStatus,
            metadata: duplicateMetadata);
        Assert.IsInstanceOfType<MultipleHttpHeader>(duplicateHeaderEvidence.ResponseMetadata.Etag);
        Assert.AreEqual(
            HttpStatusDisposition.NonDerivableStatus,
            duplicateHeaderEvidence.StatusDisposition);

        var malformedLengthMetadata = new HttpResponseMetadata(
            new SingleHttpHeader("application/xml"),
            new SingleHttpHeader("utf-8"),
            new SingleHttpHeader("not-a-length"),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new SingleHttpHeader("\"one\""),
            new AbsentHttpHeader());
        var protocolCompletion = new Http2EndStreamCompleteEvidence(
            TransferCompletionSchemaIds.TransferCompletionEvidence,
            Artifact("urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
            "urn:uuid:11111111-1111-4111-8111-111111111111",
            new string('a', 64),
            1,
            Artifact("urn:uuid:14141414-1414-4414-8414-141414141414", '1'));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            metadata: malformedLengthMetadata,
            completionEvidence: protocolCompletion));
        Assert.AreEqual(
            HttpStatusDisposition.NonDerivableStatus,
            Complete(
                statusDisposition: HttpStatusDisposition.NonDerivableStatus,
                metadata: malformedLengthMetadata,
                completionEvidence: protocolCompletion).StatusDisposition);
    }

    [TestMethod]
    public void PartialBodyKeepsZeroAndPositiveEvidenceDistinct()
    {
        Assert.IsNull(Partial(byteCount: 0, writeReceipt: null).DurableWriteReceipt);
        Assert.ThrowsExactly<ArgumentException>(() => Partial(
            byteCount: 0,
            writeReceipt: WriteReceipt(1, 'a')));
        Assert.ThrowsExactly<ArgumentException>(() => Partial(
            byteCount: 1,
            omitEvidence: true));
        Assert.ThrowsExactly<ArgumentException>(() => Partial(
            byteCount: 2,
            writeReceipt: WriteReceipt(1, 'a')));

        var declaredLengthMismatch = Partial(
            byteCount: 1,
            writeReceipt: WriteReceipt(1, 'a'),
            metadata: Metadata(contentLength: 2));
        Assert.AreEqual(1, declaredLengthMismatch.ReceivedEncodedEntityByteCount);
    }

    [TestMethod]
    public void TransferCompletionEvidenceIsExactlyTheFourPositiveProtocolLeaves()
    {
        TransferCompletionEvidence[] evidence =
        [
            CompletionEvidence(),
            new Http1TerminalChunkCompleteEvidence(
                TransferCompletionSchemaIds.TransferCompletionEvidence,
                Artifact("urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                new string('a', 64),
                1,
                Artifact("urn:uuid:13131313-1313-4313-8313-131313131313", '1')),
            new Http2EndStreamCompleteEvidence(
                TransferCompletionSchemaIds.TransferCompletionEvidence,
                Artifact("urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                new string('a', 64),
                1,
                Artifact("urn:uuid:14141414-1414-4414-8414-141414141414", '1')),
            new Http3FinCompleteEvidence(
                TransferCompletionSchemaIds.TransferCompletionEvidence,
                Artifact("urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                new string('a', 64),
                1,
                Artifact("urn:uuid:15151515-1515-4515-8515-151515151515", '1')),
        ];

        CollectionAssert.AreEqual(
            new[]
            {
                "declared_content_length_complete",
                "http1_terminal_chunk_complete",
                "http2_end_stream_complete",
                "http3_fin_complete",
            },
            evidence.Select(item =>
            {
                using var document = JsonDocument.Parse(
                    ContractJson.Serialize<TransferCompletionEvidence>(item));
                return document.RootElement.GetProperty("kind").GetString();
            }).ToArray());

        foreach (var item in evidence)
        {
            Assert.AreEqual(
                item.GetType(),
                ContractJson.Deserialize<TransferCompletionEvidence>(
                    ContractJson.Serialize<TransferCompletionEvidence>(item)).GetType());
        }
    }

    [TestMethod]
    public void NoBodyReasonMatchesTheStatusAndNeverCreatesAnEmptyBlob()
    {
        Assert.AreEqual(HttpNoBodyReason.SemanticNoEntity, WithoutBody().Reason);
        Assert.AreEqual(
            HttpNoBodyReason.CompleteZeroOctetEntity,
            WithoutBody(
                statusCode: 200,
                statusDisposition: HttpStatusDisposition.DerivableStatus,
                reason: HttpNoBodyReason.CompleteZeroOctetEntity).Reason);

        Assert.ThrowsExactly<ArgumentException>(() => WithoutBody(
            statusCode: 200,
            statusDisposition: HttpStatusDisposition.DerivableStatus,
            reason: HttpNoBodyReason.SemanticNoEntity));
        Assert.ThrowsExactly<ArgumentException>(() => WithoutBody(
            statusCode: 204,
            statusDisposition: HttpStatusDisposition.SemanticNoEntityStatus,
            reason: HttpNoBodyReason.CompleteZeroOctetEntity));
        Assert.ThrowsExactly<ArgumentException>(() => WithoutBody(
            statusCode: 304,
            statusDisposition: HttpStatusDisposition.RevalidationReferenceOnly,
            reason: HttpNoBodyReason.CompleteZeroOctetEntity));
        Assert.ThrowsExactly<ArgumentException>(() => WithoutBody(metadata: Metadata(contentLength: 1)));
    }

    [TestMethod]
    public void ResponseDispositionIsDerivedFromStatusAndRetainedMetadata()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            statusDisposition: HttpStatusDisposition.NonDerivableStatus));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            statusDisposition: HttpStatusDisposition.DerivableStatus,
            metadata: Metadata(contentLength: 1, contentRange: "bytes 0-0/1")));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            effectiveUri: "https://PUBLICATIONS.EUROPA.EU/resource/cellar"));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            effectiveUri: "https://@publications.europa.eu/resource/cellar"));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            effectiveUri: "https://publications.europa.eu/a/%2f..%2f/b"));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            effectiveUri: "https://publications.europa.eu/a/%252e%252e/b"));
        Assert.ThrowsExactly<ArgumentException>(() => Complete(
            effectiveUri: "https://publications.europa.eu/a/%25%2532%2565%25%2532%2565/b"));
    }

    [TestMethod]
    public void RevalidationIsAnExactReferenceAndCarriesNoNewBytes()
    {
        var predecessor = Predecessor();
        var observation = Revalidation(predecessor: predecessor);
        Assert.AreEqual(304, observation.StatusCode);
        Assert.AreEqual(observation.SentValidator, observation.PredecessorValidator);
        Assert.AreSame(observation, observation.AdmitAgainst(predecessor).Observation);

        var properties = typeof(Revalidation304Observation)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();
        CollectionAssert.DoesNotContain(properties, "ReceivedEncodedEntityByteCount");
        CollectionAssert.DoesNotContain(properties, "TransportByteSha256");

        Assert.ThrowsExactly<ArgumentException>(() => Revalidation(statusCode: 200));
        Assert.ThrowsExactly<ArgumentException>(() => Revalidation(
            request: Request(method: HttpRequestMethod.Post)));
        Assert.ThrowsExactly<ArgumentException>(() => Revalidation(
            predecessorValidator: Validator("If-None-Match", "ETag", "different")));
        Assert.ThrowsExactly<ArgumentException>(() => Revalidation(
            metadata: Metadata(contentRange: "bytes 0-0/1")));
        Assert.ThrowsExactly<ArgumentException>(() => Revalidation(
            predecessorBlobRef: Blob(0, '0')));

        var retainedLength = Revalidation(
            predecessor: predecessor,
            metadata: Metadata(contentLength: 1));
        Assert.AreEqual(
            "1",
            Assert.IsInstanceOfType<SingleHttpHeader>(
                retainedLength.ResponseMetadata.ContentLength).Value);
        Assert.AreSame(
            retainedLength,
            retainedLength.AdmitAgainst(predecessor).Observation);

        var mismatchedRetainedLength = Revalidation(
            predecessor: predecessor,
            metadata: Metadata(contentLength: 123));
        Assert.ThrowsExactly<ArgumentException>(
            () => mismatchedRetainedLength.AdmitAgainst(predecessor));

        var wrongObservationRef = Revalidation(
            predecessor: predecessor,
            predecessorObservationRef: Artifact(
                predecessor.ObservationId,
                '9'));
        Assert.ThrowsExactly<ArgumentException>(() => wrongObservationRef.AdmitAgainst(predecessor));

        var wrongBlob = Revalidation(
            predecessor: predecessor,
            predecessorBlobRef: Blob(1, 'b'));
        Assert.ThrowsExactly<ArgumentException>(() => wrongBlob.AdmitAgainst(predecessor));

        var otherKeyPredecessor = Predecessor(request: Request(
            representationRequestKeyIdentity: Artifact(
                "urn:uuid:17171717-1717-4717-8717-171717171717", '1')));
        Assert.ThrowsExactly<ArgumentException>(
            () => observation.AdmitAgainst(otherKeyPredecessor));
    }

    [TestMethod]
    public void PreHeaderFailureAndPolicyRejectionExposeNoResponseSurface()
    {
        string[] forbidden =
        [
            "EffectiveUri", "StatusCode", "StatusDisposition", "ResponseMetadata",
            "ReceivedEncodedEntityByteCount", "TransportByteSha256", "DurableBlobRef",
            "DurableWriteReceipt", "TransferCompletionEvidence",
        ];

        foreach (var type in new[]
        {
            typeof(TransportFailureBeforeBodyObservation),
            typeof(PolicyRejectionObservation),
        })
        {
            var names = type.GetProperties().Select(static property => property.Name).ToArray();
            Assert.IsFalse(forbidden.Any(names.Contains), type.Name);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Failure(elapsedMilliseconds: -1));
    }

    [TestMethod]
    public void VariantFieldsCannotBeSmuggledAcrossTheDiscriminator()
    {
        var original = JsonNode.Parse(ContractJson.Serialize<HttpObservation>(Complete()))!.AsObject();
        foreach (var otherKind in new[]
        {
            "response_partial_body",
            "revalidation_304",
            "response_without_body",
            "transport_failure_before_body",
            "policy_rejection",
        })
        {
            var smuggled = JsonNode.Parse(original.ToJsonString())!.AsObject();
            smuggled["kind"] = otherKind;
            Assert.ThrowsExactly<JsonException>(
                () => ContractJson.Deserialize<HttpObservation>(smuggled.ToJsonString()),
                otherKind);
        }
    }

    [TestMethod]
    public void EveryCompleteBodyWireMemberIsRequiredAndTheShapeIsClosed()
    {
        var complete = JsonNode.Parse(ContractJson.Serialize<HttpObservation>(Complete()))!.AsObject();
        foreach (var propertyName in complete.Select(static pair => pair.Key).ToArray())
        {
            var missingOne = JsonNode.Parse(complete.ToJsonString())!.AsObject();
            Assert.IsTrue(missingOne.Remove(propertyName));
            if (propertyName == "kind")
            {
                Assert.ThrowsExactly<NotSupportedException>(
                    () => ContractJson.Deserialize<HttpObservation>(missingOne.ToJsonString()),
                    propertyName);
            }
            else
            {
                Assert.ThrowsExactly<JsonException>(
                    () => ContractJson.Deserialize<HttpObservation>(missingOne.ToJsonString()),
                    propertyName);
            }
        }

        complete["headers"] = new JsonObject();
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<HttpObservation>(complete.ToJsonString()));
    }

    private static ResponseCompleteBodyObservation Complete(
        long byteCount = 1,
        TransferCompletionEvidence? completionEvidence = null,
        DurableBlobWriteReceipt? writeReceipt = null,
        int statusCode = 200,
        HttpStatusDisposition statusDisposition = HttpStatusDisposition.DerivableStatus,
        HttpResponseMetadata? metadata = null,
        HttpRequestEvidence? request = null,
        string effectiveUri = "https://publications.europa.eu/resource/cellar") =>
        new(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:11111111-1111-4111-8111-111111111111",
            request ?? Request(),
            effectiveUri,
            statusCode,
            statusDisposition,
            metadata ?? Metadata(contentLength: 1),
            completionEvidence ?? CompletionEvidence(byteCount: byteCount),
            writeReceipt ?? WriteReceipt(byteCount, 'a'));

    private static ResponsePartialBodyObservation Partial(
        long byteCount = 1,
        DurableBlobWriteReceipt? writeReceipt = null,
        HttpResponseMetadata? metadata = null,
        bool omitEvidence = false) =>
        new(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:22222222-2222-4222-8222-222222222222",
            Request(),
            "https://publications.europa.eu/resource/cellar",
            200,
            HttpStatusDisposition.DerivableStatus,
            metadata ?? Metadata(),
            byteCount,
            RegistryMember("abrupt_eof"),
            omitEvidence ? null : writeReceipt ?? (byteCount > 0 ? WriteReceipt(byteCount, 'a') : null));

    private static Revalidation304Observation Revalidation(
        int statusCode = 304,
        HttpRequestEvidence? request = null,
        HttpResponseMetadata? metadata = null,
        HttpValidatorEvidence? predecessorValidator = null,
        DurableBlobRef? predecessorBlobRef = null,
        ResponseCompleteBodyObservation? predecessor = null,
        SourceArtifactRef? predecessorObservationRef = null)
    {
        predecessor ??= Predecessor();
        var validator = Validator("If-None-Match", "ETag", "\"opaque\"");
        return new(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:33333333-3333-4333-8333-333333333333",
            request ?? Request(),
            "https://publications.europa.eu/resource/cellar",
            statusCode,
            HttpStatusDisposition.RevalidationReferenceOnly,
            metadata ?? Metadata(),
            validator,
            predecessorValidator ?? validator,
            predecessorObservationRef ?? ObservationRef(predecessor),
            (request ?? Request()).RepresentationRequestKeyIdentity,
            predecessorBlobRef ?? predecessor.DurableBlobRef);
    }

    private static ResponseCompleteBodyObservation Predecessor(HttpRequestEvidence? request = null) =>
        Complete(
            request: request,
            metadata: Metadata(contentLength: 1, etag: "\"opaque\""));

    private static ResponseWithoutBodyObservation WithoutBody(
        int statusCode = 204,
        HttpStatusDisposition statusDisposition = HttpStatusDisposition.SemanticNoEntityStatus,
        HttpNoBodyReason reason = HttpNoBodyReason.SemanticNoEntity,
        HttpResponseMetadata? metadata = null) =>
        new(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:44444444-4444-4444-8444-444444444444",
            Request(),
            "https://publications.europa.eu/resource/cellar",
            statusCode,
            statusDisposition,
            metadata ?? Metadata(contentLength: 0),
            0,
            reason);

    private static TransportFailureBeforeBodyObservation Failure(int elapsedMilliseconds = 250) =>
        new(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:55555555-5555-4555-8555-555555555555",
            Request(),
            RegistryMember("dns_failure"),
            elapsedMilliseconds);

    private static PolicyRejectionObservation Rejection() =>
        new(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:99999999-9999-4999-8999-999999999999",
            Request(),
            RegistryMember("robots_denied"),
            RegistryMember("initial_request"),
            Artifact("urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", 'a'));

    private static HttpValidatorEvidence Validator(
        string requestHeaderName,
        string responseHeaderName,
        string value) =>
        new(RegistryMember("etag"), requestHeaderName, responseHeaderName, value);

    private static HttpRequestEvidence Request(
        HttpRequestMethod method = HttpRequestMethod.Get,
        SourceArtifactRef? representationRequestKeyIdentity = null) =>
        new(
            "https://publications.europa.eu/resource/cellar",
            method,
            "2026-09-01T00:00:00.000Z",
            HttpObservationTimestampPrecision.Millisecond,
            HttpObservationClockSource.SystemUtc,
            Artifact("urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", 'b'),
            Artifact("urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
            Artifact("urn:uuid:dddddddd-dddd-4ddd-8ddd-dddddddddddd", 'd'),
            representationRequestKeyIdentity ?? Artifact(
                "urn:uuid:eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee", 'e'),
            new OutboundCrawlerIdentityEvidence(
                OutboundCrawlerIdentity.Schema,
                OutboundCrawlerIdentity.Token),
            new HttpOrigin("https", "publications.europa.eu", 443),
            Artifact("urn:uuid:ffffffff-ffff-4fff-8fff-ffffffffffff", 'f'));

    private static HttpResponseMetadata Metadata(
        long? contentLength = null,
        string? contentRange = null,
        string? etag = null) =>
        new(
            new SingleHttpHeader("application/xml"),
            new SingleHttpHeader("utf-8"),
            contentLength is null
                ? new AbsentHttpHeader()
                : new SingleHttpHeader(contentLength.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new AbsentHttpHeader(),
            contentRange is null
                ? new AbsentHttpHeader()
                : new SingleHttpHeader(contentRange),
            etag is null
                ? new AbsentHttpHeader()
                : new SingleHttpHeader(etag),
            new AbsentHttpHeader());

    private static DurableBlobRef Blob(long byteLength, char digestCharacter) =>
        new(
            CustodySchemaIds.DurableBlobRef,
            new string(digestCharacter, 64),
            byteLength,
            CustodyClass.NightlyFloor90d);

    private static DurableBlobWriteReceipt WriteReceipt(long byteLength, char digestCharacter)
    {
        var reference = Blob(byteLength, digestCharacter);
        var observedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("16161616-1616-4616-8616-161616161616"),
                CustodyProtection.LockedTime,
                observedAt,
                observedAt.AddDays(91)));
    }

    private static TransferCompletionEvidence CompletionEvidence(
        long byteCount = 1,
        char digestCharacter = 'a',
        SourceArtifactRef? adapterExecutionIdentity = null,
        string responseObservationId = "urn:uuid:11111111-1111-4111-8111-111111111111") =>
        new DeclaredContentLengthCompleteEvidence(
            TransferCompletionSchemaIds.TransferCompletionEvidence,
            adapterExecutionIdentity ?? Artifact(
                "urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", 'c'),
            responseObservationId,
            new string(digestCharacter, 64),
            byteCount);

    private static SourceRegistryMemberRef RegistryMember(string memberKey) =>
        new(Artifact("urn:uuid:12121212-1212-4212-8212-121212121212", '1'), memberKey);

    private static SourceArtifactRef Artifact(string resourceId, char digestCharacter) =>
        new(resourceId, new string(digestCharacter, 64));

    private static SourceArtifactRef ObservationRef(HttpObservation observation) =>
        new(
            observation.ObservationId,
            Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes(ContractJson.Serialize<HttpObservation>(observation)))));

    private static void AssertConcretePath<TObservation>(TObservation observation, string kind)
        where TObservation : HttpObservation
    {
        var inferred = ContractJson.Serialize(observation);
        var explicitConcrete = ContractJson.Serialize<TObservation>(observation);
        foreach (var json in new[] { inferred, explicitConcrete })
        {
            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(kind, document.RootElement.GetProperty("kind").GetString());
            Assert.AreEqual(
                typeof(TObservation),
                ContractJson.Deserialize<TObservation>(json).GetType());

            var missingKind = JsonNode.Parse(json)!.AsObject();
            Assert.IsTrue(missingKind.Remove("kind"));
            Assert.ThrowsExactly<NotSupportedException>(
                () => ContractJson.Deserialize<TObservation>(missingKind.ToJsonString()));
        }
    }

    private static string[] ExpectedProperties(string kind) => kind switch
    {
        "response_complete_body" =>
        [
            "kind", "schema", "observation_id", "request", "effective_uri", "status_code",
            "status_disposition", "response_metadata", "transfer_completion_evidence",
            "durable_write_receipt",
        ],
        "response_partial_body" =>
        [
            "kind", "schema", "observation_id", "request", "effective_uri", "status_code",
            "status_disposition", "response_metadata", "received_encoded_entity_byte_count",
            "terminal_failure_reason", "durable_write_receipt",
        ],
        "revalidation_304" =>
        [
            "kind", "schema", "observation_id", "request", "effective_uri", "status_code",
            "status_disposition", "response_metadata", "sent_validator",
            "predecessor_validator", "predecessor_observation_ref",
            "representation_request_key_ref", "predecessor_blob_ref",
        ],
        "response_without_body" =>
        [
            "kind", "schema", "observation_id", "request", "effective_uri", "status_code",
            "status_disposition", "response_metadata", "received_encoded_entity_byte_count",
            "reason",
        ],
        "transport_failure_before_body" =>
        ["kind", "schema", "observation_id", "request", "failure_class", "elapsed_milliseconds"],
        "policy_rejection" =>
        [
            "kind", "schema", "observation_id", "request", "rejection_reason",
            "rejected_stage", "zero_request_proof_ref",
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
