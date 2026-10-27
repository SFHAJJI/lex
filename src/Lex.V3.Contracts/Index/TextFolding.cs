using System.Globalization;
using System.Text;

namespace Lex.V3.Contracts.Index;

/// <summary>
/// Lex's one folding of searched text (the plan's item 4.4): what full-text search folds a query and every article's
/// searchable text with before it matches one against the other, so that capitals and accents never decide whether a passage
/// is found. Each letter or digit is folded by the title folding's table (<see cref="TitleFolding"/>: its canonical
/// decomposition, Unicode NFD, without its non-spacing marks, and lowered by the invariant case map, letter by letter, so the
/// fold is the same with the operating system's language library and without it, as the release image runs); a non-spacing
/// (combining) mark on its own is dropped; the ligatures œ and æ, of either case, are written oe and ae; the typographic
/// apostrophes (U+2019, U+2018 and the modifier letter U+02BC) are written as the straight one, and the no-break spaces
/// (U+00A0, U+202F, U+2007) as an ordinary space; a soft hyphen (U+00AD) is dropped, as a combining mark is (the driver's
/// decision on the review of #1005: on derive-r1 "l'article" is in 1,517 texts and "l’article" in 9,246, and the soft hyphen
/// appears 441 times, 438 of them inside a word); and every other character is kept as it is. Unlike the title folding
/// nothing separates words: punctuation, the hyphens and the other spaces stay as they are, so a phrase is found only where
/// its every other character is.
/// </summary>
/// <remarks>
/// <para>
/// A Hangul syllable keeps its composed form, as the title folding's table keeps it; its decomposition is its jamo, among
/// which no accent is. Nothing Lex holds is written in Hangul.
/// </para>
/// The fold is per UTF-16 unit: each unit folds to nothing, to itself, or to one or more units, so each position of the folded
/// text names the one unit of the original it comes from (<see cref="FoldedText"/>), and a match found in the folded text is
/// marked on the original's own characters, never on the fold. A surrogate folds to itself. A folded needle that holds nothing
/// but whitespace, or a lone surrogate, matches nothing (<see cref="CanMatch"/>): the text a search reads is UTF-8 read from the
/// index, which holds no lone surrogate, and a needle that matched one there would match one thing in the UTF-8 bytes and
/// another in the text.
/// </remarks>
public static class TextFolding
{
    /// <summary>The folding, in the words every answer that matches with it says it.</summary>
    public const string Rule =
        "the query and each article's searchable text are folded alike before they are matched: each letter or digit to its canonical " +
        "decomposition (Unicode NFD) without its combining marks and in lower case (the invariant case map), by Lex's own table, the same " +
        "with or without the operating system's language library; a combining mark on its own is dropped; the ligatures œ and æ, " +
        "of either case, are written oe and ae; the apostrophes ’, ‘ and ʼ are written ' and the no-break spaces U+00A0, U+202F and " +
        "U+2007 an ordinary space; a soft hyphen (U+00AD) is dropped; every other character, punctuation, hyphens and the other spaces " +
        "included, is kept as it is, so capitals, accents, these apostrophes and these spaces never decide a match and every other " +
        "character does";

    /// <summary>Each UTF-16 unit's fold: null when it is itself, empty when it is dropped (a non-spacing mark, a soft hyphen).</summary>
    private static readonly string?[] Units = BuildUnits();

    /// <summary><paramref name="value"/> folded (<see cref="Rule"/>); the same instance when nothing in it folds.</summary>
    public static string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var index = 0;
        while (index < value.Length && Units[value[index]] is null)
        {
            index++;
        }

        if (index == value.Length)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        builder.Append(value, 0, index);
        for (; index < value.Length; index++)
        {
            if (Units[value[index]] is { } folded)
            {
                builder.Append(folded);
            }
            else
            {
                builder.Append(value[index]);
            }
        }

