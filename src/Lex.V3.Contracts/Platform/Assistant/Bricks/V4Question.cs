using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>A span <c>[Start, End)</c> of the question.</summary>
public readonly record struct V4Span(int Start, int End);

/// <summary>A concept or a role of the dictionary read in the question: the row that matched and the reader's words.</summary>
/// <param name="Source">dictionary (the lookup, code) or model (the question call chose it among the concepts it was given).</param>
public sealed record V4RowConcept(string Concept, int Keyword, int Start, int End, string Source);

/// <summary>A kind of answer the question's own words ask for, with the cue that says so.</summary>
public sealed record V4RowKind(string Kind, int Start, int End);

/// <summary>
/// The parsed question (brick C): one typed row. "Two non-LLM steps stay separate: anchor keywords (regex, deterministic,
/// fast) and the expert dictionary lookup (pandas filter, no model)." [6C] Everything on this row is read by code; a model's
/// description of the question, when one is asked, is merged into it under its own source and is checked first.
/// </summary>
public sealed class V4QuestionRow
{
    internal V4QuestionRow(
        string question,
        string folded,
        List<V4Word> words,
        string language,
        bool greeting,
        bool otherDomain,
        List<V4RowConcept> concepts,
        List<V4RowConcept> roles,
        List<V4ActMention> acts,
        List<V4RowKind> kinds,
        List<V4Span> unmatched)
    {
        Question = question;
        Folded = folded;
        Words = words;
        Language = language;
        Greeting = greeting;
        OtherDomain = otherDomain;
        Concepts = concepts;
        Roles = roles;
        Acts = acts;
        Kinds = kinds;
        Unmatched = unmatched;
    }

    /// <summary>The question as the planner was given it: masked, in normalisation form C.</summary>
    public string Question { get; }

    /// <summary>Its matching copy (step 1): same length, same offsets.</summary>
    public string Folded { get; }

    public IReadOnlyList<V4Word> Words { get; }

    /// <summary>fr or en: the language the answer is written in.</summary>
    public string Language { get; }

    /// <summary>The question is a greeting or a thank-you and asks nothing of the law (route 0).</summary>
    public bool Greeting { get; }

    /// <summary>The question holds a word of another domain that shares this one's vocabulary (social insurance).</summary>
    public bool OtherDomain { get; }

    /// <summary>The concepts read in the question, in its order. A role is not here.</summary>
    public IReadOnlyList<V4RowConcept> Concepts { get; }

    /// <summary>The roles of the law read in the question (assureur, preneur d'assurance, ...), in its order.</summary>
    public IReadOnlyList<V4RowConcept> Roles { get; }

    /// <summary>The acts the question names by a short name of the corpus index.</summary>
    public IReadOnlyList<V4ActMention> Acts { get; }

    /// <summary>The kinds of answer the question's words ask for, the narrowest first; empty when no cue was read.</summary>
    public IReadOnlyList<V4RowKind> Kinds { get; }

    /// <summary>The words that carry meaning and that no table read: kept, reported, never used to block.</summary>
    public IReadOnlyList<V4Span> Unmatched { get; }

    /// <summary>The row with a concept more: the one the question call chose among the dictionary's, for words the lookup did not read.</summary>
    public V4QuestionRow With(V4RowConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return new V4QuestionRow(Question, Folded, [.. Words], Language, Greeting, OtherDomain, [.. Concepts, concept], [.. Roles], [.. Acts], [.. Kinds], [.. Unmatched]);
    }

    /// <summary>Whether the row holds what retrieval needs without a model: a concept, a role with a definition cue, or an act.</summary>
    public bool Searchable => Concepts.Count > 0 || Roles.Count > 0 || Acts.Count > 0;
}

/// <summary>
/// Question parsing by code (brick C, steps 1 to 5): the matching copy, the dictionary lookup, the acts named, the cues for
/// the kind of answer, and the words nothing read. "The function does NOT call an LLM." [B1]
/// </summary>
public static class V4QuestionParser
{
    private static readonly HashSet<string> GreetingWords = new(StringComparer.Ordinal)
    {
        "bonjour", "bonsoir", "salut", "coucou", "hello", "hi", "hey", "merci", "thanks", "thank", "you", "beaucoup", "bien", "ok",
        "super", "parfait", "bye", "revoir", "au", "a", "bientot", "good", "morning", "evening", "moien", "cava", "ca", "va",
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // French.
        "avec", "aussi", "alors", "apres", "avant", "avoir", "cela", "celle", "celui", "cette", "comme", "comment", "dans", "depuis",
        "donc", "dont", "elle", "elles", "encore", "entre", "est", "etre", "fait", "faire", "faut", "leur", "leurs", "lors", "lorsque",
        "mais", "meme", "moins", "nous", "notre", "pour", "pourquoi", "plus", "quand", "quel", "quelle", "quels", "quelles", "quoi",
        "sans", "selon", "sont", "sous", "tout", "toute", "tous", "toutes", "tres", "vous", "votre", "peut", "peuvent", "doit",
        "doivent", "ceci", "ceux", "chez", "contre", "vers", "sera", "etait", "ete", "avait", "ainsi", "cas", "loi", "lois",
        "article", "articles", "texte", "droit", "dire", "dit", "savoir", "existe", "possible", "contrat", "contrats", "assurance",
        "assurances", "facon", "maniere", "quelqu", "chose", "juste", "vraiment", "luxembourg", "luxembourgeois", "luxembourgeoise",
        // English.
        "about", "after", "also", "been", "before", "between", "could", "does", "from", "have", "into", "must", "should", "than",
        "that", "their", "them", "then", "there", "these", "this", "those", "under", "what", "when", "where", "which", "while",
        "will", "with", "would", "your", "contract", "insurance", "says", "said", "tell", "show", "many", "much", "long", "according",
    };

