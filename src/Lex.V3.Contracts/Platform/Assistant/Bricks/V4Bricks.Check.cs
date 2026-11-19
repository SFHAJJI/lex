using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>
/// The answer check (the owner's order of 2026-11-19, measured the same day on 163 labelled answers): after a plan asserts
/// an answer, one model call reads the question and the whole first article quoted and says whether that article answers
/// the question. An answer it rejects is shown as candidates (the same passages, nothing asserted); an answer it accepts
/// stands as it was; when the call fails or its reply is not of the form, the answer stands as it was. The reader who cited
/// an article by its number gets that article and no check. Code decides; the model's reason is read by no one.
/// </summary>
public static partial class V4Bricks
{
    /// <summary>How much of the article the check is given, in characters.</summary>
    internal const int AnswerCheckArticleCharacters = 7000;

    /// <summary>The most the check may write, in tokens.</summary>
    internal const int AnswerCheckTokens = 120;

    /// <summary>The check's call for a question and one article: its instruction, and the question with the article's stored text.</summary>
    internal static V4ModelCall AnswerCheckCall(string maskedQuestion, string workKey, string articleId, string articleText)
    {
        var text = articleText.Length > AnswerCheckArticleCharacters ? articleText[..AnswerCheckArticleCharacters] : articleText;
        return new V4ModelCall(
            "check",
            V4Generation.AnswerCheckPrompt,
            "QUESTION:\n" + maskedQuestion + "\n\nARTICLE (" + workKey + ", " + articleId + "):\n" + text,
            "lex_v4_answer_check",
            V4Generation.AnswerCheckSchema,
            AnswerCheckTokens);
    }

    /// <summary>The check's reply: whether the article answers, and whether it answers alone. Null when it is not of the form.</summary>
    internal static (bool Found, bool Complete)? ReadAnswerCheck(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("answer_found", out var found) || found.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("complete_answer_found", out var complete) || complete.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            return (found.ValueKind == JsonValueKind.True, complete.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A plan as the answer check leaves it. Checked: a plan that asserts an answer (not candidates, an absence, a
    /// clarification, a refusal or a plan of Lex's own operations) and quotes at least one article, for a question that
    /// cites no article by its number. Rejected, the same passages are candidates; accepted, the plan stands; the call and
    /// what it said are in the plan's record either way. A call that fails leaves the plan as it was, byte for byte.
    /// </summary>
    public static V4PlanOutcome CheckAnswer(V4PlanInput input, V4PlanOutcome outcome, IV4Corpus corpus, IV4Model? model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(corpus);
        if (model is null || outcome.PlanJson is not { } json || outcome.Reason.StartsWith("route:cited", StringComparison.Ordinal))
        {
            return outcome;
        }

        // What would be shown in place of the assertion. The same instance comes back when the plan asserts nothing.
        var candidates = AsCandidates(json);
        if (ReferenceEquals(candidates, json))
        {
            return outcome;
        }

        var row = V4QuestionParser.Parse(input.MaskedQuestion, input.Locale, V4Dictionary.Insurance, V4CorpusIndex.Insurance);
        if (V4Dispatcher.CitesArticle(row))
        {
            return outcome;
        }

        // The first article quoted, as the plan names it: the act, the article, and the date the plan reads it on.
        string work;
        string articleId;
        string date;
        try
        {
            var plan = JsonNode.Parse(json)!.AsObject();
            var first = plan["bricks"]!["answer"]!["items"]![0]!["step"]!.GetValue<string>();
            JsonObject? arguments = null;
            foreach (var step in plan["steps"]!.AsArray())
            {
                if (step?["step_id"]?.GetValue<string>() == first)
                {
                    arguments = step["arguments"] as JsonObject;
                    break;
                }
            }

            if (arguments is null)
            {
                return outcome with { Reason = outcome.Reason + "+answer_check_unavailable" };
            }

            work = arguments["identifier"]!["corpus"]!.GetValue<string>();
            articleId = arguments["article"]!["corpus"]!.GetValue<string>();
            var asked = arguments["date"] as JsonObject;
            date = input.Today;
            if (asked?["corpus"] is { } held)
            {
                date = held.GetValue<string>();
            }
            else if (asked?["ref"] is { } reference)
            {
                foreach (var parsed in input.Dates)
                {
                    if (parsed.Ref == reference.GetValue<string>())
                    {
                        date = parsed.Iso;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is NullReferenceException or InvalidOperationException or JsonException or ArgumentOutOfRangeException or FormatException)
        {
            return outcome with { Reason = outcome.Reason + "+answer_check_unavailable" };
        }

        const string prefix = "/lu-legilux/";
        var key = work.StartsWith(prefix, StringComparison.Ordinal) ? work[prefix.Length..] : work;
        var tables = corpus.Act(key, date).Tables;
        if (tables?.Article(articleId) is not { } article)
        {
            return outcome with { Reason = outcome.Reason + "+answer_check_unavailable" };
        }

        var text = new StringBuilder();
        foreach (var line in tables.LinesOf(article))
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(line.Text);
        }

        if (text.Length == 0)
        {
            return outcome with { Reason = outcome.Reason + "+answer_check_unavailable" };
        }

        var call = AnswerCheckCall(input.MaskedQuestion, key, articleId, text.ToString());
        V4ModelReply reply;
        try
        {
            reply = model.Complete(call, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A model that fails or times out leaves the answer as it was.
            return outcome with { Reason = outcome.Reason + "+answer_check_unavailable" };
        }

        var said = ReadAnswerCheck(reply.Content);
        var rejected = said is { Found: false };
        var written = JsonNode.Parse(rejected ? candidates : json)!.AsObject();
        if (written["bricks"] is JsonObject bricks)
        {
            if (bricks["model_calls"] is not JsonArray calls)
            {
                calls = [];
                bricks["model_calls"] = calls;
            }

            calls.Add(new JsonObject
            {
                ["step"] = call.Step,
                ["model"] = Token(model.ModelId),
                ["prompt"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.System + "\n" + call.User)))[..16],
                ["reply"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reply.Content))),
                ["input_tokens"] = Math.Max(0, reply.InputTokens),
                ["output_tokens"] = Math.Max(0, reply.OutputTokens),
            });

            if (bricks["passes"] is not JsonArray passes)
            {
                passes = [];
                bricks["passes"] = passes;
            }

            passes.Add(new JsonObject
            {
                ["n"] = passes.Count + 1,
                ["trigger"] = "answer_check",
                ["changed"] = rejected ? "answer_to_candidates" : "none",
                ["outcome"] = said is not { } read ? "reply_unreadable" : !read.Found ? "rejected" : read.Complete ? "accepted_complete" : "accepted_not_complete",
            });
        }

        return outcome with
        {
            PlanJson = written.ToJsonString(),
            CostMicroEuros = outcome.CostMicroEuros + reply.CostMicroEuros,
            Reason = outcome.Reason + (said is null ? "+answer_check_unavailable" : rejected ? "+answer_check_rejected" : "+answer_check_accepted"),
        };
    }
}
