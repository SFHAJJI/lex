using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>
/// The wider reading (the owner's order of 2026-11-19: "every Luxembourg act, candidates only"). When Lex's own operations
/// quoted nothing for a question, code routes it to the acts of the law index whose words it holds
/// (<see cref="V4LawIndex.Route"/>), code keeps the articles of those acts that hold its words
/// (<see cref="V4LawIndex.Prefilter"/>), and the two calls of the outline reading are made over that closed list: a model
/// names articles by ids it was given, then points at lines it was given; code checks both and cuts the quotes from the
/// stored text. Whatever is found is shown as candidates, never as the answer.
/// </summary>
/// <remarks>
/// It is asked only behind the switch (<see cref="V4PlanInput.EveryLaw"/>), only after Lex's own operations ended on a
/// clarification or a pointer with no quotation (the pipeline asks it, <see cref="V3AssistantPipeline"/>), and never for a
/// question those operations are the right reader of: an article cited by its number, an EU act, a change over time, words
/// the reader quotes, an act named that the index does not know by that name, a question put to Lex or asking an opinion.
/// </remarks>
public static partial class V4Bricks
{
    private const RegexOptions EveryLawOptions = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // An EU act, by a name people give it, by its kind, by its number, or by its subject where the only text is the Union's.
    // The tables are built for Luxembourg acts in French only: an EU act keeps Lex's own operations.
    private static readonly Regex EuropeanPattern = new(
        @"(?<![\p{L}\d])(?:rgpd|gdpr|dora|mica|nis ?2|psd ?2|emir|mifid|mifir|priips|eidas|ai act|ia act|solvabilite ii|solvency ii)(?![\p{L}\d])" +
        @"|\((?:ue|ce|eu|ec|cee|eec)\)" +
        @"|(?<![\p{L}])directives?(?![\p{L}])" +
        @"|(?<![\p{L}])(?:reglement|regulation)s?\s+(?:europeens?|delegues?|d'execution|general\s+sur|sur\s+l'intelligence|on\s+artificial)" +
        @"|(?<![\p{L}])(?:union\s+europeenne|european\s+union|droit\s+europeen|eu\s+law)(?![\p{L}])" +
        @"|intelligence\s+artificielle|artificial\s+intelligence" +
        @"|(?<!\d)(?:19|20)\d{2}/\d{1,4}(?!\d)",
        EveryLawOptions, V4Text.PatternTimeout);

    // A question on Lex itself that is not put in the second person ("Combien coûte une réponse ?").
    private static readonly Regex AboutLexPattern = new(
        @"(?<![\p{L}])(?:lex|chatbot|une\s+reponse|cette\s+reponse|ta\s+reponse|tes\s+reponses|an\s+answer\s+cost|this\s+site|ce\s+site)(?![\p{L}])", EveryLawOptions, V4Text.PatternTimeout);

    // Not a question on what a text provides: the weather, who a person is. A short hand list; the outline call's own
    // rule ("return no article ... never guess") is what stands behind it.
    private static readonly Regex NoRulePattern = new(
        @"(?<![\p{L}])(?:quel\s+temps\s+fait|meteo|weather)(?![\p{L}])|^\s*(?:qui\s+est|qui\s+sont|who\s+is|who\s+are)(?![\p{L}])", EveryLawOptions, V4Text.PatternTimeout);

    // The law of another country ("le droit allemand", "loi belge du ...", "en France"): Lex holds Luxembourg's.
    private static readonly Regex OtherCountryPattern = new(
        @"(?<![\p{L}])(?:droit|loi|lois|code|legislation|law)\s+(?:[a-z']+\s+){0,3}?(?:allemande?s?|belges?|francaise?s?|suisses?|neerlandaise?s?|german|french|belgian|swiss|dutch)(?![\p{L}])" +
        @"|(?<![\p{L}])(?:en|in)\s+(?:france|belgique|allemagne|suisse|belgium|germany|switzerland)(?![\p{L}])|(?<![\p{L}])(?:german|french|belgian|swiss)\s+law(?![\p{L}])",
        EveryLawOptions, V4Text.PatternTimeout);

