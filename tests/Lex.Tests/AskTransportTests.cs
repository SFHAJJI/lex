using System.Text;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Mcp;
using Lex.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lex.Tests;

public sealed class AskTransportTests
{
    [Fact]
    public async Task Chunked_body_limit_is_enforced_without_trusting_content_length()
    {
        var exact = new MemoryStream(new byte[65_536]);
        var oversized = new MemoryStream(new byte[65_537]);

        var accepted = await BoundedRequestBody.ReadAsync(
            exact, 65_536, CancellationToken.None);
        var rejected = await BoundedRequestBody.ReadAsync(
            oversized, 65_536, CancellationToken.None);

        Assert.Equal(65_536, accepted?.Length);
        Assert.Null(rejected);
    }

    [Fact]
    public async Task Idempotency_joins_identical_work_and_rejects_key_reuse_with_other_bytes()
    {
        var registry = new AskRequestRegistry(TimeProvider.System, maximumEntries: 8);
        var owner = registry.Claim("client", "request", Hash("same"));
        var duplicate = registry.Claim("client-on-another-network", "request", Hash("same"));
        var conflict = registry.Claim("third-network", "request", Hash("different"));

        Assert.Equal(AskRequestClaimKind.Owner, owner.Kind);
        Assert.Equal(AskRequestClaimKind.Duplicate, duplicate.Kind);
        Assert.Equal(AskRequestClaimKind.Conflict, conflict.Kind);
        Assert.Equal(owner.RequestId, duplicate.RequestId);

        owner.Complete(200, "{\"reply\":\"one execution\"}", retainForReplay: true);
        var response = await duplicate.Completion;
        Assert.Equal(200, response.Status);
        Assert.Contains("one execution", response.Body);
    }

    [Fact]
    public void Idempotency_registry_is_bounded_while_requests_are_active()
    {
        var registry = new AskRequestRegistry(TimeProvider.System, maximumEntries: 1);

        Assert.Equal(AskRequestClaimKind.Owner,
            registry.Claim("client", "one", Hash("one")).Kind);
        Assert.Equal(AskRequestClaimKind.Busy,
            registry.Claim("client", "two", Hash("two")).Kind);
    }

    [Fact]
    public async Task Nonexecuted_requests_wake_duplicates_without_filling_the_replay_registry()
    {
        var registry = new AskRequestRegistry(TimeProvider.System, maximumEntries: 1);
        var owner = registry.Claim("attacker", "bad-one", Hash("bad"));
        var duplicate = registry.Claim("attacker", "bad-one", Hash("bad"));

        owner.Complete(200, "{\"reply\":\"No instrument was selected.\"}",
            retainForReplay: false);

        Assert.Equal(200, (await duplicate.Completion).Status);
        Assert.Equal(AskRequestClaimKind.Owner,
            registry.Claim("other-client", "valid", Hash("valid")).Kind);
    }

    [Fact]
    public async Task Replay_registry_evicts_completed_responses_to_stay_within_its_memory_budget()
    {
        var registry = new AskRequestRegistry(
            TimeProvider.System, maximumEntries: 8, maximumRetainedBytes: 32);
        var first = registry.Claim("a", "one", Hash("one"));
        var firstWaiter = registry.Claim("b", "one", Hash("one"));
        first.Complete(200, new string('a', 12), retainForReplay: true);
        Assert.Equal(new string('a', 12), (await firstWaiter.Completion).Body);

        var second = registry.Claim("a", "two", Hash("two"));
        second.Complete(200, new string('b', 12), retainForReplay: true);

        Assert.True(registry.RetainedBytes <= 32);
        Assert.Equal(AskRequestClaimKind.ReplayUnavailable,
            registry.Claim("a", "one", Hash("one")).Kind);
        Assert.Equal(AskRequestClaimKind.Duplicate,
            registry.Claim("a", "two", Hash("two")).Kind);
    }

