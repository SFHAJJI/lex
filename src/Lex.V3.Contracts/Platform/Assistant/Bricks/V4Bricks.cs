using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>An act as it stood on a date: its tables when Lex can show its text, and what Lex's own index says of it.</summary>
public sealed record V4ActAtDate(V4ActTables? Tables, V4ActFacts Facts);

/// <summary>
/// The corpus as the bricks read it (brick A's columns filled by code, and brick B's tables): an act on a date, selected as
/// the evidence step selects it and under the same rights rule, so a line the bricks cite is a line the answer may quote.
/// </summary>
public interface IV4Corpus
{
    /// <summary>The act's tables on a date, or why there are none (the facts' text status).</summary>
    V4ActAtDate Act(string work, string date);

    /// <summary>Whether the mounted corpus holds an EU act, by its CELEX.</summary>
    bool HoldsEuAct(string celex);
}

/// <summary>What the bricks are given: the masked question, the answer's locale, today in Luxembourg and the dates the parser read.</summary>
public sealed record V4PlanInput(string MaskedQuestion, V3AnswerLocale Locale, string Today, IReadOnlyList<V3ParsedDate> Dates);

/// <summary>
/// What the bricks answer: a plan /2, or none with the reason (a question the bricks do not answer: it cites an article,
/// asks how the law changed, or names nothing of the dictionary), and what the model calls cost.
/// </summary>
public sealed record V4PlanOutcome(string? PlanJson, long CostMicroEuros, string Reason);

/// <summary>
/// The function behind the validator (brick H): parse the question, read the corpus context, decide the route, run one
/// pass of retrieval, extraction and validation, read the typed flags, run at most a bounded number of further passes, and
/// hand the composer a plan. "A loop that hits max_iterations doesn't crash. It returns the best output produced so far,
/// flagged" [13]. A model that fails, is refused by a check twice, or is not wired leaves the answer code found, said as such.
/// </summary>
public static class V4Bricks
{
    /// <summary>Their hard limit on passes.</summary>
    public const int MaximumPasses = 4;

    /// <summary>The most candidates the arbiter is given.</summary>
    public const int MaximumCandidates = 14;

    /// <summary>The most lines the extraction is given.</summary>
    public const int MaximumLines = 40;

    private sealed record Item(V4Line First, V4Line Last, string Role, V4Value? Value);

    private sealed class Draft
    {
        public string Route = "open";
        public string AnswerKind = "text";
        public bool Found;
        public bool Complete;
        public List<Item> Items = [];
        public List<string> Caveats = [];
        public List<string> More = [];
        public (int Lines, int Articles)? Usage;
        public string? Absence;
        public string? EuAct;
        public List<V4RecordPass> Passes = [];
        public List<(string Step, string Model, string Prompt, string Reply, int Input, int Output)> Calls = [];
        public long Cost;
    }

