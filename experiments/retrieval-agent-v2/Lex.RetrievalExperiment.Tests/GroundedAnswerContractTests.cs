using Lex.RetrievalExperiment;

namespace Lex.RetrievalExperiment.Tests;

public sealed class GroundedAnswerContractTests
{
    private static readonly ValidatedEvidence Evidence = new(
        "eu-eurlex:32016r0679", "art_33", "2025-01-01", "abc123",
        "https://eur-lex.europa.eu/example");

    [Fact]
    public void Answer_requires_an_exact_citation_from_validated_evidence()
    {
        var answer = new GroundedAnswerOutput(
            GroundedAnswerStatus.Answer,
            "L'article 33 impose une notification.\n\n- Dans les 72 heures.",
            [new(Evidence.Work, Evidence.Anchor, Evidence.Date, Evidence.TextSha256, Evidence.SourceUri)]);

        var validated = GroundedAnswerContract.Validate(answer, [Evidence]);

        Assert.Equal(answer.Status, validated.Status);
        Assert.Equal(answer.Answer, validated.Answer);
        Assert.Contains("\n\n-", validated.Answer, StringComparison.Ordinal);
        Assert.Equal(answer.Citations, validated.Citations);
        Assert.Throws<InvalidDataException>(() => GroundedAnswerContract.Validate(
            answer with { Citations = [answer.Citations[0] with { Anchor = "art_34" }] }, [Evidence]));
    }

    [Fact]
    public void Gap_has_no_citation_and_answer_cannot_exist_without_evidence()
    {
        var gap = new GroundedAnswerOutput(
            GroundedAnswerStatus.Gap, "Aucun texte validé dans le corpus.", []);

        Assert.Equal(gap, GroundedAnswerContract.Validate(gap, []));
        Assert.Throws<InvalidDataException>(() => GroundedAnswerContract.Validate(
            gap with { Status = GroundedAnswerStatus.Answer }, []));
        Assert.Throws<InvalidDataException>(() => GroundedAnswerContract.Validate(
            gap with { Citations = [new(Evidence.Work, Evidence.Anchor, Evidence.Date,
                Evidence.TextSha256, Evidence.SourceUri)] }, []));
    }

    [Fact]
    public void Judge_passes_repairs_or_refuses_through_bounded_contracts()
    {
        var answer = new GroundedAnswerOutput(
            GroundedAnswerStatus.Answer,
            "Réponse fondée.",
            [new(Evidence.Work, Evidence.Anchor, Evidence.Date, Evidence.TextSha256, Evidence.SourceUri)]);

        Assert.Equal(JudgeDisposition.Pass, GroundingJudgmentContract.Validate(
            new(JudgeDisposition.Pass, [], null), answer, [Evidence]).Disposition);
        Assert.Equal(JudgeDisposition.Repair, GroundingJudgmentContract.Validate(
            new(JudgeDisposition.Repair, ["Portée trop large"], answer with { Answer = "Réponse corrigée." }),
            answer, [Evidence]).Disposition);
        Assert.Equal(JudgeDisposition.Refuse, GroundingJudgmentContract.Validate(
            new(JudgeDisposition.Refuse, ["Preuve insuffisante"], null), answer, [Evidence]).Disposition);
        Assert.Throws<InvalidDataException>(() => GroundingJudgmentContract.Validate(
            new(JudgeDisposition.Pass, ["Contradiction"], null), answer, [Evidence]));
    }

    [Fact]
    public void Model_evidence_is_bounded_without_changing_the_authoritative_hash()
    {
        var bounded = GroundedEvidence.BoundText(new string('x', 8_100), 8_000);

        Assert.Equal(8_001, bounded.Text.Length);
        Assert.True(bounded.Truncated);
    }
}
