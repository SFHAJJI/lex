using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The assistant over a mounted corpus (docs/ANSWER-DOSSIER-1.md), with the deterministic test planner in place of a model,
/// and the machine gates' harness: quote fidelity by a checker that shares no code with the pipeline, zero non-template
/// sentences by a walk of its own, and byte-identical replay under two clocks, each with a deliberate-failure control. The
/// replay set is drafted by an agent; not D4 or D1; not a switch-on gate.
/// </summary>
[TestClass]
public sealed class V3AssistantMountTests
{
    private const string DraftedLabel = "drafted by an agent; not D4 or D1; not a switch-on gate";
    private static readonly DateTimeOffset ClockA = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClockB = new(2026, 10, 28, 20, 15, 0, TimeSpan.Zero);

    /// <summary>A runtime that answers, for the tests only: the switch on, a deterministic planner, the people's reviews not required.</summary>
    private static V3AssistantRuntime On(IAssistantPlanner? planner = null) =>
        new(switchedOn: true, operatorOff: false, planner ?? new AssistantRuleTestPlanner(), requireReviewedWording: false);

    private static async Task<(MountedFixture Fixture, V3CorpusMount Mount)> MountAsync()
    {
        var fixture = await MountedFixture.CreateAsync();
        var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        return (fixture, mount);
    }

