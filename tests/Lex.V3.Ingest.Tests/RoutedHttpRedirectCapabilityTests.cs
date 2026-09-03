using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RoutedHttpRedirectCapabilityTests
{
    [TestMethod]
    public void SendLeaseCanOnlyReachItsPrivateSendCoreThroughRetainAndSend()
    {
        var leaseType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "SendLease",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The private send capability is missing.");
        var retainedType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "RetainedSendArtifacts",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The private retention capability is missing.");
        var antecedentType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "RedirectAntecedentCapability",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The private antecedent capability is missing.");

        Assert.IsTrue(leaseType.IsNestedPrivate);
        Assert.IsTrue(retainedType.IsNestedPrivate);
        Assert.IsTrue(antecedentType.IsNestedPrivate);
        Assert.IsTrue(leaseType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .All(static constructor => constructor.IsPrivate));
        Assert.IsFalse(leaseType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Any());

        var factories = leaseType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.ReturnType == leaseType)
            .Select(static method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(new[] { "FromRedirect", "Initial" }, factories);
        var redirectFactory = leaseType.GetMethod(
            "FromRedirect",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The redirect capability factory is missing.");
        var redirectParameters = redirectFactory.GetParameters();
        Assert.AreEqual(7, redirectParameters.Length);
        Assert.AreEqual(antecedentType, redirectParameters[^1].ParameterType);
        Assert.IsFalse(redirectParameters.Any(static parameter =>
            parameter.ParameterType == typeof(RoutedHttpHop)));

        var nonPrivateSendOperations = leaseType.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly)
            .Where(static method =>
                method.Name.Contains("Send", StringComparison.Ordinal) && !method.IsPrivate)
            .Select(static method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(new[] { "RetainAndSendAsync" }, nonPrivateSendOperations);
        Assert.IsNull(leaseType.GetMethod(
            "ConsumeAndSendAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        var sendEntry = leaseType.GetMethod(
            "RetainAndSendAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The retain-and-send entry point is missing.");
        Assert.IsTrue(sendEntry.IsAssembly);

        var sendCore = leaseType.GetMethod(
            "ConsumeAndSendCoreAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The private send core is missing.");
        Assert.IsTrue(sendCore.IsPrivate);
        Assert.AreEqual(retainedType, sendCore.GetParameters()[0].ParameterType);
    }

    [TestMethod]
    public async Task ARedirectThatCannotProveItsExactAntecedentNeverSendsHopOne()
    {
        var cases = new (string Name, Func<HttpRequestMessage, HttpResponseMessage> Response)[]
        {
            ("missing location", request => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "moved")),
            ("multiple locations", request => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "moved",
                locations:
                [
                    "https://op.europa.eu/robots.txt",
                    "https://publications.europa.eu/other",
                ])),
            ("relative location", request => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "moved",
                locations: ["/robots.txt"])),
            ("scheme downgrade", request => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "moved",
                locations: ["http://op.europa.eu/robots.txt"])),
            ("wrong frozen location", request => DeclaredResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "moved",
                locations: ["https://example.invalid/robots.txt"])),
            ("status 300", request => DeclaredResponse(
                request,
                HttpStatusCode.MultipleChoices,
                "choose",
                locations: ["https://op.europa.eu/robots.txt"])),
            ("incomplete redirect body", request => IncompleteResponse(
                request,
                HttpStatusCode.MovedPermanently,
                "https://op.europa.eu/robots.txt")),
        };

        foreach (var candidate in cases)
        {
            var request = MachineRequestTestFixture.EuropeanUnionRequest();
            var handler = new CountingHandler((_, outbound) => candidate.Response(outbound));
            using var session = Session(
                request,
                handler,
                new TestCustodyStore(),
                new IsolatedTimeProvider());

            _ = await BootstrapAsync(session);

            Assert.AreEqual(1, handler.SendCount, candidate.Name);
        }
    }

    [TestMethod]
    public async Task RedirectAntecedentReadFailurePreventsTheDependentSend()
    {
        var request = MachineRequestTestFixture.EuropeanUnionRequest();
        var custody = new TestCustodyStore { FailOnReadNumber = 2 };
        var handler = new CountingHandler(static (_, outbound) => DeclaredResponse(
            outbound,
            HttpStatusCode.MovedPermanently,
            "moved",
            locations: ["https://op.europa.eu/robots.txt"]));
        using var session = Session(request, handler, custody, new IsolatedTimeProvider());

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(OfficialHttpOperationalFailureReason.CustodyUnavailable, result.OperationalReason);
        Assert.AreEqual(1, handler.SendCount);
        Assert.IsTrue(custody.CreateCount > 0);
        Assert.AreEqual(2, custody.ReadCount);
    }

    [TestMethod]
    public async Task OneRedirectLeaseCannotSendTwice()
    {
        var request = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler(static (_, outbound) => DeclaredResponse(
            outbound,
            HttpStatusCode.OK,
            "User-agent: *\nAllow: /\n",
            contentType: "text/plain"));
        var time = new IsolatedTimeProvider();
        using var session = Session(request, handler, new TestCustodyStore(), time);
        SetGenerationStart(session, time);
        var lease = await MintRedirectLeaseAsync(session);

        var invocation = await RetainAndSendAsync(lease);
        DisposeInvocation(invocation);
        Assert.AreEqual(1, handler.SendCount);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RetainAndSendAsync(lease));
        Assert.AreEqual(1, handler.SendCount);
    }

    [TestMethod]
    public async Task InitialHopLeaseCannotSendTwice()
    {
        var request = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler(static (_, outbound) => DeclaredResponse(
            outbound,
            HttpStatusCode.OK,
            "User-agent: *\nAllow: /\n",
            contentType: "text/plain"));
        using var session = Session(
            request,
            handler,
            new TestCustodyStore(),
            new IsolatedTimeProvider());
        var lease = MintInitialLease(session);

        var invocation = await RetainAndSendAsync(lease);
        DisposeInvocation(invocation);
        Assert.AreEqual(1, handler.SendCount);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RetainAndSendAsync(lease));
        Assert.AreEqual(1, handler.SendCount);
    }

    [TestMethod]
    public async Task ACallerForgedHopCannotReplaceTheRegisteredAntecedent()
    {
        var request = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler(static (_, _) =>
            throw new AssertFailedException("No send expected."));
        using var session = Session(
            request,
            handler,
            new TestCustodyStore(),
            new IsolatedTimeProvider());
        var fixture = await CreateAntecedentAsync(session);
        RegisterAntecedent(session, fixture.CustodyKey, fixture.Hop);

        const string forgedTarget = "https://example.invalid/robots.txt";
        var forgedHop = CopyHopWithLocation(fixture.Hop, forgedTarget);
        AssertPrivateOperationRefuses(() =>
            RegisterAntecedent(session, fixture.CustodyKey, forgedHop));

        var capability = OpenAntecedent(session, fixture.CustodyKey);
        AssertFactoryRefuses(() => FromRedirect(
            session,
            CreateNextRequest(session, uri: forgedTarget),
            capability,
            requestOrdinal: 0,
            attemptOrdinal: 0,
            nextHopOrdinal: 1));
        Assert.AreEqual(0, handler.SendCount);
    }

    [TestMethod]
    public async Task RedirectAntecedentCapabilityIsSessionBoundAndOneUse()
    {
        var request = MachineRequestTestFixture.EuropeanUnionRequest();
        using var first = Session(
            request,
            new CountingHandler(static (_, _) => throw new AssertFailedException("No send expected.")),
            new TestCustodyStore(),
            new IsolatedTimeProvider());
        using var second = Session(
            MachineRequestTestFixture.EuropeanUnionRequest(),
            new CountingHandler(static (_, _) => throw new AssertFailedException("No send expected.")),
            new TestCustodyStore(),
            new IsolatedTimeProvider());

        var crossSession = await CreateOpenedAntecedentAsync(first);
        AssertFactoryRefuses(() => FromRedirect(
            second,
            CreateNextRequest(second),
            crossSession,
            requestOrdinal: 0,
            attemptOrdinal: 0,
            nextHopOrdinal: 1));
        AssertFactoryRefuses(() => FromRedirect(
            first,
            CreateNextRequest(first),
            crossSession,
            requestOrdinal: 0,
            attemptOrdinal: 0,
            nextHopOrdinal: 1));

        var oneUse = await CreateOpenedAntecedentAsync(first);
        _ = FromRedirect(
            first,
            CreateNextRequest(first),
            oneUse,
            requestOrdinal: 0,
            attemptOrdinal: 0,
            nextHopOrdinal: 1);
        AssertFactoryRefuses(() => FromRedirect(
            first,
            CreateNextRequest(first),
            oneUse,
            requestOrdinal: 0,
            attemptOrdinal: 0,
            nextHopOrdinal: 1));
    }

    [TestMethod]
    public async Task RedirectFactoryRefusesWrongTargetAndEveryWrongOrdinal()
    {
        var request = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler(static (_, _) =>
            throw new AssertFailedException("No send expected."));
        using var session = Session(
            request,
            handler,
            new TestCustodyStore(),
            new IsolatedTimeProvider());

        var wrongTarget = await CreateOpenedAntecedentAsync(session);
        AssertFactoryRefuses(() => FromRedirect(
            session,
            CreateNextRequest(session, uri: session.SourceProfile.RobotsRoute.Steps[0].RequestedUri),
            wrongTarget,
            requestOrdinal: 0,
            attemptOrdinal: 0,
            nextHopOrdinal: 1));

        foreach (var ordinals in new[]
                 {
                     (Request: 1UL, Attempt: 0UL, Hop: 1UL),
                     (Request: 0UL, Attempt: 1UL, Hop: 1UL),
                     (Request: 0UL, Attempt: 0UL, Hop: 2UL),
                 })
        {
            var candidate = await CreateOpenedAntecedentAsync(session);
            AssertFactoryRefuses(() => FromRedirect(
                session,
                CreateNextRequest(session),
                candidate,
                ordinals.Request,
                ordinals.Attempt,
                ordinals.Hop));
        }

        Assert.AreEqual(0, handler.SendCount);
    }

    [TestMethod]
    public async Task NetworkSendDoesNotStartUntilArtifactRetentionCloses()
    {
        var request = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler(static (_, outbound) => DeclaredResponse(
            outbound,
            HttpStatusCode.OK,
            "User-agent: *\nAllow: /\n",
            contentType: "text/plain"));
        var custody = new TestCustodyStore();
        custody.GateNextCreate();
        using var session = Session(request, handler, custody, new IsolatedTimeProvider());
        var lease = MintInitialLease(session);

        var sendTask = RetainAndSendAsync(lease);
        await custody.WaitForGatedCreateAsync();

        Assert.AreEqual(0, handler.SendCount);
        Assert.IsFalse(sendTask.IsCompleted);

        custody.ReleaseGatedCreate();
        var invocation = await sendTask;
        DisposeInvocation(invocation);
        Assert.AreEqual(1, handler.SendCount);
    }

    private static RoutedHttpAcquisitionSession Session(
        BoundMachineRequest request,
        HttpMessageHandler handler,
        ICustodyStore custody,
        TimeProvider timeProvider)
    {
        var constructor = typeof(RoutedHttpAcquisitionSession).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (RoutedHttpAcquisitionSession)constructor.Invoke(
            [request, custody, handler, timeProvider, false]);
    }

    private static Task<RoutedHttpAcquisitionSession.StartResult> BootstrapAsync(
        RoutedHttpAcquisitionSession session) =>
        (Task<RoutedHttpAcquisitionSession.StartResult>)SessionMethod(
            "BootstrapRobotsAsync").Invoke(session, [CancellationToken.None])!;

    private static void SetGenerationStart(
        RoutedHttpAcquisitionSession session,
        TimeProvider timeProvider) =>
        SessionMethod("SetRobotsGenerationStart").Invoke(
            session,
            [timeProvider.GetUtcNow(), timeProvider.GetTimestamp()]);

    private static async Task<object> MintRedirectLeaseAsync(RoutedHttpAcquisitionSession session)
    {
        var fixture = await CreateOpenedAntecedentAsync(session);
        return FromRedirect(
            session,
            CreateNextRequest(session),
            fixture,
            requestOrdinal: 0,
            attemptOrdinal: 0,
            nextHopOrdinal: 1);
    }

    private static object MintInitialLease(RoutedHttpAcquisitionSession session)
    {
        var request = (HttpLogicalRequest)(SessionMethod("CreateRobotsRequest").Invoke(
            session,
            [session.SourceProfile.RobotsRoute.Steps[0].RequestedUri])
            ?? throw new AssertFailedException("The robots request was not created."));
        var leaseType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "SendLease",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The private send capability is missing.");
        var factory = leaseType.GetMethod(
            "Initial",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The initial capability factory is missing.");
        return factory.Invoke(
            null,
            [session, request, ReadOnlyMemory<byte>.Empty, 0UL, 0UL, true])
            ?? throw new AssertFailedException("The initial send capability was not minted.");
    }

    private static async Task<AntecedentFixture> CreateAntecedentAsync(
        RoutedHttpAcquisitionSession session)
    {
        var request = (HttpLogicalRequest)(SessionMethod("CreateRobotsRequest").Invoke(
            session,
            [session.SourceProfile.RobotsRoute.Steps[0].RequestedUri])
            ?? throw new AssertFailedException("The robots request was not created."));
        var bytes = Encoding.UTF8.GetBytes("moved");
        var observationId = $"urn:uuid:{Guid.NewGuid():D}";
        var holdTask = (Task)(SessionMethod("HoldAndResolveAsync").Invoke(
            session,
            [new ReadOnlyMemory<byte>(bytes), 0UL, 0UL, 0UL, observationId])
            ?? throw new AssertFailedException("The antecedent custody operation was not started."));
        await holdTask;
        var heldPair = holdTask.GetType().GetProperty("Result")?.GetValue(holdTask)
            ?? throw new AssertFailedException("The antecedent custody operation returned no result.");
        var custodyKey = heldPair.GetType().GetField("Item1")?.GetValue(heldPair)
            ?? throw new AssertFailedException("The antecedent custody key is missing.");
        var heldBody = heldPair.GetType().GetField("Item2")?.GetValue(heldPair)
            ?? throw new AssertFailedException("The antecedent held body is missing.");
        var receiptSha256 = (string)(heldBody.GetType().GetProperty(
            "ReceiptSha256",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(heldBody)
            ?? throw new AssertFailedException("The antecedent custody receipt digest is missing."));
        var bodySha256 = Sha256(bytes);
        var absent = new RoutedHttpAbsentHeader();
        var headers = new RoutedHttpResponseHeaders(
            absent,
            new RoutedHttpSingleHeader(bytes.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
            absent,
            absent,
            absent,
            absent,
            absent,
            new RoutedHttpSingleHeader("https://op.europa.eu/robots.txt"),
            absent,
            absent,
            absent,
            absent,
            absent);
        var hop = RoutedHttpHop.Create(
            0,
            observationId,
            null,
            Sha256(request.CopyCanonicalBytes()),
            request.Uri,
            301,
            headers,
            "2026-09-03T00:00:00.0000000Z",
            "2026-09-03T00:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(checked((ulong)bytes.Length)),
            checked((ulong)bytes.Length),
            bodySha256,
            receiptSha256,
            checked((ulong)bytes.Length),
            bodySha256);
        return new AntecedentFixture(hop, custodyKey);
    }

    private static async Task<object> CreateOpenedAntecedentAsync(
        RoutedHttpAcquisitionSession session)
    {
        var fixture = await CreateAntecedentAsync(session);
        RegisterAntecedent(session, fixture.CustodyKey, fixture.Hop);
        return OpenAntecedent(session, fixture.CustodyKey);
    }

    private static void RegisterAntecedent(
        RoutedHttpAcquisitionSession session,
        object custodyKey,
        RoutedHttpHop hop) =>
        SessionMethod("RegisterRetainedHop").Invoke(session, [custodyKey, hop]);

    private static object OpenAntecedent(
        RoutedHttpAcquisitionSession session,
        object custodyKey) =>
        SessionMethod("OpenRedirectAntecedent").Invoke(session, [custodyKey])
        ?? throw new AssertFailedException("The session did not issue an antecedent capability.");

    private static RoutedHttpHop CopyHopWithLocation(RoutedHttpHop source, string location)
    {
        var headers = source.Headers;
        return RoutedHttpHop.Create(
            source.Ordinal,
            source.ObservationId,
            source.AntecedentHopObservationId,
            source.LogicalRequestSha256,
            source.RequestUri,
            source.Status,
            new RoutedHttpResponseHeaders(
                headers.ContentType,
                headers.ContentLength,
                headers.ContentEncoding,
                headers.TransferEncoding,
                headers.ContentRange,
                headers.Etag,
                headers.LastModified,
                new RoutedHttpSingleHeader(location),
                headers.CacheControl,
                headers.Expires,
                headers.Date,
                headers.Age,
                headers.Tcn),
            source.RequestStartedAt,
            source.TerminalObservedAt,
            source.Completion,
            source.Length,
            source.Sha256,
            source.DurableWriteReceiptSha256,
            source.ReadbackByteLength,
            source.ReadbackSha256);
    }

    private static HttpLogicalRequest CreateNextRequest(
        RoutedHttpAcquisitionSession session,
        string? redirectPolicySha256 = null,
        string? uri = null)
    {
        var initial = (HttpLogicalRequest)(SessionMethod("CreateRobotsRequest").Invoke(
            session,
            [session.SourceProfile.RobotsRoute.Steps[0].RequestedUri])
            ?? throw new AssertFailedException("The robots request was not created."));
        return HttpLogicalRequest.Create(
            uri ?? "https://op.europa.eu/robots.txt",
            initial.Method,
            initial.Headers,
            initial.Body,
            initial.RequestPolicySha256,
            redirectPolicySha256 ?? initial.RedirectPolicySha256);
    }

    private static object FromRedirect(
        RoutedHttpAcquisitionSession session,
        HttpLogicalRequest request,
        object antecedentCapability,
        ulong requestOrdinal,
        ulong attemptOrdinal,
        ulong nextHopOrdinal)
    {
        var leaseType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "SendLease",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The private send capability is missing.");
        var factory = leaseType.GetMethod(
            "FromRedirect",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The redirect capability factory is missing.");
        return factory.Invoke(
            null,
            [
                session,
                request,
                ReadOnlyMemory<byte>.Empty,
                requestOrdinal,
                attemptOrdinal,
                nextHopOrdinal,
                antecedentCapability,
            ]) ?? throw new AssertFailedException("The redirect capability was not minted.");
    }

    private static async Task<object> RetainAndSendAsync(object lease)
    {
        var retainAndSend = lease.GetType().GetMethod(
            "RetainAndSendAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The private retain-and-send operation is missing.");
        var task = (Task)(retainAndSend.Invoke(lease, [CancellationToken.None])
            ?? throw new AssertFailedException("The retain-and-send operation was not started."));
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new AssertFailedException("The retain-and-send operation returned no result.");
    }

    private static void DisposeInvocation(object invocation)
    {
        foreach (var name in new[] { "Response", "OutboundRequest" })
        {
            if (invocation.GetType().GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(invocation) is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static void AssertFactoryRefuses(Func<object> action)
    {
        var wrapper = Assert.ThrowsExactly<TargetInvocationException>(() => _ = action());
        Assert.IsInstanceOfType<InvalidOperationException>(wrapper.InnerException);
    }

    private static void AssertPrivateOperationRefuses(Action action)
    {
        var wrapper = Assert.ThrowsExactly<TargetInvocationException>(action);
        Assert.IsInstanceOfType<InvalidOperationException>(wrapper.InnerException);
    }

    private static MethodInfo SessionMethod(string name) =>
        typeof(RoutedHttpAcquisitionSession).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new AssertFailedException($"The runtime seam '{name}' is missing.");

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage DeclaredResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string body,
        IReadOnlyList<string>? locations = null,
        string? contentType = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length",
            bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (contentType is not null)
        {
            Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Type", contentType));
        }

        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
        if (locations is not null)
        {
            Assert.IsTrue(response.Headers.TryAddWithoutValidation("Location", locations));
        }

        return response;
    }

    private static HttpResponseMessage IncompleteResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string location)
    {
        var response = new HttpResponseMessage(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = new StreamContent(new FailingReadStream()),
        };
        Assert.IsTrue(response.Headers.TryAddWithoutValidation("Location", location));
        return response;
    }

    private sealed record AntecedentFixture(
        RoutedHttpHop Hop,
        object CustodyKey);

    private sealed class CountingHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = SendCount++;
            return Task.FromResult(response(ordinal, request));
        }
    }

    private sealed class TestCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource<bool> _gatedCreateStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _gatedCreateReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _gateNextCreate;

        internal int CreateCount { get; private set; }

        internal int ReadCount { get; private set; }

        internal int? FailOnReadNumber { get; init; }

        internal void GateNextCreate() => _gateNextCreate = true;

        internal Task WaitForGatedCreateAsync() =>
            _gatedCreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        internal void ReleaseGatedCreate() => _gatedCreateReleased.TrySetResult(true);

        public async Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_gateNextCreate)
            {
                _gateNextCreate = false;
                _gatedCreateStarted.TrySetResult(true);
                await _gatedCreateReleased.Task.WaitAsync(cancellationToken);
            }

            CreateCount++;
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen, cancellationToken);
            _objects[digest] = frozen;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                digest,
                frozen.Length,
                custodyClass);
            var observed = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-000000000041"),
                CustodyProtection.LockedTime,
                observed,
                observed.AddDays(91));
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                policy);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (ReadCount == FailOnReadNumber)
            {
                throw new IOException("Injected antecedent custody read failure.");
            }

            if (!_objects.TryGetValue(reference.ContentSha256, out var bytes))
            {
                throw new AssertFailedException("Custody readback requested an unknown object.");
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
        }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_objects.TryGetValue(contentSha256, out var bytes))
            {
                if (MachineRequestTestFixture.TryReopenPreexistingArtifact(
                        contentSha256,
                        out var preexisting))
                {
                    return Task.FromResult(preexisting);
                }

                throw new AssertFailedException(
                    "Content-addressed reopening requested an unknown artifact.");
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
        }
    }

    private sealed class IsolatedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();

        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();

        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) => TimeProvider.System.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class FailingReadStream : Stream
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
            ValueTask.FromException<int>(new IOException("Injected body read failure."));

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Injected body read failure.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