    /// <summary>The row of a question, by code alone.</summary>
    public static V4QuestionRow Parse(string maskedQuestion, V3AnswerLocale locale, V4Dictionary dictionary, V4CorpusIndex index)
    {
        ArgumentNullException.ThrowIfNull(maskedQuestion);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(index);
        var folded = V4Text.Fold(maskedQuestion);
        var words = V4Text.Words(folded);
        var taken = new bool[words.Count];

        var acts = index.Mentions(words);
        foreach (var act in acts)
        {
            Take(words, taken, act.Start, act.End);
        }

        var concepts = new List<V4RowConcept>();
        var roles = new List<V4RowConcept>();
        foreach (var hit in dictionary.Lookup(words))
        {
            // An act's name holds words of the dictionary ("loi sur le contrat d'assurance"): the name was read first.
            if (Enumerable.Range(hit.FirstWord, hit.WordCount).Any(position => taken[position]))
            {
                continue;
            }

            var concept = dictionary.Concept(hit.Keyword.Concept)!;
            var row = new V4RowConcept(concept.Id, hit.Keyword.Row, hit.Start, hit.End, "dictionary");
            (concept.Kind == "role" ? roles : concepts).Add(row);
            Take(words, taken, hit.Start, hit.End);
        }

        // A concept read twice is one concept; its first words are kept.
        concepts = concepts.GroupBy(static concept => concept.Concept).Select(static group => group.First()).ToList();
        roles = roles.GroupBy(static role => role.Concept).Select(static group => group.First()).ToList();

        var kinds = new List<V4RowKind>();
        foreach (var kind in V4Dictionary.KindOrder)
        {
            var type = dictionary.AnswerType(kind);
            if (type is null)
            {
                continue;
            }

            foreach (var cue in type.Cues)
            {
                var at = cue.Occurrences(words).Cast<(int First, int Count)?>().FirstOrDefault();
                if (at is { } found)
                {
                    kinds.Add(new V4RowKind(kind, words[found.First].Start, words[found.First + found.Count - 1].End));
                    Take(words, taken, words[found.First].Start, words[found.First + found.Count - 1].End);
                    break;
                }
            }
        }

        var unmatched = new List<V4Span>();
        for (var at = 0; at < words.Count; at++)
        {
            var word = words[at];
            if (!taken[at] && word.Text.Length >= 5 && !StopWords.Contains(word.Text) && !word.Text.Any(char.IsAsciiDigit) && !word.Text.Contains('•', StringComparison.Ordinal))
            {
                unmatched.Add(new V4Span(word.Start, word.End));
            }
        }

        var greeting = words.Count is > 0 and <= 6 && words.All(static word => GreetingWords.Contains(word.Text));
        return new V4QuestionRow(
            maskedQuestion,
            folded,
            words,
            locale == V3AnswerLocale.Fr ? "fr" : "en",
            greeting,
            index.OtherDomain(words),
            concepts,
            roles,
            acts,
            kinds,
            unmatched);
    }

    private static void Take(List<V4Word> words, bool[] taken, int start, int end)
    {
        for (var at = 0; at < words.Count; at++)
        {
            if (words[at].Start >= start && words[at].End <= end)
            {
                taken[at] = true;
            }
        }
    }
}

