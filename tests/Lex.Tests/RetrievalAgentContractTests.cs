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
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(disclosed with
        {
            CoverageDisclosure = "See https://example.com/invented for missing material.",
        }, [coverage]));
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
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(draft with
        {
            Clarification = new AgentClarification(
                "Choose at https://example.com/invented", ["GDPR", "DORA"]),
        }, []));
    }

    [Theory]
    [InlineData("http://example.com/invented")]
    [InlineData("HTTPS://example.com/invented")]
    public void Every_rendered_field_rejects_unallowlisted_url_forms(string url)
    {
        var answer = Answer(new AgentClaim("Held text", AgentClaimKind.LegalText, ["text:1"]));
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            answer with { Answer = $"Held text {url}" }, [LegalText]));
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            answer with { CoverageDisclosure = $"Coverage detail {url}" }, [LegalText]));

        var clarify = new AgentAnswerDraft(AgentAnswerStatus.Clarify, "Which law?", [], [], null,
            new AgentClarification($"Which law? {url}", ["GDPR", "DORA"]));
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(clarify, []));

        var refusal = new AgentAnswerDraft(AgentAnswerStatus.Refusal, $"Insufficient evidence {url}",
            [], [], null, null);
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(refusal, []));
    }

    [Fact]
    public void Insecure_evidence_permalink_cannot_authorize_the_same_rendered_url()
    {
        const string insecure = "http://attacker.invalid/law";
        var evidence = LegalText with { Permalink = insecure };
        var draft = Answer(new AgentClaim("Held text", AgentClaimKind.LegalText, ["text:1"]));

        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            draft with { Answer = $"Held text {insecure}" }, [evidence]));
        Assert.Throws<InvalidDataException>(() => AgentAnswerContract.Validate(
            draft with { CoverageDisclosure = $"Coverage detail {insecure}" }, [evidence]));
    }

    [Fact]
    public void Grounding_judgment_passes_repairs_or_refuses_through_the_same_contract()
    {
        var draft = Answer(new AgentClaim(
            "The returned provision contains the rule.", AgentClaimKind.LegalText, ["text:1"]));

        Assert.Equal(AgentJudgmentDisposition.Pass, AgentGroundingJudgmentContract.Validate(
            new(AgentJudgmentDisposition.Pass, [], null), draft, [LegalText]).Disposition);
        Assert.Equal(AgentJudgmentDisposition.Repair, AgentGroundingJudgmentContract.Validate(
            new(AgentJudgmentDisposition.Repair, ["Too broad"],
                draft with { Answer = "The returned provision contains this wording." }),
            draft, [LegalText]).Disposition);
        Assert.Equal(AgentJudgmentDisposition.Refuse, AgentGroundingJudgmentContract.Validate(
            new(AgentJudgmentDisposition.Refuse, ["Evidence is insufficient"], null),
            draft, [LegalText]).Disposition);
        Assert.Throws<InvalidDataException>(() => AgentGroundingJudgmentContract.Validate(
            new(AgentJudgmentDisposition.Pass, ["Contradiction"], null), draft, [LegalText]));
    }

    [Fact]
    public void Evidence_limited_refusal_needs_no_fabricated_claim()
    {
        var refusal = new AgentAnswerDraft(
            AgentAnswerStatus.Refusal,
            "The returned evidence is insufficient.",
            [], [], null, null);

        Assert.Equal(refusal.Answer, AgentAnswerContract.Validate(refusal, []).Answer);
    }

    [Fact]
    public void Outline_navigation_survives_an_evidence_limited_prose_refusal()
    {
        var refusal = new AgentAnswerDraft(
            AgentAnswerStatus.Refusal, "Insufficient evidence.", [], [], null, null);
        var outline = new UiEffect(Provision: new ProvisionView(
            new Subject("lu-legilux:code", "Code", "2026-01-01", null),
            "2026-01-01", null,
            [new ProvisionItem("art_1", "1", "Scope", "", "abc")], null));

        var reply = AskService.ReplyFor(refusal, [outline]);

        Assert.Contains("open below", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose a provision", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_with_returned_legal_text_is_not_rewritten_as_navigation()
    {
        var refusal = new AgentAnswerDraft(
            AgentAnswerStatus.Refusal, "Insufficient evidence.", [], [], null, null);
        var text = new UiEffect(Provision: new ProvisionView(
            new Subject("lu-legilux:code", "Code", "2026-01-01", "art_1"),
            "2026-01-01", null,
            [new ProvisionItem("art_1", "1", "Scope", "Held legal text", "abc")], null));

        Assert.Equal("Insufficient evidence.", AskService.ReplyFor(refusal, [text]));
        var outline = new UiEffect(Provision: text.Provision! with
        {
            Provisions = [new ProvisionItem("art_2", "2", "Other", "", "def")],
        });
        Assert.Equal("Insufficient evidence.", AskService.ReplyFor(refusal, [outline, text]));
        Assert.Equal("Insufficient evidence.", AskService.ReplyFor(refusal, [text, outline]));
    }

    [Fact]
    public void A_gap_alongside_an_outline_preserves_the_evidence_limited_refusal()
    {
        var refusal = new AgentAnswerDraft(
            AgentAnswerStatus.Refusal, "Insufficient evidence.", [], [], null, null);
        var outline = new UiEffect(Provision: new ProvisionView(
            new Subject("lu-legilux:code", "Code", "2026-01-01", null),
            "2026-01-01", null,
            [new ProvisionItem("art_1", "1", "Scope", "", "abc")], null));
        var gap = new UiEffect(Gap: new GapView(
            "unknown_anchor", "lu-legilux:code", "2026-01-01", "Unknown article", []));

        Assert.Equal("Insufficient evidence.", AskService.ReplyFor(refusal, [outline, gap]));
    }

    [Fact]
    public void Framework_prompt_serializes_evidence_kinds_as_stable_strings()
    {
        var prompt = AgentAnswerFinalizer.EvidencePrompt([LegalText]);

        Assert.Contains("\"kind\":\"legal_text\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"text:1\"", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_synthesized_factual_claim_requires_the_conditional_judge()
    {
        var coverage = new AgentAnswerDraft(
            AgentAnswerStatus.Gap,
            "The requested material is not held.",
            [new AgentClaim("The material is not held.", AgentClaimKind.Coverage, ["coverage:1"])],
            [], "Coverage is incomplete.", null);
        var clarify = new AgentAnswerDraft(
            AgentAnswerStatus.Clarify,
            "Which law?", [], [], null,
            new AgentClarification("Which law?", ["GDPR", "DORA"]));

        Assert.True(AgentAnswerFinalizer.RequiresJudge(coverage));
        Assert.False(AgentAnswerFinalizer.RequiresJudge(clarify));
    }

    private static AgentAnswerDraft Answer(AgentClaim claim) => new(
        AgentAnswerStatus.Answer,
        claim.Text,
        [claim],
        [LegalText.Permalink!],
        null,
        null);
}
