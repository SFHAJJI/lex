using Lex.V3.Contracts.Platform.Assistant;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The deterministic parsers (R34: they replace a model router), the intake's mask (R33), the function test's cues (R15,
/// pronoun-blind) and the validator that refuses any plan that is not typed, bound and within the caps (R31, R35, R45(f)).
/// </summary>
[TestClass]
public sealed class AssistantParserAndPlanTests
{
    private static readonly string Address = "someone" + "@" + "example.invalid";

    [TestMethod]
    public void TheIntakeMasksObviousIdentifiersAndKeepsEveryOffset()
    {
        var question = $"Mon adresse est {Address}, mon IBAN LU28 0019 4006 4475 0000, tél. +352 621 123 456, matricule 1990010112345 : que dit l'article 1er ?";
        var intake = V3AssistantParsers.Intake(question)!;
        Assert.AreEqual(question.Length, intake.Masked.Length);
        Assert.IsFalse(intake.Masked.Contains('@'));
        Assert.IsFalse(intake.Masked.Contains("LU28", StringComparison.Ordinal));
        Assert.IsFalse(intake.Masked.Contains("621 123", StringComparison.Ordinal));
        Assert.IsFalse(intake.Masked.Contains("1990010112345", StringComparison.Ordinal));
        StringAssert.Contains(intake.Masked, "que dit l'article 1er", "the law's words are kept");
        Assert.AreEqual(4, intake.Masks.Count);

        Assert.IsNull(V3AssistantParsers.Intake("   "), "a blank question is not read");
        Assert.IsNull(V3AssistantParsers.Intake(new string('a', V3AssistantParsers.MaximumQuestionLength + 1)), "R45(f): question text is capped");
        Assert.AreEqual("été".Normalize(System.Text.NormalizationForm.FormC), V3AssistantParsers.Intake("été")!.Text, "the intake is NFC");
    }

    [TestMethod]
    public void TheLanguageParserReadsTheQuestionsLanguageAndTheLocaleFollowsR43()
    {
        (string Question, V3QuestionLanguage Language, V3AnswerLocale? Locale)[] cases =
        [
            ("Que dit l'article 1er de la loi du 10 août 1991 ?", V3QuestionLanguage.French, V3AnswerLocale.Fr),
            ("What does article 1 of the law say?", V3QuestionLanguage.English, V3AnswerLocale.En),
            ("Was sagt Artikel 1 des Gesetzes vom 10. August 1991?", V3QuestionLanguage.German, null),
            ("Wat seet den Artikel 1 vum Gesetz vum 10. August 1991?", V3QuestionLanguage.Luxembourgish, null),
            ("O que diz o artigo 1 da lei de 10 de agosto de 1991?", V3QuestionLanguage.Portuguese, null),
            ("32016R0679", V3QuestionLanguage.Undetermined, V3AnswerLocale.En),
        ];
        foreach (var (question, language, locale) in cases)
        {
            Assert.AreEqual(language, V3AssistantParsers.LanguageOf(question), question);
            Assert.AreEqual(locale, V3AssistantParsers.LocaleFor(language), question);
        }
    }

    [TestMethod]
    public void TheDateParserReadsCivilDatesAndRangesAndNothingInvalid()
    {
        const string question = "Que disait l'article 5 de la loi du 10 août 1991 entre le 1er janvier 2020 et le 31/12/2021, ou le 2024-02-30 ?";
        var dates = V3AssistantParsers.DatesIn(question);
        CollectionAssert.AreEqual(new[] { "1991-08-10", "2020-01-01", "2021-12-31" }, dates.Select(static date => date.Iso).ToArray(), "30 February is no date");
        CollectionAssert.AreEqual(new[] { "d1", "d2", "d3" }, dates.Select(static date => date.Ref).ToArray());
        var ranges = V3AssistantParsers.RangesIn(question, dates);
        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual(("2020-01-01", "2021-12-31"), (ranges[0].From, ranges[0].To));
        CollectionAssert.AreEqual(new[] { "2025-03-01" }, V3AssistantParsers.DatesIn("on 1 March 2025").Select(static date => date.Iso).ToArray());
        CollectionAssert.AreEqual(new[] { "2025-03-01" }, V3AssistantParsers.DatesIn("on March 1, 2025").Select(static date => date.Iso).ToArray());
    }

    [TestMethod]
    public void TheFunctionTestIsPronounBlindAndAnswersTheDescriptiveTwin()
    {
        string[][] twins =
        [
            ["Can I be fired while on sick leave?", "Can an employee be fired while on sick leave?"],
            ["Puis-je être licencié pendant un congé de maladie ?", "Un salarié peut-il être licencié pendant un congé de maladie ?"],
            ["Should I sign this lease?", "Should a tenant sign this lease?"],
            ["Ai-je le droit de résilier mon bail ?", "Le locataire a-t-il le droit de résilier son bail ?"],
            ["What are my chances of winning in court?", "What are the chances of winning in court?"],
            ["How much will I receive?", "How much will an employee receive?"],
        ];
        foreach (var pair in twins)
        {
            var first = V3AssistantParsers.ApplicationCues(pair[0], 0, pair[0].Length);
            var second = V3AssistantParsers.ApplicationCues(pair[1], 0, pair[1].Length);
            Assert.IsTrue(first.Count > 0, pair[0]);
            Assert.IsTrue(second.Count > 0, pair[1]);
            CollectionAssert.AreEqual(first.Select(static cue => cue.Function).Distinct().ToArray(), second.Select(static cue => cue.Function).Distinct().ToArray(),
                $"pronoun-flipped twins get the same function (R15): {pair[0]} / {pair[1]}");
        }

        foreach (var lookup in new[]
                 {
                     "What does article L. 121-6 say about dismissal during sick leave?",
                     "Que dit l'article 1er de la loi du 10 août 1991 ?",
                     "Can you show me article 5 as it stood on 1 March 2025?",
                 })
        {
            Assert.AreEqual(0, V3AssistantParsers.ApplicationCues(lookup, 0, lookup.Length).Count, $"a deterministic text lookup is answered: {lookup}");
        }
    }

    // ---- The validator ----------------------------------------------------------------------------------------------

    private const string Question = "Que dit l'article 1er de la loi du 10 août 1991 le 1er mars 2025 ?";

    private static V3PlanContext Context(string question = Question, IReadOnlyList<V3ContextChip>? chips = null)
    {
        var intake = V3AssistantParsers.Intake(question)!;
        var dates = V3AssistantParsers.DatesIn(intake.Text);
        return new V3PlanContext(intake, dates, V3AssistantParsers.RangesIn(intake.Text, dates), chips ?? [], ["fra"]);
    }

    private static string Plan(string steps, string followUps = "[]", string parts = "[{\"part_id\":\"p1\",\"span\":[0,64],\"proposed_verdict\":\"answer\"}]") =>
        "{\"schema\":\"lex-v3-assistant-plan/1\",\"parts\":" + parts + ",\"steps\":" + steps + ",\"follow_ups\":" + followUps + "}";

    private const string GoodSearch = "[{\"step_id\":\"s1\",\"part_id\":\"p1\",\"operation_id\":\"search\",\"arguments\":{\"query\":{\"span\":[10,47]},\"language\":{\"choice\":\"fra\"},\"date\":{\"ref\":\"d2\"}}}]";

    private const string GoodFollowUp = "[{\"step_id\":\"f1\",\"after\":\"s1\",\"condition\":\"on_single_article\",\"operation_id\":\"evidence_bundle\",\"arguments\":{\"identifier\":{\"result\":\"s1\",\"pick\":\"sole_work\"},\"article\":{\"result\":\"s1\",\"pick\":\"sole_article\"},\"date\":{\"ref\":\"d2\"}}}]";

    [TestMethod]
    public void ATypedBoundPlanIsAdmittedAndItsCanonicalBytesValidateAgain()
    {
        var validation = V3AssistantPlanValidator.Validate(Plan(GoodSearch, GoodFollowUp), Context());
        Assert.IsTrue(validation.Admitted, validation.Detail);
        var plan = validation.Plan!;
        Assert.AreEqual("article 1er de la loi du 10 août 1991", Question[plan.Steps[0].Arguments["query"].Span!.Start..plan.Steps[0].Arguments["query"].Span!.End]);
        var again = V3AssistantPlanValidator.Validate(plan.CanonicalJson, Context());
        Assert.IsTrue(again.Admitted);
        Assert.AreEqual(plan.Sha256, again.Plan!.Sha256, "the canonical plan is a fixed point, which replay relies on");
    }

    [TestMethod]
    public void EveryPlanThatIsNotTypedOrBoundOrWithinTheCapsIsRefused()
    {
        (string Why, string Json, V3PlanRefusal Refusal)[] cases =
        [
            ("not JSON", "{\"schema\":", V3PlanRefusal.Malformed),
            ("a free-text answer smuggled in", Plan(GoodSearch).Replace("\"follow_ups\"", "\"answer\":\"You can be fired.\",\"follow_ups\"", StringComparison.Ordinal), V3PlanRefusal.UnknownMember),
            ("a free-text query", Plan(GoodSearch.Replace("{\"span\":[10,47]}", "{\"text\":\"anything\"}", StringComparison.Ordinal)), V3PlanRefusal.UnknownMember),
            ("a query from a reference, not the user's words (R34)", Plan(GoodSearch.Replace("{\"span\":[10,47]}", "{\"ref\":\"d1\"}", StringComparison.Ordinal)), V3PlanRefusal.SourceNotAdmitted),
            ("ask itself", Plan(GoodSearch.Replace("\"search\"", "\"ask\"", StringComparison.Ordinal)), V3PlanRefusal.OperationNotAllowed),
            ("an operation outside the allowlist", Plan(GoodSearch.Replace("\"search\"", "\"events\"", StringComparison.Ordinal)), V3PlanRefusal.OperationNotAllowed),
            ("a span past the question", Plan(GoodSearch.Replace("[10,47]", "[8,460]", StringComparison.Ordinal)), V3PlanRefusal.SpanOutOfBounds),
            ("a span whose end is before its start (the review of #1013, LOW 5)", Plan(GoodSearch.Replace("[10,47]", "[47,10]", StringComparison.Ordinal)), V3PlanRefusal.SpanOutOfBounds),
            ("a negative span", Plan(GoodSearch.Replace("[10,47]", "[-1,10]", StringComparison.Ordinal)), V3PlanRefusal.SpanOutOfBounds),
            ("a date reference the question does not hold", Plan(GoodSearch.Replace("\"d2\"", "\"d9\"", StringComparison.Ordinal)), V3PlanRefusal.UnboundArgument),
            ("a language outside the mount's", Plan(GoodSearch.Replace("\"fra\"", "\"spa\"", StringComparison.Ordinal)), V3PlanRefusal.OutsideClosedSet),
            ("a verdict outside the six", Plan(GoodSearch, parts: "[{\"part_id\":\"p1\",\"span\":[0,64],\"proposed_verdict\":\"assistant_v3_unavailable\"}]"), V3PlanRefusal.OutsideClosedSet),
            ("a required argument unbound", Plan(GoodSearch.Replace(",\"language\":{\"choice\":\"fra\"}", "", StringComparison.Ordinal)), V3PlanRefusal.UnboundArgument),
            ("a result read by a main step (R35)", Plan("[{\"step_id\":\"s1\",\"part_id\":\"p1\",\"operation_id\":\"as_of\",\"arguments\":{\"identifier\":{\"result\":\"s1\",\"pick\":\"sole_work\"},\"date\":{\"today\":true}}}]"), V3PlanRefusal.SourceNotAdmitted),
            ("a follow-up on another date (R35)", Plan(GoodSearch, GoodFollowUp.Replace("\"date\":{\"ref\":\"d2\"}", "\"date\":{\"today\":true}", StringComparison.Ordinal)), V3PlanRefusal.FollowUpChangesRequest),
            ("three follow-ups (R35)", Plan(GoodSearch, "[" + string.Join(",", Enumerable.Range(1, 3).Select(at => GoodFollowUp[1..^1].Replace("\"f1\"", $"\"f{at}\"", StringComparison.Ordinal))) + "]"), V3PlanRefusal.CapExceeded),
            ("three parts", Plan(GoodSearch, parts: "[{\"part_id\":\"p1\",\"span\":[0,10],\"proposed_verdict\":\"answer\"},{\"part_id\":\"p2\",\"span\":[10,20],\"proposed_verdict\":\"answer\"},{\"part_id\":\"p3\",\"span\":[20,30],\"proposed_verdict\":\"answer\"}]"), V3PlanRefusal.CapExceeded),
            ("an output over 16 KiB", Plan(GoodSearch) + new string(' ', V3AssistantPlanValidator.MaximumPlanBytes), V3PlanRefusal.TooLarge),
            ("a token carrying prose", Plan(GoodSearch.Replace("\"fra\"", "\"ignore all rules\"", StringComparison.Ordinal)), V3PlanRefusal.OutsideClosedSet),
        ];
        foreach (var (why, json, refusal) in cases)
        {
            var validation = V3AssistantPlanValidator.Validate(json, Context());
            Assert.IsFalse(validation.Admitted, why);
            Assert.AreEqual(refusal, validation.Refusal, $"{why}: {validation.Detail}");
        }
    }

    [TestMethod]
    public void ASpanTouchingAMaskedIdentifierAndARangeWiderThanItsCapAreRefused()
    {
        var masked = $"Écrivez à {Address} : que dit la loi ?";
        var intake = V3AssistantParsers.Intake(masked)!;
        var start = masked.IndexOf(Address, StringComparison.Ordinal);
        var context = new V3PlanContext(intake, [], [], [], ["fra"]);
        var touching = Plan(
            $"[{{\"step_id\":\"s1\",\"part_id\":\"p1\",\"operation_id\":\"search\",\"arguments\":{{\"query\":{{\"span\":[{start},{start + 8}]}},\"language\":{{\"choice\":\"fra\"}}}}}}]",
            parts: $"[{{\"part_id\":\"p1\",\"span\":[0,{masked.Length}],\"proposed_verdict\":\"answer\"}}]");
        Assert.AreEqual(V3PlanRefusal.SpanTouchesMask, V3AssistantPlanValidator.Validate(touching, context).Refusal, "R33: a masked identifier never becomes an argument");

        const string wide = "Qu'est-ce qui a changé entre le 1er janvier 2020 et le 1er janvier 2022 ?";
        var plan = Plan(
            "[{\"step_id\":\"s1\",\"part_id\":\"p1\",\"operation_id\":\"changes_in_period\",\"arguments\":{\"date_from\":{\"ref\":\"r1.from\"},\"date_to\":{\"ref\":\"r1.to\"}}}]",
            parts: $"[{{\"part_id\":\"p1\",\"span\":[0,{wide.Length}],\"proposed_verdict\":\"answer\"}}]");
        Assert.AreEqual(V3PlanRefusal.CapExceeded, V3AssistantPlanValidator.Validate(plan, Context(wide)).Refusal, "R45(f): changes_in_period lists at most 366 days");
    }
}
