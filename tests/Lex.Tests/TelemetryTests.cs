using System.Diagnostics;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Mcp;
using Lex.Web;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Lex.Tests;

public sealed class TelemetryTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Privacy_processor_exports_only_valid_bounded_dimensions()
    {
        using var activity = Started("lex.request", LexRequestTelemetry.ActivitySourceName);
        activity.SetTag("lex.surface", "search");
        activity.SetTag("lex.response_class", "2xx");
        activity.SetTag("http.response.status_code", 200);
        activity.SetTag("lex.tool", "search");
        activity.SetTag("lex.status", McpStatus.Ok);
        activity.SetTag("lex.hit_count_bucket", "2-5");
        activity.SetTag("lex.zero_hit", false);
        activity.SetTag("lex.language", "en");
        activity.SetTag("lex.plan_shape", "mixed_synthesis");
        activity.SetTag("lex.digest", Digest);
        activity.SetTag("lex.retrieval_mode", "keyword");

        new PrivacyActivityProcessor().OnEnd(activity);

        Assert.Equal(new[]
        {
            "http.response.status_code=200",
            $"lex.digest={Digest}",
            "lex.hit_count_bucket=2-5",
            "lex.language=en",
            "lex.plan_shape=mixed_synthesis",
            "lex.response_class=2xx",
            "lex.retrieval_mode=keyword",
            "lex.status=ok",
            "lex.surface=search",
            "lex.tool=search",
            "lex.zero_hit=False",
        }, Tags(activity));
    }

    [Fact]
    public void Privacy_processor_fails_closed_for_hostile_tags_values_and_propagation()
    {
        const string canary = "PRIVATE_CANARY_4A72";
        using var activity = Started(canary, LexRequestTelemetry.ActivitySourceName);
        activity.SetTag("url.full", $"https://law.soufien.lu/search?q={canary}");
        activity.SetTag("http.url", $"https://law.soufien.lu/search?q={canary}");
        activity.SetTag("url.path", $"/search/{canary}");
        activity.SetTag("user_agent.original", canary);
        activity.SetTag("http.request.header.referer", canary);
        activity.SetTag("http.request.header.authorization", canary);
        activity.SetTag("http.request.header.cookie", canary);
        activity.SetTag("http.request.body", canary);
        activity.SetTag("client.address", "203.0.113.42");
        activity.SetTag("db.statement", canary);
        activity.SetTag("exception.message", canary);
        activity.SetTag("lex.query_expansions", canary);
        activity.SetTag("lex.retrieval_account", canary);
        activity.SetTag("lex.digest", canary);
        activity.SetTag("lex.surface", canary);
        activity.SetTag("lex.zero_hit", "true");
        activity.SetTag("http.response.status_code", "200");
        activity.AddBaggage("user.query", canary);
        activity.TraceStateString = canary;
        activity.SetStatus(ActivityStatusCode.Error, canary);

        new PrivacyActivityProcessor().OnEnd(activity);

        Assert.Equal("lex.unknown", activity.DisplayName);
        Assert.Empty(activity.TagObjects);
        Assert.Empty(activity.Baggage);
        Assert.Null(activity.TraceStateString);
        Assert.Null(activity.StatusDescription);
        Assert.DoesNotContain(canary, activity.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Privacy_processor_sanitizes_before_the_downstream_export_processor()
    {
        const string canary = "ORDER_CANARY_52BF";
        var downstream = new SnapshotProcessor();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("Lex.Tests.PrivacyOrder")
            .AddProcessor(new PrivacyActivityProcessor())
            .AddProcessor(downstream)
            .Build();
        using var source = new ActivitySource("Lex.Tests.PrivacyOrder");

        using (var activity = source.StartActivity(canary))
        {
            Assert.NotNull(activity);
            activity.SetTag("url.full", $"https://example.invalid/?q={canary}");
            activity.AddBaggage("query", canary);
            activity.SetStatus(ActivityStatusCode.Error, canary);
        }

        var snapshot = Assert.Single(downstream.Snapshots);
        Assert.Equal("lex.unknown", snapshot.Name);
        Assert.Empty(snapshot.Tags);
        Assert.Empty(snapshot.Baggage);
        Assert.Null(snapshot.TraceState);
        Assert.Null(snapshot.StatusDescription);
        Assert.DoesNotContain(canary, snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Always_on_sampler_exports_a_sanitized_span_from_an_unsampled_remote_parent()
    {
        const string sourceName = "Lex.Tests.RemoteUnsampled";
        const string canary = "REMOTE_PARENT_CANARY_117A";
        var exporter = new SnapshotExporter();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetSampler(LexTraceConfiguration.TraceSampler)
            .AddSource(sourceName)
            .AddProcessor(new PrivacyActivityProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        using var source = new ActivitySource(sourceName);
        var parent = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.None, isRemote: true);

        using (var activity = source.StartActivity(
                   "lex.request", ActivityKind.Server, parent))
        {
            Assert.NotNull(activity);
            Assert.True(activity.Recorded);
            activity.SetTag("lex.surface", "search");
            activity.AddBaggage("query", canary);
        }

        var snapshot = Assert.Single(exporter.Snapshots);
        Assert.Equal("lex.request", snapshot.Name);
        Assert.Equal(["lex.surface=search"], snapshot.Tags);
        Assert.Empty(snapshot.Baggage);
        Assert.DoesNotContain(canary, snapshot.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Export_edge_drops_spans_with_immutable_events_or_links(
        bool includeEvent, bool includeLink)
    {
        const string sourceName = "Lex.Tests.UnsafeStructure";
        var exporter = new SnapshotExporter();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetSampler(new AlwaysOnSampler())
            .AddSource(sourceName)
            .AddProcessor(new PrivacyActivityProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        using var source = new ActivitySource(sourceName);
        var linkedContext = new ActivityContext(
            ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
        ActivityLink[] links = includeLink ? [new ActivityLink(linkedContext)] : [];

        using (var activity = source.StartActivity(
                   "lex.request", ActivityKind.Internal, default(ActivityContext),
                   tags: null, links: links))
        {
            Assert.NotNull(activity);
            activity.SetTag("lex.surface", "search");
            if (includeEvent) activity.AddEvent(new ActivityEvent("unsafe"));
        }

        Assert.Empty(exporter.Snapshots);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(2, "2-5")]
    [InlineData(5, "2-5")]
    [InlineData(6, "6-10")]
    [InlineData(10, "6-10")]
    [InlineData(11, "11-25")]
    [InlineData(25, "11-25")]
    [InlineData(26, "26-50")]
    [InlineData(50, "26-50")]
    [InlineData(51, "51+")]
    public void Hit_count_buckets_have_closed_boundaries(int returned, string expected) =>
        Assert.Equal(expected, McpTelemetry.HitCountBucket(returned));

    [Fact]
    public void Tool_result_telemetry_uses_only_explicit_derived_facts()
    {
        const string canary = "RESULT_CANARY_C019";
        using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetStartTags(activity, "search", Digest);
        McpTelemetry.SetResultTags(activity, new JsonArray
        {
            new JsonObject
            {
                ["envelope"] = new JsonObject { ["status"] = McpStatus.Ok },
                ["response_row_set"] = new JsonObject { ["returned"] = 2 },
                ["retrieval_mode"] = "keyword",
                ["hits"] = new JsonArray(new JsonObject
                {
                    ["snippet"] = canary,
                    ["permalink"] = $"https://law.soufien.lu/{canary}",
                }),
                ["query_expansions"] = new JsonArray(canary),
                ["retrieval_account"] = canary,
            },
        });

        var tags = Tags(activity);
        Assert.Equal(new[]
        {
            $"lex.digest={Digest}",
            "lex.hit_count_bucket=2-5",
            "lex.retrieval_mode=keyword",
            "lex.status=ok",
            "lex.tool=search",
            "lex.zero_hit=False",
        }, tags);
        Assert.DoesNotContain(canary, string.Join('\n', tags), StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_tool_result_span_is_error_without_a_description()
    {
        using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetResultTags(activity, new JsonObject { ["status"] = "not-in-contract" });

        Assert.Contains("lex.status=failed", Tags(activity));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
    }

    [Fact]
    public void Zero_hit_is_emitted_only_for_an_explicit_zero_row_set()
    {
        using var explicitZero = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetResultTags(explicitZero, new JsonObject
        {
            ["status"] = McpStatus.NoResult,
            ["response_row_set"] = new JsonObject { ["returned"] = 0 },
        });
        Assert.Contains("lex.hit_count_bucket=0", Tags(explicitZero));
        Assert.Contains("lex.zero_hit=True", Tags(explicitZero));

        using var absent = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetResultTags(absent,
            new JsonObject { ["status"] = McpStatus.NoResult, ["hits"] = new JsonArray() });
        Assert.DoesNotContain(Tags(absent), item => item.StartsWith("lex.hit_count_bucket="));
        Assert.DoesNotContain(Tags(absent), item => item.StartsWith("lex.zero_hit="));
    }

    [Fact]
    public void Multi_publisher_local_row_sets_are_summed_not_maximized()
    {
        using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetResultTags(activity, new JsonArray
        {
            RowSet(2),
            RowSet(5),
        });

        Assert.Contains("lex.hit_count_bucket=6-10", Tags(activity));
        Assert.Contains("lex.zero_hit=False", Tags(activity));
    }

    [Fact]
    public void Repeated_global_stamped_local_row_sets_are_counted_once()
    {
        using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetResultTags(activity, new JsonArray
        {
            RowSet(5),
            RowSet(5),
        });

        Assert.Contains("lex.hit_count_bucket=2-5", Tags(activity));
    }

    [Fact]
    public void Explicit_consistent_global_row_set_is_authoritative()
    {
        using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetResultTags(activity, new JsonArray
        {
            RowSet(4, 12),
            RowSet(8, 12),
        });

        Assert.Contains("lex.hit_count_bucket=11-25", Tags(activity));
    }

    [Fact]
    public void Explicit_global_row_set_must_equal_the_checked_local_sum()
    {
        using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
        McpTelemetry.SetResultTags(activity, new JsonArray
        {
            RowSet(4, 12),
            RowSet(7, 12),
        });

        AssertNoHitCount(activity);
    }

    [Fact]
    public void Mixed_global_and_local_row_sets_fail_closed()
    {
        JsonNode[] results =
        [
            new JsonArray(RowSet(1, 3), RowSet(2)),
            new JsonArray(RowSet(4, 12), RowSet(8, 13)),
        ];
        foreach (var result in results)
        {
            using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
            McpTelemetry.SetResultTags(activity, result);
            AssertNoHitCount(activity);
        }
    }

    [Fact]
    public void Empty_negative_noninteger_and_overflowing_row_sets_fail_closed()
    {
        JsonNode[] results =
        [
            new JsonArray(),
            new JsonArray(RowSet(2), RowSet(-1)),
            new JsonArray(RowSet(2), new JsonObject
            {
                ["response_row_set"] = new JsonObject { ["returned"] = "3" },
            }),
            new JsonArray(RowSet(2, -1), RowSet(3, -1)),
            new JsonArray(RowSet(2, 5), new JsonObject
            {
                ["response_row_set"] = new JsonObject { ["returned"] = 3 },
                ["global_response_row_set"] = new JsonObject { ["returned"] = "5" },
            }),
            new JsonArray(RowSet(int.MaxValue), RowSet(1)),
        ];
        foreach (var result in results)
        {
            using var activity = Started("lex.tool", McpTelemetry.ActivitySourceName);
            McpTelemetry.SetResultTags(activity, result);
            AssertNoHitCount(activity);
        }
    }

    [Fact]
    public async Task Mcp_choke_point_emits_one_safe_span_for_valid_and_invalid_calls()
    {
        const string canary = "ARGUMENT_CANARY_F491";
        using var root = new Activity("telemetry-test-root").Start();
        var stopped = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = Listener(McpTelemetry.ActivitySourceName, activity =>
        {
            if (activity.TraceId == root.TraceId) stopped.Enqueue(activity);
        });
        var core = new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>(), Digest);

        await core.CallToolAsync("search",
            new JsonObject { ["query"] = canary }, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => core.CallToolAsync(
            canary, new JsonObject(), CancellationToken.None).AsTask());

        var activities = stopped.ToArray();
        Assert.Equal(2, activities.Length);
        Assert.All(activities, activity =>
        {
            Assert.Equal("lex.tool", activity.DisplayName);
            Assert.Equal(ActivityKind.Internal, activity.Kind);
            Assert.DoesNotContain(canary, string.Join('\n', Tags(activity)),
                StringComparison.Ordinal);
        });
        Assert.Equal(new[]
        {
            $"lex.digest={Digest}",
            "lex.status=no_corpus_mounted",
            "lex.tool=search",
        }, Tags(activities[0]));
        Assert.Equal(new[]
        {
            $"lex.digest={Digest}",
            "lex.status=invalid_request",
            "lex.tool=unknown",
        }, Tags(activities[1]));
        Assert.Equal(ActivityStatusCode.Unset, activities[0].Status);
        Assert.Equal(ActivityStatusCode.Error, activities[1].Status);
        Assert.Null(activities[0].StatusDescription);
        Assert.Null(activities[1].StatusDescription);
    }

    [Fact]
    public async Task Mcp_language_dimension_uses_only_the_validated_contract_and_closed_buckets()
    {
        using var root = new Activity("telemetry-test-root").Start();
        var stopped = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = Listener(McpTelemetry.ActivitySourceName, activity =>
        {
            if (activity.TraceId == root.TraceId) stopped.Enqueue(activity);
        });
        var core = new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>(), Digest);

        await core.CallToolAsync("search", new JsonObject
        {
            ["query"] = "synthetic request",
            ["language"] = "en",
        }, CancellationToken.None);
        await core.CallToolAsync("search", new JsonObject
        {
            ["query"] = "synthetic request",
            ["language"] = "zz",
        }, CancellationToken.None);
        await core.CallToolAsync("search", new JsonObject
        {
            ["query"] = "fr",
        }, CancellationToken.None);

        var activities = stopped.ToArray();
        Assert.Equal(3, activities.Length);
        Assert.Contains("lex.language=en", Tags(activities[0]));
        Assert.Contains("lex.language=other", Tags(activities[1]));
        Assert.DoesNotContain(Tags(activities[2]), tag =>
            tag.StartsWith("lex.language=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Planner_failure_span_is_error_without_a_description()
    {
        using var root = new Activity("telemetry-test-root").Start();
        var stopped = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = Listener(AskTelemetry.ActivitySourceName, activity =>
        {
            if (activity.TraceId == root.TraceId) stopped.Enqueue(activity);
        });
        var core = new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>(), Digest);
        var service = new AskService(core, new FailingPlanner());

        var outcome = await service.AskAsync(new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["content"] = "synthetic planning failure",
        }), "198.51.100.10", "example.invalid", CancellationToken.None);

        Assert.Equal(502, outcome.Status);
        var activity = Assert.Single(stopped.ToArray());
        Assert.Equal("lex.plan", activity.DisplayName);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
    }

    public static TheoryData<OperationPlan, string> PlanShapes => new()
    {
        { Plan(Application(ApplicationDisposition.Clarification, 0)), "clarification" },
        { Plan(Application(ApplicationDisposition.Gap, 0)), "gap" },
        { Plan(Application(ApplicationDisposition.LegalBoundary, 0)), "legal_boundary" },
        { Plan(Application(ApplicationDisposition.Gap, 0),
               Application(ApplicationDisposition.LegalBoundary, 1)), "application_mixed" },
        { Plan(Legal(0)), "single_legal" },
        { Plan(true, Legal(0)), "single_legal_synthesis" },
        { Plan(Legal(0), Legal(1)), "multi_legal" },
        { Plan(true, Legal(0), Legal(1)), "multi_legal_synthesis" },
        { Plan(Legal(0), Application(ApplicationDisposition.Gap, 1)), "mixed" },
        { Plan(true, Legal(0), Application(ApplicationDisposition.LegalBoundary, 1)),
            "mixed_synthesis" },
    };

    [Theory]
    [MemberData(nameof(PlanShapes))]
    public void Plan_shape_comes_only_from_the_typed_plan(OperationPlan plan, string expected)
    {
        Assert.Equal(expected, AskTelemetry.PlanShape(plan));
        Assert.DoesNotContain("request-canary", AskTelemetry.PlanShape(plan),
            StringComparison.Ordinal);
        Assert.DoesNotContain("operation-canary", AskTelemetry.PlanShape(plan),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/search", "search")]
    [InlineData("/mcp", "mcp")]
    [InlineData("/api/ask", "ask")]
    [InlineData("/api/ask/stream", "ask_stream")]
    public async Task Request_span_has_static_server_semantics_and_traceparent_only(
        string path, string surface)
    {
        const string traceId = "0123456789abcdef0123456789abcdef";
        const string canary = "REQUEST_CANARY_982A";
        Activity? observed = null;
        using var listener = Listener(LexRequestTelemetry.ActivitySourceName, _ => { });
        using var ambient = new Activity("ambient");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.TraceStateString = canary;
        ambient.AddBaggage("ambient-query", canary);
        ambient.Start();
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString($"?q={canary}");
        context.Request.Headers.TraceParent = $"00-{traceId}-0123456789abcdef-01";
        context.Request.Headers.TraceState = canary;
        context.Request.Headers.Baggage = $"query={canary}";
        context.Request.Headers.UserAgent = canary;
        context.Request.Headers.Referer = $"https://example.invalid/{canary}";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.42");

        await LexRequestTelemetry.ObserveAsync(context, Digest, nextContext =>
        {
            observed = Activity.Current;
            nextContext.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        using (var foreignSource = new ActivitySource(LexRequestTelemetry.ActivitySourceName))
        using (var foreign = foreignSource.StartActivity("foreign-request"))
            Assert.NotNull(foreign);

        Assert.NotNull(observed);
        Assert.Equal("lex.request", observed!.DisplayName);
        Assert.Equal(ActivityKind.Server, observed.Kind);
        Assert.Equal(traceId, observed.TraceId.ToHexString());
        Assert.Null(observed.TraceStateString);
        Assert.Empty(observed.Baggage);
        Assert.Same(ambient, Activity.Current);
        Assert.Equal(new[]
        {
            "http.response.status_code=204",
            $"lex.digest={Digest}",
            "lex.response_class=2xx",
            $"lex.surface={surface}",
        }, Tags(observed));
        Assert.DoesNotContain(canary, observed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_span_ignores_unserved_paths_and_invalid_parent_context()
    {
        using var ambient = new Activity("unserved-ambient");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.Start();
        Activity? observed = null;
        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Request.Headers.TraceParent = "hostile";

        await LexRequestTelemetry.ObserveAsync(context, Digest, _ =>
        {
            observed = Activity.Current;
            return Task.CompletedTask;
        });

        Assert.Same(ambient, observed);
        Assert.Same(ambient, Activity.Current);
    }

    [Fact]
    public async Task Request_span_rejects_invalid_parent_and_ambient_propagation()
    {
        const string canary = "AMBIENT_CANARY_93D1";
        Activity? observed = null;
        using var listener = Listener(LexRequestTelemetry.ActivitySourceName, _ => { });
        using var ambient = new Activity("ambient");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.TraceStateString = canary;
        ambient.AddBaggage("query", canary);
        ambient.Start();
        var context = new DefaultHttpContext();
        context.Request.Path = "/search";
        context.Request.Headers.TraceParent = "hostile";
        context.Request.Headers.TraceState = canary;
        context.Request.Headers.Baggage = $"query={canary}";

        await LexRequestTelemetry.ObserveAsync(context, Digest, _ =>
        {
            observed = Activity.Current;
            return Task.CompletedTask;
        });

        using (var foreignSource = new ActivitySource(LexRequestTelemetry.ActivitySourceName))
        using (var foreign = foreignSource.StartActivity("foreign-request"))
            Assert.NotNull(foreign);

        Assert.NotNull(observed);
        Assert.NotEqual(ambient.TraceId, observed!.TraceId);
        Assert.Null(observed.ParentId);
        Assert.Null(observed.TraceStateString);
        Assert.Empty(observed.Baggage);
        Assert.Same(ambient, Activity.Current);
        Assert.DoesNotContain(canary, observed.ToString(), StringComparison.Ordinal);
    }

    private static Activity Started(string name, string sourceName)
    {
        var activity = new Activity(name);
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        activity.DisplayName = name;
        activity.SetTag("test.source", sourceName);
        activity.SetTag("test.source", null);
        return activity;
    }

    private static ActivityListener Listener(string source, Action<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string[] Tags(Activity activity) => activity.TagObjects
        .OrderBy(item => item.Key, StringComparer.Ordinal)
        .Select(item => $"{item.Key}={item.Value}")
        .ToArray();

    private sealed class SnapshotProcessor : BaseProcessor<Activity>
    {
        public List<ActivitySnapshot> Snapshots { get; } = [];

        public override void OnEnd(Activity activity) => Snapshots.Add(new ActivitySnapshot(
            activity.DisplayName,
            Tags(activity),
            activity.Baggage.ToArray(),
            activity.TraceStateString,
            activity.StatusDescription));
    }

    private sealed class SnapshotExporter : BaseExporter<Activity>
    {
        public List<ActivitySnapshot> Snapshots { get; } = [];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Snapshots.Add(new ActivitySnapshot(
                    activity.DisplayName,
                    Tags(activity),
                    activity.Baggage.ToArray(),
                    activity.TraceStateString,
                    activity.StatusDescription));
            return ExportResult.Success;
        }
    }

    private sealed record ActivitySnapshot(
        string Name,
        string[] Tags,
        KeyValuePair<string, string?>[] Baggage,
        string? TraceState,
        string? StatusDescription);

    private sealed class FailingPlanner : IOperationPlanner
    {
        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("synthetic planner failure");
    }

    private static JsonObject RowSet(int returned, int? global = null)
    {
        var row = new JsonObject
        {
            ["response_row_set"] = new JsonObject { ["returned"] = returned },
        };
        if (global is not null)
            row["global_response_row_set"] = new JsonObject { ["returned"] = global.Value };
        return row;
    }

    private static void AssertNoHitCount(Activity activity)
    {
        Assert.DoesNotContain(Tags(activity), item => item.StartsWith(
            "lex.hit_count_bucket=", StringComparison.Ordinal));
        Assert.DoesNotContain(Tags(activity), item => item.StartsWith(
            "lex.zero_hit=", StringComparison.Ordinal));
    }

    private static RequestedOperation Legal(int order) => RequestedOperation.CreatePlanned(
        $"operation-canary-{order}", order, "coverage", new JsonObject());

    private static RequestedOperation Application(ApplicationDisposition disposition, int order) =>
        RequestedOperation.CreateApplication(
            $"operation-canary-{order}", order, disposition,
            disposition == ApplicationDisposition.Clarification
                ? new JsonObject
                {
                    ["question"] = "Choose one",
                    ["options"] = new JsonArray("One", "Two"),
                }
                : new JsonObject());

    private static OperationPlan Plan(params RequestedOperation[] operations) =>
        OperationPlan.Create("request-canary", "en", operations);

    private static OperationPlan Plan(bool synthesis, params RequestedOperation[] operations) =>
        OperationPlan.Create("request-canary", "en", operations, synthesis);
}
