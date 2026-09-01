using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Ingest;

[TestClass]
public sealed class HttpObservationContractTests
{
    [TestMethod]
    public void TransferClassificationAppliesTheClosedR2Precedence()
    {
        (HttpTransferFacts Facts, HttpObservationKind Expected)[] cases =
        [
            (new(true, false, false, null, 0), HttpObservationKind.PolicyRejection),
            (new(false, false, false, null, 0), HttpObservationKind.TransportFailureBeforeBody),
            (new(false, true, false, 200, 0), HttpObservationKind.ResponsePartialBody),
            (new(false, true, false, 200, 7), HttpObservationKind.ResponsePartialBody),
            (new(false, true, true, 304, 0), HttpObservationKind.Revalidation304),
            (new(false, true, true, 204, 0), HttpObservationKind.ResponseWithoutBody),
            (new(false, true, true, 205, 0), HttpObservationKind.ResponseWithoutBody),
            (new(false, true, true, 200, 0), HttpObservationKind.ResponseWithoutBody),
            (new(false, true, true, 200, 1), HttpObservationKind.ResponseCompleteBody),
            (new(false, true, false, 304, 7), HttpObservationKind.ResponsePartialBody),
        ];

        foreach (var (facts, expected) in cases)
        {
            Assert.AreEqual(expected, HttpTransferClassifier.Classify(facts), facts.ToString());
        }
    }

