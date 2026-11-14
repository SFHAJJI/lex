using System.Text;

namespace Lex.V3.Contracts.Index;

/// <summary>
/// How a search matches words and ranks articles (2026-11-13: "de" matched inside "décide", and the hits came in the index's
/// order). The text a search matches is the article's searchable text with its glued words set apart (<see cref="Segment"/>:
/// the publisher's files often run a heading into its article, "Article 44Délai de prescription1.Le délai"), folded as
/// <see cref="TextFolding"/> folds it. A query's words are its letters and digits between any other characters; the words of
/// <see cref="Stopwords"/> are dropped (French, German and English articles, prepositions and the like, and the word
/// "article"); a word matches only a whole word of the text, its plural or its singular (a trailing s or x, from four
/// letters). An article's heading is its words after its number up to its text (<see cref="HeadingOf"/>). Articles are ranked
/// by the rarity of the query's words their heading holds, then by the rarity of those their text holds, rarity being the
/// inverse of how many articles of the searched population hold the word; ties keep the reading's order.
/// </summary>
public static class SearchWords
{
    /// <summary>The rule, as an answer says it.</summary>
    public const string Rule =
        "words are the query's letters and digits between any other characters, folded as the text is, except that a whitespace-" +
        "separated token holding a digit (a number, a date, an article number such as 121-6 or 1er) is kept whole, never dropped; " +
        "French, German and English stopwords and the word article are dropped; a word matches a whole word of the article's text, or its plural or singular " +
        "(a trailing s or x, from four letters), never a part of a word; the text's glued words are set apart first (a lower-case " +
        "letter before a capital, a letter beside a digit, a stop, colon or semicolon before a letter); an article's heading is the " +
        "short run after its number that opens with a capital and ends where its text is glued on (none when a sentence or a " +
        "paragraph's number comes first); articles are ranked by the rarity of the query's words in their heading, then in " +
        "their text (rarity: the inverse of how many searched articles hold the word), ties in the reading's order";

    /// <summary>
    /// The most words a query may hold once its punctuation has split its terms (the review of #1045): a search tracks each
    /// word of a query in one bit of a 64-bit mask, and every word must be matched, so a longer query is refused, never cut.
    /// </summary>
    public const int MaxWords = 63;

    /// <summary>The words a query drops: articles, prepositions, conjunctions, pronouns and auxiliaries, and "article".</summary>
    public static readonly IReadOnlySet<string> Stopwords = new HashSet<string>(StringComparer.Ordinal)
    {
        // French
        "a", "au", "aux", "avec", "ce", "ces", "cet", "cette", "d", "dans", "de", "des", "du", "elle", "elles", "en", "est", "et",
        "il", "ils", "j", "je", "l", "la", "le", "les", "leur", "leurs", "lui", "m", "ma", "mais", "me", "mes", "mon", "n", "ne",
        "ni", "nous", "on", "ou", "par", "pas", "pour", "qu", "que", "quel", "quelle", "quelles", "quels", "qui", "quoi", "s", "sa",
        "se", "ses", "si", "son", "sont", "sur", "t", "ta", "te", "tes", "ton", "tu", "un", "une", "vos", "votre", "vous", "y",
        "dit", "dire", "peut", "doit",
        // English
        "about", "an", "and", "any", "are", "as", "at", "be", "been", "by", "can", "do", "does", "for", "from", "has", "have", "how",
        "i", "if", "in", "into", "is", "it", "its", "not", "such", "than", "these", "those",
        "may", "must", "my", "of", "on", "or", "say", "says", "should", "that", "the", "their", "there", "this", "to", "under",
        "was", "what", "when", "where", "which", "who", "will", "with", "within",
        // German
        "aus", "bei", "dem", "den", "der", "die", "das", "ein", "eine", "einem", "einen", "einer", "eines", "es", "fur", "im",
        "ist", "mit", "nach", "oder", "sind", "uber", "um", "und", "von", "was", "wie", "zu", "zum", "zur",
        // The structure's own word
        "article", "articles", "art", "artikel",
    };

