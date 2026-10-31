using System.Diagnostics;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Platform.Assistant;

namespace Lex.V3.Api;

/// <summary>
/// The assistant behind <c>ask</c> (docs/ANSWER-DOSSIER-1.md): switched off by default; on only when every condition of
/// <see cref="V3AssistantRuntime.IsOn"/> holds (the switch, no off-switch, the Azure OpenAI deployment configured, and the
/// people's reviews done or the preview acknowledged). While it is off, <see cref="Ask"/> answers the switched-off card byte
/// for byte; this file adds what answers while it is on.
/// </summary>
internal sealed partial class V3CorpusMount
{
    private V3AssistantRuntime _assistant = V3AssistantRuntime.Off;

    /// <summary>The assistant runtime this mount's <c>ask</c> consults; <see cref="V3AssistantRuntime.Off"/> unless the server names another.</summary>
    internal V3AssistantRuntime Assistant => _assistant;

    /// <summary>Names the assistant runtime this mount's <c>ask</c> consults (Program.cs, once at start; tests).</summary>
    internal void UseAssistant(V3AssistantRuntime runtime) => _assistant = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <summary>
    /// The coverage answer with the assistant's capability flag while it is switched on: a member <c>assistant</c> naming its
    /// state, the operation that answers and the page under each interface language, and whether its answers are a preview.
    /// While it is off, the answer itself, untouched, byte for byte (the recorded base answers and every page stay as they are).
    /// The home page reads the flag from this answer, as it reads the coverage line, and shows the assistant's entry only then.
    /// </summary>
    internal JsonElement WithAssistantFlag(JsonElement coverage)
    {
        if (!_assistant.IsOn)
        {
            return coverage;
        }

        var answer = System.Text.Json.Nodes.JsonNode.Parse(coverage.GetRawText())!.AsObject();
        answer["assistant"] = new System.Text.Json.Nodes.JsonObject
        {
            ["state"] = "on",
            ["operation_id"] = "ask",
            ["page"] = V3WebRoot.AssistantPageName,
            ["preview"] = V3AnswerTemplates.Preview,
        };
        return JsonSerializer.SerializeToElement(answer);
    }

