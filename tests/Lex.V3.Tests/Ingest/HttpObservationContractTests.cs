using System.Text.Json;
using Lex.V3.Contracts;
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
            (new(true, true, true, 200, 1), HttpObservationKind.PolicyRejection),
            (new(false, false, false, null, 0), HttpObservationKind.TransportFailureBeforeBody),
            (new(false, true, false, 200, 0), HttpObservationKind.ResponsePartialBody),
            (new(false, true, true, 304, 0), HttpObservationKind.Revalidation304),
            (new(false, true, true, 204, 0), HttpObservationKind.ResponseWithoutBody),
            (new(false, true, true, 200, 0), HttpObservationKind.ResponseWithoutBody),
            (new(false, true, true, 200, 1), HttpObservationKind.ResponseCompleteBody),
        ];

        foreach (var (facts, expected) in cases)
        {
            Assert.AreEqual(expected, HttpTransferClassifier.Classify(facts), facts.ToString());
        }
    }

    [TestMethod]
    public void ObservationKindsAreExactlyTheSixR2VariantsAndZeroIsInvalid()
    {
        string[] expected =
        [
            nameof(HttpObservationKind.ResponseCompleteBody),
            nameof(HttpObservationKind.ResponsePartialBody),
            nameof(HttpObservationKind.Revalidation304),
            nameof(HttpObservationKind.ResponseWithoutBody),
            nameof(HttpObservationKind.TransportFailureBeforeBody),
            nameof(HttpObservationKind.PolicyRejection),
        ];

        CollectionAssert.AreEquivalent(expected, Enum.GetNames<HttpObservationKind>());
        Assert.IsFalse(Enum.IsDefined((HttpObservationKind)0));
    }

    [TestMethod]
    public void StatusClassificationKeepsTransportEvidenceSeparateFromDerivability()
    {
        (int Status, bool HasContentRange, HttpStatusDisposition Expected)[] cases =
        [
            (200, false, HttpStatusDisposition.DerivableStatus),
            (200, true, HttpStatusDisposition.RangeNotApproved),
            (206, false, HttpStatusDisposition.RangeNotApproved),
            (301, false, HttpStatusDisposition.RedirectObserved),
            (302, false, HttpStatusDisposition.RedirectObserved),
            (303, false, HttpStatusDisposition.RedirectObserved),
            (307, false, HttpStatusDisposition.RedirectObserved),
            (308, false, HttpStatusDisposition.RedirectObserved),
            (304, false, HttpStatusDisposition.RevalidationReferenceOnly),
            (204, false, HttpStatusDisposition.SemanticNoEntityStatus),
            (205, false, HttpStatusDisposition.SemanticNoEntityStatus),
            (201, false, HttpStatusDisposition.NonDerivableStatus),
            (203, false, HttpStatusDisposition.NonDerivableStatus),
            (404, false, HttpStatusDisposition.NonDerivableStatus),
            (500, false, HttpStatusDisposition.NonDerivableStatus),
        ];

        foreach (var (status, hasContentRange, expected) in cases)
        {
            Assert.AreEqual(
                expected,
                HttpStatusClassifier.Classify(status, hasContentRange),
                $"status={status}, content-range={hasContentRange}");
        }
    }

    [TestMethod]
    public void TheOutboundCrawlerIdentityIsOneExactNonOverrideableToken()
    {
        Assert.AreEqual(
            "Lex/0.1 (+https://github.com/SFHAJJI/lex)",
            OutboundCrawlerIdentity.Token);

        var property = typeof(OutboundCrawlerIdentity).GetProperty(
            nameof(OutboundCrawlerIdentity.Token));
        Assert.IsNotNull(property);
        Assert.IsNull(property.SetMethod);
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
}
