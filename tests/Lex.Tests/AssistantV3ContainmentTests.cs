using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;
using Lex.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lex.Tests;

public sealed class AssistantV3ContainmentTests
{
    private const string EnglishNotice =
        "The assistant is temporarily unavailable while Lex installs its deterministic V3 answer path, checkable against its sources. Search and held publisher text remain available.";
    private const string FrenchNotice =
        "L'assistant est temporairement indisponible pendant que Lex met en place son parcours de réponse V3 déterministe et vérifiable par ses sources. La recherche et les textes publiés que Lex détient restent disponibles.";
    private const string LocalizationNotice =
        "Lex cannot provide this assistant notice in the requested language yet. The assistant is unavailable during the V3 answer-path replacement. Search and held publisher text remain available.";

    [Theory]
    [InlineData("What does this law say?", "assistant_v3_unavailable", "en", null, EnglishNotice)]
    [InlineData("Que prévoit cette loi ?", "assistant_v3_unavailable", "fr", null, FrenchNotice)]
    [InlineData("32016R0679", "localization_unavailable", "undetermined", "en", LocalizationNotice)]
    [InlineData("que dice esta ley", "localization_unavailable", "undetermined", "en", LocalizationNotice)]
    [InlineData("Cite this law", "localization_unavailable", "undetermined", "en", LocalizationNotice)]
    [InlineData("Comment on this law", "localization_unavailable", "undetermined", "en", LocalizationNotice)]
    [InlineData("Figure this out", "localization_unavailable", "undetermined", "en", LocalizationNotice)]
    public async Task Contained_service_returns_the_reviewed_typed_result(
        string question,
        string expectedStatus,
        string expectedRequestedLocale,
        string? expectedFallbackLocale,
        string expectedReply)
    {
        var planner = new ExplodingPlanner();
        var synthesizer = new ExplodingSynthesizer();
        var admission = new AskAdmissionController(TimeProvider.System, 1, 1, 1);
        var toolCalls = 0;
        var service = new AskService(
            EmptyCore(), planner, synthesizer, admission,
            legalTool: (_, _, _) =>
            {
                toolCalls++;
                throw new InvalidOperationException("The contained route called a legal tool.");
            },
            containLegacyAuthoritativeAssistant: true);

        var outcome = await service.AskAsync(
            History(question), "test-client", "law.soufien.lu", CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Equal(0, synthesizer.Calls);
        Assert.Equal(0, toolCalls);
        Assert.Equal(0, admission.AcceptedToday);
        Assert.Equal(200, outcome.Status);
        Assert.False(outcome.RetainForReplay);
        Assert.False(outcome.RetainConversation);
        Assert.Equal(AskConversationContextDisposition.Clear, outcome.ContextDisposition);
        Assert.Equal(expectedReply, outcome.Body["reply"]?.GetValue<string>());
        Assert.Empty(outcome.Body["trace"]!.AsArray());
        Assert.Empty(outcome.Body["operations"]!.AsArray());
        Assert.False(outcome.Body["narrated"]!.GetValue<bool>());
        Assert.Null(outcome.Body["model_usage"]);
        Assert.Null(outcome.Body["model_identity"]);
        Assert.Null(outcome.Body["timing"]);
        Assert.Null(outcome.Body["thread_token"]);

        var gap = outcome.Body["ui"]!["gap"]!.AsObject();
        Assert.Equal(expectedStatus, gap["status"]?.GetValue<string>());
        Assert.Equal(expectedRequestedLocale, gap["requested_locale"]?.GetValue<string>());
        Assert.Equal(expectedFallbackLocale, gap["fallback_locale"]?.GetValue<string>());
        Assert.Equal(["en", "fr"], Strings(gap["available_locales"]));
        Assert.Equal(["search", "browse"], Strings(gap["actions"]));
        Assert.Empty(gap["available"]!.AsArray());
        Assert.DoesNotContain("href", outcome.Body.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/?space=search", outcome.Body.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/browse", outcome.Body.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Containment_locale_uses_only_the_current_request()
    {
        var service = ContainedService();
        var history = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "What does this law say?" },
            new JsonObject { ["role"] = "assistant", ["content"] = "Earlier answer" },
            new JsonObject { ["role"] = "user", ["content"] = "32016R0679" },
        };

        var outcome = await service.AskAsync(
            history, "test-client", "law.soufien.lu", CancellationToken.None);

        var gap = outcome.Body["ui"]!["gap"]!.AsObject();
        Assert.Equal("localization_unavailable", gap["status"]?.GetValue<string>());
        Assert.Equal("undetermined", gap["requested_locale"]?.GetValue<string>());
        Assert.Equal(LocalizationNotice, outcome.Body["reply"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("/api/ask", false)]
    [InlineData("/api/ask/stream", true)]
    public async Task Containment_precedes_thread_capacity_without_eviction(
        string path, bool stream)
    {
        var threads = new AskThreadRegistry(TimeProvider.System, maximumThreads: 1);
        var acquired = await threads.AcquireAsync(null);
        var existing = acquired.Lease!;
        var token = existing.Token;
        Assert.True(existing.Commit("Earlier question", "Earlier answer", null));
        await existing.DisposeAsync();
        var originalHistory = await RetainedHistory(threads, token);
        await using var factory = FactoryWith(threads);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Request(path, null, stream));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("assistant_v3_unavailable",
            (await Payload(response, stream))["ui"]!["gap"]!["status"]?.GetValue<string>());
        Assert.Equal(1, threads.Count);
        Assert.Equal(originalHistory, await RetainedHistory(threads, token));
    }

    [Theory]
    [InlineData("/api/ask", false)]
    [InlineData("/api/ask/stream", true)]
    public async Task Containment_precedes_stale_thread_lookup(string path, bool stream)
    {
        var threads = new AskThreadRegistry(TimeProvider.System, maximumThreads: 1);
        await using var factory = FactoryWith(threads);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            Request(path, new string('A', AskThreadRegistry.TokenLength), stream));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("assistant_v3_unavailable",
            (await Payload(response, stream))["ui"]!["gap"]!["status"]?.GetValue<string>());
        Assert.Equal(0, threads.Count);
    }

    [Fact]
    public async Task Public_pages_describe_the_contained_assistant()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var how = await client.GetStringAsync("/how-it-works");
        var stories = await client.GetStringAsync("/stories");
        var architecture = await client.GetStringAsync("/built/assistant");

        Assert.Contains(EnglishNotice, how, StringComparison.Ordinal);
        Assert.DoesNotContain("may plan searches and explain retrieved evidence", how,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Ask the assistant", stories, StringComparison.Ordinal);
        Assert.Contains(EnglishNotice, architecture, StringComparison.Ordinal);
        Assert.Contains("Historical V2 architecture", architecture, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ordinary_and_streaming_endpoints_do_not_commit_or_return_a_thread()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var threads = factory.Services.GetRequiredService<AskThreadRegistry>();
        var requests = factory.Services.GetRequiredService<AskRequestRegistry>();
        var acquired = await threads.AcquireAsync(null);
        Assert.Equal(AskThreadAcquireKind.Acquired, acquired.Kind);
        var thread = acquired.Lease!;
        var token = thread.Token;
        Assert.True(thread.Commit("Earlier question", "Earlier answer", null));
        await thread.DisposeAsync();
        Assert.Equal(1, threads.RetainedTurnsFor(token));
        Assert.Equal(1, threads.Count);
        var originalHistory = await RetainedHistory(threads, token);

        using var tokenless = Request("/api/ask", null, stream: false);
        using var tokenlessResponse = await client.SendAsync(tokenless);
        Assert.Equal(HttpStatusCode.OK, tokenlessResponse.StatusCode);
        var tokenlessBody = JsonNode.Parse(
            await tokenlessResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Null(tokenlessBody["thread_token"]);
        Assert.Equal(1, threads.Count);
        Assert.Equal(0, requests.RetainedBytes);

        using var ordinary = Request("/api/ask", token, stream: false);
        using var ordinaryResponse = await client.SendAsync(ordinary);
        Assert.Equal(HttpStatusCode.OK, ordinaryResponse.StatusCode);
        var ordinaryBody = JsonNode.Parse(await ordinaryResponse.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal("assistant_v3_unavailable",
            ordinaryBody["ui"]!["gap"]!["status"]?.GetValue<string>());
        Assert.Null(ordinaryBody["thread_token"]);
        Assert.Equal(1, threads.RetainedTurnsFor(token));
        Assert.Equal(originalHistory, await RetainedHistory(threads, token));
        Assert.Equal(0, requests.RetainedBytes);

        using var streaming = Request("/api/ask/stream", token, stream: true);
        using var streamingResponse = await client.SendAsync(streaming);
        Assert.Equal(HttpStatusCode.OK, streamingResponse.StatusCode);
        var stream = await streamingResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("event: phase", stream, StringComparison.Ordinal);
        Assert.DoesNotContain("event: step", stream, StringComparison.Ordinal);
        Assert.DoesNotContain("event: operation_result", stream, StringComparison.Ordinal);
        Assert.DoesNotContain("event: synthesis", stream, StringComparison.Ordinal);
        Assert.Equal(["event: done"], stream.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("event: ", StringComparison.Ordinal)));
        var data = Assert.Single(
            stream.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith("data: ", StringComparison.Ordinal));
        var envelope = JsonNode.Parse(data[6..])!.AsObject();
        var payload = envelope["payload"]!.AsObject();
        Assert.Equal("assistant_v3_unavailable",
            payload["ui"]!["gap"]!["status"]?.GetValue<string>());
        Assert.Null(payload["thread_token"]);
        Assert.Equal(1, threads.RetainedTurnsFor(token));
        Assert.Equal(originalHistory, await RetainedHistory(threads, token));
        Assert.Equal(0, requests.RetainedBytes);
    }

    private static async Task<string> RetainedHistory(AskThreadRegistry threads, string token)
    {
        var acquired = await threads.AcquireAsync(token);
        Assert.Equal(AskThreadAcquireKind.Acquired, acquired.Kind);
        await using var thread = acquired.Lease!;
        return thread.History.ToJsonString();
    }

    private static WebApplicationFactory<Program> FactoryWith(AskThreadRegistry threads) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AskThreadRegistry>();
                services.AddSingleton(threads);
            }));

