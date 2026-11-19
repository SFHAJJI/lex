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
        bool ownCase,
        (string Work, V4Line Line)? tablesLead = null)
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

        // An act the index does not name (an EU act, a law named by a date no act of the index bears) is not in these
        // tables, and a question put to Lex itself ("remplaces-tu un avocat ?") is not about what the acts provide.
        if (row.Acts.Any(static act => act.Work is null) || (row.Acts.Count == 0 && V4Dispatcher.NamesAnActByItsDate(row.Folded)))
        {
            return new V4PlanOutcome(null, 0, "outline_names_another_act");
        }

        if (V4Dispatcher.AddressesLex(row.Folded))
        {
            return new V4PlanOutcome(null, 0, "outline_put_to_lex");
        }

        // An opinion asked ("quelle est la meilleure assurance auto ?"), or a subject left unsaid with no concept read
        // ("mon assureur a-t-il le droit de me faire ça ?"): no article states it, and none is looked for.
        if (V4Dispatcher.AsksAnOpinion(row.Folded) || (row.Concepts.Count == 0 && V4Dispatcher.LeavesItsSubjectUnsaid(row.Folded)))
        {
            return new V4PlanOutcome(null, 0, "outline_no_rule_asked");
        }

        // A question that names an act and nothing of the domain beside it asks of the act itself (its summary, how many
        // articles it holds, what it said in a year): Lex's own operations answer that, not a reading of its articles.
        if (row.Acts.Count > 0 && row.Concepts.Count == 0 && row.Roles.Count == 0 && row.Kinds.Count == 0 &&
            !index.InDomain(row.Words.Where(word => !row.Acts.Any(act => word.Start >= act.Start && word.End <= act.End)).ToList()))
        {
            return new V4PlanOutcome(null, 0, "outline_asks_of_the_act");
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

        // A branch of insurance the reader names ("assurance auto") has its own act: the tables of contents read are those
        // of the acts the question's concepts live in, the branch's and the others' ("résilier mon assurance auto": the
        // contract law and the motor law), and no other act of the domain (2026-11-19: "puisque l'assurance auto n'est
        // pas obligatoire ..." was routed to the sector law).
        var read = row.Concepts.Select(entry => dictionary.Concept(entry.Concept)!).ToList();
        if (read.Any(static concept => concept.Branch))
        {
            var homes = read.SelectMany(static concept => concept.HomeActs).ToHashSet(StringComparer.Ordinal);
            if (held.Any(entry => homes.Contains(entry.Act.Work)))
            {
                held = held.Where(entry => homes.Contains(entry.Act.Work)).ToList();
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

        // Code adds what a heading cannot show: the articles that hold a rare word of the question itself ("données
        // génétiques" stands on two lines of the act, under headings that do not say so). A word no table read, of six
        // letters or more, found in at most four articles; at most three articles are added. The model named the act;
        // code adds only articles of that act, and the extraction still decides which lines answer.
        foreach (var unread in row.Unmatched)
        {
            var word = row.Folded[unread.Start..unread.End];
            if (word.Length < 6 || articles.Count >= V4Generation.MaximumOutlinePicks + 3)
            {
                continue;
            }

            var stem = V4Text.Stem(word);
            var holding = scope.Lines.Where(line => line.Kind == "paragraph" && line.Words.Any(written => written.Text.Length >= 4 && V4Text.Stem(written.Text) == stem)).Select(static line => line.Article).Distinct(StringComparer.Ordinal).ToList();
            if (holding.Count is >= 1 and <= 4)
            {
                foreach (var id in holding)
                {
                    if (scope.Article(id) is { } more && !articles.Any(article => article.Id == id) && articles.Count < V4Generation.MaximumOutlinePicks + 3)
                    {
                        articles.Add(more);
                    }
                }
            }
        }
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
        var readArticles = draft.Items.Select(static item => item.First.Article).Distinct(StringComparer.Ordinal).ToList();

        // The tables answered too, without being sure, and this reading quotes another article of the same act: the
        // tables' first line is quoted after this reading's own, by code (the reader sees both; neither is sure).
        if (tablesLead is { } lead && lead.Work == scope.Work && !readArticles.Contains(lead.Line.Article, StringComparer.Ordinal) &&
            scope.Line(lead.Line.Id) is { } theirs && draft.Items.Count < V3AssistantPipeline.MaximumQuotes)
        {
            draft.Items.Add(new Item(theirs, theirs, "passage", null, ByCode: true));
        }

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
        return new V4PlanOutcome(json, draft.Cost, "route:outline", Articles: readArticles);
    }
}
