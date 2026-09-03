using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RoutedHttpRequestPolicyAuditTests
{
    [TestMethod]
    public void RobotsGetAndMachinePostOpenDistinctExactPolicies()
    {
        var bound = MachineRequestTestFixture.EuropeanUnionRequest();
        using var session = Session(bound, new CountingHandler(), new MemoryCustodyStore());
        var robots = RobotsRequest(session);
        var machine = MachineRequest(session, bound);

        Assert.AreNotEqual(robots.RequestPolicySha256, machine.RequestPolicySha256);
        Assert.AreNotEqual(robots.RedirectPolicySha256, machine.RedirectPolicySha256);

        var robotsPolicy = Policy(session, "_requestPolicies", robots.RequestPolicySha256);
        var machinePolicy = Policy(session, "_requestPolicies", machine.RequestPolicySha256);
        var robotsBytes = PolicyBytes(robotsPolicy);
        var machineBytes = PolicyBytes(machinePolicy);
        CollectionAssert.AreNotEqual(robotsBytes, machineBytes);
        StringAssert.Contains(Encoding.UTF8.GetString(robotsBytes), "\nrobots_get\n");
        StringAssert.Contains(Encoding.UTF8.GetString(machineBytes), "\nmachine_query_post\n");
        Assert.IsFalse(Encoding.UTF8.GetString(robotsBytes).Contains("\nquery_plan=", StringComparison.Ordinal));
        StringAssert.Contains(Encoding.UTF8.GetString(machineBytes), "\nquery_plan=");

        var robotsRedirect = Encoding.UTF8.GetString(PolicyBytes(
            Policy(session, "_redirectPolicies", robots.RedirectPolicySha256)));
        var machineRedirect = Encoding.UTF8.GetString(PolicyBytes(
            Policy(session, "_redirectPolicies", machine.RedirectPolicySha256)));
        Assert.IsFalse(robotsRedirect.Contains("\nno_redirect\n", StringComparison.Ordinal));
        StringAssert.Contains(machineRedirect, "\nno_redirect\n");
    }

    [TestMethod]
    public async Task UnknownOrSwappedRequestPolicyCannotReachTheHandler()
    {
        var bound = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler();
        using var session = Session(bound, handler, new MemoryCustodyStore());
        var robots = RobotsRequest(session);
        var machineBody = bound.CopyRequestBody();
        var machine = MachineRequest(session, bound);
        var unknown = UnknownSha(robots.RequestPolicySha256, machine.RequestPolicySha256);

        var vectors = new[]
        {
            ("robots unknown", Clone(robots, requestBody: ReadOnlyMemory<byte>.Empty, requestPolicySha256: unknown), Array.Empty<byte>(), session.SourceProfile.RobotsRoute),
            ("robots opens machine", Clone(robots, requestBody: ReadOnlyMemory<byte>.Empty, requestPolicySha256: machine.RequestPolicySha256), Array.Empty<byte>(), session.SourceProfile.RobotsRoute),
            ("machine unknown", Clone(machine, requestBody: machineBody, requestPolicySha256: unknown), machineBody, null),
            ("machine opens robots", Clone(machine, requestBody: machineBody, requestPolicySha256: robots.RequestPolicySha256), machineBody, null),
        };

        foreach (var (name, request, body, route) in vectors)
        {
            await AssertRefusedBeforeSend(session, handler, request, body, route, name);
        }
    }

    [TestMethod]
    public async Task UnknownOrSwappedRedirectPolicyCannotReachTheHandler()
    {
        var bound = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler();
        using var session = Session(bound, handler, new MemoryCustodyStore());
        var robots = RobotsRequest(session);
        var machineBody = bound.CopyRequestBody();
        var machine = MachineRequest(session, bound);
        var unknown = UnknownSha(robots.RedirectPolicySha256, machine.RedirectPolicySha256);

        var vectors = new[]
        {
            ("robots unknown", Clone(robots, requestBody: ReadOnlyMemory<byte>.Empty, redirectPolicySha256: unknown), Array.Empty<byte>(), session.SourceProfile.RobotsRoute),
            ("robots opens no-redirect", Clone(robots, requestBody: ReadOnlyMemory<byte>.Empty, redirectPolicySha256: machine.RedirectPolicySha256), Array.Empty<byte>(), session.SourceProfile.RobotsRoute),
            ("machine unknown", Clone(machine, requestBody: machineBody, redirectPolicySha256: unknown), machineBody, null),
            ("machine opens robots route", Clone(machine, requestBody: machineBody, redirectPolicySha256: robots.RedirectPolicySha256), machineBody, null),
        };

        foreach (var (name, request, body, route) in vectors)
        {
            await AssertRefusedBeforeSend(session, handler, request, body, route, name);
        }
    }

    [TestMethod]
    public async Task EveryMachineRequestFieldMustReproduceItsOpenedPolicyBeforeSend()
    {
        var bound = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler();
        using var session = Session(bound, handler, new MemoryCustodyStore());
        var body = bound.CopyRequestBody();
        var machine = MachineRequest(session, bound);
        var headers = machine.Headers.ToArray();
        var changedBody = body.ToArray();
        changedBody[0] ^= 1;
        var longerBody = body.Append((byte)' ').ToArray();
        var empty = Array.Empty<byte>();

        var vectors = new[]
        {
            ("changed header", Clone(machine, headers: headers.Select((value, index) =>
                index == 1 ? new HttpLogicalRequestHeader(value.Name, "application/xml") : value).ToArray(), requestBody: body), body),
            ("reordered headers", Clone(machine, headers: [headers[1], headers[0], headers[2]], requestBody: body), body),
            ("added header", Clone(machine, headers: headers.Append(
                new HttpLogicalRequestHeader("accept-language", "fr")).ToArray(), requestBody: body), body),
            ("changed method", Clone(
                machine,
                method: HttpRequestMethod.Get,
                headers: headers.Where(static value => value.Name != "content-type").ToArray(),
                requestBody: empty), empty),
            ("changed URI", Clone(machine, uri: "https://publications.europa.eu/webapi/rdf/other", requestBody: body), body),
            ("changed body digest", Clone(machine, requestBody: changedBody), changedBody),
            ("changed body length", Clone(machine, requestBody: longerBody), longerBody),
        };

        foreach (var (name, request, requestBody) in vectors)
        {
            await AssertRefusedBeforeSend(session, handler, request, requestBody, null, name);
        }
    }

    [TestMethod]
    public void MachinePolicyRetainsTheFullBinderOpenedContentTypeMember()
    {
        var bound = MachineRequestTestFixture.EuropeanUnionRequest();
        using var session = Session(bound, new CountingHandler(), new MemoryCustodyStore());
        var expected = bound.RenderReceipt.ContentType
            ?? throw new AssertFailedException("The machine fixture lost its content-type member.");
        var request = MachineRequest(session, bound);
        var policy = Policy(session, "_requestPolicies", request.RequestPolicySha256);
        var retained = (SourceRegistryMemberRef)(policy.GetType().GetProperty(
            "ContentType",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(policy)
            ?? throw new AssertFailedException("The policy retained no content-type member."));
        var factoryParameters = policy.GetType().GetMethod(
            "ForMachineQuery",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetParameters()
            ?? throw new AssertFailedException("The machine policy factory is missing.");

        Assert.AreEqual(expected, retained);
        Assert.AreEqual(1, factoryParameters.Count(static value =>
            value.ParameterType == typeof(OpenedMachineRequest)));
        Assert.IsFalse(factoryParameters.Any(static value =>
            value.ParameterType == typeof(MachineQueryRenderReceipt) ||
            value.ParameterType == typeof(SourceRegistryMemberRef)));
        var text = Encoding.UTF8.GetString(PolicyBytes(policy));
        StringAssert.Contains(text, $"\ncontent_type_registry={expected.RegistryRef.ResourceId}\t{expected.RegistryRef.Sha256}\n");
        StringAssert.Contains(text, $"\ncontent_type_member={expected.MemberKey}\n");

        // The reason vocabulary decides how a partial completion is labelled and was reachable
        // from no published digest, so a verifier could learn which vocabulary was in force only
        // by trusting the binary. These bytes were already retained and already bound to the hop.
        StringAssert.Contains(
            text,
            $"\nreason_registry={HttpAcquisitionReasonRegistry.Sha256}\n",
            "the retained policy must name the reason vocabulary it was rendered under");
        Assert.AreEqual(
            8,
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(static line => line.StartsWith("opened_artifact=", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task LuxembourgCountAndPageSendWithDistinctDeduplicatedArtifactClosures()
    {
        var profile = OfficialMachineQuerySourceProfile.LuxembourgSparql();
        var scopeRef = Artifact(
            "urn:uuid:8c35b1ca-f72f-4b20-b669-8d3508513781",
            "bounded Luxembourg acquisition scope"u8);
        var invariantPlan = LuxembourgQueryPlan.CreateDefaultGraph(profile.ArtifactRef, scopeRef);
        const string invariantPlanResourceId =
            "urn:uuid:336c7b5d-e474-4ef5-b7e2-bec96b4cd4dd";
        var invariantPlanBytes = LuxembourgQueryPlanIdentity.GetCanonicalBytes(invariantPlan);
        var rendererSourceBytes = "Luxembourg SPARQL renderer source"u8.ToArray();
        var rendererSourceRef = Artifact(
            "urn:uuid:bd7652f3-d9b8-44ac-8107-158eead9a01b",
            rendererSourceBytes);
        var countEvidenceBytes = "verified partition row count"u8.ToArray();
        var countEvidenceRef = Artifact(
            "urn:uuid:16882425-39e8-4ddb-a61e-c32a3a33b304",
            countEvidenceBytes);
        var partition = new LuxembourgQueryPartitionRange(
            "subjects-http",
            new LuxembourgQueryCursor(
                "http://data.legilux.public.lu/resource/a", "", "", "", "", ""),
            new LuxembourgQueryCursor(
                "http://data.legilux.public.lu/resource/z", "", "", "", "", ""));
        var count = invariantPlan.BindCount(
            invariantPlanResourceId,
            "urn:uuid:5e2f85ba-5a32-409f-825d-163aa8e885fe",
            "urn:uuid:05a0267d-7073-4302-832f-aa0ccb8fb023",
            "S",
            LuxembourgQueryPass.Pass1,
            partition,
            rendererSourceRef);
        var page = invariantPlan.BindPage(
            invariantPlanResourceId,
            "urn:uuid:0c79fc78-29d5-468a-a544-a39fe0b3b19b",
            "urn:uuid:0a761827-24e5-4ab6-9142-c70ffeffff58",
            "S",
            LuxembourgQueryPass.Pass1,
            partition,
            lastCursor: null,
            expectedPartitionRowCount: 1,
            countEvidenceRef,
            rendererSourceRef);
        var custody = new MemoryCustodyStore();
        await custody.CreateAsync(
            invariantPlanBytes,
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        await custody.CreateAsync(
            rendererSourceBytes,
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        await custody.CreateAsync(
            countEvidenceBytes,
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        var productSends = 0;
        var handler = new CountingHandler((ordinal, request) =>
        {
            if (ordinal == 0)
            {
                return Response(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n");
            }

            Interlocked.Increment(ref productSends);
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, request.RequestUri?.AbsoluteUri);
            return Response(request, HttpStatusCode.OK, "{\"results\":{\"bindings\":[]}}");
        });
        using var session = Session(count.Request, handler, custody);
        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var countAttempt = await session.OpenPlanItem(count.Request)
            .ExecuteNextAttemptAsync(CancellationToken.None);
        var pageAttempt = await session.OpenPlanItem(page.Request)
            .ExecuteNextAttemptAsync(CancellationToken.None);

        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, countAttempt.Kind);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, pageAttempt.Kind);
        Assert.AreEqual(2, productSends);
        Assert.AreEqual(3, handler.SendCount);
        AssertOpenedClosure(
            MachinePolicyFor(session, count.MachinePlanRef),
            5,
            count.MachinePlanRef,
            count.InputArtifact.ArtifactRef,
            count.InvariantPlanRef,
            rendererSourceRef);
        AssertOpenedClosure(
            MachinePolicyFor(session, page.MachinePlanRef),
            6,
            page.MachinePlanRef,
            page.InputArtifact.ArtifactRef,
            page.InvariantPlanRef,
            rendererSourceRef,
            countEvidenceRef);
    }

    [TestMethod]
    public void AdapterIdentityPinsActivityPropagationAndResponseDrainBehavior()
    {
        var bound = MachineRequestTestFixture.EuropeanUnionRequest();
        using var session = Session(bound, new CountingHandler(), new MemoryCustodyStore());
        var bytes = (byte[])(typeof(RoutedHttpAcquisitionSession).GetField(
            "_adapterExecutionBytes",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session)
            ?? throw new AssertFailedException("The runtime retained no adapter identity bytes."));
        var identity = (SourceArtifactRef)(typeof(RoutedHttpAcquisitionSession).GetField(
            "_adapterExecutionIdentity",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session)
            ?? throw new AssertFailedException("The runtime retained no adapter identity."));
        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(1, lines.Count(static line => line == "activity_headers_propagator=null"));
        Assert.AreEqual(1, lines.Count(static line => line == "max_response_drain_size=0"));
        Assert.AreEqual(Sha256(bytes), identity.Sha256);

        using var handler = (SocketsHttpHandler)(typeof(RoutedHttpAcquisitionSession).GetMethod(
            "CreatePinnedHandler",
            BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null)
            ?? throw new AssertFailedException("The pinned handler factory returned null."));
        Assert.IsNull(handler.ActivityHeadersPropagator);
        Assert.AreEqual(0, handler.MaxResponseDrainSize);
    }

    [TestMethod]
    public async Task RedirectsKeepTheRobotsPolicyWhileProductRequestsRemainNoRedirect()
    {
        var bound = MachineRequestTestFixture.EuropeanUnionRequest();
        var handler = new CountingHandler((ordinal, request) => ordinal switch
        {
            0 => Response(request, HttpStatusCode.MovedPermanently, "moved", "https://op.europa.eu/robots.txt"),
            1 => Response(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n"),
            2 => Response(request, HttpStatusCode.MovedPermanently, "product moved", "https://op.europa.eu/other"),
            _ => throw new AssertFailedException("A no-redirect product policy sent a follow-up request."),
        });
        using var session = Session(bound, handler, new MemoryCustodyStore());
        var started = await BootstrapAsync(session);
        Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

        var robots = RobotsRequest(session);
        object?[] redirectArguments =
        [
            robots,
            new RoutedHttpSingleHeader("https://op.europa.eu/robots.txt"),
            null,
        ];
        Assert.IsTrue((bool)(typeof(RoutedHttpAcquisitionSession).GetMethod(
            "TryCreateRedirectRequest",
            BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, redirectArguments)
            ?? throw new AssertFailedException("The redirect constructor returned no verdict.")));
        var redirectedRobots = Assert.IsInstanceOfType<HttpLogicalRequest>(redirectArguments[2]);
        Assert.AreEqual(robots.RequestPolicySha256, redirectedRobots.RequestPolicySha256);
        Assert.AreEqual(robots.RedirectPolicySha256, redirectedRobots.RedirectPolicySha256);

        var machine = MachineRequest(session, bound);
        Assert.AreNotEqual(robots.RedirectPolicySha256, machine.RedirectPolicySha256);
        StringAssert.Contains(
            Encoding.UTF8.GetString(PolicyBytes(
                Policy(session, "_redirectPolicies", machine.RedirectPolicySha256))),
            "\nno_redirect\n");

        var item = session.OpenPlanItem(bound);
        var attempt = await item.ExecuteNextAttemptAsync(CancellationToken.None);
        Assert.AreEqual(3, handler.SendCount);
        Assert.IsNotNull(attempt.Evidence);
        Assert.AreEqual(
            HttpRouteIncompleteReason.SourceProfileStale,
            Assert.IsInstanceOfType<IncompleteHttpRouteOutcome>(attempt.Evidence.Outcome).Reason);
    }

    private static async Task AssertRefusedBeforeSend(
        RoutedHttpAcquisitionSession session,
        CountingHandler handler,
        HttpLogicalRequest request,
        ReadOnlyMemory<byte> body,
        RobotsPolicyRoute? robotsRoute,
        string name)
    {
        var before = handler.SendCount;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => InvokeRouteAsync(session, request, body, robotsRoute),
            name);
        Assert.AreEqual(before, handler.SendCount, name);
    }

    private static RoutedHttpAcquisitionSession Session(
        BoundMachineRequest request,
        HttpMessageHandler handler,
        ICustodyStore custodyStore)
    {
        var constructor = typeof(RoutedHttpAcquisitionSession).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (RoutedHttpAcquisitionSession)constructor.Invoke(
            [request, custodyStore, handler, new AdvancingTimeProvider(), false]);
    }

    private static HttpLogicalRequest RobotsRequest(RoutedHttpAcquisitionSession session) =>
        (HttpLogicalRequest)(typeof(RoutedHttpAcquisitionSession).GetMethod(
            "CreateRobotsRequest",
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(
                session,
                [session.SourceProfile.RobotsRoute.Steps[0].RequestedUri])
            ?? throw new AssertFailedException("The robots request factory returned null."));

    private static HttpLogicalRequest MachineRequest(
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
        return
        (HttpLogicalRequest)(typeof(RoutedHttpAcquisitionSession).GetMethod(
            "CreateMachineRequest",
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(session, [resolved])
            ?? throw new AssertFailedException("The machine request factory returned null."));
    }

    private static HttpLogicalRequest Clone(
        HttpLogicalRequest source,
        string? uri = null,
        HttpRequestMethod? method = null,
        IReadOnlyList<HttpLogicalRequestHeader>? headers = null,
        ReadOnlyMemory<byte>? requestBody = null,
        string? requestPolicySha256 = null,
        string? redirectPolicySha256 = null)
    {
        var body = requestBody ?? throw new ArgumentNullException(
            nameof(requestBody),
            "A hostile clone must state the exact bytes paired with its logical body.");
        return HttpLogicalRequest.Create(
            uri ?? source.Uri,
            method ?? source.Method,
            headers ?? source.Headers,
            new HttpLogicalRequestBody(checked((ulong)body.Length), Sha256(body.Span)),
            requestPolicySha256 ?? source.RequestPolicySha256,
            redirectPolicySha256 ?? source.RedirectPolicySha256);
    }

    private static object Policy(
        RoutedHttpAcquisitionSession session,
        string fieldName,
        string sha256)
    {
        var dictionary = typeof(RoutedHttpAcquisitionSession).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session)
            ?? throw new AssertFailedException($"The runtime has no {fieldName} registry.");
        return dictionary.GetType().GetProperty("Item")?.GetValue(dictionary, [sha256])
            ?? throw new AssertFailedException($"The runtime did not retain policy {sha256}.");
    }

    private static object MachinePolicyFor(
        RoutedHttpAcquisitionSession session,
        SourceArtifactRef queryPlanRef)
    {
        var dictionary = typeof(RoutedHttpAcquisitionSession).GetField(
            "_requestPolicies",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session)
            ?? throw new AssertFailedException("The runtime has no request-policy registry.");
        var marker = $"\nquery_plan={queryPlanRef.ResourceId}\t{queryPlanRef.Sha256}\n";
        return ((System.Collections.IEnumerable)dictionary)
            .Cast<object>()
            .Select(static entry => entry.GetType().GetProperty("Value")?.GetValue(entry)
                ?? throw new AssertFailedException("A request-policy entry exposed no value."))
            .Single(policy => Encoding.UTF8.GetString(PolicyBytes(policy))
                .Contains(marker, StringComparison.Ordinal));
    }

    private static void AssertOpenedClosure(
        object policy,
        int expectedCount,
        params SourceArtifactRef[] required)
    {
        var opened = Encoding.UTF8.GetString(PolicyBytes(policy))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith("opened_artifact=", StringComparison.Ordinal))
            .Select(static line =>
            {
                var parts = line["opened_artifact=".Length..].Split('\t');
                Assert.AreEqual(2, parts.Length);
                return new SourceArtifactRef(parts[0], parts[1]);
            })
            .ToArray();

        Assert.AreEqual(expectedCount, opened.Length);
        Assert.AreEqual(expectedCount, opened.Distinct().Count());
        foreach (var reference in required)
        {
            Assert.AreEqual(
                1,
                opened.Count(value => value == reference),
                $"The deduplicated closure did not contain exactly one {reference.ResourceId}.");
        }
    }

    private static byte[] PolicyBytes(object policy) =>
        (byte[])(policy.GetType().GetMethod(
            "CopyCanonicalBytes",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(policy, null)
            ?? throw new AssertFailedException("The policy retained no canonical bytes."));

    private static Task<RoutedHttpAcquisitionSession.StartResult> BootstrapAsync(
        RoutedHttpAcquisitionSession session) =>
        (Task<RoutedHttpAcquisitionSession.StartResult>)(typeof(RoutedHttpAcquisitionSession).GetMethod(
            "BootstrapRobotsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(
                session,
                [CancellationToken.None])
            ?? throw new AssertFailedException("The robots bootstrap returned no task."));

    private static Task InvokeRouteAsync(
        RoutedHttpAcquisitionSession session,
        HttpLogicalRequest request,
        ReadOnlyMemory<byte> requestBody,
        RobotsPolicyRoute? robotsRoute) =>
        (Task)(typeof(RoutedHttpAcquisitionSession).GetMethod(
            "ExecuteRouteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(
                session,
                [request, requestBody, 0UL, 0UL, robotsRoute, false, CancellationToken.None])
            ?? throw new AssertFailedException("The route executor returned no task."));

    private static string UnknownSha(params string[] known)
    {
        foreach (var value in new[] { new string('0', 64), new string('f', 64) })
        {
            if (!known.Contains(value, StringComparer.Ordinal))
            {
                return value;
            }
        }

        throw new AssertFailedException("The fixture unexpectedly exhausted hostile SHA values.");
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static SourceArtifactRef Artifact(string resourceId, ReadOnlySpan<byte> bytes) =>
        new(resourceId, Sha256(bytes));

    private static HttpResponseMessage Response(
        HttpRequestMessage request,
        HttpStatusCode status,
        string body,
        string? location = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        Assert.IsTrue(content.Headers.TryAddWithoutValidation(
            "Content-Length",
            bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (request.RequestUri?.AbsolutePath.EndsWith("robots.txt", StringComparison.Ordinal) == true)
        {
            Assert.IsTrue(content.Headers.TryAddWithoutValidation("Content-Type", "text/plain"));
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

    private sealed class CountingHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage>? response = null) : HttpMessageHandler
    {
        private int _sendCount;

        internal int SendCount => Volatile.Read(ref _sendCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = Interlocked.Increment(ref _sendCount) - 1;
            return Task.FromResult(response?.Invoke(ordinal, request) ??
                throw new AssertFailedException("The handler must not be called by a refused policy."));
        }
    }

    private sealed class MemoryCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen, cancellationToken);
            _objects[digest] = frozen;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                digest,
                frozen.Length,
                custodyClass);
            var observed = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                reference,
                new CustodyPolicyEvidence(
                    CustodySchemaIds.CustodyPolicyEvidence,
                    reference,
                    CustodyVerificationProfile.ImmutableObject1,
                    Guid.Parse("00000000-0000-0000-0000-000000000041"),
                    CustodyProtection.LockedTime,
                    observed,
                    observed.AddDays(91))));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ReadOnlyMemory<byte>>(_objects[reference.ContentSha256].ToArray());
        }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_objects.TryGetValue(contentSha256, out var bytes))
            {
                return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
            }

            if (MachineRequestTestFixture.TryReopenPreexistingArtifact(
                    contentSha256,
                    out var preexisting))
            {
                return Task.FromResult(preexisting);
            }

            throw new AssertFailedException("Custody reopen requested an unknown digest.");
        }
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            Epoch.AddTicks(Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks));

        public override long GetTimestamp() =>
            Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks);
    }
}