    // What is not legislation and is not in the corpus: a regulator's circular, case law.
    private static readonly Regex NotLegislationPattern = new(
        @"(?<![\p{L}])(?:circulaires?|circulars?|cssf|jurisprudence|case\s+law)(?![\p{L}])", EveryLawOptions, V4Text.PatternTimeout);

    // Words the reader quotes ("Que dit la loi sur « bail commercial » ?"): a search of those words, which Lex's own
    // operations run, and whose several places they list.
    private static readonly Regex QuotedWordsPattern = new(@"[«“""][^«»“”""]{2,}[»”""]", EveryLawOptions, V4Text.PatternTimeout);

    // A code named ("Code civil", "code de la consommation"): read only when the index knows it by that name.
    private static readonly Regex CodeNamedPattern = new(@"(?<![\p{L}])code\s+(?:de\s+la\s+|de\s+l'|du\s+|des\s+|de\s+|d')?[a-z]{4,}", EveryLawOptions, V4Text.PatternTimeout);

    private static readonly Regex CapitalsPattern = new(@"(?<![\p{L}\d])\p{Lu}[\p{Lu}\d]{2,}(?![\p{L}\d])", EveryLawOptions, V4Text.PatternTimeout);

    // Capitals of everyday life that name no act: a kind of contract, a tax, a company form, an administration. A pattern,
    // not a set: a static collection here would be a registry of its own for the census to pin.
    private static readonly Regex EverydayCapitalsPattern = new(
        "^(?:cdd|cdi|tva|sarl|sas|sci|asbl|snc|scs|pme|rcs|lbr|cns|cnap|ccss|adem|itm|cnpd|revis|rmg|ssm|pacs|aed|acd|sicav|opc|fis|cedies|cae|ctie)$",
        EveryLawOptions, V4Text.PatternTimeout);