    [Fact]
    public void Duplicate_stream_subscribers_are_bounded_and_disconnect_releases_capacity()
    {
        var registry = new AskRequestRegistry(TimeProvider.System,
            maximumEntries: 4, maximumSubscribersPerEntry: 1, maximumSubscribers: 1);
        _ = registry.Claim("owner", "same", Hash("body"));
        var subscriber = registry.Claim("one", "same", Hash("body"));

        Assert.Equal(AskRequestClaimKind.Duplicate, subscriber.Kind);
        Assert.Equal(AskRequestClaimKind.Busy,
            registry.Claim("two", "same", Hash("body")).Kind);

        subscriber.Unsubscribe();
        Assert.Equal(AskRequestClaimKind.Duplicate,
            registry.Claim("three", "same", Hash("body")).Kind);
    }

    [Fact]
    public void Assistant_admission_orders_client_then_busy_then_global_without_charging_rejections()
    {
        var admission = new AskAdmissionController(
            TimeProvider.System, perClientDaily: 1, globalDaily: 1, concurrent: 1);

        using var accepted = admission.TryAdmit("client-a").Lease;
        var sameClient = admission.TryAdmit("client-a");
        var busyOtherClient = admission.TryAdmit("client-b");

        Assert.Equal(AskAdmissionFailure.PerClientQuota, sameClient.Failure);
        Assert.Equal(AskAdmissionFailure.Busy, busyOtherClient.Failure);

        accepted?.Dispose();
        var globalAfterRelease = admission.TryAdmit("client-b");
        Assert.Equal(AskAdmissionFailure.GlobalQuota, globalAfterRelease.Failure);
        Assert.Equal(1, admission.AcceptedToday);
    }

