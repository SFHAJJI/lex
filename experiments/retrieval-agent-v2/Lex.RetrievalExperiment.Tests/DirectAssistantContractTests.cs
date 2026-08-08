using Lex.RetrievalExperiment;

namespace Lex.RetrievalExperiment.Tests;

public sealed class DirectAssistantContractTests
{
    [Fact]
    public void Answer_must_match_the_deterministically_validated_tool_subject()
    {
        var output = new DirectAssistantOutput(
            DirectAssistantStatus.Answer, "Article 33 was retrieved.",
            "eu-eurlex:32016r0679", "art_33", "2025-01-01");

        Assert.Equal(output, DirectAssistantContract.Validate(
            output, "eu-eurlex:32016r0679", "art_33", "2025-01-01"));
        Assert.Throws<InvalidDataException>(() => DirectAssistantContract.Validate(
            output with { Work = "eu-eurlex:wrong" }, "eu-eurlex:32016r0679", "art_33", "2025-01-01"));
        Assert.Throws<InvalidDataException>(() => DirectAssistantContract.Validate(
            output with { Date = "2024-01-01" }, "eu-eurlex:32016r0679", "art_33", "2025-01-01"));
    }

    [Fact]
    public void Gap_cannot_smuggle_a_selected_work_and_answer_requires_tool_evidence()
    {
        var gap = new DirectAssistantOutput(
            DirectAssistantStatus.Gap, "Lex has no validated candidate.", null, null, null);

        Assert.Equal(gap, DirectAssistantContract.Validate(gap, null, null, null));
        Assert.Throws<InvalidDataException>(() => DirectAssistantContract.Validate(
            gap with { Work = "eu-eurlex:noise" }, null, null, null));
        Assert.Throws<InvalidDataException>(() => DirectAssistantContract.Validate(
            gap with { Status = DirectAssistantStatus.Answer }, null, null, null));
    }

    [Fact]
    public void Tool_queries_are_compacted_at_a_word_boundary()
    {
        var query = string.Join(' ', Enumerable.Repeat("notification", 30));

        var compact = DirectResearchTool.CompactQuery(query, 140);

        Assert.InRange(compact.Length, 1, 140);
        Assert.False(compact.EndsWith("notific", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => DirectResearchTool.CompactQuery(" ", 140));
    }
}