    /// <summary>
    /// Why the wider reading is not made for a question, or null when it is, with the acts the router kept. Code only: no
    /// model is asked and no act is opened.
    /// </summary>
    public static string? EveryLawExit(V4PlanInput input, V4QuestionRow row, V4LawIndex laws, out List<V4RoutedAct> routed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(laws);
        routed = [];
        if (row.Greeting || row.Words.Count <= 1)
        {
            return "every_law_not_a_question";
        }

        // A question asked beside an earlier answer (a context chip: an act, a date) is read in that answer's act, by
        // Lex's own operations. The dates the question itself writes are references too, and are not that.
        var chips = false;
        foreach (var reference in input.References ?? [])
        {
            chips |= reference.Kind.StartsWith("context_", StringComparison.Ordinal);
        }

        if (chips || V4Dispatcher.AsksOfAnEarlierAnswer(row))
        {
            return "every_law_refers_to_earlier";
        }

        // What Lex's own operations refuse stays refused: a question that asks to apply the law (a cue of R15 that is no
        // question on the rule), and a decision asked on the reader's own facts (the owner's point 2).
        var question = row.Question;
        var cues = V3AssistantParsers.ApplicationCues(question, 0, question.Length);
        if ((cues.Count > 0 && !V4Boundary.RuleQuestion(question, 0, question.Length, cues)) || V4Boundary.DecidesOwnFacts(question, 0, question.Length))
        {
            return "every_law_asks_application";
        }

        if (V4Dispatcher.CitesArticle(row))
        {
            return "every_law_cites_article";
        }

        var reading = V4Dispatcher.ReadingDates(row, input.Dates);
        if (reading.Count > 1)
        {
            return "every_law_several_dates";
        }

        if (V4Dispatcher.AsksChange(row, reading.Count))
        {
            return "every_law_asks_change";
        }

        if (V4Dispatcher.AddressesLex(row.Folded) || AboutLexPattern.IsMatch(row.Folded))
        {
            return "every_law_put_to_lex";
        }

        // An opinion asked, or a question that is not about what a text provides (the weather, who someone is). A subject
        // left unsaid ("ça") is not tested here: the router needs words that weigh, and "une période d'essai, ça peut
        // durer combien de temps ?" says its subject.
        if (V4Dispatcher.AsksAnOpinion(row.Folded) || NoRulePattern.IsMatch(row.Folded))
        {
            return "every_law_no_rule_asked";
        }

        if (OtherCountryPattern.IsMatch(row.Folded))
        {
            return "every_law_another_country";
        }

        var european = false;
        foreach (var act in row.Acts)
        {
            european |= act.EuAct is not null;
        }

        if (european || EuropeanPattern.IsMatch(row.Folded))
        {
            return "every_law_eu_act";
        }

        if (NotLegislationPattern.IsMatch(row.Folded))
        {
            return "every_law_not_legislation";
        }

        if (QuotedWordsPattern.IsMatch(row.Question))
        {
            return "every_law_words_quoted";
        }

        var kept = laws.Route(row.Question);

        // Capitals that are no everyday word, no abbreviation of the dictionary and no word of a name of the index name
        // something Lex cannot tell (an act, a body): Lex's own operations look for it.
        var letters = 0;
        var capitals = 0;
        foreach (var character in row.Question)
        {
            letters += char.IsLetter(character) ? 1 : 0;
            capitals += char.IsUpper(character) ? 1 : 0;
        }

        if (letters > 0 && capitals * 2 <= letters)
        {
            foreach (Match match in CapitalsPattern.Matches(row.Question))
            {
                var word = V4LawIndex.Normalise(match.Value);
                if (EverydayCapitalsPattern.IsMatch(word) || laws.NamesWord(word) || InADictionaryAbbreviation(word) || InAnActNamed(row, match.Index, match.Index + match.Length))
                {
                    continue;
                }

                return "every_law_names_capitals";
            }
        }

        // An act named (a code, a law by its date, an act of the insurance index): only that act is read, when the law
        // index knows it by that name or holds it, whatever its words weigh; an act the index does not know by name is
        // never guessed from the other words of the question.
        var mentioned = false;
        foreach (var act in row.Acts)
        {
            mentioned |= act.Work is not null;
        }

        if (mentioned || CodeNamedPattern.IsMatch(row.Folded) || V4Dispatcher.NamesAnActByItsDate(row.Folded))
        {
            var named = laws.Named(row.Question);
            foreach (var act in row.Acts)
            {
                if (act.Work is not null && laws.Act("/lu-legilux/" + act.Work) is { } held && named.Count < laws.MaxActs && !Routed(named, held.Work))
                {
                    named.Add(new V4RoutedAct(held, laws.NameBonusCents, true));
                }
            }

            if (named.Count == 0)
            {
                return "every_law_names_an_act_not_known";
            }

            kept = named;
        }
        if (kept.Count == 0)
        {
            return "every_law_router_keeps_nothing";
        }

        routed = kept;
        return null;
    }

    private static bool Routed(List<V4RoutedAct> acts, string work)
    {
        foreach (var act in acts)
        {
            if (act.Act.Work == work)
            {
                return true;
            }
        }

        return false;
    }