    private static async Task<JsonObject> Payload(HttpResponseMessage response, bool stream)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!stream) return JsonNode.Parse(body)!.AsObject();
        var data = Assert.Single(
            body.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith("data: ", StringComparison.Ordinal));
        return JsonNode.Parse(data[6..])!["payload"]!.AsObject();
    }

    private static HttpRequestMessage Request(string path, string? token, bool stream)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { message = "What does this law say?" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        if (token is not null) request.Headers.Add("X-Lex-Thread-Token", token);
        if (stream) request.Headers.Add("X-Lex-Stream-Version", "1");
        return request;
    }

    private static JsonArray History(string question) =>
    [
        new JsonObject
        {
            ["role"] = "user",
            ["content"] = question,
        },
    ];

    private static McpCore EmptyCore() =>
        new(new Dictionary<string, LexIndexReader>(StringComparer.Ordinal));

    private static AskService ContainedService() =>
        new(EmptyCore(), new ExplodingPlanner(), new ExplodingSynthesizer(),
            new AskAdmissionController(TimeProvider.System, 1, 1, 1),
            containLegacyAuthoritativeAssistant: true);

    private static string[] Strings(JsonNode? node) => node!.AsArray()
        .Select(item => item!.GetValue<string>()).ToArray();

    private sealed class ExplodingPlanner : IOperationPlanner
    {
        public int Calls { get; private set; }

        public Task<OperationPlan> PlanAsync(
            JsonArray history,
            string host,
            string requestId,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The contained route called the planner.");
        }
    }

    private sealed class ExplodingSynthesizer : IOperationSynthesizer
    {
        public int Calls { get; private set; }

        public Task<AgentFinalization> SynthesizeAsync(
            string question,
            string deterministicDraft,
            IReadOnlyList<AgentEvidence> evidence,
            string locale,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The contained route called the synthesizer.");
        }
    }
}
