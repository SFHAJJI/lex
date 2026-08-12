using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Mcp;

namespace Lex.Ask;

public enum LegalResultClass
{
    Navigate,
    ExactText,
    Comparison,
    Timeline,
    Ranking,
    Inventory,
    Verification,
}

public enum ApplicationDisposition
{
    Clarification,
    Gap,
    LegalBoundary,
}

public enum LegalOutcome
{
    NotEvaluated,
    Succeeded,
    SucceededEmpty,
    NeedsClarification,
    NotAvailable,
    NotComparable,
    NotFound,
    InvalidRequest,
    LegalBoundary,
}

public enum TransportOutcome
{
    Completed,
    Cancelled,
    TimedOut,
    UpstreamFailed,
    OverQuota,
}

public enum SupportingCallRole
{
    WorkResolution,
    AnchorResolution,
}

public enum OperationEffect
{
    Provision,
    Diff,
    History,
    Timeline,
    Ranking,
    InForce,
    CitedBy,
    Coverage,
    Workspace,
    Verification,
    Gap,
}

public enum OperationExecutionState
{
    Pending,
    Completed,
}

public static class LegalOperationPolicy
{
    public static LegalOutcome OutcomeForStatus(string status) => status switch
    {
        McpStatus.Ok => LegalOutcome.Succeeded,
        McpStatus.NoResult or McpStatus.NoChangesInPeriod => LegalOutcome.SucceededEmpty,
        McpStatus.ProfilesDiffer => LegalOutcome.NotComparable,
        McpStatus.UnknownWork or McpStatus.UnknownAnchor
            or McpStatus.UnknownPublisher => LegalOutcome.NotFound,
        McpStatus.NoVersionForDate or McpStatus.AnchorNotInVersion
            or McpStatus.NoProvisionHistory or McpStatus.TextNotAvailable
            or McpStatus.TextWithheld or McpStatus.NoCorpusMounted => LegalOutcome.NotAvailable,
        _ => throw new InvalidDataException($"Unknown MCP legal status '{status}'."),
    };

    public static LegalResultClass ResultClassFor(string tool) => tool switch
    {
        "search" or "navigate" => LegalResultClass.Navigate,
        "as_of" => LegalResultClass.ExactText,
        "diff" => LegalResultClass.Comparison,
        "timeline" or "article_history" => LegalResultClass.Timeline,
        "changes_in_period" => LegalResultClass.Ranking,
        "in_force_on" or "coverage" or "cited_by" => LegalResultClass.Inventory,
        "provenance" => LegalResultClass.Verification,
        _ => throw new InvalidDataException($"Unknown MCP tool '{tool}'."),
    };

    public static bool RequiresWorkResolution(string tool) => tool is
        "navigate" or "as_of" or "diff" or "timeline" or "article_history" or "cited_by"
        or "provenance";

    public static OperationEffect PrimaryEffectFor(string tool) => tool switch
    {
        "search" or "navigate" => OperationEffect.Workspace,
        "as_of" => OperationEffect.Provision,
        "diff" => OperationEffect.Diff,
        "timeline" => OperationEffect.Timeline,
        "article_history" => OperationEffect.History,
        "changes_in_period" => OperationEffect.Ranking,
        "in_force_on" => OperationEffect.InForce,
        "coverage" => OperationEffect.Coverage,
        "cited_by" => OperationEffect.CitedBy,
        "provenance" => OperationEffect.Verification,
        _ => throw new InvalidDataException($"Unknown MCP tool '{tool}'."),
    };

    public static bool AllowsEffect(string tool, OperationEffect effect) =>
        tool switch
        {
            "search" or "navigate" => effect is OperationEffect.Workspace or OperationEffect.Gap,
            "as_of" => effect is OperationEffect.Provision or OperationEffect.Gap,
            "diff" => effect is OperationEffect.Diff or OperationEffect.Gap,
            "timeline" => effect is OperationEffect.Timeline or OperationEffect.Gap,
            "article_history" => effect is OperationEffect.History or OperationEffect.Gap,
            "changes_in_period" => effect is OperationEffect.Ranking or OperationEffect.Workspace
                or OperationEffect.Gap,
            "in_force_on" => effect is OperationEffect.InForce or OperationEffect.Workspace
                or OperationEffect.Gap,
            "coverage" => effect is OperationEffect.Coverage or OperationEffect.Gap,
            "cited_by" => effect is OperationEffect.CitedBy or OperationEffect.Gap,
            "provenance" => effect is OperationEffect.Verification or OperationEffect.Gap,
            _ => false,
        };

