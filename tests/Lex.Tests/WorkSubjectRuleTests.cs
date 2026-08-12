using Lex.Ask;

namespace Lex.Tests;

/// <summary>
/// Choosing the SUBJECT of a citation among the works that citation named.
///
/// <para>An official title names other instruments inside itself. The CRR's own title ends
/// "...and amending Regulation (EU) No 648/2012", so a lawyer quoting it in full has, by the
/// resolver's correct reckoning, named both the CRR and EMIR, and both are authorized by the
/// user's own words. Exactly one of them is the subject, and the code that used to pick was
/// <c>HitWorks(...).FirstOrDefault()</c>: bm25 of the residual provision query over ARTICLE text,
/// which answers "which article best matches the leftover words" and is not a signal about
/// identity at all. It served EMIR Article 26 for a CRR Article 26 question.</para>
///
/// <para>These tests pin the replacement and, more importantly, the ORDER of its signals. Only the
/// first is decisive; the rest may demote or order, and when none settles it the outcome is a
/// question rather than a pick.</para>
/// </summary>
public sealed class WorkSubjectRuleTests
{
    private const string Crr = "eu-eurlex:32013r0575";
    private const string Emir = "eu-eurlex:32012r0648";

    private const string CrrTitle =
        "Regulation (EU) No 575/2013 of the European Parliament and of the Council of 26 June 2013 "
        + "on prudential requirements for credit institutions and investment firms and amending "
        + "Regulation (EU) No 648/2012";

    private static WorkSubject Select(
        string turn,
        IReadOnlyList<WorkMention> mentions,
        bool articleRequested = false,
        Func<string, bool>? carriesArticle = null,
        Func<string, bool>? holdsText = null)
        => WorkSubjectRule.Select(
            turn, mentions,
            mentions.SelectMany(mention => mention.Works).Distinct(StringComparer.Ordinal).ToArray(),
            articleRequested,
            carriesArticle ?? (_ => false),
            holdsText ?? (_ => false));

    // The audited case. Containment is the only signal here that is not a heuristic: it needs no
    // keyword list, no language, no publisher and no coverage assumption, because it is a fact
    // about the string the user typed. The CRR span covers the EMIR span, so EMIR is named INSIDE
    // the CRR's name and the CRR is what the citation is about.
    [Fact]
    public void The_containing_mention_is_the_subject()
    {
        var subject = Select($"Under {CrrTitle}, what does Article 26 require?",
        [
            new WorkMention("Regulation (EU) No 648/2012", [Emir]),
            new WorkMention(CrrTitle, [Crr]),
        ]);

        var decided = Assert.IsType<WorkSubject.Decided>(subject);
        Assert.Equal(Crr, decided.Work);
        Assert.Equal(WorkSubjectReason.Contained, decided.Reason);
        // The runner-up travels with the choice so the reply can name it in one clause.
        Assert.Equal(Emir, decided.RunnerUp);
    }

    // Containment wins over the article anchor, always, and this is the case where the two
    // disagree: only EMIR holds an article 26 and both works have derived provision text, so the
    // anchor test would fire and would select EMIR. Containment is a fact about the user's words;
    // the anchor is a fact about what Lex holds. Serve the work the user named and report the
    // provision as not found in it.
    [Fact]
    public void Containment_beats_the_article_anchor_when_they_disagree()
    {
        var subject = Select($"Under {CrrTitle}, what does Article 26 require?",
        [
            new WorkMention("Regulation (EU) No 648/2012", [Emir]),
            new WorkMention(CrrTitle, [Crr]),
        ],
            articleRequested: true,
            carriesArticle: work => work == Emir,
            holdsText: _ => true);

        Assert.Equal(Crr, Assert.IsType<WorkSubject.Decided>(subject).Work);
    }

    // A trailing-clause verb may DEMOTE over the already-authorized set and may never select. The
    // asymmetry is the point: if it demotes everything the caller lands in clarification, whereas
    // promoting on the ABSENCE of a keyword would silently pick.
    [Fact]
    public void A_trailing_clause_demotes_the_instrument_it_introduces()
    {
        var subject = Select(
            "Under Regulation (EU) No 596/2014 on market abuse and repealing Directive 2003/6/EC, "
            + "what does Article 7 say?",
        [
            new WorkMention("Regulation (EU) No 596/2014", ["eu-eurlex:32014r0596"]),
            new WorkMention("Directive 2003/6/EC", ["eu-eurlex:32003l0006"]),
        ]);

        var decided = Assert.IsType<WorkSubject.Decided>(subject);
        Assert.Equal("eu-eurlex:32014r0596", decided.Work);
        Assert.Equal(WorkSubjectReason.Demoted, decided.Reason);
    }

