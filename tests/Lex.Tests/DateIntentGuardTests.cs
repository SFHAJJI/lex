using System.Text.Json.Nodes;
using Lex.Ask;

namespace Lex.Tests;

/// <summary>
/// Re-deriving the INSTANT from the user's own words.
///
/// <para>The planner prompt said "expand a bare year to its full inclusive calendar boundary"
/// without narrowing that to a range, and the model read it onto a single point-in-time slot:
/// "what did Article 92 of the CRR require in 2024" was planned as as_of with date=2024-12-31 and
/// the December consolidation was served as though it were the answer for the whole year. Either
/// boundary would have been equally wrong.</para>
///
/// <para><see cref="OperationArguments"/> cannot see this and is deliberately not widened to:
/// from inside that gate <c>date=2024-12-31</c> for "in 2024" is byte-identical to the same
/// argument for "on 31 December 2024". The rule therefore lives where the plan is first held
/// beside the turn that produced it, and it is a guard rather than only a prompt line because a
/// prompt rule is not an invariant and this exact rule already failed once.</para>
/// </summary>
public sealed class DateIntentGuardTests
{
    // The audited turn. The year is in the question, the day is not, and the planned instant is a
    // day inside that year that the user never wrote.
    [Fact]
    public void A_bare_year_does_not_authorize_a_day_inside_it()
    {
        Assert.Equal(2024, DateIntentGuard.DerivedYear(
            "What did Article 92 of the CRR require in 2024", "2024-12-31"));
        Assert.Equal(2024, DateIntentGuard.DerivedYear(
            "What did Article 92 of the CRR require in 2024", "2024-01-01"));
        Assert.Equal(2024, DateIntentGuard.DerivedYear(
            "Qu'exigeait l'article 92 du CRR en 2024 ?", "2024-06-30"));
    }

    // The user's own words authorize the instant, exactly as the work resolver only authorizes
    // works the user's own words named. Every one of these turns states a day.
    [Theory]
    [InlineData("Show Article 92 of the CRR on 31 December 2024", "2024-12-31")]
    [InlineData("Show Article 92 of the CRR on 2024-12-31", "2024-12-31")]
    [InlineData("Show Article 92 of the CRR on 31/12/2024", "2024-12-31")]
    [InlineData("Article 92 du CRR au 31 decembre 2024", "2024-12-31")]
    [InlineData("Article 92 du CRR au 1er janvier 2024", "2024-01-01")]
    [InlineData("Show Article 92 of the CRR on December 31, 2024", "2024-12-31")]
    public void A_stated_day_authorizes_its_own_instant(string turn, string date)
        => Assert.Null(DateIntentGuard.DerivedYear(turn, date));

    // A year the turn does not contain is not the year the plan was derived from. Nothing here
    // may fire on a plan whose date has no relation to any year the user wrote, because rewriting
    // that plan would replace one unexplained instant with a different unexplained window.
    [Fact]
    public void A_year_the_turn_never_wrote_is_not_a_derived_year()
    {
        Assert.Null(DateIntentGuard.DerivedYear(
            "What did Article 92 of the CRR require in 2024", "2019-07-01"));
        Assert.Null(DateIntentGuard.DerivedYear("What does the CRR require now", "2026-08-12"));
        Assert.Null(DateIntentGuard.DerivedYear("", "2024-12-31"));
        Assert.Null(DateIntentGuard.DerivedYear("in 2024", null));
    }

    // The lookarounds keep the bare-year pattern off the year inside an identifier the user typed
    // in full. "Regulation (EU) No 575/2013" carries 2013 and names no instant at all; a five-
    // digit CELEX number carries no year the guard may read.
    [Fact]
    public void An_identifier_is_not_a_bare_year()
    {
        Assert.Null(DateIntentGuard.DerivedYear(
            "What does CELEX 32013R0575 say", "2013-06-26"));
        Assert.Equal("2024-01-01", DateIntentGuard.FirstDayOf(2024));
        Assert.Equal("2024-12-31", DateIntentGuard.LastDayOf(2024));
    }

    // article_history had to gain the window before the planner could be told to plan it. A rule
    // that tells the model to emit arguments the gate refuses is not a rule, so the gate is
    // asserted here beside the rule that depends on it.
    [Fact]
    public void Article_history_accepts_the_year_window_it_is_now_planned_with()
    {
        var arguments = new JsonObject
        {
            ["work_query"] = "CRR",
            ["article_number"] = "92",
            ["from_date"] = "2024-01-01",
            ["to_date"] = "2024-12-31",
        };

        var normalized = OperationArguments.Normalize(
            "article_history", arguments, out _, null, new DateOnly(2026, 8, 12));

        Assert.Equal("2024-01-01", normalized["from_date"]?.GetValue<string>());
        Assert.Equal("2024-12-31", normalized["to_date"]?.GetValue<string>());
        // And it is a window, so it is validated as one: a reversed pair is still refused.
        Assert.Throws<InvalidDataException>(() => OperationArguments.Normalize(
            "article_history",
            new JsonObject
            {
                ["work_query"] = "CRR",
                ["article_number"] = "92",
                ["from_date"] = "2024-12-31",
                ["to_date"] = "2024-01-01",
            },
            out _, null, new DateOnly(2026, 8, 12)));
    }

    // The rewritten planner rule, split by argument role. The prompt is the half that broke, and
    // it broke on one missing word: the answer-layer prompt says "a bare year RANGE" and this one
    // said "a bare year", so both of its worked examples being ranges was all that narrowed it.
    //
    // The interim measure that shipped before this guard, never retrying an unparsable instant, is
    // pinned by PlannerRepairTests.A_violation_whose_repair_would_choose_the_answer_is_not_retried
    // and is deliberately not loosened here.
    [Fact]
    public void The_planner_rule_is_stated_by_argument_role()
    {
        var prompt = AskService.PlannerPrompt("law.test", new DateOnly(2026, 8, 12));

        Assert.Contains("from_date / to_date: expand a bare year", prompt, StringComparison.Ordinal);
        Assert.Contains("NEVER derive a single date from a bare year", prompt,
            StringComparison.Ordinal);
        // The worked example, because two range examples are what let the model read the old
        // single sentence onto a point-in-time slot.
        Assert.Contains("never an as_of with a single 2024 date", prompt, StringComparison.Ordinal);
        // And the sentence that lost the word "range" is not in the prompt any more.
        Assert.DoesNotContain("Expand a bare year to its full inclusive calendar boundary.", prompt,
            StringComparison.Ordinal);
    }
}
