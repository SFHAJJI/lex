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
    // The hole the design review found in the first version of this guard, and the reason the two
    // confidently-wrong classes composed. The stand-down used to fire on ANY day-and-month in the
    // turn, but legal citations carry dates that name an INSTRUMENT rather than an instant: the
    // CRR's official title ends "of 26 June 2013", and Luxembourg statutes are cited by their
    // opening clause. So the exact citation forms most likely to confuse the work resolver were
    // also the ones that disarmed this guard, and 2024-12-31 bound silently behind them.
    [Theory]
    [InlineData(
        "What did Article 92 of Regulation (EU) No 575/2013 of the European Parliament and of "
        + "the Council of 26 June 2013 on prudential requirements for credit institutions "
        + "require in 2024?", "2024-12-31", 2024)]
    [InlineData(
        "Que prevoyait l'article 92 de la loi du 12 novembre 2004 en 2024 ?", "2024-12-31", 2024)]
    [InlineData(
        "Article 26 of the CRR of 26 June 2013, as it stood in 2020", "2020-01-01", 2020)]
    public void A_date_inside_a_citation_cannot_authorize_a_different_date(
        string turn, string date, int expected)
    {
        Assert.Equal(expected, DateIntentGuard.DerivedYear(turn, date));
    }

    // The other half of the same rule: the citation's own date still authorizes ITSELF, so a
    // question genuinely asking about 26 June 2013 is untouched by the scoping above.
    [Fact]
    public void A_citation_date_still_authorizes_that_very_instant()
    {
        Assert.Null(DateIntentGuard.DerivedYear(
            "Article 92 of the CRR of 26 June 2013 as adopted", "2013-06-26"));
    }

    // A month and a year with no day is not a stated day either: picking the 31st out of
    // "December 2024" chooses a version of the law exactly as picking it out of "2024" does.
    [Fact]
    public void A_month_without_a_day_does_not_authorize_a_day()
    {
        Assert.Equal(2024, DateIntentGuard.DerivedYear(
            "What did Article 92 of the CRR require in December 2024?", "2024-12-31"));
    }

    // The date parser runs on whatever the user typed, so a four-digit year the calendar has no
    // room for must be discarded rather than handed to DaysInMonth, which throws below year 1.
    [Theory]
    [InlineData("Article 92 as it stood on 0000-01-01, and in 2024", "2024-12-31", 2024)]
    [InlineData("Article 92 on 32/13/2024 in 2024", "2024-12-31", 2024)]
    [InlineData("Article 92 on 31 February 2024 in 2024", "2024-12-31", 2024)]
    public void An_impossible_date_is_discarded_rather_than_thrown(
        string turn, string date, int expected)
    {
        Assert.Equal(expected, DateIntentGuard.DerivedYear(turn, date));
    }

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
