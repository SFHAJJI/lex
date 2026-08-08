using Lex.RetrievalExperiment;

namespace Lex.RetrievalExperiment.Tests;

public sealed class ResearchPlanContractTests
{
    [Fact]
    public void Search_plan_requires_compact_unique_queries_and_rejects_model_selected_legal_facts()
    {
        var valid = new LegalResearchPlan(
            "search",
            "Find the EU rule requiring personal-data breach notification within 72 hours.",
            "EU",
            "2025-01-01",
            [
                new("notification violation données 72 heures", "fr"),
                new("personal data breach notification 72 hours", "en"),
            ],
            [
                new("notification violation 72 heures", "fr"),
                new("breach notification 72 hours", "en"),
            ],
            null);

        var normalized = ResearchPlanContract.Validate(valid);

        Assert.Equal(2, normalized.Queries.Count);
        Assert.Equal("EU", normalized.Jurisdiction);
        Assert.Throws<InvalidDataException>(() => ResearchPlanContract.Validate(valid with
        {
            Queries = [new(new string('x', 241), "fr"), new("other query", "en")],
        }));
        Assert.Throws<InvalidDataException>(() => ResearchPlanContract.Parse("""
            {
              "action":"search",
              "intent":"Find GDPR",
              "jurisdiction":"EU",
              "queries":[
                {"query":"RGPD","language":"fr"},
                {"query":"GDPR","language":"en"}
              ],
              "provision_queries":[
                {"query":"notification violation","language":"fr"},
                {"query":"breach notification","language":"en"}
              ],
              "expected_work":"eu-eurlex:32016r0679"
            }
            """));
    }

    [Fact]
    public void Clarification_is_typed_bounded_and_contains_no_search_plan()
    {
        var plan = new LegalResearchPlan(
            "clarify",
            "The requested European local-manufacturing mechanism is materially ambiguous.",
            "EU",
            null,
            [],
            [],
            new("Quel mécanisme visez-vous ?",
                ["Marchés publics", "Enchères d'énergie renouvelable", "Aides d'État"]));

        var normalized = ResearchPlanContract.Validate(plan);

        Assert.Equal("clarify", normalized.Action);
        var clarification = Assert.IsType<ClarificationRequest>(normalized.Clarification);
        Assert.Equal(3, clarification.Options.Count);
        var originalClarification = Assert.IsType<ClarificationRequest>(plan.Clarification);
        Assert.Throws<InvalidDataException>(() => ResearchPlanContract.Validate(plan with
        {
            Clarification = originalClarification with { Options = ["Un seul choix"] },
        }));
    }

    [Fact]
    public void Search_budget_is_shared_across_reformulations()
    {
        var budget = new SearchBudget(2);

        Assert.Equal(1, budget.Consume("notification violation données"));
        Assert.Equal(2, budget.Consume("data breach notification"));
        Assert.Throws<InvalidOperationException>(() => budget.Consume("third attempt"));
    }

    [Theory]
    [InlineData(true, 0, 0.1, true)]
    [InlineData(false, 1, 0.919, false)]
    [InlineData(false, 1, 0.92, true)]
    [InlineData(false, 0, 0.99, false)]
    public void Candidate_gate_requires_a_name_or_calibrated_discovery_evidence(
        bool exactName,
        int discoveryMatches,
        double similarity,
        bool expected) =>
        Assert.Equal(expected,
            ResearchPlanExecutor.IsResolvedCandidate(exactName, discoveryMatches, similarity));
}
