using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class BoundedHttpObservationAcquirerTests
{
    private static readonly byte[] EntityBytes = Encoding.UTF8.GetBytes("publisher entity bytes");

    [TestMethod]
    public void UnsafeFetcherIsNotPublicBeforeAnExecutableRequestPolicyExists()
    {
        Assert.IsFalse(typeof(BoundedHttpObservationAcquirer).IsPublic);
    }

    [TestMethod]
    public void DedicatedHandlerPreservesEntityCodingAndRedirectEvidence()
    {
        using var handler = BoundedHttpObservationAcquirer.CreateHandler();

        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.AreEqual(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.IsFalse(handler.UseCookies);
        Assert.IsFalse(handler.UseProxy);
        Assert.IsNull(handler.ActivityHeadersPropagator);
        Assert.AreEqual(0, handler.MaxResponseDrainSize);
    }

    [TestMethod]
    public async Task DeclaredLengthHttp200IsHeldAndRestoredBeforeObservationIsIssued()
    {
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent(EntityBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            content.Headers.ContentLength = EntityBytes.Length;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
        });
        var custody = new RecordingCustodyStore();
        using var acquirer = new BoundedHttpObservationAcquirer(
            transport,
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout: TimeSpan.FromSeconds(5));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var observation = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(observation);
        Assert.AreEqual(HttpStatusDisposition.DerivableStatus, observation.StatusDisposition);
        Assert.AreEqual(EntityBytes.Length, observation.ReceivedEncodedEntityByteCount);
        Assert.AreEqual("https://data.legilux.public.lu/example.xml", observation.EffectiveUri);
        Assert.IsInstanceOfType<DeclaredContentLengthCompleteEvidence>(
            observation.TransferCompletionEvidence);
        Assert.AreEqual(
            observation.ObservationId,
            observation.TransferCompletionEvidence.ResponseObservationId);
        Assert.AreEqual(
            observation.Request.AdapterIdentity,
            observation.TransferCompletionEvidence.AdapterExecutionIdentity);
        Assert.AreEqual(
            CustodyDigest.Of(EntityBytes),
            observation.TransferCompletionEvidence.TransportByteSha256);
        Assert.AreEqual(
            observation.DurableWriteReceipt.Reference,
            observation.DurableBlobRef);
        Assert.AreEqual(
            EntityBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((SingleHttpHeader)observation.ResponseMetadata.ContentLength).Value);
        CollectionAssert.AreEqual(EntityBytes, custody.CreatedBytes);
        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(1, custody.ReadCount);
        Assert.AreEqual(HttpMethod.Get, transport.LastMethod);
        Assert.AreEqual("https://data.legilux.public.lu/example.xml", transport.LastUri);
        Assert.AreEqual(HttpVersion.Version11, transport.LastVersion);
        Assert.AreEqual(OutboundCrawlerIdentity.Token, transport.LastUserAgent);
        Assert.AreEqual(string.Empty, transport.LastAccept);
    }

    [TestMethod]
    public async Task DeclaredZeroLengthIsACompleteEmptyEntityWithoutCustody()
    {
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = 0;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
        });
        var custody = new RecordingCustodyStore();
        using var acquirer = new BoundedHttpObservationAcquirer(
            transport,
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout: TimeSpan.FromSeconds(5));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var observation = result as ResponseWithoutBodyObservation;
        Assert.IsNotNull(observation);
        Assert.AreEqual(HttpNoBodyReason.CompleteZeroOctetEntity, observation.Reason);
        Assert.IsInstanceOfType<DeclaredZeroOctetContentLengthCompleteEvidence>(
            observation.ZeroOctetCompletionEvidence);
        Assert.AreEqual(
            observation.ObservationId,
            observation.ZeroOctetCompletionEvidence.ResponseObservationId);
        Assert.AreEqual(0, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    [DataRow(204, HttpNoBodyReason.FramingForbidsBody)]
    [DataRow(205, HttpNoBodyReason.SemanticNoEntity)]
    public async Task NoBodyStatusIsDistinctFromAnEmptyEntity(
        int statusCode,
        HttpNoBodyReason expectedReason)
    {
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = 0;
            return new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                RequestMessage = request,
                Content = content,
            };
        });
        var custody = new RecordingCustodyStore();
        using var acquirer = new BoundedHttpObservationAcquirer(
            transport,
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout: TimeSpan.FromSeconds(5));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var observation = result as ResponseWithoutBodyObservation;
        Assert.IsNotNull(observation);
        Assert.AreEqual(statusCode, observation.StatusCode);
        Assert.AreEqual(expectedReason, observation.Reason);
        Assert.IsNull(observation.ZeroOctetCompletionEvidence);
        Assert.AreEqual(0, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task Status204WithTransferCodingRemainsSemanticNoBody()
    {
        var transport = new RecordingHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([]),
            };
            Assert.IsTrue(response.Headers.TryAddWithoutValidation(
                "Transfer-Encoding",
                "chunked"));
            return response;
        });
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(transport, custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var withoutBody = result as ResponseWithoutBodyObservation;
        Assert.IsNotNull(withoutBody);
        Assert.AreEqual(
            "chunked",
            Assert.IsInstanceOfType<SingleHttpHeader>(
                withoutBody.ResponseMetadata.TransferEncoding).Value);
        Assert.AreEqual(HttpNoBodyReason.FramingForbidsBody, withoutBody.Reason);
        Assert.AreEqual(0, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    [DataRow(203, HttpStatusDisposition.NonDerivableStatus)]
    [DataRow(302, HttpStatusDisposition.RedirectObserved)]
    [DataRow(404, HttpStatusDisposition.NonDerivableStatus)]
    public async Task CompleteNonAnswerBodiesAreRetainedAsEvidence(
        int statusCode,
        HttpStatusDisposition expectedDisposition)
    {
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent(EntityBytes);
            content.Headers.ContentLength = EntityBytes.Length;
            return new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                RequestMessage = request,
                Content = content,
            };
        });
        var custody = new RecordingCustodyStore();
        using var acquirer = new BoundedHttpObservationAcquirer(
            transport,
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout: TimeSpan.FromSeconds(5));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var observation = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(observation);
        Assert.AreEqual(statusCode, observation.StatusCode);
        Assert.AreEqual(expectedDisposition, observation.StatusDisposition);
        CollectionAssert.AreEqual(EntityBytes, custody.CreatedBytes);
        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(1, custody.ReadCount);
    }

    [TestMethod]
    public async Task ContentCodingBytesAreHeldWithoutAutomaticDecompression()
    {
        byte[] codedBytes = [0x1f, 0x8b, 0x08, 0x00, 0xde, 0xad, 0xbe, 0xef];
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent(codedBytes);
            content.Headers.ContentLength = codedBytes.Length;
            content.Headers.ContentEncoding.Add("gzip");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
        });
        var custody = new RecordingCustodyStore();
        using var acquirer = new BoundedHttpObservationAcquirer(
            transport,
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout: TimeSpan.FromSeconds(5));

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var observation = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(observation);
        CollectionAssert.AreEqual(codedBytes, custody.CreatedBytes);
        Assert.AreEqual("gzip", ((SingleHttpHeader)observation.ResponseMetadata.ContentEncoding).Value);
        Assert.IsTrue(observation.ResponseMetadata.BlocksDerivation);
    }

    [TestMethod]
    public async Task ConflictingCharsetParametersRemainMultipleAndBlockDerivation()
    {
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent(EntityBytes);
            content.Headers.ContentLength = EntityBytes.Length;
            Assert.IsTrue(content.Headers.TryAddWithoutValidation(
                "Content-Type",
                "text/html; charset=utf-8; charset=iso-8859-1"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
        });
        var custody = new RecordingCustodyStore();
        using var acquirer = Acquirer(transport, custody);

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var observation = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(observation);
        var charsets = observation.ResponseMetadata.DeclaredCharset as MultipleHttpHeader;
        Assert.IsNotNull(charsets);
        CollectionAssert.AreEqual(
            new[] { "utf-8", "iso-8859-1" },
            charsets.Values.ToArray());
        Assert.IsTrue(observation.ResponseMetadata.BlocksDerivation);
    }

    [TestMethod]
    public async Task DuplicateAllowlistedHeadersRetainCardinalityAndBlockDerivation()
    {
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent(EntityBytes);
            content.Headers.ContentLength = EntityBytes.Length;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
            Assert.IsTrue(response.Headers.TryAddWithoutValidation(
                "ETag",
                new[] { "\"first\"", "\"second\"" }));
            return response;
        });
        using var acquirer = Acquirer(transport, new RecordingCustodyStore());

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var observation = result as ResponseCompleteBodyObservation;
        Assert.IsNotNull(observation);
        var etags = observation.ResponseMetadata.Etag as MultipleHttpHeader;
        Assert.IsNotNull(etags);
        CollectionAssert.AreEqual(
            new[] { "\"first\"", "\"second\"" },
            etags.Values.ToArray());
        Assert.IsTrue(observation.ResponseMetadata.BlocksDerivation);
    }

    [TestMethod]
    public async Task UnallowlistedResponseHeadersNeverEnterObservation()
    {
        const string secret = "super-secret-cookie-value";
        var transport = new RecordingHandler(request =>
        {
            var content = new ByteArrayContent(EntityBytes);
            content.Headers.ContentLength = EntityBytes.Length;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            };
            Assert.IsTrue(response.Headers.TryAddWithoutValidation("Set-Cookie", $"session={secret}"));
            Assert.IsTrue(response.Headers.TryAddWithoutValidation("X-Debug-Secret", secret));
            return response;
        });
        using var acquirer = Acquirer(transport, new RecordingCustodyStore());

        var result = await acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None);

        var serialized = ContractJson.Serialize(result);
        Assert.IsFalse(serialized.Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("X-Debug-Secret", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task OversizedOrExcessiveAllowlistedHeadersFailBeforeCustody()
    {
        foreach (var responseFactory in new Func<HttpRequestMessage, HttpResponseMessage>[]
                 {
                     request => ResponseWithHeader(
                         request,
                         "Content-Type",
                         new string('x', HttpResponseMetadata.MaximumHeaderValueLength + 1)),
                     request => ResponseWithHeader(
                         request,
                         "ETag",
                         Enumerable.Range(0, HttpResponseMetadata.MaximumHeaderOccurrences + 1)
                             .Select(index => $"\"{index}\"")
                             .ToArray()),
                 })
        {
            var custody = new RecordingCustodyStore();
            using var acquirer = Acquirer(new RecordingHandler(responseFactory), custody);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None));

            Assert.AreEqual(0, custody.CreateCount);
            Assert.AreEqual(0, custody.ReadCount);
        }
    }

    [TestMethod]
    public async Task PostEvidenceFailsBeforeNetworkOrCustody()
    {
        var transport = new RecordingHandler(_ =>
            throw new AssertFailedException("POST evidence reached the network."));
        var custody = new RecordingCustodyStore();
        using var acquirer = new BoundedHttpObservationAcquirer(
            transport,
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout: TimeSpan.FromSeconds(5));

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            acquirer.AcquireAsync(
                RequestEvidence(HttpRequestMethod.Post),
                CancellationToken.None));

        Assert.AreEqual(0, transport.SendCount);
        Assert.AreEqual(0, custody.CreateCount);
    }

    [TestMethod]
    public async Task CustodyReceiptSubstitutionCannotIssueAnObservation()
    {
        var transport = CompleteResponseHandler();
        var custody = new RecordingCustodyStore
        {
            ReceiptBytesOverride = Encoding.UTF8.GetBytes("substituted receipt"),
        };
        using var acquirer = Acquirer(transport, custody);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None));

        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    [TestMethod]
    public async Task CustodyRestoreSubstitutionCannotIssueAnObservation()
    {
        var transport = CompleteResponseHandler();
        var custody = new RecordingCustodyStore
        {
            ReadBytesOverride = Encoding.UTF8.GetBytes("substituted restore"),
        };
        using var acquirer = Acquirer(transport, custody);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None));

        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(1, custody.ReadCount);
    }

    [TestMethod]
    public async Task CustodyStoreCannotMutateThePublisherBytesIntoItsOwnTruth()
    {
        var transport = CompleteResponseHandler();
        var custody = new RecordingCustodyStore
        {
            MutateInputInPlace = true,
        };
        using var acquirer = Acquirer(transport, custody);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            acquirer.AcquireAsync(RequestEvidence(), CancellationToken.None));

        Assert.AreEqual(1, custody.CreateCount);
        Assert.AreEqual(0, custody.ReadCount);
    }

    private static BoundedHttpObservationAcquirer Acquirer(
        HttpMessageHandler transport,
        ICustodyStore custody) => new(
            transport,
            custody,
            maximumResponseBytes: 1024,
            headersTimeout: TimeSpan.FromSeconds(5),
            bodyTimeout: TimeSpan.FromSeconds(5));

    private static RecordingHandler CompleteResponseHandler() => new(request =>
    {
        var content = new ByteArrayContent(EntityBytes);
        content.Headers.ContentLength = EntityBytes.Length;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = content,
        };
    });

    private static HttpResponseMessage ResponseWithHeader(
        HttpRequestMessage request,
        string name,
        string value) => ResponseWithHeader(request, name, [value]);

    private static HttpResponseMessage ResponseWithHeader(
        HttpRequestMessage request,
        string name,
        IReadOnlyList<string> values)
    {
        var content = new ByteArrayContent(EntityBytes);
        content.Headers.ContentLength = EntityBytes.Length;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = content,
        };
        var added = name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
            ? content.Headers.TryAddWithoutValidation(name, values)
            : response.Headers.TryAddWithoutValidation(name, values);
        Assert.IsTrue(added);
        return response;
    }

    private static HttpRequestEvidence RequestEvidence(
        HttpRequestMethod method = HttpRequestMethod.Get) => new(
        requestedUri: "https://data.legilux.public.lu/example.xml",
        method,
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

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastUserAgent { get; private set; }

        public string? LastAccept { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastUri { get; private set; }

        public Version? LastVersion { get; private set; }

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            LastMethod = request.Method;
            LastUri = request.RequestUri?.AbsoluteUri;
            LastVersion = request.Version;
            LastUserAgent = request.Headers.UserAgent.ToString();
            LastAccept = request.Headers.Accept.ToString();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class RecordingCustodyStore : ICustodyStore
    {
        private DurableBlobWriteReceipt? _receipt;

        public byte[]? ReceiptBytesOverride { get; init; }

        public byte[]? ReadBytesOverride { get; init; }

        public bool MutateInputInPlace { get; init; }

        public byte[] CreatedBytes { get; private set; } = [];

        public int CreateCount { get; private set; }

        public int ReadCount { get; private set; }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            if (MutateInputInPlace &&
                MemoryMarshal.TryGetArray(bytes, out var segment) &&
                segment.Count > 0)
            {
                segment.Array![segment.Offset] ^= 0xff;
            }

            CreatedBytes = bytes.ToArray();
            var receiptBytes = ReceiptBytesOverride ?? CreatedBytes;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                CustodyDigest.Of(receiptBytes),
                receiptBytes.Length,
                custodyClass);
            var observed = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-000000000040"),
                CustodyProtection.LockedTime,
                observed,
                observed.AddDays(91));
            _receipt = new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                policy);
            return Task.FromResult(_receipt);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            Assert.AreEqual(_receipt?.Reference, reference);
            return Task.FromResult<ReadOnlyMemory<byte>>(
                (ReadBytesOverride ?? CreatedBytes).ToArray());
        }
    }
}