    public static V4PlanOutcome Plan(V4PlanInput input, IV4Corpus corpus, IV4Model? model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(corpus);
        var dictionary = V4Dictionary.Insurance;
        var index = V4CorpusIndex.Insurance;
        var question = input.MaskedQuestion;
        var row = V4QuestionParser.Parse(question, input.Locale, dictionary, index);
        var cues = V3AssistantParsers.ApplicationCues(question, 0, question.Length);
        var ruleQuestion = cues.Count > 0 && V4Boundary.RuleQuestion(question, 0, question.Length, cues);
        var application = cues.Count > 0 && !ruleQuestion;

        // Route 0: not a legal question. "which deserves a direct reply and no pipeline at all" [13]
        if (row.Greeting)
        {
            var greeting = new Draft { Route = "greeting", AnswerKind = "greeting" };
            return new V4PlanOutcome(Write(input, row, greeting, null, null, null, "today", ruleQuestion: false, proposed: "clarify", asked: string.Empty), 0, "route:greeting");
        }

        // Routes 2 and 3 are Lex's own: a cited article goes to the reference reader, a change over time to diff and its kin.
        if (row.OtherDomain)
        {
            return new V4PlanOutcome(null, 0, "other_domain");
        }

        if (V4Dispatcher.CitesArticle(row))
        {
            return new V4PlanOutcome(null, 0, "cites_article");
        }

        if (V4Dispatcher.AsksChange(row, input.Dates.Count))
        {
            return new V4PlanOutcome(null, 0, "asks_change");
        }

        if (row.Acts.FirstOrDefault(static act => act.EuAct is not null) is { } named)
        {
            var act = index.EuAct(named.EuAct!)!;
            if (corpus.HoldsEuAct(act.Celex))
            {
                return new V4PlanOutcome(null, 0, "eu_act_held");
            }

            var notHeld = new Draft { Route = "not_held", AnswerKind = "not_held", EuAct = act.Id };
            return new V4PlanOutcome(Write(input, row, notHeld, null, null, null, "today", ruleQuestion: false, proposed: "point", asked: string.Empty), 0, "route:not_held");
        }

        var asksDefinition = row.Kinds.Any(static kind => kind.Kind == "definition");
        if (row.Concepts.Count == 0 && row.Acts.Count == 0 && !asksDefinition)
        {
            // A role alone ("l'assuré ...") names half the lines of the act: without a concept there is nothing to filter on.
            return new V4PlanOutcome(null, 0, "nothing_of_the_dictionary");
        }

        var dateRef = input.Dates.Count == 1 ? input.Dates[0].Ref : "today";
        var date = input.Dates.Count == 1 ? input.Dates[0].Iso : input.Today;

        // Scope (step 5): the act the reader names; else the concept's home act; else, for a definition, the first act of the
        // domain that defines a term of the question.
        var works = row.Acts.Where(static act => act.Work is not null).Select(static act => act.Work!)
            .Concat(row.Concepts.Concat(row.Roles).SelectMany(read => dictionary.Concept(read.Concept)!.HomeActs))
            .Concat(asksDefinition && row.Concepts.Count == 0 ? index.Acts.Select(static act => act.Work) : [])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        V4ActAtDate? scope = null;
        List<(V4Definition Definition, int Start, int End)> terms = [];
        foreach (var work in works)
        {
            if (index.Act(work) is null)
            {
                continue;
            }

            var at = corpus.Act(work, date);
            scope ??= at;
            if (at.Tables is null)
            {
                continue;
            }

            var defined = V4Retrieval.DefinedTerms(at.Tables, row.Words);
            if (scope.Tables is null || (asksDefinition && terms.Count == 0 && defined.Count > 0))
            {
                scope = at;
                terms = defined;
            }

            if (!asksDefinition || terms.Count > 0)
            {
                if (ReferenceEquals(scope, at))
                {
                    terms = defined;
                }

                break;
            }
        }

        if (scope is null)
        {
            return new V4PlanOutcome(null, 0, "no_act_in_scope");
        }

        var tables = scope.Tables;
        if (row.Concepts.Count == 0 && row.Roles.Count == 0 && terms.Count == 0)
        {
            // An act is named, or a definition is asked, and nothing of the tables matched: Lex's own search answers.
            return new V4PlanOutcome(null, 0, "nothing_of_the_dictionary");
        }

        var decision = V4Dispatcher.Decide(
            new V4DispatchInput(row, application, EuActNotHeld: false, tables is not null, tables is { Summary.Headings: > 0 }, terms.Count > 0, model is not null), dictionary);
        var draft = new Draft { Route = decision.Route, AnswerKind = decision.Kind };
        V4RetrievalResult? retrieval = null;
        var notes = new List<V4AnswerNote>();
        if (tables is null)
        {
            draft.AnswerKind = "absence";
            draft.Absence = "text_not_held";
            return new V4PlanOutcome(
                Write(input, row, draft, null, null, decision, dateRef, ruleQuestion, "point", decision.Kind, "/lu-legilux/" + scope.Facts.Work), 0, "route:absence");
        }

        if (decision.Route == "definition")
        {
            Define(draft, tables, row, terms, dictionary);
        }
        else
        {
            var concepts = row.Concepts.Count > 0 ? row.Concepts.Select(static read => read.Concept).ToList() : row.Roles.Select(static read => read.Concept).ToList();
            var role = row.Concepts.Count > 0 ? row.Roles.FirstOrDefault()?.Concept : null;
            var brief = new V4Brief(concepts, role, decision.Kind, row.Concepts.Concat(row.Roles).Select(static read => read.Keyword).ToList());
            retrieval = V4Retrieval.Run(brief, tables, dictionary);
            draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", retrieval.Candidates.Count == 0 ? "no_candidate" : "candidates_found"));
            AssembleByCode(draft, decision, retrieval, row, dictionary);
            if (decision.Route != "own_case" && model is not null && retrieval.Candidates.Count > 0 && !(retrieval.ClearLead && decision.Kind is "duration" or "date" or "amount"))
            {
                Refine(draft, decision, retrieval, row, tables, dictionary, model, cancellationToken);
            }
            else if (decision.Route != "own_case" && retrieval.ClearLead && decision.Kind is "duration" or "date" or "amount" && draft.Items.Count == 1)
            {
                // Route 5: one fact, clearly found. The margin ends the route here, with no model.
                draft.Route = "fact";
            }

            foreach (var unmatched in row.Unmatched.Take(3))
            {
                if (V4Retrieval.LinesHolding(tables, row.Folded[unmatched.Start..unmatched.End]) == 0)
                {
                    notes.Add(new V4AnswerNote("word_not_in_act", unmatched, null));
                }
            }
        }

        // A reader's word, or another form of the law's word, is said beside the law's own term.
        foreach (var read in row.Concepts.Concat(row.Roles))
        {
            var keyword = dictionary.Keyword(read.Keyword)!;
            var written = string.Join(' ', V4Text.Words(row.Folded[read.Start..read.End]).Select(static word => word.Text));
            var law = string.Join(' ', V4Text.Words(V4Text.Fold(dictionary.Concept(read.Concept)!.LabelFr)).Select(static word => word.Text));
            // "Le terme du texte est ..." is said only of a label that is itself a word of the text: a concept whose label
            // groups several ("exclusions, dol et faute") has no one term to name.
            var lawHasAWord = dictionary.KeywordsOf(read.Concept, "law").Any(candidate => candidate.Keyword == dictionary.Concept(read.Concept)!.LabelFr);
            if (lawHasAWord && written != law &&
                (keyword.Word == "reader" || (keyword.Word == "law" && !keyword.Pattern && keyword.Keyword == dictionary.Concept(read.Concept)!.LabelFr)))
            {
                notes.Add(new V4AnswerNote("law_term", new V4Span(read.Start, read.End), read.Concept));
            }
        }

        var proposed = application ? "refuse" : draft.AnswerKind is "absence" && draft.Absence is "words_not_found" ? "clarify" : "answer";
        var json = Write(input, row, draft, tables, retrieval, decision, dateRef, ruleQuestion, proposed, decision.Kind, tables.Work, notes);
        return new V4PlanOutcome(json, draft.Cost, "route:" + draft.Route);
    }

