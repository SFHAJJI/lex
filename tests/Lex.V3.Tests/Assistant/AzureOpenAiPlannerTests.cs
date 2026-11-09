using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform.Assistant;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The Azure OpenAI planner over a fake HTTP handler: no request leaves the test. What it sends (the masked question, the
/// opaque references, the prompt and the strict schema, and nothing else, R33), what it answers, what it costs, and how every
/// failure (a timeout, a 429, malformed JSON, a schema-valid but unbound plan) fails closed on the switched-off card while the
/// validator stays the judge.
/// </summary>
[TestClass]
public sealed class AzureOpenAiPlannerTests
{
    private const string NotAKey = "test-value-not-a-key";
    private static readonly Uri Endpoint = new("https://example-resource.openai.azure.com/");
    private static readonly V3AssistantEnvironment Environment = new("2026-10-28", "corpus-0123456789abcdef", new string('b', 64), ["fra"]);

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, string? Key, string Body)> Seen { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.TryGetValues("api-key", out var values) ? values.Single() : null;
            Seen.Add((request.RequestUri!, key, request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()));
            return respond(request, cancellationToken);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private static HttpResponseMessage Completion(string content, string finish = "stop", int prompt = 1800, int completion = 200) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["finish_reason"] = finish,
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content, ["refusal"] = null },
                }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = prompt, ["completion_tokens"] = completion, ["total_tokens"] = prompt + completion },
            }.ToJsonString(), Encoding.UTF8, "application/json"),
        };

    private static AzureOpenAiAssistantPlanner Planner(FakeHandler handler, TimeSpan? timeout = null) =>
        new(Endpoint, "plan-model", "2024-10-21", NotAKey, handler, timeout: timeout);

    /// <summary>A plan in the wire form: one search with the citation as its query, and the follow-up that reads its article.</summary>
    private static string WirePlan(string question, string dateRef = "{\"today\":true}")
    {
        var start = question.IndexOf("article", StringComparison.Ordinal);
        var end = question.IndexOf("1991", StringComparison.Ordinal) + 4;
        var trimmed = question.TrimEnd(' ', '?', ' ').Length;
        return "{\"schema\":\"lex-v3-assistant-plan/1\",\"parts\":[{\"part_id\":\"p1\",\"span\":[0," + trimmed + "],\"proposed_verdict\":\"answer\"}],"
            + "\"steps\":[{\"step_id\":\"s1\",\"part_id\":\"p1\",\"operation_id\":\"search\",\"arguments\":["
            + "{\"name\":\"query\",\"source\":{\"span\":[" + start + "," + end + "]}},{\"name\":\"language\",\"source\":{\"choice\":\"fra\"}},{\"name\":\"date\",\"source\":" + dateRef + "}]}],"
            + "\"follow_ups\":[{\"step_id\":\"f1\",\"after\":\"s1\",\"condition\":\"on_single_article\",\"operation_id\":\"evidence_bundle\",\"arguments\":["
            + "{\"name\":\"identifier\",\"source\":{\"result\":\"s1\",\"pick\":\"sole_work\"}},{\"name\":\"article\",\"source\":{\"result\":\"s1\",\"pick\":\"sole_article\"}},{\"name\":\"date\",\"source\":" + dateRef + "}]}]}";
    }

    /// <summary>An executor that answers every search with no hit, so a plan runs end to end without a mount.</summary>
    private static V3StepOutcome NoHit(string operationId, string parametersJson) =>
        new(operationId, parametersJson, "quote", "answer",
            JsonSerializer.SerializeToElement(new { publisher = "lu-legilux", reference = new { outcome = "no_reference" }, lanes_run = true, hits = Array.Empty<object>() }),
            null, null);

    [TestMethod]
    public void ItSendsTheMaskedQuestionTheReferencesThePromptAndTheStrictSchemaAndNothingElse()
    {
        var address = "someone" + "@" + "example.invalid";
        var question = $"Mon adresse est {address}, mon IBAN LU28 0019 4006 4475 0000, tél. +352 621 123 456, matricule 19900101123 : que dit l'article 1er de la loi du 10 août 1991 ?";
        var handler = new FakeHandler((_, _) => Completion(WirePlan(question)));
        using var planner = Planner(handler);
        var outcome = V3AssistantPipeline.Answer(new V3AssistantRequest(question), planner, NoHit, Environment, CancellationToken.None);
        Assert.IsInstanceOfType<V3AskAnswered>(outcome, outcome.Reason.ToString());

        var (uri, key, body) = handler.Seen.Single();
        Assert.AreEqual("https://example-resource.openai.azure.com/openai/deployments/plan-model/chat/completions?api-version=2024-10-21", uri.ToString());
        Assert.AreEqual(NotAKey, key, "the key travels as the api-key header, and only there");
        using var sent = JsonDocument.Parse(body);
        var messages = sent.RootElement.GetProperty("messages");
        Assert.AreEqual(AzureOpenAiAssistantPlanner.Prompt, messages[0].GetProperty("content").GetString(), "the versioned prompt");
        StringAssert.StartsWith(AzureOpenAiAssistantPlanner.Prompt, "lex-v3-assistant-planner-prompt/1");
        var format = sent.RootElement.GetProperty("response_format");
        Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
        Assert.IsTrue(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        using var user = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
        CollectionAssert.AreEquivalent(
            new[] { "question", "answer_locale", "references", "search_languages", "operations" },
            user.RootElement.EnumerateObject().Select(static member => member.Name).ToArray(),
            "R33: only the question and opaque references, with the closed lists they are read against");
        var masked = user.RootElement.GetProperty("question").GetString()!;
        foreach (var identifier in new[] { address, "LU28", "621 123 456", "19900101123" })
        {
            Assert.IsFalse(body.Contains(identifier, StringComparison.Ordinal), $"{identifier} reached the request");
        }

        StringAssert.Contains(masked, "que dit l'article 1er de la loi du 10 août 1991", "the law's words are sent");
        Assert.AreEqual(question.Length, masked.Length);
        Assert.IsFalse(body.Contains("User-Agent", StringComparison.OrdinalIgnoreCase) || body.Contains("cookie", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ItsCostIsTheResponsesUsageAndItCountsTowardTheCaps()
    {
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        var handler = new FakeHandler((_, _) => Completion(WirePlan(question), prompt: 1800, completion: 200));
        using var planner = Planner(handler);
        var output = planner.Plan(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"]), CancellationToken.None);
        Assert.AreEqual((1800 * AzureOpenAiAssistantPlanner.DefaultInputPrice) + (200 * AzureOpenAiAssistantPlanner.DefaultOutputPrice), output.CostMicroEuros);
        var runtime = new V3AssistantRuntime(true, false, planner, requireReviewedWording: false);
        _ = runtime.Budgeted().Plan(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"]), CancellationToken.None);
        Assert.AreEqual(output.CostMicroEuros, runtime.SpentMicroEuros, "the usage is counted toward the month");
        StringAssert.StartsWith(planner.PlannerId, "azure-openai/plan-model/prompt-");
        Assert.IsFalse(planner.PlannerId.Contains(NotAKey, StringComparison.Ordinal));
    }

    [TestMethod]
    public void ATimeoutA429AndACutShortAnswerFailClosedOnTheSwitchedOffCardWithNoKeyInAnyMessage()
    {
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        (string Why, FakeHandler Handler)[] failures =
        [
            ("a timeout", new FakeHandler((_, token) =>
            {
                Task.Delay(System.Threading.Timeout.Infinite, token).GetAwaiter().GetResult();
                throw new InvalidOperationException("unreachable");
            })),
            ("a 429", new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"error\":\"" + NotAKey + "\"}") })),
            ("a 500", new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            ("an answer cut short", new FakeHandler((_, _) => Completion(WirePlan(question), finish: "length"))),
            ("an answer filtered", new FakeHandler((_, _) => Completion(WirePlan(question), finish: "content_filter"))),
        ];
        foreach (var (why, handler) in failures)
        {
            using var planner = Planner(handler, TimeSpan.FromMilliseconds(300));
            var thrown = Assert.ThrowsExactly<Exception>(() => _ = Catch(() => planner.Plan(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"]), CancellationToken.None)), why);
            Assert.IsFalse(thrown.ToString().Contains(NotAKey, StringComparison.Ordinal), $"{why}: the key is in no message");
            var outcome = V3AssistantPipeline.Answer(new V3AssistantRequest(question), planner, NoHit, Environment, CancellationToken.None);
            Assert.AreEqual(V3AskOutcomeReason.PlannerFailed, outcome.Reason, why);
            Assert.AreEqual(V3AskPresentationResult.AssistantUnavailable, ((V3AskPresented)outcome).Result, why);
        }
    }

    /// <summary>Runs <paramref name="call"/> and rethrows whatever it threw as a plain <see cref="Exception"/>, for one assertion over many types.</summary>
    private static object Catch(Func<object> call)
    {
        try
        {
            return call();
        }
        catch (Exception exception)
        {
            throw new Exception("planner failed", exception);
        }
    }

    [TestMethod]
    public void TheValidatorStaysTheJudgeOfWhatTheModelAnswers()
    {
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        (string Why, string Content, V3PlanRefusal Refusal)[] cases =
        [
            ("malformed JSON", "{\"schema\":\"lex-v3-assistant-plan/1\",", V3PlanRefusal.Malformed),
            ("prose instead of a plan", "You can certainly be fired under article 1er.", V3PlanRefusal.Malformed),
            ("schema-valid but unbound: a reference never issued", WirePlan(question, "{\"ref\":\"d9\"}"), V3PlanRefusal.UnboundArgument),
            ("an operation the grammar does not hold", WirePlan(question).Replace("\"search\"", "\"verify\"", StringComparison.Ordinal), V3PlanRefusal.OperationNotAllowed),
            ("a field outside the schema", WirePlan(question).Replace("\"follow_ups\"", "\"answer\":\"yes\",\"follow_ups\"", StringComparison.Ordinal), V3PlanRefusal.UnknownMember),
            ("an argument named twice", WirePlan(question).Replace("{\"name\":\"language\",\"source\":{\"choice\":\"fra\"}}", "{\"name\":\"language\",\"source\":{\"choice\":\"fra\"}},{\"name\":\"language\",\"source\":{\"choice\":\"fra\"}}", StringComparison.Ordinal), V3PlanRefusal.Malformed),
        ];
        foreach (var (why, content, refusal) in cases)
        {
            using var planner = Planner(new FakeHandler((_, _) => Completion(content)));
            var outcome = V3AssistantPipeline.Answer(new V3AssistantRequest(question), planner, NoHit, Environment, CancellationToken.None);
            var presented = outcome as V3AskPresented ?? throw new AssertFailedException($"{why}: answered");
            Assert.AreEqual(V3AskOutcomeReason.PlanRefused, presented.Reason, why);
            Assert.AreEqual(refusal, presented.PlanRefusal, why);
        }
    }

    [TestMethod]
    public void TheWireSchemaIsStrictAndMapsToTheGrammarWithoutRepair()
    {
        void Strict(JsonNode? node, string at)
        {
            if (node is JsonObject schema)
            {
                if (schema["type"]?.GetValue<string>() == "object")
                {
                    Assert.IsFalse(schema["additionalProperties"]!.GetValue<bool>(), $"{at}: closed");
                    CollectionAssert.AreEquivalent(
                        ((JsonObject)schema["properties"]!).Select(static property => property.Key).ToArray(),
                        ((JsonArray)schema["required"]!).Select(static name => name!.GetValue<string>()).ToArray(),
                        $"{at}: every property required");
                }

                foreach (var (name, child) in schema)
                {
                    Strict(child, at + "/" + name);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    Strict(item, at + "[]");
                }
            }
        }

        Strict(V3AssistantPlanWire.Schema(), "#");
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        var mapped = V3AssistantPlanWire.ToPlanJson(WirePlan(question));
        var intake = V3AssistantParsers.Intake(question)!;
        var dates = V3AssistantParsers.DatesIn(intake.Text);
        var validation = V3AssistantPlanValidator.Validate(mapped, new V3PlanContext(intake, dates, V3AssistantParsers.RangesIn(intake.Text, dates), [], ["fra"]));
        Assert.IsTrue(validation.Admitted, validation.Detail);
        Assert.AreEqual("not json", V3AssistantPlanWire.ToPlanJson("not json"), "a shape it does not recognise is handed on unchanged");
    }

    [TestMethod]
    public void WithNoEndpointThereIsNoPlannerAndTheAssistantStaysOff()
    {
        var complete = new Dictionary<string, string?>
        {
            [AzureOpenAiAssistantPlanner.EndpointVariable] = Endpoint.ToString(),
            [AzureOpenAiAssistantPlanner.DeploymentVariable] = "plan-model",
            [AzureOpenAiAssistantPlanner.ApiVersionVariable] = "2024-10-21",
            [AzureOpenAiAssistantPlanner.KeyVariable] = NotAKey,
        };
        using (var planner = AzureOpenAiAssistantPlanner.FromEnvironment(name => complete.GetValueOrDefault(name)))
        {
            Assert.IsNotNull(planner);
        }

        foreach (var missing in complete.Keys)
        {
            var partial = new Dictionary<string, string?>(complete) { [missing] = null };
            Assert.IsNull(AzureOpenAiAssistantPlanner.FromEnvironment(name => partial.GetValueOrDefault(name)), $"no {missing}, no planner");
        }

        var plain = new Dictionary<string, string?>(complete) { [AzureOpenAiAssistantPlanner.EndpointVariable] = "http://example-resource.openai.azure.com/" };
        Assert.IsNull(AzureOpenAiAssistantPlanner.FromEnvironment(name => plain.GetValueOrDefault(name)), "an endpoint that is not https is no endpoint");
        Assert.AreEqual("no planner wired", V3AssistantRuntime.FromConfiguration("on", null, null, "unreviewed").OffBecause);
    }
}