    // The keyword is matched only in the three tokens immediately before the mention. A verb that
    // happens to appear elsewhere in the sentence is not a trailing clause, and reading it as one
    // would demote a work named at the head of the question.
    [Fact]
    public void A_verb_far_from_the_mention_is_not_a_trailing_clause()
    {
        var subject = Select(
            "Which articles of Directive 2003/6/EC were repealed, and how does that interact with "
            + "the older Regulation (EU) No 596/2014 regime?",
        [
            new WorkMention("Regulation (EU) No 596/2014", ["eu-eurlex:32014r0596"]),
            new WorkMention("Directive 2003/6/EC", ["eu-eurlex:32003l0006"]),
        ]);

        Assert.IsType<WorkSubject.Undecided>(subject);
    }

    // "Only work W has the anchor" is evidence about the LAW when Lex holds provision text for
    // every candidate, and an artefact of COVERAGE when it does not. The two are indistinguishable
    // from the anchor alone, so the test is conditioned on both works having held text.
    [Fact]
    public void The_article_anchor_selects_only_when_every_candidate_has_held_text()
    {
        WorkMention[] mentions =
        [
            new WorkMention("Regulation (EU) No 575/2013", [Crr]),
            new WorkMention("Regulation (EU) No 648/2012", [Emir]),
        ];
        const string turn = "Under Regulation (EU) No 575/2013 and Regulation (EU) No 648/2012, "
            + "what does Article 26 require?";

        var held = Select(turn, mentions, articleRequested: true,
            carriesArticle: work => work == Crr, holdsText: _ => true);
        var uncovered = Select(turn, mentions, articleRequested: true,
            carriesArticle: work => work == Crr, holdsText: work => work == Crr);

        var decided = Assert.IsType<WorkSubject.Decided>(held);
        Assert.Equal(Crr, decided.Work);
        Assert.Equal(WorkSubjectReason.UniqueHeldAnchor, decided.Reason);
        // The same anchor, the same works, and no derived text for one of them: undecided, because
        // the absence of art_26 in EMIR is then a statement about Lex rather than about EMIR.
        Assert.IsType<WorkSubject.Undecided>(uncovered);
    }

    // Match kind, last and weakest, and only within one citation containing both forms. The
    // resolver has always known which stored name form matched and used to drop it.
    [Fact]
    public void A_quoted_title_outranks_a_bare_identifier_as_a_last_resort()
    {
        var subject = Select(
            "Regulation (EU) No 575/2013 on prudential requirements, Regulation (EU) No 648/2012: "
            + "what does Article 26 require?",
        [
            new WorkMention("Regulation (EU) No 575/2013 on prudential requirements", [Crr],
                "title"),
            new WorkMention("Regulation (EU) No 648/2012", [Emir], "identifier"),
        ]);

        var decided = Assert.IsType<WorkSubject.Decided>(subject);
        Assert.Equal(Crr, decided.Work);
        Assert.Equal(WorkSubjectReason.TitleOverIdentifier, decided.Reason);
    }

    // Two works, cited side by side, no containment, no trailing clause, no anchor and the same
    // match kind. Nothing is decisive, so the outcome is a question, and it is ordered leftmost
    // mention first. Start position orders the menu; it never selects.
    [Fact]
    public void No_decisive_signal_is_undecided_and_ordered_leftmost_first()
    {
        var subject = Select(
            "Under Regulation (EU) No 648/2012 and Regulation (EU) No 575/2013, "
            + "what does Article 26 require?",
        [
            new WorkMention("Regulation (EU) No 575/2013", [Crr], "identifier"),
            new WorkMention("Regulation (EU) No 648/2012", [Emir], "identifier"),
        ]);

        var undecided = Assert.IsType<WorkSubject.Undecided>(subject);
        Assert.Equal([Emir, Crr], undecided.Ordered);
    }

    // A mention the user's own normalized words do not contain carries no span, so it takes no
    // part in containment, demotion or ordering. This is what keeps a resolution recovered from a
    // reformulated query out of a rule that is only ever allowed to reason about this turn.
    [Fact]
    public void A_mention_absent_from_the_turn_carries_no_span()
    {
        var subject = Select($"Under {CrrTitle}, what does Article 26 require?",
        [
            new WorkMention(CrrTitle, [Crr]),
            new WorkMention("digital operational resilience", ["eu-eurlex:32022r2554"]),
        ]);

        // Only one located mention, so containment has nothing to contain and nothing selects.
        var undecided = Assert.IsType<WorkSubject.Undecided>(subject);
        Assert.Equal([Crr, "eu-eurlex:32022r2554"], undecided.Ordered);
    }
}
