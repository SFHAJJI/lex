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

public static partial class V3AssistantPipeline
{
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
        out string? why)
    {
        why = null;
        try
        {
            var output = planner.PlanWider(input, cancellationToken, out why);
            if (output is null || string.IsNullOrEmpty(output.PlanJson))
            {
                return null;
            }

            var validation = V3AssistantPlanValidator.Validate(output.PlanJson, context);
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

    /// <summary>
    /// The first answer again, with how the wider reading ended written in its plan's record (<c>bricks.wider</c>, one
    /// token), so that an answer the wider reading did not replace says why in its own trace. The same plan, validated and
    /// run again; null when anything of that fails, and the first answer then stands as it was.
    /// </summary>
    private static V3AnswerDossier? Marked(
        string planJson,
        string why,
        string plannerId,
        V3PlanContext context,
        V3AnswerLocale locale,
        V3StepExecutor execute,
        V3AssistantEnvironment environment)
    {
        try
        {
            if (JsonNode.Parse(planJson) is not JsonObject plan || plan["bricks"] is not JsonObject bricks)
            {
                return null;
            }

            var token = new string(why.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '+' or ':' or '.' or '-' ? character : '_').ToArray());
            bricks["wider"] = token.Length > 120 ? token[..120] : token.Length == 0 ? "none" : token;
            var validation = V3AssistantPlanValidator.Validate(plan.ToJsonString(), context);
            return validation.Admitted ? Run(validation.Plan!, plannerId, context, locale, execute, environment) : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}
