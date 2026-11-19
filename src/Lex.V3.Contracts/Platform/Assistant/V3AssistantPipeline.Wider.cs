using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// A planner that can read a question once more, wider, after the plan it first gave was run and quoted nothing (the
/// owner's order of 2026-11-19: every Luxembourg act, candidates only). The pipeline asks it only then; a planner that has
/// nothing wider to offer answers null, and the first answer stands.
/// </summary>
public interface IAssistantWiderPlanner : IAssistantPlanner
{
    /// <summary>
    /// A second plan for the same question, or null. An empty plan reports what was spent finding none.
    /// <paramref name="reason"/> says, in one token, how the reading ended (which stop of code, which call named nothing);
    /// null when the planner has no wider reading to offer at all (switched off).
    /// </summary>
    V3PlannerOutput? PlanWider(V3PlannerInput input, CancellationToken cancellationToken, out string? reason);
}

/// <summary>
/// What the answer check said of an answer Lex's own operations asserted (the owner's order of 2026-11-19).
/// </summary>
/// <param name="Outcome">One token: accepted_complete, accepted_not_complete, rejected, rejected_several_acts, reply_unreadable or unavailable.</param>
/// <param name="Rejected">The answer is not to be asserted.</param>
/// <param name="CallJson">The model call as a plan's record holds one (step <c>check</c>), or null when no reply came.</param>
/// <param name="CostMicroEuros">What the call cost.</param>
/// <param name="CandidatesPlanJson">For a rejected answer: a plan that shows the same article as candidates, or null.</param>
public sealed record V3AnswerCheckResult(string Outcome, bool Rejected, string? CallJson, long CostMicroEuros, string? CandidatesPlanJson);

/// <summary>
/// A planner that can check an answer its plan of Lex's own operations asserted once the plan was run: the search named
/// one article and it was read as the answer, with nothing checking it. Null when it has no check to make of this
/// question (switched off, an article cited by its number, an EU act named, an act asked alone).
/// </summary>
public interface IAssistantAnswerChecker : IAssistantPlanner
{
    V3AnswerCheckResult? CheckRun(V3PlannerInput input, string work, string article, bool severalActs, CancellationToken cancellationToken);
}

public static partial class V3AssistantPipeline
{
    /// <summary>
    /// Whether a plan of Lex's own operations was run and gave law as the answer: the first act and article it quotes,
    /// and whether it quotes more than one act.
    /// </summary>
    private static bool AssertedByOperations(V3AssistantPlan plan, V3AnswerDossier dossier, out string work, out string article, out bool severalActs)
    {
        work = string.Empty;
        article = string.Empty;
        severalActs = false;
        if (plan.Bricks is not { Kind: "operations" } || dossier.Verdict != V3AssistantVerdict.Answer)
        {
            return false;
        }

        foreach (var quote in dossier.Quotes)
        {
            if (work.Length == 0)
            {
                work = quote.Work ?? string.Empty;
                article = quote.Anchor ?? string.Empty;
            }
            else if (!string.Equals(work, quote.Work, StringComparison.Ordinal))
            {
                severalActs = true;
            }
        }

        return work.Length > 0 && article.Length > 0;
    }

    /// <summary>A plan with what the answer check said written in its record: the call (step <c>check</c>) and <c>bricks.answer_check</c>.</summary>
    private static string WithCheck(string planJson, V3AnswerCheckResult check)
    {
        try
        {
            if (JsonNode.Parse(planJson) is not JsonObject plan || plan["bricks"] is not JsonObject bricks)
            {
                return planJson;
            }

            bricks["answer_check"] = check.Outcome;
            if (check.CallJson is { } call && JsonNode.Parse(call) is JsonObject made)
            {
                if (bricks["model_calls"] is not JsonArray calls)
                {
                    calls = [];
                    bricks["model_calls"] = calls;
                }

                calls.Add(made);
            }

            return plan.ToJsonString();
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return planJson;
        }
    }