    public static bool AllowsSupportingCall(
        SupportingCallRole role,
        string tool,
        JsonObject arguments) => role switch
        {
            SupportingCallRole.WorkResolution => tool == "search"
                && arguments["query"] is JsonValue query
                && query.TryGetValue<string>(out var value)
                && !string.IsNullOrWhiteSpace(value),
            SupportingCallRole.AnchorResolution => tool == "as_of"
                && arguments["mode"]?.GetValue<string>() == "outline"
                && arguments["work"] is JsonValue work
                && work.TryGetValue<string>(out var workValue)
                && !string.IsNullOrWhiteSpace(workValue),
            _ => false,
        };

    public static LegalOutcome OutcomeForResult(string declaredStatus, JsonNode payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var declared = OutcomeForStatus(declaredStatus);
        var statuses = ObservedStatuses(payload).ToArray();
        if (statuses.Length == 0) return declared;

        var actual = DominantOutcome(statuses.Select(OutcomeForStatus));
        if (actual != declared)
            throw new InvalidDataException(
                $"Declared MCP status '{declaredStatus}' contradicts the result payload.");
        return actual;
    }

    public static string StatusForResult(JsonNode payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload is JsonArray { Count: 0 }) return McpStatus.NoResult;
        var statuses = ObservedStatuses(payload).ToArray();
        if (statuses.Length == 0) return McpStatus.Ok;
        var outcome = DominantOutcome(statuses.Select(OutcomeForStatus));
        return statuses.First(status => OutcomeForStatus(status) == outcome);
    }

    private static LegalOutcome DominantOutcome(IEnumerable<LegalOutcome> values)
    {
        var observed = values.Distinct().ToArray();
        return observed.Any(outcome => outcome == LegalOutcome.NotAvailable)
            ? LegalOutcome.NotAvailable
            : observed.Any(outcome => outcome == LegalOutcome.NotComparable)
                ? LegalOutcome.NotComparable
                : observed.Any(outcome => outcome == LegalOutcome.NotFound)
                    ? LegalOutcome.NotFound
                    : observed.Any(outcome => outcome == LegalOutcome.Succeeded)
                        ? LegalOutcome.Succeeded
                        : LegalOutcome.SucceededEmpty;
    }

    private static IEnumerable<string> ObservedStatuses(JsonNode payload)
    {
        if (payload is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>())
                if ((item["envelope"]?["status"] ?? item["status"]) is JsonValue status
                    && status.TryGetValue<string>(out var value))
                    yield return value;
            yield break;
        }

        if (payload is JsonObject itemObject
            && (itemObject["envelope"]?["status"] ?? itemObject["status"]) is JsonValue itemStatus
            && itemStatus.TryGetValue<string>(out var itemValue))
            yield return itemValue;
    }
}

public sealed record RequestedOperation
{
    public const int MaximumIdentifierLength = 128;
    public const int MaximumArgumentBytes = 32_768;

    private RequestedOperation() { }

    public string OperationId { get; private init; } = "";
    public int UserOrder { get; private init; }
    public string Tool { get; private init; } = "";
    public LegalResultClass? ResultClass { get; private init; }
    public ApplicationDisposition? Disposition { get; private init; }
    public JsonElement Arguments { get; private init; }
    public bool RequiresWorkResolution { get; private init; }
    public ImmutableArray<SupportingCallRole> SupportingCalls { get; private init; }
    public ImmutableArray<OperationEffect> Effects { get; private init; }