    private static async Task<byte[]> PostAsync(V3CorpusMount mount, DateTimeOffset clock, string operation, object parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "assistant-mount-tests";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), () => clock, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        return ((MemoryStream)context.Response.Body).ToArray();
    }

    private static string Question(string template) => template
        .Replace("{email}", "nobody" + "@" + "example.invalid", StringComparison.Ordinal)
        .Replace("{long}", new string('a', V3AssistantParsers.MaximumQuestionLength + 1), StringComparison.Ordinal);

    private static JsonElement[] Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assistant-drafted-replay-set.json")));
        Assert.AreEqual(DraftedLabel, document.RootElement.GetProperty("label").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(static item => item.Clone()).ToArray();
    }

    // ---- Off: the switched-off card, byte for byte ------------------------------------------------------------------

    [TestMethod]
    public async Task WhileTheSwitchIsOffAskAnswersTheSwitchedOffCardByteForByteHoweverItIsOff()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        var baseline = await PostAsync(mount, ClockA, "ask", new { question });
        Assert.AreSame(V3AssistantRuntime.Off, mount.Assistant, "a mount starts with the assistant off");

        // Every way of being off answers the same bytes: the switch unset, set but no planner (every deployment of this
        // code), set with the operator off-switch, set with the people's reviews missing, and the monthly cap reached.
        var planner = new AssistantRuleTestPlanner();
        var offs = new (string Why, V3AssistantRuntime Runtime)[]
        {
            ("the switch not exactly on", V3AssistantRuntime.FromConfiguration("On", null, planner)),
            ("no planner wired", V3AssistantRuntime.FromConfiguration("on", null, null)),
            ("the operator off-switch", V3AssistantRuntime.FromConfiguration("on", "1", planner)),
            ("the people's reviews not done", V3AssistantRuntime.FromConfiguration("on", null, planner)),
            ("a breaker tripped", Tripped(On(planner))),
        };
        foreach (var (why, runtime) in offs)
        {
            Assert.IsFalse(runtime.IsOn, why);
            mount.UseAssistant(runtime);
            CollectionAssert.AreEqual(baseline, await PostAsync(mount, ClockA, "ask", new { question }), why);
        }

        Assert.AreEqual(0, planner.Calls, "no planner is consulted while the assistant is off");
        var card = V3EnvelopeJson.ParseAndVerify(baseline, V3OperationRegistry.Reviewed);
        Assert.AreEqual("handoff_card", card.Result!.ObjectType);
        Assert.AreEqual("The assistant is switched off on Lex; search, reading and the other operations work.",
            card.Result.Value.GetProperty("containment").GetProperty("reason").GetString());
        Assert.AreEqual(
            V3AnswerTemplates.Get(V3AnswerTemplates.PresentationAssistantUnavailable, V3AnswerLocale.En).Text,
            card.Result.Value.GetProperty("containment").GetProperty("reason").GetString(),
            "the card's sentence is the presentation template's English, whose French is reviewed with the set");
    }

    private static V3AssistantRuntime Tripped(V3AssistantRuntime runtime)
    {
        runtime.Trip();
        return runtime;
    }

    [TestMethod]
    public async Task AtTheMonthlyCapAskAnswersTheSwitchedOffCardAndSearchKeepsWorking()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        var baseline = await PostAsync(mount, ClockA, "ask", new { question = "Que dit l'article 1er de la loi du 10 août 1991 ?" });
        var runtime = On();
        while (runtime.TryReserve())
        {
            runtime.Settle(V3AssistantRuntime.PerAnswerCeilingMicroEuros);
        }

        Assert.IsTrue(runtime.CapReached);
        mount.UseAssistant(runtime);
        CollectionAssert.AreEqual(baseline, await PostAsync(mount, ClockA, "ask", new { question = "Que dit l'article 1er de la loi du 10 août 1991 ?" }));
        var search = V3EnvelopeJson.ParseAndVerify(await PostAsync(mount, ClockA, "search", new { query = "assemblée générale", language = "fra" }), V3OperationRegistry.Reviewed);
        Assert.IsNull(search.Refusal, "search keeps working at the assistant's cap (R40)");
        Assert.IsTrue(runtime.Telemetry.ContainsKey("outcome:cap_reached"));
    }

    // ---- The drafted replay set ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task EveryDraftedCaseAnswersWhatItExpectsAndPassesTheHarnessGates()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        mount.UseAssistant(On());
        var failures = new List<string>();
        foreach (var item in Cases())
        {
            var id = item.GetProperty("id").GetString()!;
            Assert.AreEqual(DraftedLabel, item.GetProperty("label").GetString(), id);
            var question = Question(item.GetProperty("question").GetString()!);
            var expect = item.GetProperty("expect");
            var envelope = V3EnvelopeJson.ParseAndVerify(await PostAsync(mount, ClockA, "ask", new { question }), V3OperationRegistry.Reviewed);
            var outcome = expect.GetProperty("outcome").GetString();
            if (outcome is "assistant_v3_unavailable" or "localization_unavailable")
            {
                if (envelope.Result?.Value.GetProperty("presentation_result").GetString() != outcome)
                {
                    failures.Add($"{id}: presented {envelope.Result?.Value.GetRawText()[..Math.Min(120, envelope.Result?.Value.GetRawText().Length ?? 0)]}, not {outcome}");
                }

                Assert.AreEqual(V3Verdicts.Point, envelope.Verdict, $"{id}: a presentation result rides the point slot and is no verdict");
                continue;
            }

            var dossier = DossierOf(envelope);
            var verdict = dossier.GetProperty("verdict").GetString();
            if (verdict != expect.GetProperty("verdict").GetString())
            {
                failures.Add($"{id}: verdict {verdict}, expected {expect.GetProperty("verdict").GetString()}");
            }

            if (dossier.GetProperty("answer_locale").GetString() != expect.GetProperty("locale").GetString())
            {
                failures.Add($"{id}: answered in {dossier.GetProperty("answer_locale").GetString()}");
            }

            var quotes = Quotes(dossier).Length;
            if (quotes < expect.GetProperty("quotes_at_least").GetInt32())
            {
                failures.Add($"{id}: {quotes} quotations");
            }

            failures.AddRange(NonTemplateStrings(dossier, question).Select(text => $"{id}: a visible string no template, quote or span holds: {text}"));
            failures.AddRange((await QuoteFidelityFailures(mount, dossier)).Select(failure => $"{id}: {failure}"));
            Assert.AreEqual(verdict == "refuse", envelope.Refusal?.Code == "advice_boundary", $"{id}: refuse travels as advice_boundary, and only refuse");
            Assert.IsFalse(dossier.GetRawText().Contains("nobody@", StringComparison.Ordinal) && id != "fr-masked-address", id);
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    private static JsonElement DossierOf(V3Envelope envelope) =>
        envelope.Refusal is { } refusal
            ? refusal.HelpfulPayload.GetProperty("answer_dossier")
            : envelope.Result!.Value;

    private static JsonElement[] Quotes(JsonElement dossier) =>
        dossier.GetProperty("parts").EnumerateArray()
            .SelectMany(static part => part.GetProperty("blocks").EnumerateArray())
            .Where(static block => block.GetProperty("kind").GetString() == "quote")
            .ToArray();

    // ---- The harness: quote fidelity, independent of the pipeline -----------------------------------------------------

    /// <summary>
    /// Quote fidelity, gate 1.0 (36 §1.6(c)): each quotation is re-read through the API (evidence_bundle, as a reader would
    /// ask it, with the parameters the trace names), and this code hashes the article itself and compares the characters.
    /// Nothing here calls the pipeline or its gates.
    /// </summary>
    private static async Task<List<string>> QuoteFidelityFailures(V3CorpusMount mount, JsonElement dossier)
    {
        var failures = new List<string>();
        var steps = dossier.GetProperty("trace").GetProperty("steps").EnumerateArray().ToArray();
        foreach (var quote in Quotes(dossier))
        {
            var stepId = quote.GetProperty("step_id").GetString();
            var step = steps.SingleOrDefault(candidate => candidate.GetProperty("step_id").GetString() == stepId);
            if (step.ValueKind != JsonValueKind.Object || step.GetProperty("operation_id").GetString() != "evidence_bundle")
            {
                failures.Add($"quotation {quote.GetProperty("quote_id").GetString()} names no evidence_bundle step");
                continue;
            }

            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(step.GetProperty("parameters").GetRawText())!;
            var bundle = V3EnvelopeJson.ParseAndVerify(await PostAsync(mount, ClockB, "evidence_bundle", parameters), V3OperationRegistry.Reviewed);
            var text = bundle.Result!.Value.GetProperty("states").EnumerateArray()
                .SelectMany(static state => state.GetProperty("articles").EnumerateArray())
                .Where(article => article.GetProperty("publisher_id").GetString() == quote.GetProperty("anchor").GetString())
                .Select(static article => article.GetProperty("text").GetString())
                .SingleOrDefault();
            if (text is null)
            {
                failures.Add($"quotation {quote.GetProperty("quote_id").GetString()}: its article is not in the re-read bundle");
                continue;
            }

            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var offset = quote.GetProperty("offset").GetInt32();
            var length = quote.GetProperty("length").GetInt32();
            if (hash != quote.GetProperty("article_text_sha256").GetString())
            {
                failures.Add($"quotation {quote.GetProperty("quote_id").GetString()}: cites {quote.GetProperty("article_text_sha256").GetString()}, the text hashes {hash}");
            }

            if (offset < 0 || offset + length > text.Length || text.Substring(offset, length) != quote.GetProperty("text").GetString())
            {
                failures.Add($"quotation {quote.GetProperty("quote_id").GetString()}: not the text's characters at its offset");
            }
        }

        return failures;
    }

    // ---- The harness: zero non-template sentences, by a walk of its own -----------------------------------------------

    private static readonly string[] Mois = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];
    private static readonly string[] Months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>
    /// Every member named <c>text</c> outside the trace and the refusal payloads that is not a claim this code renders again
    /// from the template's own wording and the claim's facts, nor a span equal to the question's characters, nor a quotation
    /// (which the fidelity checker checks). Its rendering is written here again, not called.
    /// </summary>
    private static List<string> NonTemplateStrings(JsonElement dossier, string question)
    {
        var question0 = question.Normalize(NormalizationForm.FormC);
        var found = new List<string>();
        void Walk(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item);
                }

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (element.TryGetProperty("text", out var text))
            {
                var kind = element.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() : null;
                var ok = kind switch
                {
                    "claim" => Rendered(element) == text.GetString(),
                    "question_span" => question0.Substring(element.GetProperty("start").GetInt32(), element.GetProperty("end").GetInt32() - element.GetProperty("start").GetInt32()) == text.GetString(),
                    "quote" => true,
                    _ => false,
                };
                if (!ok)
                {
                    found.Add(text.GetRawText());
                }
            }

            foreach (var member in element.EnumerateObject())
            {
                if (member.Name is not ("trace" or "payload" or "facts"))
                {
                    Walk(member.Value);
                }
            }
        }

        Walk(dossier);
        return found;
    }

    private static string? Rendered(JsonElement claim)
    {
        var locale = claim.GetProperty("locale").GetString() == "fr" ? V3AnswerLocale.Fr : V3AnswerLocale.En;
        var id = claim.GetProperty("template_id").GetString()!;
        if (!V3AnswerTemplates.IsDefined(id))
        {
            return null;
        }

        var wording = V3AnswerTemplates.Get(id, locale).Text;
        var facts = claim.GetProperty("facts").EnumerateArray().ToDictionary(
            static fact => fact.GetProperty("name").GetString()!,
            static fact => (Kind: fact.GetProperty("kind").GetString()!, Value: fact.GetProperty("value").GetString()!));
        return Regex.Replace(wording, @"\{([a-z][a-z0-9_]*)\}", match =>
        {
            var (kind, value) = facts[match.Groups[1].Value];
            return kind switch
            {
                "calendar_date" => Words(value, locale),
                "anchor" => (value.StartsWith("art_", StringComparison.Ordinal) ? value[4..] : value).Replace('_', ' '),
                "language_code" => (value, locale) switch
                {
                    ("fra", V3AnswerLocale.Fr) => "français",
                    ("fra", _) => "French",
                    ("eng", V3AnswerLocale.Fr) => "anglais",
                    ("eng", _) => "English",
                    ("deu", V3AnswerLocale.Fr) => "allemand",
                    ("deu", _) => "German",
                    ("ltz", V3AnswerLocale.Fr) => "luxembourgeois",
                    _ => "Luxembourgish",
                },
                _ => value,
            };
        });
    }

    private static string Words(string iso, V3AnswerLocale locale)
    {
        var date = DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return locale == V3AnswerLocale.Fr
            ? (date.Day == 1 ? "1er" : date.Day.ToString(CultureInfo.InvariantCulture)) + " " + Mois[date.Month - 1] + " " + date.Year.ToString(CultureInfo.InvariantCulture)
            : date.Day.ToString(CultureInfo.InvariantCulture) + " " + Months[date.Month - 1] + " " + date.Year.ToString(CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public async Task TheHarnessGatesFailOnPurpose()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        mount.UseAssistant(On());
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        var envelope = V3EnvelopeJson.ParseAndVerify(await PostAsync(mount, ClockA, "ask", new { question }), V3OperationRegistry.Reviewed);
        var dossier = JsonNode.Parse(DossierOf(envelope).GetRawText())!;
        Assert.AreEqual(0, NonTemplateStrings(JsonSerializer.SerializeToElement(dossier), question).Count);
        Assert.AreEqual(0, (await QuoteFidelityFailures(mount, JsonSerializer.SerializeToElement(dossier))).Count);

        // A sentence written at a call site: one word of a claim changed.
        var claimed = dossier.DeepClone();
        var label = claimed["label"]!;
        label["text"] = label["text"]!.GetValue<string>() + " (certainly)";
        Assert.AreEqual(1, NonTemplateStrings(JsonSerializer.SerializeToElement(claimed), question).Count, "an edited claim is caught");
        Assert.AreEqual(1, V3AnswerGates.NonTemplateStrings(Encoding.UTF8.GetBytes(claimed.ToJsonString())), "and the code path's gate catches it too");

        // Prose added in a block of its own.
        var prose = dossier.DeepClone();
        prose["parts"]![0]!["blocks"]!.AsArray().Add(new JsonObject { ["kind"] = "note", ["text"] = "You will probably win." });
        Assert.AreEqual(1, NonTemplateStrings(JsonSerializer.SerializeToElement(prose), question).Count, "free prose is caught");

        // A quotation with one character changed.
        var misquoted = dossier.DeepClone();
        var quote = misquoted["parts"]![0]!["blocks"]!.AsArray().First(block => block!["kind"]!.GetValue<string>() == "quote")!;
        var quoted = quote["text"]!.GetValue<string>();
        quote["text"] = quoted[..^1] + (quoted[^1] == 'x' ? "y" : "x");
        Assert.AreEqual(1, (await QuoteFidelityFailures(mount, JsonSerializer.SerializeToElement(misquoted))).Count, "a misquotation is caught");

        // A span that is not the question's characters.
        var spanned = dossier.DeepClone();
        spanned["parts"]![0]!["blocks"]!.AsArray().Add(new JsonObject { ["kind"] = "question_span", ["start"] = 0, ["end"] = 3, ["text"] = "Can" });
        Assert.AreEqual(1, NonTemplateStrings(JsonSerializer.SerializeToElement(spanned), question).Count, "a forged span is caught");
    }

    // ---- The harness: byte-identical replay under two clocks -----------------------------------------------------------

    [TestMethod]
    public async Task ATraceReplaysToTheSameBytesUnderAnotherClockAndAMutatedTraceDoesNot()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        var planner = new AssistantRuleTestPlanner();
        foreach (var item in Cases().Where(static item => item.GetProperty("expect").GetProperty("outcome").GetString() == "answered"))
        {
            var id = item.GetProperty("id").GetString()!;
            var question = Question(item.GetProperty("question").GetString()!);
            var original = V3AssistantPipeline.Answer(new V3AssistantRequest(question), planner, mount.AssistantSteps(ClockA), mount.AssistantEnvironmentAt(ClockA), CancellationToken.None);
            var answered = original as V3AskAnswered ?? throw new AssertFailedException($"{id}: {original.Reason}");
            var bytes = answered.Dossier.ToUtf8();
            using var document = JsonDocument.Parse(bytes);
            var trace = document.RootElement.GetProperty("trace").GetRawText();

            var replayed = V3AssistantPipeline.Replay(trace, mount.AssistantStepsOn, mount.AssistantEnvironmentAt(ClockB));
            Assert.IsFalse(replayed.SnapshotMismatch, id);
            CollectionAssert.AreEqual(bytes, ((V3AskAnswered)replayed.Outcome!).Dossier.ToUtf8(), $"{id}: the replay is byte-identical");
        }

        // The control: a trace whose plan reads another span replays to other bytes, and another snapshot is refused.
        var first = (V3AskAnswered)V3AssistantPipeline.Answer(
            new V3AssistantRequest("Que dit l'article 1er de la loi du 10 août 1991 ?"), planner, mount.AssistantSteps(ClockA), mount.AssistantEnvironmentAt(ClockA), CancellationToken.None);
        var traceNode = JsonNode.Parse(first.Dossier.ToUtf8())!["trace"]!;
        var mutated = traceNode.DeepClone();
        mutated["question"] = "Que dit l'article 5 de la loi du 10 août 1991 ?";
        var other = V3AssistantPipeline.Replay(mutated.ToJsonString(), mount.AssistantStepsOn, mount.AssistantEnvironmentAt(ClockB));
        Assert.IsTrue(other.Outcome is not V3AskAnswered again || !again.Dossier.ToUtf8().SequenceEqual(first.Dossier.ToUtf8()), "a mutated trace does not replay to the same bytes");
        var elsewhere = traceNode.DeepClone();
        elsewhere["snapshot"]!["snapshot_sha256"] = new string('0', 64);
        Assert.IsTrue(V3AssistantPipeline.Replay(elsewhere.ToJsonString(), mount.AssistantStepsOn, mount.AssistantEnvironmentAt(ClockB)).SnapshotMismatch);
    }

    // ---- Nothing stored (R32), and what the planner is given (R33) ------------------------------------------------------

    private sealed class RecordingPlanner(IAssistantPlanner inner) : IAssistantPlanner
    {
        public List<V3PlannerInput> Inputs { get; } = [];

        public string PlannerId => inner.PlannerId;

        public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            return inner.Plan(input, cancellationToken);
        }
    }

    [TestMethod]
    public async Task AnAskBodyReachesNoLogAndNoCounterAndThePlannerSeesOnlyTheMaskedQuestion()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        var planner = new RecordingPlanner(new AssistantRuleTestPlanner());
        var runtime = On(planner);
        mount.UseAssistant(runtime);
        var address = "canary" + "@" + "example.invalid";
        var canary = "zq-canary-" + Guid.NewGuid().ToString("N");
        var question = $"Mon adresse est {address}, que dit l'article 1er de la loi du 10 août 1991 ? {canary}";

        var console = new StringWriter();
        var errors = new StringWriter();
        var (stdout, stderr) = (Console.Out, Console.Error);
        Console.SetOut(console);
        Console.SetError(errors);
        try
        {
            _ = await PostAsync(mount, ClockA, "ask", new { question });
            _ = await PostAsync(mount, ClockA, "ask", new { question = canary + " Puis-je être licencié ?" });
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }

        Assert.IsFalse(console.ToString().Contains(canary, StringComparison.Ordinal) || errors.ToString().Contains(canary, StringComparison.Ordinal), "no ask body reaches the console");
        Assert.IsTrue(runtime.Telemetry.Keys.All(static key => Regex.IsMatch(key, "^(outcome|latency|verdict|locale|refusal|presentation|error):[a-z0-9_]+$")), string.Join(", ", runtime.Telemetry.Keys));
        Assert.IsFalse(runtime.Telemetry.Keys.Any(key => key.Contains("canary", StringComparison.Ordinal)));

        // R33: the planner is given the masked question and opaque references, and nothing else.
        Assert.AreEqual(2, planner.Inputs.Count);
        Assert.IsFalse(planner.Inputs[0].MaskedQuestion.Contains('@'), "the e-mail address is masked before any planner sees it");
        Assert.AreEqual(question.Length, planner.Inputs[0].MaskedQuestion.Length, "the mask keeps every offset");
        CollectionAssert.AreEquivalent(
            new[] { "AnswerLocale", "MaskedQuestion", "Operations", "References", "SearchLanguages" },
            typeof(V3PlannerInput).GetProperties().Select(static property => property.Name).ToArray(),
            "the planner's input holds no address, user agent, cookie or history");
    }

    // ---- ask's own per-client limit (R40) -------------------------------------------------------------------------------

    [TestMethod]
    public async Task AsksOwnPerClientLimitIsBelowSixtyAMinuteAndAnswersRateLimited()
    {
        var (fixture, mount) = await MountAsync();
        await using var cleanup = fixture;
        using var mounted = mount;
        Assert.IsTrue(V3AssistantRuntime.AskPerMinute < 60, "R40: below 60 a minute");
        mount.UseAssistant(On());
        var request = new V3PlatformOperationRequest(
            V3OperationRegistry.Reviewed.Operation("ask"),
            JsonSerializer.SerializeToElement(new { question = "Que dit l'article 1er de la loi du 10 août 1991 ?" }));
        using (V3AssistantRuntime.EnterClient(UInt128.One))
        {
            for (var at = 0; at < V3AssistantRuntime.AskPerMinute; at++)
            {
                Assert.AreNotEqual("rate_limited", mount.Ask(request, ClockA).Refusal?.Code, $"ask {at + 1} is admitted");
            }

            var over = mount.Ask(request, ClockA);
            Assert.AreEqual("rate_limited", over.Refusal?.Code, "the next ask in the minute is refused rate_limited");
        }

        using (V3AssistantRuntime.EnterClient(UInt128.MaxValue))
        {
            Assert.AreNotEqual("rate_limited", mount.Ask(request, ClockA).Refusal?.Code, "another client has its own bucket");
        }
    }
}