    private static bool InADictionaryAbbreviation(string word)
    {
        var words = V4Text.Words(word);
        foreach (var abbreviation in V4Dictionary.Insurance.Abbreviations)
        {
            if (abbreviation.Short.In(words))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InAnActNamed(V4QuestionRow row, int start, int end)
    {
        foreach (var act in row.Acts)
        {
            if (start >= act.Start && end <= act.End)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The wider reading of a question: a plan /2 that quotes candidates of one act the router kept, or none with the
    /// reason. Every answer it gives is of kind <c>candidates</c>, and a question worded for the reader's case carries
    /// <c>not_your_case</c>; what asks to apply the law or to decide on the reader's own facts is never read here.
    /// </summary>
    public static V4PlanOutcome PlanEveryLaw(V4PlanInput input, IV4Corpus corpus, IV4Model? model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(corpus);
        var laws = input.Laws ?? V4LawIndex.Embedded;
        if (!input.EveryLaw || model is null || laws is null)
        {
            return new V4PlanOutcome(null, 0, "every_law_off");
        }

        var dictionary = V4Dictionary.Insurance;
        var question = input.MaskedQuestion;
        var parsed = V4QuestionParser.Parse(question, input.Locale, dictionary, V4CorpusIndex.Insurance);
        if (EveryLawExit(input, parsed, laws, out var routed) is { } exit)
        {
            return new V4PlanOutcome(null, 0, exit);
        }

        // The row the reading and its record carry reads nothing of the insurance dictionary: a word it shares with
        // another law ("préavis", "caché") is not said to be an insurance term beside an article of that other law.
        var row = new V4QuestionRow(parsed.Question, parsed.Folded, [.. parsed.Words], parsed.Language, parsed.Greeting, parsed.OtherDomain, [], [], [.. parsed.Acts], [.. parsed.Kinds], [.. parsed.Unmatched]);
        var reading = V4Dispatcher.ReadingDates(row, input.Dates);
        var dateRef = reading.Count == 1 ? reading[0].Ref : "today";
        var date = reading.Count == 1 ? reading[0].Iso : input.Today;
        var tokens = laws.Tokens(question);
        var held = new List<(string Title, V4ActTables Tables, IReadOnlyList<V4OutlineArticle> Articles)>();
        var latest = new Dictionary<V4ActTables, string>(ReferenceEqualityComparer.Instance);
        foreach (var kept in routed)
        {
            // The tables are built for a Luxembourg act, in French; an act whose text the rights rule withholds gives none.
            if (!kept.Act.Work.StartsWith("/lu-legilux/", StringComparison.Ordinal) || !V3AssistantPlanValidator.IsWorkAddress(kept.Act.Work))
            {
                continue;
            }

            var at = corpus.Act(kept.Act.Work["/lu-legilux/".Length..], date);
            if (at.Tables is not { } tables)
            {
                continue;
            }

            // An article whose identifier a plan cannot name (a range of repealed articles, "art_518_à_520") is not offered.
            var articles = new List<V4OutlineArticle>();
            foreach (var entry in laws.Prefilter(tables, tokens))
            {
                if (V3AssistantPlanValidator.IsArticleId(entry.Article.Id))
                {
                    articles.Add(entry);
                }
            }

            if (articles.Count == 0)
            {
                continue;
            }

            held.Add((kept.Act.Title, tables, articles));
            if (at.Facts.TextStatus == "latest_held" && at.Facts.VersionFrom is { } from)
            {
                latest[tables] = from;
            }
        }

        if (held.Count == 0)
        {
            return new V4PlanOutcome(null, 0, "every_law_no_act_held");
        }

        var draft = new Draft { Route = "open", AnswerKind = "text" };

        // Step 1: the model names articles of the closed list, by ids it was given.
        var (outline, ids) = V4Generation.OutlineCallOf(question, held);
        var named = Ask(draft, model, outline, cancellationToken);
        var picks = named is null ? null : V4Generation.ReadOutline(named.Content, ids.Count);
        if (picks is null || picks.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, picks is null ? "every_law_outline_unreadable" : "every_law_outline_names_nothing");
        }

        // One act answers: the act of the first article named; the articles named of another act are left.
        var scope = ids[picks[0]].Tables;
        var articlesRead = new List<V4Article>();
        foreach (var pick in picks)
        {
            if (ReferenceEquals(ids[pick].Tables, scope) && V3AssistantPlanValidator.IsArticleId(ids[pick].Article.Id))
            {
                articlesRead.Add(ids[pick].Article);
            }
        }

        // As the outline reading does: code adds the articles of that act that hold a rare word of the question (six
        // letters or more, in at most four articles), at most three; the extraction still decides which lines answer.
        foreach (var unread in row.Unmatched)
        {
            var word = row.Folded[unread.Start..unread.End];
            if (word.Length < 6 || articlesRead.Count >= V4Generation.MaximumOutlinePicks + 3)
            {
                continue;
            }

            var stem = V4Text.Stem(word);
            var holding = new List<string>();
            foreach (var line in scope.Lines)
            {
                if (line.Kind != "paragraph" || holding.Contains(line.Article, StringComparer.Ordinal))
                {
                    continue;
                }

                foreach (var written in line.Words)
                {
                    if (written.Text.Length >= 4 && V4Text.Stem(written.Text) == stem)
                    {
                        holding.Add(line.Article);
                        break;
                    }
                }

                if (holding.Count > 4)
                {
                    break;
                }
            }

            if (holding.Count is >= 1 and <= 4)
            {
                foreach (var id in holding)
                {
                    if (scope.Article(id) is { } more && V3AssistantPlanValidator.IsArticleId(id) && !Holds(articlesRead, id) && articlesRead.Count < V4Generation.MaximumOutlinePicks + 3)
                    {
                        articlesRead.Add(more);
                    }
                }
            }
        }

        var lines = new List<V4Line>();
        foreach (var article in articlesRead)
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
            return new V4PlanOutcome(null, draft.Cost, "every_law_articles_hold_no_line");
        }

        // Step 2: the extraction points at the lines that answer, among those lines only; code checks each.
        draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", "every_law_read"));
        var reply = Ask(draft, model, V4Generation.ExtractionCall(row, "text", scope, lines, [], dictionary), cancellationToken);
        var extraction = reply is null ? null : V4Generation.ReadExtraction(reply.Content);
        var items = extraction is null ? null : V4Generation.Validate(extraction, "text", lines, out _);
        if (items is null || items.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, items is null ? "every_law_reading_refused" : "every_law_reading_found_nothing");
        }

        // One passage per article, the most direct first, as the model gave them.
        var quoted = new List<string>();
        foreach (var item in items)
        {
            if (!quoted.Contains(item.Line.Article, StringComparer.Ordinal) && draft.Items.Count < V3AssistantPipeline.MaximumQuotes)
            {
                quoted.Add(item.Line.Article);
                draft.Items.Add(new Item(item.Line, item.Line, "passage", null));
            }
        }

        draft.Found = true;
        draft.LatestHeld = latest.TryGetValue(scope, out var heldOn) ? heldOn : null;
        draft.Caveats = [.. extraction!.Caveats];
        foreach (var article in articlesRead)
        {
            if (!quoted.Contains(article.Id, StringComparer.Ordinal) && !draft.More.Contains(article.Id, StringComparer.Ordinal) && draft.More.Count < V3AssistantPipeline.MaximumCandidates)
            {
                draft.More.Add(article.Id);
            }
        }

        // The advice boundary: a question that asks to apply the law, or to decide on the reader's own facts, never comes
        // here (EveryLawExit). A question worded for the reader's case is answered by the general rule, said not to say
        // how it applies to the reader, as the outline reading says it.
        var cues = V3AssistantParsers.ApplicationCues(question, 0, question.Length);
        var ruleQuestion = cues.Count > 0 && V4Boundary.RuleQuestion(question, 0, question.Length, cues);
        if (V4Boundary.PersonalMarkers(question, 0, question.Length).Count > 0 && !draft.Caveats.Contains("not_your_case"))
        {
            draft.Caveats.Add("not_your_case");
        }

        var json = Write(input, row, draft, scope, null, null, dateRef, ruleQuestion, "answer", "text", scope.Work, []);
        return new V4PlanOutcome(AsCandidates(json), draft.Cost, "route:every_law", Articles: quoted);
    }

    private static bool Holds(List<V4Article> articles, string id)
    {
        foreach (var article in articles)
        {
            if (article.Id == id)
            {
                return true;
            }
        }

        return false;
    }
}
