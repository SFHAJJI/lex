using Lex.Index;
using Xunit;

namespace Lex.Tests;

/// <summary>
/// The citation shape that tells the preflight a turn named an instrument even when no stored name
/// matched it.
///
/// <para>Asked for "the Luxembourg law of 5 April 1993 on the financial sector", the assistant
/// resolved nothing, degraded to a search over the words, and answered with eight European
/// directives headed by one that merely shares the date. The work is held; the Luxembourg index
/// simply carries no English name for anything, so nothing could match. Answering a different
/// question confidently is the failure this product exists to prevent, and the honest reply is
/// that the instrument could not be identified.</para>
///
/// <para>This is a DETECTOR and never a selector, and the distinction is the whole safety
/// argument. A date may not choose a work: the index's own citation graph shows 93 of the 401
/// apparently unique <c>loi</c> dates are shared with an act it does not hold, so a date-to-work
/// rule would name the wrong statute in roughly one case in four. Detection costs a clarification
/// and can never name anything, so that rate does not apply to it.</para>
/// </summary>
public class CitationShapeDetectorTests
{
    [Theory]
    // The forms a reader writes, in both languages the corpus is cited in.
    [InlineData("Show the French text of the Luxembourg law of 5 April 1993 on the financial sector")]
    [InlineData("Show the loi du 5 avril 1993 relative au secteur financier")]
    [InlineData("What did the reglement grand-ducal du 1er octobre 2020 require")]
    [InlineData("the act of 12 November 2004 as amended")]
    [InlineData("la loi modifiee du 5 avril 1993")]
    public void A_turn_that_cites_an_act_by_form_and_date_has_named_an_instrument(string query)
        => Assert.True(WorkSearch.CitesInstrumentByDate(query));

    [Theory]
    // Coverage questions carry an act form and a date and name no instrument at all. Every one of
    // these is a signed evaluation case that passes today, and a pattern loose enough to catch
    // them would refuse genuine discovery rather than protect anyone.
    [InlineData("Which laws in Lex were in force on 1 June 2024")]
    [InlineData("Which laws in Lex changed on 1 January 1900")]
    [InlineData("Which laws changed most during 2024")]
    [InlineData("What does Lex hold about professional obligations")]
    [InlineData("Show Article 6 of the GDPR in force on 1 January 2021")]
    public void A_turn_that_merely_mentions_laws_and_a_date_has_not(string query)
        => Assert.False(WorkSearch.CitesInstrumentByDate(query));

    [Fact]
    public void Adjacency_is_load_bearing_and_not_incidental()
    {
        // The date has to sit against the act form. Break only that adjacency and the same words
        // stop being a citation, which is what keeps the coverage questions above out.
        Assert.True(WorkSearch.CitesInstrumentByDate("the law of 5 April 1993"));
        Assert.False(WorkSearch.CitesInstrumentByDate(
            "which laws Lex holds, and what was in force on 5 April 1993"));
    }

    [Fact]
    public void Detection_never_narrows_to_a_work()
    {
        // Stated as a test because the safety argument depends on it: the detector reports that a
        // subject was named and returns nothing that could identify one. If this ever returns a
        // work, the 23 percent wrong-sibling rate becomes reachable.
        Assert.IsType<bool>(WorkSearch.CitesInstrumentByDate("the law of 5 April 1993"));
    }
}
