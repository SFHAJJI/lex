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
/// Decision 95's rights-policy GET through the production document-fetch session. The robots
/// fixture is the Publications Office capture under Fixtures/EuDocumentFetch; the rights body
/// is synthetic. No test contacts a publisher. Redirect, robots, budget and body failures are
/// retained as typed refusals, and successful routes carry the corpus run identity.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EuLegalNoticeRouteProducerTests
{
    private const string NoticeUri = EuLegalNoticeEvidence.ReuseDecisionUri;
    private const string RobotsUri = "https://publications.europa.eu/robots.txt";
    private const string RobotsFixtureSha256 = "de63106ad6607ba0bf3e313c31871d96ccc7e949ee0e29fa0b1c85a450305a75";
    private const string NoticeMediaType = "application/xhtml+xml; charset=UTF-8";

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
        // and it negotiated English XHTML beside the crawler identity.
        Assert.AreEqual(route.Hops[0].LogicalRequestSha256, Sha256(request.CopyCanonicalBytes()));
        Assert.AreEqual(NoticeUri, request.Uri);
        Assert.AreEqual(HttpRequestMethod.Get, request.Method);
        CollectionAssert.AreEquivalent(new[] { "accept", "accept-language", "user-agent" }, request.Headers.Select(static header => header.Name).ToArray());
        Assert.AreEqual(OutboundCrawlerIdentity.Token, request.Headers.Single(header => header.Name == "user-agent").Value);

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

        // Two robots hops, then one rights-policy request.
        CollectionAssert.AreEqual(new[] { RobotsUri, "https://op.europa.eu/robots.txt", NoticeUri }, handler.Sends.Select(static send => send.Uri).ToArray());
        Assert.IsTrue(handler.Sends.All(static send => send.Method == "GET"));
        Assert.AreEqual("application/xhtml+xml", handler.Sends[2].Accept);
        Assert.AreEqual(EuLegalNoticeSource.CommissionReuseDecision2011833, notice.Source);
        Assert.AreEqual(OutboundCrawlerIdentity.Token, handler.Sends[2].UserAgent);
    }

    /// <summary>
    /// The replacement uses the Cellar profile: a robots answer with no Content-Type is refused.
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
        }, redirectRobots: false);
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
                request, HttpStatusCode.OK, "User-agent: Lex\nDisallow: /\n\nUser-agent: *\nAllow: /\n"u8.ToArray(), "text/plain"),
            _ => throw new InvalidOperationException($"Unexpected request {ordinal}: {request.RequestUri}"),
        });

        var result = await Producer(store, handler).RunAsync(
            CorpusRunIdentity(), RendererSource(), EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);

        Assert.IsNull(result.Route);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.RobotsBootstrapRefused, result.Refusal);
        Assert.AreEqual(2, handler.Sends.Count);
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
        Assert.AreEqual(3, handler.Sends.Count);
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
        Assert.AreEqual(3, handler.Sends.Count);
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
        Assert.AreEqual(3, handler.Sends.Count, "an off-origin target is refused before it is sent.");
    }

    [TestMethod]
    public async Task ASameOriginRedirectIsFollowedAndTheTerminalHopIsTheNoticeEvidence()
    {
        const string Terminal = "https://publications.europa.eu/resource/cellar/cb76d4a0-c886-40bd-99d7-8db018a723d0.0010.03/DOC_1";
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
        CollectionAssert.AreEqual(new[] { RobotsUri, "https://op.europa.eu/robots.txt", NoticeUri, Terminal }, handler.Sends.Select(static send => send.Uri).ToArray());
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

        // One reservation already spent leaves exactly the two robots hops; the rights GET is the one the
        // ceiling stops.
        var budget = WireRequestBudget.OfWireRequests(3);
        Assert.IsTrue(budget.TryReserveAttempt());

        var result = await Producer(store, handler).RunAsync(
            CorpusRunIdentity(), RendererSource(), budget, CancellationToken.None);

        Assert.IsNull(result.Route);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.WireBudgetExhausted, result.Refusal);
        Assert.AreEqual(2, handler.Sends.Count);
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
        var bound = new EuDocumentFetchPlan(EuDocumentFetchAddress.TryCreate("celex", "32011D0833", EuManifestationMediaType.XhtmlXml, EuDocumentLanguage.Eng, out _)!).Bind(NewUrn(), NewUrn(), RendererSource()).Request;

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
        Assert.IsTrue(bytes.Length > 0);
        var text = Encoding.UTF8.GetString(bytes);
        StringAssert.Contains(text, "User-agent: *");
        StringAssert.Contains(text, "Allow: /");
        Assert.IsFalse(text.Contains("Disallow: /content", StringComparison.Ordinal), "the pinned notice path is not disallowed.");
    }

    // ---- Shared plumbing. ----

    /// <summary>The real 2026-09-27 robots answer: 200, Content-Length, and no Content-Type header.</summary>
    private static HttpResponseMessage Robots(HttpRequestMessage request) =>
        EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, RobotsFixtureBytes(), "text/plain");

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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetainedRightsReopenIndependentlyWithOriginalBytesTimesAndNoTraffic(bool redirect)
    {
        var (store, result, handler) = await CaptureForReopenAsync(redirect);
        var copy = await CopyForReopenAsync(store);
        var sends = handler.Sends.Count;
        var writes = copy.CreateCallCount;
        var reference = new SourceArtifactRef(NewUrn(), Sha256(result.Route!.CopyCanonicalBytes()));
        var first = await EuLegalNoticeRouteProducer.ReopenAsync(copy, reference, result.Route.RunIdentity, CancellationToken.None);
        var second = await EuLegalNoticeRouteProducer.ReopenAsync(copy, reference, result.Route.RunIdentity, CancellationToken.None);
        Assert.IsNull(first.Refusal, first.Detail);
        Assert.IsNull(second.Refusal, second.Detail);
        CollectionAssert.AreEqual(result.Route.CopyCanonicalBytes(), first.Route!.CopyCanonicalBytes());
        CollectionAssert.AreEqual(first.Route.CopyCanonicalBytes(), second.Route!.CopyCanonicalBytes());
        CollectionAssert.AreEqual(result.TerminalRequest!.CopyCanonicalBytes(), first.TerminalRequest!.CopyCanonicalBytes());
        Assert.AreEqual(EuLegalNoticeEvidence.FromRoute(result.Route, result.TerminalRequest).CanonicalSha256,
            EuLegalNoticeEvidence.FromRoute(first.Route, first.TerminalRequest).CanonicalSha256);
        Assert.AreEqual(sends, handler.Sends.Count);
        Assert.AreEqual(writes, copy.CreateCallCount);
    }

    [TestMethod]
    [DataRow("route")]
    [DataRow("request")]
    [DataRow("receipt")]
    [DataRow("body")]
    [DataRow("request_policy")]
    [DataRow("redirect_policy")]
    public async Task MissingRightsDependencyRefusesWithoutFallback(string missing)
    {
        var (store, result, handler) = await CaptureForReopenAsync(true);
        var route = result.Route!;
        var request = result.TerminalRequest!;
        var digest = missing switch
        {
            "route" => Sha256(route.CopyCanonicalBytes()),
            "request" => route.Hops[0].LogicalRequestSha256,
            "receipt" => route.Hops[0].DurableWriteReceiptSha256,
            "body" => route.Hops[^1].Sha256,
            "request_policy" => request.RequestPolicySha256,
            "redirect_policy" => request.RedirectPolicySha256,
            _ => throw new InvalidOperationException(),
        };
        var copy = await CopyForReopenAsync(store, digest);
        var sends = handler.Sends.Count;
        var writes = copy.CreateCallCount;
        var reopened = await EuLegalNoticeRouteProducer.ReopenAsync(copy,
            new SourceArtifactRef(NewUrn(), Sha256(route.CopyCanonicalBytes())), route.RunIdentity, CancellationToken.None);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.RouteNotRetained, reopened.Refusal, reopened.Detail);
        Assert.IsNull(reopened.Route);
        Assert.AreEqual(sends, handler.Sends.Count);
        Assert.AreEqual(writes, copy.CreateCallCount);
    }

    [TestMethod]
    public async Task ChangedRetainedRightsBodyRefusesItsDigest()
    {
        var (store, result, _) = await CaptureForReopenAsync(false);
        var copy = await CopyForReopenAsync(store);
        var route = result.Route!;
        var bytes = await copy.ReadByDigestAsync(route.Hops[0].Sha256, CancellationToken.None);
        Assert.IsTrue(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(bytes, out var segment));
        segment.Array![segment.Offset] ^= 1; // Deliberately violate this test store's retained bytes.
        var reopened = await EuLegalNoticeRouteProducer.ReopenAsync(copy,
            new SourceArtifactRef(NewUrn(), Sha256(route.CopyCanonicalBytes())), route.RunIdentity, CancellationToken.None);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.RouteNotRetained, reopened.Refusal);
    }

    [TestMethod]
    public async Task AnotherCorpusRunCannotAdoptTheRetainedRightsRoute()
    {
        var (store, result, _) = await CaptureForReopenAsync(false);
        var reopened = await EuLegalNoticeRouteProducer.ReopenAsync(store,
            new SourceArtifactRef(NewUrn(), Sha256(result.Route!.CopyCanonicalBytes())), CorpusRunIdentity(), CancellationToken.None);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.NoticeRouteInvalid, reopened.Refusal);
        StringAssert.Contains(reopened.Detail, "different corpus run");
    }

    [TestMethod]
    public async Task CancelledRightsReopenDoesNotBecomeAPublisherRefusal()
    {
        var (store, result, handler) = await CaptureForReopenAsync(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var writes = store.CreateCallCount;
        var sends = handler.Sends.Count;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => EuLegalNoticeRouteProducer.ReopenAsync(store,
            new SourceArtifactRef(NewUrn(), Sha256(result.Route!.CopyCanonicalBytes())), result.Route.RunIdentity, cancellation.Token));
        Assert.AreEqual(writes, store.CreateCallCount);
        Assert.AreEqual(sends, handler.Sends.Count);
    }

    [TestMethod]
    public async Task ARehashedRouteCannotSubstituteAnotherBodyReceipt()
    {
        var (store, result, _) = await CaptureForReopenAsync(false);
        var unrelated = await store.CreateAsync("another body"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var receiptBytes = Encoding.UTF8.GetBytes(Lex.V3.Contracts.ContractJson.Serialize(unrelated));
        var heldReceipt = await store.CreateAsync(receiptBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var original = Encoding.UTF8.GetString(result.Route!.CopyCanonicalBytes());
        var replacement = original.Replace(result.Route.Hops[0].DurableWriteReceiptSha256,
            heldReceipt.Reference.ContentSha256, StringComparison.Ordinal);
        Assert.AreNotEqual(original, replacement);
        var replacementBytes = Encoding.UTF8.GetBytes(replacement);
        // The route grammar still accepts these canonical bytes; the independent receipt gate must refuse.
        _ = RoutedHttpEvidence.ParseAndVerify(replacementBytes);
        var held = await store.CreateAsync(replacementBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var reopened = await EuLegalNoticeRouteProducer.ReopenAsync(store,
            new SourceArtifactRef(NewUrn(), held.Reference.ContentSha256), result.Route.RunIdentity, CancellationToken.None);
        Assert.AreEqual(EuLegalNoticeRouteRefusal.NoticeRouteInvalid, reopened.Refusal);
        StringAssert.Contains(reopened.Detail, "receipt names other bytes");
    }
    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        EuLegalNoticeRouteResult Result, ScriptedHandler Handler)> CaptureForReopenAsync(bool redirect)
    {
        const string terminal = "https://publications.europa.eu/resource/celex/32011D0833?view=legal";
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var handler = new ScriptedHandler((ordinal, request) => ordinal switch
        {
            0 => Robots(request),
            1 when redirect => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.Found, [], location: terminal),
            1 or 2 => EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, NoticeBody, NoticeMediaType),
            _ => throw new InvalidOperationException("Unexpected rights acquisition request."),
        });
        var result = await Producer(store, handler).RunAsync(CorpusRunIdentity(), RendererSource(),
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNull(result.Refusal, result.Detail);
        return (store, result, handler);
    }

    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyForReopenAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None),
                CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
    private static byte[] RobotsFixtureBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "eu-robots.txt");
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
        Func<int, HttpRequestMessage, HttpResponseMessage> respond, bool redirectRobots = true) : HttpMessageHandler
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

            Assert.AreNotEqual("eur-lex.europa.eu", request.RequestUri!.Host);
            if (redirectRobots && ordinal == 0)
            {
                return Task.FromResult(EuAcquisitionTestFixture.BinaryResponse(request,
                    HttpStatusCode.MovedPermanently, [], location: "https://op.europa.eu/robots.txt"));
            }
            return Task.FromResult(respond(redirectRobots ? ordinal - 1 : ordinal, request));
        }
    }
}
