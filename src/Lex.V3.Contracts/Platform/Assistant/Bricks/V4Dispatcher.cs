using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One activation of the dispatcher: a detector or a model step, on or off, and the rule that says so.</summary>
public sealed record V4Activation(string Id, bool On, string Reason);

/// <summary>What the dispatcher decided: the route, the kind of answer it will look for, the activations, and the rule that chose the route.</summary>
public sealed record V4Decision(string Route, string Kind, IReadOnlyList<V4Activation> Activations, string Reason);

/// <summary>What the dispatcher reads: the parsed row, and the facts of the corpus in scope. Nothing else decides.</summary>
/// <param name="Application">The question asks to apply the law to a case (R15's cues, as narrowed by the owner's decision of 2026-11-18).</param>
/// <param name="EuActNotHeld">The question names an EU act of the corpus index that Lex does not hold.</param>
/// <param name="ActHeld">The act in scope has a text Lex can show on the date asked.</param>
/// <param name="ActHasHeadings">The act's articles carry headings (its outline can be searched).</param>
/// <param name="TermDefined">A term the act defines is named in the question.</param>
/// <param name="ModelAvailable">A model is wired for the arbiter and the extraction.</param>
public sealed record V4DispatchInput(
    V4QuestionRow Row,
    bool Application,
    bool EuActNotHeld,
    bool ActHeld,
    bool ActHasHeadings,
    bool TermDefined,
    bool ModelAvailable);

