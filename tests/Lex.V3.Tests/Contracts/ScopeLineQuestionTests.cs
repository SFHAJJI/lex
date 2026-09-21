using System;
using System.Collections.Generic;
using System.Linq;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// The eighteen questions <c>41-attack-product-scope.md</c> puts to the scope line, as executable
/// cases. S4-A13's first slice.
/// </summary>
/// <remarks>
/// <para>
/// S4-A13 says *"the 18 scope-line questions and the self-excusing threshold finding in the objection
/// map are acceptance cases here; measured V2 retrieval defects are regression evidence, not V3
/// authority"*. The objection map's own disposition (5.1) is blunter: *"each becomes a test case, not
/// a Decision. They are the closest thing in the pack to an acceptance suite for the answer
/// boundary."* This is that file.
/// </para>
/// <para>
/// <b>What each case carries, and what it deliberately does not.</b> The question as the pack wrote
/// it, its language, and <b>the verdict the scope line's rule requires</b> — drawn from the closed
/// set <c>32-question-catalog.md</c> defines. It does <b>not</b> carry what V2 did. The clause says
/// measured V2 defects are regression evidence and not V3 authority, so a case that asserted V2's
/// behaviour would be asserting the wrong thing: those observations are recorded in
/// <see cref="ScopeLineCase.Note"/> as history, and nothing tests against them.
/// </para>
/// <para>
/// <b>The three the rule cannot decide are the point of the exercise.</b> The pack scores itself: of
/// eighteen, the rule gives a defensible verdict on ten, gives the mandated verdict while the product
/// contradicted it on five, and is <b>genuinely undecidable or self-contradictory on three</b> — 4
/// (no branch of the rule handles language), 15 (the rule says POINT and its own channel test says
/// otherwise), 16 (the person test fires on *"Can I"* and not on *"peut-on"*: identical need,
/// opposite verdicts by pronoun). Those three carry <b>no verdict at all</b>, and this file asserts
/// they carry none. A case that quietly picked one would be the product deciding by fiat exactly
/// where the pack says the boundary does not decide.
/// </para>
/// <para>
/// <b>What this proves, stated no higher than it is.</b> It does <b>not</b> prove the product answers
/// these questions: it cannot be asked any of them, because <c>ask</c> is a registered operation with
/// no route. What it proves is that the eighteen exist here as data, are well formed against the
/// catalogue's closed verdict set, that the pack's own score is reproduced from the cases rather than
/// copied from its prose, and — the load-bearing part —
/// <see cref="NoneOfTheEighteenCanBeAskedOfThisProductYet"/> <b>fails the day <c>ask</c> gets a
/// route</b>. That is deliberate: it makes it impossible to ship the operation these questions are
/// asked of without coming back here and asserting the verdicts. An acceptance suite whose cases can
/// never run is a filing system; this one is wired to break when the product grows into it.
/// </para>
/// </remarks>
[TestClass]
public sealed class ScopeLineQuestionTests
{
    // The verdicts 32-question-catalog.md defines as a closed set. SPLIT is a meta-verdict and only
    // appears with the sub-verdicts it decomposes into.
    private const string Answer = "ANSWER";
    private const string Enrichment = "AWE";
    private const string Point = "POINT";
    private const string Clarify = "CLARIFY";
    private const string Refuse = "REFUSE";
    private const string Split = "SPLIT";

    private static readonly string[] ClosedVerdictSet = [Answer, Clarify, Enrichment, Point, Refuse, Split];

    private enum Standing
    {
        /// <summary>The rule produces a defensible verdict. Ten of the eighteen.</summary>
        RuleIsDefensible,

        /// <summary>The rule produces the mandated verdict and V2 contradicted it. Five.</summary>
        ProductContradictedTheRule,

        /// <summary>The rule is undecidable or self-contradictory here. Three, and they carry no verdict.</summary>
        RuleCannotDecide,
    }

    /// <summary>
    /// One question put to the scope line. <paramref name="RuleVerdict"/> is what the rule requires,
    /// empty where it cannot decide; <paramref name="Note"/> is history and is never asserted against.
    /// </summary>
    private sealed record ScopeLineCase(
        int Number, string Language, string Question, string[] RuleVerdict, Standing Standing, string Note);