    /// <summary>
    /// The text with its glued words set apart by one space: between a lower-case letter and a capital, between a letter and a
    /// digit either way, and after a stop, colon or semicolon followed by a letter. Nothing else changes.
    /// </summary>
    public static string Segment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length + (text.Length / 16));
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (index > 0)
            {
                var before = text[index - 1];
                var glued = (char.IsLower(before) && char.IsUpper(current))
                    || (char.IsLetter(before) && char.IsDigit(current))
                    || (char.IsDigit(before) && char.IsLetter(current))
                    || (before is '.' or ':' or ';' && char.IsLetter(current) && index > 1 && !char.IsUpper(text[index - 2]));
                if (glued)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    /// <summary>The most characters a heading holds: a longer run is the article's text, not its heading.</summary>
    public const int HeadingMaxCharacters = 150;

    /// <summary>
    /// The article's heading in its own text: after its number ("Article 44", "Art. 74.", the publisher's spelling of it), a run
    /// that opens with a capital and ends where the article's text is glued to it (a capital right after a lower-case letter,
    /// "prescription1.Le"'s numbered paragraph, "systems 1. Providers"), within <see cref="HeadingMaxCharacters"/>. Empty when the
    /// text opens with no article number, when what follows it is no heading (a paragraph's number "(1)", a lower-case word), or
    /// when a sentence ends before any such end: then it is the article's text ("Art. 3.Le présent règlement s'applique.").
    /// </summary>
    public static string HeadingOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = NumberEnd(text);
        if (start < 0 || start >= text.Length || !char.IsUpper(text[start]))
        {
            return string.Empty;
        }

        var end = Math.Min(text.Length, start + HeadingMaxCharacters + 1);
        for (var index = start + 1; index < end; index++)
        {
            var before = text[index - 1];
            var current = text[index];
            // A capital glued to a lower-case letter: the publisher's text starting right after the heading.
            if (char.IsLower(before) && char.IsUpper(current))
            {
                return text[start..index].Trim();
            }

            // A numbered paragraph ("prescription1.Le", "systems 1. Providers").
            if (char.IsDigit(current) && index + 1 < text.Length && text[index + 1] == '.' && !char.IsDigit(before))
            {
                return text[start..index].Trim();
            }

            // A sentence's end, spaced or glued ("avocats. Il", "avocats.Il"): the run is the article's text.
            if (current == '.' && char.IsLower(before) && index + 1 < text.Length && (char.IsWhiteSpace(text[index + 1]) || char.IsUpper(text[index + 1])))
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    // The end of the article number a text opens with ("Article 44", "Art. 74.", "Article 62-11", "Artcile 57"), or -1.
    private static int NumberEnd(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index + 3 > text.Length || !text.AsSpan(index, 3).Equals("Art", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        while (index < text.Length && char.IsLetter(text[index]))
        {
            index++;
        }

        while (index < text.Length && (text[index] == '.' || char.IsWhiteSpace(text[index])))
        {
            index++;
        }

        var digits = index;
        while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '-' || (index > digits && char.IsLetter(text[index]) && index - digits <= 6 && char.IsLower(text[index]))))
        {
            // "1er", "62-11", "5bis": the number and its suffix, a capital starts the heading.
            index++;
        }

        if (index == digits)
        {
            return -1;
        }

        while (index < text.Length && (text[index] == '.' || char.IsWhiteSpace(text[index])))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// The query's words, folded, without stopwords, each once, in their order; every word when all are stopwords. A
    /// whitespace-separated token holding a digit (a number, a date, an article number: "121-6", "1er", "2016/679") is
    /// protected (R90): kept whole, folded and its glued words set apart, without the punctuation around it, and matched on word
    /// bounds as a phrase.
    /// </summary>
    public static IReadOnlyList<string> Terms(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = new List<string>();
        var content = new List<string>();
        foreach (var token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var folded = TextFolding.Fold(Segment(token));
            var words = WordsOf(folded);
            if (words.Count == 0)
            {
                continue;
            }

            if (token.Any(char.IsDigit))
            {
                var first = folded.IndexOf(words[0], StringComparison.Ordinal);
                var last = folded.LastIndexOf(words[^1], StringComparison.Ordinal) + words[^1].Length;
                var whole = folded[first..last];
                all.Add(whole);
                content.Add(whole);
                continue;
            }

            all.AddRange(words);
            content.AddRange(words.Where(static word => !Stopwords.Contains(word)));
        }

        return (content.Count > 0 ? content : all).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>A folded text's words: its runs of letters and digits.</summary>
    public static IReadOnlyList<string> WordsOf(string folded)
    {
        ArgumentNullException.ThrowIfNull(folded);
        var words = new List<string>();
        var start = -1;
        for (var index = 0; index <= folded.Length; index++)
        {
            var word = index < folded.Length && char.IsLetterOrDigit(folded[index]);
            if (word && start < 0)
            {
                start = index;
            }
            else if (!word && start >= 0)
            {
                words.Add(folded[start..index]);
                start = -1;
            }
        }

        return words;
    }

    /// <summary>The forms a folded word matches, in UTF-8: itself, and from four letters its plural or singular.</summary>
    public static byte[][] Forms(string word) => FormsOf(word).Select(Encoding.UTF8.GetBytes).ToArray();

    /// <summary>The forms a folded word matches: itself, and from four letters its plural or singular.</summary>
    public static IReadOnlyList<string> FormsOf(string word)
    {
        ArgumentException.ThrowIfNullOrEmpty(word);
        var forms = new List<string> { word };
        if (word.Length >= 4 && word.All(char.IsLetter))
        {
            forms.Add(word[^1] is 's' or 'x' ? word[..^1] : word + "s");
        }

        return forms.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Whether folded UTF-8 text holds one of the forms as a whole word (a letter or digit on neither side).</summary>
    public static bool HoldsWord(ReadOnlySpan<byte> text, byte[][] forms)
    {
        foreach (var form in forms)
        {
            if (HoldsBounded(text, form))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether folded UTF-8 text holds the folded phrase starting and ending on word bounds.</summary>
    public static bool HoldsBounded(ReadOnlySpan<byte> text, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
        {
            return false;
        }

        var from = 0;
        while (from <= text.Length - needle.Length)
        {
            var at = text[from..].IndexOf(needle);
            if (at < 0)
            {
                return false;
            }

            var start = from + at;
            var end = start + needle.Length;
            var openBound = start == 0 || !IsWordByte(text[start - 1]) || !IsWordByte(needle[0]);
            var closeBound = end == text.Length || !IsWordByte(text[end]) || !IsWordByte(needle[^1]);
            if (openBound && closeBound)
            {
                return true;
            }

            from = start + 1;
        }

        return false;
    }

    // A byte of a word: an ASCII letter or digit, or a byte of a character outside ASCII (folded text keeps few, and they are
    // letters: a word does not end inside one).
    private static bool IsWordByte(byte value) =>
        (value >= (byte)'a' && value <= (byte)'z') || (value >= (byte)'A' && value <= (byte)'Z') || (value >= (byte)'0' && value <= (byte)'9') || value >= 0x80;

    /// <summary>The mask of a query's first <paramref name="count"/> words: an article of the word lane holds every one.</summary>
    public static long EveryWord(int count) => count >= MaxWords ? long.MaxValue : (1L << count) - 1;

    /// <summary>
    /// What a heading holds of a query's words (<paramref name="mask"/>, one bit a word): how many, and whether the rarest of
    /// the query's words (by <paramref name="rarity"/>, the first of equals) is one of them.
    /// </summary>
    public static (int Words, bool Rarest) HeadingHolds(long mask, IReadOnlyList<double> rarity)
    {
        ArgumentNullException.ThrowIfNull(rarity);
        var words = 0;
        for (var word = 0; word < rarity.Count; word++)
        {
            if ((mask & (1L << word)) != 0)
            {
                words++;
            }
        }

        var rarest = -1;
        for (var word = 0; word < rarity.Count; word++)
        {
            if (rarest < 0 || rarity[word] > rarity[rarest])
            {
                rarest = word;
            }
        }

        return (words, rarest >= 0 && (mask & (1L << rarest)) != 0);
    }

    /// <summary>A word's rarity among <paramref name="population"/> articles of which <paramref name="holding"/> hold it.</summary>
    public static double Rarity(int population, int holding) => Math.Log((population + 1.0) / (holding + 1.0)) + 1.0;
}

/// <summary>
/// An article's rank for a query (<see cref="SearchWords.Rule"/>): the rarity its heading holds, then its text; and, for the
/// best-hit decision, how many query words its heading holds and whether the query's rarest word is one of them.
/// </summary>
public readonly record struct SearchRank(double Heading, double Text, bool PhraseInHeading, int HeadingWords = 0, bool HeadingHoldsRarest = false)
{
    /// <summary>Greater first: the heading's rarity, then the text's.</summary>
    public static int Compare(SearchRank left, SearchRank right)
    {
        var compared = right.Heading.CompareTo(left.Heading);
        return compared != 0 ? compared : right.Text.CompareTo(left.Text);
    }
}
