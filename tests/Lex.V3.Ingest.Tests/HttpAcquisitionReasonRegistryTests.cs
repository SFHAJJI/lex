using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class HttpAcquisitionReasonRegistryTests
{
    private const string ExpectedSha256 =
        "7648f04492573e9a748a167d27c42841da0cfba1a8735070d2b322c39a242197";

    [TestMethod]
    public void CanonicalArtifactBindsThePinnedIdentityAndClosedVocabulary()
    {
        var bytes = HttpAcquisitionReasonRegistry.CanonicalBytes.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.AreEqual(ExpectedSha256, digest);
        Assert.AreEqual(ExpectedSha256, HttpAcquisitionReasonRegistry.RegistryRef.Sha256);
        CollectionAssert.AreEqual(
            bytes,
            File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory,
                "http-acquisition-reason-registry.json")));

        using var document = JsonDocument.Parse(bytes);
        var members = document.RootElement.GetProperty("members")
            .EnumerateArray()
            .Select(member => $"{member.GetProperty("member_key").GetString()}:{member.GetProperty("stage").GetString()}")
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "body_deadline:entity_transfer",
                "body_read_failure:entity_transfer",
                "byte_bound_prevented_completion:entity_transfer",
                "caller_cancelled_after_headers:entity_transfer",
                "declared_length_short_read:entity_transfer",
                "header_deadline:before_response_headers",
                "missing_completion_proof:completion_unproven",
                "transfer_coding_conflict:completion_unproven",
                "transport_before_headers:before_response_headers",
            },
            members);
    }

    [TestMethod]
    public void TypedMembersRoundTripAndRejectWrongStageOrForeignRegistry()
    {
        foreach (var reason in Enum.GetValues<HttpPartialBodyReason>())
        {
            var member = HttpAcquisitionReasonRegistry.Member(reason);
            Assert.AreEqual(reason, HttpAcquisitionReasonRegistry.RequirePartial(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireCompletionUnproven(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireBeforeHeaders(member));
        }

        foreach (var reason in Enum.GetValues<HttpCompletionUnprovenReason>())
        {
            var member = HttpAcquisitionReasonRegistry.Member(reason);
            Assert.AreEqual(
                reason,
                HttpAcquisitionReasonRegistry.RequireCompletionUnproven(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequirePartial(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireBeforeHeaders(member));
        }

        foreach (var failure in Enum.GetValues<HttpPreHeaderFailureClass>())
        {
            var member = HttpAcquisitionReasonRegistry.Member(failure);
            Assert.AreEqual(failure, HttpAcquisitionReasonRegistry.RequireBeforeHeaders(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequirePartial(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireCompletionUnproven(member));
        }

        var valid = HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.BodyDeadline);
        var foreign = new SourceRegistryMemberRef(
            new SourceArtifactRef(
                valid.RegistryRef.ResourceId,
                new string('f', 64)),
            valid.MemberKey);
        Assert.ThrowsExactly<ArgumentException>(() =>
            HttpAcquisitionReasonRegistry.RequirePartial(foreign));
        Assert.ThrowsExactly<ArgumentException>(() =>
            HttpAcquisitionReasonRegistry.RequirePartial(
                new SourceRegistryMemberRef(valid.RegistryRef, "unknown_reason")));
    }

    [TestMethod]
    public void ObservationConstructorsRejectReasonsFromTheWrongAcquisitionStage()
    {
        var request = RequestEvidence();
        var metadata = EmptyMetadata();

        Assert.ThrowsExactly<ArgumentException>(() => new ResponsePartialBodyObservation(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:00000000-0000-0000-0000-000000000010",
            request,
            request.RequestedUri,
            200,
            HttpStatusClassifier.Classify(200, metadata),
            metadata,
            receivedEncodedEntityByteCount: 0,
            admittedEncodedEntityByteLimit: 1024,
            HttpAcquisitionReasonRegistry.Member(HttpPreHeaderFailureClass.HeaderDeadline),
            durableWriteReceipt: null));

        Assert.ThrowsExactly<ArgumentException>(() => new ResponsePartialBodyObservation(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:00000000-0000-0000-0000-000000000012",
            request,
            request.RequestedUri,
            200,
            HttpStatusClassifier.Classify(200, metadata),
            metadata,
            receivedEncodedEntityByteCount: 0,
            admittedEncodedEntityByteLimit: 1024,
            HttpAcquisitionReasonRegistry.Member(
                HttpCompletionUnprovenReason.MissingCompletionProof),
            durableWriteReceipt: null));

        Assert.ThrowsExactly<ArgumentException>(() => new ResponseCompletionUnprovenObservation(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:00000000-0000-0000-0000-000000000013",
            request,
            request.RequestedUri,
            200,
            HttpStatusClassifier.Classify(200, metadata),
            metadata,
            receivedEncodedEntityByteCount: 0,
            admittedEncodedEntityByteLimit: 1024,
            HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.BodyDeadline),
            durableWriteReceipt: null));

        Assert.ThrowsExactly<ArgumentException>(() => new TransportFailureBeforeBodyObservation(
            HttpObservationSchemaIds.HttpObservation,
            "urn:uuid:00000000-0000-0000-0000-000000000011",
            request,
            HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.BodyDeadline),
            elapsedMilliseconds: 1));
    }

    private static HttpResponseMetadata EmptyMetadata() => new(
        new AbsentHttpHeader(),
        new AbsentHttpHeader(),
        new AbsentHttpHeader(),
        new AbsentHttpHeader(),
        new AbsentHttpHeader(),
        new AbsentHttpHeader(),
        new AbsentHttpHeader(),
        new AbsentHttpHeader());

    private static HttpRequestEvidence RequestEvidence() => new(
        requestedUri: "https://data.legilux.public.lu/example.xml",
        HttpRequestMethod.Get,
        observedAtUtc: "2026-09-01T10:00:00.000Z",
        timestampPrecision: HttpObservationTimestampPrecision.Millisecond,
        clockSource: HttpObservationClockSource.SystemUtc,
        runIdentity: Artifact(1),
        adapterIdentity: Artifact(2),
        requestPolicyIdentity: Artifact(3),
        representationRequestKeyIdentity: Artifact(4),
        outboundCrawlerIdentity: new OutboundCrawlerIdentityEvidence(
            OutboundCrawlerIdentity.Schema,
            OutboundCrawlerIdentity.Token),
        origin: new HttpOrigin("https", "data.legilux.public.lu", 443),
        queryPlanIdentity: Artifact(5));

    private static SourceArtifactRef Artifact(int suffix) => new(
        $"urn:uuid:00000000-0000-0000-0000-{suffix:D12}",
        new string('a', 64));
}
