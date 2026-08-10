using System.Text;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Mcp;
using Lex.Web;
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
        var duplicate = registry.Claim("client", "request", Hash("same"));
        var conflict = registry.Claim("client", "request", Hash("different"));

        Assert.Equal(AskRequestClaimKind.Owner, owner.Kind);
        Assert.Equal(AskRequestClaimKind.Duplicate, duplicate.Kind);
        Assert.Equal(AskRequestClaimKind.Conflict, conflict.Kind);

        owner.Complete(200, "{\"reply\":\"one execution\"}");
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
        var frames = Frames(firstWire);
        Assert.Equal(["operation_result", "done"], frames.Select(frame => frame.Event));
        Assert.All(frames, frame =>
        {
            Assert.Equal("1", frame.Data["version"]?.GetValue<string>());
            Assert.Equal("stream-request", frame.Data["request_id"]?.GetValue<string>());
        });
        Assert.Equal([1, 2], frames.Select(frame => frame.Data["sequence"]?.GetValue<int>()));
        Assert.Equal(["done"], Frames(duplicateWire).Select(frame => frame.Event));
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

        Assert.Equal(200, status);
        var operations = Assert.IsType<JsonArray>(body["operations"]);
        Assert.Equal(2, operations.Count);
        Assert.Equal("legal_boundary", operations[0]?["legal_outcome"]?.GetValue<string>());
        Assert.Equal("completed", operations[0]?["transport_outcome"]?.GetValue<string>());
        Assert.Equal("not_evaluated", operations[1]?["legal_outcome"]?.GetValue<string>());
        Assert.Equal("cancelled", operations[1]?["transport_outcome"]?.GetValue<string>());
        Assert.NotNull(operations[0]?["ui"]?["gap"]);
        Assert.NotNull(operations[1]?["ui"]?["gap"]);
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
                $"client-{index}", hybrid: index % 2 == 0, CancellationToken.None).AsTask())
            .ToList();
        await Task.Delay(50);

        Assert.Equal(36, burst.Count(task => task.IsCompletedSuccessfully
            && task.Result.Failure == McpAdmissionFailure.Busy));
        Assert.Equal(4, burst.Count(task => !task.IsCompleted));

        running.Lease!.Dispose();
        while (burst.Any(task => !task.IsCompleted))
        {
            var completed = await Task.WhenAny(burst.Where(task => !task.IsCompleted));
            var admitted = await completed;
            Assert.True(admitted.Accepted, admitted.Failure.ToString());
            admitted.Lease!.Dispose();
        }

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

    [Fact]
    public async Task Malformed_mcp_shape_reaches_the_normal_bounded_protocol_error_not_a_500()
    {
        await using var site = new StreamingSite();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":42}",
                Encoding.UTF8, "application/json"),
        };

        var response = await site.Client.SendAsync(request);

        Assert.NotEqual(500, (int)response.StatusCode);
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

        public StreamingSite()
        {
            Directory.CreateDirectory(_indexDir);
            Environment.SetEnvironmentVariable("LEX_INDEX_DIR", _indexDir);
            Environment.SetEnvironmentVariable("LEX_PUBLIC_BASE_URL", "https://stream.test");
            Planner = new BoundaryPlanner();
            Client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        }

        public BoundaryPlanner Planner { get; }
        public HttpClient Client { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AskService>();
                services.AddSingleton(_ => new AskService(
                    new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>()), Planner));
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

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }
}