    // ---- Route 4: a term the law defines ----------------------------------------------------------------------------------

    private static void Define(Draft draft, V4ActTables tables, V4QuestionRow row, List<(V4Definition Definition, int Start, int End)> terms, V4Dictionary dictionary)
    {
        // The term a role or a concept of the question names, else the longest term found.
        var reads = row.Roles.Concat(row.Concepts).ToList();
        var chosen = terms.OrderByDescending(term => reads.Any(read => read.Start < term.End && term.Start < read.End))
            .ThenByDescending(static term => term.End - term.Start)
            .First().Definition;
        var article = tables.Article(chosen.Article)!;
        var first = tables.Lines[article.FirstLine + chosen.FirstLine];
        var last = tables.Lines[article.FirstLine + chosen.LastLine];
        draft.AnswerKind = "definition";
        draft.Found = true;
        draft.Complete = true;
        draft.Items.Add(new Item(first, last, "definition", null));
        draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", "defining_line_read"));

        // Where else the act uses the term: counted, and offered as articles to read.
        var key = V4Key.Read(chosen.TermFolded)!;
        var lines = 0;
        var articles = new List<string>();
        foreach (var line in tables.Lines)
        {
            if (line.Kind != "label" && key.In(line.Words))
            {
                lines++;
                if (!articles.Contains(line.Article, StringComparer.Ordinal))
                {
                    articles.Add(line.Article);
                }
            }
        }

        draft.Usage = (lines, articles.Count);
        draft.More = articles.Where(id => id != chosen.Article).Take(V3AssistantPipeline.MaximumCandidates).ToList();
    }

    // ---- The answer as code alone finds it ----------------------------------------------------------------------------------

