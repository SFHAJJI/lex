using Lex.Index;

namespace Lex.Tests;

/// <summary>
/// The clause test itself, at the layer that creates the conflation.
///
/// <para>An official EU title routinely ends by naming another instrument: the CRR's ends
/// "and amending Regulation (EU) No 648/2012". A lawyer quoting it in full names two works, both
/// match as identity, both resolve to one candidate each, and nothing downstream looks ambiguous.
/// WorkSubjectRule survives that; this stops it being produced.</para>
/// </summary>
public sealed class AmendingClauseDemotionTests
{
    private static int Start(string query, string mention) =>
        (" " + WorkSearch.Normalize(query) + " ")
            .IndexOf(" " + WorkSearch.Normalize(mention) + " ", StringComparison.Ordinal);

    private static bool Follows(string query, string mention) =>
        WorkSearch.FollowsAmendingClause(WorkSearch.Normalize(query), Start(query, mention));

    // The audited citation, and the French form a Luxembourg practitioner would use.
    [Theory]
    [InlineData("Regulation (EU) No 575/2013 on prudential requirements and amending "
                + "Regulation (EU) No 648/2012", "Regulation (EU) No 648/2012")]
    [InlineData("Regulation (EU) 2016/679 repealing Directive 95/46/EC", "Directive 95/46/EC")]
    [InlineData("le reglement 2016/679 abrogeant la directive 95/46/CE", "la directive 95/46/CE")]
    [InlineData("Directive 2014/65/EU replacing Directive 2004/39/EC", "Directive 2004/39/EC")]
    public void A_work_named_after_an_amending_participle_sits_in_the_clause(
        string query, string mention)
    {
        Assert.True(Follows(query, mention));
    }

    // The subject of the same sentences is not in the clause, which is the half that makes the
    // rule a demotion rather than a filter that empties the set.
    [Theory]
    [InlineData("Regulation (EU) No 575/2013 on prudential requirements and amending "
                + "Regulation (EU) No 648/2012", "Regulation (EU) No 575/2013")]
    [InlineData("Regulation (EU) 2016/679 repealing Directive 95/46/EC", "Regulation (EU) 2016/679")]
    public void The_subject_of_the_citation_is_not(string query, string mention)
    {
        Assert.False(Follows(query, mention));
    }

    // A work the user asks about directly is never in a clause, however the sentence is phrased.
    [Theory]
    [InlineData("What does Regulation (EU) No 648/2012 require?", "Regulation (EU) No 648/2012")]
    [InlineData("Show Regulation (EU) No 648/2012 as amended", "Regulation (EU) No 648/2012")]
    [InlineData("Which act amended Regulation (EU) No 648/2012?", "Regulation (EU) No 648/2012")]
    public void A_work_the_question_is_about_is_never_demoted(string query, string mention)
    {
        Assert.False(Follows(query, mention));
    }

    // The window is bounded, so an amending verb far earlier in a long question does not reach
    // forward and demote an unrelated later mention.
    [Fact]
    public void The_clause_does_not_reach_past_its_window()
    {
        const string query = "amending acts aside, what does Regulation (EU) No 648/2012 require?";

        Assert.False(Follows(query, "Regulation (EU) No 648/2012"));
    }

    [Fact]
    public void A_mention_at_the_start_of_the_query_has_no_clause_before_it()
    {
        Assert.False(WorkSearch.FollowsAmendingClause("regulation eu no 648 2012", 0));
        Assert.False(WorkSearch.FollowsAmendingClause("", 0));
    }
}
