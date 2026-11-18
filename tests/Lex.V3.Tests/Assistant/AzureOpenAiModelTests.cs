using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.Contracts.Platform.Assistant.Bricks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The model the bricks ask, over a fake HTTP handler: no request leaves the test. What a call sends (the masked question
/// with where its words and dates stand, the step's instruction and its strict form, and nothing else of the reader, R33),
/// what it answers, what it costs, and how every failure (a timeout, a 429, an answer cut short, a reply outside its form)
/// leaves the question to code: nothing is repaired, and the answer says no model took part.
/// </summary>
[TestClass]
public sealed class AzureOpenAiModelTests
{
    private const string NotAKey = "test-value-not-a-key";
    private static readonly Uri Endpoint = new("https://example-resource.openai.azure.com/");

    private sealed class FakeHandler(Func<string, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, string? Key, string Body)> Seen { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.TryGetValues("api-key", out var values) ? values.Single() : null;
            var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            Seen.Add((request.RequestUri!, key, body));
            return respond(body, cancellationToken);
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

    private static AzureOpenAiModel Model(FakeHandler handler, TimeSpan? timeout = null) =>
        new(Endpoint, "plan-model", "2024-10-21", NotAKey, handler, timeout: timeout);

    /// <summary>The question a request carries, as the model is sent it.</summary>
    private static string QuestionOf(string body)
    {
        using var sent = JsonDocument.Parse(body);
        using var user = JsonDocument.Parse(sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        return user.RootElement.GetProperty("question").GetString()!;
    }

    /// <summary>A description in its closed form: the question cites an article, from "l'article" to the year of the act's date.</summary>
    private static string Description(string masked)
    {
        var start = masked.IndexOf("l'article", StringComparison.Ordinal);
        var end = masked.IndexOf("1991", StringComparison.Ordinal) + 4;
        return new JsonObject
        {
            ["parts"] = new JsonArray(new JsonObject
            {
                ["asks"] = "article_or_act_cited",
                ["own_case"] = false,
                ["span"] = new JsonArray(0, masked.Length),
                ["citation"] = new JsonArray(start, end),
                ["act"] = new JsonArray(-1, -1),
                ["words"] = new JsonArray(-1, -1),
                ["language"] = "fra",
                ["date"] = "today",
                ["range"] = string.Empty,
                ["act_ref"] = string.Empty,
                ["concept"] = string.Empty,
            }),
        }.ToJsonString();
    }

    private static V3AskOutcome Ask(string question, AzureOpenAiModel model, BricksFixture.Executor executor) =>
        V3AssistantPipeline.Answer(new V3AssistantRequest(question), new V4BricksPlanner(new BricksFixture.Corpus(), model), executor.Run, BricksFixture.Environment, CancellationToken.None);

    [TestMethod]
    public void ItSendsTheMaskedQuestionTheInstructionAndTheStrictFormAndNothingElseOfTheReader()
    {
        var address = "someone" + "@" + "example.invalid";
        var question = $"Mon adresse est {address}, mon IBAN LU28 0019 4006 4475 0000, tél. +352 621 123 456, matricule 19900101123 : que dit l'article 1er de la loi du 10 août 1991 ?";
        var handler = new FakeHandler((body, _) => Completion(Description(QuestionOf(body))));
        using var model = Model(handler);
        var executor = new BricksFixture.Executor();
        var outcome = Ask(question, model, executor);
        Assert.IsInstanceOfType<V3AskAnswered>(outcome, outcome.Reason.ToString());
        using var parameters = JsonDocument.Parse(executor.Calls.Single().Parameters);
        Assert.AreEqual("l'article 1er de la loi du 10 août 1991", parameters.RootElement.GetProperty("query").GetString(), "code searched the span the model pointed at");

        var (uri, key, body) = handler.Seen.Single();
        Assert.AreEqual("https://example-resource.openai.azure.com/openai/deployments/plan-model/chat/completions?api-version=2024-10-21", uri.ToString());
        Assert.AreEqual(NotAKey, key, "the key travels as the api-key header, and only there");
        using var sent = JsonDocument.Parse(body);
        var messages = sent.RootElement.GetProperty("messages");
        Assert.AreEqual(V4QuestionCall.Prompt, messages[0].GetProperty("content").GetString(), "the versioned instruction of the step");
        Assert.AreEqual(0, sent.RootElement.GetProperty("temperature").GetInt32(), "the steadiest reply the model writes");
        var format = sent.RootElement.GetProperty("response_format");
        Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
        Assert.IsTrue(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        using var user = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
        CollectionAssert.AreEquivalent(
            new[] { "question", "question_length", "words", "references", "search_languages", "concepts" },
            user.RootElement.EnumerateObject().Select(static member => member.Name).ToArray(),
            "R33: of the reader, only the masked question; beside it, where its words and dates stand and lists Lex holds");
        var masked = user.RootElement.GetProperty("question").GetString()!;
        foreach (var identifier in new[] { address, "LU28", "621 123 456", "19900101123" })
        {
            Assert.IsFalse(body.Contains(identifier, StringComparison.Ordinal), $"{identifier} reached the request");
        }

        StringAssert.Contains(masked, "que dit l'article 1er de la loi du 10 août 1991", "the law's words are sent");
        Assert.AreEqual(masked.Length, user.RootElement.GetProperty("question_length").GetInt32());
        foreach (var word in user.RootElement.GetProperty("words").EnumerateArray())
        {
            var (start, end) = (word.GetProperty("start").GetInt32(), word.GetProperty("end").GetInt32());
            Assert.AreEqual(masked[start..end], word.GetProperty("w").GetString(), "each word is the question's own run at its own positions");
            Assert.IsFalse(word.GetProperty("w").GetString()!.Contains('•'), "no masked run is offered as a word");
        }

        Assert.AreEqual(question.Length, masked.Length);
        Assert.IsFalse(body.Contains("User-Agent", StringComparison.OrdinalIgnoreCase) || body.Contains("cookie", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ItsCostIsTheResponsesUsageAndItCountsTowardTheCaps()
    {
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        var handler = new FakeHandler((body, _) => Completion(Description(QuestionOf(body)), prompt: 1800, completion: 200));
        using var model = Model(handler);
        var call = V4QuestionCall.Call(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"], BricksFixture.Today), V4Dictionary.Insurance);
        var reply = model.Complete(call, CancellationToken.None);
        Assert.AreEqual((1800 * AzureOpenAiModel.DefaultInputPrice) + (200 * AzureOpenAiModel.DefaultOutputPrice), reply.CostMicroEuros);
        Assert.AreEqual((1800, 200), (reply.InputTokens, reply.OutputTokens));

        var planner = new V4BricksPlanner(new BricksFixture.Corpus(), model);
        var runtime = new V3AssistantRuntime(true, false, planner, requireReviewedWording: false);
        var output = runtime.Budgeted().Plan(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"], BricksFixture.Today), CancellationToken.None);
        Assert.AreEqual(reply.CostMicroEuros, output.CostMicroEuros, "what the bricks report is what their calls cost");
        Assert.AreEqual(output.CostMicroEuros, runtime.SpentMicroEuros, "the usage is counted toward the month");
        StringAssert.StartsWith(planner.PlannerId, "lex-v4-bricks/plan-model/prompt-");
        Assert.IsFalse(planner.PlannerId.Contains(NotAKey, StringComparison.Ordinal));
        Assert.AreEqual("plan-model", model.ModelId);
    }

    [TestMethod]
    public void ATimeoutA429AndACutShortAnswerLeaveTheQuestionToCodeWithNoKeyInAnyMessage()
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
            ("an answer cut short", new FakeHandler((body, _) => Completion(Description(QuestionOf(body)), finish: "length"))),
            ("an answer filtered", new FakeHandler((body, _) => Completion(Description(QuestionOf(body)), finish: "content_filter"))),
        ];
        var call = V4QuestionCall.Call(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"], BricksFixture.Today), V4Dictionary.Insurance);
        foreach (var (why, handler) in failures)
        {
            using var model = Model(handler, TimeSpan.FromMilliseconds(300));
            var thrown = Assert.ThrowsExactly<Exception>(() => _ = Catch(() => model.Complete(call, CancellationToken.None)), why);
            Assert.IsFalse(thrown.ToString().Contains(NotAKey, StringComparison.Ordinal), $"{why}: the key is in no message");

            // The call failed: code searches the reader's own words, and the answer says that no model took part in it.
            var executor = new BricksFixture.Executor();
            var outcome = Ask(question, model, executor);
            var answered = outcome as V3AskAnswered ?? throw new AssertFailedException($"{why}: {outcome.Reason}");
            Assert.AreEqual("search", executor.Calls.Single().Operation, why);
            CollectionAssert.Contains(answered.Dossier.Notices.Select(static notice => notice.TemplateId).ToArray(), V3AnswerTemplates.NoticeNoModel, why);
            Assert.IsFalse(Encoding.UTF8.GetString(answered.Dossier.ToUtf8()).Contains(NotAKey, StringComparison.Ordinal), why);
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
            throw new Exception("the model's call failed", exception);
        }
    }

    [TestMethod]
    public void AReplyOutsideItsClosedFormIsNotRepairedAndNoModelWordReachesTheAnswer()
    {
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        foreach (var (why, content) in new[]
                 {
                     ("malformed JSON", "{\"parts\":["),
                     ("prose instead of a description", "You can certainly be dismissed under article 1er (zebra-marker)."),
                     ("a plan instead of a description", "{\"schema\":\"lex-v3-assistant-plan/1\",\"parts\":[],\"steps\":[],\"follow_ups\":[]}"),
                     ("a kind outside the list", Description(question).Replace("article_or_act_cited", "evidence_bundle", StringComparison.Ordinal)),
                 })
        {
            var handler = new FakeHandler((_, _) => Completion(content));
            using var model = Model(handler);
            var executor = new BricksFixture.Executor();
            var answered = Ask(question, model, executor) as V3AskAnswered ?? throw new AssertFailedException($"{why}: not answered");
            Assert.AreEqual(1, handler.Seen.Count, $"{why}: asked once");
            Assert.AreEqual("search", executor.Calls.Single().Operation, why);
            var json = Encoding.UTF8.GetString(answered.Dossier.ToUtf8());
            Assert.IsFalse(json.Contains("zebra-marker", StringComparison.Ordinal) || json.Contains("dismissed", StringComparison.Ordinal), $"{why}: nothing the model wrote is in the answer");
            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("refused", document.RootElement.GetProperty("trace").GetProperty("plan").GetProperty("bricks").GetProperty("question_call").GetString(), why);
        }
    }

    [TestMethod]
    public void TheWordsAreTheQuestionsRunsBetweenWhiteSpaceAtTheirUtf16PositionsWithoutMaskedRuns()
    {
        CollectionAssert.AreEqual(
            new[] { ("Que", 0, 3), ("dit", 4, 7), ("l'article", 8, 17), ("1er", 18, 21), ("août", 23, 27) },
            V4QuestionCall.WordsOf("Que dit l'article 1er  août ••• ").ToArray(),
            "two spaces, a trailing space and a masked run");
        Assert.AreEqual(0, V4QuestionCall.WordsOf("   ").Count);
        var astral = "art. 𝟙 x";
        var words = V4QuestionCall.WordsOf(astral);
        Assert.AreEqual((astral[5..7], 5, 7), words[1], "positions count UTF-16 units, a surrogate pair is two");
    }

    [TestMethod]
    public void EveryFormAModelIsHeldToIsStrict()
    {
        void Strict(JsonNode? node, string at)
        {
            if (node is JsonObject schema)
            {
                if (schema["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == "object")
                {
                    Assert.IsFalse(schema["additionalProperties"]!.GetValue<bool>(), $"{at}: closed");
                    CollectionAssert.AreEquivalent(
                        ((JsonObject)schema["properties"]!).Select(static property => property.Key).ToArray(),
                        ((JsonArray)schema["required"]!).Select(static required => required!.GetValue<string>()).ToArray(),
                        $"{at}: every property required");
                }

                foreach (var (member, child) in schema)
                {
                    Strict(child, at + "/" + member);
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

        const string question = "l'assureur peut résilier le contrat de façon unilatérale ?";
        var row = V4QuestionParser.Parse(question, V3AnswerLocale.Fr, V4Dictionary.Insurance, V4CorpusIndex.Insurance);
        var lines = new[] { BricksFixture.Line("art_39#3") };
        var retrieval = V4Retrieval.Run(new V4Brief(["resiliation"], "assureur", "rule", row.Concepts.Concat(row.Roles).Select(static read => read.Keyword).ToList()), BricksFixture.Tables, V4Dictionary.Insurance);
        V4ModelCall[] calls =
        [
            V4QuestionCall.Call(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"], BricksFixture.Today), V4Dictionary.Insurance),
            V4Generation.ArbiterCall(row, "rule", BricksFixture.Tables, retrieval.Candidates.Take(3).ToList(), V4Dictionary.Insurance),
            V4Generation.ExtractionCall(row, "rule", BricksFixture.Tables, lines, [], V4Dictionary.Insurance),
        ];
        CollectionAssert.AreEqual(new[] { "question", "arbiter", "extraction" }, calls.Select(static call => call.Step).ToArray());
        foreach (var call in calls)
        {
            Strict(JsonNode.Parse(call.SchemaJson), call.Step);
            StringAssert.Matches(call.SchemaName, new System.Text.RegularExpressions.Regex("^lex_v4_[a-z]+$"));
        }
    }

    [TestMethod]
    public void WithNoEndpointThereIsNoModelAndTheAssistantStaysOff()
    {
        var complete = new Dictionary<string, string?>
        {
            [AzureOpenAiModel.EndpointVariable] = Endpoint.ToString(),
            [AzureOpenAiModel.DeploymentVariable] = "plan-model",
            [AzureOpenAiModel.ApiVersionVariable] = "2024-10-21",
            [AzureOpenAiModel.KeyVariable] = NotAKey,
        };
        using (var model = AzureOpenAiModel.FromEnvironment(name => complete.GetValueOrDefault(name)))
        {
            Assert.IsNotNull(model);
        }

        foreach (var missing in complete.Keys)
        {
            var partial = new Dictionary<string, string?>(complete) { [missing] = null };
            Assert.IsNull(AzureOpenAiModel.FromEnvironment(name => partial.GetValueOrDefault(name)), $"no {missing}, no model");
        }

        var plain = new Dictionary<string, string?>(complete) { [AzureOpenAiModel.EndpointVariable] = "http://example-resource.openai.azure.com/" };
        Assert.IsNull(AzureOpenAiModel.FromEnvironment(name => plain.GetValueOrDefault(name)), "an endpoint that is not https is no endpoint");
        Assert.AreEqual("no planner wired", V3AssistantRuntime.FromConfiguration("on", null, null, "unreviewed").OffBecause);
    }
}
