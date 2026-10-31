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
        var cappedBytes = await PostAsync(mount, ClockA, "ask", new { question = "Que dit l'article 1er de la loi du 10 août 1991 ?" });
        var capped = V3EnvelopeJson.ParseAndVerify(cappedBytes, V3OperationRegistry.Reviewed);
        Assert.AreEqual("assistant_v3_unavailable", capped.Result!.Value.GetProperty("presentation_result").GetString(), "R40: at a cap, ask answers assistant_v3_unavailable");
        Assert.AreEqual(V3CorpusMount.AskFailedReason, capped.Result.Value.GetProperty("containment").GetProperty("reason").GetString(),
            "while on, the card says the assistant could not answer this question, never that it is switched off");
        Assert.AreNotEqual(Encoding.UTF8.GetString(baseline), Encoding.UTF8.GetString(cappedBytes), "the off card and the on card differ");
        // The review of #1013, HIGH 1: on this path the question was read and sent masked to the processor; the card says so.
        Assert.AreEqual(JsonValueKind.True, capped.Result.Value.GetProperty("question_read").ValueKind, "while on, the question was read");
        Assert.AreEqual(V3CorpusMount.AskQuestionReadNote, capped.Result.Value.GetProperty("question_note").GetString());
        StringAssert.Contains(V3CorpusMount.AskQuestionReadNote, "sent to the configured processor");
        StringAssert.Contains(V3CorpusMount.AskQuestionReadNote, "not stored");

        // And the MCP tool list says what ask does while on, and the off sentence while off.
        var listing = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        var onList = Encoding.UTF8.GetString((await V3McpJsonRpc.HandleAsync(listing, new V3PlatformHost(), mount, () => ClockA, "assistant-mount-tests", CancellationToken.None))!);
        StringAssert.Contains(onList, "switched on as a labelled preview");
        mount.UseAssistant(V3AssistantRuntime.Off);
        var offList = Encoding.UTF8.GetString((await V3McpJsonRpc.HandleAsync(listing, new V3PlatformHost(), mount, () => ClockA, "assistant-mount-tests", CancellationToken.None))!);
        StringAssert.Contains(offList, "The assistant, switched off on Lex");
        Assert.IsFalse(offList.Contains("labelled preview", StringComparison.Ordinal));
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

    // ---- The gates over the real planner: opt-in, never in CI ----------------------------------------------------------

    /// <summary>
    /// The machine gates over the real planner (the owner's order of 2026-10-28), only when <c>LEX_ASSISTANT_GATES_LIVE</c> is
    /// exactly <c>1</c> and the deployment's variables are set (AzureOpenAiAssistantPlanner); otherwise inconclusive, so CI
    /// never calls a model. Each drafted case runs five times (R40). The report gives quote fidelity, non-template sentences,
    /// trace replay, latency p95 and cost per answer, and goes to <c>LEX_ASSISTANT_GATES_REPORT</c> or the temporary folder,
    /// never inside the repository. Over <c>LEX_ASSISTANT_GATES_MOUNT</c> (a mount directory) when set, else the fixture.
    /// The cases are drafted by an agent; not D4 or D1; not a switch-on gate.
    /// </summary>
    [TestMethod]
    public async Task TheGatesOverTheRealPlannerWhenAskedTo()
    {
        if (!string.Equals(System.Environment.GetEnvironmentVariable("LEX_ASSISTANT_GATES_LIVE"), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive("LEX_ASSISTANT_GATES_LIVE is not 1: the live gates call a model, and run only when asked to.");
        }

        using var planner = AzureOpenAiAssistantPlanner.FromEnvironment(System.Environment.GetEnvironmentVariable)
            ?? throw new AssertInconclusiveException("The deployment's variables are not set (LEX_ASSISTANT_ENDPOINT, _DEPLOYMENT, _API_VERSION, _KEY).");
        var report = System.Environment.GetEnvironmentVariable("LEX_ASSISTANT_GATES_REPORT") is { Length: > 0 } named
            ? Path.GetFullPath(named)
            : Path.Combine(Path.GetTempPath(), $"lex-assistant-gates-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.md");
        Assert.IsFalse(report.StartsWith(RepositoryRoot(), StringComparison.OrdinalIgnoreCase), "the report is written outside the repository");

        MountedFixture? fixture = null;
        var directory = System.Environment.GetEnvironmentVariable("LEX_ASSISTANT_GATES_MOUNT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            fixture = await MountedFixture.CreateAsync();
            directory = fixture.Directory;
        }

        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None) ?? throw new AssertFailedException("the mount did not open");
            var runtime = On(planner);
            mount.UseAssistant(runtime);
            const int Runs = 5;
            var cases = Cases();
            var rows = new List<string>();
            var latencies = new List<double>();
            var (quotes, quoteFailures, nonTemplate, replays, replayFailures, answered, failedClosed) = (0, 0, 0, 0, 0, 0, 0);
            foreach (var item in cases)
            {
                var id = item.GetProperty("id").GetString()!;
                var question = Question(item.GetProperty("question").GetString()!);
                var expect = item.GetProperty("expect");
                var expected = expect.TryGetProperty("verdict", out var verdictElement) ? verdictElement.GetString() : expect.GetProperty("outcome").GetString();
                var seen = new List<string>();
                for (var run = 0; run < Runs; run++)
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var envelope = V3EnvelopeJson.ParseAndVerify(await PostAsync(mount, DateTimeOffset.UtcNow, "ask", new { question }), V3OperationRegistry.Reviewed);
                    latencies.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    if (envelope.Result?.ObjectType == "handoff_card")
                    {
                        var presented = envelope.Result.Value.GetProperty("presentation_result").GetString()!;
                        seen.Add(presented);
                        failedClosed += presented == "assistant_v3_unavailable" ? 1 : 0;
                        continue;
                    }

                    answered++;
                    var dossier = DossierOf(envelope);
                    seen.Add(dossier.GetProperty("verdict").GetString()!);
                    quotes += Quotes(dossier).Length;
                    quoteFailures += (await QuoteFidelityFailures(mount, dossier)).Count;
                    nonTemplate += NonTemplateStrings(dossier, question).Count;
                    var trace = dossier.GetProperty("trace").GetRawText();
                    var first = V3AssistantPipeline.Replay(trace, mount.AssistantStepsOn, mount.AssistantEnvironmentAt(DateTimeOffset.UtcNow));
                    var second = V3AssistantPipeline.Replay(trace, mount.AssistantStepsOn, mount.AssistantEnvironmentAt(ClockB));
                    replays++;
                    var identical = first.Outcome is V3AskAnswered one && second.Outcome is V3AskAnswered two &&
                        one.Dossier.ToUtf8().SequenceEqual(two.Dossier.ToUtf8()) &&
                        JsonNode.DeepEquals(JsonNode.Parse(one.Dossier.ToUtf8()), JsonNode.Parse(dossier.GetRawText()));
                    replayFailures += identical ? 0 : 1;
                }

                rows.Add($"| {id} | {expected} | {string.Join(", ", seen)} |");
            }

            latencies.Sort();
            var p95 = latencies.Count == 0 ? 0 : latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
            var costPerAsk = runtime.SpentMicroEuros / Math.Max(1.0, cases.Length * Runs) / 1_000_000.0;
            var fidelity = quotes == 0 ? "no quotation" : $"{1.0 - ((double)quoteFailures / quotes):0.000} ({quoteFailures} failures in {quotes} quotations)";
            var text = new StringBuilder()
                .AppendLine("# The assistant's machine gates over the real planner")
                .AppendLine()
                .AppendLine($"{DraftedLabel}. Planner: {planner.PlannerId}. Mount: {(fixture is null ? directory : "the test fixture")}. {Runs} runs per case, {DateTimeOffset.UtcNow:u}.")
                .AppendLine()
                .AppendLine($"- Quote fidelity: {fidelity}; gate 1.0.")
                .AppendLine($"- Non-template sentences: {nonTemplate}; gate 0.")
                .AppendLine($"- Trace replay: {replays - replayFailures} of {replays} byte-identical; gate all.")
                .AppendLine($"- Latency to the full answer, p95: {p95:0} ms over {latencies.Count} asks (R40: 10 s).")
                .AppendLine($"- Cost per ask: {costPerAsk:0.0000} EUR from the responses' usage; {answered} answered, {failedClosed} failed closed on the switched-off card.")
                .AppendLine()
                .AppendLine("| Case | Drafted expectation | What the runs answered |")
                .AppendLine("|---|---|---|");
            foreach (var row in rows)
            {
                text.AppendLine(row);
            }

            await File.WriteAllTextAsync(report, text.ToString());
            Assert.AreEqual(0, quoteFailures, $"quote fidelity is 1.0 ({report})");
            Assert.AreEqual(0, nonTemplate, $"zero non-template sentences ({report})");
            Assert.AreEqual(0, replayFailures, $"every trace replays byte for byte ({report})");
        }
        finally
        {
            if (fixture is not null)
            {
                await fixture.DisposeAsync();
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lex.V3.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cannot locate the V3 repository root.");
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
        int[] limits = [V3AssistantRuntime.AskPerMinute];
        Assert.IsTrue(limits[0] < 60, "R40: below 60 a minute");
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