/// <summary>
/// Where the advice boundary sits for a question on a rule (the owner's decision of 2026-11-18, decision 4(i) of the V4
/// plan; it narrows R15's cues, which read a function and never a person). A question whose subject is a role of the law
/// (assureur, preneur d'assurance) with no marker of the reader's own case is a question on the rule, and is answered by
/// quoting the rule. With such a marker, with a number that states a fact, or with a cue that predicts or computes, it stays
/// refused. Code decides; no model is asked.
/// </summary>
public static class V4Boundary
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex Marker = new(
        @"(?<![\p{L}\d])(?:je|moi|mon|ma|mes|mien|mienne|miens|miennes|nous|notre|nos|tu|toi|ton|ta|tes|me|te)(?![\p{L}\d])" +
        @"|(?<![\p{L}\d])(?:j|m|t)['’](?=\p{L})" +
        @"|(?<![\p{L}\d])(?:(?-i:I)|my|mine|myself|we|us|our|ours)(?![\p{L}\d'’])" +
        @"|\bdans (?:mon|ma|son|sa|leur|notre|votre|ce|cette) (?:cas|situation)\b|\bin (?:my|his|her|their|our|your|this) (?:case|situation)\b",
        Options, Timeout);

    // "pouvez-vous me dire", "explique-moi", "tell me": the reader speaks to Lex, and says nothing of a case of their own.
    private static readonly Regex AddressedToLex = new(
        @"(?<![\p{L}\d])(?:me|m['’]|nous)\s*(?:dire|montrer|donner|expliquer|indiquer|citer|preciser|préciser|renseigner|rappeler)(?![\p{L}])" +
        @"|(?<![\p{L}\d])(?:dis|dites|montre|montrez|explique|expliquez|indique|indiquez|donne|donnez|cite|citez)-(?:moi|nous)(?![\p{L}])" +
        @"|(?<![\p{L}\d])(?:tell|show|give|remind)\s+(?:me|us)(?![\p{L}])|(?<![\p{L}\d])explain\s+to\s+(?:me|us)(?![\p{L}])",
        Options, Timeout);

    private static readonly Regex Number = new(@"\d+", Options, Timeout);
    private static readonly Regex Citation = new(@"(?:art(?:icle)?s?\.?|loi|règlement|reglement|directive|code|§|paragraphe|point|alinéa|alinea|n°|no)\s*(?:du\s+|de\s+la\s+)?[\d\s./\-,et()UECE]*$", Options, Timeout);

    /// <summary>The markers of the reader's own case in <c>text[start, end)</c>, in order.</summary>
    public static List<V4Span> PersonalMarkers(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        var part = text[start..end];
        var addressed = AddressedToLex.Matches(part).Select(static match => (match.Index, End: match.Index + match.Length)).ToArray();
        var markers = new List<V4Span>();
        foreach (Match match in Marker.Matches(part))
        {
            if (!addressed.Any(span => match.Index >= span.Index && match.Index + match.Length <= span.End))
            {
                markers.Add(new V4Span(start + match.Index, start + match.Index + match.Length));
            }
        }

        return markers;
    }

    /// <summary>
    /// Whether a number in the part states a fact of a situation ("après 2 sinistres", "3 000 euros"): any number that is
    /// neither inside a date the parser read nor part of a citation ("article 39", "loi du 27 juillet 1997").
    /// </summary>
    public static bool StatesFactNumber(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        var dates = V3AssistantParsers.DatesIn(text);
        foreach (Match match in Number.Matches(text[start..end]))
        {
            var at = start + match.Index;
            if (dates.Any(date => at >= date.Start && at < date.End))
            {
                continue;
            }

            var before = text[Math.Max(0, at - 40)..at];
            if (Citation.IsMatch(before))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a part that holds cues of R15 is nevertheless a question on the rule: a role of the law is named before the
    /// first cue, every cue is of the two functions a rule can be asked in ("peut-il", "doit-il"), and nothing marks a case of
    /// the reader's own.
    /// </summary>
    public static bool RuleQuestion(string text, int start, int end, IReadOnlyList<V3ApplicationCue> cues)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(cues);
        if (cues.Count == 0 || start < 0 || end > text.Length || start >= end)
        {
            return false;
        }

        if (cues.Any(static cue => cue.Function is not (V3ApplicationFunction.ConcludesOnSituation or V3ApplicationFunction.ChoosesAmongOptions)))
        {
            return false;
        }

        if (PersonalMarkers(text, start, end).Count > 0 || StatesFactNumber(text, start, end))
        {
            return false;
        }

        var folded = V4Text.Fold(text[start..end]);
        var words = V4Text.Words(folded);
        var dictionary = V4Dictionary.Insurance;
        var first = cues.OrderBy(static cue => cue.Start).First();
        var cueStart = first.Start - start;
        var cueEnd = first.End - start;

        // The role is the subject of the cue's verb: named before it ("L'assureur peut-il ..."), or right after an inverted
        // one ("Que doit faire le preneur d'assurance ...").
        return dictionary.Lookup(words).Any(hit => dictionary.Concept(hit.Keyword.Concept)!.Kind == "role" && (hit.Start < cueStart || (hit.Start >= cueEnd && hit.Start - cueEnd <= 16)));
    }
}