/// <summary>
/// The dispatcher (brick E): one function of code, readable top to bottom. "a piece of code we can read: a dispatcher that
/// turns the parsed question and the document profile into an explicit plan" [13]. The same question on the same data takes
/// the same route, always; no model chooses it. "The series's position: Approach B as default" [6C].
/// </summary>
public static class V4Dispatcher
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = V4Text.PatternTimeout;

    // A cited article ("l'article 39", "art. 62-3", "art. L. 121-6") or an identifier (a CELEX, an ELI): Lex's reference
    // reader answers these, as it did before the bricks.
    private static readonly Regex CitationPattern = new(
        @"(?<![\p{L}])art(?:icle|\.)?s?\s*(?:\d|1er|premier|l\.\s*\d)|(?<![\p{L}\d])[1-9]\d{4}[a-z]{1,2}\d{3,4}(?![\p{L}\d])|/eli/|eur-lex\.europa\.eu",
        Options, Timeout);

    // A question on how the law changed: Lex's own operations (diff, changes_in_period, timeline) answer these.
    // Words that are about a text's own life whatever stands beside them ("historique", "version", "abrogé").
    private static readonly Regex PointerPattern = new(
        @"\btel(?:le)?s? que (?:vise|defini|prevu)e?s? (?:par|a) l'article (\d+(?:er)?(?:-\d+)?)\b", RegexOptions.CultureInvariant, V4Text.PatternTimeout);

    /// <summary>
    /// The article a definition points at in place of defining ("tel que visé par l'article 15"), read in the clause of
    /// the folded defining line that opens with the term; null when the clause points nowhere.
    /// </summary>
    public static string? PointedArticle(string folded, string term)
    {
        ArgumentNullException.ThrowIfNull(folded);
        ArgumentException.ThrowIfNullOrEmpty(term);
        foreach (var clause in folded.Split(';'))
        {
            var at = clause.IndexOf(term, StringComparison.Ordinal);
            if (at < 0 || at > 12)
            {
                continue;
            }

            // A pointer and nothing after it: "tel que visé à l'article 43, point 14, de la loi modifiée du 7 décembre
            // 2015" points into another act and is not followed.
            var match = PointerPattern.Match(clause, at);
            return match.Success && clause.AsSpan(match.Index + match.Length).Trim().IsEmpty ? "art_" + match.Groups[1].Value : null;
        }

        return null;
    }

    // An act named by its kind and a date ("loi du 31 février 2020", "loi belge du 4 avril 2014"), and a question put to
    // Lex itself ("remplaces-tu un avocat", "qui es-tu", "ton prompt"): read on the folded question.
    private static readonly Regex DatedActPattern = new(
        @"\b(?:lois?|reglement|directive|arrete|ordonnance|law|act|regulation)\b(?:\s+[\p{L}-]+){0,3}?\s+(?:du|de|of)\s+\d{1,2}(?:er)?\s+\p{L}+\s+\d{4}\b",
        RegexOptions.CultureInvariant, V4Text.PatternTimeout);

    private static readonly Regex ToLexPattern = new(
        @"(?:\b|-)(?:tu|toi|ton|ta|tes)\b|\b(?:avez|etes|detenez|couvrez)-vous\b", RegexOptions.CultureInvariant, V4Text.PatternTimeout);

    // A question that asks for an opinion or a choice ("la meilleure assurance auto"), and one that leaves its subject
    // unsaid ("a-t-il le droit de me faire ça ?"): no article of an act states what is asked.
    private static readonly Regex OpinionPattern = new(
        @"\b(?:meilleure?s?|pires?|moins chere?s?|recommande\w*|conseille\w*|best|cheapest|recommend\w*)\b", RegexOptions.CultureInvariant, V4Text.PatternTimeout);

    private static readonly Regex UnsaidPattern = new(@"\b(?:ca|cela|ceci)\b", RegexOptions.CultureInvariant, V4Text.PatternTimeout);

    // A follow-up that only an earlier answer gives a meaning to: "et ...", "pourquoi", "tu es sûr", "en anglais", "ce n'est
    // pas ce que j'ai demandé", "l'article complet" or "le texte officiel" with nothing named, "me le résumer".
    private static readonly Regex ShortFollowUp = new(@"^\s*(?:et|pourquoi|why|and)\b", RegexOptions.CultureInvariant, V4Text.PatternTimeout);

    private static readonly Regex EarlierAnswer = new(
        @"^\s*(?:tu es sur|vous etes sur|t'es sur|en anglais|en francais|in english|in french|ce n'est pas|c'est faux|are you sure)\b" +
        @"|\b(?:l'article|le texte|la loi|cet article|ce texte|cette loi|le paragraphe|the article|this article|the text|this text)\b(?!\s*(?:\d|du\b|de la\b|de l'|des\b|sur\b|n[°o]|no\b|of\b|on\b))" +
        @"|\b(?:me|nous)\s+(?:le|la|les)\s+(?:resumer|expliquer|traduire|montrer|donner|redonner)\b|\b(?:resume|resumez|explique|expliquez|traduis|traduisez)-(?:le|la|les)\b",
        RegexOptions.CultureInvariant, V4Text.PatternTimeout);

    /// <summary>
    /// Whether the question is a follow-up that only an earlier answer gives a meaning to: it names no act, no subject of
    /// the dictionary and no article, and reads as "et …", "pourquoi", "tu es sûr", "en anglais", "l'article complet".
    /// </summary>
    public static bool AsksOfAnEarlierAnswer(V4QuestionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        // "Et …", "Pourquoi …" only when short: "Et si l'acheteur de la police ne règle plus rien ?" is a question.
        return row.Acts.Count == 0 && row.Concepts.Count == 0 && !CitesArticle(row) &&
            (EarlierAnswer.IsMatch(row.Folded) || (ShortFollowUp.IsMatch(row.Folded) && row.Words.Count <= 4));
    }

    /// <summary>Whether the folded question asks for an opinion or a choice between offers.</summary>
    public static bool AsksAnOpinion(string folded) => OpinionPattern.IsMatch(folded);

    /// <summary>Whether the folded question points at its subject without saying it ("ça", "cela").</summary>
    public static bool LeavesItsSubjectUnsaid(string folded) => UnsaidPattern.IsMatch(folded);

    /// <summary>Whether the folded question names an act by its kind and a date.</summary>
    public static bool NamesAnActByItsDate(string folded) => DatedActPattern.IsMatch(folded);

    /// <summary>Whether the folded question is put to Lex itself, in the second person singular.</summary>
    public static bool AddressesLex(string folded) => ToLexPattern.IsMatch(folded);

    private static readonly Regex ChangePattern = new(
        @"(?<![\p{L}])(?:historique|versions?|avant et apres|amend\w*|history|abroge\w*|abrogations?|repealed)(?![\p{L}])",
        Options, Timeout);

    // Words that say a change of anything ("modifier les conditions générales", "changer d'assureur", "la différence entre
    // l'assuré et le preneur"): they ask how the law changed only beside a word for a text, or a year (the reader's set of
    // 2026-11-19: four such questions on the content of the law were sent to the comparison of versions).
    private static readonly Regex LooseChangePattern = new(
        @"(?<![\p{L}])(?:modifi\w*|change\w*|changement\w*|evolu\w*|difference\w*|compar\w*|changed|differ\w*)(?![\p{L}])",
        Options, Timeout);

    private static readonly Regex TextOrYearPattern = new(
        @"(?<![\p{L}\d])(?:loi|lois|texte|textes|article|articles|art|reglement|reglements|code|directive|law|laws|act|regulation|legislation|(?:19|20)\d{2})(?![\p{L}\d])",
        Options, Timeout);

    // The number an article is cited by: "l'article 39", "art. 62-3", "article 1er", "article 15 bis".
    internal const string ArticleNumber = @"(?<number>1er|premier|\d{1,4}(?:-\d{1,3})?(?:\s?(?:bis|ter|quater|quinquies|sexies|septies|octies|nonies|decies))?)";

    private static readonly Regex CitedNumberPattern = new(@"(?<![\p{L}])art(?:icle|\.)?\s*" + ArticleNumber + @"(?![\p{L}\d-])", Options, Timeout);

    // Several articles ("les articles 38 et 39", "art. 12 à 15", "l'article 5, 6 ou 7"), or a part of one ("l'article 39,
    // 2e alinéa"): not one whole article to open.
    private static readonly Regex SeveralPattern = new(@"(?<![\p{L}])art(?:icle|\.)?s\s|\d\s*(?:et|a|ou|,)\s*\d", Options, Timeout);

    /// <summary>
    /// The number of the one article the question cites, in the form the tables compare ("39", "62-3", "1", "15bis"); null
    /// when it cites none, several, or one in a form code does not read (then the question call describes it).
    /// </summary>
    public static string? CitedArticle(V4QuestionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cited = CitedNumberPattern.Matches(row.Folded).Select(static match => Canonical(match.Groups["number"].Value)).Distinct(StringComparer.Ordinal).ToList();
        return cited.Count == 1 && !SeveralPattern.IsMatch(row.Folded) ? cited[0] : null;
    }

    /// <summary>An article's number as the tables compare it: "1er" and "premier" are 1, and "15 bis" is "15bis".</summary>
    internal static string Canonical(string number) =>
        number is "1er" or "premier" ? "1" : number.Replace(" ", string.Empty, StringComparison.Ordinal);

    // A date that is part of an act's name ("loi du 10 août 1991", "règlement grand-ducal du 8 octobre 2014"): not the date
    // to read the law on.
    private static readonly Regex NamesAnActPattern = new(
        @"(?:loi|reglement(?: grand-ducal| ministeriel)?|arrete(?: grand-ducal| ministeriel)?|directive|decision|ordonnance|convention|law|act|regulation)(?: modifiee| coordonnee)? (?:du|of) $",
        Options, Timeout);

    /// <summary>
    /// The dates a question asks to read the law on: the dates it writes, without those that are part of an act's name
    /// (inside a name the corpus index reads, or right after "loi du", "règlement grand-ducal du").
    /// </summary>
    public static List<V3ParsedDate> ReadingDates(V4QuestionRow row, IReadOnlyList<V3ParsedDate> dates)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(dates);
        return dates
            .Where(date => !row.Acts.Any(act => date.Start >= act.Start && date.End <= act.End))
            .Where(date => date.Start > row.Folded.Length || !NamesAnActPattern.IsMatch(row.Folded[Math.Max(0, date.Start - 48)..Math.Min(date.Start, row.Folded.Length)]))
            .ToList();
    }

    // An act named by a code's name ("Code civil", "Code du travail") or by an EU instrument's number ("règlement (UE)
    // 2022/2554", "directive 2016/97"): none of them is an act of the insurance index unless the index reads the name.
    private static readonly Regex OtherActPattern = new(
        @"(?<![\p{L}])code\s+(?:civil|penal|du\s+travail|de\s+commerce|de\s+la\s+consommation|de\s+la\s+route|des\s+assurances|[a-z]{4,})" +
        @"|(?<![\p{L}])(?:reglement|directive|regulation|decision)\s*(?:\((?:ue|ce|eu|ec|cee)\))?\s*(?:n[o°]\s*)?\d{2,4}/\d{2,4}",
        Options, Timeout);

    // A name written in capitals ("DORA", "RGPD", "CSSF"): an acronym, read against the dictionary and the index.
    private static readonly Regex AcronymPattern = new(@"(?<![\p{L}\d])\p{Lu}[\p{Lu}\d]{2,}(?![\p{L}\d])", RegexOptions.CultureInvariant | RegexOptions.Compiled, Timeout);

    /// <summary>
    /// Whether the question names an act the corpus index does not hold a name for: an acronym in capitals that is no word
    /// of the dictionary or of the index ("DORA", "RGPD"), a code, an EU instrument by its number, or an act named by its
    /// date. The tables of one domain do not answer a question that names another act: Lex's own operations do, on the act
    /// it names (2026-11-19: "Que prévoit DORA pour la notification des incidents ?" got an article of the insurance law).
    /// </summary>
    public static bool NamesAnotherAct(V4QuestionRow row, IReadOnlyList<V3ParsedDate> dates, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(dictionary);
        bool Read(int start, int end) =>
            row.Acts.Any(act => start >= act.Start && end <= act.End) ||
            row.Concepts.Concat(row.Roles).Any(read => start >= read.Start && end <= read.End);

        foreach (Match match in OtherActPattern.Matches(row.Folded))
        {
            if (!Read(match.Index, match.Index + match.Length))
            {
                return true;
            }
        }

        // An act named by its date that the index does not name.
        if (dates.Any(date => !row.Acts.Any(act => date.Start >= act.Start && date.End <= act.End) &&
            date.Start <= row.Folded.Length && NamesAnActPattern.IsMatch(row.Folded[Math.Max(0, date.Start - 48)..date.Start])))
        {
            return true;
        }

        // A question typed all in capitals holds no acronym to read.
        var letters = row.Question.Count(char.IsLetter);
        if (letters == 0 || row.Question.Count(char.IsUpper) * 2 > letters)
        {
            return false;
        }

        foreach (Match match in AcronymPattern.Matches(row.Question))
        {
            var folded = V4Text.Fold(match.Value);
            if (!Read(match.Index, match.Index + match.Length) && !dictionary.Abbreviations.Any(abbreviation => abbreviation.Short.In(V4Text.Words(folded))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the question cites an article or an identifier (route 2: Lex's reference reader).</summary>
    public static bool CitesArticle(V4QuestionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return CitationPattern.IsMatch(row.Folded);
    }

    /// <summary>Whether the question asks how the law changed over time (route 3: Lex's own operations).</summary>
    public static bool AsksChange(V4QuestionRow row, int dates)
    {
        ArgumentNullException.ThrowIfNull(row);
        return dates >= 2 || ChangePattern.IsMatch(row.Folded) || (LooseChangePattern.IsMatch(row.Folded) && TextOrYearPattern.IsMatch(row.Folded));
    }

    /// <summary>
    /// The kind of answer to look for, read from the row: the first kind the question's cues ask for that the scope can
    /// answer; else the usual kind of the first concept; else the passages. A definition is asked of a term the act defines:
    /// a cue for one on a concept the act does not define falls to the next kind.
    /// </summary>
    public static string KindOf(V4QuestionRow row, V4Dictionary dictionary, bool termDefined)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(dictionary);
        foreach (var kind in row.Kinds)
        {
            if (kind.Kind == "definition" && !termDefined && (row.Kinds.Count > 1 || row.Concepts.Count > 0))
            {
                continue;
            }

            return kind.Kind;
        }

        if (row.Kinds.Any(static kind => kind.Kind == "definition"))
        {
            return "definition";
        }

        // A term alone ("personne lésée ?", "note de couverture") asks what it is, when the act defines it.
        if (row.Kinds.Count == 0 && termDefined && row.Concepts.Count == 0 && row.Words.Count <= 5)
        {
            return "definition";
        }

        if (row.Concepts.Count == 0)
        {
            return "text";
        }

        // The usual kind of the first concept. A definition is its usual answer only when the term stands alone, with
        // nothing else of substance beside it ("prime ?"): a question of several content words and no cue for a
        // definition ("prime assurance non payée suspension couverture délai", the fresh set of 2026-11-19, answered by
        // the line that defines "prime") asks something else of the concept, and goes on to the tables and the outline.
        var alone = row.Concepts.Count == 1 && row.Roles.Count == 0 && row.Unmatched.Count == 0;
        foreach (var usual in dictionary.Concept(row.Concepts[0].Concept)!.UsualAnswer)
        {
            if (usual != "definition" || alone)
            {
                return usual;
            }
        }

        return "text";
    }

    /// <summary>The route and the activations for a row. The routes are tried the cheapest first.</summary>
    public static V4Decision Decide(V4DispatchInput input, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(dictionary);
        var row = input.Row;
        var kind = KindOf(row, dictionary, input.TermDefined);
        if (row.Greeting)
        {
            return new V4Decision("greeting", kind, [], "question_is_a_greeting");
        }

        if (input.EuActNotHeld)
        {
            return new V4Decision("not_held", kind, [new V4Activation("retrieval", false, "text_not_held")], "eu_act_named_and_not_held");
        }

        if (!input.ActHeld)
        {
            return new V4Decision("absence", kind, [new V4Activation("retrieval", false, "text_not_held")], "text_not_held");
        }

        var hasConcept = row.Concepts.Count > 0 || row.Roles.Count > 0;
        var activations = new List<V4Activation>
        {
            input.ActHasHeadings ? new("title", true, "act_has_headings") : new("title", false, "act_has_no_headings"),
            hasConcept ? new("keyword", true, "row_has_concept") : new("keyword", false, "row_has_no_concept"),
            row.Roles.Count > 0 && row.Concepts.Count > 0 ? new("cooccurrence", true, "row_has_actor") : new("cooccurrence", false, "row_has_no_actor"),
            V4Patterns.HasPattern(kind) ? new("type_pattern", true, "kind_has_pattern") : new("type_pattern", false, "kind_has_no_pattern"),
            row.Roles.Count > 0 ? new("lexicon", true, "row_has_actor") : new("lexicon", false, "row_has_no_actor"),
            kind == "list" ? new("sweep", true, "kind_is_list") : new("sweep", true, "proof_of_absence"),
            new("outline_descent", false, "act_has_no_chapter_levels"),
            row.Language == "en" ? new("embeddings", false, "not_built_question_in_english") : new("embeddings", false, "off_by_default"),
        };

        if (input.Application)
        {
            // Route 1: the reader's own case. Lex's refusal, with the law found by code beside it; no model is asked.
            activations.Add(new("arbiter", false, "own_case"));
            activations.Add(new("extraction", false, "own_case"));
            return new V4Decision("own_case", kind, activations, "application_cue_and_personal_marker");
        }

        if (kind == "definition" && input.TermDefined)
        {
            // Route 4: a term the law defines. "the expert's dictionary decides the route, not the LLM." [9ter]
            activations.Add(new("arbiter", false, "defining_line_known"));
            activations.Add(new("extraction", false, "defining_line_known"));
            return new V4Decision("definition", kind, activations, "term_defined_and_definition_asked");
        }

        var model = input.ModelAvailable;
        activations.Add(model ? new("arbiter", true, "candidates_to_rank") : new("arbiter", false, "no_model_wired"));
        activations.Add(model ? new("extraction", true, kind == "list" ? "all_candidates_together" : "one_candidate_at_a_time") : new("extraction", false, "no_model_wired"));
        return kind switch
        {
            "list" => new V4Decision("list", kind, activations, "kind_is_list"),
            "rule" => new V4Decision("rule", kind, activations, "kind_is_rule"),
            _ => new V4Decision("open", kind, activations, "kind_is_" + kind),
        };
    }
}