        return builder.ToString();
    }

    /// <summary><paramref name="value"/> folded, with the unit of the original each folded unit comes from.</summary>
    public static FoldedText Folded(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var folded = Fold(value);
        if (ReferenceEquals(folded, value))
        {
            return new FoldedText(value, value, origins: null);
        }

        var origins = new int[folded.Length];
        var at = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var units = Units[value[index]]?.Length ?? 1;
            for (var unit = 0; unit < units; unit++)
            {
                origins[at++] = index;
            }
        }

        return new FoldedText(value, folded, origins);
    }

    /// <summary>
    /// Whether a folded needle can match a text read from an index: it holds a unit other than whitespace, and no lone
    /// surrogate. A needle that folds to whitespace alone, or to nothing, holds nothing to match.
    /// </summary>
    public static bool CanMatch(string foldedNeedle) => HoldsSomething(foldedNeedle) && !HasLoneSurrogate(foldedNeedle);

    /// <summary>Whether a folded needle holds a unit other than whitespace: one that folded to nothing else holds nothing to match.</summary>
    public static bool HoldsSomething(string foldedNeedle)
    {
        ArgumentNullException.ThrowIfNull(foldedNeedle);
        foreach (var character in foldedNeedle)
        {
            if (!char.IsWhiteSpace(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(character))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a unit is a combining mark the fold drops, so a match that ends before it takes it along: it belongs to the letter
    /// before it. A soft hyphen is dropped too, but belongs to no letter, and a match never takes it along.
    /// </summary>
    internal static bool IsDropped(char character) => Units[character] is { Length: 0 } && character != SoftHyphen;

    /// <summary>The soft hyphen (U+00AD), dropped by the fold: a line-break hint inside a word, never seen.</summary>
    private const char SoftHyphen = (char)0x00AD;

    private static string?[] BuildUnits()
    {
        var units = new string?[char.MaxValue + 1];
        for (var code = 0; code <= char.MaxValue; code++)
        {
            var character = (char)code;
            if (char.IsSurrogate(character))
            {
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                units[code] = string.Empty;
                continue;
            }

            var folded = (TitleFolding.FoldOf(character) ?? character.ToString())
                .Replace("œ", "oe", StringComparison.Ordinal)
                .Replace("æ", "ae", StringComparison.Ordinal);
            units[code] = folded.Length == 1 && folded[0] == character ? null : folded;
        }

        // The typographic apostrophes and the no-break spaces, one unit for one, and the soft hyphen dropped: offsets stay exact.
        foreach (var apostrophe in new[] { (char)0x2019, (char)0x2018, (char)0x02BC })
        {
            units[apostrophe] = "'";
        }

        foreach (var space in new[] { (char)0x00A0, (char)0x202F, (char)0x2007 })
        {
            units[space] = " ";
        }

        units[SoftHyphen] = string.Empty;
        return units;
    }
}

/// <summary>
/// A text and its fold (<see cref="TextFolding"/>), each folded unit naming the unit of the text it comes from, so that what
/// a folded needle matches is said as a span of the text's own characters.
/// </summary>
public sealed class FoldedText
{
    private readonly int[]? _origins;

    internal FoldedText(string original, string folded, int[]? origins)
    {
        Original = original;
        Folded = folded;
        _origins = origins;
    }

    /// <summary>The text as it is.</summary>
    public string Original { get; }

    /// <summary>The text folded.</summary>
    public string Folded { get; }

    /// <summary>Whether the folded text holds <paramref name="foldedNeedle"/> (a needle that cannot match holds nowhere).</summary>
    public bool Contains(string foldedNeedle) =>
        TextFolding.CanMatch(foldedNeedle) && Folded.Contains(foldedNeedle, StringComparison.Ordinal);

    /// <summary>
    /// Every occurrence of <paramref name="foldedNeedle"/> in the folded text, left to right, each looked for after the end of
    /// the one before, as the span of the original it comes from: from the unit its first folded unit comes from to the end of
    /// the unit its last comes from, with the combining marks that follow that unit (they are folded away, and belong to it).
    /// None for a needle that cannot match.
    /// </summary>
    public IReadOnlyList<(int Start, int End)> Occurrences(string foldedNeedle)
    {
        ArgumentNullException.ThrowIfNull(foldedNeedle);
        var spans = new List<(int Start, int End)>();
        if (!TextFolding.CanMatch(foldedNeedle))
        {
            return spans;
        }

        for (var at = Folded.IndexOf(foldedNeedle, StringComparison.Ordinal); at >= 0;
             at = Folded.IndexOf(foldedNeedle, at + foldedNeedle.Length, StringComparison.Ordinal))
        {
            var start = _origins is null ? at : _origins[at];
            var end = (_origins is null ? at + foldedNeedle.Length - 1 : _origins[at + foldedNeedle.Length - 1]) + 1;
            while (end < Original.Length && TextFolding.IsDropped(Original[end]))
            {
                end++;
            }

            spans.Add((start, end));
        }

        return spans;
    }
}
