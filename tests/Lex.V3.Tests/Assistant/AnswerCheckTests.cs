using System.Text.Json;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.Contracts.Platform.Assistant.Bricks;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The answer check (the owner's order of 2026-11-19): an answer the plan asserts is read once more by the model against
/// the first article it quotes. Rejected, the same passages are candidates; accepted, the answer stands; a call that fails
/// leaves the plan as it was; a cited article is never checked; switched off, nothing changes.
/// </summary>
[TestClass]
public sealed class AnswerCheckTests
{
    // Asserted by code alone: the defining line of the act, no model call.
    private const string Question = "c'est quoi une personne lésé";

    private const string Rejects = """{"reason":"The article defines other terms and does not state the rule asked.","answer_found":false,"complete_answer_found":false}""";
    private const string Accepts = """{"reason":"The article defines the term asked.","answer_found":true,"complete_answer_found":true}""";

    private static BricksFixture.Model Checker(string reply) => new(call => call.Step == "check"
        ? reply
        : throw new HttpRequestException("only the check answers in this test"));

    private static V3PlannerInput Input(string question) => new(question, V3AnswerLocale.Fr, [], ["fra"], BricksFixture.Today);

    private static V3AnswerDossier Answered(string question, IV4Model? model, bool answerCheck)
    {
        var outcome = V3AssistantPipeline.Answer(
            new V3AssistantRequest(question),
            new V4BricksPlanner(new BricksFixture.Corpus(), model, answerCheck: answerCheck),
            new BricksFixture.Executor().Run,
            BricksFixture.Environment,
            CancellationToken.None);
        if (outcome is V3AskPresented presented)
        {
            Assert.Fail($"not answered: {presented.Reason} {presented.PlanRefusal}");
        }

        return ((V3AskAnswered)outcome).Dossier;
    }

    private static JsonElement Bricks(V3AnswerDossier dossier)
    {
        using var document = JsonDocument.Parse(dossier.ToUtf8());
        return document.RootElement.GetProperty("trace").GetProperty("plan").GetProperty("bricks").Clone();
    }

    private static string[] Templates(V3AnswerDossier dossier) =>
        dossier.Blocks.OfType<V3ClaimBlock>().Select(static block => block.Claim.TemplateId).ToArray();

    [TestMethod]
    public void AnAnswerTheCheckRejectsIsShownAsCandidatesAndTheCallIsInTheRecord()
    {
        var off = Answered(Question, null, answerCheck: false);
        Assert.AreNotEqual("candidates", Bricks(off).GetProperty("answer").GetProperty("kind").GetString(), "asserted today");

        var model = Checker(Rejects);
        var dossier = Answered(Question, model, answerCheck: true);
        Assert.AreEqual(V3AssistantVerdict.Answer, dossier.Verdict);
        CollectionAssert.AreEqual(off.Quotes.Select(static quote => (quote.Anchor, quote.Text)).ToArray(), dossier.Quotes.Select(static quote => (quote.Anchor, quote.Text)).ToArray(), "the same passages");
        CollectionAssert.Contains(Templates(dossier), V3AnswerTemplates.AnswerCandidates, "shown, not asserted");

        var bricks = Bricks(dossier);
        Assert.AreEqual("candidates", bricks.GetProperty("answer").GetProperty("kind").GetString());
        var call = bricks.GetProperty("model_calls").EnumerateArray().Last();
        Assert.AreEqual(("check", "scripted-model", 1000, 100), (call.GetProperty("step").GetString(), call.GetProperty("model").GetString(), call.GetProperty("input_tokens").GetInt32(), call.GetProperty("output_tokens").GetInt32()));
        var pass = bricks.GetProperty("passes").EnumerateArray().Last();
        Assert.AreEqual(("answer_check", "answer_to_candidates", "rejected"), (pass.GetProperty("trigger").GetString(), pass.GetProperty("changed").GetString(), pass.GetProperty("outcome").GetString()));

        // The answer says a model took part.
        var notices = dossier.Notices.Select(static notice => notice.TemplateId).ToArray();
        CollectionAssert.DoesNotContain(notices, V3AnswerTemplates.NoticeNoModel);
        CollectionAssert.Contains(notices, V3AnswerTemplates.NoticeModelAssisted);

        // The one call, as ordered: the instruction, the question and the whole first article quoted, 120 tokens at most.
        var check = model.Calls.Single();
        Assert.AreEqual(V4Generation.AnswerCheckPrompt, check.System);
        Assert.AreEqual(120, check.MaximumTokens);
        var first = off.Quotes.First();
        var work = first.Work[(first.Work.LastIndexOf('/') + 1)..];
        StringAssert.StartsWith(check.User, "QUESTION:\n" + Question + "\n\nARTICLE (" + work + ", " + first.Anchor + "):\n");
        StringAssert.Contains(check.User, first.Text, "the article's own text");
        using (var schema = JsonDocument.Parse(check.SchemaJson))
        {
            CollectionAssert.AreEqual(new[] { "reason", "answer_found", "complete_answer_found" }, schema.RootElement.GetProperty("required").EnumerateArray().Select(static name => name.GetString()).ToArray());
        }

        // A replay asks no model and gives the same bytes.
        using var document = JsonDocument.Parse(dossier.ToUtf8());
        var replayed = V3AssistantPipeline.Replay(document.RootElement.GetProperty("trace").GetRawText(), static _ => new BricksFixture.Executor().Run, BricksFixture.Environment);
        CollectionAssert.AreEqual(dossier.ToUtf8(), ((V3AskAnswered)replayed.Outcome!).Dossier.ToUtf8());
    }

    [TestMethod]
    public void AnAnswerTheCheckAcceptsStandsAndTheCallIsInTheRecord()
    {
        var off = Answered(Question, null, answerCheck: false);
        var model = Checker(Accepts);
        var dossier = Answered(Question, model, answerCheck: true);
        CollectionAssert.AreEqual(Templates(off), Templates(dossier), "the same sentences");
        var bricks = Bricks(dossier);
        Assert.AreEqual(Bricks(off).GetProperty("answer").GetRawText(), bricks.GetProperty("answer").GetRawText(), "the same answer");
        Assert.AreEqual("check", bricks.GetProperty("model_calls").EnumerateArray().Last().GetProperty("step").GetString());
        var pass = bricks.GetProperty("passes").EnumerateArray().Last();
        Assert.AreEqual(("answer_check", "none", "accepted_complete"), (pass.GetProperty("trigger").GetString(), pass.GetProperty("changed").GetString(), pass.GetProperty("outcome").GetString()));
        Assert.AreEqual(1, model.Calls.Count);
    }

    [TestMethod]
    public void WhenTheCheckFailsTheAnswerStandsAsItWas()
    {
        var before = new V4BricksPlanner(new BricksFixture.Corpus(), null).Plan(Input(Question), CancellationToken.None);
        var failing = new BricksFixture.Model(static _ => throw new HttpRequestException("down"));
        var failed = new V4BricksPlanner(new BricksFixture.Corpus(), failing, answerCheck: true).Plan(Input(Question), CancellationToken.None);
        Assert.AreEqual(before.PlanJson, failed.PlanJson, "byte for byte");
        Assert.AreEqual(before.CostMicroEuros, failed.CostMicroEuros);

        // A reply that is not of the form changes nothing of the answer; the call it cost is in the record.
        var odd = new V4BricksPlanner(new BricksFixture.Corpus(), Checker("""{"answer_found":"perhaps"}"""), answerCheck: true).Plan(Input(Question), CancellationToken.None);
        using var plan = JsonDocument.Parse(odd.PlanJson!);
        using var was = JsonDocument.Parse(before.PlanJson!);
        Assert.AreEqual(was.RootElement.GetProperty("bricks").GetProperty("answer").GetProperty("kind").GetString(), plan.RootElement.GetProperty("bricks").GetProperty("answer").GetProperty("kind").GetString());
        Assert.AreEqual("reply_unreadable", plan.RootElement.GetProperty("bricks").GetProperty("passes").EnumerateArray().Last().GetProperty("outcome").GetString());
        Assert.IsNull(V4Bricks.ReadAnswerCheck("""{"reason":"x","answer_found":true}"""));
        Assert.AreEqual((true, false), V4Bricks.ReadAnswerCheck("""{"reason":"x","answer_found":true,"complete_answer_found":false}"""));
    }

    [TestMethod]
    public void ACitedArticleAndWhatAssertsNothingAreNeverChecked()
    {
        var never = new BricksFixture.Model(static _ => throw new InvalidOperationException("no model is asked"));
        foreach (var question in new[]
                 {
                     "Que dit l'article 39 de la loi sur le contrat d'assurance ?",
                     "Article 1er de la loi sur le contrat d'assurance",
                     "Bonjour",
                     "garantie",
                 })
        {
            var off = new V4BricksPlanner(new BricksFixture.Corpus(), never).Plan(Input(question), CancellationToken.None);
            var on = new V4BricksPlanner(new BricksFixture.Corpus(), never, answerCheck: true).Plan(Input(question), CancellationToken.None);
            Assert.AreEqual(off.PlanJson, on.PlanJson, question);
            Assert.AreEqual(0, never.Calls.Count, question);
        }
    }

    [TestMethod]
    public void SwitchedOffThePlannerIsThePlannerItWas()
    {
        var model = Checker(Rejects);
        var before = new V4BricksPlanner(new BricksFixture.Corpus(), model);
        var off = new V4BricksPlanner(new BricksFixture.Corpus(), model, answerCheck: false);
        var on = new V4BricksPlanner(new BricksFixture.Corpus(), model, answerCheck: true);
        Assert.AreEqual(before.PlannerId, off.PlannerId);
        Assert.AreNotEqual(before.PlannerId, on.PlannerId, "switched on, the check's instruction is part of the planner's identity");
        Assert.AreEqual(before.Plan(Input(Question), CancellationToken.None).PlanJson, off.Plan(Input(Question), CancellationToken.None).PlanJson);
        Assert.AreEqual(0, model.Calls.Count, "switched off, no check is asked");
        Assert.AreEqual(64, V4Generation.AnswerCheckSha256.Length);
    }
}