    public static RequestedOperation Create(
        string operationId,
        int userOrder,
        string tool,
        JsonObject arguments,
        bool requiresWorkResolution,
        IEnumerable<SupportingCallRole> supportingCalls,
        IEnumerable<OperationEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(supportingCalls);
        ArgumentNullException.ThrowIfNull(effects);
        if (string.IsNullOrWhiteSpace(operationId)
            || operationId.Length > MaximumIdentifierLength)
            throw new InvalidDataException(
                $"An operation id must contain 1 to {MaximumIdentifierLength} characters.");
        if (userOrder < 0)
            throw new InvalidDataException("Operation order cannot be negative.");
        var resultClass = LegalOperationPolicy.ResultClassFor(tool);
        var expectedResolution = LegalOperationPolicy.RequiresWorkResolution(tool);
        if (requiresWorkResolution != expectedResolution)
            throw new InvalidDataException(
                $"Tool '{tool}' requiresWorkResolution must be {expectedResolution.ToString().ToLowerInvariant()}.");

        var support = supportingCalls.Distinct().ToImmutableArray();
        if (!requiresWorkResolution && support.Contains(SupportingCallRole.WorkResolution))
            throw new InvalidDataException($"Tool '{tool}' cannot request work resolution.");

        var requestedEffects = effects.Distinct().ToImmutableArray();
        if (requestedEffects.IsDefaultOrEmpty)
            throw new InvalidDataException("An operation must declare at least one typed effect.");
        if (requestedEffects.Any(effect => !LegalOperationPolicy.AllowsEffect(tool, effect)))
            throw new InvalidDataException($"Tool '{tool}' declares an incompatible typed effect.");
        var primaryEffect = LegalOperationPolicy.PrimaryEffectFor(tool);
        if (!requestedEffects.Contains(primaryEffect) || !requestedEffects.Contains(OperationEffect.Gap))
            throw new InvalidDataException(
                $"Tool '{tool}' must declare its '{primaryEffect}' result and typed gap effects.");

        var argumentBytes = JsonSerializer.SerializeToUtf8Bytes(arguments);
        if (argumentBytes.Length > MaximumArgumentBytes)
            throw new InvalidDataException(
                $"Operation arguments cannot exceed {MaximumArgumentBytes} UTF-8 bytes.");

        return new RequestedOperation
        {
            OperationId = operationId,
            UserOrder = userOrder,
            Tool = tool,
            ResultClass = resultClass,
            Disposition = null,
            Arguments = JsonSerializer.Deserialize<JsonElement>(argumentBytes),
            RequiresWorkResolution = requiresWorkResolution,
            SupportingCalls = support,
            Effects = requestedEffects,
        };
    }

    public static RequestedOperation CreateApplication(
        string operationId,
        int userOrder,
        ApplicationDisposition disposition,
        JsonObject arguments)
    {
        if (disposition is not (ApplicationDisposition.Clarification
                or ApplicationDisposition.Gap or ApplicationDisposition.LegalBoundary))
            throw new InvalidDataException($"Unsupported application disposition '{disposition}'.");
        if (string.IsNullOrWhiteSpace(operationId)
            || operationId.Length > MaximumIdentifierLength || userOrder < 0)
            throw new InvalidDataException("Application operation identity is invalid.");
        var action = disposition switch
        {
            ApplicationDisposition.Clarification => "clarification",
            ApplicationDisposition.LegalBoundary => "legal_boundary",
            _ => "gap",
        };
        var normalized = OperationArguments.Normalize(action, arguments);
        var argumentBytes = JsonSerializer.SerializeToUtf8Bytes(normalized);
        if (argumentBytes.Length > MaximumArgumentBytes)
            throw new InvalidDataException(
                $"Operation arguments cannot exceed {MaximumArgumentBytes} UTF-8 bytes.");
        return new RequestedOperation
        {
            OperationId = operationId,
            UserOrder = userOrder,
            Tool = action,
            ResultClass = null,
            Disposition = disposition,
            Arguments = JsonSerializer.Deserialize<JsonElement>(argumentBytes),
            RequiresWorkResolution = false,
            SupportingCalls = [],
            Effects = [OperationEffect.Gap],
        };
    }

    public static RequestedOperation CreatePlanned(
        string operationId,
        int userOrder,
        string tool,
        JsonObject arguments,
        CorpusVocabulary? vocabulary = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var normalized = OperationArguments.Normalize(tool, arguments, vocabulary);
        var requiresResolution = LegalOperationPolicy.RequiresWorkResolution(tool);
        var supporting = requiresResolution
            ? new List<SupportingCallRole> { SupportingCallRole.WorkResolution }
            : [];
        if (requiresResolution
            && normalized["article_number"] is JsonValue
            && tool is "as_of" or "diff" or "article_history")
            supporting.Add(SupportingCallRole.AnchorResolution);
        var effects = new List<OperationEffect>
        {
            LegalOperationPolicy.PrimaryEffectFor(tool),
            OperationEffect.Gap,
        };
        if (tool is "changes_in_period" or "in_force_on")
            effects.Add(OperationEffect.Workspace);
        return Create(operationId, userOrder, tool, normalized, requiresResolution,
            supporting, effects);
    }
}

