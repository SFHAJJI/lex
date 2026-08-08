using System.Text.Json.Serialization;

namespace Lex.RetrievalExperiment;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DirectAssistantStatus
{
    [JsonStringEnumMemberName("answer")]
    Answer,
    [JsonStringEnumMemberName("gap")]
    Gap,
}

public sealed record DirectAssistantOutput(
    DirectAssistantStatus Status,
    string Answer,
    string? Work,
    string? Anchor,
    string? Date);

public static class DirectAssistantContract
{
    public static DirectAssistantOutput Validate(
        DirectAssistantOutput output,
        string? evidenceWork,
        string? evidenceAnchor,
        string? evidenceDate)
    {
        if (string.IsNullOrWhiteSpace(output.Answer))
            throw new InvalidDataException("Assistant answer is required.");

        if (output.Status == DirectAssistantStatus.Gap)
        {
            if (output.Work is not null || output.Anchor is not null || output.Date is not null)
                throw new InvalidDataException("A gap cannot claim selected legal evidence.");
            return output;
        }

        if (evidenceWork is null || !string.Equals(output.Work, evidenceWork, StringComparison.Ordinal))
            throw new InvalidDataException("The answer must cite the deterministically selected work.");
        if (!string.Equals(output.Anchor, evidenceAnchor, StringComparison.Ordinal))
            throw new InvalidDataException("The answer must cite the deterministically selected provision.");
        if (evidenceDate is null
            || !DateOnly.TryParseExact(output.Date, "yyyy-MM-dd", out _)
            || !string.Equals(output.Date, evidenceDate, StringComparison.Ordinal))
            throw new InvalidDataException("The answer must carry the deterministically selected evidence date.");

        return output;
    }
}
