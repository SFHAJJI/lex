using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RoutedHttpAcquisitionSessionAuditTests
{
    private const string EuQueryUri = "https://publications.europa.eu/webapi/rdf/sparql";

    [TestMethod]
    public void PlanItemConstructionAndRawOrdinalStayBehindTheSession()
    {
        var sessionType = typeof(RoutedHttpAcquisitionSession);
        var planItemType = sessionType.GetNestedType("PlanItem", BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The session has no private plan-item implementation.");
        var planItemInterface = sessionType.GetNestedType("IPlanItem", BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The session has no internal plan-item surface.");
        var open = sessionType.GetMethod(
            "OpenPlanItem",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The session has no plan-item factory.");

        Assert.IsTrue(planItemType.IsNestedPrivate);
        Assert.IsTrue(planItemInterface.IsNestedAssembly);
        Assert.AreEqual(planItemInterface, open.ReturnType);
        CollectionAssert.AreEqual(
            new[] { typeof(BoundMachineRequest) },
            open.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
        Assert.AreEqual(
            0,
            sessionType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Count(method =>
                    !method.IsPrivate &&
                    method.GetParameters().Any(static parameter =>
                        parameter.ParameterType == typeof(ulong)) &&
                    (method.ReturnType == planItemType || method.ReturnType == planItemInterface)),
            "A non-private plan-item factory exposed a caller-selected raw ordinal.");
        CollectionAssert.AreEquivalent(
            new[] { "RequestOrdinal", "ExecuteNextAttemptAsync" },
            planItemInterface.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                .Where(static member => member is PropertyInfo ||
                    member is MethodInfo { IsSpecialName: false })
                .Select(static member => member.Name)
                .ToArray());
    }

    [TestMethod]
    public async Task ConflictingLengthAndTransferCodingRetainTheEntireEntityBeforeClassification()
    {
        var request = EuropeanUnionRequest();
        var productBody = Encoding.UTF8.GetBytes("all publisher bytes must reach custody");
        var custody = new RecordingCustodyStore();
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            ConflictingFramingResponse(outbound, productBody, "1", "chunked")));
        using var session = Session(request, handler, custody, new ManualTimeProvider());
        await StartSuccessfullyAsync(session);

        RoutedHttpAcquisitionSession.AttemptResult? result = null;
        try
        {
            result = await session.OpenPlanItem(request)
                .ExecuteNextAttemptAsync(CancellationToken.None);
        }
        catch (ArgumentException)
        {
            // A typed /4 document cannot be minted from a partially retained conflicted entity.
        }

        Assert.IsTrue(
            custody.ContainsExact(productBody),
            "Content-Length constrained the body read before the Transfer-Encoding conflict was classified.");

        var hop = result?.Evidence?.Hops.Single()
            ?? throw new AssertFailedException("The product response produced no /4 hop.");
        Assert.IsInstanceOfType<IncompleteHttpCompletion>(hop.Completion);
        Assert.AreEqual(
            "transfer_coding_conflict",
            ((IncompleteHttpCompletion)hop.Completion).Reason.MemberKey);
        Assert.AreEqual((ulong)productBody.Length, hop.Length);
        Assert.IsTrue(custody.ContainsExact(productBody));
    }

    [TestMethod]
    public async Task HostileHeaderCardinalityCannotPreventEntityCustody()
    {
        var request = EuropeanUnionRequest();
        var productBody = Encoding.UTF8.GetBytes("body survives hostile headers");
        var custody = new RecordingCustodyStore();
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            ResponseWithTooManyHeaderValues(outbound, productBody)));
        using var session = Session(request, handler, custody, new ManualTimeProvider());
        await StartSuccessfullyAsync(session);

        try
        {
            _ = await session.OpenPlanItem(request)
                .ExecuteNextAttemptAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // The response may remain unrepresentable, but its entity must already be held.
        }

        Assert.IsTrue(
            custody.ContainsExact(productBody),
            "Publisher bytes were lost because typed header projection ran before custody.");
    }

    [TestMethod]
    public async Task RedirectTargetTransportFailureIsOperationalAndRetainsItsAntecedentEvidence()
    {
        var request = EuropeanUnionRequest();
        var handler = new AsyncSequenceHandler((ordinal, outbound, _) => ordinal switch
        {
            0 => Task.FromResult(EuBootstrapResponse(ordinal, outbound)),
            1 => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("simulated failure before redirect-target headers")),
            _ => throw new AssertFailedException("The failed redirect target was retried unexpectedly."),
        });
        using var session = Session(
            request,
            handler,
            new RecordingCustodyStore(),
            new ManualTimeProvider());

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(OfficialHttpOperationalFailureReason.NetworkFailure, result.OperationalReason);
        Assert.IsNull(result.LocalSafetyReason);
        Assert.IsNull(result.PostHeaderRejection, "A loss before headers is not a post-header rejection.");
        Assert.IsNull(result.Session);
        var evidence = result.Evidence
            ?? throw new AssertFailedException("The observed redirect antecedent was discarded.");
        Assert.AreEqual(1, evidence.Hops.Count);
        Assert.IsInstanceOfType<RedirectTargetUnobservedHttpRouteOutcome>(evidence.Outcome);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task ResponseEndedOn304DoesNotBecomeDeclaredLengthShortRead()
    {
        var request = EuropeanUnionRequest();
        using var session = Session(
            request,
            new AsyncSequenceHandler(static (_, _, _) =>
                throw new AssertFailedException("No network send expected.")),
            new RecordingCustodyStore(),
            new ManualTimeProvider());
        using var content = new StreamContent(new ResponseEndedStream());
        var capture = await CaptureBodyAsync(
            session,
            content,
            declaredLength: null,
            CancellationToken.None,
            maximumRetainedBytes: 8);
        var completion = ClassifyCompletion(
            session,
            ConditionalGetRequest(),
            status: 304,
            Headers(contentLength: new RoutedHttpSingleHeader("5")),
            capture);

        Assert.IsInstanceOfType<IncompleteHttpCompletion>(completion);
        Assert.AreEqual(
            "body_read_failure",
            ((IncompleteHttpCompletion)completion).Reason.MemberKey);
    }

    [TestMethod]
    public async Task UnsupportedProtocolAfterHeadersCannotBeReportedAsPreHeaderFailure()
    {
        await AssertUnrepresentableResponseIsPostHeaderAsync(
            new Version(2, 0),
            HttpStatusCode.OK,
            RoutedHttpAcquisitionSession.PostHeaderFailureClass.UnsupportedNegotiatedProtocol,
            "unsupported negotiated protocol");
    }

    [TestMethod]
    public async Task UnsupportedTerminalStatusAfterHeadersCannotBeReportedAsPreHeaderFailure()
    {
        await AssertUnrepresentableResponseIsPostHeaderAsync(
            HttpVersion.Version11,
            (HttpStatusCode)199,
            RoutedHttpAcquisitionSession.PostHeaderFailureClass.UnsupportedStatus,
            "unsupported final status");
    }

    // The four tests below assert on the StartResult the bootstrap actually returns, one layer
    // above the private route execution the two tests above inspect. A robots response that
    // arrived, entered custody and was then refused representation in /4 must be told apart from
    // a transport loss that never reached the publisher, on the surface a caller can see.

    [TestMethod]
    public async Task RobotsResponseWithUnsupportedProtocolIsAPostHeaderRejectionNotANetworkFailure()
    {
        await AssertRobotsPostHeaderRejectionAsync(
            static (outbound, body) => DeclaredResponse(outbound, HttpStatusCode.OK, body, new Version(2, 0)),
            OfficialHttpAcquisitionOutcomeKind.OperationalFailure,
            RoutedHttpAcquisitionSession.PostHeaderFailureClass.UnsupportedNegotiatedProtocol);
    }

    [TestMethod]
    public async Task RobotsResponseWithUnsupportedStatusIsAPostHeaderRejectionNotANetworkFailure()
    {
        await AssertRobotsPostHeaderRejectionAsync(
            static (outbound, body) => DeclaredResponse(outbound, (HttpStatusCode)199, body),
            OfficialHttpAcquisitionOutcomeKind.OperationalFailure,
            RoutedHttpAcquisitionSession.PostHeaderFailureClass.UnsupportedStatus);
    }

    [TestMethod]
    public async Task RobotsResponseWithUnprojectableHeadersIsAPostHeaderRejectionNotANetworkFailure()
    {
        await AssertRobotsPostHeaderRejectionAsync(
            static (outbound, body) => ResponseWithTooManyHeaderValues(outbound, body),
            OfficialHttpAcquisitionOutcomeKind.OperationalFailure,
            RoutedHttpAcquisitionSession.PostHeaderFailureClass.HeaderProjectionRejected);
    }

    [TestMethod]
    public async Task RobotsResponseRejectedByAdapterIdentityIsAnIntegrityFailureWithItsBytesHeld()
    {
        // An injected handler can never warrant chunked EOF, so the robots bootstrap must route
        // this exactly as the product path does: integrity, not operational, and never network.
        await AssertRobotsPostHeaderRejectionAsync(
            static (outbound, body) => ChunkedResponse(outbound, HttpStatusCode.OK, body),
            OfficialHttpAcquisitionOutcomeKind.IntegrityFailure,
            RoutedHttpAcquisitionSession.PostHeaderFailureClass.AdapterIdentityRejected);
    }

    [TestMethod]
    public async Task ProductResponseRejectedAfterHeadersCarriesItsTypedClassOnTheAttempt()
    {
        var request = EuropeanUnionRequest();
        var body = Encoding.UTF8.GetBytes("product bytes that /4 cannot encode");
        var custody = new RecordingCustodyStore();
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            DeclaredResponse(outbound, HttpStatusCode.OK, body, new Version(2, 0))));
        using var session = Session(request, handler, custody, new ManualTimeProvider());
        await StartSuccessfullyAsync(session);
        var item = session.OpenPlanItem(request);

        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.IsNull(result.OperationalReason, "A response that arrived carries no transport reason.");
        Assert.IsNull(result.PreHeaderFailureClass);
        Assert.IsNull(result.Evidence);
        var rejection = result.PostHeaderRejection
            ?? throw new AssertFailedException("The product post-header rejection lost its typed class.");
        Assert.AreEqual(
            RoutedHttpAcquisitionSession.PostHeaderFailureClass.UnsupportedNegotiatedProtocol,
            rejection.FailureClass);
        Assert.AreEqual(0, rejection.PriorHops.Count);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(body)), rejection.ContentSha256);
        Assert.AreEqual(custody.DurableWriteReceiptSha256For(body), rejection.DurableWriteReceiptSha256);
        Assert.IsTrue(custody.ContainsExact(body));
        // A post-header rejection is not a retryable transport state: the item cannot send again.
        var sendsAfterRejection = handler.SendCount;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => item.ExecuteNextAttemptAsync(CancellationToken.None));
        Assert.AreEqual(sendsAfterRejection, handler.SendCount);
    }

    [TestMethod]
    public void PostHeaderRejectionCanOnlyBeMintedByTheSessionsOwnRouteExecution()
    {
        // The rejection is evidence: it asserts that specific bytes sit in custody under a specific
        // digest after specific hops. A construction path outside the route execution would let a
        // friend assembly forge that claim, so the surface is pinned by kind, scope and nesting.
        var rejectionType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "PostHeaderRejection",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The post-header rejection type is missing.");
        Assert.IsTrue(rejectionType.IsAbstract);
        Assert.IsTrue(rejectionType.IsNestedAssembly);

        var constructors = rejectionType.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.AreEqual(1, constructors.Length);
        Assert.IsTrue(
            constructors[0].IsFamilyAndAssembly,
            "the base constructor must be private protected, which InternalsVisibleTo cannot reach");
        Assert.AreEqual(
            0,
            rejectionType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Count(method => rejectionType.IsAssignableFrom(method.ReturnType) ||
                    method.GetParameters().Any(parameter =>
                        parameter.ParameterType.IsByRef &&
                        rejectionType.IsAssignableFrom(parameter.ParameterType.GetElementType()))),
            "no static factory or out parameter may mint a rejection");

        var concrete = typeof(RoutedHttpAcquisitionSession).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && rejectionType.IsAssignableFrom(type))
            .ToArray();
        Assert.AreEqual(1, concrete.Length, "exactly one concrete rejection exists");
        Assert.IsTrue(concrete[0].IsNestedPrivate, "and it is private to its minter");
        Assert.AreEqual("PostHeaderFailure", concrete[0].DeclaringType?.Name);
    }

    [TestMethod]
    public void AnOperationalFailureCarriesExactlyOneOfATransportReasonOrAPostHeaderRejection()
    {
        foreach (var resultType in new[] { "StartResult", "AttemptResult" })
        {
            var type = typeof(RoutedHttpAcquisitionSession).GetNestedType(resultType, BindingFlags.NonPublic)
                ?? throw new AssertFailedException($"The {resultType} type is missing.");
            var constructor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            var parameters = constructor.GetParameters();
            var reasonIndex = Array.FindIndex(parameters, static p => p.ParameterType == typeof(OfficialHttpOperationalFailureReason?));
            var rejectionIndex = Array.FindIndex(parameters, static p => p.ParameterType.Name == "PostHeaderRejection");
            Assert.IsTrue(reasonIndex >= 0 && rejectionIndex >= 0, $"{resultType} lost one of its reason shapes");

            // Kind says failure, both shapes absent: refused.
            var neither = new object?[parameters.Length];
            neither[0] = OfficialHttpAcquisitionOutcomeKind.OperationalFailure;
            var refusal = Assert.ThrowsExactly<TargetInvocationException>(() => constructor.Invoke(neither));
            Assert.IsInstanceOfType<InvalidOperationException>(refusal.InnerException, $"{resultType} accepted a reasonless failure");

            // Kind says failure, transport reason present: admitted. Proves the check is reachable
            // and not simply refusing every construction.
            var one = new object?[parameters.Length];
            one[0] = OfficialHttpAcquisitionOutcomeKind.OperationalFailure;
            one[reasonIndex] = OfficialHttpOperationalFailureReason.NetworkFailure;
            Assert.IsNotNull(constructor.Invoke(one));

            // A non-failure kind with neither shape is fine; the invariant is scoped to failures.
            var executed = new object?[parameters.Length];
            executed[0] = OfficialHttpAcquisitionOutcomeKind.ExecutedObservation;
            Assert.IsNotNull(constructor.Invoke(executed));
        }
    }

    // Candidate D. The classifier and the validator must agree about what a response is, or a
    // publisher response that was received and held becomes an escaping exception instead of
    // evidence. The two inputs below reached that exception at 4f4ca3de.

    [TestMethod]
    public async Task AShortReadUnderConflictingFramingIsABodyReadFailureNotADeclaredLengthShortRead()
    {
        // RFC 9112 section 6.1: a Content-Length beside a Transfer-Encoding has no standing, so a
        // stream that ends early cannot be measured against it. The honest reason is that the
        // read failed, and the validator already refuses declared_length_short_read here.
        var request = EuropeanUnionRequest();
        var custody = new RecordingCustodyStore();
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            ConflictingFramingResponseEndingEarly(outbound)));
        using var session = Session(request, handler, custody, new ManualTimeProvider());
        await StartSuccessfullyAsync(session);

        var result = await session.OpenPlanItem(request)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        var hop = result.Evidence?.Hops.Single()
            ?? throw new AssertFailedException("The conflicting-framing short read produced no /4 hop.");
        Assert.IsInstanceOfType<RoutedHttpSingleHeader>(hop.Headers.ContentLength);
        Assert.IsInstanceOfType<RoutedHttpSingleHeader>(hop.Headers.TransferEncoding);
        var completion = Assert.IsInstanceOfType<IncompleteHttpCompletion>(hop.Completion);
        Assert.AreEqual(
            HttpPartialBodyReason.BodyReadFailure,
            HttpAcquisitionReasonRegistry.RequirePartial(completion.Reason));
    }

    [TestMethod]
    public async Task ANoContentResponseCarryingContentRangeIsStillAResponseWithoutBody()
    {
        // A header-terminated status has no entity to range. Classifying the Content-Range before
        // the 204 arm made the response's own completion unrepresentable and threw out of the
        // attempt; the header is retained as evidence and decides nothing.
        var request = EuropeanUnionRequest();
        var custody = new RecordingCustodyStore();
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            NoContentResponseWithContentRange(outbound)));
        using var session = Session(request, handler, custody, new ManualTimeProvider());
        await StartSuccessfullyAsync(session);

        var result = await session.OpenPlanItem(request)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        var hop = result.Evidence?.Hops.Single()
            ?? throw new AssertFailedException("The ranged 204 produced no /4 hop.");
        Assert.AreEqual(204, hop.Status);
        Assert.AreEqual(HttpStatusDisposition.SemanticNoEntityStatus, hop.StatusDisposition);
        Assert.IsInstanceOfType<ResponseWithoutBodyHttpCompletion>(hop.Completion);
        Assert.IsInstanceOfType<RoutedHttpSingleHeader>(hop.Headers.ContentRange, "the header is evidence");
        Assert.AreEqual(0UL, hop.Length);
    }

    private static HttpResponseMessage ConflictingFramingResponseEndingEarly(HttpRequestMessage request)
    {
        var content = new StreamContent(new ResponseEndedStream());
        Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Length", "100"));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
        Assert.IsTrue(response.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked"));
        return response;
    }

    private static HttpResponseMessage NoContentResponseWithContentRange(HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentLength = null;
        Assert.IsTrue(response.Content.Headers.TryAddWithoutValidation("Content-Range", "bytes */100"));
        return response;
    }

    private static async Task AssertRobotsPostHeaderRejectionAsync(
        Func<HttpRequestMessage, byte[], HttpResponseMessage> terminalRobotsResponse,
        OfficialHttpAcquisitionOutcomeKind expectedKind,
        RoutedHttpAcquisitionSession.PostHeaderFailureClass expectedFailure)
    {
        var request = EuropeanUnionRequest();
        var body = Encoding.UTF8.GetBytes("robots bytes that /4 cannot encode");
        var custody = new RecordingCustodyStore();
        var handler = new AsyncSequenceHandler((ordinal, outbound, _) => ordinal switch
        {
            0 => Task.FromResult(EuBootstrapResponse(ordinal, outbound)),
            1 => Task.FromResult(terminalRobotsResponse(outbound, body)),
            _ => throw new AssertFailedException("A rejected robots response must open no product socket."),
        });
        using var session = Session(request, handler, custody, new ManualTimeProvider());

        var result = await BootstrapAsync(session);

        Assert.AreEqual(expectedKind, result.Kind);
        Assert.IsNull(
            result.OperationalReason,
            "A robots response that arrived and entered custody was reported as a transport loss.");
        Assert.IsNull(result.LocalSafetyReason);
        Assert.IsNull(result.Session);
        Assert.IsNull(result.Evidence, "No /4 document can represent this response.");
        var rejection = result.PostHeaderRejection
            ?? throw new AssertFailedException("The robots post-header rejection lost its typed class.");
        Assert.AreEqual(expectedFailure, rejection.FailureClass);
        Assert.AreEqual(1, rejection.PriorHops.Count, "The observed redirect hop was discarded.");
        Assert.AreEqual(301, rejection.PriorHops[0].Status);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(body)), rejection.ContentSha256);
        Assert.AreEqual(custody.DurableWriteReceiptSha256For(body), rejection.DurableWriteReceiptSha256);
        Assert.IsTrue(custody.ContainsExact(body));
        Assert.AreEqual(2, handler.SendCount);
        Assert.ThrowsExactly<ObjectDisposedException>(() => session.OpenPlanItem(request));
    }

    [TestMethod]
    public async Task RequestStartedAtIsReadAtTheOwnedSendInvocation()
    {
        var request = EuropeanUnionRequest();
        var time = new ManualTimeProvider();
        var custody = new RecordingCustodyStore();
        DateTimeOffset? firstInvocationAt = null;
        var handler = new AsyncSequenceHandler((ordinal, outbound, _) =>
        {
            if (ordinal == 0)
            {
                firstInvocationAt = time.GetUtcNow();
            }

            return Task.FromResult(EuBootstrapResponse(ordinal, outbound));
        });
        using var session = Session(request, handler, custody, time);
        time.Advance(TimeSpan.FromHours(6));

        var started = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
        Assert.IsNotNull(firstInvocationAt);
        Assert.AreEqual(
            Timestamp(firstInvocationAt.Value),
            started.Evidence?.Hops[0].RequestStartedAt);
    }

    [TestMethod]
    public async Task RobotsRedirectCannotSendAfterTheGenerationExpires()
    {
        var request = EuropeanUnionRequest();
        var time = new ManualTimeProvider();
        var custody = new RecordingCustodyStore();
        var handler = new AsyncSequenceHandler((ordinal, outbound, _) =>
        {
            if (ordinal == 0)
            {
                var response = EuBootstrapResponse(ordinal, outbound);
                time.Advance(TimeSpan.FromHours(24));
                return Task.FromResult(response);
            }

            return Task.FromResult(EuBootstrapResponse(ordinal, outbound));
        });
        using var session = Session(request, handler, custody, time);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(1, handler.SendCount);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(
            OfficialHttpOperationalFailureReason.RobotsPolicyExpired,
            result.OperationalReason);
    }

    [TestMethod]
    public async Task CallerCancellationBeforeHeadersPropagatesAndClosesThePlanItem()
    {
        var request = EuropeanUnionRequest();
        var time = new ManualTimeProvider();
        var custody = new RecordingCustodyStore();
        var productStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = EuSequence(async (_, _, transportCancellation) =>
        {
            productStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, transportCancellation);
            throw new AssertFailedException("A cancelled send resumed unexpectedly.");
        });
        using var session = Session(request, handler, custody, time);
        await StartSuccessfullyAsync(session);
        var item = session.OpenPlanItem(request);
        using var callerCancellation = new CancellationTokenSource();
        var firstAttempt = item.ExecuteNextAttemptAsync(callerCancellation.Token);
        await productStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callerCancellation.Cancel();

        try
        {
            _ = await firstAttempt;
            Assert.Fail("Caller cancellation before headers became an operational retry result.");
        }
        catch (OperationCanceledException)
        {
        }

        var sendsAfterCancellation = handler.SendCount;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => item.ExecuteNextAttemptAsync(CancellationToken.None));
        Assert.AreEqual(sendsAfterCancellation, handler.SendCount);
    }

    [TestMethod]
    public async Task HeaderDeadlineRetainsItsPrivateFailureClass()
    {
        var request = EuropeanUnionRequest();
        var time = new ImmediateDeadlineTimeProvider();
        var handler = new AsyncSequenceHandler(static async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertFailedException("A timed-out send resumed unexpectedly.");
        });
        using var session = Session(request, handler, new RecordingCustodyStore(), time);
        SetGenerationStart(session, time);

        var result = await session.OpenPlanItem(request)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(OfficialHttpOperationalFailureReason.NetworkFailure, result.OperationalReason);
        Assert.AreEqual(
            HttpPreHeaderFailureClass.HeaderDeadline,
            BeforeHeadersClass(result));
    }

    [TestMethod]
    public async Task TransportFailureRetainsItsPrivateFailureClass()
    {
        var request = EuropeanUnionRequest();
        var time = new ManualTimeProvider();
        var handler = new AsyncSequenceHandler(static (_, _, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection failed")));
        using var session = Session(request, handler, new RecordingCustodyStore(), time);
        SetGenerationStart(session, time);

        var result = await session.OpenPlanItem(request)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(OfficialHttpOperationalFailureReason.NetworkFailure, result.OperationalReason);
        Assert.AreEqual(
            HttpPreHeaderFailureClass.TransportBeforeHeaders,
            BeforeHeadersClass(result));
    }

    [TestMethod]
    public async Task CustodyFailurePermanentlyClosesThePlanItem()
    {
        var request = EuropeanUnionRequest();
        var time = new ManualTimeProvider();
        var failedBody = Encoding.UTF8.GetBytes("product custody failure");
        var custody = new RecordingCustodyStore { FailOnExactBytes = failedBody };
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            DeclaredResponse(outbound, HttpStatusCode.OK, "product custody failure")));
        using var session = Session(request, handler, custody, time);
        await StartSuccessfullyAsync(session);
        var item = session.OpenPlanItem(request);

        var first = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, first.Kind);
        Assert.AreEqual(OfficialHttpOperationalFailureReason.CustodyUnavailable, first.OperationalReason);
        Assert.IsNull(first.Evidence);
        Assert.AreEqual(3, handler.SendCount, "The custody failure must happen after product headers arrive.");
        var sendsAfterCustodyFailure = handler.SendCount;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => item.ExecuteNextAttemptAsync(CancellationToken.None));
        Assert.AreEqual(sendsAfterCustodyFailure, handler.SendCount);
    }

    [TestMethod]
    public async Task UnpinnedChunkedResponseReachesCustodyBeforeRuntimeRefusal()
    {
        var request = EuropeanUnionRequest();
        var productBody = Encoding.UTF8.GetBytes("chunked bytes need custody first");
        var custody = new RecordingCustodyStore();
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            ChunkedResponse(outbound, HttpStatusCode.OK, productBody)));
        using var session = Session(request, handler, custody, new ManualTimeProvider());
        await StartSuccessfullyAsync(session);

        var result = await session.OpenPlanItem(request)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.IntegrityFailure, result.Kind);
        Assert.IsNull(result.Evidence);
        Assert.IsTrue(custody.ContainsExact(productBody));
    }

    [TestMethod]
    public async Task NonRetryableCompletionFactsOutrankRetryableStatus()
    {
        var request = EuropeanUnionRequest();
        var custody = new RecordingCustodyStore();
        var handler = EuSequence((_, outbound, _) => Task.FromResult(
            InvalidLengthResponse(outbound, HttpStatusCode.ServiceUnavailable, "retry is unsafe")));
        using var session = Session(request, handler, custody, new ManualTimeProvider());
        await StartSuccessfullyAsync(session);
        var item = session.OpenPlanItem(request);

        var first = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        var hop = first.Evidence?.Hops.Single()
            ?? throw new AssertFailedException("The retryable status produced no /4 hop.");
        Assert.IsInstanceOfType<IncompleteHttpCompletion>(hop.Completion);
        Assert.AreEqual(
            "invalid_content_length",
            ((IncompleteHttpCompletion)hop.Completion).Reason.MemberKey);
        var sendsAfterFirst = handler.SendCount;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => item.ExecuteNextAttemptAsync(CancellationToken.None));
        Assert.AreEqual(sendsAfterFirst, handler.SendCount);
    }

    private static async Task AssertUnrepresentableResponseIsPostHeaderAsync(
        Version version,
        HttpStatusCode status,
        RoutedHttpAcquisitionSession.PostHeaderFailureClass expectedFailure,
        string bodyText)
    {
        var request = EuropeanUnionRequest();
        var body = Encoding.UTF8.GetBytes(bodyText);
        var custody = new RecordingCustodyStore();
        var handler = new AsyncSequenceHandler((_, outbound, _) => Task.FromResult(
            DeclaredResponse(outbound, status, body, version)));
        var time = new ManualTimeProvider();
        using var session = Session(request, handler, custody, time);
        SetGenerationStart(session, time);
        var logicalRequest = CreateMachineRequest(session, request);

        var route = await ExecuteRouteAsync(
            session,
            logicalRequest,
            request.CopyRequestBody(),
            requestOrdinal: 1,
            attemptOrdinal: 0,
            robotsRoute: null,
            enforceGenerationAge: false,
            CancellationToken.None);

        Assert.IsNull(RouteProperty(route, "Evidence"));
        Assert.IsNull(
            RouteProperty(route, "PreHeaderFailure"),
            "A response with received headers was falsely classified as a pre-header failure.");
        var postHeader = RouteProperty(route, "PostHeaderFailure")
            ?? throw new AssertFailedException("A post-header rejection lost its typed failure.");
        Assert.AreEqual(expectedFailure, RouteProperty(postHeader, "FailureClass"));
        Assert.IsTrue(custody.ContainsExact(body));
    }

    private static async Task StartSuccessfullyAsync(
        RoutedHttpAcquisitionSession session)
    {
        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
        Assert.AreSame(session, started.Session);
    }

    private static AsyncSequenceHandler EuSequence(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> product) =>
        new((ordinal, outbound, cancellationToken) => ordinal switch
        {
            0 or 1 => Task.FromResult(EuBootstrapResponse(ordinal, outbound)),
            _ => product(ordinal, outbound, cancellationToken),
        });

    private static HttpResponseMessage EuBootstrapResponse(
        int ordinal,
        HttpRequestMessage request) => ordinal switch
        {
            0 => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "moved",
                location: "https://op.europa.eu/robots.txt"),
            1 => DeclaredResponse(
                request,
                HttpStatusCode.OK,
                "User-agent: *\nAllow: /\n",
                contentType: "text/plain;charset=UTF-8"),
            _ => throw new AssertFailedException("The frozen robots route sent an extra request."),
        };

    private static HttpResponseMessage DeclaredResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string body,
        Version? version = null,
        string? location = null,
        string? contentType = null) =>
        DeclaredResponse(request, status, Encoding.UTF8.GetBytes(body), version, location, contentType);

    private static HttpResponseMessage DeclaredResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        byte[] body,
        Version? version = null,
        string? location = null,
        string? contentType = null)
    {
        var content = new ByteArrayContent(body);
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length",
            body.Length.ToString(CultureInfo.InvariantCulture)));
        if (contentType is not null)
        {
            Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Type", contentType));
        }

        var response = new HttpResponseMessage(status)
        {
            Version = version ?? HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
        if (location is not null)
        {
            Assert.IsTrue(response.Headers.TryAddWithoutValidation("Location", location));
        }

        return response;
    }

    private static HttpResponseMessage ConflictingFramingResponse(
        HttpRequestMessage request,
        byte[] body,
        string contentLength,
        string transferEncoding)
    {
        var content = new ByteArrayContent(body);
        Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Length", contentLength));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
        Assert.IsTrue(response.Headers.TryAddWithoutValidation(
            "Transfer-Encoding",
            transferEncoding));
        return response;
    }

    private static HttpResponseMessage ChunkedResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        byte[] body)
    {
        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = new ByteArrayContent(body),
        };
        Assert.IsTrue(response.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked"));
        return response;
    }

    private static HttpResponseMessage InvalidLengthResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string body)
    {
        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        };
        Assert.IsTrue(response.Content.Headers.TryAddWithoutValidation("Content-Length", "+1"));
        return response;
    }

    private static HttpResponseMessage ResponseWithTooManyHeaderValues(
        HttpRequestMessage request,
        byte[] body)
    {
        var response = DeclaredResponse(request, HttpStatusCode.OK, body);
        Assert.IsTrue(response.Headers.TryAddWithoutValidation(
            "Cache-Control",
            Enumerable.Range(0, 17).Select(index => $"extension-{index.ToString(CultureInfo.InvariantCulture)}")));
        Assert.IsTrue(response.Headers.NonValidated.TryGetValues("Cache-Control", out var values));
        Assert.AreEqual(17, values.Count());
        return response;
    }

    private static BoundMachineRequest EuropeanUnionRequest()
        => MachineRequestTestFixture.EuropeanUnionRequest();

    private static HttpLogicalRequest ConditionalGetRequest()
    {
        var empty = Array.Empty<byte>();
        return HttpLogicalRequest.Create(
            "https://op.europa.eu/resource",
            HttpRequestMethod.Get,
            [
                new HttpLogicalRequestHeader("user-agent", OutboundCrawlerIdentity.Token),
                new HttpLogicalRequestHeader("if-none-match", "\"etag\""),
            ],
            new HttpLogicalRequestBody(0, Sha256(empty)),
            new string('a', 64),
            new string('b', 64));
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
        CultureInfo.InvariantCulture);

    private static RoutedHttpAcquisitionSession Session(
        BoundMachineRequest request,
        HttpMessageHandler handler,
        ICustodyStore custody,
        TimeProvider timeProvider)
    {
        var constructor = typeof(RoutedHttpAcquisitionSession).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var session = (RoutedHttpAcquisitionSession)constructor.Invoke(
            [request, custody, handler, timeProvider, false]);
        PrivateMethod("ActivateGeneration", BindingFlags.Instance).Invoke(session, null);
        return session;
    }

    private static Task<RoutedHttpAcquisitionSession.StartResult> BootstrapAsync(
        RoutedHttpAcquisitionSession session) =>
        (Task<RoutedHttpAcquisitionSession.StartResult>)PrivateMethod(
            "BootstrapRobotsAsync",
            BindingFlags.Instance).Invoke(session, [CancellationToken.None])!;

    private static HttpLogicalRequest CreateMachineRequest(
        RoutedHttpAcquisitionSession session,
        BoundMachineRequest request)
    {
        var resolverType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "SessionMachineArtifactResolver",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The session machine-artifact resolver is missing.");
        var resolver = (IMachineQueryArtifactResolver)(resolverType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single()
            .Invoke([session]));
        var opened = MachineQueryBinder.OpenForSendAsync(
                request,
                resolver,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var artifacts = resolverType.GetMethod(
            "CopyResolvedArtifacts",
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(resolver, null)
            ?? throw new AssertFailedException("The resolver exposed no reopened artifacts.");
        var resolvedType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "ResolvedMachineRequest",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The resolved machine request type is missing.");
        var resolved = resolvedType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(static constructor =>
                constructor.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(OpenedMachineRequest))
            .Invoke([opened, artifacts]);
        return (HttpLogicalRequest)(PrivateMethod(
            "CreateMachineRequest",
            BindingFlags.Instance).Invoke(session, [resolved])
            ?? throw new AssertFailedException("The runtime created no machine logical request."));
    }

    private static async Task<object> ExecuteRouteAsync(
        RoutedHttpAcquisitionSession session,
        HttpLogicalRequest request,
        ReadOnlyMemory<byte> requestBody,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        RobotsPolicyRoute? robotsRoute,
        bool enforceGenerationAge,
        CancellationToken cancellationToken)
    {
        var task = (Task)(PrivateMethod("ExecuteRouteAsync", BindingFlags.Instance).Invoke(
            session,
            [
                request,
                requestBody,
                requestOrdinal,
                attemptOrdinal,
                robotsRoute,
                enforceGenerationAge,
                cancellationToken,
            ]) ?? throw new AssertFailedException("The route execution returned no task."));
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new AssertFailedException("The route execution returned no result.");
    }

    private static object? RouteProperty(object route, string name) =>
        route.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic)?.GetValue(route)
        ?? (route.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic) is null
            ? throw new AssertFailedException($"The route result has no '{name}' property.")
            : null);

    private static async Task<object> CaptureBodyAsync(
        RoutedHttpAcquisitionSession session,
        HttpContent content,
        ulong? declaredLength,
        CancellationToken cancellationToken,
        long maximumRetainedBytes)
    {
        var task = (Task)PrivateMethod("CaptureBodyAsync", BindingFlags.Instance).Invoke(
            session,
            [content, declaredLength, cancellationToken, maximumRetainedBytes])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new AssertFailedException("The body capture returned no result.");
    }

    private static RoutedHttpCompletion ClassifyCompletion(
        RoutedHttpAcquisitionSession session,
        HttpLogicalRequest request,
        int status,
        RoutedHttpResponseHeaders headers,
        object capture) =>
        (RoutedHttpCompletion)(PrivateMethod("ClassifyCompletion", BindingFlags.Instance).Invoke(
            session,
            [request, status, headers, capture])
            ?? throw new AssertFailedException("The runtime returned no completion."));

    private static RoutedHttpResponseHeaders Headers(
        RoutedHttpHeaderField? contentLength = null,
        RoutedHttpHeaderField? transferEncoding = null)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            absent,
            contentLength ?? absent,
            absent,
            transferEncoding ?? absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent);
    }

    private static MethodInfo PrivateMethod(string name, BindingFlags scope) =>
        typeof(RoutedHttpAcquisitionSession).GetMethod(name, BindingFlags.NonPublic | scope)
        ?? throw new AssertFailedException($"The runtime seam '{name}' is missing.");

    private static void SetGenerationStart(
        RoutedHttpAcquisitionSession session,
        TimeProvider timeProvider) =>
        PrivateMethod("SetRobotsGenerationStart", BindingFlags.Instance).Invoke(
            session,
            [timeProvider.GetUtcNow(), timeProvider.GetTimestamp()]);

    private static HttpPreHeaderFailureClass BeforeHeadersClass(
        RoutedHttpAcquisitionSession.AttemptResult result)
    {
        var matches = result.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property =>
                property.PropertyType == typeof(HttpPreHeaderFailureClass) ||
                Nullable.GetUnderlyingType(property.PropertyType) == typeof(HttpPreHeaderFailureClass))
            .ToArray();
        Assert.AreEqual(
            1,
            matches.Length,
            "An operational attempt must retain exactly one private before-header taxonomy field.");
        var value = matches[0].GetValue(result)
            ?? throw new AssertFailedException("The before-header failure class was absent.");
        return (HttpPreHeaderFailureClass)value;
    }

    private sealed class AsyncSequenceHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        private int _sendCount;

        internal int SendCount => Volatile.Read(ref _sendCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var ordinal = Interlocked.Increment(ref _sendCount) - 1;
            return respond(ordinal, request, cancellationToken);
        }
    }

    // internal rather than private: the Luxembourg executor and evidence-set tests
    // (LuxembourgDeliveryEvidenceSetTests, LuxembourgRepeatedEnumerationExecutorTests) share this
    // as their flooring double for happy-path runs, per the executor design synthesis.
    internal sealed class RecordingCustodyStore : ICustodyStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DurableBlobWriteReceipt> _receipts = new(StringComparer.Ordinal);
        private readonly List<byte[]> _writes = [];
        private int _createCount;

        internal byte[]? FailOnExactBytes { get; init; }

        /// <summary>
        /// When true this double answers reads ONLY from what it was actually written, with no
        /// recourse to the shared fixture lookup table below. Item 1b deleted exactly this shape
        /// from the product path ("a path nothing production exercises... left in place it would
        /// fail exactly the way it did before: quietly, against a recording test double, until the
        /// day a real unseeded store meets it"), and a test double that keeps it can hide the same
        /// defect from the test that was supposed to catch it. Every Luxembourg executor test sets
        /// this, so a run that only NAMES a dependency is distinguishable from one that holds it.
        /// </summary>
        internal bool RefuseFallback { get; init; }

        /// <summary>How many reads this store answered from the shared fixture table.</summary>
        internal int FallbackHits => Volatile.Read(ref _fallbackHits);

        private int _fallbackHits;

        internal bool ContainsExact(ReadOnlySpan<byte> expected)
        {
            lock (_gate)
            {
                foreach (var bytes in _writes)
                {
                    if (bytes.AsSpan().SequenceEqual(expected))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// The digest of the exact write receipt this store issued for <paramref name="bytes"/>,
        /// computed the way the session computes it, so a test compares against what the store
        /// actually returned rather than against a shape.
        /// </summary>
        internal string DurableWriteReceiptSha256For(ReadOnlySpan<byte> bytes)
        {
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            DurableBlobWriteReceipt receipt;
            lock (_gate)
            {
                if (!_receipts.TryGetValue(digest, out var issued))
                {
                    throw new AssertFailedException("The store issued no write receipt for those bytes.");
                }

                receipt = issued;
            }

            return DurableBlobWriteReceiptDigest.Of(receipt);
        }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = Interlocked.Increment(ref _createCount);
            if (FailOnExactBytes is { } failed && bytes.Span.SequenceEqual(failed))
            {
                return Task.FromException<DurableBlobWriteReceipt>(
                    new IOException("simulated custody outage"));
            }

            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen, cancellationToken);
            lock (_gate)
            {
                _writes.Add(frozen);
                _objects[digest] = frozen;
            }

            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                digest,
                frozen.Length,
                custodyClass);
            var observed = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-000000000040"),
                CustodyProtection.LockedTime,
                observed,
                observed.AddDays(91));
            var receipt = new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                policy);
            lock (_gate)
            {
                _receipts[digest] = receipt;
            }

            return Task.FromResult(receipt);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_objects.TryGetValue(reference.ContentSha256, out var bytes))
                {
                    throw new AssertFailedException("Custody readback requested an unknown object.");
                }

                return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
            }
        }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_objects.TryGetValue(contentSha256, out var bytes))
                {
                    if (!RefuseFallback &&
                        MachineRequestTestFixture.TryReopenPreexistingArtifact(
                            contentSha256,
                            out var preexisting))
                    {
                        _ = Interlocked.Increment(ref _fallbackHits);
                        return Task.FromResult(preexisting);
                    }

                    throw new AssertFailedException("Custody reopen requested an unknown digest.");
                }

                return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
            }
        }
    }

    private sealed class ResponseEndedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new HttpRequestException(
                HttpRequestError.ResponseEnded,
                "simulated response end"));

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "simulated response end");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _utc = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utc;
            }
        }

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        internal void Advance(TimeSpan value)
        {
            lock (_gate)
            {
                _utc = _utc.Add(value);
                Interlocked.Add(ref _timestamp, value.Ticks);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new CallbackTimer(callback, state);
            if (dueTime >= TimeSpan.Zero && dueTime <= TimeSpan.FromSeconds(30))
            {
                Advance(dueTime);
                timer.Queue();
            }

            return timer;
        }
    }

    private sealed class ImmediateDeadlineTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(
            2026,
            9,
            2,
            10,
            0,
            0,
            TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => Epoch;

        public override long GetTimestamp() => 0;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new CallbackTimer(callback, state);
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                timer.Queue();
            }

            return timer;
        }
    }

    private sealed class CallbackTimer(TimerCallback callback, object? state) : ITimer
    {
        private int _disposed;

        internal void Queue() => ThreadPool.QueueUserWorkItem(_ =>
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                callback(state);
            }
        });

        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            Volatile.Read(ref _disposed) == 0;

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
