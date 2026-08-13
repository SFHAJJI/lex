using Lex.Ask;

namespace Lex.Tests;

/// <summary>
/// The selection and instant disclosures survive synthesis.
///
/// <para>A coverage disclosure is force-appended by the finalizer, but the clause saying Lex
/// CHOSE, and what it chose against, was only advisory: the deterministic line carried it and a
/// synthesized answer replaced that line wholesale. The composer is free to rewrite the prose, so
/// the one sentence that makes a wrong instrument correctable in a single turn could disappear
/// precisely when a model had been involved in the answer.</para>
/// </summary>
public sealed class SelectionDisclosureTests
{
    private static readonly AnswerDisclosure RunnerUp = new(
        RunnerUpWork: "eu-eurlex:32012r0648",
        RunnerUpTitle: "Regulation (EU) No 648/2012");

    [Fact]
    public void A_synthesized_answer_that_dropped_the_selection_clause_regains_it()
    {
        var synthesized = "Article 26 sets out the composition of Common Equity Tier 1.";

        var served = AskService.WithDisclosures(synthesized, "en", [RunnerUp]);

        Assert.StartsWith(synthesized, served, StringComparison.Ordinal);
        Assert.Contains("named more than one instrument", served, StringComparison.Ordinal);
        Assert.Contains("Regulation (EU) No 648/2012 (eu-eurlex:32012r0648)", served,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_composer_that_kept_the_clause_is_not_made_to_repeat_it()
    {
        var kept = AskService.WithDisclosures("Article 26 requires.", "en", [RunnerUp]);

        var twice = AskService.WithDisclosures(kept, "en", [RunnerUp]);

        Assert.Equal(kept, twice);
    }

    [Fact]
    public void The_same_clause_from_several_operations_appears_once()
    {
        var served = AskService.WithDisclosures("Answer.", "en", [RunnerUp, RunnerUp, RunnerUp]);

        var occurrences = served.Split("named more than one instrument").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void A_derived_instant_is_disclosed_through_synthesis_too()
    {
        var widened = new AnswerDisclosure(Instant: InstantSource.WidenedFromYear);

        var served = AskService.WithDisclosures("The 2024 states are open below.", "en", [widened]);

        Assert.NotEqual("The 2024 states are open below.", served);
    }

    [Fact]
    public void A_turn_that_earned_no_disclosure_gains_no_text()
    {
        Assert.Equal("Answer.", AskService.WithDisclosures("Answer.", "en", [null]));
        Assert.Equal("Answer.", AskService.WithDisclosures("Answer.", "en", []));
    }

    [Fact]
    public void The_clause_is_served_in_the_asker_language()
    {
        var served = AskService.WithDisclosures("Réponse.", "fr", [RunnerUp]);

        Assert.Contains("nommait plusieurs instruments", served, StringComparison.Ordinal);
        Assert.DoesNotContain("named more than one", served, StringComparison.Ordinal);
    }
}
