using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>
/// The wider reading (the owner's order of 2026-11-19: "every Luxembourg act, candidates only"). When Lex's own operations
/// quoted nothing for a question, code routes it to the acts of the law index whose words it holds
/// (<see cref="V4LawIndex.Route"/>). A small act is given whole to the outline call; a big code is read by its parts
/// first (<see cref="V4LawIndex.Sections"/>: a model names parts by ids it was given), and the articles of the parts
/// named go to the outline call with the articles of the rest that best hold the question's words
/// (<see cref="V4LawIndex.Prefilter(V4ActTables, IReadOnlyList{string}, int, Func{V4Article, bool}?)"/>). Then the two calls
/// of the outline reading are made over that closed list: a model names articles by ids it was given, then points at
/// lines it was given; code checks both and cuts the quotes from the stored text. Whatever is found is shown as
/// candidates, never as the answer.
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

    /// <summary>An act of at most this many articles is given whole to the outline call; a bigger one is read by its parts first.</summary>
    internal const int EveryLawSmallAct = 250;

    /// <summary>How many parts of one big act the parts call may name.</summary>
    internal const int EveryLawSectionsNamed = 4;

    /// <summary>How many articles of the rest of a big act are given beside the articles of the parts named: the best by the question's words.</summary>
    internal const int EveryLawBeside = 30;

    /// <summary>How many articles of a big act are given when it has no parts, or none was named: the best by the question's words.</summary>
    internal const int EveryLawNoParts = 120;

    /// <summary>A route is weak when its best act scores under this many times the router's floor.</summary>
    internal const int EveryLawWeakRoute = 3;

    /// <summary>The most acts read on a weak route: the acts the router kept and the general codes beside them.</summary>
    internal const int EveryLawWeakActs = 5;

    /// <summary>The most lines one outline call holds.</summary>
    internal const int EveryLawLines = 400;

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

    // The reader asks leave for an act of their own, by the inverted first person ("puis-je exercer la profession ... ?",
    // "ai-je le droit de ... ?", "may I ... ?"): a decision on their own case, which stays refused. "Est-ce que je peux
    // ... ?" and "mon employeur peut-il ... ?" are not that form and are read.
    private static readonly Regex OwnLeavePattern = new(
        @"(?<![\p{L}])(?:puis|peux|pourrais|pourrai)-je(?![\p{L}])|(?<![\p{L}])(?:ai|aurais|aurai)-je\s+le\s+droit(?![\p{L}])|(?<![\p{L}])suis-je\s+(?:autorise|autorisee|oblige|obligee)(?![\p{L}])|(?<![\p{L}])(?:may\s+i|am\s+i\s+allowed)(?![\p{L}])",
        EveryLawOptions, V4Text.PatternTimeout);

    // A question on Lex itself that is not put in the second person ("Combien coûte une réponse ?"), or something asked
    // of Lex that is no question ("Appelez-moi au ...").
    private static readonly Regex AboutLexPattern = new(
        @"(?<![\p{L}])(?:lex|chatbot|une\s+reponse|cette\s+reponse|ta\s+reponse|tes\s+reponses|an\s+answer\s+cost|this\s+site|ce\s+site)(?![\p{L}])" +
        @"|(?<![\p{L}])(?:appelez|rappelez|contactez|ecrivez|appelle|rappelle|contacte)-(?:moi|nous)(?![\p{L}])|(?<![\p{L}])(?:call|phone|email|contact)\s+(?:me|us)(?![\p{L}])",
        EveryLawOptions, V4Text.PatternTimeout);

    // Not a question on what a text provides: the weather, who holds an office ("qui est le directeur de ..."; "qui est
    // responsable de ..." is a question on a rule and is not stopped). A short hand list; the outline call's own
    // rule ("return no article ... never guess") is what stands behind it.
    private static readonly Regex NoRulePattern = new(
        @"(?<![\p{L}])(?:quel\s+temps\s+fait|meteo|weather)(?![\p{L}])" +
        @"|^\s*qui\s+est\s+(?:le|la|l')\s*(?:directeur|directrice|president|presidente|ministre|pdg|patron|patronne|chef|maire|bourgmestre|premier)(?![\p{L}])" +
        @"|^\s*who\s+is\s+the\s+(?:ceo|director|president|minister|head|mayor|prime|boss|chair)(?![\p{L}])",
        EveryLawOptions, V4Text.PatternTimeout);

    // The law of another country ("le droit allemand", "loi belge du ..."): Lex holds Luxembourg's. A place ("j'habite
    // en France et je travaille au Luxembourg") and a language ("la loi en français") are not that.
    private static readonly Regex OtherCountryPattern = new(
        @"(?<![\p{L}])(?:droit|loi|lois|code|legislation|law)\s+(?:[a-z']+\s+){0,3}?(?<!en\s)(?:allemande?s?|belges?|francaise?s?|suisses?|neerlandaise?s?|german|french|belgian|swiss|dutch)(?![\p{L}])" +
        @"|(?<![\p{L}])(?:german|french|belgian|swiss|dutch)\s+law(?![\p{L}])",
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
        "^(?:cdd|cdi|dap|ccp|tva|sarl|sas|sci|asbl|snc|scs|pme|rcs|lbr|cns|cnap|ccss|adem|itm|cnpd|revis|rmg|ssm|pacs|aed|acd|sicav|opc|fis|cedies|cae|ctie)$",
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
        if (row.Greeting || row.Words.Count <= 1 || V4QuestionParser.AsksNothingNamed(row))
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

        // The owner's point 2, as on the insurance road: only a decision asked on the reader's own facts stays refused (a
        // judgement, advice, a prediction, "who pays" after a fact of the reader's own, an instruction to Lex), and the
        // reader asking leave for an act of their own ("puis-je exercer ... ?"). A general question in personal words,
        // with a cue of R15 or without ("mon employeur peut-il me licencier pendant un arrêt maladie ?"), is read: the
        // rule is shown as candidates, with the line that the text does not say how it applies to the reader's situation.
        var question = row.Question;
        if (V4Boundary.DecidesOwnFacts(question, 0, question.Length) || OwnLeavePattern.IsMatch(row.Folded))
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
        var named0 = false;
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
            named0 = true;
        }

        if (kept.Count == 0)
        {
            return "every_law_router_keeps_nothing";
        }

        // A weak route (the best act scores under three times the router's floor, and the question names no act): the
        // question's everyday words say little of where its rule stands, and the rule is often one of the general law
        // ("un chien mord un passant, qui paie ?", "prendre une voiture sans demander, est-ce du vol ?": the fresh set of
        // 2026-11-19 routed neither to the Code civil nor to the Code pénal). The two general codes are read beside the
        // acts kept, by their parts, as any big act is.
        if (!named0 && kept[0].ScoreCents < EveryLawWeakRoute * laws.MinScoreCents)
        {
            // The general law: the Code civil and the Code pénal (a local list: a static one would be a registry to pin).
            foreach (var general in new[] { "/lu-legilux/code-civil", "/lu-legilux/code-penal" })
            {
                if (kept.Count < EveryLawWeakActs && laws.Act(general) is { } code && !Routed(kept, code.Work))
                {
                    kept.Add(new V4RoutedAct(code, 0, false));
                }
            }
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

        // The words articles are found by inside an act: the question's, and the law's words for its everyday ones.
        var tokens = laws.Extended(laws.Tokens(question));

        // An article whose identifier a plan cannot name (a range of repealed articles, "art_518_à_520") is not offered.
        static bool Nameable(V4Article article) => V3AssistantPlanValidator.IsArticleId(article.Id);

        var opened = new List<(string Title, V4ActTables Tables, List<V4OutlineArticle> All, IReadOnlyList<V4LawSection> Sections)>();
        var latest = new Dictionary<V4ActTables, string>(ReferenceEqualityComparer.Instance);
        foreach (var kept in routed)
        {
            // The tables of a big code are built on its first question and cannot be stopped half-way: past the deadline,
            // no further act is opened.
            cancellationToken.ThrowIfCancellationRequested();

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

            var all = laws.Prefilter(tables, tokens, int.MaxValue, Nameable);
            if (all.Count == 0)
            {
                continue;
            }

            // A big act is read by its parts first, when its article numbers say them (V4LawIndex.Sections).
            opened.Add((kept.Act.Title, tables, all, all.Count > EveryLawSmallAct ? laws.Sections(tables) : []));
            if (at.Facts.TextStatus == "latest_held" && at.Facts.VersionFrom is { } from)
            {
                latest[tables] = from;
            }
        }

        if (opened.Count == 0)
        {
            return new V4PlanOutcome(null, 0, "every_law_no_act_held");
        }

        var draft = new Draft { Route = "open", AnswerKind = "text" };

        // Step 0, for the big acts with parts: the model names parts of the closed list, by ids it was given; code checks
        // the ids. Said in the reason, so that a reading that ends on nothing says what the parts call gave.
        var partsOf = new Dictionary<int, HashSet<string>>();
        var sectioned = string.Empty;
        var withParts = new List<(string Title, IReadOnlyList<V4LawSection> Sections)>();
        var placeOf = new List<int>();
        for (var at = 0; at < opened.Count; at++)
        {
            if (opened[at].Sections.Count > 0)
            {
                withParts.Add((opened[at].Title, opened[at].Sections));
                placeOf.Add(at);
            }
        }

        if (withParts.Count > 0)
        {
            var most = Math.Min(8, EveryLawSectionsNamed * withParts.Count);
            var (parts, partIds) = V4Generation.SectionCall(question, withParts, most);
            var answered = Ask(draft, model, parts, cancellationToken);
            var chosen = answered is null ? null : V4Generation.ReadSections(answered.Content, partIds.Count, most);
            sectioned = chosen is null ? "+sections_unreadable" : chosen.Count == 0 ? "+sections_named_nothing" : "+sections_named";
            foreach (var pick in chosen ?? [])
            {
                var (act, section) = partIds[pick];
                if (!partsOf.TryGetValue(placeOf[act], out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    partsOf[placeOf[act]] = set;
                }

                foreach (var article in section.Articles)
                {
                    set.Add(article.Id);
                }
            }
        }

        // What the outline call is given of each act, in the order of the document: every article of a small act; of a big
        // act, every article of the parts named and, beside them, the articles of the rest of the act that best hold the
        // question's words (a deterministic union [9B]); of a big act with no part named, or none to name, its best
        // articles by the question's words. One call holds at most EveryLawLines lines: an act that would pass it gives
        // its best lines by the question's words.
        var held = new List<(string Title, V4ActTables Tables, IReadOnlyList<V4OutlineArticle> Articles)>();
        var room = EveryLawLines;
        for (var at = 0; at < opened.Count && room > 0; at++)
        {
            var (title, tables, all, _) = opened[at];
            List<V4OutlineArticle> given;
            if (all.Count <= EveryLawSmallAct)
            {
                given = all;
            }
            else if (partsOf.TryGetValue(at, out var named) && named.Count > 0)
            {
                var beside = new HashSet<string>(StringComparer.Ordinal);
                // Beside the parts a model named: thirty articles by the question's words when the parts are the code's own
                // (its numbers say them); the full hundred and twenty when they are runs cut by code, which a chapter may straddle.
                var runs = opened[at].Sections.Count > 0 && opened[at].Sections[0].Key.StartsWith("run", StringComparison.Ordinal);
                foreach (var entry in laws.Prefilter(tables, tokens, runs ? EveryLawNoParts : EveryLawBeside, article => Nameable(article) && !named.Contains(article.Id)))
                {
                    beside.Add(entry.Article.Id);
                }

                given = laws.Prefilter(tables, tokens, int.MaxValue, article => Nameable(article) && (named.Contains(article.Id) || beside.Contains(article.Id)));
            }
            else
            {
                given = laws.Prefilter(tables, tokens, EveryLawNoParts, Nameable);
            }

            if (given.Count > room)
            {
                var within = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in given)
                {
                    within.Add(entry.Article.Id);
                }

                given = laws.Prefilter(tables, tokens, room, article => within.Contains(article.Id));
            }

            if (given.Count > 0)
            {
                room -= given.Count;
                held.Add((title, tables, given));
            }
        }

        if (held.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, "every_law_no_act_held" + sectioned);
        }

        // Step 1: the model names articles of the closed list, by ids it was given.
        var (outline, ids) = V4Generation.OutlineCallOf(question, held);
        var named1 = Ask(draft, model, outline, cancellationToken);
        var picks = named1 is null ? null : V4Generation.ReadOutline(named1.Content, ids.Count);
        if (picks is null || picks.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, (cancellationToken.IsCancellationRequested ? "every_law_deadline" : picks is null ? "every_law_outline_unreadable" : "every_law_outline_names_nothing") + sectioned);
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
            return new V4PlanOutcome(null, draft.Cost, "every_law_articles_hold_no_line" + sectioned);
        }

        // Step 2: the extraction points at the lines that answer, among those lines only; code checks each.
        draft.Passes.Add(new V4RecordPass(1, "first_pass", "none", "every_law_read"));
        var reply = Ask(draft, model, V4Generation.ExtractionCall(row, "text", scope, lines, [], dictionary), cancellationToken);
        var extraction = reply is null ? null : V4Generation.ReadExtraction(reply.Content);
        var items = extraction is null ? null : V4Generation.Validate(extraction, "text", lines, out _);
        if (items is null || items.Count == 0)
        {
            return new V4PlanOutcome(null, draft.Cost, (cancellationToken.IsCancellationRequested ? "every_law_deadline" : items is null ? "every_law_reading_refused" : "every_law_reading_found_nothing") + sectioned);
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

        // The advice boundary: a decision asked on the reader's own facts never comes here (EveryLawExit). A question
        // worded for the reader's case, or one that holds a cue of R15 and is no question on the rule, is answered by the
        // general rule, said not to say how it applies to the reader, as the outline reading says it.
        var cues = V3AssistantParsers.ApplicationCues(question, 0, question.Length);
        var ruleQuestion = cues.Count > 0 && V4Boundary.RuleQuestion(question, 0, question.Length, cues);
        if (((cues.Count > 0 && !ruleQuestion) || V4Boundary.PersonalMarkers(question, 0, question.Length).Count > 0 || V4Boundary.AsksOfOneself(question, 0, question.Length)) && !draft.Caveats.Contains("not_your_case"))
        {
            draft.Caveats.Add("not_your_case");
        }

        var json = Write(input, row, draft, scope, null, null, dateRef, ruleQuestion, "answer", "text", scope.Work, []);
        return new V4PlanOutcome(AsCandidates(json), draft.Cost, "route:every_law" + sectioned, Articles: quoted);
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
