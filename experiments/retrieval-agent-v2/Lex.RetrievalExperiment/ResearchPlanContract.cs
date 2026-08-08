using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.RetrievalExperiment;

public sealed record ResearchQuery(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("language")] string Language);

public sealed record ClarificationRequest(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("options")] IReadOnlyList<string> Options);

public sealed record LegalResearchPlan(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("jurisdiction")] string? Jurisdiction,
    [property: JsonPropertyName("as_of")] string? AsOf,
    [property: JsonPropertyName("queries")] IReadOnlyList<ResearchQuery> Queries,
    [property: JsonPropertyName("provision_queries")] IReadOnlyList<ResearchQuery> ProvisionQueries,
    [property: JsonPropertyName("clarification")] ClarificationRequest? Clarification);

public static class ResearchPlanContract
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static LegalResearchPlan Parse(string json)
    {
        try
        {
            return Validate(JsonSerializer.Deserialize<LegalResearchPlan>(json, JsonOptions)
                ?? throw new JsonException("The plan is null."));
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The model returned a plan outside the contract.", error);
        }
    }

    public static LegalResearchPlan Validate(LegalResearchPlan plan)
    {
        var action = plan.Action.Trim().ToLowerInvariant();
        if (action is not ("search" or "clarify"))
            throw new InvalidDataException("Plan action must be search or clarify.");
        var intent = Compact(plan.Intent, 500, "intent");
        var jurisdiction = plan.Jurisdiction?.Trim().ToUpperInvariant();
        if (jurisdiction is not (null or "EU" or "LU" or "BOTH"))
            throw new InvalidDataException("Jurisdiction must be EU, LU, both, or absent.");
        var asOf = plan.AsOf?.Trim();
        if (asOf is not null && !DateOnly.TryParseExact(asOf, "yyyy-MM-dd", out _))
            throw new InvalidDataException("as_of must be an ISO date.");

        if (action == "clarify")
        {
            if (plan.Queries.Count != 0 || plan.ProvisionQueries.Count != 0 || plan.Clarification is null)
                throw new InvalidDataException("A clarification plan contains no searches and one question.");
            var question = Compact(plan.Clarification.Question, 280, "clarification question");
            var options = plan.Clarification.Options.Select(option => Compact(option, 100, "clarification option"))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (options.Length is < 2 or > 4)
                throw new InvalidDataException("Clarification requires two to four distinct options.");
            return plan with
            {
                Action = action,
                Intent = intent,
                Jurisdiction = jurisdiction,
                AsOf = asOf,
                Queries = [],
                ProvisionQueries = [],
                Clarification = new ClarificationRequest(question, options),
            };
        }

        if (plan.Clarification is not null || plan.Queries.Count is < 2 or > 4)
            throw new InvalidDataException("A search plan requires two to four queries and no clarification.");
        var queries = Queries(plan.Queries, 240, "query");
        if (queries.Length < 2)
            throw new InvalidDataException("Search queries must be distinct after normalization.");
        if (plan.ProvisionQueries.Count is < 2 or > 3)
            throw new InvalidDataException("A search plan requires two or three provision queries.");
        var provisionQueries = Queries(plan.ProvisionQueries, 140, "provision query");
        if (provisionQueries.Length < 2)
            throw new InvalidDataException("Provision queries must be distinct after normalization.");
        return plan with
        {
            Action = action,
            Intent = intent,
            Jurisdiction = jurisdiction,
            AsOf = asOf,
            Queries = queries,
            ProvisionQueries = provisionQueries,
            Clarification = null,
        };
    }

    private static ResearchQuery[] Queries(
        IReadOnlyList<ResearchQuery> values,
        int maximum,
        string field) =>
        values.Select(query =>
            {
                var language = query.Language.Trim().ToLowerInvariant();
                if (language is not ("fr" or "en"))
                    throw new InvalidDataException("Query language must be fr or en.");
                return new ResearchQuery(Compact(query.Query, maximum, field), language);
            })
            .GroupBy(query => (EnrichmentContract.Normalize(query.Query), query.Language))
            .Select(group => group.First()).ToArray();

    private static string Compact(string? value, int maximum, string field)
    {
        var compact = string.Join(' ', (value ?? "").Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (compact.Length is 0 || compact.Length > maximum)
            throw new InvalidDataException($"{field} must contain 1 to {maximum} characters.");
        return compact;
    }
}

public sealed class SearchBudget(int maximum)
{
    private int _used;

    public int Consume(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Search query is required.", nameof(query));
        if (_used >= maximum) throw new InvalidOperationException($"Search budget of {maximum} is exhausted.");
        return ++_used;
    }
}