    private static readonly ScopeLineCase[] Questions =
    [
        new(1, "fr", "Que disait l'art. L. 121-6 le 15 mars 2021?", [Answer],
            Standing.ProductContradictedTheRule,
            "V2 served the full text, a hash and a permalink without the derogation disclosure the spec mandates."),
        new(2, "en", "Can I be fired while on sick leave?", [Refuse],
            Standing.RuleIsDefensible,
            "REFUSE plus the descriptive maximum. V2 could not deliver the maximum: English retrieval did not reach the article."),
        new(3, "de", "Kann ich in der Probezeit gekündigt werden, und wie lang darf sie sein?", [Split, Refuse, Answer],
            Standing.RuleIsDefensible,
            "REFUSE the \"ich\", ANSWER the maximum duration. V2 returned zero German hits, so both halves were unreachable."),
        new(4, "pt", "Quantos meses de caução pode pedir o senhorio?", [],
            Standing.RuleCannotDecide,
            "No branch of the rule handles language at all, so a pure fact question falls to the default refusal. The rule does not decide it; it defaults."),
        new(5, "lb", "Wéi laang ass de Congé parental?", [Enrichment],
            Standing.RuleIsDefensible,
            "AWE by catalogue row 51, which means after the enrichment phase. Nothing today."),
        new(6, "en", "How many days can I telework from France before my taxes change?", [Point],
            Standing.RuleIsDefensible,
            "The person test trims \"my\"; the threshold sits in a bilateral convention among the never-consolidated acts."),
        new(7, "fr", "Combien de points pour un excès de vitesse?", [Point],
            Standing.ProductContradictedTheRule,
            "POINT, because the Code de la route is textless. V2 served tachograph RGDs as hits."),
        new(8, "en", "Does Regulation 261/2004 cover my cancelled flight?", [Split, Point, Refuse],
            Standing.ProductContradictedTheRule,
            "POINT the absent act and REFUSE \"my\". V2 served merger-control law as hits."),
        new(9, "en", "What did the AML law require in June 2020?", [Point],
            Standing.ProductContradictedTheRule,
            "POINT with the gazette chain, catalogue row 4. V2 returned unknown_work on the catalogue's own identifier."),
        new(10, "en", "Was the GDPR applicable in May 2017?", [Answer, Enrichment],
            Standing.ProductContradictedTheRule,
            "ANSWER the wording plus AWE the force context. V2 flagged a pre-application state as in force."),
        new(11, "fr", "Le plafond de garantie locative de 2024 s'applique-t-il à mon bail de 2022?",
            [Split, Answer, Refuse],
            Standing.RuleIsDefensible,
            "Catalogue row 87 decomposes it: ANSWER both dated states of art. 5 and the transitional "
            + "art. 33bis in full, REFUSE the mapping onto the asker's lease because the transitional "
            + "article decides that kind of case and a professional applies it. Survives on paper; "
            + "delivery depends on surfaces not yet built."),
        new(12, "fr", "Quelles lois ont changé en 2015?", [Answer],
            Standing.RuleIsDefensible,
            "V2 returned ninety works all consolidation-dated the same day with no sparsity caveat: a delivery defect, not a defect of the rule."),
        new(13, "en", "Which LU laws are in force today in financial regulation?", [Answer],
            Standing.RuleIsDefensible,
            "ANSWER with the basis disclosed. Luxembourg binding_status is unpopulated, so the filter matches nothing until it is."),
        new(14, "fr", "Jusqu'à quelle semaine l'IVG est-elle légale?", [Answer],
            Standing.RuleIsDefensible,
            "The number is stated in held Code pénal text. Survives if the lay phrasing reaches the article, which the pack marks SPECULATION."),
        new(15, "fr", "Quel est le salaire social minimum aujourd'hui?", [],
            Standing.RuleCannotDecide,
            "The rule says POINT and its own channel test contradicts it: the missing multiplier is on an open official machine channel, and \"official publisher\" is undefined."),
        new(16, "fr", "Peut-on licencier une salariée enceinte?", [],
            Standing.RuleCannotDecide,
            "The person test fires on \"Can I\" and not on \"peut-on\": identical need, opposite verdicts by pronoun."),
        new(17, "en", "What does CSSF Circular 22/806 require?", [Point],
            Standing.RuleIsDefensible,
            "POINT, forever, and correctly: rights reserved. It is the compliance reader's daily corpus and a link-out is all that is ever offered."),
        new(18, "en", "Has the answer you gave me in March drifted?", [Answer],
            Standing.RuleIsDefensible,
            "ANSWER by catalogue row 26. V2 had no March to replay: its observation history began the month it was measured."),
    ];

    /// <summary>The pack's own score line, which the cases must reproduce rather than restate.</summary>
    private const int Defensible = 10;
    private const int Contradicted = 5;
    private const int Undecidable = 3;

    /// <summary>The operation these eighteen would be put to. Registered, and served by no route.</summary>
    private const string TheOperationTheyWouldBeAskedOf = "ask";