    private static void AssembleByCode(Draft draft, V4Decision decision, V4RetrievalResult retrieval, V4QuestionRow row, V4Dictionary dictionary)
    {
        var candidates = retrieval.Candidates;
        var kind = decision.Kind;
        draft.Items.Clear();
        draft.Caveats.Clear();
        draft.More.Clear();
        draft.Absence = null;
        if (candidates.Count == 0)
        {
            // Rule 8: zero hits on a complete row is evidence; zero hits on a reader's own word is only a missing row.
            var complete = row.Concepts.Count > 0 && row.Concepts.All(read => dictionary.Concept(read.Concept)!.Complete);
            draft.AnswerKind = "absence";
            draft.Absence = complete ? "terms_nowhere" : "words_not_found";
            draft.Found = false;
            return;
        }

        var shortlist = retrieval.Shortlist(MaximumCandidates);
        if (decision.Route == "own_case")
        {
            draft.AnswerKind = "candidates";
            if (shortlist[0].Score >= 5.0)
            {
                draft.Items.Add(new Item(shortlist[0].Line, shortlist[0].Line, "passage", null));
            }

            return;
        }

        var valueKind = kind is "duration" or "date" or "amount";
        if (valueKind)
        {
            var valued = shortlist.Where(static candidate => candidate.Detectors.Contains("keyword") && candidate.Values.Count > 0).ToList();
            if (valued.Count == 0)
            {
                // The kind is kept, no value is given, and the line that says why is quoted: never a default value.
                draft.AnswerKind = "absence";
                draft.Absence = "no_answer_in_lines";
                draft.Found = false;
                foreach (var nearest in shortlist.Take(2))
                {
                    draft.Items.Add(new Item(nearest.Line, nearest.Line, "nearest", null));
                }

                if (shortlist.Take(2).Any(static candidate => V4Patterns.LeftToContract(candidate.Line.Folded)))
                {
                    draft.Caveats.Add("left_to_contract");
                }

                return;
            }

            var top = valued.Where(candidate => candidate.Score >= valued[0].Score - 0.5).Take(3).ToList();
            draft.AnswerKind = kind;
            draft.Found = true;
            foreach (var candidate in top)
            {
                draft.Items.Add(new Item(candidate.Line, candidate.Line, "value", candidate.Values.Count == 1 ? candidate.Values[0] : null));
            }

            if (top.Count > 1)
            {
                draft.Caveats.Add("several_by_scope");
            }

            Offer(draft, shortlist);
            return;
        }

        if (kind is "rule")
        {
            var lead = shortlist[0];
            var sure = lead.Detectors.Contains("type_pattern") && (lead.Detectors.Contains("general_rule") || lead.Detectors.Contains("direct_form")) &&
                (shortlist.Count == 1 || lead.Score - shortlist[1].Score >= 1.0);
            draft.AnswerKind = sure ? "rule" : "candidates";
            draft.Found = true;
            draft.Items.Add(new Item(lead.Line, lead.Line, sure ? "rule" : "passage", null));
            var cases = shortlist.Skip(1)
                .Where(candidate => candidate.Detectors.Contains("keyword") && candidate.Detectors.Contains("type_pattern") && (row.Roles.Count == 0 || candidate.Detectors.Contains("cooccurrence")))
                .GroupBy(static candidate => candidate.Article.Id).Select(static group => group.First())
                .Take(sure ? 4 : 2)
                .ToList();
            foreach (var candidate in cases)
            {
                draft.Items.Add(new Item(candidate.Line, candidate.Line, sure ? "case" : "passage", null));
            }

            if (sure && cases.Count > 0)
            {
                draft.Caveats.Add("cases_from_sweep");
                if (retrieval.Tables.References.Any(reference => reference.Kind == "unnamed_cases" && reference.FromLine == lead.Line.Id))
                {
                    draft.Passes.Add(new V4RecordPass(2, "unnamed_cases", "sweep_whole_act", "cases_listed"));
                }
            }

            if (V4Patterns.StatesCondition(lead.Line.Folded))
            {
                draft.Caveats.Add("conditions_apply");
            }

            Offer(draft, shortlist);
            return;
        }

        if (kind is "list")
        {
            draft.AnswerKind = "list";
            draft.Found = true;
            foreach (var candidate in shortlist.Where(static candidate => candidate.Detectors.Contains("keyword")).Take(5).OrderBy(static candidate => candidate.Article.Position).ThenBy(static candidate => candidate.Line.Index))
            {
                draft.Items.Add(new Item(candidate.Line, candidate.Line, "item", null));
            }

            draft.Caveats.Add("list_from_sweep");
            Offer(draft, shortlist);
            return;
        }

        // A definition asked of a term the act does not define, a reference, or an open question: the passages.
        draft.AnswerKind = "text";
        draft.Found = true;
        foreach (var candidate in shortlist.Take(3))
        {
            draft.Items.Add(new Item(candidate.Line, candidate.Line, "passage", null));
        }

        if (kind == "definition")
        {
            draft.Caveats.Add("not_defined");
        }

        Offer(draft, shortlist);
    }

    /// <summary>The further articles an answer offers to read: those of the candidates it does not quote.</summary>
    private static void Offer(Draft draft, IReadOnlyList<V4Candidate> shortlist)
    {
        var quoted = draft.Items.Select(static item => item.First.Article).ToHashSet(StringComparer.Ordinal);
        draft.More = shortlist.Select(static candidate => candidate.Article.Id).Where(id => !quoted.Contains(id)).Distinct(StringComparer.Ordinal).Take(V3AssistantPipeline.MaximumCandidates).ToList();
    }

    // ---- The two model steps, and the bounded loop around them ------------------------------------------------------------

