using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>
/// The second pass (the owner's brief of 2026-11-19, work item 1; brick I's outline descent, by a model): a question of
/// the domain that no dictionary key reads is routed by the tables of contents of the acts Lex reads on the subject, and
/// the articles named are read by the extraction call, which points at lines. Code then quotes those lines, as for every
/// answer. A model names ids it was given and nothing else: an id that was not given refuses the whole reply.
/// </summary>
public static partial class V4Bricks
{
    /// <summary>The most lines of the articles the outline names that the extraction is given.</summary>
    public const int MaximumOutlineLines = 120;

    private static V4PlanOutcome ByTheOutline(
        V4PlanInput input,
        V4QuestionRow row,
        IV4Corpus corpus,
        IV4Model model,
        CancellationToken cancellationToken,
        (V4ModelCall Call, V4ModelReply Reply, string Model)? prior,
        bool ownCase)
    {
        var dictionary = V4Dictionary.Insurance;
        var index = V4CorpusIndex.Insurance;
        var question = input.MaskedQuestion;

        // The outline is read for a question of this domain only: a word of the dictionary, an act of the index, or a
        // word of the domain. Any other subject keeps Lex's own search, with no call spent here.
        if (row.Greeting || row.OtherDomain || (row.Concepts.Count == 0 && row.Roles.Count == 0 && row.Acts.Count == 0 && !index.InDomain(row.Words)))
        {
            return new V4PlanOutcome(null, 0, "outline_not_of_the_domain");
        }

        var reading = V4Dispatcher.ReadingDates(row, input.Dates);
        if (reading.Count > 1)
        {
            return new V4PlanOutcome(null, 0, "outline_several_dates");
        }

        var dateRef = reading.Count == 1 ? reading[0].Ref : "today";
        var date = reading.Count == 1 ? reading[0].Iso : input.Today;
        var held = new List<(V4IndexAct Act, V4ActTables Tables)>();
        var latest = new Dictionary<V4ActTables, string>(ReferenceEqualityComparer.Instance);
        foreach (var act in index.Acts)
        {
            var at = corpus.Act(act.Work, date);
            if (at.Tables is { } tables)
            {
                held.Add((act, tables));
                if (at.Facts.TextStatus == "latest_held" && at.Facts.VersionFrom is { } from)
                {
                    latest[tables] = from;
                }
            }
        }

        if (held.Count == 0)
        {
            return new V4PlanOutcome(null, 0, "outline_no_act_held");
        }

        var draft = new Draft { Route = "open", AnswerKind = "text" };
        if (prior is { } call)
        {
            draft.Cost += call.Reply.CostMicroEuros;
            draft.Calls.Add((
                call.Call.Step,
                call.Model,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.Call.System + "\n" + call.Call.User)))[..16],
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.Reply.Content))),
                call.Reply.InputTokens,
                call.Reply.OutputTokens));
        }

        // Step 1: the model names articles of the outlines, by ids it was given.
        var (outline, ids) = V4Generation.OutlineCall(question, held);
        var routed = Ask(draft, model, outline, cancellationToken);
        var picks = routed is null ? null : V4Generation.ReadOutline(routed.Content, ids.Count);
        if (picks is null || picks.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, picks is null ? "outline_unreadable" : "outline_names_nothing");
        }

        // One act answers: the act of the first article named; the articles named of another act are left.
        var scope = ids[picks[0]].Tables;
        var articles = picks.Select(pick => ids[pick]).Where(pick => ReferenceEquals(pick.Tables, scope)).Select(static pick => pick.Article).ToList();
        var lines = new List<V4Line>();
        foreach (var article in articles)
        {
            foreach (var line in scope.TextLinesOf(article))
            {
                if (lines.Count < MaximumOutlineLines)
                {
                    lines.Add(line);
                }
            }
        }

        if (lines.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, "outline_articles_hold_no_line");
        }

        // Step 2: the extraction points at the lines that answer, among those lines only; code checks each.
        draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", "outline_read"));
        var reply = Ask(draft, model, V4Generation.ExtractionCall(row, "text", scope, lines, [], dictionary), cancellationToken);
        var extraction = reply is null ? null : V4Generation.ReadExtraction(reply.Content);
        var items = extraction is null ? null : V4Generation.Validate(extraction, "text", lines, out _);
        if (items is null || items.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, items is null ? "outline_reading_refused" : "outline_reading_found_nothing");
        }

        // One passage per article, the most direct first, as the model gave them.
        var kept = new List<V4Generation.Checked>();
        foreach (var item in items)
        {
            if (!kept.Any(other => other.Line.Article == item.Line.Article))
            {
                kept.Add(item);
            }
        }

        draft.Found = true;
        draft.LatestHeld = latest.TryGetValue(scope, out var heldOn) ? heldOn : null;
        draft.Caveats = extraction!.Caveats.ToList();
        draft.Items = kept.Take(V3AssistantPipeline.MaximumQuotes).Select(static item => new Item(item.Line, item.Line, "passage", null)).ToList();
        var quoted = draft.Items.Select(static item => item.First.Article).ToHashSet(StringComparer.Ordinal);
        draft.More = articles.Select(static article => article.Id).Where(id => !quoted.Contains(id)).Distinct(StringComparer.Ordinal).Take(V3AssistantPipeline.MaximumCandidates).ToList();

        var cues = V3AssistantParsers.ApplicationCues(question, 0, question.Length);
        var ruleQuestion = cues.Count > 0 && V4Boundary.RuleQuestion(question, 0, question.Length, cues);
        if (((cues.Count > 0 && !ruleQuestion) || ownCase) && !draft.Caveats.Contains("not_your_case"))
        {
            // Lines of the law were found: they are quoted as the general rule, and the answer says so of the reader's case.
            draft.Caveats.Add("not_your_case");
        }

        var json = Write(input, row, draft, scope, null, null, dateRef, ruleQuestion, "answer", "text", scope.Work, []);
        return new V4PlanOutcome(json, draft.Cost, "route:outline", Articles: draft.Items.Select(static item => item.First.Article).Distinct(StringComparer.Ordinal).ToList());
    }
}
