using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
public sealed record V4PlanInput(
    string MaskedQuestion,
    V3AnswerLocale Locale,
    string Today,
    IReadOnlyList<V3ParsedDate> Dates,
    IReadOnlyList<V3PlannerReference>? References = null,
    IReadOnlyList<string>? SearchLanguages = null);

/// <summary>
/// What the bricks answer: a plan /2, or none with the reason (a question the bricks do not answer: it cites an article,
/// asks how the law changed, or names nothing of the dictionary), and what the model calls cost.
/// </summary>
/// <param name="Firm">Code is sure of the answer by the law's own words (a term the act defines, a general rule that says so itself, one value clearly ahead, a word the act never uses): no second reading is asked for it.</param>
/// <param name="Articles">The articles the plan quotes, in its order.</param>
public sealed record V4PlanOutcome(string? PlanJson, long CostMicroEuros, string Reason, bool Firm = false, IReadOnlyList<string>? Articles = null)
{
    /// <summary>The first line the tables' answer quotes, with the act it is of: what a second reading that differs also quotes.</summary>
    internal (string Work, V4Line Line)? Lead { get; init; }
}

/// <summary>
/// The function behind the validator (brick H): parse the question, read the corpus context, decide the route, run one
/// pass of retrieval, extraction and validation, read the typed flags, run at most a bounded number of further passes, and
/// hand the composer a plan. "A loop that hits max_iterations doesn't crash. It returns the best output produced so far,
/// flagged" [13]. A model that fails, is refused by a check twice, or is not wired leaves the answer code found, said as such.
/// </summary>
public static partial class V4Bricks
{
    /// <summary>Their hard limit on passes.</summary>
    public const int MaximumPasses = 4;

    /// <summary>The most candidates the arbiter is given.</summary>
    public const int MaximumCandidates = 14;

    /// <summary>The most lines the extraction is given.</summary>
    public const int MaximumLines = 40;

    /// <param name="ByCode">The line is in an answer a model helped with, and code added it: the model did not point at it.</param>
    private sealed record Item(V4Line First, V4Line Last, string Role, V4Value? Value, bool ByCode = false);

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
        public bool Firm;

        /// <summary>The date of the version read when it is the latest held and does not cover the date asked.</summary>
        public string? LatestHeld;

