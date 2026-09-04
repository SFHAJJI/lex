using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
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
        // The pair, not the bare reference: the binder now requires whoever names the renderer
        // source to be holding it.
        var rendererSource = MachineQueryRendererSource.Open(
            rendererSourceRef,
            rendererSourceBytes);
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
            rendererSource);
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
            rendererSource);
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
    public async Task TheRetainedRequestPolicyHasAPinnedCanonicalForm()
    {
        // Every other assertion about these bytes says a particular line is present. None of them
        // can see a line that was added, and none can see one that was removed unless somebody
        // remembered to assert it. The retained policy is what a verifier reads to learn the terms
        // a request was sent under, so a silent change to its shape is a silent change to what the
        // evidence means. This pins the whole form: the digest, and the ordered list of keys that
        // produced it so a failure says which line moved rather than only that something did.
        // Bound freshly for each run from identical inputs, not bound once and sent twice. That
        // distinction is the whole measurement: binding is where the render receipt is minted, so
        // sending one bound request twice would compare a run with itself and report agreement it
        // had not earned. The first version of this test did exactly that and reported zero
        // differences.
        static LuxembourgBoundQueryCount BindFresh()
        {
            var profile = OfficialMachineQuerySourceProfile.LuxembourgSparql();
            var invariantPlan = LuxembourgQueryPlan.CreateDefaultGraph(
                profile.ArtifactRef,
                Artifact("urn:uuid:6a1d3e55-8c2b-4f19-9a77-0d5e2b8c4f31", "pinned scope"u8));
            var rendererSourceBytes = "pinned renderer source"u8.ToArray();
            return invariantPlan.BindCount(
                "urn:uuid:7b2e4f66-9d3c-4a2b-8b88-1e6f3c9d5a42",
                "urn:uuid:8c3f5a77-ae4d-4b3c-9c99-2f7a4dae6b53",
                "urn:uuid:9d4a6b88-bf5e-4c4d-8daa-3a8b5ebf7c64",
                "S",
                LuxembourgQueryPass.Pass1,
                new LuxembourgQueryPartitionRange(
                    "subjects-pinned",
                    new LuxembourgQueryCursor(
                        "http://data.legilux.public.lu/resource/a", "", "", "", "", ""),
                    new LuxembourgQueryCursor(
                        "http://data.legilux.public.lu/resource/z", "", "", "", "", "")),
                MachineQueryRendererSource.Open(
                    Artifact("urn:uuid:ae5b7c99-c06f-4d5e-9ebb-4b9c6fca8d75", rendererSourceBytes),
                    rendererSourceBytes));
        }

        var first = await RetainedPolicyTextAsync(BindFresh());
        var second = await RetainedPolicyTextAsync(BindFresh());
        var bytes = Encoding.UTF8.GetBytes(first);
        var keys = Encoding.UTF8.GetString(bytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Split('=', 2)[0])
            .ToArray();

        // The key list carries repeats where the form repeats a key, so a removed uri or header
        // line changes it. It is the readable half; the digest below is the half that cannot be
        // satisfied by a line that merely keeps its key while changing its value.
        CollectionAssert.AreEqual(
            new[]
            {
                // The first two carry no "=" at all: the schema line and the request kind. They
                // are the form's own identity and are listed exactly as they appear.
                "lex-http-request-policy/1", "machine_query_post",
                "source_profile", "adapter_execution", "adapter_execution_bytes_sha256",
                "reason_registry", "method", "requested_http_version", "version_policy",
                "request_timeout_ticks", "minimum_request_interval_ticks", "maximum_attempts",
                "initial_retry_delay_ticks", "maximum_retry_delay_ticks",
                "maximum_response_bytes", "allow_auto_redirect", "automatic_decompression",
                "activity_headers_propagator", "max_response_drain_size", "cookies", "proxy",
                "http_client_timeout",
                "retry", "retry", "retry", "retry", "retry", "retry", "retry", "retry",
                "uri", "header", "header", "header", "body",
                "render_receipt", "query_plan", "ordered_parameter_set", "renderer_profile",
                "renderer_source", "content_type_registry", "content_type_member",
                "opened_artifact", "opened_artifact", "opened_artifact", "opened_artifact",
                "opened_artifact",
            },
            keys,
            "the retained policy's shape changed; a line was added, removed or reordered");

        // Two fresh binds of one request under one set of terms must produce byte-identical
        // retained policies. Before Decision 77 they did not: the binder minted the render
        // receipt's resource id with Guid.NewGuid, so two lines differed, the receipt's own and
        // its opened_artifact echo, with identical content digests on both. That was not a
        // cosmetic defect. The policy digest is a member of the R3.3 absence key tuple, so the
        // absence key changed at every cut, three consecutive absent cuts could never share a key,
        // and an absence history could never advance. Nothing would have failed; absence would
        // simply never have become provable.
        //
        // Compared line by line rather than by digest alone, so a failure names the line.
        var firstLines = first.Split('\n');
        var secondLines = second.Split('\n');
        Assert.AreEqual(
            firstLines.Length,
            secondLines.Length,
            "two binds of one request produced policies of different length");

        var differing = Enumerable.Range(0, firstLines.Length)
            .Where(index => !string.Equals(
                firstLines[index], secondLines[index], StringComparison.Ordinal))
            .Select(index => $"line {index}: {firstLines[index]} versus {secondLines[index]}")
            .ToArray();

        Assert.AreEqual(
            0,
            differing.Length,
            "two binds of one request under one set of terms must agree exactly, and an "
            + "identifier minted per bind is the way that stops being true: "
            + string.Join(" | ", differing));

        Assert.AreEqual(
            PinnedLuxembourgCountPolicySha256,
            Sha256(bytes),
            "the retained policy's canonical bytes changed for a fixed fixture");
    }

    /// <summary>
    /// Sends the bound count once through a fresh session and returns the retained request policy
    /// as text. Every run gets its own session and its own store, because two runs sharing either
    /// would be one run observed twice.
    /// </summary>
    private static async Task<string> RetainedPolicyTextAsync(LuxembourgBoundQueryCount count)
    {
        var handler = new CountingHandler((ordinal, request) => ordinal == 0
            ? Response(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n")
            : Response(request, HttpStatusCode.OK, "7"));
        using var session = Session(count.Request, handler, new MemoryCustodyStore());
        Assert.AreEqual(
            OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
            (await BootstrapAsync(session)).Kind);
        Assert.AreEqual(
            OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
            (await session.OpenPlanItem(count.Request)
                .ExecuteNextAttemptAsync(CancellationToken.None)).Kind);
        return Encoding.UTF8.GetString(
            PolicyBytes(MachinePolicyFor(session, count.MachinePlanRef)));
    }

    /// <summary>
    /// The digest of the retained request policy for the fixture above, transcribed from a run
    /// rather than computed by the test, so that recomputing it the way the code does could not
    /// make this agree with itself. It is the raw bytes, not a normalised form: since Decision 77
    /// every identifier reaching a retained policy is derived from the content it names, so two
    /// binds of one request agree exactly and there is nothing left to normalise away.
    /// </summary>
    private const string PinnedLuxembourgCountPolicySha256 =
        "f74d443c54efca057f310df7b8392ac7d87802547ae8f469303456ad3688db7e";

    [TestMethod]
    public async Task LuxembourgCountSendsAgainstAFreshRealStoreHoldingNothing()
    {
        // Decision 75, and the only test shape that can show it. Every other machine-send test
        // runs against a store that was handed the send closure first, either by a double that
        // answers from a table of fixture bytes or by three CreateAsync calls before the session
        // starts. Under that arrangement a run that merely names its dependencies is
        // indistinguishable from one that holds them, which is how the product route stayed green
        // here for as long as it did while failing against a real FileSystemCustodyStore with
        // "the content-addressed artifact is not retained by this store".
        //
        // So: a real store, on an empty directory, with nothing put in it. The count closure is
        // exactly the receipt, the machine plan, the ordered parameter set, the invariant plan and
        // the renderer source. The first three the binder produces. The last two the renderer now
        // produces, which is the change. Nothing in this closure is anyone else's to have
        // retained, so the run either holds all of it or does not send.
        var profile = OfficialMachineQuerySourceProfile.LuxembourgSparql();
        var scopeRef = Artifact(
            "urn:uuid:1f7f4e26-1a4e-4a6d-9d3e-5c1c4a1d2f60",
            "bounded Luxembourg acquisition scope"u8);
        var invariantPlan = LuxembourgQueryPlan.CreateDefaultGraph(profile.ArtifactRef, scopeRef);
        var rendererSourceBytes = "Luxembourg SPARQL renderer source"u8.ToArray();
        var rendererSource = MachineQueryRendererSource.Open(
            Artifact("urn:uuid:2c9a4b70-0f5e-4a51-8a53-6b0f2e7c9a11", rendererSourceBytes),
            rendererSourceBytes);
        var count = invariantPlan.BindCount(
            "urn:uuid:3d8b5c81-1a6f-4b62-9b64-7c1f3f8daa22",
            "urn:uuid:4e9c6d92-2b70-4c73-8c75-8d2a4a9ebb33",
            "urn:uuid:5fad7ea3-3c81-4d84-9d86-9e3b5bafcc44",
            "S",
            LuxembourgQueryPass.Pass1,
            new LuxembourgQueryPartitionRange(
                "subjects-fresh-store",
                new LuxembourgQueryCursor(
                    "http://data.legilux.public.lu/resource/a", "", "", "", "", ""),
                new LuxembourgQueryCursor(
                    "http://data.legilux.public.lu/resource/z", "", "", "", "", "")),
            rendererSource);

        var root = Path.Combine(
            Path.GetTempPath(),
            "lex-fresh-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var custody = new FileSystemCustodyStore(root);
            var productSends = 0;
            var handler = new CountingHandler((ordinal, request) =>
            {
                if (ordinal == 0)
                {
                    return Response(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n");
                }

                Interlocked.Increment(ref productSends);
                Assert.AreEqual(
                    LuxembourgQueryPlan.PublisherEndpoint,
                    request.RequestUri?.AbsoluteUri);
                return Response(request, HttpStatusCode.OK, "42");
            });
            using var session = Session(count.Request, handler, custody);
            var started = await BootstrapAsync(session);
            Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

            var attempt = await session.OpenPlanItem(count.Request)
                .ExecuteNextAttemptAsync(CancellationToken.None);

            Assert.AreEqual(
                OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
                attempt.Kind,
                "a run that produces its own send closure needs nothing already in the store");
            Assert.AreEqual(1, productSends);
            AssertOpenedClosure(
                MachinePolicyFor(session, count.MachinePlanRef),
                5,
                count.MachinePlanRef,
                count.InputArtifact.ArtifactRef,
                count.InvariantPlanRef,
                rendererSource.Reference);

            // Readable back out of the store by digest, which is the difference between having
            // retained an artifact and having named one. A store that never held these would have
            // failed the send above; this says the send did not pass by holding something else.
            foreach (var digest in new[]
                {
                    count.InvariantPlanRef.Sha256,
                    rendererSource.Reference.Sha256,
                })
            {
                var reopened = await custody.ReadByDigestAsync(digest, CancellationToken.None);
                Assert.AreEqual(
                    digest,
                    Convert.ToHexString(SHA256.HashData(reopened.Span)).ToLowerInvariant());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuConsolidationCountSendsAgainstAFreshRealStoreHoldingNothing()
    {
        // The EU half of Decision 75, and the same shape as the LU proof above for the same
        // reason: every other EU machine-send test either uses a recording double that answers
        // from a table of fixture bytes, or hands the store the closure with CreateAsync before
        // the session starts. Under either arrangement a run that merely names its renderer
        // profile and renderer source is indistinguishable from one that holds them.
        //
        // A real FileSystemCustodyStore on an empty directory, with nothing put in it. The EU
        // count closure is exactly the render receipt, the machine plan, the ordered parameter
        // set, the renderer profile and the renderer source. The first three the binder produces.
        // The last two the renderer now produces, which is what this change adds. Everything else
        // the EU count names is the discovery plan's own ArtifactRef again, so it deduplicates
        // into the profile rather than being a sixth artifact somebody else would have to hold.
        //
        // Count, not page. A page additionally names a partition row-count evidence reference,
        // and that one genuinely is not renderer-produced: it is the http evidence an earlier
        // count send of the same run wrote. Seeding it here to reach a page would be the exact
        // defect this change exists to remove, so the page route is left to the run that produces
        // its own count evidence and is not claimed as proven here.
        var plan = EuConsolidationDiscoveryPlan.Create();
        var rendererSourceBytes = "EU consolidation SPARQL renderer source"u8.ToArray();
        var rendererSource = MachineQueryRendererSource.Open(
            Artifact("urn:uuid:6b1c2d34-4e5f-4a60-9b71-8c2d3e4f5a61", rendererSourceBytes),
            rendererSourceBytes);
        var count = plan.BindCount(
            EuConsolidationQuerySet.Family,
            "32016R0679",
            EuConsolidationQueryPass.Pass1,
            "urn:uuid:7c2d3e45-5f60-4b71-8c82-9d3e4f5a6b72",
            "urn:uuid:8d3e4f56-6a71-4c82-9d93-ae4f5a6b7c83",
            rendererSource);

        var root = Path.Combine(
            Path.GetTempPath(),
            "lex-fresh-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var custody = new FileSystemCustodyStore(root);
            var productSends = 0;
            var handler = new CountingHandler((ordinal, request) => ordinal switch
            {
                // The EU robots route is two hops by profile: a 301 off publications.europa.eu
                // and a 200 on op.europa.eu.
                0 => Response(
                    request,
                    HttpStatusCode.MovedPermanently,
                    "moved",
                    "https://op.europa.eu/robots.txt"),
                1 => Response(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n"),
                _ => ProductResponse(request),
            });
            using var session = Session(count.Request, handler, custody);
            var started = await BootstrapAsync(session);
            Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

            var attempt = await session.OpenPlanItem(count.Request)
                .ExecuteNextAttemptAsync(CancellationToken.None);

            Assert.AreEqual(
                OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
                attempt.Kind,
                "a run that produces its own send closure needs nothing already in the store");
            Assert.AreEqual(1, productSends);
            AssertOpenedClosure(
                MachinePolicyFor(session, count.MachinePlanRef),
                5,
                count.MachinePlanRef,
                count.InputArtifact.ArtifactRef,
                plan.ArtifactRef,
                rendererSource.Reference);

            // Readable back out by digest, which is the difference between having retained an
            // artifact and having named one. A store that never held these would have refused the
            // send above; this says the send did not pass by holding something else.
            foreach (var digest in new[]
                {
                    plan.ArtifactRef.Sha256,
                    rendererSource.Reference.Sha256,
                })
            {
                var reopened = await custody.ReadByDigestAsync(digest, CancellationToken.None);
                Assert.AreEqual(
                    digest,
                    Convert.ToHexString(SHA256.HashData(reopened.Span)).ToLowerInvariant());
            }

            // The profile artifact is the discovery plan's own canonical identity rather than
            // some other blob that happens to hash right, so a reader who fetches it by digest
            // gets the plan back. Asserted on the retained bytes, not on what the renderer
            // returned, because the point is what the store now holds.
            var profileBytes = await custody.ReadByDigestAsync(
                plan.ArtifactRef.Sha256,
                CancellationToken.None);
            CollectionAssert.AreEqual(
                plan.CopyCanonicalIdentityBytes(),
                profileBytes.ToArray(),
                "the retained profile artifact must be the plan identity its reference names");

            HttpResponseMessage ProductResponse(HttpRequestMessage request)
            {
                Interlocked.Increment(ref productSends);
                Assert.AreEqual(HttpMethod.Post, request.Method);
                Assert.AreEqual(
                    EuConsolidationDiscoveryPlan.PublisherEndpoint,
                    request.RequestUri?.AbsoluteUri);
                return Response(
                    request,
                    HttpStatusCode.OK,
                    "{\"head\":{\"vars\":[\"count\"]},\"results\":{\"bindings\":[]}}");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EuConsolidationPageSendsAgainstAFreshRealStoreUsingItsOwnCountEvidence()
    {
        // Completes Decision 75 for the EU page route. The count proof above stopped one step
        // short on purpose: a page bind additionally names ExpectedPartitionRowCountEvidenceRef,
        // and MachineQueryPlan.ExternalArtifactReferences offers no producer bytes for that
        // reference ("partition row-count evidence" is added with no bytes argument), so
        // OpenForSendAsync's fallback requires it already durably reachable by digest before the
        // page send is admitted. Seeding that digest ahead of time would be exactly the defect
        // this day of work exists to remove: a run cannot be shown to hold evidence that no run
        // actually produced.
        //
        // So: one session, one fresh FileSystemCustodyStore on an empty directory. The count
        // sends first, for real, inside this run. Its RoutedHttpEvidence -- the exact bytes this
        // run observed, not a fixture -- is then placed into the same real store under its own
        // digest, through the same public content-addressed contract the session's own dependency
        // retention already uses (RoutedHttpAcquisitionSession never retains the evidence
        // *document* itself as a side effect of sending; only its response body and its send
        // dependencies are retained that way). Only then is the page bound against that genuine
        // reference and sent. Nothing here is handed to the store before this run produced it.
        var plan = EuConsolidationDiscoveryPlan.Create();
        var rendererSourceBytes = "EU consolidation SPARQL renderer source"u8.ToArray();
        var rendererSource = MachineQueryRendererSource.Open(
            Artifact("urn:uuid:dc4cec16-6805-4ea2-91a9-047f60437523", rendererSourceBytes),
            rendererSourceBytes);
        var count = plan.BindCount(
            EuConsolidationQuerySet.Family,
            "32016R0679",
            EuConsolidationQueryPass.Pass1,
            "urn:uuid:f8d24cab-509a-4291-830c-fba6dba68165",
            "urn:uuid:83280ec4-d165-457a-9d87-95bce309b7d4",
            rendererSource);

        var root = Path.Combine(
            Path.GetTempPath(),
            "lex-fresh-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var custody = new FileSystemCustodyStore(root);
            var productSends = 0;
            var handler = new CountingHandler((ordinal, request) => ordinal switch
            {
                // The EU robots route is two hops by profile: a 301 off publications.europa.eu
                // and a 200 on op.europa.eu.
                0 => Response(
                    request,
                    HttpStatusCode.MovedPermanently,
                    "moved",
                    "https://op.europa.eu/robots.txt"),
                1 => Response(request, HttpStatusCode.OK, "User-agent: *\nAllow: /\n"),
                2 => CountResponse(request),
                _ => PageResponse(request),
            });
            using var session = Session(count.Request, handler, custody);
            var started = await BootstrapAsync(session);
            Assert.AreEqual(OfficialHttpAcquisitionOutcomeKind.ExecutedObservation, started.Kind);

            var countAttempt = await session.OpenPlanItem(count.Request)
                .ExecuteNextAttemptAsync(CancellationToken.None);
            Assert.AreEqual(
                OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
                countAttempt.Kind,
                "the count half of this run needs nothing already in the store");
            var countEvidence = countAttempt.Evidence
                ?? throw new AssertFailedException(
                    "An executed observation must carry HTTP evidence.");
            var countEvidenceBytes = countEvidence.CopyCanonicalBytes();

            // Retain the count's own evidence document under its own digest, in the same real
            // store, through the same public custody contract a genuine caller has. This is not
            // seeding: the bytes are exactly what this run's own count send just observed.
            var evidenceReceipt = await custody.CreateAsync(
                countEvidenceBytes,
                CustodyClass.NightlyFloor90d,
                CancellationToken.None);
            Assert.AreEqual(
                Sha256(countEvidenceBytes),
                evidenceReceipt.Reference.ContentSha256,
                "the retained receipt must name the exact evidence bytes this run observed");
            var countEvidenceRef = new SourceArtifactRef(
                "urn:uuid:0ad9d9de-c607-4004-809d-b66b61a8c8bf",
                evidenceReceipt.Reference.ContentSha256);

            var page = plan.BindPage(
                EuConsolidationQuerySet.Family,
                "32016R0679",
                EuConsolidationQueryPass.Pass1,
                null,
                0,
                countEvidenceRef,
                "urn:uuid:a0057fc1-01ca-4ceb-8254-271927b5184a",
                "urn:uuid:3189d23d-b360-4542-b5d4-20ef5a21cc8f",
                rendererSource);

            var pageAttempt = await session.OpenPlanItem(page.Request)
                .ExecuteNextAttemptAsync(CancellationToken.None);
            Assert.AreEqual(
                OfficialHttpAcquisitionOutcomeKind.ExecutedObservation,
                pageAttempt.Kind,
                "the page send must be admitted once its count evidence is genuinely reachable");
            Assert.AreEqual(2, productSends);

            AssertOpenedClosure(
                MachinePolicyFor(session, count.MachinePlanRef),
                5,
                count.MachinePlanRef,
                count.InputArtifact.ArtifactRef,
                plan.ArtifactRef,
                rendererSource.Reference);
            AssertOpenedClosure(
                MachinePolicyFor(session, page.MachinePlanRef),
                6,
                page.MachinePlanRef,
                page.InputArtifact.ArtifactRef,
                plan.ArtifactRef,
                rendererSource.Reference,
                countEvidenceRef);

            // Readable back out by digest for every artifact either send depended on, which is
            // the difference between having retained an artifact and having named one.
            foreach (var digest in new[]
                {
                    plan.ArtifactRef.Sha256,
                    rendererSource.Reference.Sha256,
                    countEvidenceRef.Sha256,
                })
            {
                var reopened = await custody.ReadByDigestAsync(digest, CancellationToken.None);
                Assert.AreEqual(
                    digest,
                    Convert.ToHexString(SHA256.HashData(reopened.Span)).ToLowerInvariant());
            }

            var reopenedEvidenceBytes = await custody.ReadByDigestAsync(
                countEvidenceRef.Sha256,
                CancellationToken.None);
            CollectionAssert.AreEqual(
                countEvidenceBytes,
                reopenedEvidenceBytes.ToArray(),
                "the retained evidence artifact must be the exact bytes this run's count observed");

            HttpResponseMessage CountResponse(HttpRequestMessage request)
            {
                Interlocked.Increment(ref productSends);
                Assert.AreEqual(HttpMethod.Post, request.Method);
                Assert.AreEqual(
                    EuConsolidationDiscoveryPlan.PublisherEndpoint,
                    request.RequestUri?.AbsoluteUri);
                return Response(
                    request,
                    HttpStatusCode.OK,
                    "{\"head\":{\"vars\":[\"count\"]},\"results\":{\"bindings\":[]}}");
            }

            HttpResponseMessage PageResponse(HttpRequestMessage request)
            {
                Interlocked.Increment(ref productSends);
                Assert.AreEqual(HttpMethod.Post, request.Method);
                Assert.AreEqual(
                    EuConsolidationDiscoveryPlan.PublisherEndpoint,
                    request.RequestUri?.AbsoluteUri);
                return Response(
                    request,
                    HttpStatusCode.OK,
                    "{\"head\":{\"vars\":[\"base_celex\",\"base\",\"state\",\"family_multiplicity\"," +
                    "\"state_key\"]},\"results\":{\"bindings\":[]}}");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            robots.Uri,
            null,
        ];
        Assert.IsTrue((bool)(typeof(RoutedHttpAcquisitionSession).GetMethod(
            "TryCreateRedirectRequest",
            BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, redirectArguments)
            ?? throw new AssertFailedException("The redirect constructor returned no verdict.")));
        var redirectedRobots = Assert.IsInstanceOfType<HttpLogicalRequest>(redirectArguments[3]);
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
            [request, custodyStore, handler, new AdvancingTimeProvider(), false, Array.Empty<string>()]);
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
