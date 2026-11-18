using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.Contracts.Platform.Assistant.Bricks;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The question call on the real model: what it points at when a question cites an article or names a subject outside
/// the dictionary. The cases are the ones the planner's prompt was corrected for (2026-11-13: a citation searched without
/// the title words that tell two acts of one date apart; a concept question searched by its last noun phrase; a dated
/// question read as the act on a date, which quotes nothing). This class calls the model, so it runs only when
/// <c>LEX_ASSISTANT_PLANNER_LIVE</c> is 1 and the deployment's variables are set (the driver's run, never CI).
/// </summary>
[TestClass]
public sealed class AzureOpenAiModelLiveTests
{
    private const string LiveVariable = "LEX_ASSISTANT_PLANNER_LIVE";

    private static AzureOpenAiModel Live()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(LiveVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"{LiveVariable} is not 1: this test calls a model, and runs only when asked to.");
        }

        return AzureOpenAiModel.FromEnvironment(Environment.GetEnvironmentVariable)
            ?? throw new AssertInconclusiveException("The deployment's variables are not set (LEX_ASSISTANT_ENDPOINT, _DEPLOYMENT, _API_VERSION, _KEY).");
    }

    [TestMethod]
    public void ACitationWithTitleWordsIsSearchedWithThemAndOneWithNoneIsNotLengthened()
    {
        using var model = Live();
        var failures = new List<string>();
        foreach (var (question, mustHold) in new[]
                 {
                     ("Que dit l'article 1er de la loi du 27 juillet 1997 sur le contrat d'assurance ?", "sur le contrat d'assurance"),
                     ("Que dit l’article 1er de la loi du 27 juillet 1997 sur le contrat d’assurance ?", "sur le contrat d’assurance"),
                     ("Que dit l'article 1er de la loi du 7 décembre 2015 sur le secteur des assurances ?", "sur le secteur des assurances"),
                     ("Que dit l'article 100-1 de la loi du 10 août 1915 concernant les sociétés commerciales ?", "concernant les sociétés commerciales"),
                 })
        {
            var (query, _, _) = SearchStep(model, question);
            if (query is null || !query.Contains(mustHold, StringComparison.Ordinal) || query.EndsWith('?'))
            {
                failures.Add($"{question} -> {query ?? "no search"}");
            }
        }

        // A citation by its date alone keeps its own words: nothing is added.
        foreach (var (question, expected) in new[]
                 {
                     ("Que dit l'article 12 de la loi du 4 avril 2019 ?", "article 12 de la loi du 4 avril 2019"),
                     ("Que dit l'article 5 du RGPD ?", "article 5 du RGPD"),
                 })
        {
            var (query, _, _) = SearchStep(model, question);
            if (query is null || !query.EndsWith(expected, StringComparison.Ordinal))
            {
                failures.Add($"{question} -> {query ?? "no search"}");
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A question on a subject the dictionary does not hold keeps its concept in the words searched, and its dated form is
    /// the same search with the date, never the act on a date. Questions of several subjects, none of them the instruction's example.
    /// </summary>
    [TestMethod]
    public void AConceptQuestionKeepsItsConceptAndItsDatedFormIsTheSameSearchWithTheDate()
    {
        using var model = Live();
        var failures = new List<string>();
        foreach (var (question, concept) in new[]
                 {
                     ("Quelle est la durée du préavis de licenciement d'un salarié ?", "préavis de licenciement"),
                     ("Que prévoit la loi sur le congé parental ?", "congé parental"),
                     ("Quelles sont les conditions de constitution d'une société à responsabilité limitée ?", "société à responsabilité limitée"),
                     ("Quel est le délai de conservation des données personnelles ?", "conservation des données"),
                 })
        {
            foreach (var dated in new[] { false, true })
            {
                var asked = dated ? question.Replace(" ?", " au 1er janvier 2020 ?", StringComparison.Ordinal) : question;
                var (query, hasDate, operations) = SearchStep(model, asked);
                if (query is null || !query.Contains(concept, StringComparison.Ordinal) || operations.Contains("as_of"))
                {
                    failures.Add($"{asked} -> search {query ?? "none"}; operations {string.Join(",", operations)}");
                }
                else if (dated && !hasDate)
                {
                    failures.Add($"{asked} -> the search reads no date");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The first search of the plan code writes from the model's description: its query words, whether it reads a date that
    /// is not today, and every operation of the plan. The input is the one the site sends (the masked question, the dates
    /// code read in it as references, the languages the mounted indexes search).
    /// </summary>
    private static (string? Query, bool HasDate, string[] Operations) SearchStep(AzureOpenAiModel model, string question)
    {
        string[] languages = ["deu", "eng", "fra"];
        var intake = V3AssistantParsers.Intake(question) ?? throw new AssertFailedException($"{question} is no question");
        var dates = V3AssistantParsers.DatesIn(intake.Text);
        var context = new V3PlanContext(intake, dates, V3AssistantParsers.RangesIn(intake.Text, dates), [], languages);
        var output = new V4BricksPlanner(new BricksFixture.Corpus(), model).Plan(
            new V3PlannerInput(intake.Masked, V3AnswerLocale.Fr, context.References(), languages, BricksFixture.Today), CancellationToken.None);
        using var plan = JsonDocument.Parse(output.PlanJson);
        var operations = plan.RootElement.GetProperty("steps").EnumerateArray()
            .Concat(plan.RootElement.GetProperty("follow_ups").EnumerateArray())
            .Select(static step => step.GetProperty("operation_id").GetString()!).ToArray();
        foreach (var step in plan.RootElement.GetProperty("steps").EnumerateArray())
        {
            if (step.GetProperty("operation_id").GetString() != "search")
            {
                continue;
            }

            var arguments = step.GetProperty("arguments");
            var query = arguments.TryGetProperty("query", out var q) && q.TryGetProperty("span", out var span)
                ? intake.Masked[span[0].GetInt32()..span[1].GetInt32()]
                : null;
            var hasDate = arguments.TryGetProperty("date", out var date) && !date.TryGetProperty("today", out _);
            return (query, hasDate, operations);
        }

        return (null, false, operations);
    }
}
