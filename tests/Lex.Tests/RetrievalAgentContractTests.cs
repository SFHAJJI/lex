using Lex.Ask;

namespace Lex.Tests;

public sealed class RetrievalAgentContractTests
{
    private static readonly AgentEvidence LegalText = new(
        "text:1", AgentEvidenceKind.LegalText, "eu-eurlex:32016r0679", "art_33",
        "2025-01-01", "abc123", "https://law.soufien.lu/eu-eurlex/32016r0679/2025-01-01#art_33");

    [Fact]
    public void Legal_claim_requires_legal_text_not_a_search_pointer()
    {
        var draft = Answer(new AgentClaim(
            "Article 33 contains the notification rule.", AgentClaimKind.LegalText, ["text:1"]));

        var validated = AgentAnswerContract.Validate(draft, [LegalText]);
        Assert.Equal(draft.Answer, validated.Answer);
        Assert.Equal(draft.Claims[0].Text, validated.Claims[0].Text);
        Assert.Equal(draft.Claims[0].EvidenceIds, validated.Claims[0].EvidenceIds);

        var pointer = LegalText with { Id = "pointer:1", Kind = AgentEvidenceKind.Pointer };
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            draft with { Claims = [draft.Claims[0] with { EvidenceIds = ["pointer:1"] }] },
            [pointer]));
    }

    [Fact]
    public void Claim_type_must_match_the_evidence_type()
    {
        var timeline = LegalText with { Id = "timeline:1", Kind = AgentEvidenceKind.Timeline };
        var draft = Answer(new AgentClaim(
            "The provision changed on this date.", AgentClaimKind.Change, ["timeline:1"]));

        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(draft, [timeline]));

        var change = timeline with { Kind = AgentEvidenceKind.Change };
        Assert.Equal(draft.Claims[0].EvidenceIds,
            AgentAnswerContract.Validate(draft, [change]).Claims[0].EvidenceIds);
    }

    [Fact]
    public void Answer_links_must_be_returned_by_the_cited_evidence()
    {
        var draft = Answer(new AgentClaim(
            "The returned text contains the rule.", AgentClaimKind.LegalText, ["text:1"]));

        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            draft with { Permalinks = ["https://example.com/invented"] }, [LegalText]));
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            draft with { Answer = "See https://example.com/invented for the rule." }, [LegalText]));
    }

    [Fact]
    public void Coverage_gap_requires_an_explicit_disclosure()
    {
        var coverage = new AgentEvidence(
            "coverage:1", AgentEvidenceKind.Coverage, null, null, null, null, null,
            RequiresCoverageDisclosure: true);
        var draft = new AgentAnswerDraft(
            AgentAnswerStatus.Gap,
            "Lex does not hold enough evidence to answer.",
            [new AgentClaim("The requested material is outside the held corpus.",
                AgentClaimKind.Coverage, ["coverage:1"])],
            [],
            null,
            null);

        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(draft, [coverage]));

        var disclosed = draft with
        {
            CoverageDisclosure = "Coverage is incomplete for the requested work and period.",
        };
        Assert.Equal(disclosed.CoverageDisclosure,
            AgentAnswerContract.Validate(disclosed, [coverage]).CoverageDisclosure);
    }

    [Fact]
    public void Clarification_is_typed_bounded_and_has_no_claims()
    {
        var draft = new AgentAnswerDraft(
            AgentAnswerStatus.Clarify,
            "Which instrument do you mean?",
            [],
            [],
            null,
            new AgentClarification("Which instrument do you mean?", ["GDPR", "DORA"]));

        var validated = AgentAnswerContract.Validate(draft, []);
        Assert.Equal(draft.Clarification?.Question, validated.Clarification?.Question);
        Assert.Equal(draft.Clarification?.Options, validated.Clarification?.Options);
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            draft with { Claims = [new AgentClaim("A claim", AgentClaimKind.LegalText, ["text:1"])] },
            [LegalText]));
    }

    private static AgentAnswerDraft Answer(AgentClaim claim) => new(
        AgentAnswerStatus.Answer,
        claim.Text,
        [claim],
        [LegalText.Permalink!],
        null,
        null);
}