public sealed class OperationPlan
{
    public const int MaximumOperations = 8;

    private OperationPlan(
        string requestId,
        string locale,
        ImmutableArray<RequestedOperation> operations,
        bool synthesisRequested)
    {
        RequestId = requestId;
        Locale = locale;
        Operations = operations;
        SynthesisRequested = synthesisRequested;
    }

    public string RequestId { get; }
    public string Locale { get; }
    public ImmutableArray<RequestedOperation> Operations { get; }
    public bool SynthesisRequested { get; }

    public static OperationPlan Create(
        string requestId,
        string locale,
        IEnumerable<RequestedOperation> operations,
        bool synthesisRequested = false)
    {
        if (string.IsNullOrWhiteSpace(requestId)
            || requestId.Length > RequestedOperation.MaximumIdentifierLength)
            throw new InvalidDataException(
                $"A request id must contain 1 to {RequestedOperation.MaximumIdentifierLength} characters.");
        if (locale is not ("en" or "fr"))
            throw new InvalidDataException("Locale must be 'en' or 'fr'.");
        ArgumentNullException.ThrowIfNull(operations);

        var ordered = operations.OrderBy(item => item.UserOrder).ToImmutableArray();
        if (ordered.IsDefaultOrEmpty || ordered.Length > MaximumOperations)
            throw new InvalidDataException($"A plan must contain 1 to {MaximumOperations} operations.");
        if (ordered.Select(item => item.OperationId).Any(string.IsNullOrWhiteSpace)
            || ordered.Select(item => item.OperationId).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("Operation ids must be non-empty and unique.");
        if (ordered.Where((item, index) => item.UserOrder != index).Any())
            throw new InvalidDataException("Operation order must be contiguous and start at zero.");

        foreach (var operation in ordered)
        {
            if (operation.Disposition is null)
            {
                if (LegalOperationPolicy.ResultClassFor(operation.Tool) != operation.ResultClass
                    || LegalOperationPolicy.RequiresWorkResolution(operation.Tool)
                        != operation.RequiresWorkResolution)
                    throw new InvalidDataException(
                        $"Operation '{operation.OperationId}' does not match tool policy.");
            }
            else if (operation.ResultClass is not null || operation.RequiresWorkResolution
                     || operation.Effects.Length != 1
                     || operation.Effects[0] != OperationEffect.Gap)
                throw new InvalidDataException(
                    $"Application operation '{operation.OperationId}' does not match disposition policy.");
        }

        if (synthesisRequested && ordered.All(operation => operation.Disposition is not null))
            throw new InvalidDataException(
                "A requested synthesis requires at least one authoritative legal operation.");
        if (ordered.Any(operation => operation.Disposition == ApplicationDisposition.Clarification)
            && ordered.Length != 1)
            throw new InvalidDataException(
                "A planning clarification must be the only operation and run before legal state calls.");
        return new OperationPlan(requestId, locale, ordered, synthesisRequested);
    }

    public static OperationPlan FromPlannerOutput(
        string requestId,
        string locale,
        JsonArray operations,
        bool synthesisRequested = false,
        CorpusVocabulary? vocabulary = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count is 0 or > MaximumOperations)
            throw new InvalidDataException($"A plan must contain 1 to {MaximumOperations} operations.");
        var requested = operations.Select((node, index) =>
        {
            var item = node as JsonObject
                ?? throw new InvalidDataException("Every planned operation must be an object.");
            var tool = item["tool"] is JsonValue toolValue
                       && toolValue.TryGetValue<string>(out var parsedTool)
                ? parsedTool
                : throw new InvalidDataException("Every planned operation must name a string tool or action.");
            var arguments = item["arguments"] as JsonObject
                ?? throw new InvalidDataException("Every planned operation must contain object arguments.");
            if (tool == "legal_boundary")
                return RequestedOperation.CreateApplication(
                    $"{requestId}:op-{index + 1}", index,
                    ApplicationDisposition.LegalBoundary, arguments);
            if (tool == "clarification")
                return RequestedOperation.CreateApplication(
                    $"{requestId}:op-{index + 1}", index,
                    ApplicationDisposition.Clarification, arguments);
            return RequestedOperation.CreatePlanned($"{requestId}:op-{index + 1}", index,
                tool, arguments, vocabulary);
        }).ToArray();
        return Create(requestId, locale, requested, synthesisRequested);
    }

}

