using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The one legal-notice GET per corpus run (Decision 88), produced under the corpus run's own
/// identity. Real production code end to end (<see cref="EuLegalNoticePlan"/>,
/// <see cref="OfficialMachineQuerySourceProfiles"/>, <see cref="RoutedHttpAcquisitionSession"/>,
/// <see cref="EuRepeatedEnumerationExecutor.RunDocumentFetchAsync"/>,
/// <see cref="EuLegalNoticeRouteProducer"/>) driven by a scripted transport, exactly the discipline
/// <see cref="EuDocumentFetchReachabilityTests"/> uses for the Cellar route.
/// </summary>
/// <remarks>
/// The robots answer is the real one: <c>Fixtures/EuLegalNotice/eur-lex-robots-2026-09-27.txt</c>
/// is the body <c>GET https://eur-lex.europa.eu/robots.txt</c> returned on 2026-09-27 (200, 2,475
/// bytes, <b>no Content-Type header</b>, <c>Crawl-delay: 10</c>), re-hashed on every run. The
/// notice page body is synthetic: the real page's bytes are not held in this repository (only their
/// measured digest and length are, in <c>EuLegalNoticeEvidenceTests</c>), and nothing these tests
/// prove depends on the notice prose.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuLegalNoticeRouteProducerTests
{
    private const string NoticeUri = EuLegalNoticeEvidence.RequestedUri;
    private const string RobotsUri = "https://eur-lex.europa.eu/robots.txt";
    private const string RobotsFixtureSha256 = "a5987a744e454da2ba668f26b21ba79b016bb0c13ac42679de95a285e6b7735f";
    private const string NoticeMediaType = "text/html; charset=UTF-8";

    private static readonly byte[] NoticeBody = Encoding.UTF8.GetBytes(
        "<!DOCTYPE html><html lang=\"en\"><head><title>Legal notice</title></head>"
        + "<body><h1>Legal notice</h1><p>Synthetic test body; see the class remarks.</p></body></html>\n");

    [TestMethod]
    public async Task DeliversTheRouteUnderTheCorpusRunIdentityAndTheEnvelopeDoorMintsNoticeEvidenceFromIt()
    {
        var corpusRunIdentity = CorpusRunIdentity();
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            1 => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, NoticeBody, NoticeMediaType),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        var result = await Producer(store, handler).RunAsync(
            corpusRunIdentity, RendererSource(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(result.Refusal, result.Detail);
        var route = result.Route!;
        var request = result.TerminalRequest!;

        // The identity the envelope checks against the corpus records is the corpus run's, and the
        // route is otherwise exactly the session's observation: one hop, the pinned URI, a real 200.
        Assert.AreEqual(corpusRunIdentity, route.RunIdentity);
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(route.Outcome);
        Assert.AreEqual(1, route.Hops.Count);
        Assert.AreEqual(NoticeUri, route.Hops[0].RequestUri);
        Assert.AreEqual(200, route.Hops[0].Status);
        Assert.AreEqual((ulong)NoticeBody.Length, route.Hops[0].Length);
        Assert.AreEqual(Sha256(NoticeBody), route.Hops[0].Sha256);

        // The terminal request is the one the hop actually sent, reopened from custody by digest,
        // and it carried nothing but the crawler identity: this route negotiates nothing.
        Assert.AreEqual(route.Hops[0].LogicalRequestSha256, Sha256(request.CopyCanonicalBytes()));
        Assert.AreEqual(NoticeUri, request.Uri);
        Assert.AreEqual(HttpRequestMethod.Get, request.Method);
        CollectionAssert.AreEqual(new[] { "user-agent" }, request.Headers.Select(static header => header.Name).ToArray());
        Assert.AreEqual(OutboundCrawlerIdentity.Token, request.Headers[0].Value);

        // The envelope's own door accepts exactly this pair.
        var notice = EuLegalNoticeEvidence.FromRoute(route, request);
        Assert.AreEqual(NoticeUri, notice.EffectiveUri);
        Assert.AreEqual((ulong)NoticeBody.Length, notice.ByteLength);
        Assert.AreEqual(Sha256(NoticeBody), notice.Sha256);
        Assert.AreEqual(Sha256(route.CopyCanonicalBytes()), notice.RoutedEvidenceSha256);
        Assert.AreEqual(route.Hops[0].DurableWriteReceiptSha256, notice.DurableWriteReceiptSha256);

        // Decision 78: the corpus-identity route is retained and is what was handed back.
        var retained = await CustodyRestore.ReadByDigestCheckedAsync(
            store, Sha256(route.CopyCanonicalBytes()), CancellationToken.None);
        CollectionAssert.AreEqual(route.CopyCanonicalBytes(), retained.ToArray());

        // Exactly two publisher requests: the robots policy, then the notice.
        CollectionAssert.AreEqual(new[] { RobotsUri, NoticeUri }, handler.Sends.Select(static send => send.Uri).ToArray());
        Assert.IsTrue(handler.Sends.All(static send => send.Method == "GET"));
        Assert.AreEqual(string.Empty, handler.Sends[1].Accept, "the notice GET sends no Accept header.");
        Assert.AreEqual(OutboundCrawlerIdentity.Token, handler.Sends[1].UserAgent);
    }

    /// <summary>
    /// The robots widening is the legal-notice profile's alone. The same publisher shape (a 200
    /// robots answer with no Content-Type header) that the notice route admits above is still
    /// refused for the Cellar document-fetch route, so no other channel's admission moved.
    /// </summary>
    [TestMethod]
    public async Task ARobotsPolicyWithoutAContentTypeIsStillRefusedForTheCellarRoute()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => EuAcquisitionTestFixture.BinaryResponse(
                request, HttpStatusCode.MovedPermanently, [], location: "https://op.europa.eu/robots.txt"),
            1 => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, RobotsFixtureBytes()),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });
        var executor = new EuRepeatedEnumerationExecutor(store, Clock(), handler);
        var witness = EuAcquisitionTestFixture.DocumentFetchSourceWitness();

        var attempt = await executor.RunDocumentFetchAsync(
            witness, witness, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(attempt.Evidence);
        Assert.AreEqual(EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused, attempt.Refusal);
        Assert.AreEqual(2, handler.Sends.Count, "the product request must never be sent.");
    }

    [TestMethod]
    public async Task ARobotsDisallowForTheProductTokenIsATypedRefusalAndTheNoticeIsNeverRequested()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => EuAcquisitionTestFixture.BinaryResponse(
                request, HttpStatusCode.OK, "User-agent: Lex\nDisallow: /\n\nUser-agent: *\nAllow: /\n"u8.ToArray()),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        var result = await Producer(store, handler).RunAsync(
            CorpusRunIdentity(), RendererSource(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(result.Route);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.RobotsBootstrapRefused, result.Refusal);
        Assert.AreEqual(1, handler.Sends.Count);
    }

    /// <summary>
    /// Decision 23 records that EUR-Lex sits behind a WAF challenge for non-browser clients. A
    /// challenge or block page is a real, retained observation and it is not notice evidence: the
    /// route executed, the bytes are held, and the result is a typed refusal carrying FromRoute's
    /// own reason rather than a route the envelope would refuse later.
    /// </summary>
    [TestMethod]
    public async Task AChallengeOrBlockedTerminalIsNoticeRouteInvalidWithTheRefusingReason()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            1 => EuAcquisitionTestFixture.BinaryResponse(
                request, HttpStatusCode.Forbidden, "<html><body>challenge</body></html>"u8.ToArray(), NoticeMediaType),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        var result = await Producer(store, handler).RunAsync(
            CorpusRunIdentity(), RendererSource(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(result.Route);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.NoticeRouteInvalid, result.Refusal);
        StringAssert.Contains(result.Detail, "200");
        Assert.AreEqual(2, handler.Sends.Count);
    }

    /// <summary>
    /// Review finding on this slice: a 200 text/html answer whose body fails part-way is sealed by
    /// the session as an incomplete route carrying the bytes that did arrive, and the executor
    /// reports it executed. Status and media type both survive the truncation, so before
    /// <see cref="EuLegalNoticeEvidence.FromRoute"/> required a complete route this was delivered
    /// as notice evidence with the partial length as its byte count. It is a typed refusal.
    /// </summary>
    [TestMethod]
    public async Task ATruncatedNoticeBodyIsNoticeRouteInvalidNotDeliveredEvidence()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            1 => TruncatedNotice(request),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        var result = await Producer(store, handler).RunAsync(
            CorpusRunIdentity(), RendererSource(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(result.Route, "a body the session sealed as incomplete must not become notice evidence.");
        Assert.AreEqual(EuLegalNoticeRouteRefusal.NoticeRouteInvalid, result.Refusal);
        StringAssert.Contains(result.Detail, "complete route");
        Assert.AreEqual(2, handler.Sends.Count);
    }

    [TestMethod]
    public async Task AnOffOriginRedirectIsNoticeRouteInvalidAndItsTargetIsNeverRequested()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            1 => EuAcquisitionTestFixture.BinaryResponse(
                request, HttpStatusCode.Found, [], location: "https://example.invalid/legal-notice.html"),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        var result = await Producer(store, handler).RunAsync(
            CorpusRunIdentity(), RendererSource(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(result.Route);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.NoticeRouteInvalid, result.Refusal);
        Assert.AreEqual(2, handler.Sends.Count, "an off-origin target is refused before it is sent.");
    }

    [TestMethod]
    public async Task ASameOriginRedirectIsFollowedAndTheTerminalHopIsTheNoticeEvidence()
    {
        const string Terminal = "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en&session=1";
        var corpusRunIdentity = CorpusRunIdentity();
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            1 => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.Found, [], location: Terminal),
            2 => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, NoticeBody, NoticeMediaType),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        var result = await Producer(store, handler).RunAsync(
            corpusRunIdentity, RendererSource(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(result.Refusal, result.Detail);
        var route = result.Route!;
        Assert.AreEqual(corpusRunIdentity, route.RunIdentity);
        Assert.AreEqual(2, route.Hops.Count);
        Assert.AreEqual(NoticeUri, route.Hops[0].RequestUri);
        Assert.AreEqual(302, route.Hops[0].Status);
        Assert.AreEqual(Terminal, route.Hops[1].RequestUri);
        Assert.AreEqual(200, route.Hops[1].Status);

        // The reopened request is the TERMINAL hop's, not the first hop's.
        Assert.AreEqual(Terminal, result.TerminalRequest!.Uri);
        Assert.AreEqual(route.Hops[1].LogicalRequestSha256, Sha256(result.TerminalRequest.CopyCanonicalBytes()));

        var notice = EuLegalNoticeEvidence.FromRoute(route, result.TerminalRequest);
        Assert.AreEqual(Terminal, notice.EffectiveUri);
        CollectionAssert.AreEqual(new[] { RobotsUri, NoticeUri, Terminal }, handler.Sends.Select(static send => send.Uri).ToArray());
    }

    [TestMethod]
    public async Task TheWireCeilingStopsTheNoticeRequestBeforeItIsSent()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        // A budget is at least two (robots plus one product request). One reservation already
        // spent by the run leaves exactly the robots fetch, so the notice request is the one the
        // ceiling stops.
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt());

        var result = await Producer(store, handler).RunAsync(
            CorpusRunIdentity(), RendererSource(), budget, CancellationToken.None);

        Assert.IsNull(result.Route);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.WireBudgetExhausted, result.Refusal);
        Assert.AreEqual(1, handler.Sends.Count);
    }

    /// <summary>
    /// The receipts door itself, through the executor's public result. The receipts an executed
    /// fetch carries are the ones custody issued for its hops: presenting them re-creates the
    /// session's own evidence byte for byte, and presents the same hops under another run identity;
    /// a receipt naming other bytes is refused by <see cref="RoutedHttpEvidence.Create"/>. The
    /// negative half is what proves the door checks rather than trusts.
    /// </summary>
    [TestMethod]
    public async Task TheReceiptsCarriedByAnExecutedFetchAreTheOnesCustodyIssuedAndAForgedOneIsRefused()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            1 => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, NoticeBody, NoticeMediaType),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });
        var executor = new EuRepeatedEnumerationExecutor(store, Clock(), handler);
        var bound = new EuLegalNoticePlan().Bind(NewUrn(), NewUrn(), RendererSource()).Request;

        var attempt = await executor.RunDocumentFetchAsync(
            bound, bound, EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(attempt.Refusal, attempt.Detail);
        var evidence = attempt.Evidence!;
        var receipts = attempt.HopWriteReceiptsByObservationId!;
        Assert.AreEqual(evidence.Hops.Count, receipts.Count);
        var hop = evidence.Hops[^1];
        var receipt = receipts[hop.ObservationId];
        Assert.AreEqual(hop.Sha256, receipt.Reference.ContentSha256);
        Assert.AreEqual((long)hop.Length, receipt.Reference.ByteLength);
        Assert.AreEqual(hop.DurableWriteReceiptSha256, DurableBlobWriteReceiptDigest.Of(receipt));

        // Lossless under the session's own identity...
        var reproduced = RoutedHttpEvidence.Create(
            evidence.RunIdentity, evidence.RequestOrdinal, evidence.AttemptOrdinal, evidence.Hops, evidence.Outcome, receipts);
        CollectionAssert.AreEqual(evidence.CopyCanonicalBytes(), reproduced.CopyCanonicalBytes());

        // ...and re-presentable under the corpus run's.
        var corpusRunIdentity = CorpusRunIdentity();
        var represented = RoutedHttpEvidence.Create(
            corpusRunIdentity, evidence.RequestOrdinal, evidence.AttemptOrdinal, evidence.Hops, evidence.Outcome, receipts);
        Assert.AreEqual(corpusRunIdentity, represented.RunIdentity);
        Assert.AreEqual(evidence.Hops[^1].Sha256, represented.Hops[^1].Sha256);

        // A receipt for other bytes, otherwise well formed, is refused: the door compares the
        // presented receipt to the hop, never the hop to a copy of itself.
        var forgedReference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, Sha256("other bytes"u8), receipt.Reference.ByteLength, receipt.Reference.CustodyClass);
        var forged = new Dictionary<string, DurableBlobWriteReceipt>(receipts, StringComparer.Ordinal)
        {
            [hop.ObservationId] = new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                forgedReference,
                new CustodyPolicyEvidence(
                    CustodySchemaIds.CustodyPolicyEvidence,
                    forgedReference,
                    receipt.PolicyEvidence.VerificationProfile,
                    receipt.PolicyEvidence.PolicyKey,
                    receipt.PolicyEvidence.Protection,
                    receipt.PolicyEvidence.ObservedAt,
                    receipt.PolicyEvidence.ProtectedUntil)),
        };
        var refused = Assert.ThrowsExactly<ArgumentException>(() => RoutedHttpEvidence.Create(
            corpusRunIdentity, evidence.RequestOrdinal, evidence.AttemptOrdinal, evidence.Hops, evidence.Outcome, forged));
        StringAssert.Contains(refused.Message, "names other bytes");

        // And a receipt that names the right bytes but is not the receipt custody issued (one
        // second later on the policy clock) fails the second check: its own canonical digest does
        // not reproduce the digest the hop claims.
        var policy = receipt.PolicyEvidence;
        var replayed = new Dictionary<string, DurableBlobWriteReceipt>(receipts, StringComparer.Ordinal)
        {
            [hop.ObservationId] = new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                receipt.Reference,
                new CustodyPolicyEvidence(
                    CustodySchemaIds.CustodyPolicyEvidence,
                    receipt.Reference,
                    policy.VerificationProfile,
                    policy.PolicyKey,
                    policy.Protection,
                    policy.ObservedAt.AddSeconds(1),
                    policy.ProtectedUntil?.AddSeconds(1))),
        };
        var notReproduced = Assert.ThrowsExactly<ArgumentException>(() => RoutedHttpEvidence.Create(
            corpusRunIdentity, evidence.RequestOrdinal, evidence.AttemptOrdinal, evidence.Hops, evidence.Outcome, replayed));
        StringAssert.Contains(notReproduced.Message, "does not reproduce");

        // And a set missing a hop never becomes a result at all.
        Assert.ThrowsExactly<ArgumentException>(() => EuDocumentFetchAttemptResult.Executed(
            evidence, new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal)));
    }

    [TestMethod]
    public void RobotsFixtureBytesMatchTheLiveCaptureDigest()
    {
        var bytes = RobotsFixtureBytes();
        Assert.AreEqual(2475, bytes.Length);
        var text = Encoding.UTF8.GetString(bytes);
        StringAssert.Contains(text, "User-agent: *");
        StringAssert.Contains(text, "Crawl-delay: 10");
        Assert.IsFalse(text.Contains("Disallow: /content", StringComparison.Ordinal), "the pinned notice path is not disallowed.");
    }

    // ---- Shared plumbing. ----

    /// <summary>The real 2026-09-27 robots answer: 200, Content-Length, and no Content-Type header.</summary>
    private static HttpResponseMessage Robots(HttpRequestMessage request) =>
        EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, RobotsFixtureBytes());

    /// <summary>
    /// A 200 text/html notice whose declared length is the whole page but whose stream fails after
    /// the first 40 bytes: the shape the session seals as an incomplete route (body read failure).
    /// </summary>
    private static HttpResponseMessage TruncatedNotice(HttpRequestMessage request)
    {
        var content = new StreamContent(new FailAfterPrefixStream(NoticeBody.AsSpan(0, 40).ToArray()));
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length", NoticeBody.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Type", NoticeMediaType));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version11, RequestMessage = request, Content = content,
        };
    }

    private sealed class FailAfterPrefixStream(byte[] prefix) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= prefix.Length)
            {
                return ValueTask.FromException<int>(new IOException("Injected mid-body failure."));
            }

            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] RobotsFixtureBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuLegalNotice", "eur-lex-robots-2026-09-27.txt");
        var bytes = File.ReadAllBytes(path);
        Assert.AreEqual(RobotsFixtureSha256, Sha256(bytes), "the robots fixture no longer matches its live capture digest.");
        return bytes;
    }

    private static EuLegalNoticeRouteProducer Producer(ICustodyStore store, HttpMessageHandler handler) =>
        new(store, Clock(), handler);

    /// <summary>
    /// The legal-notice profile paces at 10 s; this clock advances on every read and fires every
    /// timer at once, so the pacing is exercised without waiting for it.
    /// </summary>
    private static TimeProvider Clock() => new RoutedHttpAcquisitionSessionTests.ShortDelayTimeProvider();

    private static MachineQueryRendererSource RendererSource() => EuAcquisitionTestFixture.BuildRendererSource(9101);

    private static SourceArtifactRef CorpusRunIdentity() => new(NewUrn(), Sha256("corpus-run-manifest"u8));

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Send(string Method, string Uri, string Accept, string UserAgent);

    private sealed class ScriptedHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<Send> _sends = [];

        internal IReadOnlyList<Send> Sends
        {
            get
            {
                lock (_sends)
                {
                    return _sends.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int ordinal;
            lock (_sends)
            {
                ordinal = _sends.Count;
                // Captured at send time: the session disposes the outbound message after it answers.
                _sends.Add(new Send(
                    request.Method.Method,
                    request.RequestUri!.AbsoluteUri,
                    request.Headers.Accept.ToString(),
                    request.Headers.UserAgent.ToString()));
            }

            return Task.FromResult(respond(ordinal, request));
        }
    }
}
