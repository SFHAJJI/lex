using System.Text.Json.Nodes;

namespace Lex.RetrievalExperiment;

public sealed class DirectTurnTrace(string conversationInput, int maximumCalls = 2)
{
    private static readonly AsyncLocal<DirectTurnTrace?> CurrentTrace = new();

    public static DirectTurnTrace? Current
    {
        get => CurrentTrace.Value;
        set => CurrentTrace.Value = value;
    }

    public string ConversationInput { get; } = conversationInput;
    public int MaximumCalls { get; } = maximumCalls;
    public List<JsonObject> Calls { get; } = [];
    public string? SelectedWork { get; private set; }
    public string? SelectedAnchor { get; private set; }
    public string? SelectedDate { get; private set; }

    public int Begin(JsonObject request)
    {
        if (Calls.Count >= MaximumCalls)
            throw new InvalidOperationException($"Direct research tool budget of {MaximumCalls} is exhausted.");
        Calls.Add(new JsonObject
        {
            ["status"] = "started",
            ["request"] = request.DeepClone(),
        });
        return Calls.Count - 1;
    }

    public void Complete(int index, JsonObject evidence)
    {
        var completed = (JsonObject)evidence.DeepClone();
        completed["request"] = Calls[index]["request"]?.DeepClone();
        Calls[index] = completed;
        if (evidence["status"]?.GetValue<string>() != "validated_candidate") return;
        SelectedWork = evidence["selected_work"]?.GetValue<string>();
        SelectedAnchor = evidence["selected_anchor"]?.GetValue<string>();
        SelectedDate = evidence["date"]?.GetValue<string>();
    }

    public JsonObject Fail(int index, Exception error)
    {
        var failure = new JsonObject
        {
            ["status"] = "tool_error",
            ["error_type"] = error.GetType().Name,
            ["error"] = error.Message,
            ["request"] = Calls[index]["request"]?.DeepClone(),
        };
        Calls[index] = (JsonObject)failure.DeepClone();
        return failure;
    }
}

public sealed class DirectResearchTool(ResearchPlanExecutor executor, DateOnly today)
{
    public string SearchLegalEvidence(
        string subject_query_fr,
        string subject_query_en,
        string provision_query_fr,
        string provision_query_en,
        string? jurisdiction = null,
        string? as_of = null)
    {
        var trace = DirectTurnTrace.Current
            ?? throw new InvalidOperationException("A direct-agent turn trace is required.");
        var request = new JsonObject
        {
            ["subject_query_fr"] = subject_query_fr,
            ["subject_query_en"] = subject_query_en,
            ["provision_query_fr"] = provision_query_fr,
            ["provision_query_en"] = provision_query_en,
            ["jurisdiction"] = jurisdiction,
            ["as_of"] = as_of,
        };
        var callIndex = trace.Begin(request);
        try
        {
            var plan = new LegalResearchPlan(
                "search",
                "Direct Agent Framework research call",
                NormalizeJurisdiction(jurisdiction),
                NullIfBlank(as_of),
                [new(CompactQuery(subject_query_fr, 240), "fr"), new(CompactQuery(subject_query_en, 240), "en")],
                [new(CompactQuery(provision_query_fr, 140), "fr"), new(CompactQuery(provision_query_en, 140), "en")],
                null);
            var execution = executor.Execute(plan, today, trace.ConversationInput);
            var compact = CompactEvidence(execution, plan.AsOf ?? today.ToString("yyyy-MM-dd"));
            trace.Complete(callIndex, compact);
            return compact.ToJsonString();
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        {
            return trace.Fail(callIndex, error).ToJsonString();
        }
    }

    private static string? NormalizeJurisdiction(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is null or "" or "BOTH" ? null : normalized;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string CompactQuery(string value, int maximum)
    {
        var compact = string.Join(' ', value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (compact.Length == 0) throw new ArgumentException("A tool query is required.", nameof(value));
        if (compact.Length <= maximum) return compact;
        var boundary = compact.LastIndexOf(' ', maximum - 1);
        return compact[..(boundary > 0 ? boundary : maximum)];
    }

    internal static JsonObject CompactEvidence(JsonObject execution, string date)
    {
        var compact = new JsonObject
        {
            ["status"] = execution["status"]?.DeepClone(),
            ["selected_work"] = execution["selected_work"]?.DeepClone(),
            ["selected_anchor"] = execution["selected_anchor"]?.DeepClone(),
            ["date"] = date,
            ["validation_status"] = execution["validation_status"]?.DeepClone(),
            ["candidate_works"] = new JsonArray(
                execution["candidate_works"]?.AsArray().Take(3)
                    .Select(item => item?.DeepClone()).Where(item => item is not null).ToArray()
                ?? []),
        };
        var validation = execution["tool_calls"]?.AsArray().OfType<JsonObject>()
            .LastOrDefault(call => call["tool"]?.GetValue<string>() == "as_of")?["result"];
        if (validation is null) return compact;
        var document = validation["document"]?.AsObject();
        var provision = validation["provisions"]?.AsArray().OfType<JsonObject>().FirstOrDefault();
        compact["evidence"] = new JsonObject
        {
            ["timeline_semantics"] = validation["envelope"]?["timeline_semantics"]?.DeepClone(),
            ["lex_id"] = document?["lex_id"]?.DeepClone(),
            ["title"] = document?["title"]?.DeepClone(),
            ["language"] = document?["language"]?.DeepClone(),
            ["valid_from"] = document?["valid_from"]?.DeepClone(),
            ["valid_to"] = document?["valid_to"]?.DeepClone(),
            ["source_uri"] = document?["source_uri"]?.DeepClone(),
            ["anchor"] = provision?["anchor"]?.DeepClone(),
            ["number"] = provision?["num"]?.DeepClone(),
            ["heading"] = provision?["heading"]?.DeepClone(),
            ["text_sha256"] = provision?["text_sha256"]?.DeepClone(),
            ["text"] = Bound(provision?["text"]?.GetValue<string>(), 6_000),
        };
        return compact;
    }

    private static string? Bound(string? value, int maximum) => value is null || value.Length <= maximum
        ? value
        : string.Concat(value.AsSpan(0, maximum), "…");
}