    private static V4ModelReply? Ask(Draft draft, IV4Model model, V4ModelCall call, CancellationToken cancellationToken)
    {
        try
        {
            var reply = model.Complete(call, cancellationToken);
            draft.Cost += reply.CostMicroEuros;
            draft.Calls.Add((
                call.Step,
                model.ModelId,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.System + "\n" + call.User)))[..16],
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reply.Content))),
                reply.InputTokens,
                reply.OutputTokens));
            return reply;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A model that fails or times out leaves the answer code found; the loop never crashes on it.
            return null;
        }
    }

    private static void Refine(Draft draft, V4Decision decision, V4RetrievalResult retrieval, V4QuestionRow row, V4ActTables tables, V4Dictionary dictionary, IV4Model model, CancellationToken cancellationToken)
    {
        var kind = decision.Kind == "definition" || decision.Kind == "reference" ? "text" : decision.Kind;
        var shortlist = retrieval.Shortlist(MaximumCandidates);

        // Step 3, the arbiter: a role and a reason per candidate. "Why a method ranked a candidate matters." [7C]
        var arbiter = Ask(draft, model, V4Generation.ArbiterCall(row, kind, tables, shortlist, dictionary), cancellationToken);
        var roles = arbiter is null ? null : V4Generation.ReadArbiter(arbiter.Content, shortlist.Count);
        if (roles is null)
        {
            draft.Caveats.Add("model_unavailable");
            Downgrade(draft);
            return;
        }

        foreach (var entry in roles)
        {
            var candidate = shortlist[int.Parse(entry.Id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture) - 1];
            candidate.Role = entry.Role;
            candidate.Reason = entry.Reason;
        }

        var primaries = shortlist.Where(static candidate => candidate.Role == "primary").Take(4).ToList();
        var supporting = shortlist.Where(static candidate => candidate.Role == "supporting").Take(8).ToList();
        if (primaries.Count == 0)
        {
            // No candidate carries the answer: the nearest lines are shown as such, and nothing is restated.
            draft.AnswerKind = "absence";
            draft.Absence = "no_answer_in_lines";
            draft.Found = false;
            draft.Items = (supporting.Count > 0 ? supporting : shortlist).Take(2).Select(static candidate => new Item(candidate.Line, candidate.Line, "nearest", null)).ToList();
            draft.Caveats = draft.Items.Any(static item => V4Patterns.LeftToContract(item.First.Folded)) ? ["left_to_contract"] : [];
            Offer(draft, shortlist);
            draft.Passes.Add(new V4RecordPass(draft.Passes.Count + 1, "arbiter_found_no_primary", "none", "nearest_lines_shown"));
            return;
        }

        // Step 4, the context: the anchors' own points; a line an anchor points to brings its article's rule with it.
        List<V4Line> Context(bool wide)
        {
            var lines = new List<V4Line>();
            void Add(IEnumerable<V4Line> more)
            {
                foreach (var line in more)
                {
                    if (lines.Count < MaximumLines && !lines.Any(existing => existing.Id == line.Id))
                    {
                        lines.Add(line);
                    }
                }
            }

            foreach (var primary in wide ? primaries.Take(1) : primaries)
            {
                Add(V4Retrieval.ContextOf(tables, primary.Line, wide ? "list" : kind, wide ? 24 : 6));
            }

            if (!wide)
            {
                Add(supporting.Select(static candidate => candidate.Line));
                foreach (var reference in tables.References.Where(reference => reference.Kind == "article" && reference.Resolved && primaries.Any(primary => primary.Line.Id == reference.FromLine)).Take(2))
                {
                    Add(tables.TextLinesOf(tables.Article(reference.Target!)!).Take(6));
                }
            }

            return lines.OrderBy(line => tables.Article(line.Article)!.Position).ThenBy(static line => line.Index).ToList();
        }

        List<V4Definition> DefinitionsIn(List<V4Line> lines) => tables.Definitions
            .Where(definition => row.Roles.Any(read => V4Key.Read(definition.TermFolded) is { } key && key.In(V4Text.Words(V4Text.Fold(dictionary.Concept(read.Concept)!.LabelFr)))))
            .GroupBy(static definition => (definition.Article, definition.FirstLine)).Select(static group => group.First())
            .Take(3).ToList();

        List<V4Generation.Checked>? items = null;
        V4Extraction? extraction = null;
        string? failed = null;
        for (var attempt = 0; attempt < 2 && items is null && draft.Passes.Count < MaximumPasses; attempt++)
        {
            // "Every retry should change something": the second attempt reads the whole article of the first anchor.
            var lines = Context(wide: attempt == 1);
            if (attempt == 1)
            {
                draft.Passes.Add(new V4RecordPass(draft.Passes.Count + 1, "validator_rejected", "context_is_first_anchor_article", failed ?? "reply_not_of_the_form"));
            }

            var reply = Ask(draft, model, V4Generation.ExtractionCall(row, kind, tables, lines, DefinitionsIn(lines), dictionary), cancellationToken);
            extraction = reply is null ? null : V4Generation.ReadExtraction(reply.Content);
            if (reply is null)
            {
                break;
            }

            items = extraction is null ? null : V4Generation.Validate(extraction, kind, lines, out failed);
        }

        if (items is null || extraction is null)
        {
            // A typed "not answered by the model" rather than a wrong value: the passages code found, said as such.
            draft.Caveats.Add("model_unavailable");
            Downgrade(draft);
            return;
        }

        draft.Caveats = extraction.Caveats.ToList();
        if (items.Count == 0)
        {
            draft.AnswerKind = "absence";
            draft.Absence = "no_answer_in_lines";
            draft.Found = false;
            draft.Items = primaries.Take(2).Select(static candidate => new Item(candidate.Line, candidate.Line, "nearest", null)).ToList();
            if (draft.Items.Any(static item => V4Patterns.LeftToContract(item.First.Folded)) && !draft.Caveats.Contains("left_to_contract"))
            {
                draft.Caveats.Add("left_to_contract");
            }

            Offer(draft, shortlist);
            return;
        }

        draft.AnswerKind = kind;
        draft.Found = true;
        draft.Complete = extraction.Complete && kind != "rule";
        draft.Items = items.Take(V3AssistantPipeline.MaximumQuotes).Select(static item => new Item(item.Line, item.Line, item.Role, item.Value)).ToList();
        if (kind == "rule" && draft.Items.Any(static item => item.Role == "case"))
        {
            draft.Caveats.Add("cases_from_sweep");
            var rule = draft.Items.First(static item => item.Role == "rule");
            if (tables.References.Any(reference => reference.Kind == "unnamed_cases" && reference.FromLine == rule.First.Id) && !draft.Passes.Any(static pass => pass.Trigger == "unnamed_cases"))
            {
                draft.Passes.Add(new V4RecordPass(draft.Passes.Count + 1, "unnamed_cases", "sweep_whole_act", "cases_listed"));
            }
        }
        else
        {
            draft.Passes.RemoveAll(static pass => pass.Trigger == "unnamed_cases");
        }

        if (kind is "duration" or "date" or "amount" && draft.Items.Count > 1)
        {
            draft.Caveats.Add("several_by_scope");
        }

        if (kind == "list" && !draft.Complete)
        {
            draft.Caveats.Add("list_from_sweep");
        }

        if (decision.Kind == "definition")
        {
            draft.Caveats.Add("not_defined");
        }

        // The articles the arbiter kept and the answer does not quote stay one step away.
        var quoted = draft.Items.Select(static item => item.First.Article).ToHashSet(StringComparer.Ordinal);
        draft.More = primaries.Concat(supporting).Select(static candidate => candidate.Article.Id).Where(id => !quoted.Contains(id)).Distinct(StringComparer.Ordinal).Take(V3AssistantPipeline.MaximumCandidates).ToList();
    }

    /// <summary>An answer code assembled that no model confirmed is shown as passages, never as "the rule".</summary>
    private static void Downgrade(Draft draft)
    {
        if (draft.AnswerKind is "rule" && !draft.Items.Any(static item => item.Role == "rule"))
        {
            draft.AnswerKind = "candidates";
        }
    }

    // ---- The plan /2 ------------------------------------------------------------------------------------------------------

    private static string Write(
        V4PlanInput input,
        V4QuestionRow row,
        Draft draft,
        V4ActTables? tables,
        V4RetrievalResult? retrieval,
        V4Decision? decision,
        string dateRef,
        bool ruleQuestion,
        string proposed,
        string asked,
        string? work = null,
        List<V4AnswerNote>? notes = null)
    {
        // One evidence step per article quoted, in the order the answer shows them; an article's extracts in the text's order.
        var steps = new List<(string Id, V4Article Article, List<(Item Item, int Offset, int Length, List<int> Breaks)> Extracts)>();
        var places = new List<(Item Item, string Step, int Extract)>();
        if (tables is not null)
        {
            foreach (var group in draft.Items.Take(V3AssistantPipeline.MaximumQuotes).GroupBy(static item => item.First.Article).Take(V3AssistantPlanValidator.MaximumOperations))
            {
                var article = tables.Article(group.Key)!;
                var extracts = new List<(Item, int, int, List<int>)>();
                foreach (var item in group.OrderBy(static item => item.First.Offset))
                {
                    var offset = item.First.Offset;
                    var length = item.Last.Offset + item.Last.Length - offset;
                    if (extracts.Count > 0 && offset < extracts[^1].Item2 + extracts[^1].Item3)
                    {
                        continue;
                    }

                    var breaks = new List<int>();
                    for (var index = item.First.Index + 1; index <= item.Last.Index; index++)
                    {
                        var line = tables.Lines[article.FirstLine + index];
                        if (line.Kind != "marker")
                        {
                            breaks.Add(line.Offset - offset);
                        }
                    }

                    extracts.Add((item, offset, Math.Min(length, V3AssistantPipeline.MaximumQuoteLength), breaks.Where(position => position > 0 && position < length).Distinct().ToList()));
                }

                steps.Add(("s" + (steps.Count + 1).ToString(CultureInfo.InvariantCulture), article, extracts));
            }

            foreach (var item in draft.Items)
            {
                foreach (var (id, _, extracts) in steps)
                {
                    var at = extracts.FindIndex(extract => ReferenceEquals(extract.Item, item));
                    if (at >= 0)
                    {
                        places.Add((item, id, at));
                    }
                }
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            void Span(string name, int start, int end)
            {
                writer.WriteStartArray(name);
                writer.WriteNumberValue(start);
                writer.WriteNumberValue(end);
                writer.WriteEndArray();
            }

            writer.WriteStartObject();
            writer.WriteString("schema", V3AssistantPlanValidator.Schema2);
            writer.WriteStartArray("parts");
            writer.WriteStartObject();
            writer.WriteString("part_id", "p1");
            Span("span", 0, input.MaskedQuestion.Length);
            writer.WriteString("proposed_verdict", proposed);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteStartArray("steps");
            foreach (var (id, article, extracts) in steps)
            {
                writer.WriteStartObject();
                writer.WriteString("step_id", id);
                writer.WriteString("part_id", "p1");
                writer.WriteString("operation_id", "evidence_bundle");
                writer.WriteStartObject("arguments");
                writer.WriteStartObject("article");
                writer.WriteString("corpus", article.Id);
                writer.WriteEndObject();
                writer.WriteStartObject("date");
                if (dateRef == "today")
                {
                    writer.WriteBoolean("today", true);
                }
                else
                {
                    writer.WriteString("ref", dateRef);
                }

                writer.WriteEndObject();
                writer.WriteStartObject("identifier");
                writer.WriteString("corpus", tables!.Work);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteString("text_sha256", article.TextSha256);
                if (tables.LinesOf(article).FirstOrDefault(static line => line.Kind == "heading") is { } heading)
                {
                    Span("heading", heading.Offset, heading.Offset + heading.Length);
                }

                writer.WriteStartArray("extracts");
                foreach (var (item, offset, length, breaks) in extracts)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("offset", offset);
                    writer.WriteNumber("length", length);
                    writer.WriteString("role", item.Role);
                    if (breaks.Count > 0)
                    {
                        writer.WriteStartArray("breaks");
                        foreach (var position in breaks)
                        {
                            writer.WriteNumberValue(position);
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("follow_ups");
            writer.WriteEndArray();

            // The bricks record: tokens, numbers and positions only.
            writer.WriteStartObject("bricks");
            writer.WriteString("version", V4PlanRecord.Version);
            writer.WriteString("dictionary", V4Dictionary.Insurance.Sha256);
            writer.WriteString("corpus_index", V4CorpusIndex.Insurance.Sha256);
            writer.WriteString("route", draft.Route);
            if (asked.Length > 0)
            {
                writer.WriteString("kind", asked);
            }

            writer.WriteString("date", dateRef);
            if (work is not null)
            {
                writer.WriteString("work", work);
            }

            if (tables is not null)
            {
                writer.WriteString("state", tables.StateSha256);
                writer.WriteString("state_date", tables.Date);
                writer.WriteNumber("lines", tables.Summary.Lines);
                writer.WriteNumber("articles", tables.Summary.Articles);
            }

            void Reads(string name, IReadOnlyList<V4RowConcept> reads)
            {
                writer.WriteStartArray(name);
                foreach (var read in reads)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", read.Concept);
                    writer.WriteNumber("keyword", read.Keyword);
                    Span("span", read.Start, read.End);
                    writer.WriteString("source", read.Source);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            Reads("concepts", row.Concepts);
            Reads("roles", row.Roles);
            writer.WriteBoolean("rule_question", ruleQuestion);
            writer.WriteStartArray("unmatched");
            foreach (var unmatched in row.Unmatched.Take(8))
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(unmatched.Start);
                writer.WriteNumberValue(unmatched.End);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("activations");
            foreach (var activation in decision?.Activations ?? [])
            {
                writer.WriteStartObject();
                writer.WriteString("id", activation.Id);
                writer.WriteBoolean("on", activation.On);
                writer.WriteString("reason", activation.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (decision is not null)
            {
                writer.WriteString("route_reason", decision.Reason);
            }

            writer.WriteStartArray("detectors");
            foreach (var detector in retrieval?.Detectors.Where(static detector => detector.Ran) ?? [])
            {
                writer.WriteStartObject();
                writer.WriteString("id", detector.Id);
                writer.WriteNumber("lines", detector.Lines);
                writer.WriteNumber("articles", detector.Articles);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("candidates");
            foreach (var candidate in retrieval?.Shortlist(MaximumCandidates) ?? [])
            {
                writer.WriteStartObject();
                writer.WriteString("line", candidate.Line.Id);
                writer.WriteStartArray("detectors");
                foreach (var detector in candidate.Detectors)
                {
                    writer.WriteStringValue(detector);
                }

                writer.WriteEndArray();
                writer.WriteNumber("score_tenths", (int)Math.Round(Math.Max(0, candidate.Score) * 10));
                if (candidate.Role is not null)
                {
                    writer.WriteString("role", candidate.Role);
                    writer.WriteString("reason", candidate.Reason);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("sweep");
            foreach (var sweep in retrieval?.Sweep.Take(24) ?? [])
            {
                writer.WriteStartObject();
                writer.WriteNumber("keyword", sweep.Keyword);
                writer.WriteNumber("lines", sweep.Lines);
                writer.WriteNumber("articles", sweep.Articles);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("passes");
            foreach (var pass in draft.Passes.Take(MaximumPasses))
            {
                writer.WriteStartObject();
                writer.WriteNumber("n", pass.Number);
                writer.WriteString("trigger", pass.Trigger);
                writer.WriteString("changed", pass.Changed);
                writer.WriteString("outcome", pass.Outcome);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("model_calls");
            foreach (var call in draft.Calls)
            {
                writer.WriteStartObject();
                writer.WriteString("step", call.Step);
                writer.WriteString("model", Token(call.Model));
                writer.WriteString("prompt", call.Prompt);
                writer.WriteString("reply", call.Reply);
                writer.WriteNumber("input_tokens", Math.Max(0, call.Input));
                writer.WriteNumber("output_tokens", Math.Max(0, call.Output));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartObject("answer");
            writer.WriteString("kind", draft.AnswerKind);
            writer.WriteBoolean("found", draft.Found);
            writer.WriteBoolean("complete", draft.Complete);
            writer.WriteStartArray("items");
            foreach (var (item, step, extract) in places)
            {
                writer.WriteStartObject();
                writer.WriteString("step", step);
                writer.WriteNumber("extract", extract);
                writer.WriteString("role", item.Role);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("values");
            foreach (var (item, step, _) in places.Where(static place => place.Item.Value is not null))
            {
                writer.WriteStartObject();
                writer.WriteString("step", step);
                writer.WriteNumber("offset", item.First.Offset + item.Value!.Value.Start);
                writer.WriteNumber("length", item.Value.Value.Length);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("caveats");
            foreach (var caveat in draft.Caveats.Distinct(StringComparer.Ordinal))
            {
                writer.WriteStringValue(caveat);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("notes");
            foreach (var note in notes ?? [])
            {
                writer.WriteStartObject();
                writer.WriteString("code", note.Code);
                Span("span", note.Span.Start, note.Span.End);
                if (note.Concept is not null)
                {
                    writer.WriteString("concept", note.Concept);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("more");
            foreach (var article in work is null ? [] : draft.More.Where(V3AssistantPlanValidator.IsArticleId))
            {
                writer.WriteStringValue(article);
            }

            writer.WriteEndArray();
            if (draft.Usage is { } usage)
            {
                writer.WriteStartObject("usage");
                writer.WriteNumber("lines", usage.Lines);
                writer.WriteNumber("articles", usage.Articles);
                writer.WriteEndObject();
            }

            if (draft.Absence is not null)
            {
                writer.WriteString("absence", draft.Absence);
            }

            if (draft.EuAct is not null)
            {
                writer.WriteString("eu_act", draft.EuAct);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A model's identity as a token of the record: its letters, digits and a few signs, the rest replaced.</summary>
    private static string Token(string value)
    {
        var token = new string(value.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '/' or ':' or '+' or '-' ? character : '-').ToArray());
        return token.Length is 0 ? "model" : token.Length > 96 ? token[..96] : token;
    }
}