public sealed record OperationResult(
    string OperationId,
    int UserOrder,
    LegalResultClass? ResultClass,
    ApplicationDisposition? Disposition,
    LegalOutcome LegalOutcome,
    TransportOutcome TransportOutcome,
    ImmutableArray<OperationEffect> Effects,
    JsonElement? Payload);

public sealed class OperationExecution
{
    private OperationResult? _result;

    public OperationExecution(RequestedOperation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public RequestedOperation Request { get; }
    public OperationExecutionState State =>
        Volatile.Read(ref _result) is null ? OperationExecutionState.Pending : OperationExecutionState.Completed;
    public OperationResult? Result => Volatile.Read(ref _result);

    public OperationResult Complete(string status, JsonNode payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var candidate = LegalResult(status, payload);
        return CompleteCore(candidate);
    }

    public OperationResult CompleteTransport(TransportOutcome outcome)
    {
        if (outcome == TransportOutcome.Completed)
            throw new InvalidDataException("A completed transport requires an MCP legal status.");
        var candidate = TransportResult(outcome);
        return CompleteCore(candidate);
    }

    public OperationResult CompleteLegal(LegalOutcome outcome, JsonObject? payload = null)
    {
        if (outcome == LegalOutcome.NotEvaluated)
            throw new InvalidDataException("NotEvaluated requires a non-completed transport outcome.");
        if (outcome is not (LegalOutcome.NeedsClarification or LegalOutcome.InvalidRequest
                or LegalOutcome.LegalBoundary)
            && !(outcome == LegalOutcome.Succeeded
                && Request.ResultClass == LegalResultClass.Navigate))
            throw new InvalidDataException(
                $"Outcome '{outcome}' requires an authoritative tool result for '{Request.ResultClass}'.");
        return CompleteCore(BuildResult(
            Request.OperationId,
            Request.UserOrder,
            Request.ResultClass,
            Request.Disposition,
            outcome,
            TransportOutcome.Completed,
            payload is null ? null : JsonSerializer.SerializeToElement(payload)));
    }

    private OperationResult CompleteCore(OperationResult result)
    {
        if (Interlocked.CompareExchange(ref _result, result, null) is not null)
            throw new InvalidOperationException($"Operation '{Request.OperationId}' is already complete.");
        return result;
    }

    internal bool TryCompleteTransport(TransportOutcome outcome, out OperationResult result)
    {
        if (outcome == TransportOutcome.Completed)
            throw new InvalidDataException("Pending operations require a non-completed transport outcome.");
        return TryCompleteCore(TransportResult(outcome), out result);
    }

    internal bool TryComplete(string status, JsonNode payload, out OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return TryCompleteCore(LegalResult(status, payload), out result);
    }

    internal bool TryCompleteLegal(
        LegalOutcome outcome,
        JsonObject? payload,
        out OperationResult result)
    {
        if (outcome is not (LegalOutcome.NeedsClarification or LegalOutcome.InvalidRequest
                or LegalOutcome.LegalBoundary))
            throw new InvalidDataException(
                $"Outcome '{outcome}' is not an application-owned pending disposition.");
        return TryCompleteCore(BuildResult(
            Request.OperationId,
            Request.UserOrder,
            Request.ResultClass,
            Request.Disposition,
            outcome,
            TransportOutcome.Completed,
            payload is null ? null : JsonSerializer.SerializeToElement(payload)),
            out result);
    }

    private OperationResult LegalResult(string status, JsonNode payload) => BuildResult(
        Request.OperationId,
        Request.UserOrder,
        Request.ResultClass,
        Request.Disposition,
        LegalOperationPolicy.OutcomeForResult(status, payload),
        TransportOutcome.Completed,
        JsonSerializer.SerializeToElement(payload));

    private OperationResult TransportResult(TransportOutcome outcome) => BuildResult(
        Request.OperationId,
        Request.UserOrder,
        Request.ResultClass,
        Request.Disposition,
        LegalOutcome.NotEvaluated,
        outcome,
        null);

    private OperationResult BuildResult(
        string operationId,
        int userOrder,
        LegalResultClass? resultClass,
        ApplicationDisposition? disposition,
        LegalOutcome legalOutcome,
        TransportOutcome transportOutcome,
        JsonElement? payload)
    {
        var effects = EffectsFor(legalOutcome, transportOutcome);
        if (effects.Any(effect => !Request.Effects.Contains(effect)))
            throw new InvalidDataException(
                $"Operation '{Request.OperationId}' produced an effect outside its frozen plan.");
        return new OperationResult(
            operationId, userOrder, resultClass, disposition, legalOutcome, transportOutcome,
            effects, payload);
    }

    private ImmutableArray<OperationEffect> EffectsFor(
        LegalOutcome legalOutcome,
        TransportOutcome transportOutcome)
    {
        if (transportOutcome != TransportOutcome.Completed)
            return [OperationEffect.Gap];
        if (legalOutcome is LegalOutcome.Succeeded or LegalOutcome.SucceededEmpty)
            return Request.Disposition is null
                ? [LegalOperationPolicy.PrimaryEffectFor(Request.Tool)]
                : [OperationEffect.Gap];
        if (legalOutcome == LegalOutcome.NotComparable
            && Request.ResultClass == LegalResultClass.Comparison)
            return [LegalOperationPolicy.PrimaryEffectFor(Request.Tool), OperationEffect.Gap];
        return [OperationEffect.Gap];
    }

    private bool TryCompleteCore(OperationResult candidate, out OperationResult result)
    {
        var existing = Interlocked.CompareExchange(ref _result, candidate, null);
        result = existing ?? candidate;
        return existing is null;
    }
}

public sealed record SupportingCallObservation(
    string OperationId,
    SupportingCallRole Role,
    string Tool,
    string Status,
    JsonElement Payload);

public sealed class OperationRun
{
    private readonly List<SupportingCallObservation> _supportingCalls = [];