    [TestMethod]
    public void TransferClassificationRejectsContradictoryCausalFacts()
    {
        HttpTransferFacts[] invalid =
        [
            new(true, true, true, 200, 1),
            new(true, false, false, null, 1),
            new(false, false, true, null, 0),
            new(false, false, false, 200, 0),
            new(false, false, false, null, 1),
            new(false, true, false, null, 0),
            new(false, true, true, 99, 0),
            new(false, true, true, 600, 0),
            new(false, true, true, 304, 1),
            new(false, true, true, 204, 1),
            new(false, true, true, 205, 1),
        ];

        foreach (var facts in invalid)
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => HttpTransferClassifier.Classify(facts),
                facts.ToString());
        }
    }

    [TestMethod]
    public void ObservationKindsAreExactlyTheSixR2Variants()
    {
        (HttpObservationKind Value, int Number, string Symbol, string Wire)[] expected =
        [
            (HttpObservationKind.ResponseCompleteBody, 1, nameof(HttpObservationKind.ResponseCompleteBody), "response_complete_body"),
            (HttpObservationKind.ResponsePartialBody, 2, nameof(HttpObservationKind.ResponsePartialBody), "response_partial_body"),
            (HttpObservationKind.Revalidation304, 3, nameof(HttpObservationKind.Revalidation304), "revalidation_304"),
            (HttpObservationKind.ResponseWithoutBody, 4, nameof(HttpObservationKind.ResponseWithoutBody), "response_without_body"),
            (HttpObservationKind.TransportFailureBeforeBody, 5, nameof(HttpObservationKind.TransportFailureBeforeBody), "transport_failure_before_body"),
            (HttpObservationKind.PolicyRejection, 6, nameof(HttpObservationKind.PolicyRejection), "policy_rejection"),
        ];

        CollectionAssert.AreEqual(expected.Select(static row => row.Symbol).ToArray(), Enum.GetNames<HttpObservationKind>());
        CollectionAssert.AreEqual(expected.Select(static row => row.Number).ToArray(), Enum.GetValues<HttpObservationKind>().Select(static value => (int)value).ToArray());
        Assert.AreEqual(expected.Length, expected.Select(static row => row.Number).Distinct().Count());
        Assert.IsFalse(Enum.IsDefined((HttpObservationKind)0));

        foreach (var (value, _, _, wire) in expected)
        {
            Assert.AreEqual($"\"{wire}\"", ContractJson.Serialize(value));
            Assert.AreEqual(value, ContractJson.Deserialize<HttpObservationKind>($"\"{wire}\""));
        }

        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpObservationKind>("1"));
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpObservationKind>("\"ResponseCompleteBody\""));
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpObservationKind>("\"unknown\""));
    }

    [TestMethod]
    public void StatusClassificationExhaustivelyKeepsTransportEvidenceSeparateFromDerivability()
    {
        var withoutContentRange = EmptyResponseMetadata();
        var withContentRange = new HttpResponseMetadata(
            null, null, null, null, "bytes 0-0/1", null, null);

        for (var status = 100; status <= 599; status++)
        {
            foreach (var hasContentRange in new[] { false, true })
            {
                var expected = status == 206 || hasContentRange
                    ? HttpStatusDisposition.RangeNotApproved
                    : status switch
                    {
                        200 => HttpStatusDisposition.DerivableStatus,
                        301 or 302 or 303 or 307 or 308 => HttpStatusDisposition.RedirectObserved,
                        304 => HttpStatusDisposition.RevalidationReferenceOnly,
                        204 or 205 => HttpStatusDisposition.SemanticNoEntityStatus,
                        _ => HttpStatusDisposition.NonDerivableStatus,
                    };

                Assert.AreEqual(
                    expected,
                    HttpStatusClassifier.Classify(
                        status,
                        hasContentRange ? withContentRange : withoutContentRange),
                    $"status={status}, content-range={hasContentRange}");
            }
        }
    }

    [TestMethod]
    public void StatusDispositionsAreExactlyTheClosedR2Vocabulary()
    {
        (HttpStatusDisposition Value, int Number, string Symbol, string Wire)[] expected =
        [
            (HttpStatusDisposition.DerivableStatus, 1, nameof(HttpStatusDisposition.DerivableStatus), "derivable_status"),
            (HttpStatusDisposition.RedirectObserved, 2, nameof(HttpStatusDisposition.RedirectObserved), "redirect_observed"),
            (HttpStatusDisposition.RevalidationReferenceOnly, 3, nameof(HttpStatusDisposition.RevalidationReferenceOnly), "revalidation_reference_only"),
            (HttpStatusDisposition.SemanticNoEntityStatus, 4, nameof(HttpStatusDisposition.SemanticNoEntityStatus), "semantic_no_entity_status"),
            (HttpStatusDisposition.RangeNotApproved, 5, nameof(HttpStatusDisposition.RangeNotApproved), "range_not_approved"),
            (HttpStatusDisposition.NonDerivableStatus, 6, nameof(HttpStatusDisposition.NonDerivableStatus), "non_derivable_status"),
        ];

        CollectionAssert.AreEqual(expected.Select(static row => row.Symbol).ToArray(), Enum.GetNames<HttpStatusDisposition>());
        CollectionAssert.AreEqual(expected.Select(static row => row.Number).ToArray(), Enum.GetValues<HttpStatusDisposition>().Select(static value => (int)value).ToArray());
        Assert.AreEqual(expected.Length, expected.Select(static row => row.Number).Distinct().Count());
        Assert.IsFalse(Enum.IsDefined((HttpStatusDisposition)0));

        foreach (var (value, _, _, wire) in expected)
        {
            Assert.AreEqual($"\"{wire}\"", ContractJson.Serialize(value));
            Assert.AreEqual(value, ContractJson.Deserialize<HttpStatusDisposition>($"\"{wire}\""));
        }

        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpStatusDisposition>("1"));
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpStatusDisposition>("\"DerivableStatus\""));
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpStatusDisposition>("\"unknown\""));
    }

    [TestMethod]
    public void TheOutboundCrawlerIdentityIsOneExactNonOverrideableToken()
    {
        Assert.AreEqual(
            "Lex/0.1 (+https://github.com/SFHAJJI/lex)",
            OutboundCrawlerIdentity.Token);
        Assert.AreEqual("outbound_crawler_identity/1", OutboundCrawlerIdentity.Schema);

        foreach (var name in new[] { nameof(OutboundCrawlerIdentity.Schema), nameof(OutboundCrawlerIdentity.Token) })
        {
            var property = typeof(OutboundCrawlerIdentity).GetProperty(name);
            Assert.IsNotNull(property);
            Assert.IsNull(property.SetMethod);
        }
    }

    [TestMethod]
    public void ResponseMetadataIsExactlyTheSevenFieldAllowlistWithExplicitNulls()
    {
        var metadata = new HttpResponseMetadata(
            contentType: null,
            declaredCharset: null,
            contentLength: null,
            contentEncoding: null,
            contentRange: null,
            etag: null,
            lastModified: null);

        using var document = JsonDocument.Parse(ContractJson.Serialize(metadata));
        var properties = document.RootElement.EnumerateObject().ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                "content_type",
                "declared_charset",
                "content_length",
                "content_encoding",
                "content_range",
                "etag",
                "last_modified",
            },
            properties.Select(static property => property.Name).ToArray());
        Assert.IsTrue(properties.All(static property => property.Value.ValueKind == JsonValueKind.Null));

        string[] forbidden =
        [
            "Headers", "Cookies", "Addresses", "Credentials", "Query", "Sparql",
            "UserText", "Ip", "InboundUserAgent", "RedirectHistory",
        ];
        var publicMembers = typeof(HttpResponseMetadata)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();
        Assert.IsFalse(forbidden.Any(publicMembers.Contains));
    }

    [TestMethod]
    public void ResponseMetadataRejectsUnboundedOrStructurallyUnsafeValues()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HttpResponseMetadata(
            null, null, -1, null, null, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => new HttpResponseMetadata(
            "text/plain\r\nset-cookie: secret", null, null, null, null, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => new HttpResponseMetadata(
            new string('x', HttpResponseMetadata.MaximumHeaderValueLength + 1),
            null,
            null,
            null,
            null,
            null,
            null));
    }

    [TestMethod]
    public void EveryOptionalResponseMetadataFieldIsPresenceAwareOnTheWire()
    {
        var complete = JsonNode.Parse(ContractJson.Serialize(EmptyResponseMetadata()))!.AsObject();
        foreach (var propertyName in complete.Select(static pair => pair.Key).ToArray())
        {
            var missingOne = JsonNode.Parse(complete.ToJsonString())!.AsObject();
            Assert.IsTrue(missingOne.Remove(propertyName));
            Assert.ThrowsExactly<JsonException>(
                () => ContractJson.Deserialize<HttpResponseMetadata>(missingOne.ToJsonString()),
                propertyName);
        }
    }

    [TestMethod]
    public void RequestEvidenceCarriesOnlyTheBoundedR2CausalInputs()
    {
        var request = RequestEvidence();
        using var document = JsonDocument.Parse(ContractJson.Serialize(request));
        CollectionAssert.AreEquivalent(
            new[]
            {
                "requested_uri",
                "method",
                "observed_at_utc",
                "timestamp_precision",
                "clock_source",
                "run_identity",
                "adapter_identity",
                "request_policy_identity",
                "outbound_crawler_identity",
                "origin",
                "query_plan_identity",
            },
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());

        string[] forbidden =
        [
            "Headers", "Cookies", "Addresses", "Credentials", "Query", "Sparql",
            "UserText", "Ip", "InboundUserAgent", "RedirectHistory",
        ];
        var publicMembers = typeof(HttpRequestEvidence)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();
        Assert.IsFalse(forbidden.Any(publicMembers.Contains));

        var roundTrip = ContractJson.Deserialize<HttpRequestEvidence>(ContractJson.Serialize(request));
        Assert.AreEqual(request, roundTrip);
        Assert.AreEqual(OutboundCrawlerIdentity.Schema, request.OutboundCrawlerIdentity.Schema);
        Assert.AreEqual(OutboundCrawlerIdentity.Token, request.OutboundCrawlerIdentity.Token);
    }

    [TestMethod]
    public void RequestEvidenceRejectsOriginTimeIdentityAndCrawlerDrift()
    {
        var valid = RequestEvidence();
        Assert.IsNotNull(valid);
        Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(
            observedAtUtc: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(1))));
        Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(
            origin: new HttpOrigin("https", "op.europa.eu", 443)));
        Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(
            requestedUri: "https://publications.europa.eu:443/sparql?query=secret"));
        Assert.ThrowsExactly<ArgumentException>(() => new OutboundCrawlerIdentityEvidence(
            "outbound_crawler_identity/2",
            Artifact("urn:uuid:77777777-7777-4777-8777-777777777777", '7'),
            OutboundCrawlerIdentity.Token));
        Assert.ThrowsExactly<ArgumentException>(() => new OutboundCrawlerIdentityEvidence(
            OutboundCrawlerIdentity.Schema,
            Artifact("urn:uuid:77777777-7777-4777-8777-777777777777", '7'),
            "caller override"));
    }

    [TestMethod]
    public void EveryRequestEvidenceMemberIsRequiredAndUnknownMembersFail()
    {
        var complete = JsonNode.Parse(ContractJson.Serialize(RequestEvidence()))!.AsObject();
        foreach (var propertyName in complete.Select(static pair => pair.Key).ToArray())
        {
            var missingOne = JsonNode.Parse(complete.ToJsonString())!.AsObject();
            Assert.IsTrue(missingOne.Remove(propertyName));
            Assert.ThrowsExactly<JsonException>(
                () => ContractJson.Deserialize<HttpRequestEvidence>(missingOne.ToJsonString()),
                propertyName);
        }

        var extra = JsonNode.Parse(complete.ToJsonString())!.AsObject();
        extra["headers"] = new JsonObject();
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<HttpRequestEvidence>(extra.ToJsonString()));
    }

    [TestMethod]
    public void OriginIsAnExactDnsTupleAndNeverAnAddress()
    {
        Assert.AreEqual(
            new HttpOrigin("https", "publications.europa.eu", 443),
            new HttpOrigin("https", "publications.europa.eu", 443));

        foreach (var (scheme, host) in new[]
        {
            ("HTTPS", "publications.europa.eu"),
            ("https", "Publications.europa.eu"),
            ("https", "127.0.0.1"),
            ("https", "evil..example"),
            ("https", "-evil.example"),
            ("https", "evil-.example"),
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new HttpOrigin(scheme, host, 443));
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new HttpOrigin("https", "evil.example", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new HttpOrigin("https", "evil.example", 65536));
    }

    private static HttpRequestEvidence RequestEvidence(
        string requestedUri = "https://publications.europa.eu:443/resource/cellar",
        DateTimeOffset? observedAtUtc = null,
        HttpOrigin? origin = null) =>
        new(
            requestedUri: requestedUri,
            method: RegistryMember("GET"),
            observedAtUtc: observedAtUtc ?? new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            timestampPrecision: RegistryMember("millisecond"),
            clockSource: RegistryMember("system_utc"),
            runIdentity: Artifact("urn:uuid:11111111-1111-4111-8111-111111111111", '1'),
            adapterIdentity: Artifact("urn:uuid:22222222-2222-4222-8222-222222222222", '2'),
            requestPolicyIdentity: Artifact("urn:uuid:33333333-3333-4333-8333-333333333333", '3'),
            outboundCrawlerIdentity: new OutboundCrawlerIdentityEvidence(
                OutboundCrawlerIdentity.Schema,
                Artifact("urn:uuid:44444444-4444-4444-8444-444444444444", '4'),
                OutboundCrawlerIdentity.Token),
            origin: origin ?? new HttpOrigin("https", "publications.europa.eu", 443),
            queryPlanIdentity: Artifact("urn:uuid:55555555-5555-4555-8555-555555555555", '5'));

    private static SourceRegistryMemberRef RegistryMember(string memberKey) =>
        new(Artifact("urn:uuid:66666666-6666-4666-8666-666666666666", '6'), memberKey);

    private static SourceArtifactRef Artifact(string resourceId, char digestCharacter) =>
        new(resourceId, new string(digestCharacter, 64));

    private static HttpResponseMetadata EmptyResponseMetadata() =>
        new(null, null, null, null, null, null, null);
}
