using Lex.RetrievalExperiment;

namespace Lex.RetrievalExperiment.Tests;

public sealed class ClarificationPolicyTests
{
    private static readonly ClarificationDimension Dimension = new(
        "local_manufacturing_mechanism",
        ["fabrication locale", "local manufacturing", "contenu local", "local content"],
        "Quel mécanisme visez-vous ?",
        [
            new("Marchés publics", ["marchés publics", "commande publique", "public procurement"]),
            new("Enchères d'énergie renouvelable",
                ["enchères", "mécanismes de soutien", "renewable energy auction", "support scheme"]),
            new("Aides d'État", ["aides d'état", "aide d'état", "state aid"]),
        ]);

    [Fact]
    public void Broad_trigger_becomes_a_typed_clarification()
    {
        var result = ClarificationPolicy.Apply(SearchPlan(),
            "Quelles règles européennes favorisent la fabrication locale ?", [Dimension]);

        Assert.Equal("clarify", result.Action);
        Assert.Equal(3, result.Clarification?.Options.Count);
        Assert.Empty(result.Queries);
        Assert.Empty(result.ProvisionQueries);
    }

    [Fact]
    public void Explicit_mechanisms_and_unrelated_questions_are_not_overridden()
    {
        var explicitScope = ClarificationPolicy.Apply(SearchPlan(),
            "Comparer les marchés publics et les mécanismes de soutien pour des panneaux fabriqués en Europe.",
            [Dimension]);
        var unrelated = ClarificationPolicy.Apply(SearchPlan(),
            "Quel texte impose de notifier une violation de données ?", [Dimension]);

        Assert.Equal("search", explicitScope.Action);
        Assert.Equal("search", unrelated.Action);
    }

    private static LegalResearchPlan SearchPlan() => new(
        "search",
        "Research the question.",
        "EU",
        null,
        [new("fabrication locale UE", "fr"), new("local manufacturing EU", "en")],
        [new("critères origine", "fr"), new("origin criteria", "en")],
        null);
}
