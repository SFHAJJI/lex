using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Evaluation;
using Lex.Index;
using Lex.Mcp;
using Lex.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lex.Tests;

public sealed class EvaluationAdmissionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Signed_admission_is_release_bound_and_commits_only_exact_requests()
    {
        var (authority, privateKey) = Authority();
        var capability = Capability();
        var bytes = EvaluationAdmissionContract.Serialize(capability);
        var signature = ArtifactManifests.SignBase64(bytes, privateKey);

        var verified = EvaluationAdmissionContract.Verify(
            bytes, signature, authority, Identity(), Now);

        Assert.Equal(capability.CandidateRevision, verified.CandidateRevision);
        Assert.Equal(capability.AllowedRequests, verified.AllowedRequests);
        Assert.Throws<CryptographicException>(() =>
            EvaluationAdmissionContract.Verify(
                Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)
                    .Replace(new string('c', 64), new string('d', 64),
                        StringComparison.Ordinal)),
                signature, authority, Identity(), Now));
        Assert.Throws<InvalidDataException>(() =>
            EvaluationAdmissionContract.Verify(
                bytes, signature, authority,
                Identity() with { CandidateRevision = "lex--wrong" }, Now));
        Assert.Throws<InvalidDataException>(() =>
            EvaluationAdmissionContract.Verify(
                bytes, signature, authority, Identity(), capability.ExpiresAt));
    }

    [Fact]
    public void Malformed_null_capability_fields_fail_as_invalid_data()
    {
        var capability = JsonNode.Parse(
            EvaluationAdmissionContract.Serialize(Capability()))!.AsObject();
        capability["key_id"] = null;
        Assert.Throws<InvalidDataException>(() =>
            EvaluationAdmissionContract.Parse(
                Encoding.UTF8.GetBytes(capability.ToJsonString())));

        capability = JsonNode.Parse(
            EvaluationAdmissionContract.Serialize(Capability()))!.AsObject();
        capability["allowed_requests"] = new JsonArray((JsonNode?)null);
        capability["max_calls"] = 1;
        Assert.Throws<InvalidDataException>(() =>
            EvaluationAdmissionContract.Parse(
                Encoding.UTF8.GetBytes(capability.ToJsonString())));

        var skippedFirstTurn = Capability() with
        {
            AllowedRequests =
            [Capability().AllowedRequests[0] with { Turn = 2 }],
        };
        Assert.Throws<InvalidDataException>(() =>
            EvaluationAdmissionContract.Serialize(skippedFirstTurn));
        Assert.Throws<InvalidDataException>(() =>
            EvaluationAdmissionContract.Serialize(
                Capability() with { MaxCalls = 2 }));
    }

    [Fact]
    public void Admission_registry_is_opaque_bounded_expiring_and_replay_counted()
    {
        var clock = new ManualClock(Now);
        var registry = new EvaluationAdmissionRegistry(
            clock, maximumEntries: 1, maximumRetainedBytes: 16_384);
        var capability = Capability();

        var registered = registry.Register(capability,
            EvaluationAdmissionContract.Serialize(capability).Length);

        Assert.Equal(EvaluationAdmissionRegistrationKind.Registered, registered.Kind);
        Assert.True(EvaluationAdmissionRegistry.IsValidToken(registered.Token));
        Assert.Equal(EvaluationAdmissionRegistrationKind.Replayed,
            registry.Register(capability,
                EvaluationAdmissionContract.Serialize(capability).Length).Kind);
        var secondCapability = capability with { Nonce = OpaqueToken(9) };
        Assert.Equal(EvaluationAdmissionRegistrationKind.Capacity,
            registry.Register(secondCapability,
                EvaluationAdmissionContract.Serialize(secondCapability).Length).Kind);
        Assert.Equal(EvaluationAdmissionAuthorizationKind.Allowed,
            registry.Inspect(registered.Token!, "eval-run-case-1",
                EvaluationAdmissionContract.RequestBodySha256("Show coverage.")));
        Assert.Equal(EvaluationAdmissionAuthorizationKind.RequestNotAllowed,
            registry.Inspect(registered.Token!, "eval-run-case-1",
                EvaluationAdmissionContract.RequestBodySha256("Show something else.")));

        using (var use = registry.Reserve(
                   registered.Token!, "eval-run-case-1",
                   EvaluationAdmissionContract.RequestBodySha256("Show coverage.")))
        {
            Assert.NotNull(use);
            Assert.Equal(EvaluationAdmissionAuthorizationKind.AlreadyUsed,
                registry.Inspect(registered.Token!, "eval-run-case-1",
                    EvaluationAdmissionContract.RequestBodySha256("Show coverage.")));
            use!.Commit();
        }
        Assert.Null(registry.Reserve(
            registered.Token!, "eval-run-case-1",
            EvaluationAdmissionContract.RequestBodySha256("Show coverage.")));
        Assert.Equal(1, registry.ConsumedCallsFor(registered.Token!));

        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(EvaluationAdmissionAuthorizationKind.Expired,
            registry.Inspect(registered.Token!, "eval-run-case-1",
                EvaluationAdmissionContract.RequestBodySha256("Show coverage.")));
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, registry.RetainedBytes);

        var bytes = EvaluationAdmissionContract.Serialize(capability).Length;
        var byteBounded = new EvaluationAdmissionRegistry(
            new ManualClock(Now), maximumEntries: 2, maximumRetainedBytes: bytes);
        Assert.Equal(EvaluationAdmissionRegistrationKind.Registered,
            byteBounded.Register(capability, bytes).Kind);
        Assert.Equal(EvaluationAdmissionRegistrationKind.Capacity,
            byteBounded.Register(secondCapability, bytes).Kind);
    }

    [Fact]
    public void Uncommitted_admission_reservation_can_be_retried_after_shared_busy_rejection()
    {
        var registry = new EvaluationAdmissionRegistry(
            new ManualClock(Now), maximumEntries: 2, maximumRetainedBytes: 16_384);
        var capability = Capability();
        var token = registry.Register(capability,
            EvaluationAdmissionContract.Serialize(capability).Length).Token!;
        var digest = EvaluationAdmissionContract.RequestBodySha256("Show coverage.");

        using (var rejectedBeforeModel = registry.Reserve(
                   token, "eval-run-case-1", digest))
            Assert.NotNull(rejectedBeforeModel);

        using var retry = registry.Reserve(token, "eval-run-case-1", digest);
        Assert.NotNull(retry);
        retry!.Commit();
        retry.Dispose();
        Assert.Equal(1, registry.ConsumedCallsFor(token));
    }

    [Fact]
    public void Admission_registry_binds_signed_turns_to_one_fresh_server_thread()
    {
        var first = "Approved setup.";
        var second = "Approved follow-up.";
        var fresh = "Approved independent case.";
        var capability = Capability() with
        {
            MaxCalls = 3,
            MaximumCandidateInputTokens = 3_000,
            MaximumCandidateOutputTokens = 600,
            MaximumCostEur = 0.30m,
            AllowedRequests =
            [
                Request("eval-flow-setup", first, "eval-flow", 1),
                Request("eval-flow-final", second, "eval-flow", 2),
                Request("eval-fresh-final", fresh, "eval-fresh", 1),
            ],
        };
        var registry = new EvaluationAdmissionRegistry(new ManualClock(Now));
        var token = registry.Register(capability,
            EvaluationAdmissionContract.Serialize(capability).Length).Token!;
        var thread = OpaqueToken(7);
        var otherThread = OpaqueToken(8);

        Assert.Equal(EvaluationAdmissionAuthorizationKind.RequestNotAllowed,
            registry.Inspect(token, "eval-flow-setup",
                EvaluationAdmissionContract.RequestBodySha256(first), thread));
        using (var setup = registry.Reserve(token, "eval-flow-setup",
                   EvaluationAdmissionContract.RequestBodySha256(first)))
        {
            Assert.NotNull(setup);
            setup!.Commit(thread);
        }
        Assert.Equal(EvaluationAdmissionAuthorizationKind.RequestNotAllowed,
            registry.Inspect(token, "eval-flow-final",
                EvaluationAdmissionContract.RequestBodySha256(second)));
        Assert.Equal(EvaluationAdmissionAuthorizationKind.RequestNotAllowed,
            registry.Inspect(token, "eval-flow-final",
                EvaluationAdmissionContract.RequestBodySha256(second), otherThread));
        Assert.Equal(EvaluationAdmissionAuthorizationKind.Allowed,
            registry.Inspect(token, "eval-flow-final",
                EvaluationAdmissionContract.RequestBodySha256(second), thread));
        using (var final = registry.Reserve(token, "eval-flow-final",
                   EvaluationAdmissionContract.RequestBodySha256(second), thread))
        {
            Assert.NotNull(final);
            final!.Commit();
        }

        Assert.Equal(EvaluationAdmissionAuthorizationKind.RequestNotAllowed,
            registry.Inspect(token, "eval-fresh-final",
                EvaluationAdmissionContract.RequestBodySha256(fresh), thread));
        Assert.Equal(EvaluationAdmissionAuthorizationKind.Allowed,
            registry.Inspect(token, "eval-fresh-final",
                EvaluationAdmissionContract.RequestBodySha256(fresh)));

        static EvaluationAdmissionRequest Request(
            string key, string message, string invocation, int turn) => new(
            key,
            EvaluationAdmissionContract.RequestBodySha256(message),
            invocation,
            turn,
            1_000,
            200,
            0.10m);
    }

    [Fact]
    public async Task Signed_lane_bypasses_public_daily_counters_and_duplicate_spend()
    {
        var clock = new ManualClock(Now);
        var publicAdmission = new AskAdmissionController(
            clock, perClientDaily: 1, globalDaily: 1, concurrent: 1);
        var registry = new EvaluationAdmissionRegistry(
            clock, maximumEntries: 2, maximumRetainedBytes: 32_768);
        var (authority, privateKey) = Authority();
        var capability = Capability();
        var capabilityBytes = EvaluationAdmissionContract.Serialize(capability);
        var verifier = new EvaluationAdmissionVerifier(authority, Identity(), clock);
        await using var site = new EvaluationSite(
            publicAdmission, registry, verifier);

        using var publicRequest = AskRequest(
            "Show coverage.", "public-daily-turn", evaluationToken: null);
        Assert.Equal(200, (int)(await site.Client.SendAsync(publicRequest)).StatusCode);
        Assert.Equal(1, publicAdmission.AcceptedToday);

        using var exchange = new HttpRequestMessage(
            HttpMethod.Post, "/api/ask/evaluation/admission")
        {
            Content = new ByteArrayContent(capabilityBytes),
        };
        exchange.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        exchange.Headers.Add("X-Lex-Evaluation-Admission-Signature",
            ArtifactManifests.SignBase64(capabilityBytes, privateKey));
        var exchangeResponse = await site.Client.SendAsync(exchange);
        var exchangeBody = JsonNode.Parse(
            await exchangeResponse.Content.ReadAsStringAsync());
        var token = exchangeBody?["evaluation_token"]?.GetValue<string>();

        Assert.Equal(200, (int)exchangeResponse.StatusCode);
        Assert.True(exchangeResponse.Headers.CacheControl?.NoStore);
        Assert.True(exchangeResponse.Headers.CacheControl?.Private);
        Assert.True(EvaluationAdmissionRegistry.IsValidToken(token));
        Assert.DoesNotContain(capability.Nonce,
            await exchangeResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var owner = AskRequest(
            "Show coverage.", "eval-run-case-1", token);
        using var duplicate = AskRequest(
            "Show coverage.", "eval-run-case-1", token);
        Assert.Equal(200, (int)(await site.Client.SendAsync(owner)).StatusCode);
        Assert.Equal(200, (int)(await site.Client.SendAsync(duplicate)).StatusCode);
        Assert.Equal(2, site.Planner.Calls);
        Assert.Equal(1, registry.ConsumedCallsFor(token!));
        Assert.Equal(1, publicAdmission.AcceptedToday);

        using var deniedPublic = AskRequest(
            "Show coverage.", "second-public-turn", evaluationToken: null);
        deniedPublic.RequestUri = new Uri("/api/ask", UriKind.Relative);
        deniedPublic.Headers.Remove("X-Lex-Stream-Version");
        Assert.Equal(429, (int)(await site.Client.SendAsync(deniedPublic)).StatusCode);
    }

    [Fact]
    public async Task Signed_lane_rejects_an_uncommitted_prompt_before_thread_or_planner()
    {
        var clock = new ManualClock(Now);
        var registry = new EvaluationAdmissionRegistry(clock);
        var (authority, privateKey) = Authority();
        var capability = Capability();
        var bytes = EvaluationAdmissionContract.Serialize(capability);
        await using var site = new EvaluationSite(
            new AskAdmissionController(clock, 1, 1, 1), registry,
            new EvaluationAdmissionVerifier(authority, Identity(), clock));
        var token = await Exchange(site.Client, bytes,
            ArtifactManifests.SignBase64(bytes, privateKey));

        using var request = AskRequest(
            "Show something else.", "eval-run-case-1", token);
        var response = await site.Client.SendAsync(request);

        Assert.Equal(403, (int)response.StatusCode);
        Assert.Equal(0, site.Planner.Calls);
        Assert.Equal(0, registry.ConsumedCallsFor(token));
        Assert.Equal(0, site.Services.GetRequiredService<AskThreadRegistry>().Count);
    }

    [Fact]
    public async Task Signed_lane_enforces_fresh_start_and_blocks_public_thread_contamination()
    {
        const string setupMessage = "Approved setup.";
        const string finalMessage = "Approved follow-up.";
        var clock = new ManualClock(Now);
        var registry = new EvaluationAdmissionRegistry(clock);
        var (authority, privateKey) = Authority();
        var capability = Capability() with
        {
            MaxCalls = 2,
            MaximumCandidateInputTokens = 2_000,
            MaximumCandidateOutputTokens = 400,
            MaximumCostEur = 0.20m,
            AllowedRequests =
            [
                Request("eval-flow-setup", setupMessage, "eval-flow", 1),
                Request("eval-flow-final", finalMessage, "eval-flow", 2),
            ],
        };
        var bytes = EvaluationAdmissionContract.Serialize(capability);
        await using var site = new EvaluationSite(
            new AskAdmissionController(clock, 10, 10, 2), registry,
            new EvaluationAdmissionVerifier(authority, Identity(), clock));
        var token = await Exchange(site.Client, bytes,
            ArtifactManifests.SignBase64(bytes, privateKey));

        using var setup = JsonAskRequest(
            setupMessage, "eval-flow-setup", token, threadToken: null);
        var setupResponse = await site.Client.SendAsync(setup);
        var setupBody = JsonNode.Parse(await setupResponse.Content.ReadAsStringAsync());
        var threadToken = setupBody?["thread_token"]?.GetValue<string>();
        Assert.Equal(200, (int)setupResponse.StatusCode);
        Assert.True(AskThreadRegistry.IsValidToken(threadToken));

        using var contamination = JsonAskRequest(
            "Unapproved intervening prompt.", "public-contamination", null, threadToken);
        Assert.Equal(409, (int)(await site.Client.SendAsync(contamination)).StatusCode);
        Assert.Equal(1, site.Planner.Calls);

        using var final = JsonAskRequest(
            finalMessage, "eval-flow-final", token, threadToken);
        Assert.Equal(200, (int)(await site.Client.SendAsync(final)).StatusCode);
        Assert.Equal(2, site.Planner.Calls);
        Assert.Equal(2, registry.ConsumedCallsFor(token));

        using var publicReset = ResetRequest(
            "public-reset", threadToken!, evaluationToken: null);
        Assert.Equal(409, (int)(await site.Client.SendAsync(publicReset)).StatusCode);
        using var admittedReset = ResetRequest(
            "eval-reset", threadToken!, token);
        Assert.Equal(200, (int)(await site.Client.SendAsync(admittedReset)).StatusCode);

        static EvaluationAdmissionRequest Request(
            string key, string message, string invocation, int turn) => new(
            key,
            EvaluationAdmissionContract.RequestBodySha256(message),
            invocation,
            turn,
            1_000,
            200,
            0.10m);
    }

    private static EvaluationAdmissionCapability Capability() => new(
        EvaluationAdmissionContract.Schema,
        "review-key",
        "entra:reviewer",
        "lex--candidate",
        "registry.example/lex@sha256:" + new string('a', 64),
        new string('b', 40),
        new string('c', 64),
        new string('d', 64),
        Now,
        Now.AddMinutes(10),
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        1,
        12_000,
        3_000,
        1.00m,
        [new EvaluationAdmissionRequest(
            "eval-run-case-1",
            EvaluationAdmissionContract.RequestBodySha256("Show coverage."),
            "eval-run-case-1",
            1,
            12_000,
            3_000,
            1.00m)]);

    private static string OpaqueToken(byte value) => Convert.ToBase64String(
            Enumerable.Repeat(value, 32).ToArray())
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static EvaluationAdmissionIdentity Identity() => new(
        "lex--candidate",
        "registry.example/lex@sha256:" + new string('a', 64),
        new string('b', 40),
        new string('c', 64),
        new string('d', 64));

    private static (EvaluationAdmissionAuthority Authority, string PrivateKey) Authority()
    {
        var privateKey = StampSigner.CreateKeyPem();
        var root = ArtifactManifests.TrustRoot("review-key", privateKey);
        return (new EvaluationAdmissionAuthority(
            "entra:reviewer", root.KeyId, root.FingerprintSha256, root.PublicKeyPem),
            privateKey);
    }

    private static async Task<string> Exchange(
        HttpClient client,
        byte[] capability,
        string signature)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/ask/evaluation/admission")
        {
            Content = new ByteArrayContent(capability),
        };
        request.Headers.Add("X-Lex-Evaluation-Admission-Signature", signature);
        var response = await client.SendAsync(request);
        Assert.Equal(200, (int)response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())?
            ["evaluation_token"]?.GetValue<string>()
            ?? throw new InvalidDataException("Evaluation token absent.");
    }

    private static HttpRequestMessage AskRequest(
        string message,
        string idempotencyKey,
        string? evaluationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask/stream")
        {
            Content = new ByteArrayContent(EvaluationAdmissionContract.RequestBody(message)),
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Lex-Stream-Version", "1");
        if (evaluationToken is not null)
            request.Headers.Add("X-Lex-Evaluation-Admission", evaluationToken);
        return request;
    }

    private static HttpRequestMessage JsonAskRequest(
        string message,
        string idempotencyKey,
        string? evaluationToken,
        string? threadToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask")
        {
            Content = new ByteArrayContent(
                EvaluationAdmissionContract.RequestBody(message)),
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (evaluationToken is not null)
            request.Headers.Add("X-Lex-Evaluation-Admission", evaluationToken);
        if (threadToken is not null)
            request.Headers.Add("X-Lex-Thread-Token", threadToken);
        return request;
    }

    private static HttpRequestMessage ResetRequest(
        string idempotencyKey,
        string threadToken,
        string? evaluationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/ask/thread/reset")
        {
            Content = new ByteArrayContent([]),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Lex-Thread-Token", threadToken);
        if (evaluationToken is not null)
            request.Headers.Add("X-Lex-Evaluation-Admission", evaluationToken);
        return request;
    }

    private sealed class EvaluationSite : WebApplicationFactory<Program>
    {
        private readonly string _indexDir = Path.Combine(
            Path.GetTempPath(), $"lex-eval-admission-{Guid.NewGuid():N}");
        private readonly AskAdmissionController _publicAdmission;
        private readonly EvaluationAdmissionRegistry _registry;
        private readonly EvaluationAdmissionVerifier _verifier;

        public EvaluationSite(
            AskAdmissionController publicAdmission,
            EvaluationAdmissionRegistry registry,
            EvaluationAdmissionVerifier verifier)
        {
            Directory.CreateDirectory(_indexDir);
            _publicAdmission = publicAdmission;
            _registry = registry;
            _verifier = verifier;
            Planner = new BoundaryPlanner();
            Client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        }

        public HttpClient Client { get; }
        public BoundaryPlanner Planner { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _indexDir);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://evaluation.test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AskService>();
                services.AddSingleton(_ => new AskService(
                    new McpCore(new Dictionary<string, LexIndexReader>()),
                    Planner, admission: _publicAdmission));
                services.RemoveAll<EvaluationAdmissionRegistry>();
                services.AddSingleton(_registry);
                services.RemoveAll<EvaluationAdmissionVerifier>();
                services.AddSingleton(_verifier);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await base.DisposeAsync();
            try { Directory.Delete(_indexDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Evaluation_owned_threads_reject_public_or_other_capability_access()
    {
        var threads = new AskThreadRegistry(new ManualClock(Now));
        var evaluationScope = new string('a', 64);
        var created = await threads.AcquireAsync(
            null, CancellationToken.None, evaluationScope);
        await using var owner = created.Lease!;
        Assert.True(owner.Commit(
            "Approved setup.", "Approved answer.", null,
            AskConversationContextDisposition.Clear));
        var token = owner.Token;
        await owner.DisposeAsync();

        Assert.Equal(AskThreadAcquireKind.NotFound,
            (await threads.AcquireAsync(token)).Kind);
        Assert.Equal(AskThreadAcquireKind.NotFound,
            (await threads.AcquireAsync(
                token, CancellationToken.None, new string('b', 64))).Kind);
        var resumed = await threads.AcquireAsync(
            token, CancellationToken.None, evaluationScope);
        Assert.Equal(AskThreadAcquireKind.Acquired, resumed.Kind);
        await resumed.Lease!.DisposeAsync();
        Assert.False(threads.Reset(token));
        Assert.True(threads.Reset(token, evaluationScope));
    }

    private sealed class BoundaryPlanner : IOperationPlanner
    {
        public int Calls { get; private set; }

        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(OperationPlan.FromPlannerOutput(
                requestId, "en", new JsonArray(new JsonObject
                {
                    ["tool"] = "legal_boundary",
                    ["arguments"] = new JsonObject { ["reason"] = "legal advice" },
                }), synthesisRequested: false));
        }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
