using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class RoutedHttpLogicalRequestTests
{
    private const string RequestPolicy =
        "1111111111111111111111111111111111111111111111111111111111111111";
    private const string RedirectPolicy =
        "2222222222222222222222222222222222222222222222222222222222222222";
    private const string BodyDigest =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestMethod]
    public void CanonicalBytesUseTheExactSuccessorShapeAndRoundTripFromTypedValues()
    {
        var request = Request();
        var expected =
            "{\"schema\":\"lex-http-logical-request/1\",\"uri\":\"https://publications.europa.eu/webapi/rdf/sparql\",\"method\":\"POST\",\"headers\":[{\"name\":\"user-agent\",\"value\":\"Lex/0.1 (+https://github.com/SFHAJJI/lex)\"},{\"name\":\"content-type\",\"value\":\"application/sparql-query; charset=utf-8\"},{\"name\":\"x-note\",\"value\":\"droit-é\\u0009\\\"\\\\\"}],\"body\":{\"length\":42,\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"},\"requested_http_version\":\"http/1.1\",\"version_policy\":\"request_version_exact\",\"request_policy_sha256\":\"1111111111111111111111111111111111111111111111111111111111111111\",\"redirect_policy_sha256\":\"2222222222222222222222222222222222222222222222222222222222222222\"}\n";

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), request.CopyCanonicalBytes());

        var reopened = HttpLogicalRequest.ParseAndVerify(request.CopyCanonicalBytes());
        Assert.AreEqual(request.Uri, reopened.Uri);
        Assert.AreEqual(request.Method, reopened.Method);
        Assert.AreEqual(request.Body, reopened.Body);
        CollectionAssert.AreEqual(request.Headers.ToArray(), reopened.Headers.ToArray());
        CollectionAssert.AreEqual(request.CopyCanonicalBytes(), reopened.CopyCanonicalBytes());
    }

    [TestMethod]
    public void ReaderRebuildsFromTheTypedValueAndRejectsEveryNoncanonicalSpelling()
    {
        var canonical = Encoding.UTF8.GetString(Request().CopyCanonicalBytes());
        foreach (var mutation in new[]
        {
            canonical.Replace("\":\"lex-http", "\": \"lex-http", StringComparison.Ordinal),
            canonical.Replace("\"schema\":\"lex-http-logical-request/1\",\"uri\"", "\"uri\":\"https://publications.europa.eu/webapi/rdf/sparql\",\"schema\"", StringComparison.Ordinal),
            canonical.Replace("\"schema\":", "\"schema\":\"lex-http-logical-request/1\",\"schema\":", StringComparison.Ordinal),
            canonical.Replace("\"headers\":", "\"extra\":0,\"headers\":", StringComparison.Ordinal),
            canonical.Replace("\"length\":42", "\"length\":042", StringComparison.Ordinal),
            canonical.Replace("droit-é", "droit-\\u00e9", StringComparison.Ordinal),
            canonical.Replace("\\u0009", "\\t", StringComparison.Ordinal),
            canonical.Replace("droit-é", "droit-\\uD800", StringComparison.Ordinal),
            "\uFEFF" + canonical,
            canonical.Replace("}\n", "}\r\n", StringComparison.Ordinal),
            canonical.TrimEnd('\n'),
            canonical + "\n",
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpLogicalRequest.ParseAndVerify(Encoding.UTF8.GetBytes(mutation)));
        }
    }

    [TestMethod]
    public void StructureRefusesSideChannelsAliasesAndCallerSelectedProtocol()
    {
        foreach (var name in new[]
        {
            "authorization", "proxy-authorization", "cookie", "forwarded", "expect",
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
                "https://publications.europa.eu/webapi/rdf/sparql",
                HttpRequestMethod.Get,
                [new HttpLogicalRequestHeader(name, "value")],
                new HttpLogicalRequestBody(0, EmptyDigest),
                RequestPolicy,
                RedirectPolicy));
        }

        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://PUBLICATIONS.europa.eu/webapi/rdf/sparql",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://user@publications.europa.eu/webapi/rdf/sparql",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu:443/webapi/rdf/sparql",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://127.0.0.1/webapi/rdf/sparql",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/webapi/rdf/sparql#fragment",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/a/../webapi/rdf/sparql",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/webapi/rdf/sparql",
            (HttpRequestMethod)0,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
    }

    [TestMethod]
    public void GetHasTheEmptyBodyAndPostHasAPositiveBody()
    {
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/robots.txt",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(1, BodyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/webapi/rdf/sparql",
            HttpRequestMethod.Post,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/robots.txt",
            HttpRequestMethod.Get,
            [.. Headers, new("content-type", "text/plain")],
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy));
    }

    [TestMethod]
    public void PostRequiresExactlyOneContentType()
    {
        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/webapi/rdf/sparql",
            HttpRequestMethod.Post,
            Headers,
            new HttpLogicalRequestBody(42, BodyDigest),
            RequestPolicy,
            RedirectPolicy));

        Assert.ThrowsExactly<ArgumentException>(() => HttpLogicalRequest.Create(
            "https://publications.europa.eu/webapi/rdf/sparql",
            HttpRequestMethod.Post,
            [
                .. Headers,
                new("content-type", "application/sparql-query; charset=utf-8"),
                new("content-type", "application/sparql-query; charset=utf-8"),
            ],
            new HttpLogicalRequestBody(42, BodyDigest),
            RequestPolicy,
            RedirectPolicy));
    }

    [TestMethod]
    public void CanonicalGetReopensAsGetWithAnEmptyBodyAndNoContentType()
    {
        var request = HttpLogicalRequest.Create(
            "https://publications.europa.eu/robots.txt",
            HttpRequestMethod.Get,
            Headers,
            new HttpLogicalRequestBody(0, EmptyDigest),
            RequestPolicy,
            RedirectPolicy);

        var bytes = request.CopyCanonicalBytes();
        var reopened = HttpLogicalRequest.ParseAndVerify(bytes);
        Assert.AreEqual(HttpRequestMethod.Get, reopened.Method);
        Assert.AreEqual(0UL, reopened.Body.Length);
        Assert.AreEqual(EmptyDigest, reopened.Body.Sha256);
        Assert.IsFalse(reopened.Headers.Any(static header =>
            string.Equals(header.Name, "content-type", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(bytes, reopened.CopyCanonicalBytes());
    }

    private static readonly string EmptyDigest =
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])).ToLowerInvariant();

    private static readonly HttpLogicalRequestHeader[] Headers =
    [
        new("user-agent", "Lex/0.1 (+https://github.com/SFHAJJI/lex)"),
    ];

    private static HttpLogicalRequest Request() => HttpLogicalRequest.Create(
        "https://publications.europa.eu/webapi/rdf/sparql",
        HttpRequestMethod.Post,
        [
            .. Headers,
            new("content-type", "application/sparql-query; charset=utf-8"),
            new("x-note", "droit-é\t\"\\"),
        ],
        new HttpLogicalRequestBody(42, BodyDigest),
        RequestPolicy,
        RedirectPolicy);
}

