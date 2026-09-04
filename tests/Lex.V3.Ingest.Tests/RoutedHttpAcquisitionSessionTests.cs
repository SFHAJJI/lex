using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RoutedHttpAcquisitionSessionTests
{
    private const string EuQueryUri = "https://publications.europa.eu/webapi/rdf/sparql";

    [TestMethod]
    public void ProductionSurfaceAcceptsNoCallerAuthoredTransportFacts()
    {
        // Every non-private constructor or method the session declares, on itself or on any
        // nested type a caller can name (nothing inside a private nested type is reachable), with
        // every parameter that names a transport fact by type or by name.
        var forbiddenTypes = new[]
        {
            typeof(HttpMessageHandler), typeof(HttpClient), typeof(HttpRequestMessage),
            typeof(TimeProvider), typeof(DateTimeOffset), typeof(RoutedHttpEvidence),
            typeof(HeldAcquisitionReceipt),
        };
        var forbiddenNames = new[] { "attempt", "ordinal", "timestamp", "receipt" };
        var offenders = ConstructionSurface.DeclaredMembersTransitive(typeof(RoutedHttpAcquisitionSession))
            .OfType<MethodBase>()
            .Where(static member => !member.IsPrivate && !ReachesAPrivateNesting(member.DeclaringType!))
            .Where(static member => member is ConstructorInfo || member is MethodInfo { IsSpecialName: false })
            .SelectMany(member => member.GetParameters()
                .Where(parameter =>
                    forbiddenTypes.Contains(parameter.ParameterType) ||
                    forbiddenNames.Any(name => parameter.Name?.Contains(name, StringComparison.OrdinalIgnoreCase) == true))
                .Select(parameter =>
                    $"{member.DeclaringType!.Name}::{member.Name}({parameter.ParameterType.Name} {parameter.Name})"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // The session itself accepts none. What remains are the internal result types, which
        // carry the evidence the session produced out to its caller rather than accepting any in,
        // and the abstract post-header rejection, whose private protected constructor is reachable
        // only by a same-assembly subtype and whose sole minter is pinned by the audit tests. The
        // result factories cannot be closed by visibility: an enclosing type has no access to a
        // nested type's private members, so private factories would break the session's own
        // calls. What the pin tolerates is not nothing: same-assembly code, a test or a later
        // pipeline step, can mint a result and assert on it as though it were observed, against
        // the rule that evidence is minted only by its observer. Each is listed here so that a
        // new entry is a visible change, not a silent one.
        CollectionAssert.AreEqual(
            new[]
            {
                "AttemptResult::Executed(RoutedHttpEvidence evidence)",
                "PostHeaderRejection::.ctor(String durableWriteReceiptSha256)",
                // The one internal door that takes a transport and a clock, added deliberately.
                // LuxembourgRepeatedEnumerationExecutor used to reach the private constructor and
                // the private robots bootstrap by reflection FROM PRODUCTION SOURCE, which this
                // pin could not see at all: reflection names no parameter types. Trading that for
                // a named internal entry makes the widening visible here and makes a rename a
                // compile error rather than a run-time surprise. Internal is narrower than public
                // in exactly the way that matters: nothing outside this assembly can reach it, and
                // inside it the one caller is the LU executor's own test-handler seam.
                // Two entries per overload, and there are now two overloads: D1-06c-LU-2 added one
                // that also takes the store-derived robots paths the three-path ruling requires
                // (lex-event-20260904T180444431Z-13c6f8f86ddf4f02857cf4001c202143). The new
                // overload widens no transport surface -- the added parameter is a list of paths,
                // not a transport fact -- and the shorter overload now simply forwards to it.
                "RoutedHttpAcquisitionSession::StartWithTestTransportAsync(HttpMessageHandler handler)",
                "RoutedHttpAcquisitionSession::StartWithTestTransportAsync(HttpMessageHandler handler)",
                "RoutedHttpAcquisitionSession::StartWithTestTransportAsync(TimeProvider timeProvider)",
                "RoutedHttpAcquisitionSession::StartWithTestTransportAsync(TimeProvider timeProvider)",
                "StartResult::Integrity(RoutedHttpEvidence evidence)",
                "StartResult::Operational(RoutedHttpEvidence evidence)",
                "StartResult::PublisherDenied(RoutedHttpEvidence evidence)",
                "StartResult::Refused(RoutedHttpEvidence evidence)",
                "StartResult::Started(RoutedHttpEvidence evidence)",
            },
            offenders);
    }

    private static bool ReachesAPrivateNesting(Type type)
    {
        for (var enclosing = type; enclosing is not null; enclosing = enclosing.DeclaringType)
        {
            if (enclosing.IsNestedPrivate)
            {
                return true;
            }
        }

        return false;
    }

    [TestMethod]
    public void FriendConstructedPublicTupleCannotCreateASessionOrReachTheNetwork()
    {
        var genuine = EuropeanUnionRequest();
        var fake = new FriendBoundMachineRequest(
            genuine.RequestedUri,
            genuine.RenderReceipt,
            genuine.CopyRequestBody());
        var handler = new SequenceHandler((_, outbound) => DeclaredResponse(
            outbound,
            HttpStatusCode.OK,
            "User-agent: *\nAllow: /\n",
            contentType: "text/plain;charset=UTF-8"));

        var rejection = Assert.ThrowsExactly<TargetInvocationException>(() => Session(
            fake,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false));

        Assert.IsInstanceOfType<ArgumentException>(rejection.InnerException);
        Assert.AreEqual(0, handler.SendCount);
    }

    [TestMethod]
    public async Task RejectedFriendPlanItemConsumesNoOrdinalAndCannotSend()
    {
        var genuine = EuropeanUnionRequest();
        var fake = new FriendBoundMachineRequest(
            genuine.RequestedUri,
            genuine.RenderReceipt,
            genuine.CopyRequestBody());
        var handler = EuSuccessHandler();
        using var session = Session(
            genuine,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);
        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
        var sendsBeforeFake = handler.SendCount;

        Assert.ThrowsExactly<ArgumentException>(() => session.OpenPlanItem(fake));

        Assert.AreEqual(sendsBeforeFake, handler.SendCount);
        var genuineItem = session.OpenPlanItem(genuine);
        Assert.AreEqual(session.SourceProfile.FirstProductRequestOrdinal, genuineItem.RequestOrdinal);
        Assert.AreEqual(sendsBeforeFake, handler.SendCount);
        var result = await genuineItem.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        Assert.AreEqual(session.SourceProfile.FirstProductRequestOrdinal, result.Evidence?.RequestOrdinal);
        Assert.AreEqual(sendsBeforeFake + 1, handler.SendCount);
    }

    [TestMethod]
    public async Task ReservingAGenuinePlanItemDoesNoRenderCustodyOrNetworkWork()
    {
        var request = EuropeanUnionRequest();
        var renderer = new CountingRenderer(
            request.RenderReceipt.RendererProfileRef,
            request.RenderReceipt.RendererSourceRef,
            request.RequestedUri,
            request.CopyRequestBody());
        ReplaceRenderer(request, renderer);
        var custody = new MultiObjectCustodyStore();
        var handler = EuSuccessHandler();
        using var session = Session(
            request,
            handler,
            custody,
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);
        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
        Assert.AreEqual(0, renderer.RenderCount);
        var createsBeforeOpen = custody.CreateCount;
        var readsBeforeOpen = custody.ReadCount;
        var reopensBeforeOpen = custody.ReopenCount;
        var sendsBeforeOpen = handler.SendCount;

        var item = session.OpenPlanItem(request);

        Assert.AreEqual(0, renderer.RenderCount);
        Assert.AreEqual(createsBeforeOpen, custody.CreateCount);
        Assert.AreEqual(readsBeforeOpen, custody.ReadCount);
        Assert.AreEqual(reopensBeforeOpen, custody.ReopenCount);
        Assert.AreEqual(sendsBeforeOpen, handler.SendCount);
        var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        Assert.AreEqual(1, renderer.RenderCount);
        Assert.IsTrue(custody.CreateCount > createsBeforeOpen);
        Assert.IsTrue(custody.ReadCount > readsBeforeOpen);
        Assert.IsTrue(custody.ReopenCount > reopensBeforeOpen);
        Assert.AreEqual(sendsBeforeOpen + 1, handler.SendCount);
    }

    [TestMethod]
    public void PinnedHandlerDisablesRedirectDecompressionCookiesProxyAndActivityPropagation()
    {
        var factory = typeof(RoutedHttpAcquisitionSession).GetMethod(
            "CreatePinnedHandler",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new AssertFailedException("The owned handler factory is missing.");
        using var handler = (SocketsHttpHandler)(factory.Invoke(null, null)
            ?? throw new AssertFailedException("The owned handler factory returned null."));

        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.AreEqual(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.IsFalse(handler.UseCookies);
        Assert.IsFalse(handler.UseProxy);
        Assert.IsNull(handler.ActivityHeadersPropagator);
        Assert.AreEqual(0, handler.MaxResponseDrainSize);
    }

    [TestMethod]
    public void RuntimeCannotMintAHeldAcquisitionReceiptFromHttpAlone()
    {
        // No type in the runtime assembly, at any scope, mints, returns, holds or hands out a
        // held receipt: not the session, not a nested type, not a state machine, not by out or ref.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(RoutedHttpAcquisitionSession).Assembly,
                typeof(HeldAcquisitionReceipt),
                includeNonPublic: true).ToArray());
    }

    [TestMethod]
    public void DeclaredContentLengthAcceptsUnsignedDecimalIncludingLeadingZeroes()
    {
        var parser = PrivateMethod("TryGetDeclaredContentLength", BindingFlags.Static);
        var accepted = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            ["0"] = 0,
            ["00"] = 0,
            ["1"] = 1,
            ["01"] = 1,
            [ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)] = ulong.MaxValue,
        };
        foreach (var (spelling, expected) in accepted)
        {
            object?[] arguments = [new RoutedHttpSingleHeader(spelling), 0UL];
            Assert.IsTrue((bool)parser.Invoke(null, arguments)!);
            Assert.AreEqual(expected, (ulong)arguments[1]!);
        }

        var refused = new[]
        {
            string.Empty,
            "+1",
            " 1",
            "1 ",
            "١",
            "18446744073709551616",
        };
        foreach (var spelling in refused)
        {
            object?[] arguments = [new RoutedHttpSingleHeader(spelling), 0UL];
            Assert.IsFalse((bool)parser.Invoke(null, arguments)!);
        }

        object?[] multiple = [new RoutedHttpMultipleHeader(["1", "1"]), 0UL];
        Assert.IsFalse((bool)parser.Invoke(null, multiple)!);
    }

    [TestMethod]
    public async Task BodyCapRetainsExactlyOneSentinelBeyondTheCompleteCeiling()
    {
        var request = EuropeanUnionRequest();
        using var session = Session(
            request,
            new SequenceHandler(static (_, _) => throw new AssertFailedException("No send expected.")),
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        using var completeContent = new ByteArrayContent([1, 2, 3]);
        var complete = await CaptureBodyAsync(
            session,
            completeContent,
            declaredLength: 3,
            CancellationToken.None,
            maximumRetainedBytes: 4);
        Assert.AreEqual("DeclaredLengthReached", CaptureEvent(complete));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, CaptureBytes(complete));

        using var cappedContent = new ByteArrayContent([1, 2, 3, 4, 5]);
        var capped = await CaptureBodyAsync(
            session,
            cappedContent,
            declaredLength: 4,
            CancellationToken.None,
            maximumRetainedBytes: 4);
        Assert.AreEqual("CapSentinel", CaptureEvent(capped));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, CaptureBytes(capped));
    }

    [TestMethod]
    public async Task CallerCancellationOutranksDeclaredLengthReachedAfterTheSameRead()
    {
        var request = EuropeanUnionRequest();
        using var session = Session(
            request,
            new SequenceHandler(static (_, _) => throw new AssertFailedException("No send expected.")),
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);
        using var cancellation = new CancellationTokenSource();
        using var content = new StreamContent(
            new CancelAfterFirstReadStream([1, 2, 3], cancellation));

        var capture = await CaptureBodyAsync(
            session,
            content,
            declaredLength: 3,
            cancellation.Token,
            maximumRetainedBytes: 8);

        Assert.AreEqual("CallerCancelledAfterHeaders", CaptureEvent(capture));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, CaptureBytes(capture));
    }

    [TestMethod]
    public async Task RedirectHopEntersCustodyBeforeTheNextNetworkInvocation()
    {
        var request = EuropeanUnionRequest();
        var custody = new MultiObjectCustodyStore();
        var handler = new SequenceHandler((ordinal, outbound) => ordinal switch
        {
            0 => DeclaredResponse(
                outbound,
                HttpStatusCode.MovedPermanently,
                "moved",
                location: "https://op.europa.eu/robots.txt"),
            1 => RobotsResponseAfterFirstHopCustody(outbound, custody),
            _ => throw new AssertFailedException("The frozen robots route sent an extra request."),
        });
        using var session = Session(
            request,
            handler,
            custody,
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        Assert.AreSame(session, result.Session);
        Assert.IsNotNull(result.Evidence);
        Assert.AreEqual(2, result.Evidence.Hops.Count);
        Assert.IsTrue(custody.ContainsExact("moved"u8));
        Assert.IsTrue(custody.ContainsExact("User-agent: *\nAllow: /\n"u8));
        // Each hop is reopened before it enters /4, the redirect capability independently reopens
        // its antecedent, and the terminal robots consumer reopens its own body before parsing.
        Assert.AreEqual(4, custody.ReadCount);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreNotSame(handler.Requests[0], handler.Requests[1]);
    }

    [TestMethod]
    public async Task CompleteEuRobots4xxIsALocalPolicyUnavailableRefusalInsteadOfCompleteOrStale()
    {
        var request = EuropeanUnionRequest();
        var handler = new SequenceHandler(static (_, outbound) => DeclaredResponse(
            outbound,
            HttpStatusCode.NotFound,
            "missing robots policy"));
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.LocalSafetyRefusal, result.Kind);
        Assert.AreEqual(
            OfficialMachineQueryLocalSafetyReason.RobotsPolicyUnavailable,
            result.LocalSafetyReason);
        Assert.IsNull(result.OperationalReason);
        Assert.IsNull(result.Session);
        Assert.IsNotNull(result.Evidence);
        Assert.AreEqual(1, result.Evidence.Hops.Count);
        Assert.AreEqual(404, result.Evidence.Hops[0].Status);
        Assert.AreEqual(
            HttpRouteIncompleteReason.RobotsPolicyUnavailable,
            ((IncompleteHttpRouteOutcome)result.Evidence.Outcome).Reason);
        Assert.AreEqual(1, handler.SendCount);
    }

    [TestMethod]
    public async Task CompleteEuRobots5xxIsAPublisherServerOperationalFailureInsteadOfCompleteOrStale()
    {
        var request = EuropeanUnionRequest();
        var handler = new SequenceHandler(static (_, outbound) => DeclaredResponse(
            outbound,
            HttpStatusCode.InternalServerError,
            "publisher failed"));
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.OperationalFailure, result.Kind);
        Assert.AreEqual(
            OfficialHttpOperationalFailureReason.PublisherServerFailure,
            result.OperationalReason);
        Assert.IsNull(result.LocalSafetyReason);
        Assert.IsNull(result.Session);
        Assert.IsNotNull(result.Evidence);
        Assert.AreEqual(1, result.Evidence.Hops.Count);
        Assert.AreEqual(500, result.Evidence.Hops[0].Status);
        Assert.AreEqual(
            HttpRouteIncompleteReason.PublisherServerFailure,
            ((IncompleteHttpRouteOutcome)result.Evidence.Outcome).Reason);
        Assert.AreEqual(1, handler.SendCount);
    }

    [TestMethod]
    public async Task IncompleteRobotsBodyOutranksBoth4xxAnd5xxStatusReasons()
    {
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
        {
            var request = EuropeanUnionRequest();
            var handler = new SequenceHandler((_, outbound) => IncompleteResponse(outbound, status));
            using var session = Session(
                request,
                handler,
                new MultiObjectCustodyStore(),
                new ShortDelayTimeProvider(),
                usesPinnedHandler: false);

            var result = await BootstrapAsync(session);

            Assert.IsNotNull(result.Evidence);
            Assert.AreEqual((int)status, result.Evidence.Hops[0].Status);
            var completion = Assert.IsInstanceOfType<IncompleteHttpCompletion>(
                result.Evidence.Hops[0].Completion);
            Assert.AreEqual(
                HttpPartialBodyReason.BodyReadFailure,
                HttpAcquisitionReasonRegistry.RequirePartial(completion.Reason));
            Assert.AreEqual(
                HttpRouteIncompleteReason.HopIncomplete,
                ((IncompleteHttpRouteOutcome)result.Evidence.Outcome).Reason);
        }
    }

    // The robots verdict is the gate that decides whether this run may open a socket to a legal
    // publisher. The six tests below are the only ones in which robots ever says no, so they are
    // what makes the product token and the witness path arguments load-bearing: swapping either
    // for a constant keeps every allow-everything fixture green and fails one of these.

    [TestMethod]
    public async Task RobotsDisallowForEveryAgentDeniesTheWitnessPathAndOpensNoProductSocket()
    {
        var request = EuropeanUnionRequest();
        var handler = EuRobotsHandler("User-agent: *\nDisallow: /webapi/\n");
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
        Assert.IsNull(result.Session);
        Assert.IsNull(result.LocalSafetyReason);
        Assert.IsNull(result.OperationalReason);
        Assert.IsNull(result.PostHeaderRejection);
        Assert.IsNotNull(result.Evidence);
        Assert.AreEqual(2, result.Evidence.Hops.Count);
        Assert.IsInstanceOfType<CompleteHttpRouteOutcome>(result.Evidence.Outcome);
        CollectionAssert.AreEqual(
            new[] { "https://publications.europa.eu/robots.txt", "https://op.europa.eu/robots.txt" },
            handler.Requests.Select(static sent => sent.RequestUri?.AbsoluteUri).ToArray());
        // A denial grants nothing: the run is already disposed, so no plan item can be opened and
        // the socket count cannot move.
        Assert.ThrowsExactly<ObjectDisposedException>(() => session.OpenPlanItem(request));
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task RobotsLexGroupDenialOutranksAWildcardAllow()
    {
        // The only shape in which the product-token argument decides the outcome: evaluating the
        // wildcard group instead of the Lex group would allow this run.
        var request = EuropeanUnionRequest();
        var handler = EuRobotsHandler("User-agent: *\nAllow: /\n\nUser-agent: Lex\nDisallow: /\n");
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.PublisherDenial, result.Kind);
        Assert.IsNull(result.Session);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task RobotsLexGroupAllowOutranksAWildcardDisallow()
    {
        // The positive twin of the test above: the same argument swap would deny this run.
        var request = EuropeanUnionRequest();
        var handler = EuRobotsHandler("User-agent: Lex\nAllow: /\n\nUser-agent: *\nDisallow: /\n");
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        Assert.AreSame(session, result.Session);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task RobotsDisallowOfANeighbouringPathDoesNotDenyTheExactWitnessPath()
    {
        // Pins the path argument: the witness path is allowed while a one-character neighbour and
        // an unrelated tree are not, so evaluating any constant in place of the witness path
        // cannot reproduce this verdict together with the denial above.
        var request = EuropeanUnionRequest();
        var handler = EuRobotsHandler(
            "User-agent: *\nDisallow: /webapi/rdf/sparql/\nDisallow: /other/\n");
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        Assert.AreSame(session, result.Session);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task RobotsUninterpretableApplicableGroupIsALocalSafetyRefusal()
    {
        // A disallow value without a leading slash or wildcard is not a rule RFC 9309 defines, so
        // the applicable group cannot be interpreted and Lex refuses locally rather than guessing.
        var request = EuropeanUnionRequest();
        var handler = EuRobotsHandler("User-agent: *\nDisallow: webapi\n");
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.LocalSafetyRefusal, result.Kind);
        Assert.AreEqual(
            OfficialMachineQueryLocalSafetyReason.ApplicableRobotsGroupUninterpretable,
            result.LocalSafetyReason);
        Assert.IsNull(result.Session);
        Assert.IsNull(result.OperationalReason);
        Assert.IsNotNull(result.Evidence);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task RobotsUninterpretableGroupForAnotherAgentDoesNotRefuseTheRun()
    {
        // Only the applicable group's interpretability matters; a broken group for some other
        // product must not turn an explicit wildcard allow into a refusal.
        var request = EuropeanUnionRequest();
        var handler = EuRobotsHandler(
            "User-agent: Other\nDisallow: webapi\n\nUser-agent: *\nAllow: /\n");
        using var session = Session(
            request,
            handler,
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var result = await BootstrapAsync(session);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
        Assert.AreSame(session, result.Session);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task FourRetryableAttemptsUseFreshRequestsAndAFifthCannotSend()
    {
        var request = EuropeanUnionRequest();
        var custody = new MultiObjectCustodyStore();
        var handler = new SequenceHandler((ordinal, outbound) => ordinal switch
        {
            0 => DeclaredResponse(
                outbound,
                HttpStatusCode.MovedPermanently,
                "moved",
                location: "https://op.europa.eu/robots.txt"),
            1 => DeclaredResponse(
                outbound,
                HttpStatusCode.OK,
                "User-agent: *\nAllow: /\n",
                contentType: "text/plain;charset=UTF-8"),
            _ => DeclaredResponse(outbound, HttpStatusCode.ServiceUnavailable, "retry"),
        });
        using var session = Session(
            request,
            handler,
            custody,
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);
        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);
        var item = session.OpenPlanItem(request);

        for (var attempt = 0; attempt < session.SourceProfile.MaximumAttempts; attempt++)
        {
            var result = await item.ExecuteNextAttemptAsync(CancellationToken.None);
            Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, result.Kind);
            Assert.IsNotNull(result.Evidence);
            Assert.AreEqual(1UL, result.Evidence.RequestOrdinal);
            Assert.AreEqual((ulong)attempt, result.Evidence.AttemptOrdinal);
            Assert.AreEqual(503, result.Evidence.Hops[^1].Status);
        }

        var sendsBeforeRefusedAttempt = handler.SendCount;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => item.ExecuteNextAttemptAsync(CancellationToken.None));
        Assert.AreEqual(sendsBeforeRefusedAttempt, handler.SendCount);

        var productRequests = handler.Requests.Skip(2).ToArray();
        Assert.AreEqual(session.SourceProfile.MaximumAttempts, productRequests.Length);
        Assert.AreEqual(
            productRequests.Length,
            productRequests.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.IsTrue(productRequests.All(static outbound => outbound.Method == HttpMethod.Post));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MissingOrCorruptReopenedDependencyStopsBeforeProductNetwork(bool missing)
    {
        var request = EuropeanUnionRequest();
        var dependencySha256 = request.RenderReceipt.RendererSourceRef.Sha256;
        var custody = new MultiObjectCustodyStore
        {
            MissingContentSha256 = missing ? dependencySha256 : null,
            CorruptContentSha256 = missing ? null : dependencySha256,
        };
        var handler = new SequenceHandler((ordinal, outbound) => ordinal switch
        {
            0 => DeclaredResponse(
                outbound,
                HttpStatusCode.MovedPermanently,
                "moved",
                location: "https://op.europa.eu/robots.txt"),
            1 => DeclaredResponse(
                outbound,
                HttpStatusCode.OK,
                "User-agent: *\nAllow: /\n",
                contentType: "text/plain;charset=UTF-8"),
            _ => throw new AssertFailedException(
                "A request with an unverified renderer dependency reached the network."),
        });
        using var session = Session(
            request,
            handler,
            custody,
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);
        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var result = await session.OpenPlanItem(request)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.IntegrityFailure, result.Kind);
        Assert.AreEqual(2, handler.SendCount);
    }

    [TestMethod]
    public async Task TestInjectedHandlerCannotWarrantChunkedEofCompletion()
    {
        var request = EuropeanUnionRequest();
        using var session = Session(
            request,
            new SequenceHandler(static (_, _) => throw new AssertFailedException("No send expected.")),
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);
        using var content = new StreamContent(new MemoryStream([1, 2, 3], writable: false));
        var capture = await CaptureBodyAsync(
            session,
            content,
            declaredLength: null,
            CancellationToken.None,
            maximumRetainedBytes: 8);
        Assert.AreEqual("CleanEof", CaptureEvent(capture));
        var requestEvidence = (HttpLogicalRequest)PrivateMethod(
            "CreateMachineRequest",
            BindingFlags.Instance).Invoke(session, [ResolveMachineRequest(session, request)])!;
        var headers = Headers(transferEncoding: new RoutedHttpSingleHeader("chunked"));

        var wrapper = Assert.ThrowsExactly<TargetInvocationException>(() =>
            PrivateMethod("ClassifyCompletion", BindingFlags.Instance).Invoke(
                session,
                [requestEvidence, 200, headers, capture]));
        Assert.IsInstanceOfType<InvalidOperationException>(wrapper.InnerException);
    }

    private static BoundMachineRequest EuropeanUnionRequest()
        => MachineRequestTestFixture.EuropeanUnionRequest();

    private static SequenceHandler EuRobotsHandler(string policy) => new((ordinal, outbound) => ordinal switch
    {
        0 => DeclaredResponse(
            outbound,
            HttpStatusCode.MovedPermanently,
            "moved",
            location: "https://op.europa.eu/robots.txt"),
        1 => DeclaredResponse(
            outbound,
            HttpStatusCode.OK,
            policy,
            contentType: "text/plain;charset=UTF-8"),
        _ => throw new AssertFailedException(
            "The robots verdict must decide the run before any product send."),
    });

    private static SequenceHandler EuSuccessHandler() => new((ordinal, outbound) => ordinal switch
    {
        0 => DeclaredResponse(
            outbound,
            HttpStatusCode.MovedPermanently,
            "moved",
            location: "https://op.europa.eu/robots.txt"),
        1 => DeclaredResponse(
            outbound,
            HttpStatusCode.OK,
            "User-agent: *\nAllow: /\n",
            contentType: "text/plain;charset=UTF-8"),
        _ => DeclaredResponse(outbound, HttpStatusCode.OK, "{\"results\":{\"bindings\":[]}}"),
    });

    private static void ReplaceRenderer(
        BoundMachineRequest request,
        IMachineQueryRenderer renderer)
    {
        var field = request.GetType().GetField(
            "<Renderer>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The binder capability retained no renderer.");
        field.SetValue(request, renderer);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static RoutedHttpAcquisitionSession Session(
        BoundMachineRequest request,
        HttpMessageHandler handler,
        ICustodyStore custody,
        TimeProvider timeProvider,
        bool usesPinnedHandler)
    {
        var constructor = typeof(RoutedHttpAcquisitionSession).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (RoutedHttpAcquisitionSession)constructor.Invoke(
            [request, custody, handler, timeProvider, usesPinnedHandler, Array.Empty<string>()]);
    }

    private static Task<RoutedHttpAcquisitionSession.StartResult> BootstrapAsync(
        RoutedHttpAcquisitionSession session) =>
        (Task<RoutedHttpAcquisitionSession.StartResult>)PrivateMethod(
            "BootstrapRobotsAsync",
            BindingFlags.Instance).Invoke(session, [CancellationToken.None])!;

    private static MethodInfo PrivateMethod(string name, BindingFlags scope) =>
        typeof(RoutedHttpAcquisitionSession).GetMethod(name, BindingFlags.NonPublic | scope)
        ?? throw new AssertFailedException($"The runtime seam '{name}' is missing.");

    [TestMethod]
    public void AMachineRequestMissingARequiredArtifactRoleIsRefusedBeforeAPolicyIsMinted()
    {
        // The role-membership guard in ForMachineQuery is a tripwire between two components: the
        // binder in Contracts decides what it reopens, the policy in Ingest decides what it
        // requires, and they agree today. This drops the render receipt from an otherwise genuine
        // reopened closure so the guard is exercised rather than trusted.
        var request = EuropeanUnionRequest();
        using var session = Session(
            request,
            new SequenceHandler(static (_, _) => throw new AssertFailedException("No send expected.")),
            new MultiObjectCustodyStore(),
            new ShortDelayTimeProvider(),
            usesPinnedHandler: false);

        var refusal = Assert.ThrowsExactly<TargetInvocationException>(() =>
            PrivateMethod("CreateMachineRequest", BindingFlags.Instance).Invoke(
                session,
                [ResolveMachineRequest(session, request, dropRenderReceipt: true)]));
        Assert.IsInstanceOfType<ArgumentException>(refusal.InnerException);
        StringAssert.Contains(refusal.InnerException!.Message, "required artifact role");

        // The complete closure still mints, so the refusal above is the guard and not the seam.
        Assert.IsNotNull(PrivateMethod("CreateMachineRequest", BindingFlags.Instance).Invoke(
            session,
            [ResolveMachineRequest(session, request)]));
    }

    private static object ResolveMachineRequest(
        RoutedHttpAcquisitionSession session,
        BoundMachineRequest request,
        bool dropRenderReceipt = false)
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
        if (dropRenderReceipt)
        {
            // The element type is private to the session, so the closure is filtered through
            // reflection and rebuilt as a typed array the private constructor accepts.
            var all = ((System.Collections.IEnumerable)artifacts).Cast<object>().ToArray();
            var elementType = all[0].GetType();
            var reference = elementType.GetProperty("Reference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new AssertFailedException("The reopened artifact carries no reference.");
            var kept = all.Where(artifact => !Equals(reference.GetValue(artifact), opened.RenderReceiptRef)).ToArray();
            Assert.AreEqual(all.Length - 1, kept.Length, "exactly the render receipt was dropped");
            var typed = Array.CreateInstance(elementType, kept.Length);
            Array.Copy(kept, typed, kept.Length);
            artifacts = typed;
        }

        var resolvedType = typeof(RoutedHttpAcquisitionSession).GetNestedType(
            "ResolvedMachineRequest",
            BindingFlags.NonPublic)
            ?? throw new AssertFailedException("The resolved machine request type is missing.");
        return resolvedType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(static constructor =>
                constructor.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(OpenedMachineRequest))
            .Invoke([opened, artifacts]);
    }

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

    private static string CaptureEvent(object capture) =>
        capture.GetType().GetProperty("Event")?.GetValue(capture)?.ToString()
        ?? throw new AssertFailedException("The body capture returned no terminal event.");

    private static byte[] CaptureBytes(object capture) =>
        ((ReadOnlyMemory<byte>)(capture.GetType().GetProperty("Bytes")?.GetValue(capture)
            ?? throw new AssertFailedException("The body capture returned no bytes."))).ToArray();

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

    private static HttpResponseMessage RobotsResponseAfterFirstHopCustody(
        HttpRequestMessage request,
        MultiObjectCustodyStore custody)
    {
        Assert.IsTrue(custody.ContainsExact("moved"u8));
        Assert.AreEqual(2, custody.ReadCount);
        return DeclaredResponse(
            request,
            HttpStatusCode.OK,
            "User-agent: *\nAllow: /\n",
            contentType: "text/plain");
    }

    private static HttpResponseMessage DeclaredResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string body,
        string? location = null,
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
        if (location is not null)
        {
            Assert.IsTrue(response.Headers.TryAddWithoutValidation("Location", location));
        }

        return response;
    }

    private static HttpResponseMessage IncompleteResponse(
        HttpRequestMessage request,
        HttpStatusCode status) => new(status)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = new StreamContent(new FailingReadStream()),
        };

    private sealed class SequenceHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = [];

        internal int SendCount => Requests.Count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Requests.Count;
            Requests.Add(request);
            return Task.FromResult(respond(ordinal, request));
        }
    }

    private sealed class FriendBoundMachineRequest(
        string requestedUri,
        MachineQueryRenderReceipt renderReceipt,
        byte[] requestBody) : BoundMachineRequest
    {
        public override string RequestedUri { get; } = requestedUri;

        public override MachineQueryRenderReceipt RenderReceipt { get; } = renderReceipt;

        public override byte[] CopyRequestBody() => requestBody.ToArray();
    }

    private sealed class CountingRenderer(
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        string requestedUri,
        byte[] requestBody) : IMachineQueryRenderer
    {
        public SourceArtifactRef RendererProfileRef { get; } = rendererProfileRef;

        public SourceArtifactRef RendererSourceRef { get; } = rendererSourceRef;

        internal int RenderCount { get; private set; }

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan,
            MachineQueryInputArtifact orderedParameterSet)
        {
            RenderCount++;
            return new MachineQueryRenderOutput(requestedUri, requestBody);
        }
    }

    internal sealed class MultiObjectCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        internal string? MissingContentSha256 { get; init; }

        internal string? CorruptContentSha256 { get; init; }

        internal int CreateCount { get; private set; }

        internal int ReadCount { get; private set; }

        internal int ReopenCount { get; private set; }

        internal bool ContainsExact(ReadOnlySpan<byte> expected)
        {
            foreach (var bytes in _objects.Values)
            {
                if (bytes.AsSpan().SequenceEqual(expected))
                {
                    return true;
                }
            }

            return false;
        }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen, cancellationToken);
            _objects[digest] = frozen;
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
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                policy));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
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
            ReopenCount++;
            if (string.Equals(contentSha256, MissingContentSha256, StringComparison.Ordinal))
            {
                throw new FileNotFoundException("The requested fixture artifact is absent.");
            }

            byte[] bytes;
            if (_objects.TryGetValue(contentSha256, out var retained))
            {
                bytes = retained.ToArray();
            }
            else if (MachineRequestTestFixture.TryReopenPreexistingArtifact(
                         contentSha256,
                         out var preexisting))
            {
                bytes = preexisting.ToArray();
            }
            else
            {
                throw new AssertFailedException("Custody reopen requested an unknown digest.");
            }

            if (string.Equals(contentSha256, CorruptContentSha256, StringComparison.Ordinal))
            {
                bytes[0] ^= 0xff;
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }

    private sealed class CancelAfterFirstReadStream(
        byte[] bytes,
        CancellationTokenSource cancellation) : Stream
    {
        private bool _read;

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
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_read)
            {
                return ValueTask.FromResult(0);
            }

            _read = true;
            bytes.CopyTo(buffer);
            cancellation.Cancel();
            return ValueTask.FromResult(bytes.Length);
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

    internal sealed class ShortDelayTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(
            2026,
            9,
            2,
            10,
            0,
            0,
            TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            Epoch.AddTicks(Interlocked.Read(ref _timestamp));

        public override long GetTimestamp() =>
            Interlocked.Add(ref _timestamp, TimeSpan.FromSeconds(2).Ticks);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ShortDelayTimer(this, callback, state);
            _ = timer.Change(dueTime, period);
            return timer;
        }

        private void Advance(TimeSpan value)
        {
            if (value > TimeSpan.Zero)
            {
                Interlocked.Add(ref _timestamp, value.Ticks);
            }
        }

        private sealed class ShortDelayTimer(
            ShortDelayTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    dueTime == Timeout.InfiniteTimeSpan ||
                    dueTime > TimeSpan.FromSeconds(30))
                {
                    return Volatile.Read(ref _disposed) == 0;
                }

                owner.Advance(dueTime);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        callback(state);
                    }
                });
                return true;
            }

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
