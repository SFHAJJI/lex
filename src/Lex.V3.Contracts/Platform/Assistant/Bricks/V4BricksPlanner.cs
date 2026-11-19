using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>
/// The bricks behind the pipeline's one seam (V4): what stands where the planner model stood. Code reads the question,
/// decides the route and writes the plan; a model is asked only to describe a question the tables did not read, to rank
/// candidates it is given and to point at lines it is given. "The LLM appears at every brick. It never chooses the next call."
/// </summary>
/// <remarks>
/// No model plans any more: a question the tables do not answer (it cites an article, asks how the law changed, or names
/// nothing of the dictionary) is described by the model from closed lists, and code maps that description to operations
/// (<see cref="V4QuestionCall"/>). With no model, or none that answers, code searches the reader's own words.
/// With <paramref name="everyLaw"/> (LEX_V3_EVERY_LAW exactly <c>on</c>), a question those operations quoted nothing for
/// is read once more, in the acts of the law index its words route to, and shown as candidates
/// (<see cref="V4Bricks.PlanEveryLaw"/>). With <paramref name="answerCheck"/> (LEX_V3_ANSWER_CHECK exactly <c>on</c>), an answer
/// the plan asserts is checked against the first article it quotes (<see cref="V4Bricks.CheckAnswer"/>).
/// </remarks>
public sealed class V4BricksPlanner(IV4Corpus corpus, IV4Model? model, bool everyLaw = false, V4LawIndex? laws = null, bool answerCheck = false) : IAssistantWiderPlanner
{
    private readonly IV4Corpus _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
    private readonly V4LawIndex? _laws = everyLaw ? laws ?? V4LawIndex.Embedded : null;

    /// <summary>
    /// The planner's identity, written into every trace: the bricks, the model they ask, and a digest of everything that
    /// decides an answer besides the corpus (the record's version, the instructions, the dictionary, the corpus index;
    /// with the wider reading switched on, the law index it routes with; with the answer check switched on, the check's
    /// instruction). Switched off, it is the identity it was.
    /// </summary>
    public string PlannerId { get; } = "lex-v4-bricks/" + (model?.ModelId ?? "code-only") + "/prompt-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        V4PlanRecord.Version + "\n" + V4Generation.PromptsSha256 + "\n" + V4QuestionCall.PromptSha256 + "\n" + V4Dictionary.Insurance.Sha256 + "\n" + V4CorpusIndex.Insurance.Sha256 +
        (everyLaw && (laws ?? V4LawIndex.Embedded) is { } index ? "\n" + index.Sha256 : string.Empty) +
        (answerCheck ? "\n" + V4Generation.AnswerCheckSha256 : string.Empty))))[..12];

    public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var today = input.Today ?? throw new InvalidOperationException("The bricks need today's date to read the version in force.");
        var asked = new V4PlanInput(input.MaskedQuestion, input.AnswerLocale, today, V3AssistantParsers.DatesIn(input.MaskedQuestion), input.References, input.SearchLanguages);
        var outcome = V4Bricks.Plan(asked, _corpus, model, cancellationToken);

        // The answer check (LEX_V3_ANSWER_CHECK exactly `on`): an answer the plan asserts is read once more against the
        // first article it quotes, and one the check rejects is shown as candidates (V4Bricks.CheckAnswer).
        if (answerCheck)
        {
            outcome = V4Bricks.CheckAnswer(asked, outcome, _corpus, model, cancellationToken);
        }

        return new V3PlannerOutput(
            outcome.PlanJson ?? throw new InvalidOperationException("The bricks wrote no plan (" + outcome.Reason + ")."),
            outcome.CostMicroEuros);
    }

    /// <summary>
    /// The wider reading of a question Lex's own operations quoted nothing for: a plan of candidates from one act of the law
    /// index, or null when the switch is off, the question is not one it reads, or nothing was found. What the calls cost is
    /// reported either way.
    /// </summary>
    public V3PlannerOutput? PlanWider(V3PlannerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_laws is null || model is null || input.Today is not { } today)
        {
            return null;
        }

        var outcome = V4Bricks.PlanEveryLaw(
            new V4PlanInput(input.MaskedQuestion, input.AnswerLocale, today, V3AssistantParsers.DatesIn(input.MaskedQuestion), input.References, input.SearchLanguages) { EveryLaw = true, Laws = _laws },
            _corpus,
            model,
            cancellationToken);
        return outcome.PlanJson is null && outcome.CostMicroEuros == 0 ? null : new V3PlannerOutput(outcome.PlanJson ?? string.Empty, outcome.CostMicroEuros);
    }
}
