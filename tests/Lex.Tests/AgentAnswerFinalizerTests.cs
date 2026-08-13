using System.Text.Json;
using Lex.Ask;

namespace Lex.Tests;

/// <summary>
/// FinalizeAsync had no test of any kind. Every other layer of this product is pinned, but the
/// one component that decides whether MODEL PROSE reaches a lawyer ran entirely unexercised: the
/// compose retry, the judge's pass, repair and refuse mapping, and the refusal fallbacks were all
/// reachable only in production.
///
/// <para>The scripted agents return recorded model TEXT rather than ready-made objects, so the
/// real structured-output deserialization, the real evidence contract and the real judge mapping
/// execute in every case below. A fake that returned an <see cref="AgentAnswerDraft"/> directly
/// would skip precisely the machinery worth testing.</para>
/// </summary>
public sealed class AgentAnswerFinalizerTests
{
    private static readonly AgentEvidence LegalText = new(
        "text:1", AgentEvidenceKind.LegalText, "eu-eurlex:32016r0679", "art_33",
        "2025-01-01", "abc123",
        "https://law.soufien.lu/eu-eurlex/32016r0679/2025-01-01#art_33");

    private static AgentAnswerDraft Grounded(string text) => new(
        AgentAnswerStatus.Answer,
        text,
        [new AgentClaim(text, AgentClaimKind.LegalText, ["text:1"])],
        [LegalText.Permalink!],
        null,
        null);

    private static string Json<T>(T value) =>
        JsonSerializer.Serialize(value, AgentAnswerFinalizer.JsonOptions);

    private static Task<AgentFinalization> Run(ScriptedAgent composer, ScriptedAgent judge) =>
        new AgentAnswerFinalizer(composer, judge).FinalizeAsync(
            "What does Article 33 require?", "draft", [LegalText], "en", CancellationToken.None);

    [Fact]
    public async Task A_draft_that_breaks_the_evidence_contract_is_corrected_once()
    {
        // First output cites evidence that was never supplied, which Validate refuses.
        var invalid = Grounded("Article 33 requires notification.") with
        {
            Claims = [new AgentClaim("Article 33 requires notification.",
                AgentClaimKind.LegalText, ["text:does-not-exist"])],
        };
        var composer = new ScriptedAgent(
            Json(invalid), Json(Grounded("Article 33 requires notification.")));
        var judge = new ScriptedAgent(Json(new AgentGroundingJudgment(
            AgentJudgmentDisposition.Pass, [], null)));

        var result = await Run(composer, judge);

        Assert.Equal(2, composer.Calls);
        Assert.False(result.SynthesisFailed);
        Assert.Equal("Article 33 requires notification.", result.Draft.Answer);
    }

    [Fact]
    public async Task A_second_contract_violation_refuses_rather_than_retrying_again()
    {
        var invalid = Json(Grounded("x") with
        {
            Claims = [new AgentClaim("x", AgentClaimKind.LegalText, ["text:missing"])],
        });
        var composer = new ScriptedAgent(invalid, invalid);
        var judge = new ScriptedAgent();

        var result = await Run(composer, judge);

        // Exactly two attempts, then the deterministic refusal. The judge is never consulted
        // about a draft that never became valid.
        Assert.Equal(2, composer.Calls);
        Assert.Equal(0, judge.Calls);
        Assert.True(result.SynthesisFailed);
        Assert.Equal(AgentAnswerStatus.Refusal, result.Draft.Status);
    }

    [Fact]
    public async Task A_judge_pass_serves_the_composed_draft()
    {
        var composer = new ScriptedAgent(Json(Grounded("Article 33 requires notification.")));
        var judge = new ScriptedAgent(Json(new AgentGroundingJudgment(
            AgentJudgmentDisposition.Pass, [], null)));

        var result = await Run(composer, judge);

        Assert.Equal(1, judge.Calls);
        Assert.False(result.SynthesisFailed);
        Assert.Equal("Article 33 requires notification.", result.Draft.Answer);
    }

    [Fact]
    public async Task A_judge_repair_serves_the_replacement_not_the_original()
    {
        var composer = new ScriptedAgent(Json(Grounded("The original overreached.")));
        var judge = new ScriptedAgent(Json(new AgentGroundingJudgment(
            AgentJudgmentDisposition.Repair, ["overreached"],
            Grounded("The narrower supported statement."))));

        var result = await Run(composer, judge);

        Assert.False(result.SynthesisFailed);
        Assert.Equal("The narrower supported statement.", result.Draft.Answer);
    }

    // The disposition that matters most: a refusal must reach the reader as a refusal, because
    // this is the moment the product declines to answer rather than guessing.
    [Fact]
    public async Task A_judge_refusal_replaces_the_answer_and_is_reported_as_a_failure()
    {
        var composer = new ScriptedAgent(Json(Grounded("An ungrounded assertion.")));
        var judge = new ScriptedAgent(Json(new AgentGroundingJudgment(
            AgentJudgmentDisposition.Refuse, ["unsupported"], null)));

        var result = await Run(composer, judge);

        Assert.True(result.SynthesisFailed);
        Assert.Equal(AgentAnswerStatus.Refusal, result.Draft.Status);
        Assert.DoesNotContain("ungrounded", result.Draft.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_judgment_that_breaks_its_own_contract_refuses_rather_than_passing()
    {
        var composer = new ScriptedAgent(Json(Grounded("Article 33 requires notification.")));
        // Repair without a replacement: the judgment contract cannot be satisfied.
        var judge = new ScriptedAgent(Json(new AgentGroundingJudgment(
            AgentJudgmentDisposition.Repair, ["needs work"], null)));

        var result = await Run(composer, judge);

        Assert.True(result.SynthesisFailed);
        Assert.Equal(AgentAnswerStatus.Refusal, result.Draft.Status);
    }

    [Fact]
    public async Task Token_usage_from_every_call_is_reported()
    {
        var composer = new ScriptedAgent(Json(Grounded("Article 33 requires notification.")));
        var judge = new ScriptedAgent(Json(new AgentGroundingJudgment(
            AgentJudgmentDisposition.Pass, [], null)));

        var result = await Run(composer, judge);

        // One composer call and one judge call, each scripted at 100 in and 20 out. Usage that
        // silently dropped the judge would understate what a turn actually costs.
        Assert.Equal(200, result.Usage.InputTokens);
        Assert.Equal(40, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task A_refusal_is_written_in_the_asker_language()
    {
        var composer = new ScriptedAgent("not json at all", "still not json");
        var judge = new ScriptedAgent();

        var result = await new AgentAnswerFinalizer(composer, judge).FinalizeAsync(
            "Que prévoit l'article 33 ?", "draft", [LegalText], "fr", CancellationToken.None);

        Assert.True(result.SynthesisFailed);
        Assert.Contains("preuves", result.Draft.Answer, StringComparison.Ordinal);
    }
}
