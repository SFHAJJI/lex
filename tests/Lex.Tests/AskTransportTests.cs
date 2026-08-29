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
    public async Task Server_threads_retain_only_bounded_recent_turns_and_bytes()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero));
        var registry = new AskThreadRegistry(
            clock, maximumThreads: 4, maximumRetainedTurns: 2,
            maximumThreadBytes: 12_000, maximumRetainedBytes: 24_000);

        string token;
        await using (var created = AssertAcquired(await registry.AcquireAsync(null)))
        {
            token = created.Token;
            Assert.Empty(created.History);
            created.Commit("question one", "answer one", context: null);
        }
        await using (var second = AssertAcquired(await registry.AcquireAsync(token)))
            second.Commit("question two", "answer two", context: null);
        await using (var third = AssertAcquired(await registry.AcquireAsync(token)))
            third.Commit("question three", "answer three", context: null);

        await using var restored = AssertAcquired(await registry.AcquireAsync(token));
        Assert.Equal([
            "question two", "answer two", "question three", "answer three",
        ], restored.History.OfType<JsonObject>()
            .Select(message => message["content"]!.GetValue<string>()));
        Assert.True(registry.RetainedBytes <= 24_000);
        Assert.True(registry.RetainedBytesFor(token) <= 12_000);
    }

    [Fact]
    public async Task Global_thread_byte_pressure_evicts_only_an_inactive_thread()
    {
        var registry = new AskThreadRegistry(
            TimeProvider.System, maximumThreads: 4, maximumRetainedTurns: 1,
            maximumThreadBytes: 64, maximumRetainedBytes: 80);
        string firstToken;
        await using (var first = AssertAcquired(await registry.AcquireAsync(null)))
        {
            firstToken = first.Token;
            Assert.True(first.Commit("first", "first answer", context: null));
        }

        string secondToken;
        await using (var second = AssertAcquired(await registry.AcquireAsync(null)))
        {
            secondToken = second.Token;
            Assert.True(second.Commit("second", "second answer", context: null));
        }

        Assert.True(registry.RetainedBytes <= 80);
        Assert.Equal(AskThreadAcquireKind.NotFound,
            (await registry.AcquireAsync(firstToken)).Kind);
        await using var retained = AssertAcquired(await registry.AcquireAsync(secondToken));
        Assert.Contains(retained.History.OfType<JsonObject>(), message =>
            message["content"]?.GetValue<string>() == "second answer");
    }

    [Fact]
    public async Task Server_threads_serialize_one_owner_and_bound_waiters()
    {
        var registry = new AskThreadRegistry(
            TimeProvider.System, maximumThreads: 4, maximumWaitersPerThread: 1);
        var first = AssertAcquired(await registry.AcquireAsync(null));
        var waiting = registry.AcquireAsync(first.Token).AsTask();

        Assert.False(waiting.IsCompleted);
        Assert.Equal(AskThreadAcquireKind.Busy,
            (await registry.AcquireAsync(first.Token)).Kind);

        await first.DisposeAsync();
        await using var second = AssertAcquired(await waiting);
        Assert.Equal(first.Token, second.Token);
    }

    [Fact]
    public async Task Forged_expired_and_reset_thread_tokens_are_isolated()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero));
        var registry = new AskThreadRegistry(
            clock, maximumThreads: 4, idleTtl: TimeSpan.FromMinutes(5));
        string token;
        await using (var created = AssertAcquired(await registry.AcquireAsync(null)))
        {
            token = created.Token;
            created.Commit("private question", "private answer", context: null);
        }

        var forged = token[..^1] + (token[^1] == 'A' ? "B" : "A");
        Assert.Equal(AskThreadAcquireKind.NotFound,
            (await registry.AcquireAsync(forged)).Kind);
        await using (var intact = AssertAcquired(await registry.AcquireAsync(token)))
            Assert.Contains(intact.History.OfType<JsonObject>(), message =>
                message["content"]?.GetValue<string>() == "private question");

        Assert.True(registry.Reset(token));
        Assert.Equal(AskThreadAcquireKind.NotFound,
            (await registry.AcquireAsync(token)).Kind);

        await using (var replacement = AssertAcquired(await registry.AcquireAsync(null)))
        {
            token = replacement.Token;
            replacement.Commit("new question", "new answer", context: null);
        }
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(AskThreadAcquireKind.NotFound,
            (await registry.AcquireAsync(token)).Kind);
    }

    [Fact]
    public async Task Thread_context_disposition_distinguishes_preserve_replace_and_clear()
    {
        var registry = new AskThreadRegistry(TimeProvider.System, maximumThreads: 2);
        var source = new AskSubjectAuthoritySource(
            "publisher_short_title",
            "http://publications.europa.eu/ontology/cdm#expression_title_short",
            "CRR", "en",
            "https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32013R0575");
        var authority = new AskConversationContext(
            [new AskResolvedSubjectContext(
                "eu-eurlex:32013r0575", "art_92", AuthoritySource: source)], "92");
        string token;
        await using (var created = AssertAcquired(await registry.AcquireAsync(null)))
        {
            token = created.Token;
            Assert.True(created.Commit(
                "CRR question", "CRR answer", authority,
                AskConversationContextDisposition.Replace));
        }
        await using (var preserved = AssertAcquired(await registry.AcquireAsync(token)))
        {
            Assert.Equal("92", preserved.Context?.ArticleNumber);
            Assert.Equal(source, Assert.Single(preserved.Context!.Subjects).AuthoritySource);
            Assert.True(preserved.Commit(
                "follow up", "follow-up answer", context: null,
                AskConversationContextDisposition.Preserve));
        }
        await using (var cleared = AssertAcquired(await registry.AcquireAsync(token)))
        {
            Assert.Equal("92", cleared.Context?.ArticleNumber);
            Assert.True(cleared.Commit(
                "aggregate", "aggregate answer", context: null,
                AskConversationContextDisposition.Clear));
        }
        await using var restored = AssertAcquired(await registry.AcquireAsync(token));
        Assert.Null(restored.Context);
    }

    private static AskThreadLease AssertAcquired(AskThreadAcquire result)
    {
        Assert.Equal(AskThreadAcquireKind.Acquired, result.Kind);
        return Assert.IsType<AskThreadLease>(result.Lease);
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
    public void Evaluation_admission_skips_both_daily_counters_but_shares_concurrency()
    {
        var admission = new AskAdmissionController(
            TimeProvider.System, perClientDaily: 1, globalDaily: 1, concurrent: 1);

        using var publicLease = admission.TryAdmit("public-client").Lease;
        var busyEvaluation = admission.TryAdmit(
            "release-runner", AskAdmissionLane.Evaluation);

        Assert.Equal(AskAdmissionFailure.Busy, busyEvaluation.Failure);
        publicLease?.Dispose();

        using var evaluationLease = admission.TryAdmit(
            "release-runner", AskAdmissionLane.Evaluation).Lease;
        Assert.NotNull(evaluationLease);
        Assert.Equal(1, admission.AcceptedToday);
        evaluationLease?.Dispose();
        Assert.Equal(AskAdmissionFailure.PerClientQuota,
            admission.TryAdmit("public-client").Failure);
        Assert.Equal(AskAdmissionFailure.GlobalQuota,
            admission.TryAdmit("another-public-client").Failure);
    }

    [Fact]
    public void Public_default_admits_exactly_200_turns_per_client_per_utc_day()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 14, 23, 0, 0, TimeSpan.Zero));
        var admission = new AskAdmissionController(
            clock,
            AskService.DefaultPerIpDaily,
            AskService.DefaultGlobalDaily,
            AskService.DefaultConcurrent);

        Assert.Equal(200, AskService.DefaultPerIpDaily);
        Assert.Equal(400, AskService.DefaultGlobalDaily);
        for (var turn = 0; turn < 200; turn++)
        {
            using var lease = admission.TryAdmit("interview-client").Lease;
            Assert.NotNull(lease);
        }
        Assert.Equal(AskAdmissionFailure.PerClientQuota,
            admission.TryAdmit("interview-client").Failure);

        clock.Advance(TimeSpan.FromHours(1));
        using var nextUtcDay = admission.TryAdmit("interview-client").Lease;
        Assert.NotNull(nextUtcDay);
    }

    [Fact]
    public async Task Http_stream_is_versioned_and_a_duplicate_does_not_plan_twice()
    {
        await using var site = new StreamingSite();
        const string body = "{\"message\":\"Can Lex advise me?\"}";
        using var first = Request(body, "stream-request");
        using var duplicate = Request(body, "stream-request");

        var firstResponse = await site.Client.SendAsync(first);
        var firstWire = await firstResponse.Content.ReadAsStringAsync();
        var duplicateResponse = await site.Client.SendAsync(duplicate);
        var duplicateWire = await duplicateResponse.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)firstResponse.StatusCode);
        Assert.Equal(200, (int)duplicateResponse.StatusCode);
        Assert.True(firstResponse.Headers.CacheControl?.NoStore);
        Assert.True(firstResponse.Headers.CacheControl?.Private);
        Assert.Equal(1, site.Planner.Calls);
        var serverRequestId = firstResponse.Headers.GetValues("X-Lex-Request-Id").Single();
        Assert.Matches("^[a-f0-9]{32}$", serverRequestId);
        Assert.NotEqual("stream-request", serverRequestId);
        Assert.Equal(serverRequestId,
            duplicateResponse.Headers.GetValues("X-Lex-Request-Id").Single());
        var frames = Frames(firstWire);
        Assert.Equal([
            "phase", "phase", "phase", "phase", "phase", "operation_result", "phase", "done",
        ], frames.Select(frame => frame.Event));
        Assert.All(frames, frame =>
        {
            Assert.Equal("1", frame.Data["version"]?.GetValue<string>());
            Assert.Equal(serverRequestId, frame.Data["request_id"]?.GetValue<string>());
            Assert.True(frame.Data["server_elapsed_ms"]?.GetValue<double>() >= 0);
        });
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8],
            frames.Select(frame => frame.Data["sequence"]?.GetValue<int>()));
        Assert.Equal(["operation_result", "done"],
            Frames(duplicateWire).Select(frame => frame.Event));
        Assert.All(frames.Where(frame => frame.Event == "operation_result"), frame =>
            Assert.DoesNotContain("stream-request",
                frame.Data["payload"]?["operation_id"]?.GetValue<string>() ?? ""));
        var timing = frames.Single(frame => frame.Event == "done")
            .Data["payload"]?["timing"];
        Assert.True(timing?["planner_ms"]?.GetValue<double>() >= 0);
        Assert.True(timing?["mcp_ms"]?.GetValue<double>() >= 0);
        Assert.Null(timing?["synthesis_ms"]);
        Assert.True(timing?["operation_result_emitted_ms"]?.GetValue<double>() >= 0);
    }

    [Fact]
    public async Task Owner_streams_terminal_refusal_once_and_replays_the_same_operation()
    {
        var planner = new InvalidPlanner();
        await using var site = new StreamingSite(planner: planner);
        const string body = "{\"message\":\"Show coverage.\"}";
        using var owner = Request(body, "invalid-plan");
        using var duplicate = Request(body, "invalid-plan");

        var ownerResponse = await site.Client.SendAsync(owner);
        var ownerFrames = Frames(await ownerResponse.Content.ReadAsStringAsync());
        var duplicateResponse = await site.Client.SendAsync(duplicate);
        var duplicateFrames = Frames(await duplicateResponse.Content.ReadAsStringAsync());

        Assert.Equal(200, (int)ownerResponse.StatusCode);
        Assert.Equal(200, (int)duplicateResponse.StatusCode);
        Assert.Equal(1, planner.Calls);
        var ownerOperation = Assert.Single(
            ownerFrames, frame => frame.Event == "operation_result").Data["payload"];
        var ownerDone = ownerFrames.Single(frame => frame.Event == "done").Data["payload"];
        Assert.True(JsonNode.DeepEquals(ownerOperation, ownerDone?["operations"]?[0]));
        Assert.True(ownerDone?["timing"]?["operation_result_emitted_ms"]?.GetValue<double>() >= 0);
        var duplicateOperation = Assert.Single(
            duplicateFrames, frame => frame.Event == "operation_result").Data["payload"];
        Assert.True(JsonNode.DeepEquals(ownerOperation, duplicateOperation));
        var duplicateDone = duplicateFrames.Single(frame => frame.Event == "done").Data["payload"];
        Assert.Equal(
            ownerDone?["timing"]?["operation_result_emitted_ms"]?.GetValue<double>(),
            duplicateDone?["timing"]?["operation_result_emitted_ms"]?.GetValue<double>());
        Assert.Equal(["operation_result", "done"],
            duplicateFrames.Select(frame => frame.Event));
    }

    [Fact]
    public async Task Http_threads_are_server_owned_and_idempotent_before_turn_mutation()
    {
        await using var site = new StreamingSite();
        const string firstBody = "{\"message\":\"Can Lex advise me?\"}";
        using var firstRequest = Request(firstBody, "thread-first");
        var firstResponse = await site.Client.SendAsync(firstRequest);
        var firstWire = await firstResponse.Content.ReadAsStringAsync();
        var firstDone = Frames(firstWire).Single(frame => frame.Event == "done");
        var token = firstDone.Data["payload"]?["thread_token"]?.GetValue<string>();

        Assert.True(AskThreadRegistry.IsValidToken(token));
        Assert.Equal(1, site.Planner.Calls);
        Assert.Single(site.Planner.Histories);
        Assert.Equal(["Can Lex advise me?"], site.Planner.Histories[0]
            .OfType<JsonObject>().Select(message => message["content"]!.GetValue<string>()));

        const string secondBody = "{\"message\":\"What about that law?\"}";
        using var secondRequest = Request(secondBody, "thread-second", token);
        using var duplicate = Request(secondBody, "thread-second", token);
        var secondResponse = await site.Client.SendAsync(secondRequest);
        var secondWire = await secondResponse.Content.ReadAsStringAsync();
        var duplicateResponse = await site.Client.SendAsync(duplicate);
        var duplicateWire = await duplicateResponse.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)secondResponse.StatusCode);
        Assert.Equal(200, (int)duplicateResponse.StatusCode);
        Assert.Equal(2, site.Planner.Calls);
        Assert.Equal(3, site.Planner.Histories[1].Count);
        Assert.Equal("What about that law?",
            site.Planner.Histories[1][2]?["content"]?.GetValue<string>());
        Assert.Equal(token, Frames(secondWire).Single(frame => frame.Event == "done")
            .Data["payload"]?["thread_token"]?.GetValue<string>());
        Assert.Equal(["operation_result", "done"],
            Frames(duplicateWire).Select(frame => frame.Event));
        Assert.Equal(2, site.Services.GetRequiredService<AskThreadRegistry>()
            .RetainedTurnsFor(token!));
    }

    [Fact]
    public async Task Nonstream_thread_capability_is_private_and_not_cacheable()
    {
        await using var site = new StreamingSite();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask")
        {
            Content = new StringContent(
                "{\"message\":\"Can Lex advise me?\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", "nonstream-thread");

        var response = await site.Client.SendAsync(request);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(AskThreadRegistry.IsValidToken(body?["thread_token"]?.GetValue<string>()));
    }

    [Fact]
    public async Task Unknown_thread_token_isolated_before_planning()
    {
        await using var site = new StreamingSite();
        var unknown = Convert.ToBase64String(new byte[32])
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var request = Request(
            "{\"message\":\"Show coverage.\"}", "unknown-thread", unknown);

        var response = await site.Client.SendAsync(request);

        Assert.Equal(409, (int)response.StatusCode);
        Assert.Equal("thread_unavailable",
            JsonNode.Parse(await response.Content.ReadAsStringAsync())?
                ["status"]?.GetValue<string>());
        Assert.Equal(0, site.Planner.Calls);
    }

    [Fact]
    public async Task Preexecution_thread_failures_do_not_fill_the_idempotency_registry()
    {
        var requests = new AskRequestRegistry(TimeProvider.System, maximumEntries: 2);
        await using var site = new StreamingSite(askRequests: requests);
        var unknown = Convert.ToBase64String(new byte[32])
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        for (var i = 0; i < 5; i++)
        {
            using var rejected = Request(
                "{\"message\":\"Show coverage.\"}", $"unknown-thread-{i}", unknown);
            Assert.Equal(409, (int)(await site.Client.SendAsync(rejected)).StatusCode);
        }

        using var valid = Request(
            "{\"message\":\"Can Lex advise me?\"}", "valid-after-forged-flood");
        Assert.Equal(200, (int)(await site.Client.SendAsync(valid)).StatusCode);
        Assert.Equal(1, site.Planner.Calls);
    }

    [Fact]
    public async Task Cancelled_thread_waiter_completes_duplicate_without_replay_retention()
    {
        const string body = "{\"message\":\"Continue.\"}";
        const string key = "cancelled-thread-waiter";
        var requests = new AskRequestRegistry(TimeProvider.System, maximumEntries: 4);
        var threads = new AskThreadRegistry(
            TimeProvider.System, maximumThreads: 2, maximumWaitersPerThread: 1);
        var held = AssertAcquired(await threads.AcquireAsync(null));
        Assert.True(held.Commit("seed", "seed answer", context: null));
        await using var site = new StreamingSite(
            askThreads: threads, askRequests: requests);
        using var cancellation = new CancellationTokenSource();
        using var ownerRequest = Request(body, key, held.Token);
        var ownerTask = site.Client.SendAsync(
            ownerRequest, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        Assert.True(await EventuallyAsync(() => threads.WaitersFor(held.Token) == 1));
        var duplicate = requests.Claim(
            "another-client", key, AskFingerprint(body, held.Token));
        Assert.Equal(AskRequestClaimKind.Duplicate, duplicate.Kind);

        cancellation.Cancel();
        var completed = await duplicate.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(499, completed.Status);
        Assert.Contains("cancelled", completed.Body, StringComparison.OrdinalIgnoreCase);
        try { await ownerTask; }
        catch (OperationCanceledException) { }
        Assert.True(await EventuallyAsync(() => threads.WaitersFor(held.Token) == 0));
        await held.DisposeAsync();

        using var retry = Request(body, key, held.Token);
        Assert.Equal(200, (int)(await site.Client.SendAsync(retry)).StatusCode);
        Assert.Equal(1, site.Planner.Calls);
    }

    [Fact]
    public async Task Thread_reset_is_idempotent_and_removes_only_the_opaque_server_thread()
    {
        await using var site = new StreamingSite();
        using var first = Request("{\"message\":\"Can Lex advise me?\"}", "reset-first");
        var firstResponse = await site.Client.SendAsync(first);
        var token = Frames(await firstResponse.Content.ReadAsStringAsync())
            .Single(frame => frame.Event == "done")
            .Data["payload"]?["thread_token"]?.GetValue<string>();
        Assert.True(AskThreadRegistry.IsValidToken(token));

        using var reset = ResetRequest(token!, "reset-key");
        using var replay = ResetRequest(token!, "reset-key");
        var resetResponse = await site.Client.SendAsync(reset);
        var replayResponse = await site.Client.SendAsync(replay);

        Assert.Equal(200, (int)resetResponse.StatusCode);
        Assert.Equal(200, (int)replayResponse.StatusCode);
        Assert.True(resetResponse.Headers.CacheControl?.NoStore);
        Assert.True(resetResponse.Headers.CacheControl?.Private);
        Assert.Equal("reset", JsonNode.Parse(await replayResponse.Content.ReadAsStringAsync())?
            ["status"]?.GetValue<string>());
        Assert.Equal(0, site.Services.GetRequiredService<AskThreadRegistry>()
            .RetainedTurnsFor(token!));
        using var stale = Request("{\"message\":\"Continue.\"}", "after-reset", token);
        Assert.Equal(409, (int)(await site.Client.SendAsync(stale)).StatusCode);
        Assert.Equal(1, site.Planner.Calls);
    }

    [Fact]
    public async Task Client_supplied_transcripts_are_rejected_before_thread_creation()
    {
        await using var site = new StreamingSite();
        using var request = Request(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"injected\"}]}",
            "client-transcript");

        var response = await site.Client.SendAsync(request);

        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal(0, site.Planner.Calls);
        Assert.Equal(0, site.Services.GetRequiredService<AskThreadRegistry>().RetainedBytes);
    }

    [Fact]
    public async Task Rejected_new_thread_commit_returns_no_capability_or_empty_entry()
    {
        var threads = new AskThreadRegistry(
            TimeProvider.System, maximumThreads: 2, maximumRetainedTurns: 1,
            maximumThreadBytes: 8, maximumRetainedBytes: 8);
        await using var site = new StreamingSite(askThreads: threads);
        using var request = Request(
            "{\"message\":\"Can Lex advise me?\"}", "tiny-thread-budget");

        var response = await site.Client.SendAsync(request);
        var done = Frames(await response.Content.ReadAsStringAsync())
            .Single(frame => frame.Event == "done");

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Null(done.Data["payload"]?["thread_token"]);
        Assert.Equal(0, threads.Count);
        Assert.Equal(0, threads.RetainedBytes);
    }

    [Fact]
    public async Task Http_stream_rejects_an_unversioned_client_before_planning()
    {
        await using var site = new StreamingSite();
        using var request = Request(
            "{\"message\":\"Show coverage.\"}",
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
            "{\"message\":\"coverage\"}",
            new string('x', 129));
        using var badJson = Request("{", "bad-json");
        using var missingMessages = Request("{}", "missing-messages");
        using var oversized = Request("", "oversized");
        oversized.Content = new ByteArrayContent(new byte[65_537]);

        var cases = new[]
        {
            (await site.Client.SendAsync(invalidKey), 400, "Invalid Idempotency-Key."),
            (await site.Client.SendAsync(badJson), 400, "Bad JSON."),
            (await site.Client.SendAsync(missingMessages), 400, "Body must be {\"message\": \"...\"}."),
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
        const string body = "{\"message\":\"Show coverage.\"}";
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
            ["role"] = "user",
            ["content"] = "Can Lex advise me?",
        }), "client", "law.test", CancellationToken.None, progress, "disconnect-request");

        Assert.Equal(200, status);
        Assert.Equal("legal_boundary",
            body["operations"]?[0]?["legal_outcome"]?.GetValue<string>());
        Assert.Null(body["error"]);
    }

    [Fact]
    public void Cited_by_answer_claims_a_complete_total_only_from_an_exact_false_receipt()
    {
        const string knownScope =
            "captured_cross_references_in_held_non_withdrawn_versions";
        static string Render(JsonNode? truncated, string? scope, bool includeScope = true)
        {
            var result = new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["status"] = McpStatus.Ok, ["publisher"] = "lu-legilux",
                },
                ["cited_work"] = "eu-eurlex:32016r0679",
                ["current_legal_effect_assessed"] = false,
                ["relationship_type_assessed"] = false,
                ["citing_articles"] = 1,
                ["citations"] = new JsonArray(new JsonObject
                {
                    ["work"] = "lu-legilux:loi-example",
                    ["valid_from"] = "2026-01-01",
                    ["anchor"] = "art_1",
                    ["num"] = "Art. 1",
                }),
                ["publisher_result_set"] = new JsonObject
                {
                    ["total"] = 1, ["returned"] = 1, ["maximum"] = 32,
                    ["truncated"] = false,
                },
            };
            if (includeScope) result["evidence_scope"] = scope;
            if (truncated is not null)
                result["response_row_set"] = new JsonObject
                {
                    ["maximum"] = 50, ["returned"] = 1, ["truncated"] = truncated,
                };
            return OperationAnswerPolicy.Describe(
                "en", UiMapper.From("cited_by", new JsonObject(), result))!;
        }

        Assert.StartsWith("Lex found a total of 1 article referring to",
            Render(false, knownScope));
        Assert.StartsWith("Lex returned 1 article referring to", Render(true, knownScope));
        Assert.StartsWith("Lex returned 1 article referring to", Render(null, knownScope));
        Assert.StartsWith("Lex returned 1 article referring to",
            Render("not-a-boolean", knownScope));
        Assert.StartsWith("Lex returned 1 article referring to",
            Render(false, null, includeScope: false));
        Assert.StartsWith("Lex returned 1 article referring to",
            Render(false, "future_unreviewed_scope"));
    }

    [Fact]
    public void Cited_by_answer_states_scope_and_false_assessments_with_grammatical_counts()
    {
        var english = OperationAnswerPolicy.Describe("en", new UiEffect(CitedBy: new CitedByView(
            "eu-eurlex:32016r0679", 1, [], RowsTruncated: false,
            EvidenceScope: "captured_cross_references_in_held_non_withdrawn_versions",
            CurrentLegalEffectAssessed: false, RelationshipTypeAssessed: false,
            ExactComplete: true)));
        var french = OperationAnswerPolicy.Describe("fr", new UiEffect(CitedBy: new CitedByView(
            "eu-eurlex:32016r0679", 2, [], RowsTruncated: true,
            EvidenceScope: "captured_cross_references_in_held_non_withdrawn_versions",
            CurrentLegalEffectAssessed: false, RelationshipTypeAssessed: false)));
        var limited = OperationAnswerPolicy.Describe("en", new UiEffect(
            CitedBy: new CitedByView(
                "eu-eurlex:32016r0679", 1, [], RowsTruncated: false,
                EvidenceScope: "captured_cross_references_in_held_non_withdrawn_versions"),
            PublisherLimitations:
            [
                new(McpStatus.FilterNotSupportedByIndex, "cited_by", "eu-eurlex", "EU", []),
            ]));

        Assert.Equal(
            "Lex found a total of 1 article referring to eu-eurlex:32016r0679. "
            + "The evidence covers cross-references Lex captured in held, non-withdrawn publisher versions. "
            + "Lex did not assess whether this reference is currently legally operative. "
            + "Lex did not classify its relationship type.", english);
        Assert.Equal(
            "Lex a renvoyé 2 articles faisant référence à eu-eurlex:32016r0679. "
            + "Les éléments de preuve couvrent les renvois capturés par Lex dans les versions éditeur détenues et non retirées. "
            + "Lex n'a pas évalué si ces renvois produisent actuellement un effet juridique. "
            + "Lex n'a pas classé leurs types de relation.", french);
        Assert.StartsWith("Lex returned 1 article referring to", limited);
        Assert.DoesNotContain("a total of", limited);
    }

    [Fact]
    public void Cited_by_answer_does_not_repeat_an_unrecognized_evidence_scope()
    {
        var answer = OperationAnswerPolicy.Describe("en", new UiEffect(CitedBy: new CitedByView(
            "eu-eurlex:32016r0679", 0, [], RowsTruncated: null,
            EvidenceScope: "future_unreviewed_scope")));

        Assert.Contains("The response does not carry a recognized evidence scope.", answer);
        Assert.DoesNotContain("future_unreviewed_scope", answer);
    }

    [Fact]
    public void Cited_by_synthesis_evidence_starts_with_a_bounded_aggregate_fact()
    {
        const string knownScope =
            "captured_cross_references_in_held_non_withdrawn_versions";
        static (AgentEvidence Evidence, JsonObject Fact) AggregateFact(JsonNode source)
        {
            var (status, docs) = AskService.Summarize(source);
            var ledger = new AgentEvidenceLedger();
            ledger.Observe("cited_by", status, docs, source, new JsonObject());
            var evidence = Assert.Single(ledger.Evidence,
                item => item.Title == "cited_by aggregate fact");
            return (evidence, JsonNode.Parse(evidence.Excerpt!)!.AsObject());
        }
        static JsonObject PublisherResult(
            string publisher, string citingWork, int publisherCount = 1, int responseRows = 1)
        {
            var suffix = publisher.Replace("-", "", StringComparison.Ordinal);
            return new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["status"] = McpStatus.Ok,
                    ["publisher"] = publisher,
                },
                ["cited_work"] = citingWork,
                ["evidence_scope"] =
                    "captured_cross_references_in_held_non_withdrawn_versions",
                ["current_legal_effect_assessed"] = false,
                ["relationship_type_assessed"] = false,
                ["citing_articles"] = 1,
                ["publisher_result_set"] = new JsonObject
                {
                    ["total"] = publisherCount, ["returned"] = publisherCount,
                    ["maximum"] = 32, ["truncated"] = false,
                },
                ["response_row_set"] = new JsonObject
                {
                    ["maximum"] = 50, ["returned"] = responseRows,
                    ["truncated"] = false,
                },
                ["citations"] = new JsonArray(new JsonObject
                {
                    ["work"] = $"{publisher}:work-{suffix}",
                    ["title"] = "Example",
                    ["valid_from"] = "2026-01-01",
                    ["anchor"] = "art_1",
                    ["num"] = "Art. 1",
                }),
            };
        }

        var payload = new JsonObject
        {
            ["envelope"] = new JsonObject
            {
                ["status"] = McpStatus.Ok, ["publisher"] = "lu-legilux",
            },
            ["cited_work"] = "eu-eurlex:32016r0679",
            ["evidence_scope"] = knownScope,
            ["current_legal_effect_assessed"] = false,
            ["relationship_type_assessed"] = false,
            ["citing_articles"] = 1,
            ["publisher_result_set"] = new JsonObject
            {
                ["total"] = 1, ["returned"] = 1, ["maximum"] = 32,
                ["truncated"] = false,
            },
            ["response_row_set"] = new JsonObject
            {
                ["maximum"] = 50, ["returned"] = 1, ["truncated"] = false,
            },
            ["citations"] = new JsonArray(new JsonObject
            {
                ["work"] = "lu-legilux:loi-example",
                ["title"] = "Example",
                ["valid_from"] = "2026-01-01",
                ["anchor"] = "art_1",
                ["num"] = "Art. 1",
            }),
        };

        var (aggregate, fact) = AggregateFact(payload);
        Assert.Equal("complete_total", fact["count_semantics"]?.GetValue<string>());
        Assert.Equal(1, fact["count"]?.GetValue<int>());
        Assert.Equal(
            "captured_cross_references_in_held_non_withdrawn_versions",
            fact["evidence_scope"]?.GetValue<string>());
        Assert.False(fact["current_legal_effect_assessed"]?.GetValue<bool>());
        Assert.False(fact["relationship_type_assessed"]?.GetValue<bool>());
        Assert.True(aggregate.Excerpt!.Length <= 8_000);

        payload["response_row_set"]!["truncated"] = true;
        Assert.Equal("returned_count",
            AggregateFact(payload).Fact["count_semantics"]?.GetValue<string>());
        payload.Remove("response_row_set");
        Assert.Equal("returned_count",
            AggregateFact(payload).Fact["count_semantics"]?.GetValue<string>());
        payload["response_row_set"] = new JsonObject { ["truncated"] = "not-a-boolean" };
        Assert.Equal("returned_count",
            AggregateFact(payload).Fact["count_semantics"]?.GetValue<string>());
        payload["response_row_set"] = new JsonObject
        {
            ["maximum"] = 50, ["returned"] = 1, ["truncated"] = false,
        };
        payload.Remove("evidence_scope");
        Assert.Equal("returned_count",
            AggregateFact(payload).Fact["count_semantics"]?.GetValue<string>());
        payload["evidence_scope"] = "future_unreviewed_scope";
        Assert.Equal("returned_count",
            AggregateFact(payload).Fact["count_semantics"]?.GetValue<string>());

        var citedWork = "eu-eurlex:32016r0679";
        var successAndRefusal = new JsonArray(
            PublisherResult("lu-legilux", citedWork, publisherCount: 2),
            new JsonObject
            {
                ["envelope"] = new JsonObject
                {
                    ["status"] = McpStatus.FilterNotSupportedByIndex,
                    ["publisher"] = "eu-eurlex",
                },
                ["cited_work"] = citedWork,
                ["publisher_result_set"] = new JsonObject
                {
                    ["total"] = 2, ["returned"] = 2, ["maximum"] = 32,
                    ["truncated"] = false,
                },
                ["response_row_set"] = new JsonObject
                {
                    ["maximum"] = 50, ["returned"] = 1, ["truncated"] = false,
                },
            });
        var partial = AggregateFact(successAndRefusal).Fact;
        Assert.Equal("returned_count", partial["count_semantics"]?.GetValue<string>());
        Assert.Equal(1, partial["count"]?.GetValue<int>());

        var twoSuccesses = AggregateFact(new JsonArray(
            PublisherResult("lu-legilux", citedWork, publisherCount: 2, responseRows: 2),
            PublisherResult("eu-eurlex", citedWork, publisherCount: 2, responseRows: 2))).Fact;
        Assert.Equal("complete_total", twoSuccesses["count_semantics"]?.GetValue<string>());
        Assert.Equal(2, twoSuccesses["count"]?.GetValue<int>());
    }

    [Fact]
    public void Timeline_answer_qualifies_true_and_unknown_completeness_receipts()
    {
        var subject = new Subject("lu-legilux:loi-example", "Example law", null, null);
        TimelineView View(bool? truncated) => new(subject,
            [
                new(null, "2020-01-01", null, null, null, null, null),
                new(null, "2021-01-01", null, null, null, null, null),
            ], TotalCount: 3, Truncated: truncated);

        var complete = OperationAnswerPolicy.Describe(
            "en", new UiEffect(Timeline: View(false)))!;
        var bounded = OperationAnswerPolicy.Describe(
            "en", new UiEffect(Timeline: View(true)))!;
        var unknown = OperationAnswerPolicy.Describe(
            "en", new UiEffect(Timeline: View(null)))!;

        Assert.Contains("Lex holds 3 publisher version states", complete);
        Assert.Contains("latest state beginning 2021-01-01", complete);
        Assert.Contains("Lex returned 2 of 3 publisher version states", bounded);
        Assert.Contains("last returned state beginning 2021-01-01", bounded);
        Assert.Contains("This bounded view is truncated.", bounded);
        Assert.DoesNotContain("latest state", bounded);
        Assert.Contains("Lex returned 2 publisher version states", unknown);
        Assert.Contains("does not record whether the timeline is complete", unknown);
        Assert.DoesNotContain("latest state", unknown);
    }

    [Fact]
    public void History_answer_never_calls_a_bounded_or_unknown_last_row_latest()
    {
        var subject = new Subject(
            "lu-legilux:loi-example", "Example law", null, "art_1", "fr");
        HistoryView View(bool? truncated) => new(subject, "art_1", DistinctTexts: 3,
            States:
            [
                new("2020-01-01", null, null, null),
                new("2021-01-01", null, null, null),
            ], Truncated: truncated);

        var complete = OperationAnswerPolicy.Describe(
            "en", new UiEffect(History: View(false)))!;
        var bounded = OperationAnswerPolicy.Describe(
            "en", new UiEffect(History: View(true)))!;
        var unknown = OperationAnswerPolicy.Describe(
            "en", new UiEffect(History: View(null)))!;

        Assert.Contains("contains 3 distinct text(s)", complete);
        Assert.Contains("latest state beginning 2021-01-01", complete);
        Assert.Contains("reports 3 distinct text(s) and returns 2 state(s)", bounded);
        Assert.Contains("last returned state beginning 2021-01-01", bounded);
        Assert.Contains("This bounded response is truncated.", bounded);
        Assert.DoesNotContain("latest state", bounded);
        Assert.Contains("does not record whether the history is complete", unknown);
        Assert.DoesNotContain("latest state", unknown);
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
            ["role"] = "user",
            ["content"] = "Can Lex advise and interpret this?",
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
            // Keep the outer deadline well clear of the planner deadline so this test proves
            // planner cancellation and lease release even when the full suite delays continuations.
            firstResultDeadline: TimeSpan.FromSeconds(2));
        var history = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["content"] = "Show coverage.",
        });

        var first = await service.AskAsync(
                history, "first-client", "law.test", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var second = await service.AskAsync(
                history, "second-client", "law.test", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(504, first.Status);
        Assert.Equal(504, second.Status);
        Assert.True(first.RetainForReplay);
        Assert.Equal(2, planner.Calls);
        Assert.Equal(2, planner.Cancellations);
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

    private static HttpRequestMessage Request(string body, string key, string? threadToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask/stream")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Lex-Stream-Version", "1");
        if (threadToken is not null)
            request.Headers.Add("X-Lex-Thread-Token", threadToken);
        return request;
    }

    private static HttpRequestMessage ResetRequest(string threadToken, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask/thread/reset")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Lex-Thread-Token", threadToken);
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

    private static string AskFingerprint(string body, string? threadToken) =>
        Hash($"ask-v2\n{threadToken ?? "-"}\n{body}");

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    private sealed class StreamingSite : WebApplicationFactory<Program>
    {
        private readonly string _indexDir = Path.Combine(
            Path.GetTempPath(), $"lex-stream-{Guid.NewGuid():N}");
        private readonly IOperationPlanner _planner;

        public StreamingSite(
            McpAdmissionController? mcpAdmission = null,
            AskThreadRegistry? askThreads = null,
            AskRequestRegistry? askRequests = null,
            IOperationPlanner? planner = null)
        {
            Directory.CreateDirectory(_indexDir);
            McpAdmission = mcpAdmission;
            AskThreads = askThreads;
            AskRequests = askRequests;
            Planner = new BoundaryPlanner();
            _planner = planner ?? Planner;
            Client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        }

        public BoundaryPlanner Planner { get; }
        public HttpClient Client { get; }
        public McpAdmissionController? McpAdmission { get; }
        public AskThreadRegistry? AskThreads { get; }
        public AskRequestRegistry? AskRequests { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("LEX_INDEX_DIR", _indexDir);
            builder.UseSetting("LEX_PUBLIC_BASE_URL", "https://stream.test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AskService>();
                services.AddSingleton(_ => new AskService(
                    new McpCore(new Dictionary<string, Lex.Index.LexIndexReader>()), _planner));
                if (McpAdmission is not null)
                {
                    services.RemoveAll<McpAdmissionController>();
                    services.AddSingleton(McpAdmission);
                }
                if (AskThreads is not null)
                {
                    services.RemoveAll<AskThreadRegistry>();
                    services.AddSingleton(AskThreads);
                }
                if (AskRequests is not null)
                {
                    services.RemoveAll<AskRequestRegistry>();
                    services.AddSingleton(AskRequests);
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
        public List<JsonArray> Histories { get; } = [];

        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Calls++;
            Histories.Add(history.DeepClone().AsArray());
            return Task.FromResult(OperationPlan.FromPlannerOutput(
                requestId, "en", new JsonArray(new JsonObject
                {
                    ["tool"] = "legal_boundary",
                    ["arguments"] = new JsonObject { ["reason"] = "legal advice" },
                }), synthesisRequested: false));
        }
    }

    private sealed class InvalidPlanner : IOperationPlanner
    {
        public int Calls { get; private set; }

        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidDataException("The planner returned a terminal invalid plan.");
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
        public int Cancellations;

        public async Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref Cancellations);
                throw;
            }
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