    /// <summary>
    /// What <c>ask</c> answers while the assistant is on, or null while it is off (the caller then answers the switched-off
    /// card). A question the assistant does not answer, a planner that fails, a plan the validator refuses, a gate that fails
    /// and the monthly cap all answer the switched-off card too; the per-client limit answers <c>rate_limited</c>.
    /// </summary>
    private V3PlatformOperationOutcome? AskAssistant(V3PlatformOperationRequest request, DateTimeOffset observedAt, string question)
    {
        var runtime = _assistant;
        if (!runtime.IsOn)
        {
            return null;
        }

        var wait = runtime.AdmitAsk();
        if (wait > 0)
        {
            return V3ApiHandler.RateLimitedOutcome(this, () => observedAt, request, wait);
        }

        if (runtime.CapReached)
        {
            runtime.Count(new V3AskPresented(V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.CapReached), TimeSpan.Zero);
            return null;
        }

        var started = Stopwatch.GetTimestamp();
        V3AskOutcome outcome;
        try
        {
            outcome = V3AssistantPipeline.Answer(
                new V3AssistantRequest(question),
                runtime.Budgeted(),
                (operationId, parametersJson) => RunAssistantStep(operationId, parametersJson, observedAt),
                AssistantEnvironment(observedAt),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Fails closed: the switched-off card, and nothing of the question or the failure is written anywhere.
            runtime.CountError("execution");
            return null;
        }

        runtime.Count(outcome, Stopwatch.GetElapsedTime(started));
        return outcome switch
        {
            V3AskAnswered answered => AnswerOf(request, observedAt, answered.Dossier),
            V3AskPresented { Result: V3AskPresentationResult.LocalizationUnavailable } => V3PlatformOperationOutcome.Success(
                Context("success", observedAt),
                AskLocalizationUnavailable(request, _reader!.SearchableLanguages())),
            _ => null,
        };
    }

    /// <summary>The steps of a plan as this mount runs them at <paramref name="observedAt"/> (for a replay and its tests).</summary>
    internal V3StepExecutor AssistantSteps(DateTimeOffset observedAt) => (operationId, parametersJson) => RunAssistantStep(operationId, parametersJson, observedAt);

    /// <summary>The steps of a plan as this mount runs them on a civil day, at noon UTC, which is that day in Luxembourg (R13).</summary>
    internal V3StepExecutor AssistantStepsOn(string day) => AssistantSteps(new DateTimeOffset(
        DateOnly.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

    /// <summary>What the pipeline knows about this mount at <paramref name="observedAt"/> (for a replay and its tests).</summary>
    internal V3AssistantEnvironment AssistantEnvironmentAt(DateTimeOffset observedAt) => AssistantEnvironment(observedAt);

    /// <summary>What the pipeline knows about this mount: today in Luxembourg (R13), the pinned snapshot, the search languages.</summary>
    private V3AssistantEnvironment AssistantEnvironment(DateTimeOffset observedAt) => new(
        LuxembourgCalendar.DateAt(observedAt).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        "corpus-" + _corpus.ArtifactRef.Sha256[..16],
        _corpus.ArtifactRef.Sha256,
        (_reader?.SearchableLanguages() ?? []).Concat(_europeReader?.SearchableLanguages() ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    /// <summary>
    /// One step of a plan, run as REST and MCP run it: the request validated against the operation's reviewed request schema,
    /// then the one dispatch (<see cref="V3ApiHandler.ExecuteFor"/>), so the out-of-scope list and the names record apply.
    /// </summary>
    private V3StepOutcome RunAssistantStep(string operationId, string parametersJson, DateTimeOffset observedAt)
    {
        var operation = V3OperationRegistry.Reviewed.Operation(operationId);
        using var requestDocument = JsonDocument.Parse(
            "{\"operation_id\":" + JsonSerializer.Serialize(operationId) + ",\"parameters\":" + parametersJson + "}");
        V3PlatformSchemaDocuments.Reviewed.ValidateRequest(operation, requestDocument.RootElement);
        var step = new V3PlatformOperationRequest(operation, requestDocument.RootElement.GetProperty("parameters"));
        var outcome = V3ApiHandler.ExecuteFor(this, () => observedAt, operationId)(step);
        return outcome.Result is { } result
            ? new V3StepOutcome(operationId, parametersJson, result.ObjectType, result.Verdict, result.Value.Clone(), null, null)
            : new V3StepOutcome(operationId, parametersJson, null, null, null, outcome.Refusal!.Code, outcome.Refusal.HelpfulPayload.Clone());
    }

    /// <summary>
    /// An answer as ask's envelope carries it: the result <c>answer_dossier</c> under its verdict; or, for refuse, the
    /// registry's own refusal for it, <c>advice_boundary</c>, whose payload carries the descriptive maximum (the law shown,
    /// verbatim and dated), the handoff and the whole answer. A refusal is never a result.
    /// </summary>
    private V3PlatformOperationOutcome AnswerOf(V3PlatformOperationRequest request, DateTimeOffset observedAt, V3AnswerDossier dossier)
    {
        using var document = JsonDocument.Parse(dossier.ToUtf8());
        if (dossier.Verdict != V3AssistantVerdict.Refuse)
        {
            return V3PlatformOperationOutcome.Success(
                Context("success", observedAt),
                new V3PlatformOperationResult(request, "answer_dossier", document.RootElement, ContractWire.NameOf(dossier.Verdict)));
        }

        var blocks = document.RootElement.GetProperty("parts").EnumerateArray().SelectMany(static part => part.GetProperty("blocks").EnumerateArray()).ToArray();
        using var payload = JsonSerializer.SerializeToDocument(new
        {
            descriptive_maximum = new
            {
                quotes = blocks.Where(static block => block.GetProperty("kind").GetString() == "quote").ToArray(),
            },
            handoff = blocks.Where(static block => block.GetProperty("kind").GetString() == "handoff").ToArray(),
            answer_dossier = document.RootElement,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, "advice_boundary", payload.RootElement));
    }

    /// <summary>
    /// The presentation result <c>localization_unavailable</c> (Decision 91; R43): the assistant answers in French and English
    /// only, said in both, with the deterministic operations that answer from held law. Never a refusal code.
    /// </summary>
    internal static V3PlatformOperationResult AskLocalizationUnavailable(V3PlatformOperationRequest request, IEnumerable<string> searchableLanguages)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var contained = JsonDocument.Parse(AskContained(request, searchableLanguages).Value.GetRawText());
        using var result = JsonSerializer.SerializeToDocument(new
        {
            presentation_result = "localization_unavailable",
            presentation_note = AskPresentationNote,
            answered_locales = new[] { "fr", "en" },
            sentences = new
            {
                fr = V3AnswerTemplates.Get(V3AnswerTemplates.PresentationLocalizationUnavailable, V3AnswerLocale.Fr).Text,
                en = V3AnswerTemplates.Get(V3AnswerTemplates.PresentationLocalizationUnavailable, V3AnswerLocale.En).Text,
            },
            question_read = true,
            question_note = "the question's language was read by a deterministic parser; the question is not stored or echoed",
            deterministic_actions = contained.RootElement.GetProperty("deterministic_actions"),
        });
        return new V3PlatformOperationResult(request, "handoff_card", result.RootElement, V3Verdicts.Point);
    }
}