    private OperationRun(OperationPlan plan)
    {
        Plan = plan;
        Executions = plan.Operations.Select(item => new OperationExecution(item)).ToImmutableArray();
    }

    public OperationPlan Plan { get; }
    public ImmutableArray<OperationExecution> Executions { get; }
    public IReadOnlyList<SupportingCallObservation> SupportingCalls => _supportingCalls.AsReadOnly();

    public static OperationRun Start(OperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new OperationRun(plan);
    }

    public void ObserveSupportingCall(
        string operationId,
        SupportingCallRole role,
        string tool,
        JsonObject arguments,
        string status,
        JsonNode payload)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(payload);
        var execution = Executions.SingleOrDefault(item => item.Request.OperationId == operationId)
            ?? throw new InvalidDataException($"Unknown operation '{operationId}'.");
        if (!execution.Request.SupportingCalls.Contains(role))
            throw new InvalidDataException(
                $"Supporting role '{role}' was not declared for operation '{operationId}'.");
        if (!LegalOperationPolicy.AllowsSupportingCall(role, tool, arguments))
            throw new InvalidDataException(
                $"Tool '{tool}' cannot serve supporting role '{role}'.");
        _ = LegalOperationPolicy.OutcomeForResult(status, payload);
        _supportingCalls.Add(new SupportingCallObservation(
            operationId,
            role,
            tool,
            status,
            JsonSerializer.SerializeToElement(payload)));
    }

    public IReadOnlyList<OperationResult> CompletePending(TransportOutcome outcome)
    {
        if (outcome == TransportOutcome.Completed)
            throw new InvalidDataException("Pending operations require a non-completed transport outcome.");
        var completed = new List<OperationResult>();
        foreach (var execution in Executions)
            if (execution.TryCompleteTransport(outcome, out var result))
                completed.Add(result);
        return completed.OrderBy(item => item.UserOrder).ToArray();
    }

    public IReadOnlyList<OperationResult> CompletePendingLegal(LegalOutcome outcome)
    {
        var completed = new List<OperationResult>();
        foreach (var execution in Executions)
            if (execution.TryCompleteLegal(outcome, null, out var result))
                completed.Add(result);
        return completed.OrderBy(item => item.UserOrder).ToArray();
    }

    public bool TryComplete(
        string operationId,
        string status,
        JsonNode payload,
        out OperationResult result)
    {
        var execution = Executions.SingleOrDefault(item => item.Request.OperationId == operationId)
            ?? throw new InvalidDataException($"Unknown operation '{operationId}'.");
        return execution.TryComplete(status, payload, out result);
    }
}