    /// <summary>A plan with how the wider reading ended written in its record (<c>bricks.wider</c>, one token).</summary>
    private static string WithWider(string planJson, string why)
    {
        try
        {
            if (JsonNode.Parse(planJson) is not JsonObject plan || plan["bricks"] is not JsonObject bricks)
            {
                return planJson;
            }

            var token = new string(why.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '+' or ':' or '.' or '-' ? character : '_').ToArray());
            bricks["wider"] = token.Length > 120 ? token[..120] : token.Length == 0 ? "none" : token;
            return plan.ToJsonString();
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return planJson;
        }
    }

    /// <summary>A plan validated and run: its answer, or null when it is not admitted, fails a gate, or anything of it fails.</summary>
    private static V3AnswerDossier? Rerun(string planJson, string plannerId, V3PlanContext context, V3AnswerLocale locale, V3StepExecutor execute, V3AssistantEnvironment environment)
    {
        try
        {
            var validation = V3AssistantPlanValidator.Validate(planJson, context);
            return validation.Admitted ? Run(validation.Plan!, plannerId, context, locale, execute, environment) : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
    /// <summary>
    /// Whether an answer is one the wider reading may replace: a plan of Lex's own operations that was run and quoted
    /// nothing, ending on a clarification, a pointer, or a refusal with no text beside it. An answer that quotes, an act
    /// itself shown and every answer of the bricks' own routes stand as they are: the wider reading never takes an answer
    /// away. Which refusals stay refusals is decided by the planner, in code (a cue of R15 that is no question on the rule,
    /// a decision asked on the reader's own facts): for those it offers no second plan.
    /// </summary>
    private static bool QuotedNothing(V3AssistantPlan plan, V3AnswerDossier dossier)
    {
        if (plan.Bricks is not { Kind: "operations" } || dossier.Verdict is not (V3AssistantVerdict.Clarify or V3AssistantVerdict.Point or V3AssistantVerdict.Refuse))
        {
            return false;
        }

        foreach (var part in dossier.Parts)
        {
            foreach (var block in part.Blocks)
            {
                if (block is V3QuoteBlock)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The answer of the wider reading, or null when there is none to give: the planner offers no second plan, the plan is
    /// not admitted, is not a bricks answer, fails a gate, or quotes nothing. Anything that goes wrong here leaves the
    /// first answer in place.
    /// </summary>
    private static V3AnswerDossier? Wider(
        IAssistantWiderPlanner planner,
        V3PlannerInput input,
        V3PlanContext context,
        V3AnswerLocale locale,
        V3StepExecutor execute,
        V3AssistantEnvironment environment,
        CancellationToken cancellationToken,
        out string? why,
        V3AnswerCheckResult? check = null)
    {
        why = null;
        try
        {
            var output = planner.PlanWider(input, cancellationToken, out why);
            if (output is null || string.IsNullOrEmpty(output.PlanJson))
            {
                return null;
            }

            // The answer this reading replaces was rejected by the answer check: the record of the answer shown says so.
            var validation = V3AssistantPlanValidator.Validate(check is null ? output.PlanJson : WithCheck(output.PlanJson, check), context);
            if (!validation.Admitted || validation.Plan!.Bricks is null || validation.Plan.Bricks.Kind == "operations")
            {
                why = "wider_plan_refused";
                return null;
            }

            var dossier = Run(validation.Plan, planner.PlannerId, context, locale, execute, environment);
            if (dossier is null)
            {
                why = "wider_gate_failed";
                return null;
            }

            foreach (var part in dossier.Parts)
            {
                foreach (var block in part.Blocks)
                {
                    if (block is V3QuoteBlock)
                    {
                        return dossier;
                    }
                }
            }

            why = "wider_quoted_nothing";
            return null;
        }
        catch (OperationCanceledException)
        {
            why = "wider_deadline";
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            why = "wider_failed";
            return null;
        }
    }
}
