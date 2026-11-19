namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>A word of a folded text: its characters and where they are, <c>[Start, End)</c>, in the text it was read from.</summary>
public readonly record struct V4Word(string Text, int Start, int End);

/// <summary>
/// The matching copy of a text (brick C, step 1; brick B's "fold both for matching only"). The fold is one character for one
/// character, so an offset in the folded copy is the same offset in the text as published: the law's text is never changed,
/// only the copy that is compared. No model is consulted: "The function does NOT call an LLM." [B1]
/// </summary>
public static class V4Text
{
    /// <summary>
    /// The longest one match of a pattern may run. It bounds a pattern that would backtrack without end on a hostile
    /// question; it is not a budget for an ordinary match, which takes microseconds. It was 250 ms, and a match that a
    /// loaded machine or a collection pause held for that long failed the whole answer (seen once in the tests on
    /// 2026-11-19, the machine running three other suites): two seconds keeps the bound and leaves a stall room.
    /// </summary>
    public static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    private static readonly char[] Table = BuildTable();

    /// <summary>
    /// The folded copy: lower case, accents taken off, both apostrophes one apostrophe, every space one space, every dash one
    /// hyphen. Each UTF-16 unit gives exactly one unit.
    /// </summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var at = 0; at < source.Length; at++)
            {
                var character = source[at];
                span[at] = character < Table.Length ? Table[character] : FoldRare(character);
            }
        });
    }

    private static char FoldRare(char character) => character switch
    {
        '\u2018' or '\u2019' or '\u02BC' or '\u2032' => '\'',
        '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' => '-',
        '\u2009' or '\u200A' or '\u202F' or '\u2002' or '\u2003' or '\u3000' => ' ',
        '\u201C' or '\u201D' or '\u201E' => '"',
        _ => char.IsWhiteSpace(character) ? ' ' : char.ToLowerInvariant(character),
    };

    // The base letter of every letter of U+00C0 to U+017F (Latin-1 Supplement and Latin Extended-A), in code point order: "é"
    // gives "e", "ç" gives "c", "œ" gives "o". Written out, and not asked of string.Normalize: the API runs with
    // InvariantGlobalization, where a normalisation decomposes nothing, and a fold that worked on a developer's machine
    // and not on the server would find no line of the law.
    private const string LatinBases =
        "aaaaaaaceeeeiiiidnooooo\u00D7ouuuuytsaaaaaaaceeeeiiiidnooooo\u00F7ouuuuyty"
        + "aaaaaaccccccccddddeeeeeeeeeegggggggghhhhiiiiiiiiiiiijjkkklllllll"
        + "lllnnnnnnnnnoooooooorrrrrrssssssssttttttuuuuuuuuuuuuwwyyyzzzzzzs";

    private static char[] BuildTable()
    {
        var table = new char[0x250];
        for (var code = 0; code < table.Length; code++)
        {
            var character = (char)code;
            table[code] = character is '`' or '\u00B4' ? '\''
                : char.IsWhiteSpace(character) || character == '\u00A0' ? ' '
                : code is >= 0xC0 and < 0x180 ? LatinBases[code - 0xC0]
                : char.ToLowerInvariant(character);
        }

        return table;
    }

    /// <summary>The words of a folded text, in order: each a run of letters and digits. An apostrophe and a hyphen end a word.</summary>
    public static List<V4Word> Words(string folded)
    {
        ArgumentNullException.ThrowIfNull(folded);
        var words = new List<V4Word>();
        var at = 0;
        while (at < folded.Length)
        {
            while (at < folded.Length && !char.IsLetterOrDigit(folded[at]))
            {
                at++;
            }

            var start = at;
            while (at < folded.Length && char.IsLetterOrDigit(folded[at]))
            {
                at++;
            }

            if (at > start)
            {
                words.Add(new V4Word(folded[start..at], start, at));
            }
        }

        return words;
    }

    /// <summary>
    /// The stem two forms of one French word share (rule 3 of the dictionary, ours): from four letters, one final "s" or "x"
    /// taken off, then every final "e". So "lésée", "lésé", "lésés" and "lésées" meet, as "contrat" and "contrats" do. A
    /// shorter word is its own stem.
    /// </summary>
    public static string Stem(string foldedWord)
    {
        ArgumentNullException.ThrowIfNull(foldedWord);
        if (foldedWord.Length < 4)
        {
            return foldedWord;
        }

        var end = foldedWord.Length;
        if (foldedWord[end - 1] is 's' or 'x')
        {
            end--;
        }

        while (end > 3 && foldedWord[end - 1] == 'e')
        {
            end--;
        }

        return end == foldedWord.Length ? foldedWord : foldedWord[..end];
    }
}

