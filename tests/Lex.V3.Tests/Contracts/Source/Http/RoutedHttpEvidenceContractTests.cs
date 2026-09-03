using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class RoutedHttpEvidenceContractTests
{
    [TestMethod]
    public void OneHopEvidenceHasTheExactClosedShapeAndRoundTrips()
    {
        var evidence = Evidence([CompleteHop()]);
        var bytes = evidence.CopyCanonicalBytes();
        var expected =
            "{\"schema\":\"lex-license-http-evidence/4\",\"run_identity\":{" +
            "\"resource_id\":\"urn:uuid:11111111-1111-4111-8111-111111111111\"," +
            $"\"sha256\":\"{Digest('1')}\"}},\"request_ordinal\":7,\"attempt_ordinal\":2," +
            "\"hops\":[{\"ordinal\":0,\"observation_id\":\"urn:uuid:00000000-0000-4000-8000-000000000001\"," +
            $"\"antecedent_hop_observation_id\":null,\"logical_request_sha256\":\"{Digest('9')}\"," +
            "\"request_uri\":\"https://publications.europa.eu/resource/cellar\"," +
            "\"network_origin\":{\"scheme\":\"https\",\"host\":\"publications.europa.eu\",\"effective_port\":443}," +
            "\"negotiated_http_version\":\"http/1.1\",\"status\":200," +
            "\"status_disposition\":\"derivable_status\",\"headers\":{" +
            "\"content_type\":{\"kind\":\"absent\"}," +
            "\"content_length\":{\"kind\":\"single\",\"value\":\"3\"}," +
            "\"content_encoding\":{\"kind\":\"absent\"}," +
            "\"transfer_encoding\":{\"kind\":\"absent\"}," +
            "\"content_range\":{\"kind\":\"absent\"},\"etag\":{\"kind\":\"absent\"}," +
            "\"last_modified\":{\"kind\":\"absent\"},\"location\":{\"kind\":\"absent\"}," +
            "\"cache_control\":{\"kind\":\"absent\"},\"expires\":{\"kind\":\"absent\"}," +
            "\"date\":{\"kind\":\"absent\"},\"age\":{\"kind\":\"absent\"}," +
            "\"tcn\":{\"kind\":\"absent\"}}," +
            "\"request_started_at\":\"2026-09-02T20:00:00.0000000Z\"," +
            "\"terminal_observed_at\":\"2026-09-02T20:00:01.0000000Z\"," +
            "\"completion\":{\"kind\":\"declared_content_length_complete\",\"declared_length\":3}," +
            $"\"length\":3,\"sha256\":\"{Digest('a')}\",\"durable_write_receipt_sha256\":\"{Digest('b')}\"," +
            $"\"readback_byte_length\":3,\"readback_sha256\":\"{Digest('a')}\"}}]," +
            "\"outcome\":{\"kind\":\"complete\"}}\n";
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), bytes);

        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        CollectionAssert.AreEqual(
            new[] { "schema", "run_identity", "request_ordinal", "attempt_ordinal", "hops", "outcome" },
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());
        var hop = document.RootElement.GetProperty("hops")[0];
        CollectionAssert.AreEqual(
            new[]
            {
                "ordinal", "observation_id", "antecedent_hop_observation_id",
                "logical_request_sha256", "request_uri", "network_origin",
                "negotiated_http_version", "status", "status_disposition", "headers",
                "request_started_at", "terminal_observed_at", "completion", "length", "sha256",
                "durable_write_receipt_sha256", "readback_byte_length", "readback_sha256",
            },
            hop.EnumerateObject().Select(static property => property.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "scheme", "host", "effective_port" },
            hop.GetProperty("network_origin").EnumerateObject()
                .Select(static property => property.Name).ToArray());
        Assert.AreEqual("https", hop.GetProperty("network_origin").GetProperty("scheme").GetString());
        Assert.AreEqual("publications.europa.eu", hop.GetProperty("network_origin").GetProperty("host").GetString());
        Assert.AreEqual(443, hop.GetProperty("network_origin").GetProperty("effective_port").GetInt32());

        var reopened = RoutedHttpEvidence.ParseAndVerify(bytes);
        CollectionAssert.AreEqual(bytes, reopened.CopyCanonicalBytes());
        Assert.AreEqual(HttpStatusDisposition.DerivableStatus, reopened.Hops[0].StatusDisposition);
    }

    [TestMethod]
    public void RouteCausalityAndStatusFactsAreDerivedInsteadOfAccepted()
    {
        var first = CompleteHop(
            status: 301,
            observationId: Observation0,
            antecedent: null,
            headers: Headers(contentLength: "0", location: "https://op.europa.eu/resource/cellar"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        var second = CompleteHop(
            ordinal: 1,
            observationId: Observation1,
            antecedent: Observation0,
            uri: "https://op.europa.eu/resource/cellar",
            status: 300,
            headers: Headers(contentLength: "0", contentRange: "bytes 0-0/1", tcn: "choice"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        var route = Evidence([first, second]);

        Assert.AreEqual(HttpStatusDisposition.RedirectObserved, route.Hops[0].StatusDisposition);
        Assert.AreEqual(HttpStatusDisposition.NegotiationChoiceOffered, route.Hops[1].StatusDisposition);
        Assert.AreEqual("op.europa.eu", route.Hops[1].NetworkOrigin.Host);

        var ranged200 = CompleteHop(
            status: 200,
            headers: Headers(contentLength: "3", contentRange: "bytes 0-2/3"));
        Assert.AreEqual(HttpStatusDisposition.RangeNotApproved, ranged200.StatusDisposition);

        var wrongSecond = CompleteHop(
            ordinal: 1,
            observationId: Observation1,
            antecedent: "urn:uuid:99999999-9999-4999-8999-999999999999");
        Assert.ThrowsExactly<ArgumentException>(() => Evidence([first, wrongSecond]));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence([first, first]));

        var redirectWithoutLocation = CompleteHop(
            status: 301,
            observationId: Observation0,
            antecedent: null,
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        var uncausedNextHop = CompleteHop(
            ordinal: 1,
            observationId: Observation1,
            antecedent: Observation0,
            uri: "https://evil.example/final");
        Assert.ThrowsExactly<ArgumentException>(() => Evidence([redirectWithoutLocation, uncausedNextHop]));

        var redirectToAnotherTarget = CompleteHop(
            status: 301,
            observationId: Observation0,
            antecedent: null,
            headers: Headers(contentLength: "0", location: "https://op.europa.eu/expected"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence([redirectToAnotherTarget, uncausedNextHop]));
    }

    [TestMethod]
    public void UtcTimestampsAreDescriptiveAndMayMoveBackwardWhileTheOwnedOperationRemainsCausal()
    {
        _ = CompleteHop(
            requestStartedAt: "2026-09-02T20:00:01.0000000Z",
            terminalObservedAt: "2026-09-02T20:00:00.0000000Z");
    }

    [TestMethod]
    public void EveryDocumentVisibleRouteOutcomeRequiresItsOwnTerminalShape()
    {
        foreach (var reason in new[]
                 {
                     HttpPartialBodyReason.BodyDeadline,
                     HttpPartialBodyReason.BodyReadFailure,
                     HttpPartialBodyReason.CallerCancelledAfterHeaders,
                 })
        {
            var incompleteHop = CompleteHop(completion: Incomplete(reason));
            _ = Evidence(
                [incompleteHop],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete));
        }

        _ = Evidence(
            [CompleteHop()],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale));

        var refusedRedirect = CompleteHop(
            status: 301,
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        _ = Evidence(
            [refusedRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectRefused));

        var loopRedirect = CompleteHop(
            status: 301,
            headers: Headers(
                contentLength: "0",
                location: "https://publications.europa.eu/resource/cellar"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        _ = Evidence(
            [loopRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLoop));

        var admissibleRedirect = CompleteHop(
            status: 301,
            headers: Headers(contentLength: "0", location: "https://op.europa.eu/next"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        _ = Evidence(
            [admissibleRedirect],
            new RedirectTargetUnobservedHttpRouteOutcome(
                Digest('d'),
                "2026-09-02T20:00:02.0000000Z"));
        _ = Evidence(
            [admissibleRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale));

        var ceilingRoute = RedirectCeilingRoute();
        _ = Evidence(
            ceilingRoute,
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLimitExceeded));

        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [admissibleRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectRefused)));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [admissibleRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLoop)));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [admissibleRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLimitExceeded)));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [loopRedirect],
            new RedirectTargetUnobservedHttpRouteOutcome(
                Digest('d'),
                "2026-09-02T20:00:02.0000000Z")));

        // Profile staleness is later in the normative precedence than every visible redirect
        // defect below. A caller cannot relabel one of those defects as stale profile data.
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [refusedRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale)));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [loopRedirect],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale)));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            ceilingRoute,
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale)));
    }

    [TestMethod]
    public void RobotsStatusRouteReasonsRequireExactCompleteTerminalFactsAndOutrankStaleness()
    {
        var cases = new[]
        {
            (Status: 400, Reason: HttpRouteIncompleteReason.RobotsPolicyUnavailable),
            (Status: 499, Reason: HttpRouteIncompleteReason.RobotsPolicyUnavailable),
            (Status: 500, Reason: HttpRouteIncompleteReason.PublisherServerFailure),
            (Status: 599, Reason: HttpRouteIncompleteReason.PublisherServerFailure),
        };
        foreach (var item in cases)
        {
            var terminal = CompleteHop(
                uri: "https://publications.europa.eu/robots.txt",
                status: item.Status);
            var evidence = Evidence(
                [terminal],
                new IncompleteHttpRouteOutcome(item.Reason),
                requestOrdinal: 0);

            using var document = JsonDocument.Parse(evidence.CopyCanonicalBytes());
            CollectionAssert.AreEqual(
                new[] { "kind", "reason" },
                document.RootElement.GetProperty("outcome").EnumerateObject()
                    .Select(static property => property.Name).ToArray());
            var reopened = RoutedHttpEvidence.ParseAndVerify(evidence.CopyCanonicalBytes());
            Assert.AreEqual(item.Reason, ((IncompleteHttpRouteOutcome)reopened.Outcome).Reason);

            Assert.ThrowsExactly<ArgumentException>(() => Evidence(
                [terminal],
                new CompleteHttpRouteOutcome(),
                requestOrdinal: 0));
            Assert.ThrowsExactly<ArgumentException>(() => Evidence(
                [terminal],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale),
                requestOrdinal: 0));
            Assert.ThrowsExactly<ArgumentException>(() => Evidence(
                [terminal],
                new IncompleteHttpRouteOutcome(item.Reason)));
        }

        var robots404 = CompleteHop(
            uri: "https://publications.europa.eu/robots.txt",
            status: 404);
        var robots500 = CompleteHop(
            uri: "https://publications.europa.eu/robots.txt",
            status: 500);
        _ = Evidence([robots404], new CompleteHttpRouteOutcome(), requestOrdinal: 7);
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [CompleteHop(status: 200)],
            new CompleteHttpRouteOutcome(),
            requestOrdinal: 0));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [robots404],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.PublisherServerFailure),
            requestOrdinal: 0));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [robots500],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RobotsPolicyUnavailable),
            requestOrdinal: 0));

        foreach (var uri in new[]
                 {
                     "https://publications.europa.eu/robots.txt?source=lex",
                     "https://publications.europa.eu/nested/robots.txt",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => Evidence(
                [CompleteHop(uri: uri, status: 404)],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RobotsPolicyUnavailable),
                requestOrdinal: 0));
            Assert.ThrowsExactly<ArgumentException>(() => Evidence(
                [CompleteHop(uri: uri, status: 500)],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.PublisherServerFailure),
                requestOrdinal: 0));
        }

        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [CompleteHop(uri: "https://publications.europa.eu/robots.txt", status: 399)],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RobotsPolicyUnavailable),
            requestOrdinal: 0));
        Assert.ThrowsExactly<ArgumentException>(() => Evidence(
            [CompleteHop(uri: "https://publications.europa.eu/robots.txt", status: 499)],
            new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.PublisherServerFailure),
            requestOrdinal: 0));

        foreach (var status in new[] { 404, 500 })
        {
            var incompleteTerminal = CompleteHop(
                uri: "https://publications.europa.eu/robots.txt",
                status: status,
                completion: Incomplete(HttpPartialBodyReason.BodyReadFailure));
            _ = Evidence(
                [incompleteTerminal],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete),
                requestOrdinal: 0);
            Assert.ThrowsExactly<ArgumentException>(() => Evidence(
                [incompleteTerminal],
                new IncompleteHttpRouteOutcome(
                    status == 404
                        ? HttpRouteIncompleteReason.RobotsPolicyUnavailable
                        : HttpRouteIncompleteReason.PublisherServerFailure),
                requestOrdinal: 0));
        }
    }

    [TestMethod]
    public void EveryCompletionAndRouteOutcomeKeepsItsExactWireDiscriminatorAndShape()
    {
        var chunked = CompleteHop(
            headers: Headers(transferEncoding: "chunked"),
            completion: new PinnedHandlerChunkedEofHttpCompletion(Digest('e')));
        var revalidation = CompleteHop(
            status: 304,
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: new Revalidation304HttpCompletion());
        var noBody = CompleteHop(
            status: 204,
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: new ResponseWithoutBodyHttpCompletion());
        var incomplete = CompleteHop(
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.MissingCompletionProof));
        var completionEvidence = new[]
        {
            Evidence([CompleteHop()]),
            Evidence([chunked]),
            Evidence([revalidation]),
            Evidence([noBody]),
            Evidence(
                [incomplete],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete)),
        };

        CollectionAssert.AreEqual(
            new[]
            {
                "{\"kind\":\"declared_content_length_complete\",\"declared_length\":3}",
                $"{{\"kind\":\"pinned_handler_chunked_eof\",\"adapter_execution_sha256\":\"{Digest('e')}\"}}",
                "{\"kind\":\"revalidation_304\"}",
                "{\"kind\":\"response_without_body\"}",
                "{\"kind\":\"incomplete\",\"reason\":{\"registry_ref\":{" +
                "\"resource_id\":\"urn:uuid:f9eb3136-c855-44f5-b84f-6c28353b592d\"," +
                "\"sha256\":\"803ed00fc952d30e66984c21e045dc79dd39c2d555f81df159b7045e32dbbc89\"}," +
                "\"member_key\":\"missing_completion_proof\"}}",
            },
            completionEvidence.Select(static evidence => CompletionJson(evidence.Hops[0])).ToArray());
        foreach (var evidence in completionEvidence)
        {
            var reopened = RoutedHttpEvidence.ParseAndVerify(evidence.CopyCanonicalBytes());
            CollectionAssert.AreEqual(evidence.CopyCanonicalBytes(), reopened.CopyCanonicalBytes());
            Assert.AreEqual(evidence.Hops[0].Completion.GetType(), reopened.Hops[0].Completion.GetType());
            if (evidence.Hops[0].Completion is IncompleteHttpCompletion expectedIncomplete)
            {
                Assert.AreEqual(
                    expectedIncomplete.Reason,
                    ((IncompleteHttpCompletion)reopened.Hops[0].Completion).Reason);
            }
        }

        var refusedRedirect = CompleteHop(
            status: 301,
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        var loopRedirect = CompleteHop(
            status: 301,
            headers: Headers(
                contentLength: "0",
                location: "https://publications.europa.eu/resource/cellar"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        var admissibleRedirect = CompleteHop(
            status: 301,
            headers: Headers(contentLength: "0", location: "https://op.europa.eu/next"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        var robotsUnavailable = CompleteHop(
            uri: "https://publications.europa.eu/robots.txt",
            status: 404);
        var publisherServerFailure = CompleteHop(
            uri: "https://publications.europa.eu/robots.txt",
            status: 500);
        var incompleteHop = CompleteHop(
            completion: Incomplete(HttpPartialBodyReason.BodyDeadline));
        var routeEvidence = new[]
        {
            Evidence([CompleteHop()]),
            Evidence(
                [incompleteHop],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete)),
            Evidence(
                [robotsUnavailable],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RobotsPolicyUnavailable),
                requestOrdinal: 0),
            Evidence(
                [publisherServerFailure],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.PublisherServerFailure),
                requestOrdinal: 0),
            Evidence(
                [CompleteHop()],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale)),
            Evidence(
                [refusedRedirect],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectRefused)),
            Evidence(
                [loopRedirect],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLoop)),
            Evidence(
                RedirectCeilingRoute(),
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectLimitExceeded)),
            Evidence(
                [admissibleRedirect],
                new RedirectTargetUnobservedHttpRouteOutcome(
                    Digest('d'),
                    "2026-09-02T20:00:02.0000000Z")),
        };

        CollectionAssert.AreEqual(
            new[]
            {
                "{\"kind\":\"complete\"}",
                "{\"kind\":\"incomplete\",\"reason\":\"hop_incomplete\"}",
                "{\"kind\":\"incomplete\",\"reason\":\"robots_policy_unavailable\"}",
                "{\"kind\":\"incomplete\",\"reason\":\"publisher_server_failure\"}",
                "{\"kind\":\"incomplete\",\"reason\":\"source_profile_stale\"}",
                "{\"kind\":\"incomplete\",\"reason\":\"redirect_refused\"}",
                "{\"kind\":\"incomplete\",\"reason\":\"redirect_loop\"}",
                "{\"kind\":\"incomplete\",\"reason\":\"redirect_limit_exceeded\"}",
                $"{{\"kind\":\"incomplete\",\"reason\":\"redirect_target_unobserved\"," +
                $"\"logical_request_sha256\":\"{Digest('d')}\"," +
                "\"request_started_at\":\"2026-09-02T20:00:02.0000000Z\"}",
            },
            routeEvidence.Select(OutcomeJson).ToArray());
        foreach (var evidence in routeEvidence)
        {
            var reopened = RoutedHttpEvidence.ParseAndVerify(evidence.CopyCanonicalBytes());
            CollectionAssert.AreEqual(evidence.CopyCanonicalBytes(), reopened.CopyCanonicalBytes());
            Assert.AreEqual(evidence.Outcome.GetType(), reopened.Outcome.GetType());
            Assert.AreEqual(RouteReason(evidence.Outcome), RouteReason(reopened.Outcome));
        }
    }

    [TestMethod]
    public void EveryStatusDispositionKeepsItsExactWireTokenAndReopensThroughTheSharedClassifier()
    {
        var redirect = CompleteHop(
            status: 301,
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        var revalidation = CompleteHop(
            status: 304,
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: new Revalidation304HttpCompletion());
        var noBody = CompleteHop(
            status: 204,
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: new ResponseWithoutBodyHttpCompletion());
        var cases = new[]
        {
            (Evidence: Evidence([CompleteHop()]), Token: "derivable_status", Expected: HttpStatusDisposition.DerivableStatus),
            (Evidence: Evidence(
                [redirect],
                new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.RedirectRefused)),
                Token: "redirect_observed", Expected: HttpStatusDisposition.RedirectObserved),
            (Evidence: Evidence([revalidation]), Token: "revalidation_reference_only", Expected: HttpStatusDisposition.RevalidationReferenceOnly),
            (Evidence: Evidence([noBody]), Token: "semantic_no_entity_status", Expected: HttpStatusDisposition.SemanticNoEntityStatus),
            (Evidence: Evidence([CompleteHop(headers: Headers(
                contentLength: "3",
                contentRange: "bytes 0-2/3"))]),
                Token: "range_not_approved", Expected: HttpStatusDisposition.RangeNotApproved),
            (Evidence: Evidence([CompleteHop(status: 404)]), Token: "non_derivable_status", Expected: HttpStatusDisposition.NonDerivableStatus),
            (Evidence: Evidence([CompleteHop(
                status: 300,
                headers: Headers(
                    contentLength: "3",
                    contentRange: "bytes 0-2/3",
                    tcn: "choice"))]),
                Token: "negotiation_choice_offered", Expected: HttpStatusDisposition.NegotiationChoiceOffered),
        };

        foreach (var item in cases)
        {
            var bytes = item.Evidence.CopyCanonicalBytes();
            using var document = JsonDocument.Parse(bytes);
            Assert.AreEqual(
                item.Token,
                document.RootElement.GetProperty("hops")[0]
                    .GetProperty("status_disposition").GetString());
            var reopened = RoutedHttpEvidence.ParseAndVerify(bytes);
            Assert.AreEqual(item.Expected, reopened.Hops[0].StatusDisposition);
            CollectionAssert.AreEqual(bytes, reopened.CopyCanonicalBytes());
        }

        var repeatedCacheControl = Evidence([CompleteHop(
            headers: Headers(
                contentLength: "3",
                cacheControl: ["no-store", "no-store"]))]);
        var reopenedCacheControl = RoutedHttpEvidence.ParseAndVerify(
            repeatedCacheControl.CopyCanonicalBytes());
        var multiple = (RoutedHttpMultipleHeader)reopenedCacheControl.Hops[0].Headers.CacheControl;
        CollectionAssert.AreEqual(new[] { "no-store", "no-store" }, multiple.Values.ToArray());
        CollectionAssert.AreEqual(
            repeatedCacheControl.CopyCanonicalBytes(),
            reopenedCacheControl.CopyCanonicalBytes());
    }

    [TestMethod]
    public void CompletionKindsEnforceTheFactsVisibleInsideTheDocument()
    {
        _ = CompleteHop(
            status: 205,
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: new DeclaredContentLengthHttpCompletion(0));
        _ = CompleteHop(
            status: 205,
            headers: Headers(transferEncoding: "chunked"),
            length: 0,
            digest: EmptyDigest,
            completion: new PinnedHandlerChunkedEofHttpCompletion(Digest('e')));
        _ = CompleteHop(
            status: 304,
            headers: Headers(contentLength: "999", transferEncoding: "gzip"),
            length: 0,
            digest: EmptyDigest,
            completion: new Revalidation304HttpCompletion());
        _ = CompleteHop(
            status: 204,
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: new ResponseWithoutBodyHttpCompletion());
        _ = CompleteHop(
            headers: Headers(contentLength: "003"),
            length: 3,
            completion: new DeclaredContentLengthHttpCompletion(3));

        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(contentLength: "4"),
            completion: new DeclaredContentLengthHttpCompletion(3)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(contentLength: "3", transferEncoding: "chunked"),
            completion: new DeclaredContentLengthHttpCompletion(3)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            status: 205,
            headers: Headers(contentLength: "3"),
            completion: new DeclaredContentLengthHttpCompletion(3)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            status: 204,
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: new ResponseWithoutBodyHttpCompletion()));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(transferEncoding: "gzip"),
            completion: new PinnedHandlerChunkedEofHttpCompletion(Digest('e'))));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: Digest('a'),
            completion: new DeclaredContentLengthHttpCompletion(0)));

        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            status: 304,
            headers: Headers(contentLength: "not-a-number"),
            length: 0,
            digest: EmptyDigest,
            completion: new IncompleteHttpCompletion(HttpAcquisitionReasonRegistry.Member(
                HttpCompletionUnprovenReason.InvalidContentLength))));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            status: 204,
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: new IncompleteHttpCompletion(HttpAcquisitionReasonRegistry.Member(
                HttpResponseSemanticsReason.StatusFramingConflict))));
    }

    [TestMethod]
    public void EveryDocumentVisibleIncompleteReasonRequiresItsOwnFactsAndPrecedence()
    {
        _ = CompleteHop(
            headers: Headers(contentLength: "invalid"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.InvalidContentLength));
        _ = CompleteHop(
            headers: Headers(contentLength: "2"),
            length: 3,
            completion: Incomplete(HttpCompletionUnprovenReason.InvalidContentLength));
        // Transfer framing determines the retained bytes when both fields exist. A canonical
        // Content-Length remains conflicting evidence regardless of whether it is above or below
        // the retained length, so transfer-coding conflict wins before a length comparison.
        _ = CompleteHop(
            headers: Headers(contentLength: "4", transferEncoding: "chunked"),
            length: 3,
            completion: Incomplete(HttpCompletionUnprovenReason.TransferCodingConflict));
        _ = CompleteHop(
            headers: Headers(contentLength: "0", transferEncoding: "chunked"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.TransferCodingConflict));
        _ = CompleteHop(
            headers: Headers(transferEncoding: "gzip"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.UnsupportedTransferCoding));
        _ = CompleteHop(
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.MissingCompletionProof));
        _ = CompleteHop(
            status: 304,
            headers: Headers(contentLength: "invalid", transferEncoding: "gzip"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpResponseSemanticsReason.RevalidationRequestNotAdmitted));
        _ = CompleteHop(
            status: 304,
            headers: Headers(),
            length: 1,
            digest: Digest('a'),
            completion: Incomplete(HttpResponseSemanticsReason.StatusContentForbidden));
        _ = CompleteHop(
            status: 204,
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpResponseSemanticsReason.StatusFramingConflict));
        _ = CompleteHop(
            status: 205,
            headers: Headers(contentLength: "3"),
            length: 3,
            completion: Incomplete(HttpResponseSemanticsReason.StatusContentForbidden));
        _ = CompleteHop(
            headers: Headers(contentLength: "3"),
            length: 2,
            completion: Incomplete(HttpPartialBodyReason.DeclaredLengthShortRead));

        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            status: 204,
            headers: Headers(contentLength: "invalid"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.InvalidContentLength)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(transferEncoding: "chunked"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.UnsupportedTransferCoding)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(contentLength: "0"),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpCompletionUnprovenReason.MissingCompletionProof)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            status: 200,
            headers: Headers(),
            length: 0,
            digest: EmptyDigest,
            completion: Incomplete(HttpResponseSemanticsReason.RevalidationRequestNotAdmitted)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            status: 205,
            headers: Headers(),
            length: 1,
            completion: Incomplete(HttpResponseSemanticsReason.StatusContentForbidden)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(contentLength: "2"),
            length: 2,
            completion: Incomplete(HttpPartialBodyReason.DeclaredLengthShortRead)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(contentLength: "2"),
            length: 3,
            completion: Incomplete(HttpPartialBodyReason.DeclaredLengthShortRead)));
        _ = CompleteHop(
            headers: Headers(contentLength: "2", transferEncoding: "chunked"),
            length: 3,
            completion: Incomplete(HttpCompletionUnprovenReason.TransferCodingConflict));
        _ = CompleteHop(
            headers: Headers(contentLength: "4", transferEncoding: "chunked"),
            length: 3,
            completion: Incomplete(HttpCompletionUnprovenReason.TransferCodingConflict));
    }

    [TestMethod]
    public void SentinelAndReasonRegistryCannotBecomeCompleteOrBeforeHeaderEvidence()
    {
        var sentinelReason = HttpAcquisitionReasonRegistry.Member(
            HttpPartialBodyReason.ByteBoundPreventedCompletion);
        _ = CompleteHop(
            headers: Headers(),
            length: 268_435_456,
            digest: Digest('c'),
            completion: new IncompleteHttpCompletion(sentinelReason));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompleteHop(
            headers: Headers(contentLength: "268435456"),
            length: 268_435_456,
            digest: Digest('c'),
            completion: new DeclaredContentLengthHttpCompletion(268_435_456)));
        Assert.ThrowsExactly<ArgumentException>(() => CompleteHop(
            headers: Headers(),
            length: 268_435_456,
            digest: Digest('c'),
            completion: new IncompleteHttpCompletion(
                HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.BodyReadFailure))));
        Assert.ThrowsExactly<ArgumentException>(() => new IncompleteHttpCompletion(
            HttpAcquisitionReasonRegistry.Member(HttpPreHeaderFailureClass.HeaderDeadline)));
        Assert.ThrowsExactly<ArgumentException>(() => RoutedHttpEvidence.Create(
            new SourceArtifactRef(
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                Digest('1')),
            7,
            2,
            Enumerable.Range(0, 7).Select(index => CompleteHop(
                ordinal: (ulong)index,
                observationId: $"urn:uuid:00000000-0000-4000-8000-{index + 1:D12}",
                antecedent: index == 0
                    ? null
                    : $"urn:uuid:00000000-0000-4000-8000-{index:D12}")).ToArray(),
            new CompleteHttpRouteOutcome()));
    }

    [TestMethod]
    public void ExactReaderRejectsCanonicalAndStructuralSubstitution()
    {
        var canonical = Encoding.UTF8.GetString(Evidence([CompleteHop()]).CopyCanonicalBytes());
        foreach (var mutation in new[]
        {
            canonical.Replace("\"schema\":", "\"schema\": \"ignored\",\"old_schema\":", StringComparison.Ordinal),
            canonical.Replace("\"request_ordinal\":7", "\"request_ordinal\":07", StringComparison.Ordinal),
            canonical.Replace("\"attempt_ordinal\":2", "\"attempt_ordinal\":-1", StringComparison.Ordinal),
            canonical.Replace("\"readback_byte_length\":3", "\"readback_byte_length\":2", StringComparison.Ordinal),
            canonical.Replace("\"antecedent_hop_observation_id\":null", "\"antecedent_hop_observation_id\":\"urn:uuid:99999999-9999-4999-8999-999999999999\"", StringComparison.Ordinal),
            canonical.TrimEnd('\n'),
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                RoutedHttpEvidence.ParseAndVerify(Encoding.UTF8.GetBytes(mutation)));
        }
    }

    private const string Observation0 = "urn:uuid:00000000-0000-4000-8000-000000000001";
    private const string Observation1 = "urn:uuid:00000000-0000-4000-8000-000000000002";
    private static readonly string EmptyDigest =
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])).ToLowerInvariant();

    private static RoutedHttpEvidence Evidence(
        IReadOnlyList<RoutedHttpHop> hops,
        RoutedHttpRouteOutcome? outcome = null,
        ulong requestOrdinal = 7) =>
        RoutedHttpEvidence.Create(
            new SourceArtifactRef(
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                Digest('1')),
            requestOrdinal,
            attemptOrdinal: 2,
            hops,
            outcome ?? new CompleteHttpRouteOutcome());

    private static RoutedHttpHop CompleteHop(
        ulong ordinal = 0,
        string observationId = Observation0,
        string? antecedent = null,
        string uri = "https://publications.europa.eu/resource/cellar",
        int status = 200,
        RoutedHttpResponseHeaders? headers = null,
        ulong length = 3,
        string? digest = null,
        RoutedHttpCompletion? completion = null,
        string requestStartedAt = "2026-09-02T20:00:00.0000000Z",
        string terminalObservedAt = "2026-09-02T20:00:01.0000000Z") =>
        RoutedHttpHop.Create(
            ordinal,
            observationId,
            antecedent,
            Digest('9'),
            uri,
            status,
            headers ?? Headers(contentLength: "3"),
            requestStartedAt,
            terminalObservedAt,
            completion ?? new DeclaredContentLengthHttpCompletion(3),
            length,
            digest ?? Digest('a'),
            Digest('b'),
            length,
            digest ?? Digest('a'));

    private static RoutedHttpResponseHeaders Headers(
        string? contentLength = null,
        string? transferEncoding = null,
        string? contentRange = null,
        string? location = null,
        string? tcn = null,
        IReadOnlyList<string>? cacheControl = null)
    {
        RoutedHttpHeaderField Field(string? value) => value is null
            ? new RoutedHttpAbsentHeader()
            : new RoutedHttpSingleHeader(value);
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            absent,
            Field(contentLength),
            absent,
            Field(transferEncoding),
            Field(contentRange),
            absent,
            absent,
            Field(location),
            cacheControl is null ? absent : new RoutedHttpMultipleHeader(cacheControl),
            absent,
            absent,
            absent,
            Field(tcn));
    }

    private static string Digest(char value) => new(value, 64);

    private static RoutedHttpHop[] RedirectCeilingRoute() =>
        Enumerable.Range(0, 6)
            .Select(index => CompleteHop(
                ordinal: (ulong)index,
                observationId: $"urn:uuid:00000000-0000-4000-8000-{index + 1:D12}",
                antecedent: index == 0
                    ? null
                    : $"urn:uuid:00000000-0000-4000-8000-{index:D12}",
                uri: $"https://route{index}.example/path",
                status: 301,
                headers: Headers(
                    contentLength: "0",
                    location: $"https://route{index + 1}.example/path"),
                length: 0,
                digest: EmptyDigest,
                completion: new DeclaredContentLengthHttpCompletion(0)))
            .ToArray();

    private static string CompletionJson(RoutedHttpHop hop)
    {
        var outcome = hop.Completion is IncompleteHttpCompletion
            ? (RoutedHttpRouteOutcome)new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete)
            : new CompleteHttpRouteOutcome();
        using var document = JsonDocument.Parse(Evidence([hop], outcome).CopyCanonicalBytes());
        return document.RootElement.GetProperty("hops")[0].GetProperty("completion").GetRawText();
    }

    private static string OutcomeJson(RoutedHttpEvidence evidence)
    {
        using var document = JsonDocument.Parse(evidence.CopyCanonicalBytes());
        return document.RootElement.GetProperty("outcome").GetRawText();
    }

    private static HttpRouteIncompleteReason? RouteReason(RoutedHttpRouteOutcome outcome) =>
        outcome switch
        {
            CompleteHttpRouteOutcome => null,
            IncompleteHttpRouteOutcome incomplete => incomplete.Reason,
            RedirectTargetUnobservedHttpRouteOutcome target => target.Reason,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    private static IncompleteHttpCompletion Incomplete(HttpPartialBodyReason reason) =>
        new(HttpAcquisitionReasonRegistry.Member(reason));

    private static IncompleteHttpCompletion Incomplete(HttpCompletionUnprovenReason reason) =>
        new(HttpAcquisitionReasonRegistry.Member(reason));

    private static IncompleteHttpCompletion Incomplete(HttpResponseSemanticsReason reason) =>
        new(HttpAcquisitionReasonRegistry.Member(reason));
}