        /// <summary>An article the reader cites that exists in the publisher's document and whose text Lex does not hold.</summary>
        public string? NotHeld;
    }

    public static V4PlanOutcome Plan(V4PlanInput input, IV4Corpus corpus, IV4Model? model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(corpus);
        var dictionary = V4Dictionary.Insurance;
        var question = input.MaskedQuestion;

        // Steps 1 to 5 of question parsing are code; the tables answer what they read.
        var row = V4QuestionParser.Parse(question, input.Locale, dictionary, V4CorpusIndex.Insurance);
        var planner = new V3PlannerInput(question, input.Locale, input.References ?? [], input.SearchLanguages ?? [], input.Today);

        // Step 6, the question call: a model describes the question, from closed lists and words it copies from the question.
        // It names no operation. It is asked at most once for a question, and only where code cannot decide alone.
        string? outcome = null;
        List<V4DescribedPart>? parts = null;
        (V4ModelCall Call, V4ModelReply Reply, string Model)? asked = null;
        long cost = 0;
        void Describe()
        {
            if (outcome is not null)
            {
                return;
            }

            if (model is null)
            {
                outcome = "not_wired";
                return;
            }

            var call = V4QuestionCall.Call(planner, dictionary);
            V4ModelReply reply;
            try
            {
                reply = model.Complete(call, cancellationToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                outcome = "failed";
                return;
            }

            cost = reply.CostMicroEuros;
            parts = V4QuestionCall.Read(reply.Content, question, dictionary);
            outcome = parts is null ? "refused" : "described";
            asked = parts is null ? null : (call, reply, model.ModelId);
        }

        // The advice boundary (R15; the owner's decision 4(i) of 2026-11-18). Code refuses a part that holds a cue, unless it
        // is a question on the rule. One judgement code cannot make from words alone: a question that carries a mark of the
        // reader's own case ("mon contrat", "my claim") and no cue code reads ("My insurer says my claim is time-barred, is
        // it right?"). As before the bricks, a model is asked whether it is a person's own case, and such a part is refused,
        // with the law beside it. With no model to ask it is refused too: the boundary is biased to refusal.
        //
        // The owner's direction of 2026-11-19 (a reader's verdict: "make it answer first"): where the acts of the domain
        // answer the general question behind a personal wording ("L'assureur peut-il augmenter ma prime ?"), Lex quotes that
        // rule and says the text does not say how it applies to the reader's situation; it applies nothing. A refusal is
        // what a question worded for a case gets when no line of the law is found for it. So the tables and the outline
        // are asked first, with the mark of a personal wording as code reads it, and no model is asked to judge it.
        var personal = !row.Greeting && V4Boundary.PersonalMarkers(question, 0, question.Length).Count > 0;

        // The owner's point 2 of 2026-11-19: only a decision on the person's own facts is refused, and the article found
        // is still shown beside the refusal. Code reads it; no model is asked.
        var decides = !row.Greeting && V4Boundary.DecidesOwnFacts(question, 0, question.Length);
        var own = ByTheTables(input, row, corpus, model, cancellationToken, asked, personal || decides, decides);
        if (own.PlanJson is not null && (own.Firm || model is null))
        {
            return own with { CostMicroEuros = own.CostMicroEuros + (asked is null ? cost : 0) };
        }

        // The second reading (the owner's brief of 2026-11-19): a question of this domain is also routed by the acts' own
        // tables of contents, and the articles named are read. It answers what the tables do not read, and it checks what
        // they answered without being sure: when both name the same article first, the tables' answer stands (it is the
        // typed one); when they differ, the articles read under their own headings are the answer. A citation, a change
        // over time, another domain or another act keep their own routes, and what code is sure of is not read twice.
        long outline = 0;
        var outlined = string.Empty;
        if (model is not null && (own.PlanJson is not null || own.Reason is "nothing_of_the_dictionary" or "no_act_in_scope"))
        {
            var read = ByTheOutline(input, row, corpus, model, cancellationToken, asked, personal || decides, own.Lead, decides);
            if (read.PlanJson is not null)
            {
                // The tables' answer stands when the second reading quotes the tables' first article and no article the
                // tables did not see. Where it quotes that article and others besides, it read more of the question than
                // the tables did ("qui doit prouver quoi en cas de sinistre" : the tables read "sinistre" alone), and its
                // answer stands: it holds the tables' first article too. Where the two differ, neither is sure: the
                // second reading's answer stands and quotes the tables' first line after its own, when it is of the same
                // act (`read.Articles` are the articles the model's reading quoted, without that line).
                var agree = own.PlanJson is not null && own.Articles is { Count: > 0 } quoted && (read.Articles ?? []).Contains(quoted[0], StringComparer.Ordinal);
                var wider = agree && (read.Articles ?? []).Any(article => !own.Articles!.Contains(article, StringComparer.Ordinal));
                if (agree && !wider)
                {
                    return own with { CostMicroEuros = own.CostMicroEuros + read.CostMicroEuros + (asked is null ? cost : 0), Reason = own.Reason + "+outline_agrees" };
                }

                // The owner's point 1 of 2026-11-19: "when code is not sure, show candidates or say 'not found', never
                // assert". Where the tables and the second reading differ, neither is sure: the lines of both are shown as
                // candidates, not as the answer.
                var differs = own.PlanJson is not null && !wider;
                return read with
                {
                    PlanJson = differs ? AsCandidates(read.PlanJson) : read.PlanJson,
                    CostMicroEuros = read.CostMicroEuros + own.CostMicroEuros + (asked is null ? cost : 0),
                    Reason = read.Reason + (own.PlanJson is null ? string.Empty : wider ? "+tables_narrower" : "+tables_differ+candidates"),
                };
            }

            if (own.PlanJson is not null)
            {
                // The second reading ran and did not confirm what the tables answered without being sure: the tables' lines
                // are shown as candidates (point 1). Where it did not run (a question it is not read for), the tables'
                // answer stands as before.
                var unconfirmed = read.Reason is "outline_reading_found_nothing" or "outline_names_nothing" or "outline_unreadable" or "outline_reading_refused" or "outline_articles_hold_no_line";
                return own with
                {
                    PlanJson = unconfirmed ? AsCandidates(own.PlanJson) : own.PlanJson,
                    CostMicroEuros = own.CostMicroEuros + read.CostMicroEuros + (asked is null ? cost : 0),
                    Reason = own.Reason + "+" + read.Reason + (unconfirmed ? "+candidates" : string.Empty),
                };
            }

            outline = read.CostMicroEuros;
            outlined = "+" + read.Reason;
        }

        // For Lex's own operations the boundary is as before the bricks: a mark of the reader's own case with no cue is
        // judged by the model, the reader as the subject of the question's own verb is a case by its grammar, and with no
        // model to ask the part is refused.
        var ownCase = decides;
        if (!ownCase && personal && V3AssistantParsers.ApplicationCues(question, 0, question.Length).Count == 0)
        {
            if (V4Boundary.AsksOfOneself(question, 0, question.Length))
            {
                ownCase = true;
            }
            else
            {
                Describe();
                ownCase = parts is null || parts.Any(static part => part.OwnCase);
            }
        }

        // With no description from a model (none is wired, the call failed, its reply left its closed form), nothing is
        // repaired and nothing is asked again: code searches the reader's own words, and the answer says no model took part.
        Describe();

        // An article cited by its number, no act named, no act of a last answer to read it in: searched, never read alone.
        // An act the index does not name ("article 1382 du Code civil", "règlement (UE) 2016/679 article 17") is an act
        // named: the search finds its article and it is read, as before.
        // And a question on how the law changed is never answered by the one article a search of its words finds
        // ("quelles modifications de la loi ... depuis 2015 ?" was answered with an article that holds "modification").
        var searchOnly = own.Reason == "asks_change" ||
            (own.Reason == "cites_article" && row.Acts.Count == 0 && (input.References?.Count ?? 0) == 0 && !V4Dispatcher.NamesAnotherAct(row, input.Dates, dictionary));
        // An act of the index named with nothing of the domain beside it and no article cited: the act itself is asked.
        var index = V4CorpusIndex.Insurance;
        var actAlone = row.Acts.Count(static act => act.Work is not null) == 1 && row.Concepts.Count == 0 && row.Roles.Count == 0 && !V4Dispatcher.CitesArticle(row) &&
            !index.InDomain(row.Words.Where(word => !row.Acts.Any(act => word.Start >= act.Start && word.End <= act.End)).ToList());
        var readingDates = V4Dispatcher.ReadingDates(row, input.Dates).Select(static date => date.Ref).ToList();
        if (parts is null || asked is null)
        {
            var read = V4QuestionCall.ByCode(planner, row).Select(part => part with { OwnCase = ownCase }).ToList();
            return new V4PlanOutcome(V4QuestionCall.PlanJson(planner, read, outcome!, null, readingDates, searchOnly, row.Acts, actAlone), cost + outline, own.Reason + "+question_" + outcome + outlined);
        }

        // The model's own words are used for one thing only: to reach a concept of the dictionary the reader named in
        // words the dictionary lacks. Then the tables answer, as for any question, under the boundary above.
        if (own.Reason == "nothing_of_the_dictionary" && parts is [{ Concept.Length: > 0 } described] && dictionary.Concept(described.Concept) is { Branch: false } concept)
        {
            var span = described.Words ?? described.Span;
            var keyword = dictionary.KeywordsOf(concept.Id, "law", "reader").FirstOrDefault();
            if (keyword is not null && !question.AsSpan(span.Start, span.End - span.Start).Contains('•'))
            {
                var reached = ByTheTables(input, row.With(new V4RowConcept(concept.Id, keyword.Row, span.Start, span.End, "model")), corpus, model, cancellationToken, asked, personal || decides, decides);
                // An answer reached through a concept the model named stands when code is sure of it, or when no second
                // reading was there to contradict it; where the articles read under their own headings held no answer,
                // an answer code is not sure of is not given (the reader's set of 2026-11-19: one such answer was wrong).
                if (reached.PlanJson is not null && (reached.Firm || outlined != "+outline_reading_found_nothing"))
                {
                    return reached with { CostMicroEuros = reached.CostMicroEuros + outline };
                }
            }
        }

        // An act as it stood on a date quotes nothing. When the question's own words ask what a text says or provides
        // (a cue of the dictionary: "que prévoit", "que dit", "quel est le délai"), code searches the words the description
        // points at, on the date asked, whatever kind the description gave it (2026-11-13: such a question got no quotation).
        if (row.Kinds.Count > 0)
        {
            parts = parts.Select(static part => part.Asks == "act_on_a_date" ? part with { Asks = "words_on_a_subject", Words = part.Words ?? part.Act, Act = null } : part).ToList();
        }

        return new V4PlanOutcome(V4QuestionCall.PlanJson(planner, parts, "described", asked, readingDates, searchOnly, row.Acts, actAlone), cost + outline, "route:operations(" + own.Reason + ")" + outlined);
    }

    /// <summary>
    /// What the tables answer of a parsed row: a plan /2, or none with the reason (the question cites an article, asks how the
    /// law changed, holds a word of another domain, or names nothing the dictionary and the acts' own terms read).
    /// </summary>
    private static V4PlanOutcome ByTheTables(
        V4PlanInput input,
        V4QuestionRow row,
        IV4Corpus corpus,
        IV4Model? model,
        CancellationToken cancellationToken,
        (V4ModelCall Call, V4ModelReply Reply, string Model)? prior,
        bool ownCase = false,
        bool decides = false)
    {
        var dictionary = V4Dictionary.Insurance;
        var index = V4CorpusIndex.Insurance;
        var question = input.MaskedQuestion;
        var cues = V3AssistantParsers.ApplicationCues(question, 0, question.Length);
        var ruleQuestion = cues.Count > 0 && V4Boundary.RuleQuestion(question, 0, question.Length, cues);
        var application = (cues.Count > 0 && !ruleQuestion) || ownCase;

        // A question call made for this question (the model was asked whether it is a person's own case, or which concept
        // the reader's words name) is part of the answer's record and of its cost, whatever route answers.
        Draft Asked(Draft made)
        {
            if (prior is { } call)
            {
                made.Cost += call.Reply.CostMicroEuros;
                made.Calls.Add((
                    call.Call.Step,
                    call.Model,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.Call.System + "\n" + call.Call.User)))[..16],
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(call.Reply.Content))),
                    call.Reply.InputTokens,
                    call.Reply.OutputTokens));
            }

            return made;
        }

        // Route 0: not a legal question. "which deserves a direct reply and no pipeline at all" [13]
        if (row.Greeting)
        {
            var greeting = new Draft { Route = "greeting", AnswerKind = "greeting" };
            return new V4PlanOutcome(Write(input, row, greeting, null, null, null, "today", ruleQuestion: false, proposed: "clarify", asked: string.Empty), 0, "route:greeting", Firm: true);
        }

        // One word alone ("garantie", "test") is not a question: Lex asks for a full one and reads nothing (the owner's
        // brief of 2026-11-19: a single word got an article quoted as if it answered something).
        if (row.Words.Count == 1 && row.Acts.Count == 0)
        {
            var oneWord = new Draft { Route = "clarify", AnswerKind = "greeting" };
            return new V4PlanOutcome(Write(input, row, oneWord, null, null, null, "today", ruleQuestion: false, proposed: "clarify", asked: string.Empty), 0, "route:clarify", Firm: true);
        }

        // Routes 2 and 3 are Lex's own: a cited article goes to the reference reader, a change over time to diff and its kin.
        if (row.OtherDomain)
        {
            return new V4PlanOutcome(null, 0, "other_domain");
        }

        // The dates to read the law on: a date inside an act's name ("loi du 27 juillet 1997") is not one of them.
        var reading = V4Dispatcher.ReadingDates(row, input.Dates);
        var asksChange = V4Dispatcher.AsksChange(row, reading.Count);
        if (V4Dispatcher.CitesArticle(row))
        {
            // One article of an act the index names, on one date: code opens it, with no model and no search.
            return (asksChange || prior is not null ? null : Cited(input, row, corpus, reading, decides)) ?? new V4PlanOutcome(null, 0, "cites_article");
        }

        if (asksChange)
        {
            return new V4PlanOutcome(null, 0, "asks_change");
        }

        // The tables of this domain answer no question that names an act of another: Lex's own operations do.
        if (V4Dispatcher.NamesAnotherAct(row, input.Dates, dictionary))
        {
            return new V4PlanOutcome(null, 0, "names_another_act");
        }

        if (row.Acts.FirstOrDefault(static act => act.EuAct is not null) is { } named)
        {
            var act = index.EuAct(named.EuAct!)!;
            if (corpus.HoldsEuAct(act.Celex))
            {
                return new V4PlanOutcome(null, 0, "eu_act_held");
            }

            var notHeld = Asked(new Draft { Route = "not_held", AnswerKind = "not_held", EuAct = act.Id });
            return new V4PlanOutcome(Write(input, row, notHeld, null, null, null, "today", ruleQuestion: false, proposed: "point", asked: string.Empty), notHeld.Cost, "route:not_held", Firm: true);
        }

        var asksDefinition = row.Kinds.Any(static kind => kind.Kind == "definition") || (row.Kinds.Count == 0 && row.Concepts.Count == 0 && row.Words.Count <= 5);
        if (row.Concepts.Count == 0 && row.Acts.Count == 0 && !asksDefinition)
        {
            // A role alone ("l'assuré ...") names half the lines of the act: without a concept there is nothing to filter on.
            return new V4PlanOutcome(null, 0, "nothing_of_the_dictionary");
        }

        var dateRef = reading.Count == 1 ? reading[0].Ref : "today";
        var date = reading.Count == 1 ? reading[0].Iso : input.Today;

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

            var defined = TermsOf(at.Tables, row, dictionary);
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
        if (row.Concepts.Count == 0 && row.Roles.Count == 0 && !(asksDefinition && terms.Count > 0))
        {
            // An act is named, or a definition is asked, and nothing of the tables matched: Lex's own search answers.
            return new V4PlanOutcome(null, 0, "nothing_of_the_dictionary");
        }

        var latestHeld = scope.Facts.TextStatus == "latest_held" ? scope.Facts.VersionFrom : null;
        var decision = V4Dispatcher.Decide(
            new V4DispatchInput(row, Application: false, EuActNotHeld: false, tables is not null, tables is { Summary.Headings: > 0 }, terms.Count > 0, model is not null), dictionary);
        var draft = Asked(new Draft { Route = decision.Route, AnswerKind = decision.Kind, LatestHeld = latestHeld });

        V4RetrievalResult? retrieval = null;
        var notes = new List<V4AnswerNote>();
        if (tables is null)
        {
            draft.AnswerKind = "absence";
            draft.Absence = "text_not_held";
            return new V4PlanOutcome(
                Write(input, row, draft, null, null, decision, dateRef, ruleQuestion, "point", decision.Kind, "/lu-legilux/" + scope.Facts.Work), draft.Cost, "route:absence", Firm: true);
        }

        if (decision.Route == "definition")
        {
            Define(draft, tables, row, terms, dictionary);
        }
        else
        {
            var concepts = row.Concepts.Count > 0 ? row.Concepts.Select(static read => read.Concept).ToList() : row.Roles.Select(static read => read.Concept).ToList();
            var role = row.Concepts.Count > 0 ? row.Roles.FirstOrDefault()?.Concept : null;
            // The keywords the question used: the one the lookup read each concept by, and every other word of the same
            // concept the question also writes ("assurance sur la vie" and "erreur sur l'âge" in one question).
            var used = row.Concepts.Concat(row.Roles).Select(static read => read.Keyword)
                .Concat(row.Concepts.SelectMany(read => dictionary.KeywordsOf(read.Concept, "law", "reader")).Where(keyword => keyword.Key.In(row.Words)).Select(static keyword => keyword.Row))
                .Distinct()
                .ToList();
            var brief = new V4Brief(concepts, role, decision.Kind, used,
                row.Unmatched.Select(unmatched => row.Folded[unmatched.Start..unmatched.End]).ToList(), row.Words);
            retrieval = V4Retrieval.Run(brief, tables, dictionary);

            // A branch of insurance ("assurance sur la vie") says where a question stands, not what it asks. Read alone,
            // beside words of the question that are foreign to the tables, it is half a reading: the tables would answer
            // with some line of that branch. The question call names the subject among the dictionary's concepts, or Lex's
            // search looks for the words.
            if (row.Concepts.Count > 0 && row.Concepts.All(read => dictionary.Concept(read.Concept)!.Branch))
            {
                // What the tables did read of the question: the keys of the concept it writes, a heading it writes out
                // whole, and any word of the concept's own keys ("responsabilité" in "l'assureur de responsabilité", beside
                // "droit propre": the branch named in another turn of phrase is no foreign word).
                var keys = row.Concepts.SelectMany(read => dictionary.KeywordsOf(read.Concept, "law", "reader")).ToList();
                var runs = keys
                    .SelectMany(keyword => keyword.Key.Occurrences(row.Words))
                    .Concat(V4Retrieval.HeadingsWrittenOut(tables, row.Words).Select(static heading => (heading.First, heading.Count)))
                    .Select(run => (row.Words[run.First].Start, row.Words[run.First + run.Count - 1].End))
                    .ToList();
                bool Foreign(V4Span word) =>
                    !runs.Any(run => run.Start <= word.Start && run.End >= word.End) &&
                    !keys.Any(keyword => keyword.Key.Writes(row.Folded[word.Start..word.End]));
                if (row.Unmatched.Any(Foreign))
                {
                    return new V4PlanOutcome(null, 0, "nothing_of_the_dictionary");
                }
            }
            draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", retrieval.Candidates.Count == 0 ? "no_candidate" : "candidates_found"));
            AssembleByCode(draft, decision, retrieval, row, dictionary);
            var leftToContract = draft.Absence == "no_answer_in_lines" && draft.Caveats.Contains("left_to_contract");
            var leftToParties = draft.Items.FirstOrDefault(static item => item.Role == "passage" && item.Value is null && V4Patterns.LeftToContract(item.First.Folded));
            if (decision.Route != "own_case" && model is not null && retrieval.Candidates.Count > 0 && !leftToContract && !(retrieval.ClearLead && decision.Kind is "duration" or "date" or "amount"))
            {
                // Not reached when the law itself leaves the value to the contract ("dans le délai fixé par le contrat"): code
                // read it on the line, and no model can find a number that is not there.
                Refine(draft, decision, retrieval, row, tables, dictionary, model, cancellationToken);
            }
            else if (decision.Route != "own_case" && retrieval.ClearLead && decision.Kind is "duration" or "date" or "amount" && draft.Items.Count == 1)
            {
                // Route 5: one fact, clearly found. The margin ends the route here, with no model.
                draft.Route = "fact";
            }

            // The line that leaves the matter to the parties stays first, whatever a model pointed at after it.
            if (leftToParties is not null && draft.AnswerKind is "duration" or "amount" && !draft.Items.Any(item => item.First.Id == leftToParties.First.Id))
            {
                draft.Items.Insert(0, leftToParties);
                if (draft.Items.Count > V3AssistantPipeline.MaximumQuotes)
                {
                    draft.Items.RemoveAt(draft.Items.Count - 1);
                }

                if (!draft.Caveats.Contains("left_to_contract"))
                {
                    draft.Caveats.Add("left_to_contract");
                }
            }

            // A word that qualifies what is asked (it comes after the concept: "résilier ... de façon unilatérale") and that
            // the act never uses is said as such. A word before the concept is the question's own frame ("que se passe-t-il").
            var askedAt = row.Concepts.Concat(row.Roles).Select(static read => read.Start).DefaultIfEmpty(int.MaxValue).Min();
            foreach (var unmatched in row.Unmatched.Where(unmatched => unmatched.Start > askedAt).Take(3))
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

        // A question worded for the reader's own case: with lines of the law found, they are quoted as the general rule, and
        // the answer says the text does not say how it applies to the reader; with none, the part is refused. A decision on
        // the person's own facts is refused, and the lines found are shown beside the refusal (the owner's point 2).
        var general = application && draft.Items.Count > 0 && !decides;
        if (general && !draft.Caveats.Contains("not_your_case"))
        {
            draft.Caveats.Add("not_your_case");
        }

        var proposed = application && !general ? "refuse" : draft.AnswerKind is "absence" && draft.Absence is "words_not_found" ? "clarify" : "answer";
        var json = Write(input, row, draft, tables, retrieval, decision, dateRef, ruleQuestion, proposed, decision.Kind, tables.Work, notes);
        // What code is sure of by the law's own words: the line that defines the term asked, the rule that says of itself
        // that it is the general one, the one value clearly ahead, and a word of a complete row that the act never uses.
        var firm = (draft.Route == "definition" && draft.AnswerKind == "definition") || draft.Route == "fact" || draft.Firm ||
            (draft.AnswerKind == "absence" && draft.Absence == "terms_nowhere");
        return new V4PlanOutcome(json, draft.Cost, "route:" + draft.Route, firm, draft.Items.Select(static item => item.First.Article).Distinct(StringComparer.Ordinal).ToList())
        {
            Lead = draft.Items.Count > 0 && draft.AnswerKind != "absence" ? (tables.Work, draft.Items[0].First) : null,
        };
    }

    /// <summary>
    /// A plan whose answer code is not sure of, shown as candidates (the owner's point 1 of 2026-11-19): the same lines,
    /// quoted under "Lex a trouvé ces passages sans les départager davantage. Les plus proches :" in place of the sentence
    /// that gives them as the answer, every line as a passage, and no value restated. A refusal, an absence, a greeting
    /// and a plan of Lex's own operations are returned as they are.
    /// </summary>
    internal static string AsCandidates(string planJson)
    {
        ArgumentNullException.ThrowIfNull(planJson);
        var plan = JsonNode.Parse(planJson)!.AsObject();
        if (plan["bricks"]?["answer"] is not JsonObject answer || plan["parts"] is not JsonArray parts || parts.Count != 1 ||
            parts[0]?["proposed_verdict"]?.GetValue<string>() != "answer" || answer["items"] is not JsonArray items || items.Count == 0 ||
            answer["kind"]?.GetValue<string>() is "absence" or "greeting" or "not_held" or "operations" or "candidates")
        {
            return planJson;
        }

        answer["kind"] = "candidates";
        foreach (var item in items.OfType<JsonObject>())
        {
            item["role"] = "passage";
        }

        answer["values"] = new JsonArray();
        answer["complete"] = false;
        return plan.ToJsonString();
    }

    // ---- Route 2, by the tables: one article of an act the index names -----------------------------------------------------

    /// <summary>
    /// The plan that opens the one article a question cites, when code can name both ends itself: the act by a name of the
    /// corpus index, the article by its number in the act's own table, the date as today or the one date asked. Null when
    /// any of the three is not read (another act, several articles, an article the version does not hold): the question
    /// call then describes the question, as for every citation code does not read.
    /// </summary>
    private static V4PlanOutcome? Cited(V4PlanInput input, V4QuestionRow row, IV4Corpus corpus, List<V3ParsedDate> reading, bool ownCase)
    {
        var works = row.Acts.Where(static act => act.Work is not null).Select(static act => act.Work!).Distinct(StringComparer.Ordinal).ToList();
        if (works.Count != 1 || row.Acts.Any(static act => act.EuAct is not null) || reading.Count > 1 || V4Dispatcher.CitedArticle(row) is not { } number)
        {
            return null;
        }

        var at = corpus.Act(works[0], reading.Count == 1 ? reading[0].Iso : input.Today);
        if (at.Tables?.ArticleByNumber(number) is not { } article)
        {
            // The article exists in the publisher's document and Lex does not hold its text: said so, with the act's
            // official address, and nothing is searched (the owner's brief of 2026-11-19, work item 3).
            if (at.Tables?.NotHeld.FirstOrDefault(missing => missing.Id == "art_" + number) is { } missing)
            {
                var absent = new Draft { Route = "absence", AnswerKind = "absence", Absence = "article_not_held", NotHeld = missing.Id, LatestHeld = at.Facts.TextStatus == "latest_held" ? at.Facts.VersionFrom : null };
                absent.Passes.Add(new V4RecordPass(1, "first_pass", "none", "article_not_held"));
                return new V4PlanOutcome(Write(input, row, absent, at.Tables, null, null, "today", ruleQuestion: false, "point", string.Empty, at.Tables.Work), 0, "route:absence", Firm: true);
            }

            return null;
        }

        // The act's last version ended before the date: the article is quoted from the latest version held, and the
        // answer says so (the owner's brief of 2026-11-19, work item 2). Lex's own operation would refuse the date.
        if (at.Facts.TextStatus == "latest_held" && at.Facts.VersionFrom is { } heldOn)
        {
            var lines = at.Tables.TextLinesOf(article).Take(V3AssistantPipeline.MaximumQuotes).ToList();
            if (lines.Count == 0)
            {
                return null;
            }

            var draft = new Draft { Route = "open", AnswerKind = "text", Found = true, LatestHeld = heldOn };
            draft.Items = lines.Select(static line => new Item(line, line, "passage", null)).ToList();
            draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", "cited_article_read"));
            if (ownCase)
            {
                draft.Caveats.Add("not_your_case");
            }

            var written = Write(input, row, draft, at.Tables, null, null, reading.Count == 1 ? reading[0].Ref : "today", ruleQuestion: false, "answer", "text", at.Tables.Work, []);
            return new V4PlanOutcome(written, 0, "route:cited_latest_held", Firm: true, Articles: [article.Id]);
        }

        var planner = new V3PlannerInput(input.MaskedQuestion, input.Locale, input.References ?? [], input.SearchLanguages ?? [], input.Today);
        var json = V4QuestionCall.CitedPlanJson(planner, at.Tables.Work, article.Id, reading.Count == 1 ? reading[0].Ref : "today", at.Tables.Language, ownCase);
        return new V4PlanOutcome(json, 0, "route:operations+cited", Firm: true);
    }

    // ---- Route 4: a term the law defines ----------------------------------------------------------------------------------

    /// <summary>
    /// The terms the act defines that the question names: by the question's own words outside the acts it names, and, for a
    /// role or a concept the reader named by another word ("victime", "souscripteur"), by the law's word for it.
    /// </summary>
    private static List<(V4Definition Definition, int Start, int End)> TermsOf(V4ActTables tables, V4QuestionRow row, V4Dictionary dictionary)
    {
        var words = row.Words.Where(word => !row.Acts.Any(act => word.Start >= act.Start && word.End <= act.End)).ToList();
        var terms = V4Retrieval.DefinedTerms(tables, words);
        foreach (var read in row.Roles.Concat(row.Concepts))
        {
            if (terms.Any(term => term.Start < read.End && read.Start < term.End))
            {
                continue;
            }

            // A word of the law the reader wrote ("Fonds de Garantie Automobile") is looked up as it stands: another word
            // of the same concept is not what was asked (2026-11-19: that question got the definition of "véhicules").
            // A reader's own word ("victime") is looked up by the concept's label and the law's words for it.
            var concept = dictionary.Concept(read.Concept)!;
            var written = dictionary.Keyword(read.Keyword);
            var names = written is { Word: "law" } ? [written.Keyword] : new[] { concept.LabelFr }.Concat(dictionary.KeywordsOf(concept.Id, "law").Select(static keyword => keyword.Keyword));
            foreach (var keyword in names)
            {
                var keywordWords = V4Text.Words(V4Text.Fold(keyword));
                var whole = V4Retrieval.DefinedTerms(tables, keywordWords).FirstOrDefault(term => V4Text.Words(term.Definition.TermFolded).Count == keywordWords.Count);
                if (whole.Definition is not null)
                {
                    terms.Add((whole.Definition, read.Start, read.End));
                    break;
                }
            }
        }

        return terms.OrderBy(static term => term.Start).ThenByDescending(static term => term.End - term.Start).ToList();
    }

    private static void Define(Draft draft, V4ActTables tables, V4QuestionRow row, List<(V4Definition Definition, int Start, int End)> terms, V4Dictionary dictionary)
    {
        // The first term the question names: what stands right after "c'est quoi" is what is asked.
        var chosen = terms[0].Definition;
        var article = tables.Article(chosen.Article)!;
        var first = tables.Lines[article.FirstLine + chosen.FirstLine];
        var last = tables.Lines[article.FirstLine + chosen.LastLine];
        draft.AnswerKind = "definition";
        draft.Found = true;
        draft.Complete = true;
        draft.Items.Add(new Item(first, last, "definition", null));
        draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", "defining_line_read"));

        // A definition that only points at an article of the same act ("FGA: le Fonds de Garantie Automobile tel que visé
        // par l'article 15") is followed there: that article's first line is quoted after it. The pointer is read in the
        // term's own clause of the defining line (a list of definitions stands on one line, clause after clause).
        if (first.Id == last.Id && V4Dispatcher.PointedArticle(first.Folded, chosen.TermFolded) is { } target && target != chosen.Article &&
            tables.Article(target) is { } there && tables.TextLinesOf(there).FirstOrDefault() is { } opening)
        {
            draft.Items.Add(new Item(opening, opening, "passage", null));
        }

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
            if (valued.Count == 0 && kind == "date")
            {
                // A moment the law states in words: the passages are the answer, and no value is restated.
                draft.AnswerKind = "text";
                draft.Found = true;
                foreach (var candidate in shortlist.Take(3))
                {
                    draft.Items.Add(new Item(candidate.Line, candidate.Line, "passage", null));
                }

                if (shortlist.Take(3).Any(static candidate => V4Patterns.LeftToContract(candidate.Line.Folded)))
                {
                    draft.Caveats.Add("left_to_contract");
                }

                Offer(draft, shortlist);
                return;
            }

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

            // The line that leaves the matter to the parties ("La durée du contrat est fixée par les parties.") comes first:
            // it is why the values that follow are bounds and not the answer.
            if (shortlist.FirstOrDefault(static candidate => candidate.Detectors.Contains("article_lead") && candidate.Values.Count == 0 && V4Patterns.LeftToContract(candidate.Line.Folded)) is { } left)
            {
                draft.Items.Add(new Item(left.Line, left.Line, "passage", null));
                draft.Caveats.Add("left_to_contract");
                top = top.Take(2).ToList();
            }

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
            // Code is sure of the rule's line when the line says so itself: it sends to "the cases provided by the law", the
            // party asked about is the subject of its verb, or it is the first paragraph under the heading that names the
            // concept; and it leads.
            var sure = (lead.Detectors.Contains("general_rule") || lead.Detectors.Contains("direct_form") || lead.Detectors.Contains("article_lead")) &&
                (shortlist.Count == 1 || lead.Score - shortlist[1].Score >= 1.0);
            draft.AnswerKind = sure ? "rule" : "candidates";
            draft.Firm = sure && lead.Detectors.Contains("general_rule");
            draft.Found = true;
            draft.Items.Add(new Item(lead.Line, lead.Line, sure ? "rule" : "passage", null));
            var cases = shortlist.Skip(1)
                .Where(candidate => candidate.Detectors.Contains("keyword") && candidate.Detectors.Contains("type_pattern") && !candidate.Detectors.Contains("other_party") &&
                    (row.Roles.Count == 0 || candidate.Detectors.Contains("cooccurrence")))
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

        // When code's own lead is clear, the arbiter is not asked ("call the model less", their fast path [9ter]): a rule
        // line that states the general rule in its own words is pinned, and the extraction only points at its cases; the
        // lines that hold the value asked, well ahead of the others, go straight to the extraction.
        var pinnedRule = kind == "rule" && draft.AnswerKind == "rule" ? draft.Items.FirstOrDefault(static item => item.Role == "rule")?.First : null;
        var valuedLead = kind is "duration" or "amount" && draft.AnswerKind == kind && draft.Items.Count is >= 1 and <= 3
            ? shortlist.Where(candidate => draft.Items.Any(item => item.Role == "value" && item.First.Id == candidate.Line.Id)).ToList()
            : [];
        var clearValues = valuedLead.Count > 0 &&
            shortlist.Where(candidate => !valuedLead.Contains(candidate)).All(candidate => candidate.Score <= valuedLead.Min(static lead => lead.Score) - V4Retrieval.Margin);
        List<V4Candidate> primaries;
        List<V4Candidate> supporting;
        if (pinnedRule is not null)
        {
            primaries = shortlist.Where(candidate => candidate.Line.Id == pinnedRule.Id).ToList();
            supporting = shortlist.Where(candidate => candidate.Line.Id != pinnedRule.Id && candidate.Detectors.Contains("keyword") && candidate.Detectors.Contains("type_pattern") &&
                !candidate.Detectors.Contains("other_party")).Take(12).ToList();
        }
        else if (clearValues)
        {
            primaries = valuedLead;
            supporting = [];
        }
        else
        {
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

            primaries = shortlist.Where(static candidate => candidate.Role == "primary").Take(4).ToList();
            supporting = shortlist.Where(static candidate => candidate.Role == "supporting").Take(8).ToList();
        }

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

            var reply = Ask(draft, model, V4Generation.ExtractionCall(row, kind, tables, lines, DefinitionsIn(lines), dictionary, pinnedRule), cancellationToken);
            extraction = reply is null ? null : V4Generation.ReadExtraction(reply.Content);
            if (reply is null)
            {
                break;
            }

            items = extraction is null ? null : V4Generation.Validate(extraction, kind, lines, out failed, pinnedRule);
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

        draft.AnswerKind = kind == "date" && items.All(static item => item.Value is null) ? "text" : kind;
        draft.Found = true;
        draft.Complete = extraction.Complete && kind != "rule";

        // One case per article: a second line of an article already quoted stays one step away, in the article itself. A
        // case the model pointed at that states another party's right is not a case of the rule asked, and is left out.
        var found = retrieval.Candidates.ToDictionary(static candidate => candidate.Line.Id, StringComparer.Ordinal);
        var kept = new List<V4Generation.Checked>();
        var byCode = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Role is "case" or "item" && kept.Any(other => other.Role == item.Role && other.Line.Article == item.Line.Article))
            {
                continue;
            }

            if (item.Role == "case" && found.TryGetValue(item.Line.Id, out var candidate) && candidate.Detectors.Contains("other_party"))
            {
                continue;
            }

            kept.Add(item);
        }

        if (pinnedRule is not null)
        {
            // A line that states a case in the direct form of the rule asked ("l'assureur peut résilier le contrat", in an
            // article of its own) is a case whether or not the model pointed at it: code is sure of the form, and a model
            // that names two cases of five has not listed the cases. The cases are then shown in the order of the text.
            foreach (var candidate in supporting.Where(static candidate => candidate.Detectors.Contains("direct_form")))
            {
                if (kept.Count >= V3AssistantPipeline.MaximumQuotes)
                {
                    break;
                }

                if (!kept.Any(item => item.Line.Article == candidate.Line.Article))
                {
                    kept.Add(new V4Generation.Checked(candidate.Line, "case", null));
                    byCode.Add(candidate.Line.Id);
                }
            }

            kept = kept.Where(static item => item.Role != "case")
                .Concat(kept.Where(static item => item.Role == "case").OrderBy(item => tables.Article(item.Line.Article)!.Position).ThenBy(static item => item.Line.Index))
                .ToList();
        }

        if (kind is "duration" or "amount" && shortlist.Count > 0)
        {
            // The line code itself ranks first, under a heading that names what is asked and with a value of the kind asked
            // written on it, is quoted whether or not the model pointed at it: the general rule is said before a special
            // regime, and a model that prefers the special one has not answered the question asked. Two values are then
            // shown as two, each with its article (several_by_scope).
            var lead = shortlist[0];
            if (lead.Values.Count > 0 && lead.Detectors.Contains("keyword") && lead.Detectors.Contains("title") && kept.All(item => item.Line.Id != lead.Line.Id))
            {
                kept.Insert(0, new V4Generation.Checked(lead.Line, "value", lead.Values[0]));
                byCode.Add(lead.Line.Id);
            }
        }

        if (draft.AnswerKind == "text")
        {
            // Passages are shown in the order code's own detectors give them: the line under the matching heading first.
            kept = kept.OrderByDescending(item => found.TryGetValue(item.Line.Id, out var scored) ? scored.Score : 0)
                .ThenBy(item => tables.Article(item.Line.Article)!.Position).ThenBy(static item => item.Line.Index).ToList();
        }

        draft.Items = kept.Take(V3AssistantPipeline.MaximumQuotes)
            .Select(item => new Item(item.Line, item.Line, draft.AnswerKind == "text" ? "passage" : item.Role, item.Value, byCode.Contains(item.Line.Id))).ToList();
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

        if (kind is "duration" or "amount" && draft.Items.Count > 1)
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
                if (draft.LatestHeld is not null)
                {
                    writer.WriteString("corpus", draft.LatestHeld);
                }
                else if (dateRef == "today")
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
            if (draft.LatestHeld is not null)
            {
                writer.WriteString("latest_held", draft.LatestHeld);
            }

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
            foreach (var planned in decision?.Activations ?? [])
            {
                // What the record says ran is what ran: a model step the dispatcher allowed and the loop did not need is
                // written off, with why.
                var activation = planned;
                if (planned.On && planned.Id is "arbiter" or "extraction" && !draft.Calls.Any(call => call.Step == planned.Id))
                {
                    activation = planned with
                    {
                        On = false,
                        Reason = draft.Route == "fact" ? "one_line_clearly_leads"
                            : planned.Id == "arbiter" && draft.Calls.Count > 0 ? "code_lead_is_clear"
                            : draft.Absence is not null ? "nothing_to_rank"
                            : "not_reached",
                    };
                }

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
                if (item.ByCode)
                {
                    // Said in the record, so that what a model pointed at is counted as that and no more.
                    writer.WriteString("by", "code");
                }

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

            // The articles not held: the one the reader cites, and those right before or after an article the answer
            // quotes ("l'article 16 existe mais Lex n'en détient pas le texte", beside article 15 quoted).
            // And an article the dictionary names for a concept of the question, in that concept's home act.
            var quotedArticles = draft.Items.Select(static item => item.First.Article).ToHashSet(StringComparer.Ordinal);
            var ofConcepts = tables is null || draft.Items.Count == 0
                ? []
                : row.Concepts.Select(static read => V4Dictionary.Insurance.Concept(read.Concept)!)
                    .Where(concept => concept.HomeActs.Any(home => tables.Work.EndsWith("/" + home, StringComparison.Ordinal)))
                    .SelectMany(static concept => concept.Articles).ToHashSet(StringComparer.Ordinal);
            var notHeld = (draft.NotHeld is null ? [] : new[] { draft.NotHeld })
                .Concat(tables is null ? [] : tables.NotHeld.Where(missing => quotedArticles.Contains(missing.After) || quotedArticles.Contains(missing.Before) || ofConcepts.Contains(missing.Id)).Select(static missing => missing.Id))
                .Where(V3AssistantPlanValidator.IsArticleId).Distinct(StringComparer.Ordinal).Take(V4PlanRecord.MaximumNotHeld).ToList();
            if (work is not null && notHeld.Count > 0)
            {
                writer.WriteStartArray("not_held");
                foreach (var article in notHeld)
                {
                    writer.WriteStringValue(article);
                }

                writer.WriteEndArray();
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