[TestClass]
public sealed class RoutedHttpResponseHeadersTests
{
    [TestMethod]
    public void HeaderObjectAndUnionHaveTheExactSuccessorShape()
    {
        var headers = Headers(
            contentType: new RoutedHttpSingleHeader("text/plain;charset=UTF-8"),
            cacheControl: new RoutedHttpMultipleHeader(["max-age=60", "must-revalidate"]),
            tcn: new RoutedHttpSingleHeader("choice"));

        var expected =
            "{\"content_type\":{\"kind\":\"single\",\"value\":\"text/plain;charset=UTF-8\"},\"content_length\":{\"kind\":\"absent\"},\"content_encoding\":{\"kind\":\"absent\"},\"transfer_encoding\":{\"kind\":\"absent\"},\"content_range\":{\"kind\":\"absent\"},\"etag\":{\"kind\":\"absent\"},\"last_modified\":{\"kind\":\"absent\"},\"location\":{\"kind\":\"absent\"},\"cache_control\":{\"kind\":\"multiple\",\"values\":[\"max-age=60\",\"must-revalidate\"]},\"expires\":{\"kind\":\"absent\"},\"date\":{\"kind\":\"absent\"},\"age\":{\"kind\":\"absent\"},\"tcn\":{\"kind\":\"single\",\"value\":\"choice\"}}";

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes(expected),
            RoutedHttpCanonicalJson.WriteResponseHeaders(headers));
    }

    [TestMethod]
    public void MultipleValuesAreSnapshottedAndUtf8Bounded()
    {
        string[] source = ["first", "second"];
        var field = new RoutedHttpMultipleHeader(source);
        source[0] = "mutated";
        CollectionAssert.AreEqual(new[] { "first", "second" }, field.Values.ToArray());

        Assert.ThrowsExactly<ArgumentException>(() => new RoutedHttpMultipleHeader(["one"]));
        Assert.ThrowsExactly<ArgumentException>(() => new RoutedHttpMultipleHeader(
            Enumerable.Repeat("value", 17).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(() => new RoutedHttpSingleHeader(
            new string('é', 2049)));
        Assert.ThrowsExactly<ArgumentException>(() => new RoutedHttpSingleHeader("line\nfeed"));
    }

    private static RoutedHttpResponseHeaders Headers(
        RoutedHttpHeaderField? contentType = null,
        RoutedHttpHeaderField? cacheControl = null,
        RoutedHttpHeaderField? tcn = null)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            contentType ?? absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            cacheControl ?? absent,
            absent,
            absent,
            absent,
            tcn ?? absent);
    }
}