    [Fact]
    public async Task Http_stream_is_versioned_and_a_duplicate_does_not_plan_twice()
    {
        await using var site = new StreamingSite();
        const string body = "{\"messages\":[{\"role\":\"user\",\"content\":\"Can Lex advise me?\"}]}";
        using var first = Request(body, "stream-request");
        using var duplicate = Request(body, "stream-request");

        var firstResponse = await site.Client.SendAsync(first);
        var firstWire = await firstResponse.Content.ReadAsStringAsync();
        var duplicateResponse = await site.Client.SendAsync(duplicate);
        var duplicateWire = await duplicateResponse.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)firstResponse.StatusCode);
        Assert.Equal(200, (int)duplicateResponse.StatusCode);
        Assert.Equal(1, site.Planner.Calls);
        var serverRequestId = firstResponse.Headers.GetValues("X-Lex-Request-Id").Single();
        Assert.Matches("^[a-f0-9]{32}$", serverRequestId);
        Assert.NotEqual("stream-request", serverRequestId);
        Assert.Equal(serverRequestId,
            duplicateResponse.Headers.GetValues("X-Lex-Request-Id").Single());
        var frames = Frames(firstWire);
        Assert.Equal(["operation_result", "done"], frames.Select(frame => frame.Event));
        Assert.All(frames, frame =>
        {
            Assert.Equal("1", frame.Data["version"]?.GetValue<string>());
            Assert.Equal(serverRequestId, frame.Data["request_id"]?.GetValue<string>());
        });
        Assert.Equal([1, 2], frames.Select(frame => frame.Data["sequence"]?.GetValue<int>()));
        Assert.Equal(["operation_result", "done"],
            Frames(duplicateWire).Select(frame => frame.Event));
        Assert.All(frames.Where(frame => frame.Event == "operation_result"), frame =>
            Assert.DoesNotContain("stream-request",
                frame.Data["payload"]?["operation_id"]?.GetValue<string>() ?? ""));
    }

    [Fact]
    public async Task Http_stream_rejects_an_unversioned_client_before_planning()
    {
        await using var site = new StreamingSite();
        using var request = Request(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"Show coverage.\"}]}",
            "unversioned-request");
        request.Headers.Remove("X-Lex-Stream-Version");

        var response = await site.Client.SendAsync(request);

        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal(0, site.Planner.Calls);
    }

    [Fact]
    public async Task Http_stream_rejections_return_bounded_actionable_json_before_planning()
    {
        await using var site = new StreamingSite();
        using var invalidKey = Request(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"coverage\"}]}",
            new string('x', 129));
        using var badJson = Request("{", "bad-json");
        using var missingMessages = Request("{}", "missing-messages");
        using var oversized = Request("", "oversized");
        oversized.Content = new ByteArrayContent(new byte[65_537]);

        var cases = new[]
        {
            (await site.Client.SendAsync(invalidKey), 400, "Invalid Idempotency-Key."),
            (await site.Client.SendAsync(badJson), 400, "Bad JSON."),
            (await site.Client.SendAsync(missingMessages), 400, "Body must be {\"messages\": [...]}."),
            (await site.Client.SendAsync(oversized), 413, "Request too large."),
        };

        foreach (var (response, status, error) in cases)
        {
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(error, JsonNode.Parse(await response.Content.ReadAsStringAsync())?
                ["error"]?.GetValue<string>());
        }
        Assert.Equal(0, site.Planner.Calls);
    }

    [Fact]
    public async Task Idempotency_key_boundaries_are_checked_before_planning_and_stay_out_of_request_ids()
    {
        await using var site = new StreamingSite();
        const string body = "{\"messages\":[{\"role\":\"user\",\"content\":\"Show coverage.\"}]}";
        using var missing = Request(body, "placeholder");
        missing.Headers.Remove("Idempotency-Key");
        using var maximum = Request(body, new string('a', 128));
        using var over = Request(body, new string('b', 129));

        var missingResponse = await site.Client.SendAsync(missing);
        var maximumResponse = await site.Client.SendAsync(maximum);
        var overResponse = await site.Client.SendAsync(over);

        Assert.Equal(200, (int)missingResponse.StatusCode);
        Assert.Equal(200, (int)maximumResponse.StatusCode);
        Assert.Equal(400, (int)overResponse.StatusCode);
        Assert.Equal(2, site.Planner.Calls);
        var serverRequestId = maximumResponse.Headers
            .GetValues("X-Lex-Request-Id").Single();
        Assert.Matches("^[a-f0-9]{32}$", serverRequestId);
        Assert.DoesNotContain(new string('a', 128),
            await maximumResponse.Content.ReadAsStringAsync());

        var headers = new HeaderDictionary { ["Idempotency-Key"] = "" };
        Assert.False(ApiEndpoints.TryIdempotencyKey(headers, out _));
    }

    [Fact]
    public async Task A_progress_transport_disconnect_cannot_change_the_legal_result()
    {
        var planner = new BoundaryPlanner();
        var service = new AskService(
            new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>()), planner);
        var progress = new AskService.AskProgressCallbacks(
            OperationResult: (_, _) => throw new IOException("reader disconnected"));

        var (status, body) = await service.AskAsync(new JsonArray(new JsonObject
        {
            ["role"] = "user", ["content"] = "Can Lex advise me?",
        }), "client", "law.test", CancellationToken.None, progress, "disconnect-request");

        Assert.Equal(200, status);
        Assert.Equal("legal_boundary",
            body["operations"]?[0]?["legal_outcome"]?.GetValue<string>());
        Assert.Null(body["error"]);
    }

    [Fact]
    public async Task Reader_cancellation_after_one_result_preserves_it_and_terminates_the_rest()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new AskService(
            new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>()),
            new TwoBoundaryPlanner());
        var observed = 0;
        var progress = new AskService.AskProgressCallbacks(
            OperationResult: (_, _) =>
            {
                if (Interlocked.Increment(ref observed) == 1)
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
                return ValueTask.CompletedTask;
            });

        var (status, body) = await service.AskAsync(new JsonArray(new JsonObject
        {
            ["role"] = "user", ["content"] = "Can Lex advise and interpret this?",
        }), "client", "law.test", cancellation.Token, progress, "cancel-after-one");

        Assert.Equal(499, status);
        var operations = Assert.IsType<JsonArray>(body["operations"]);
        Assert.Equal(2, operations.Count);
        Assert.Equal("legal_boundary", operations[0]?["legal_outcome"]?.GetValue<string>());
        Assert.Equal("completed", operations[0]?["transport_outcome"]?.GetValue<string>());
        Assert.Equal("not_evaluated", operations[1]?["legal_outcome"]?.GetValue<string>());
        Assert.Equal("cancelled", operations[1]?["transport_outcome"]?.GetValue<string>());
        Assert.NotNull(operations[0]?["ui"]?["gap"]);
        Assert.NotNull(operations[1]?["ui"]?["gap"]);
        Assert.Equal(2, observed);
    }

    [Fact]
    public async Task Malformed_history_is_rejected_before_admission_and_is_not_replay_cached()
    {
        var planner = new BoundaryPlanner();
        var service = new AskService(
            new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>()), planner);

        foreach (var history in new[]
                 {
                     new JsonArray(JsonValue.Create("not-an-object")),
                     new JsonArray(new JsonObject { ["role"] = 1, ["content"] = "question" }),
                     new JsonArray(new JsonObject { ["role"] = "user", ["content"] = 1 }),
                 })
        {
            var outcome = await service.AskAsync(
                history, "client", "law.test", CancellationToken.None);
            Assert.Equal(400, outcome.Status);
            Assert.False(outcome.RetainForReplay);
        }
        Assert.Equal(0, planner.Calls);
    }

    [Fact]
    public async Task Planner_deadline_is_bounded_and_releases_the_execution_lease()
    {
        var planner = new WaitingPlanner();
        var service = new AskService(
            new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>()), planner,
            admission: new AskAdmissionController(
                TimeProvider.System, perClientDaily: 10, globalDaily: 10, concurrent: 1),
            plannerDeadline: TimeSpan.FromMilliseconds(50),
            firstResultDeadline: TimeSpan.FromMilliseconds(100));
        var history = new JsonArray(new JsonObject
        {
            ["role"] = "user", ["content"] = "Show coverage.",
        });

        var started = System.Diagnostics.Stopwatch.StartNew();
        var first = await service.AskAsync(
            history, "first-client", "law.test", CancellationToken.None);
        var second = await service.AskAsync(
            history, "second-client", "law.test", CancellationToken.None);

        Assert.Equal(504, first.Status);
        Assert.Equal(504, second.Status);
        Assert.True(first.RetainForReplay);
        Assert.Equal(2, planner.Calls);
        Assert.True(started.Elapsed < TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.FromSeconds(12), AskService.DefaultPlannerDeadline);
        Assert.Equal(TimeSpan.FromSeconds(25), AskService.DefaultFirstResultDeadline);
    }

    [Fact]
    public async Task Mcp_admission_bounds_execution_queue_hybrid_and_rolling_rate()
    {
        var admission = new McpAdmissionController(
            TimeProvider.System,
            executing: 1,
            queued: 1,
            queueDeadline: TimeSpan.FromSeconds(1),
            perClientPerMinute: 2,
            globalPerMinute: 4,
            hybridExecuting: 1);

        var first = await admission.EnterAsync("client-a", hybrid: false, CancellationToken.None);
        var queued = admission.EnterAsync("client-b", hybrid: false, CancellationToken.None).AsTask();
        var overflow = await admission.EnterAsync("client-c", hybrid: false, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.Equal(McpAdmissionFailure.Busy, overflow.Failure);
        first.Lease!.Dispose();
        var second = await queued;
        Assert.True(second.Accepted);
        second.Lease!.Dispose();

        var rateAdmission = new McpAdmissionController(
            TimeProvider.System, executing: 2, queued: 2, queueDeadline: TimeSpan.FromSeconds(1),
            perClientPerMinute: 2, globalPerMinute: 10, hybridExecuting: 1);
        var rateOne = await rateAdmission.EnterAsync("client-rate", hybrid: true, CancellationToken.None);
        rateOne.Lease!.Dispose();
        var rateTwo = await rateAdmission.EnterAsync("client-rate", hybrid: true, CancellationToken.None);
        rateTwo.Lease!.Dispose();
        var rateLimited = await rateAdmission.EnterAsync("client-rate", hybrid: true, CancellationToken.None);
        Assert.Equal(McpAdmissionFailure.RateLimited, rateLimited.Failure);
    }

    [Fact]
    public async Task Mcp_burst_keeps_exactly_one_execution_and_a_bounded_queue_then_recovers()
    {
        var admission = new McpAdmissionController(
            TimeProvider.System, executing: 1, queued: 4,
            queueDeadline: TimeSpan.FromSeconds(30),
            perClientPerMinute: 200, globalPerMinute: 200, hybridExecuting: 1);
        var running = await admission.EnterAsync("running", hybrid: false, CancellationToken.None);
        Assert.True(running.Accepted);

        var burst = Enumerable.Range(0, 40)
            .Select(index => admission.EnterAsync(
                    $"client-{index}", hybrid: index % 2 == 0, CancellationToken.None).AsTask()
                .ContinueWith(task =>
                {
                    var result = task.GetAwaiter().GetResult();
                    result.Lease?.Dispose();
                    return result;
                }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default))
            .ToList();
        await Task.Delay(50);

        Assert.Equal(36, burst.Count(task => task.IsCompletedSuccessfully
            && task.Result.Failure == McpAdmissionFailure.Busy));
        Assert.Equal(4, burst.Count(task => !task.IsCompleted));

        running.Lease!.Dispose();
        var outcomes = await Task.WhenAll(burst);
        Assert.Equal(4, outcomes.Count(result => result.Accepted));
        Assert.Equal(36, outcomes.Count(result => result.Failure == McpAdmissionFailure.Busy));

        var recovered = await admission.EnterAsync(
            "recovered", hybrid: true, CancellationToken.None);
        Assert.True(recovered.Accepted);
        recovered.Lease!.Dispose();
    }

    [Fact]
    public async Task Cancelled_hybrid_wait_releases_general_execution_capacity()
    {
        var admission = new McpAdmissionController(
            TimeProvider.System, executing: 2, queued: 0,
            queueDeadline: TimeSpan.FromSeconds(5),
            perClientPerMinute: 10, globalPerMinute: 10, hybridExecuting: 1);
        var first = await admission.EnterAsync("first", hybrid: true, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = admission.EnterAsync("waiting", hybrid: true, cancellation.Token).AsTask();
        await Task.Delay(25);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var ordinary = await admission.EnterAsync(
            "ordinary", hybrid: false, CancellationToken.None);

        Assert.True(ordinary.Accepted);
        ordinary.Lease!.Dispose();
        first.Lease!.Dispose();
    }

    [Fact]
    public async Task Mcp_rate_windows_remain_bounded_under_rotating_rejected_clients()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(
            2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        var admission = new McpAdmissionController(
            clock, executing: 4, queued: 0, queueDeadline: TimeSpan.Zero,
            perClientPerMinute: 4, globalPerMinute: 4, hybridExecuting: 1);

        for (var index = 0; index < 4; index++)
            (await admission.EnterAsync(
                $"accepted-{index}", hybrid: false, CancellationToken.None)).Lease!.Dispose();
        for (var index = 0; index < 1_000; index++)
            Assert.Equal(McpAdmissionFailure.RateLimited,
                (await admission.EnterAsync(
                    $"rejected-{index}", hybrid: false, CancellationToken.None)).Failure);
        Assert.Equal(4, admission.TrackedClients);

        clock.Advance(TimeSpan.FromMinutes(2));
        for (var index = 0; index < 4; index++)
            (await admission.EnterAsync(
                $"second-{index}", hybrid: false, CancellationToken.None)).Lease!.Dispose();
        Assert.Equal(8, admission.TrackedClients);

        clock.Advance(TimeSpan.FromMinutes(2));
        (await admission.EnterAsync(
            "third", hybrid: false, CancellationToken.None)).Lease!.Dispose();
        Assert.Equal(1, admission.TrackedClients);
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    public async Task Malformed_mcp_shape_reaches_the_normal_bounded_protocol_error_not_a_500(
        string path)
    {
        await using var site = new StreamingSite();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":42}",
                Encoding.UTF8, "application/json"),
        };

        var response = await site.Client.SendAsync(request);

        Assert.NotEqual(500, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    public async Task Every_mcp_route_shape_enforces_body_and_rate_boundaries(string path)
    {
        var admission = new McpAdmissionController(
            TimeProvider.System, executing: 1, queued: 0, queueDeadline: TimeSpan.Zero,
            perClientPerMinute: 1, globalPerMinute: 1, hybridExecuting: 1);
        await using var site = new StreamingSite(admission);
        using var oversized = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(new byte[65_537]),
        };
        oversized.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var tooLarge = await site.Client.SendAsync(oversized);
        Assert.Equal(413, (int)tooLarge.StatusCode);
        Assert.Equal("request_too_large",
            JsonNode.Parse(await tooLarge.Content.ReadAsStringAsync())?
                ["error"]?["data"]?["status"]?.GetValue<string>());

        const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
        var first = await site.Client.PostAsync(
            path, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.NotEqual(429, (int)first.StatusCode);
        var limited = await site.Client.PostAsync(
            path, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(429, (int)limited.StatusCode);
        Assert.Equal("rate_limited",
            JsonNode.Parse(await limited.Content.ReadAsStringAsync())?
                ["error"]?["data"]?["status"]?.GetValue<string>());
    }

    private static HttpRequestMessage Request(string body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask/stream")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Lex-Stream-Version", "1");
        return request;
    }

    private static (string Event, JsonObject Data)[] Frames(string wire) => wire
        .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        .Select(frame =>
        {
            var lines = frame.Split('\n');
            return (
                lines.Single(line => line.StartsWith("event: ", StringComparison.Ordinal))[7..],
                JsonNode.Parse(lines.Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..])!.AsObject());
        }).ToArray();

    private static string Hash(string value) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class StreamingSite : WebApplicationFactory<Program>
    {
        private readonly string _indexDir = Path.Combine(
            Path.GetTempPath(), $"lex-stream-{Guid.NewGuid():N}");

        public StreamingSite(McpAdmissionController? mcpAdmission = null)
        {
            Directory.CreateDirectory(_indexDir);
            McpAdmission = mcpAdmission;
            Planner = new BoundaryPlanner();
            Client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        }

        public BoundaryPlanner Planner { get; }
        public HttpClient Client { get; }
        public McpAdmissionController? McpAdmission { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _indexDir);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://stream.test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AskService>();
                services.AddSingleton(_ => new AskService(
                    new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>()), Planner));
                if (McpAdmission is not null)
                {
                    services.RemoveAll<McpAdmissionController>();
                    services.AddSingleton(McpAdmission);
                }
            });
        }

        public override async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await base.DisposeAsync();
            try { Directory.Delete(_indexDir, recursive: true); } catch { }
        }
    }

    public sealed class BoundaryPlanner : IOperationPlanner
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

    private sealed class TwoBoundaryPlanner : IOperationPlanner
    {
        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken) => Task.FromResult(
                OperationPlan.FromPlannerOutput(requestId, "en", new JsonArray(
                    new JsonObject
                    {
                        ["tool"] = "legal_boundary",
                        ["arguments"] = new JsonObject { ["reason"] = "legal advice" },
                    },
                    new JsonObject
                    {
                        ["tool"] = "legal_boundary",
                        ["arguments"] = new JsonObject { ["reason"] = "interpretation" },
                    }), synthesisRequested: false));
    }

    private sealed class WaitingPlanner : IOperationPlanner
    {
        public int Calls;

        public async Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }
}
