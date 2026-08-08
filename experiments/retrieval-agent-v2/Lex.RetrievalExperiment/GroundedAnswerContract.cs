using System.Text.Json.Serialization;

namespace Lex.RetrievalExperiment;

public sealed record ValidatedEvidence(
    string Work,
    string Anchor,
    string Date,
    string TextSha256,
    string SourceUri);

public sealed record GroundedCitation(
    [property: JsonPropertyName("work")] string Work,
    [property: JsonPropertyName("anchor")] string Anchor,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("text_sha256")] string TextSha256,
    [property: JsonPropertyName("source_uri")] string SourceUri);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GroundedAnswerStatus
{
    [JsonStringEnumMemberName("answer")]
    Answer,
    [JsonStringEnumMemberName("gap")]
    Gap,
}

public sealed record GroundedAnswerOutput(
    [property: JsonPropertyName("status")] GroundedAnswerStatus Status,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("citations")] IReadOnlyList<GroundedCitation> Citations);

public static class GroundedAnswerContract
{
    public static GroundedAnswerOutput Validate(
        GroundedAnswerOutput output,
        IReadOnlyList<ValidatedEvidence> evidence)
    {
        var answer = Bounded(output.Answer, 6_000, "answer");
        if (output.Status == GroundedAnswerStatus.Gap)
        {
            if (output.Citations.Count != 0)
                throw new InvalidDataException("A corpus gap cannot cite legal evidence.");
            return output with { Answer = answer, Citations = [] };
        }

        if (evidence.Count == 0 || output.Citations.Count == 0)
            throw new InvalidDataException("An answer requires deterministically validated evidence.");
        var allowed = evidence.ToHashSet();
        var citations = output.Citations.Select(citation =>
            {
                var normalized = new ValidatedEvidence(
                    Compact(citation.Work, 200, "citation work"),
                    Compact(citation.Anchor, 100, "citation anchor"),
                    Compact(citation.Date, 10, "citation date"),
                    Compact(citation.TextSha256, 128, "citation text hash"),
                    Compact(citation.SourceUri, 2_000, "citation source URI"));
                if (!DateOnly.TryParseExact(normalized.Date, "yyyy-MM-dd", out _)
                    || !Uri.TryCreate(normalized.SourceUri, UriKind.Absolute, out var source)
                    || source.Scheme != Uri.UriSchemeHttps
                    || !allowed.Contains(normalized))
                    throw new InvalidDataException("A citation does not match validated evidence exactly.");
                return new GroundedCitation(normalized.Work, normalized.Anchor, normalized.Date,
                    normalized.TextSha256, normalized.SourceUri);
            })
            .Distinct().ToArray();
        if (citations.Length != output.Citations.Count)
            throw new InvalidDataException("Duplicate citations are not allowed.");
        return output with { Answer = answer, Citations = citations };
    }

    private static string Compact(string? value, int maximum, string field)
    {
        var compact = string.Join(' ', (value ?? "").Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (compact.Length is 0 || compact.Length > maximum)
            throw new InvalidDataException($"{field} must contain 1 to {maximum} characters.");
        return compact;
    }

    private static string Bounded(string? value, int maximum, string field)
    {
        var bounded = value?.Trim() ?? "";
        if (bounded.Length is 0 || bounded.Length > maximum)
            throw new InvalidDataException($"{field} must contain 1 to {maximum} characters.");
        return bounded;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JudgeDisposition
{
    [JsonStringEnumMemberName("pass")]
    Pass,
    [JsonStringEnumMemberName("repair")]
    Repair,
    [JsonStringEnumMemberName("refuse")]
    Refuse,
}

public sealed record GroundingJudgment(
    [property: JsonPropertyName("disposition")] JudgeDisposition Disposition,
    [property: JsonPropertyName("issues")] IReadOnlyList<string> Issues,
    [property: JsonPropertyName("replacement")] GroundedAnswerOutput? Replacement);

public static class GroundingJudgmentContract
{
    public static GroundingJudgment Validate(
        GroundingJudgment judgment,
        GroundedAnswerOutput draft,
        IReadOnlyList<ValidatedEvidence> evidence)
    {
        GroundedAnswerContract.Validate(draft, evidence);
        var issues = judgment.Issues.Select(issue => issue.Trim())
            .Where(issue => issue.Length > 0).Distinct(StringComparer.Ordinal).Take(5).ToArray();
        return judgment.Disposition switch
        {
            JudgeDisposition.Pass when issues.Length == 0 && judgment.Replacement is null =>
                judgment with { Issues = [] },
            JudgeDisposition.Repair when issues.Length > 0 && judgment.Replacement is not null =>
                judgment with
                {
                    Issues = issues,
                    Replacement = GroundedAnswerContract.Validate(judgment.Replacement, evidence),
                },
            JudgeDisposition.Refuse when issues.Length > 0 && judgment.Replacement is null =>
                judgment with { Issues = issues },
            _ => throw new InvalidDataException(
                "Judge output does not satisfy the pass, repair, or refusal contract."),
        };
    }
}
