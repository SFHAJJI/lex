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
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new SingleHttpHeader("bytes 0-0/1"),
            new AbsentHttpHeader(),
            new AbsentHttpHeader());

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
    public void ResponseMetadataIsExactlyTheSevenFieldAllowlistWithExplicitAbsence()
    {
        var metadata = EmptyResponseMetadata();

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
        Assert.IsTrue(properties.All(static property =>
            property.Value.GetProperty("cardinality").GetString() == "absent"));

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
        Assert.ThrowsExactly<ArgumentException>(() => new SingleHttpHeader(
            "text/plain\r\nset-cookie: secret"));
        Assert.ThrowsExactly<ArgumentException>(() => new SingleHttpHeader(
            new string('x', HttpResponseMetadata.MaximumHeaderValueLength + 1)));
        Assert.ThrowsExactly<ArgumentException>(() => new MultipleHttpHeader(
            Enumerable.Repeat("value", HttpResponseMetadata.MaximumHeaderOccurrences + 1).ToArray()));
    }

    [TestMethod]
    public void ResponseMetadataPreservesAbsentSingleAndMultipleHeaderEvidence()
    {
        var metadata = new HttpResponseMetadata(
            new AbsentHttpHeader(),
            new SingleHttpHeader("utf-8"),
            new MultipleHttpHeader(["1", "1"]),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new SingleHttpHeader("\"opaque\""),
            new AbsentHttpHeader());

        var json = ContractJson.Serialize(metadata);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(
            "absent",
            document.RootElement.GetProperty("content_type").GetProperty("cardinality").GetString());
        Assert.AreEqual(
            "single",
            document.RootElement.GetProperty("declared_charset").GetProperty("cardinality").GetString());
        Assert.AreEqual(
            "multiple",
            document.RootElement.GetProperty("content_length").GetProperty("cardinality").GetString());
        CollectionAssert.AreEqual(
            new[] { "1", "1" },
            document.RootElement.GetProperty("content_length").GetProperty("values")
                .EnumerateArray().Select(static value => value.GetString()).ToArray());

        var roundTrip = ContractJson.Deserialize<HttpResponseMetadata>(json);
        Assert.IsInstanceOfType<AbsentHttpHeader>(roundTrip.ContentType);
        Assert.IsInstanceOfType<SingleHttpHeader>(roundTrip.DeclaredCharset);
        Assert.IsInstanceOfType<MultipleHttpHeader>(roundTrip.ContentLength);

        Assert.ThrowsExactly<ArgumentException>(() => new MultipleHttpHeader(["only-one"]));
        Assert.ThrowsExactly<ArgumentException>(() => new SingleHttpHeader("bad\r\nvalue"));
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
                "representation_request_key_identity",
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
        using var crawlerDocument = JsonDocument.Parse(
            ContractJson.Serialize(request.OutboundCrawlerIdentity));
        CollectionAssert.AreEquivalent(
            new[] { "schema", "token" },
            crawlerDocument.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());
    }

    [TestMethod]
    public void RequestEvidenceRejectsOriginTimeIdentityAndCrawlerDrift()
    {
        var valid = RequestEvidence();
        Assert.IsNotNull(valid);
        Assert.AreEqual(HttpRequestMethod.Get, valid.Method);
        Assert.AreEqual(HttpObservationTimestampPrecision.Millisecond, valid.TimestampPrecision);
        Assert.AreEqual(HttpObservationClockSource.SystemUtc, valid.ClockSource);
        Assert.AreEqual("2026-09-01T00:00:00.000Z", valid.ObservedAtUtc);

        var legalNotice = RequestEvidence(
            requestedUri: "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en",
            origin: new HttpOrigin("https", "eur-lex.europa.eu", 443));
        Assert.AreEqual(
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en",
            legalNotice.RequestedUri);

        foreach (var encodedLiteral in new[]
        {
            "https://publications.europa.eu/resource/a%20b",
            "https://publications.europa.eu/resource/a%2520b",
        })
        {
            Assert.AreEqual(encodedLiteral, RequestEvidence(requestedUri: encodedLiteral).RequestedUri);
        }

        Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(
            observedAtUtc: "2026-09-01T00:00:00.000-00:00"));
        Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(
            observedAtUtc: "2026-09-01T00:00:00Z"));
        Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(
            origin: new HttpOrigin("https", "op.europa.eu", 443)));
        Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(
            requestedUri: "https://publications.europa.eu/sparql?query=secret"));
        Assert.ThrowsExactly<ArgumentException>(() => new OutboundCrawlerIdentityEvidence(
            "outbound_crawler_identity/2",
            OutboundCrawlerIdentity.Token));
        Assert.ThrowsExactly<ArgumentException>(() => new OutboundCrawlerIdentityEvidence(
            OutboundCrawlerIdentity.Schema,
            "caller override"));

        foreach (var alias in new[]
        {
            "https://publications.europa.eu:443/resource/cellar",
            "https://publications.europa.eu:0443/resource/cellar",
            "https://PUBLICATIONS.EUROPA.EU/resource/cellar",
            "https://publications.europa.eu/a/../resource/cellar",
            "https://publications.europa.eu/a/%2e%2e/resource/cellar",
            "https://publications.europa.eu\\resource\\cellar",
            "https://@publications.europa.eu/resource/cellar",
            "https://publications.europa.eu/a/%2f..%2f/b",
            "https://publications.europa.eu/a/%5c..%5c/b",
            "https://publications.europa.eu/a/%252e%252e/b",
            "https://publications.europa.eu/a/%25%2532%2565%25%2532%2565/b",
            "https://publications.europa.eu/a/%25%2532%2566/b",
            "https://publications.europa.eu/a/%25%2535%2563/b",
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() => RequestEvidence(requestedUri: alias), alias);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RequestEvidence(
            method: (HttpRequestMethod)0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RequestEvidence(
            timestampPrecision: (HttpObservationTimestampPrecision)0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RequestEvidence(
            clockSource: (HttpObservationClockSource)0));

        var hostile = JsonNode.Parse(ContractJson.Serialize(valid))!.AsObject();
        foreach (var (propertyName, value) in new[]
        {
            ("method", "can_i_be_fired_while_sick"),
            ("timestamp_precision", "arbitrary_precision"),
            ("clock_source", "caller_clock"),
        })
        {
            var mutated = JsonNode.Parse(hostile.ToJsonString())!.AsObject();
            mutated[propertyName] = value;
            Assert.ThrowsExactly<JsonException>(
                () => ContractJson.Deserialize<HttpRequestEvidence>(mutated.ToJsonString()),
                propertyName);
        }

        var crawlerWithForgedIdentity = JsonNode.Parse(
            ContractJson.Serialize(valid.OutboundCrawlerIdentity))!.AsObject();
        crawlerWithForgedIdentity["identity_ref"] = JsonNode.Parse(
            ContractJson.Serialize(Artifact("urn:uuid:77777777-7777-4777-8777-777777777777", '7')));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<OutboundCrawlerIdentityEvidence>(
                crawlerWithForgedIdentity.ToJsonString()));
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
    public void RequestSemanticVocabulariesAreClosedAndStringEncoded()
    {
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            Enum.GetValues<HttpRequestMethod>().Select(static value => (int)value).ToArray());
        Assert.AreEqual("\"GET\"", ContractJson.Serialize(HttpRequestMethod.Get));
        Assert.AreEqual("\"POST\"", ContractJson.Serialize(HttpRequestMethod.Post));
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpRequestMethod>("0"));
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<HttpRequestMethod>("\"HEAD\""));

        CollectionAssert.AreEqual(
            new[] { 1 },
            Enum.GetValues<HttpObservationTimestampPrecision>()
                .Select(static value => (int)value)
                .ToArray());
        Assert.AreEqual(
            "\"millisecond\"",
            ContractJson.Serialize(HttpObservationTimestampPrecision.Millisecond));

        CollectionAssert.AreEqual(
            new[] { 1 },
            Enum.GetValues<HttpObservationClockSource>().Select(static value => (int)value).ToArray());
        Assert.AreEqual(
            "\"system_utc\"",
            ContractJson.Serialize(HttpObservationClockSource.SystemUtc));
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
        string requestedUri = "https://publications.europa.eu/resource/cellar",
        string observedAtUtc = "2026-09-01T00:00:00.000Z",
        HttpOrigin? origin = null,
        HttpRequestMethod method = HttpRequestMethod.Get,
        HttpObservationTimestampPrecision timestampPrecision = HttpObservationTimestampPrecision.Millisecond,
        HttpObservationClockSource clockSource = HttpObservationClockSource.SystemUtc) =>
        new(
            requestedUri: requestedUri,
            method: method,
            observedAtUtc: observedAtUtc,
            timestampPrecision: timestampPrecision,
            clockSource: clockSource,
            runIdentity: Artifact("urn:uuid:11111111-1111-4111-8111-111111111111", '1'),
            adapterIdentity: Artifact("urn:uuid:22222222-2222-4222-8222-222222222222", '2'),
            requestPolicyIdentity: Artifact("urn:uuid:33333333-3333-4333-8333-333333333333", '3'),
            representationRequestKeyIdentity: Artifact(
                "urn:uuid:44444444-4444-4444-8444-444444444444", '4'),
            outboundCrawlerIdentity: new OutboundCrawlerIdentityEvidence(
                OutboundCrawlerIdentity.Schema,
                OutboundCrawlerIdentity.Token),
            origin: origin ?? new HttpOrigin("https", "publications.europa.eu", 443),
            queryPlanIdentity: Artifact("urn:uuid:55555555-5555-4555-8555-555555555555", '5'));

    private static SourceRegistryMemberRef RegistryMember(string memberKey) =>
        new(Artifact("urn:uuid:66666666-6666-4666-8666-666666666666", '6'), memberKey);

    private static SourceArtifactRef Artifact(string resourceId, char digestCharacter) =>
        new(resourceId, new string(digestCharacter, 64));

    private static HttpResponseMetadata EmptyResponseMetadata() =>
        new(
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new AbsentHttpHeader(),
            new AbsentHttpHeader());
}
