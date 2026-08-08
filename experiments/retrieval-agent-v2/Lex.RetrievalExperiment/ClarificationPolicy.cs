using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.RetrievalExperiment;

public sealed record ClarificationOption(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("aliases")] IReadOnlyList<string> Aliases);

public sealed record ClarificationDimension(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("triggers")] IReadOnlyList<string> Triggers,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("options")] IReadOnlyList<ClarificationOption> Options);

public static class ClarificationPolicy
{
    public static IReadOnlyList<ClarificationDimension> Load(string path)
    {
        var root = JsonSerializer.Deserialize<Configuration>(
            File.ReadAllBytes(path), ResearchPlanContract.JsonOptions)
            ?? throw new InvalidDataException("Clarification configuration is empty.");
        if (root.Schema != "lex-clarification-dimensions/1")
            throw new InvalidDataException($"Unsupported clarification schema: {root.Schema}");
        Validate(root.Dimensions);
        return root.Dimensions;
    }

    public static LegalResearchPlan Apply(
        LegalResearchPlan rawPlan,
        string userInput,
        IReadOnlyList<ClarificationDimension> dimensions)
    {
        var plan = ResearchPlanContract.Validate(rawPlan);
        if (plan.Action == "clarify") return plan;
        var clarification = Match(userInput, dimensions);
        if (clarification is null) return plan;
        return ResearchPlanContract.Validate(plan with
        {
            Action = "clarify",
            Queries = [],
            ProvisionQueries = [],
            Clarification = clarification,
        });
    }

    public static ClarificationRequest? Match(
        string userInput,
        IReadOnlyList<ClarificationDimension> dimensions)
    {
        var input = $" {EnrichmentContract.Normalize(userInput)} ";
        foreach (var dimension in dimensions)
        {
            if (!dimension.Triggers.Any(trigger => Contains(input, trigger))) continue;
            if (dimension.Options.Any(option => option.Aliases.Any(alias => Contains(input, alias)))) continue;
            return new ClarificationRequest(
                dimension.Question,
                dimension.Options.Select(option => option.Label).ToArray());
        }
        return null;
    }

    private static bool Contains(string normalizedInput, string phrase)
    {
        var normalized = EnrichmentContract.Normalize(phrase);
        return normalized.Length > 0
               && normalizedInput.Contains($" {normalized} ", StringComparison.Ordinal);
    }

    private static void Validate(IReadOnlyList<ClarificationDimension> dimensions)
    {
        if (dimensions.Select(dimension => dimension.Id).Distinct(StringComparer.Ordinal).Count()
            != dimensions.Count)
            throw new InvalidDataException("Clarification dimension ids must be unique.");
        foreach (var dimension in dimensions)
        {
            if (string.IsNullOrWhiteSpace(dimension.Id)
                || dimension.Triggers.Count == 0
                || string.IsNullOrWhiteSpace(dimension.Question)
                || dimension.Options.Count is < 2 or > 4
                || dimension.Options.Any(option => string.IsNullOrWhiteSpace(option.Label)
                                                   || option.Aliases.Count == 0))
                throw new InvalidDataException($"Invalid clarification dimension: {dimension.Id}");
        }
    }

    private sealed record Configuration(
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("dimensions")] IReadOnlyList<ClarificationDimension> Dimensions);
}
