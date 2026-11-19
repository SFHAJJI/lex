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
/// </remarks>
public sealed class V4BricksPlanner(IV4Corpus corpus, IV4Model? model) : IAssistantPlanner
{
    private readonly IV4Corpus _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));

    /// <summary>
    /// The planner's identity, written into every trace: the bricks, the model they ask, and a digest of everything that
    /// decides an answer besides the corpus (the record's version, the instructions, the dictionary, the corpus index).
    /// </summary>
    public string PlannerId { get; } = "lex-v4-bricks/" + (model?.ModelId ?? "code-only") + "/prompt-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        V4PlanRecord.Version + "\n" + V4Generation.PromptsSha256 + "\n" + V4QuestionCall.PromptSha256 + "\n" + V4Dictionary.Insurance.Sha256 + "\n" + V4CorpusIndex.Insurance.Sha256)))[..12];

    public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var today = input.Today ?? throw new InvalidOperationException("The bricks need today's date to read the version in force.");
        var outcome = V4Bricks.Plan(
            new V4PlanInput(input.MaskedQuestion, input.AnswerLocale, today, V3AssistantParsers.DatesIn(input.MaskedQuestion), input.References, input.SearchLanguages),
            _corpus,
            model,
            cancellationToken);
        return new V3PlannerOutput(
            outcome.PlanJson ?? throw new InvalidOperationException("The bricks wrote no plan (" + outcome.Reason + ")."),
            outcome.CostMicroEuros);
    }
}