    /// <summary>
    /// What this mount serves today, named rather than counted, so a route added or removed is a
    /// failure here and not a number that quietly moves.
    /// </summary>
    private static readonly string[] ServedOperations =
    [
        "article_history", "as_of", "changes_in_period", "coverage", "diff", "dossier",
        "in_force_on", "provenance", "resolve", "search", "timeline",
    ];

    [TestMethod]
    public void TheEighteenArePresentAndNumberedOneToEighteenWithNoGap()
    {
        CollectionAssert.AreEqual(
            Enumerable.Range(1, 18).ToArray(),
            Questions.Select(static question => question.Number).OrderBy(static number => number).ToArray(),
            "The pack puts eighteen questions to the scope line and each is a case here. A number "
            + "missing or repeated means a case was dropped or duplicated in an edit.");
    }

    [TestMethod]
    public void EveryQuestionCarriesItsWordingAndItsLanguage()
    {
        foreach (var question in Questions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(question.Question), $"question {question.Number} has no wording.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(question.Note), $"question {question.Number} has no note.");
            CollectionAssert.Contains(
                new[] { "de", "en", "fr", "lb", "pt" }, question.Language,
                $"question {question.Number} names a language outside the five the pack uses.");
        }

        // Five languages are represented, and that is the pack's point: three of the four national
        // working languages plus Portuguese are where the boundary misfires.
        CollectionAssert.AreEquivalent(
            new[] { "de", "en", "fr", "lb", "pt" },
            Questions.Select(static question => question.Language).Distinct().ToArray(),
            "Every language the pack put to the scope line must still be represented.");
    }

    [TestMethod]
    public void EveryRuleVerdictIsOneTheCatalogueDefines()
    {
        foreach (var question in Questions)
        {
            foreach (var verdict in question.RuleVerdict)
            {
                CollectionAssert.Contains(
                    ClosedVerdictSet, verdict,
                    $"question {question.Number} names the verdict {verdict}, which 32-question-catalog.md "
                    + "does not define. The set is closed and a verdict outside it is not a verdict.");
            }

            if (question.RuleVerdict.Contains(Split, StringComparer.Ordinal))
            {
                Assert.IsGreaterThan(
                    1, question.RuleVerdict.Length,
                    $"question {question.Number} is SPLIT and names no sub-verdict. SPLIT is a meta-verdict: "
                    + "it decomposes into atomic verdicts and never stands alone, because the whole point is "
                    + "that the answerable part must not smuggle the refused part.");
            }
        }
    }

    [TestMethod]
    public void TheThreeTheRuleCannotDecideCarryNoVerdictAndSayWhy()
    {
        var undecidable = Questions
            .Where(static question => question.Standing == Standing.RuleCannotDecide)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { 4, 15, 16 },
            undecidable.Select(static question => question.Number).OrderBy(static number => number).ToArray(),
            "The pack names exactly three as genuinely undecidable or self-contradictory.");