/// <summary>
/// A key of the dictionary as it is matched (rule 2: "A phrase stays whole"): its words in order, each matched as itself, by
/// its stem, or as a prefix when the key writes it with a final star ("résili*").
/// </summary>
public sealed class V4Key
{
    private readonly (string Text, bool Prefix, string Stem)[] _words;

    private V4Key(string source, (string, bool, string)[] words)
    {
        Source = source;
        _words = words;
    }

    /// <summary>The key as the dictionary writes it.</summary>
    public string Source { get; }

    /// <summary>How many words the key holds: a longer key is tried first.</summary>
    public int Length => _words.Length;

    /// <summary>Reads a key: folded, cut into words, a star after a word making it a prefix. Null when it holds no word.</summary>
    public static V4Key? Read(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        var folded = V4Text.Fold(keyword);
        var words = new List<(string, bool, string)>();
        foreach (var word in V4Text.Words(folded))
        {
            var prefix = word.End < folded.Length && folded[word.End] == '*';
            words.Add((word.Text, prefix, V4Text.Stem(word.Text)));
        }

        return words.Count == 0 ? null : new V4Key(keyword, [.. words]);
    }

    private bool Meets(int position, string word) =>
        _words[position].Prefix
            ? word.StartsWith(_words[position].Text, StringComparison.Ordinal)
            : string.Equals(word, _words[position].Text, StringComparison.Ordinal) ||
              (word.Length >= 4 && _words[position].Text.Length >= 4 && string.Equals(V4Text.Stem(word), _words[position].Stem, StringComparison.Ordinal));

    // The pronoun a French question puts after its verb ("prend-elle effet", "peut-il résilier", "a-t-il le droit"): a key
    // written in the law's order is still read across it.
    private static readonly HashSet<string> Inverted = new(StringComparer.Ordinal) { "il", "elle", "ils", "elles", "on", "t" };

    /// <summary>
    /// Where the key is in a text's words, in order: the index of the first word of each occurrence and how many words of
    /// the text it covers (more than the key holds when a question's inverted pronoun stands inside it).
    /// </summary>
    public IEnumerable<(int First, int Count)> Occurrences(IReadOnlyList<V4Word> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        for (var at = 0; at + _words.Length <= words.Count; at++)
        {
            if (!Meets(0, words[at].Text))
            {
                continue;
            }

            var next = at + 1;
            var all = true;
            for (var position = 1; position < _words.Length && all; position++)
            {
                var skipped = 0;
                while (next < words.Count && skipped < 2 && Inverted.Contains(words[next].Text) && !Meets(position, words[next].Text))
                {
                    next++;
                    skipped++;
                }

                all = next < words.Count && Meets(position, words[next].Text);
                next++;
            }

            if (all)
            {
                yield return (at, next - at);
            }
        }
    }

    /// <summary>Whether the key is anywhere in a text's words.</summary>
    public bool In(IReadOnlyList<V4Word> words) => Occurrences(words).Any();

    /// <summary>Whether this folded word is one of the key's own words (as itself, by its stem, or by the key's prefix).</summary>
    public bool Writes(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        for (var position = 0; position < _words.Length; position++)
        {
            if (Meets(position, word))
            {
                return true;
            }
        }

        return false;
    }
}
