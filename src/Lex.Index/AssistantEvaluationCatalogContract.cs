using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lex.Evaluation;

/// <summary>
/// Canonical closed schema and semantics for the reviewed assistant evaluation catalog.
/// Both the evaluation runner and the public evidence reader call this lower-layer contract.
/// </summary>
public static partial class AssistantEvaluationCatalogContract
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> Tools =
    [
        "search", "navigate", "as_of", "diff", "timeline", "article_history",
        "changes_in_period", "in_force_on", "coverage", "cited_by", "provenance",
        "legal_boundary",
    ];

    private static readonly HashSet<string> Outcomes =
    [
        "succeeded", "succeeded_empty", "needs_clarification", "not_available",
        "not_comparable", "not_found", "invalid_request", "legal_boundary",
    ];

    private static readonly HashSet<string> Effects =
    [
        "provision", "diff", "history", "timeline", "ranking", "in_force",
        "cited_by", "coverage", "workspace", "verification", "gap",
    ];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    public static void Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
            throw new InvalidDataException(
                "Assistant evaluation catalog is outside its byte limit.");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes.ToArray(),
                new JsonDocumentOptions { MaxDepth = 48 });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Assistant evaluation catalog is malformed.", exception);
        }

        using (document)
        {
            var catalog = document.RootElement;
            ExactProperties(catalog,
                "schema", "frozen_at", "authored_by", "author_id",
                "pricing", "budget", "cases");
            if (String(catalog, "schema") != "lex-assistant-eval/3")
                throw Invalid("schema");
            _ = Utc(String(catalog, "frozen_at"), "frozen_at");
            _ = BoundedText(catalog, "authored_by", 200);
            _ = BoundedText(catalog, "author_id", 200);
            ValidatePricing(Object(catalog, "pricing"));
            ValidateBudget(Object(catalog, "budget"));
            ValidateCases(Array(catalog, "cases"));
        }
    }

    private static void ValidatePricing(JsonElement pricing)
    {
        ExactProperties(pricing,
            "schema", "currency", "source_uri", "retrieved_at", "valid_until",
            "candidate", "grader");
        var retrieved = Utc(String(pricing, "retrieved_at"), "pricing retrieved_at");
        var validUntil = Utc(String(pricing, "valid_until"), "pricing valid_until");
        if (String(pricing, "schema") != "lex-assistant-eval-pricing/1"
            || String(pricing, "currency") != "EUR"
            || String(pricing, "source_uri")
                != "https://prices.azure.com/api/retail/prices"
            || validUntil <= retrieved
            || validUntil - retrieved > TimeSpan.FromDays(7))
            throw Invalid("pricing");
        ValidateModelPricing(Object(pricing, "candidate"));
        ValidateModelPricing(Object(pricing, "grader"));
    }

    private static void ValidateModelPricing(JsonElement model)
    {
        ExactProperties(model, "model_name", "model_version", "sku", "input", "output");
        _ = BoundedText(model, "model_name", 100);
        _ = BoundedText(model, "model_version", 100);
        if (String(model, "sku") != "GlobalStandard")
            throw Invalid("model pricing");
        ValidateMeter(Object(model, "input"));
        ValidateMeter(Object(model, "output"));
    }

    private static void ValidateMeter(JsonElement meter)
    {
        ExactProperties(meter,
            "meter_id", "meter_name", "effective_start_date", "euros_per_million");
        _ = BoundedText(meter, "meter_id", 100);
        _ = BoundedText(meter, "meter_name", 200);
        _ = Utc(String(meter, "effective_start_date"), "meter effective_start_date");
        var price = Decimal(meter, "euros_per_million");
        if (price is <= 0 or > 1_000) throw Invalid("meter price");
    }

    private static void ValidateBudget(JsonElement budget)
    {
        ExactProperties(budget,
            "maximum_candidate_input_tokens", "maximum_candidate_output_tokens",
            "maximum_grader_input_tokens", "maximum_grader_output_tokens",
            "maximum_cost_eur", "maximum_first_operation_p95_latency_ms",
            "maximum_first_operation_hard_latency_ms",
            "maximum_synthesis_p95_latency_ms",
            "maximum_transport_queue_residual_p95_latency_ms",
            "maximum_total_p99_latency_ms");
        var candidateInput = Integer64(budget, "maximum_candidate_input_tokens");
        var candidateOutput = Integer64(budget, "maximum_candidate_output_tokens");
        var graderInput = Integer64(budget, "maximum_grader_input_tokens");
        var graderOutput = Integer64(budget, "maximum_grader_output_tokens");
        var maximumCost = Decimal(budget, "maximum_cost_eur");
        var firstP95 = Number(budget, "maximum_first_operation_p95_latency_ms");
        var firstHard = Number(budget, "maximum_first_operation_hard_latency_ms");
        var synthesisP95 = Number(budget, "maximum_synthesis_p95_latency_ms");
        var residualP95 = Number(
            budget, "maximum_transport_queue_residual_p95_latency_ms");
        var totalP99 = Number(budget, "maximum_total_p99_latency_ms");
        if (candidateInput is < 1 or > 1_000_000
            || candidateOutput is < 1 or > 125_000
            || graderInput is < 1 or > 1_000_000
            || graderOutput is < 1 or > 392_000
            || maximumCost is <= 0 or > 10
            || firstP95 is < 1_000 or > 25_000
            || firstHard < firstP95 || firstHard > 25_000
            || synthesisP95 is < 1_000 or > 60_000
            || residualP95 is < 1 or > 1_500
            || totalP99 < firstHard || totalP99 > 90_000)
            throw Invalid("budget");
    }

    private static void ValidateCases(JsonElement cases)
    {
        var count = cases.GetArrayLength();
        if (count is < 1 or > 25) throw Invalid("case count");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var questions = new HashSet<string>(StringComparer.Ordinal);
        var synthesis = false;
        var noSynthesis = false;
        foreach (var item in cases.EnumerateArray())
        {
            ClosedProperties(item,
                ["id", "question", "repetitions", "maximum_input_tokens",
                    "maximum_output_tokens", "maximum_latency_ms", "expected_synthesis",
                    "expected", "grading"],
                "history");
            var id = BoundedText(item, "id", 64);
            var question = BoundedText(item, "question", 1_000);
            if (!Identifier().IsMatch(id) || !ids.Add(id)
                || !questions.Add(Normalize(question)))
                throw Invalid("case identity");
            var repetitions = Integer(item, "repetitions");
            var maximumInput = Integer(item, "maximum_input_tokens");
            var maximumOutput = Integer(item, "maximum_output_tokens");
            var maximumLatency = Number(item, "maximum_latency_ms");
            if (repetitions is < 1 or > 3
                || maximumInput is < 1 or > 100_000
                || maximumOutput is < 1 or > 20_000
                || maximumLatency is < 1_000 or > 90_000)
                throw Invalid("case bounds");
            var expectedSynthesis = Boolean(item, "expected_synthesis");
            synthesis |= expectedSynthesis;
            noSynthesis |= !expectedSynthesis;
            ValidateExpected(Object(item, "expected"), id);
            ValidateGrading(Object(item, "grading"), id);
            if (item.TryGetProperty("history", out var history))
                ValidateHistory(history, id);
        }
        if (!synthesis || !noSynthesis) throw Invalid("synthesis coverage");
    }

    private static void ValidateHistory(JsonElement history, string caseId)
    {
        if (history.ValueKind != JsonValueKind.Array || history.GetArrayLength() > 8)
            throw Invalid($"{caseId} history");
        foreach (var message in history.EnumerateArray())
        {
            ClosedProperties(message,
                ["role", "content", "maximum_input_tokens", "maximum_output_tokens"],
                "expected_synthesis", "expected");
            if (String(message, "role") != "user") throw Invalid($"{caseId} history role");
            _ = BoundedText(message, "content", 1_000);
            var maximumInput = Integer(message, "maximum_input_tokens");
            var maximumOutput = Integer(message, "maximum_output_tokens");
            if (maximumInput is < 1 or > 100_000
                || maximumOutput is < 1 or > 20_000)
                throw Invalid($"{caseId} history bounds");
            var hasSynthesis = message.TryGetProperty("expected_synthesis", out _);
            var hasExpected = message.TryGetProperty("expected", out var expected);
            if (hasSynthesis != hasExpected) throw Invalid($"{caseId} history contract");
            if (hasSynthesis)
            {
                _ = Boolean(message, "expected_synthesis");
                ValidateExpected(expected, $"{caseId} setup turn");
            }
        }
    }

    private static void ValidateGrading(JsonElement grading, string caseId)
    {
        ClosedProperties(grading,
            ["mode", "maximum_input_tokens", "maximum_output_tokens"], "rubric");
        var mode = String(grading, "mode");
        var maximumInput = Integer(grading, "maximum_input_tokens");
        var maximumOutput = Integer(grading, "maximum_output_tokens");
        var hasRubric = grading.TryGetProperty("rubric", out var rubric);
        var rubricText = hasRubric ? Text(rubric, "grading rubric") : null;
        if (mode is not ("deterministic" or "llm")
            || mode == "llm" && string.IsNullOrWhiteSpace(rubricText)
            || mode == "llm" && maximumInput < 4_096
            || rubricText?.Length > 4_000
            || maximumInput is < 1 or > 100_000
            || maximumOutput is < 1 or > 20_000)
            throw Invalid($"{caseId} grading");
    }

    private static void ValidateExpected(JsonElement expected, string context)
    {
        OnlyProperties(expected,
            "tool", "legal_outcome", "transport_outcome", "effect", "arguments",
            "gap_status", "clarification", "population_minimum", "population_path",
            "forbidden_reply_contains", "argument_alternatives", "operations");
        if (expected.TryGetProperty("gap_status", out var gapStatus))
            _ = BoundedText(gapStatus, 200, "gap_status");
        if (expected.TryGetProperty("clarification", out var clarification))
            _ = BooleanValue(clarification, "clarification");

        IReadOnlyList<JsonElement> operations;
        var isCompound = expected.TryGetProperty("operations", out var compound);
        if (isCompound)
        {
            if (compound.ValueKind != JsonValueKind.Array
                || compound.GetArrayLength() is < 2 or > 8
                || HasAny(expected, "tool", "legal_outcome", "transport_outcome", "effect",
                    "arguments", "argument_alternatives"))
                throw Invalid($"{context} compound contract");
            operations = compound.EnumerateArray().ToArray();
        }
        else
        {
            ClosedOperation(expected, compoundMember: false);
            operations = [expected];
        }

        foreach (var operation in operations)
        {
            if (isCompound)
                ClosedOperation(operation, compoundMember: true);
            var tool = String(operation, "tool");
            var legalOutcome = String(operation, "legal_outcome");
            if (!Tools.Contains(tool)
                || !Outcomes.Contains(legalOutcome)
                || String(operation, "transport_outcome") != "completed"
                || !Effects.Contains(String(operation, "effect"))
                || isCompound
                    && (tool == "legal_boundary" || legalOutcome == "needs_clarification"))
                throw Invalid($"{context} expected operation");
            var arguments = Object(operation, "arguments");
            ValidateArguments(arguments, allowEmpty: true);
            if (operation.TryGetProperty("argument_alternatives", out var alternatives))
                ValidateAlternatives(alternatives, arguments, context);
        }

        var hasMinimum = expected.TryGetProperty("population_minimum", out var minimum);
        var hasPath = expected.TryGetProperty("population_path", out var populationPath);
        if (hasMinimum && IntegerValue(minimum, "population_minimum") < 0)
            throw Invalid($"{context} population minimum");
        if (hasMinimum != hasPath)
            throw Invalid($"{context} population contract");
        if (hasPath)
        {
            var path = BoundedText(populationPath, 200, "population_path");
            if (!path.StartsWith("/", StringComparison.Ordinal))
                throw Invalid($"{context} population path");
        }
        if (expected.TryGetProperty("forbidden_reply_contains", out var forbidden))
        {
            if (forbidden.ValueKind != JsonValueKind.Array
                || forbidden.GetArrayLength() > 8
                || forbidden.EnumerateArray().Any(value =>
                    value.ValueKind != JsonValueKind.String
                    || value.GetString() is not { Length: > 0 and <= 100 }))
                throw Invalid($"{context} forbidden reply markers");
        }
    }

    private static void ClosedOperation(JsonElement operation, bool compoundMember)
    {
        if (compoundMember)
            ClosedProperties(operation,
                ["tool", "legal_outcome", "transport_outcome", "effect", "arguments"],
                "argument_alternatives");
        else if (!HasAll(operation,
                     "tool", "legal_outcome", "transport_outcome", "effect", "arguments"))
            throw Invalid("expected operation");
    }

    private static void ValidateArguments(JsonElement arguments, bool allowEmpty)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || arguments.GetPropertyCount() > 12
            || !allowEmpty && arguments.GetPropertyCount() == 0)
            throw Invalid("expected arguments");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in arguments.EnumerateObject())
            if (!names.Add(argument.Name)
                || string.IsNullOrWhiteSpace(argument.Name)
                || argument.Name.Length > 64
                || argument.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(argument.Value.GetString())
                || argument.Value.GetString()!.Length > 1_000)
                throw Invalid("expected argument");
    }

    private static void ValidateAlternatives(
        JsonElement alternatives,
        JsonElement arguments,
        string context)
    {
        if (alternatives.ValueKind != JsonValueKind.Array
            || alternatives.GetArrayLength() is < 2 or > 4)
            throw Invalid($"{context} argument alternatives");
        var baseKeys = arguments.EnumerateObject()
            .Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alternative in alternatives.EnumerateArray())
        {
            ValidateArguments(alternative, allowEmpty: false);
            if (alternative.EnumerateObject().Any(item => baseKeys.Contains(item.Name)))
                throw Invalid($"{context} argument alternative overlap");
            var identity = string.Join('\n', alternative.EnumerateObject()
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => $"{item.Name}={item.Value.GetString()}"));
            if (!normalized.Add(identity))
                throw Invalid($"{context} duplicate argument alternative");
        }
    }

    private static bool HasAny(JsonElement value, params string[] names) =>
        names.Any(name => value.TryGetProperty(name, out _));

    private static bool HasAll(JsonElement value, params string[] names) =>
        names.All(name => value.TryGetProperty(name, out _));

    private static string Normalize(string value) => string.Join(' ',
        value.Trim().ToLowerInvariant().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

    private static DateTimeOffset Utc(string value, string context)
    {
        if (!DateTimeOffset.TryParse(value, out var parsed)
            || parsed.Offset != TimeSpan.Zero)
            throw Invalid(context);
        return parsed;
    }

    private static JsonElement Object(JsonElement root, string name)
    {
        var value = Property(root, name);
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(name);
        return value;
    }

    private static JsonElement Array(JsonElement root, string name)
    {
        var value = Property(root, name);
        if (value.ValueKind != JsonValueKind.Array) throw Invalid(name);
        return value;
    }

    private static string String(JsonElement root, string name) =>
        Text(Property(root, name), name);

    private static string Text(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
            throw Invalid(name);
        return text;
    }

    private static string BoundedText(JsonElement root, string name, int maximum) =>
        BoundedText(Property(root, name), maximum, name);

    private static string BoundedText(JsonElement value, int maximum, string name)
    {
        var text = Text(value, name);
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum) throw Invalid(name);
        return text;
    }

    private static bool Boolean(JsonElement root, string name) =>
        BooleanValue(Property(root, name), name);

    private static bool BooleanValue(JsonElement value, string name)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid(name);
        return value.GetBoolean();
    }

    private static int Integer(JsonElement root, string name) =>
        IntegerValue(Property(root, name), name);

    private static int IntegerValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw Invalid(name);
        return result;
    }

    private static long Integer64(JsonElement root, string name)
    {
        var value = Property(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
            throw Invalid(name);
        return result;
    }

    private static double Number(JsonElement root, string name)
    {
        var value = Property(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result)
            || !double.IsFinite(result))
            throw Invalid(name);
        return result;
    }

    private static decimal Decimal(JsonElement root, string name)
    {
        var value = Property(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var result))
            throw Invalid(name);
        return result;
    }

    private static JsonElement Property(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out var value))
            throw Invalid(name);
        return value;
    }

    private static void ExactProperties(JsonElement value, params string[] expected)
    {
        OnlyProperties(value, expected);
        if (value.GetPropertyCount() != expected.Length) throw Invalid("required property");
    }

    private static void ClosedProperties(
        JsonElement value,
        IReadOnlyList<string> required,
        params string[] optional)
    {
        OnlyProperties(value, [.. required, .. optional]);
        if (required.Any(name => !value.TryGetProperty(name, out _)))
            throw Invalid("required property");
    }

    private static void OnlyProperties(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("object");
        var remaining = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!remaining.Remove(property.Name)) throw Invalid("unknown or duplicate property");
    }

    private static InvalidDataException Invalid(string context) => new(
        $"Assistant evaluation catalog {context} is invalid.");
}