        foreach (var question in undecidable)
        {
            CollectionAssert.AreEqual(
                Array.Empty<string>(), question.RuleVerdict,
                $"question {question.Number} is one the rule cannot decide and it has been given a verdict. "
                + "That is the boundary deciding by fiat exactly where the pack says it does not decide. "
                + "If the rule has since been repaired so that it does decide, repair the rule in the "
                + "authority first and move this case out of RuleCannotDecide with that decision cited.");
            Assert.IsGreaterThan(
                40, question.Note.Length,
                $"question {question.Number} carries no verdict, so its note is the only account of why.");
        }
    }

    [TestMethod]
    public void TheFiveTheProductContradictedAreRegressionEvidenceAndStillCarryTheRulesVerdict()
    {
        var contradicted = Questions
            .Where(static question => question.Standing == Standing.ProductContradictedTheRule)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { 1, 7, 8, 9, 10 },
            contradicted.Select(static question => question.Number).OrderBy(static number => number).ToArray(),
            "The pack names exactly five where the rule mandates a verdict and the product contradicted it.");

        foreach (var question in contradicted)
        {
            Assert.IsNotEmpty(
                question.RuleVerdict,
                $"question {question.Number} must still carry the verdict the rule requires. What the "
                + "previous product did is regression evidence and not authority here, so a measured "
                + "defect must never be allowed to erase the rule it violated.");
        }
    }

    [TestMethod]
    public void ThePacksScoreIsReproducedFromTheCasesAndNotCopiedFromItsProse()
    {
        // 41-attack-product-scope.md: "of 18, the rule produces a defensible verdict on 10; produces
        // the mandated verdict but the product contradicts it on 5 (1, 7, 8, 9, 10); and is genuinely
        // undecidable or self-contradictory on 3 (4, 15, 16)". Counted here from the standings, so
        // editing a standing without editing the score fails rather than drifting.
        var byStanding = Questions.GroupBy(static question => question.Standing)
            .ToDictionary(static group => group.Key, static group => group.Count());

        Assert.AreEqual(Defensible, byStanding.GetValueOrDefault(Standing.RuleIsDefensible),
            "the pack scores ten as defensible.");
        Assert.AreEqual(Contradicted, byStanding.GetValueOrDefault(Standing.ProductContradictedTheRule),
            "the pack scores five as mandated-and-contradicted.");
        Assert.AreEqual(Undecidable, byStanding.GetValueOrDefault(Standing.RuleCannotDecide),
            "the pack scores three as undecidable.");
        Assert.AreEqual(Defensible + Contradicted + Undecidable, Questions.Length,
            "the three standings must account for every question and nothing else.");

        // The pack's conclusion, which is the reason this clause exists: "a boundary that misfires or
        // is overridden on 8 of 18 hard cases is not yet the product boundary; it is a design
        // intention with a good filing system."
        Assert.AreEqual(
            8,
            Questions.Count(static question => question.Standing != Standing.RuleIsDefensible),
            "eight of eighteen is the pack's finding and the size of what S4-A13 is asking to be closed. "
            + "Counted over the cases rather than added from the two constants above, which a compiler "
            + "can fold and an analyser rightly calls an assertion that cannot fail.");
    }

    [TestMethod]
    public void NoneOfTheEighteenCanBeAskedOfThisProductYet()
    {
        var registered = V3OperationRegistry.Reviewed.Operations
            .Select(static operation => operation.OperationId)
            .ToArray();
        CollectionAssert.Contains(
            registered, TheOperationTheyWouldBeAskedOf,
            "the operation these questions are put to must be a reviewed operation, or this case set "
            + "is measuring something the product never intended to have.");

        var served = V3RestRouteBinding.Served
            .Select(static binding => binding.OperationId)
            .OrderBy(static operation => operation, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            ServedOperations, served,
            "The routes this mount serves have changed. They are named here rather than counted so "
            + "that a route added or removed is a decision someone takes in the open.");

        CollectionAssert.DoesNotContain(
            served, TheOperationTheyWouldBeAskedOf,
            "`ask` now has a route, so these eighteen questions can be put to the product for the "
            + "first time. Every case above must now assert the verdict the scope line requires of it, "
            + "and the three the rule cannot decide must be disclosed as undecided rather than "
            + "answered. This assertion exists to fail on exactly this day: S4-A13 makes the eighteen "
            + "acceptance cases, and an acceptance case that the product can answer and nobody checks "
            + "is worse than one it cannot answer at all.");
    }

    /// <summary>
    /// B42's fifth lesser finding, whose disposition in the objection map (5.2) is *"a real gate
    /// defect and should be scheduled"*: index completeness is held *"within 10 percent unless a
    /// <c>coverage_changed</c> event explains it"*, and <b>the same pipeline emits the event</b>, so
    /// the gate excuses itself. The disposition is that the explanation needs an out-of-band check —
    /// a manifest diff signed by the build, not an event the ingest can mint.
    /// </summary>
    [TestMethod]
    public void TheSelfExcusingCompletenessThresholdHasNoEventToExcuseItselfWith()
    {
        // This asserts an absence, and an absence is the weakest evidence there is, so it is worth
        // being exact about what it buys. It does not prove the gate is sound: it proves the event
        // the gate would excuse itself with cannot be minted here yet, and it fails the day one is.
        // That is the same shape as the S4-A12 pin: the value is not in today's green, it is that
        // the day it goes red is the day the finding has to be answered instead of scheduled.
        Assert.IsFalse(
            V3OperationRegistry.Reviewed.DeclaresRefusal(CoverageChangedEvent),
            $"{CoverageChangedEvent} is now a declared code. If it can be emitted by the pipeline whose "
            + "completeness it explains, the threshold is self-excusing and B42's finding has arrived: "
            + "the explanation needs an out-of-band check, a manifest diff signed by the build, and not "
            + "an event the ingest can mint for itself.");

        CollectionAssert.DoesNotContain(
            V3OperationRegistry.Reviewed.Operations.Select(static operation => operation.OperationId).ToArray(),
            CoverageChangedEvent,
            $"{CoverageChangedEvent} is now an operation, and the same finding applies to it.");
    }

    private const string CoverageChangedEvent = "coverage_changed";
}
