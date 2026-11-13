using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform.Assistant;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The planner on the real model (2026-11-13): asked "Que dit l'article 1er de la loi du 27 juillet 1997 sur le contrat
/// d'assurance ?", the live assistant searched "l'article 1er de la loi du 27 juillet 1997" alone, which several acts share, and
/// asked which act was meant; the search with the title words opens the insurance contract law. The prompt now says the query
/// runs to the end of the title words. This class calls the model, so it runs only when <c>LEX_ASSISTANT_PLANNER_LIVE</c> is 1
/// and the deployment's variables are set (the driver's run, never CI).
/// </summary>
[TestClass]
public sealed class AzureOpenAiPlannerLiveTests
{
    private const string LiveVariable = "LEX_ASSISTANT_PLANNER_LIVE";

    [TestMethod]
    public void ACitationWithTitleWordsIsSearchedWithThemAndOneWithNoneIsNotLengthened()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(LiveVariable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"{LiveVariable} is not 1: this test calls a model, and runs only when asked to.");
        }

        using var planner = AzureOpenAiAssistantPlanner.FromEnvironment(Environment.GetEnvironmentVariable)
            ?? throw new AssertInconclusiveException("The deployment's variables are not set (LEX_ASSISTANT_ENDPOINT, _DEPLOYMENT, _API_VERSION, _KEY).");
        var failures = new List<string>();
        foreach (var (question, mustHold) in new[]
                 {
                     ("Que dit l'article 1er de la loi du 27 juillet 1997 sur le contrat d'assurance ?", "sur le contrat d'assurance"),
                     ("Que dit l’article 1er de la loi du 27 juillet 1997 sur le contrat d’assurance ?", "sur le contrat d’assurance"),
                     ("Que dit l'article 1er de la loi du 7 décembre 2015 sur le secteur des assurances ?", "sur le secteur des assurances"),
                     ("Que dit l'article 100-1 de la loi du 10 août 1915 concernant les sociétés commerciales ?", "concernant les sociétés commerciales"),
                 })
        {
            var query = SearchQuery(planner, question);
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
            var query = SearchQuery(planner, question);
            if (query is null || !query.EndsWith(expected, StringComparison.Ordinal))
            {
                failures.Add($"{question} -> {query ?? "no search"}");
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    /// <summary>The words of the plan's first search step's query span, or null when the plan has no search with a span.</summary>
    private static string? SearchQuery(AzureOpenAiAssistantPlanner planner, string question)
    {
        // The input the site sends (V3AssistantPipeline): the masked question, the dates code read in it as references, and the
        // languages the mounted indexes search, in the release's order.
        string[] languages = ["deu", "eng", "fra"];
        var intake = V3AssistantParsers.Intake(question) ?? throw new AssertFailedException($"{question} is no question");
        var dates = V3AssistantParsers.DatesIn(intake.Text);
        var context = new V3PlanContext(intake, dates, V3AssistantParsers.RangesIn(intake.Text, dates), [], languages);
        var output = planner.Plan(new V3PlannerInput(intake.Masked, V3AnswerLocale.Fr, context.References(), languages), CancellationToken.None);
        using var plan = JsonDocument.Parse(output.PlanJson);
        foreach (var step in plan.RootElement.GetProperty("steps").EnumerateArray())
        {
            if (step.GetProperty("operation_id").GetString() != "search")
            {
                continue;
            }

            if (step.GetProperty("arguments").TryGetProperty("query", out var query) && query.TryGetProperty("span", out var span))
            {
                return intake.Masked[span[0].GetInt32()..span[1].GetInt32()];
            }
        }

        return null;
    }
}
