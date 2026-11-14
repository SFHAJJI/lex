using System.Text;
using Lex.V3.Contracts.Index;

namespace Lex.V3.Ingest;

/// <summary>
/// Distinct searchable texts, each folded once (<see cref="TextFolding"/>) and held as UTF-8 in one buffer: what a search
/// matches its folded needles against without reading the index again. A text identical to one already held is held once,
/// so a code's article that no version changed is folded and matched once however many versions hold it. A folded text
/// holds a folded needle exactly when its bytes hold the needle's: both are well-formed (a text read from SQLite is one,
/// and a needle that is not one matches nothing, <see cref="TextFolding.CanMatch"/>), and a UTF-8 match never starts or
/// ends inside a character.
/// </summary>
internal sealed class FoldedSearchTexts
{
    private readonly byte[] _bytes;
    private readonly int[] _ends;
    private readonly byte[] _headings;
    private readonly int[] _headingEnds;

    private FoldedSearchTexts(byte[] bytes, int[] ends, byte[] headings, int[] headingEnds)
    {
        _bytes = bytes;
        _ends = ends;
        _headings = headings;
        _headingEnds = headingEnds;
    }

    /// <summary>How many distinct texts are held.</summary>
    public int Count => _ends.Length;

    /// <summary>The bytes the folded texts take.</summary>
    public long ByteCount => _bytes.LongLength;

    /// <summary>
    /// The needles folded and in UTF-8, once per search. A needle that folds to whitespace alone, or to nothing (a combining
    /// mark typed alone), holds nothing to match and sets no condition; null when no needle is left, or when one folds to a
    /// lone surrogate, which no text holds: the search then has no hit.
    /// </summary>
    public static byte[][]? NeedlesOf(IReadOnlyList<string> needles)
    {
        ArgumentNullException.ThrowIfNull(needles);
        var folded = new List<byte[]>(needles.Count);
        foreach (var needle in needles)
        {
            var one = TextFolding.Fold(SearchWords.Segment(needle));
            if (!TextFolding.HoldsSomething(one))
            {
                continue;
            }

            if (!TextFolding.CanMatch(one))
            {
                return null;
            }

            folded.Add(Encoding.UTF8.GetBytes(one));
        }

        return folded.Count == 0 ? null : folded.ToArray();
    }

    /// <summary>
    /// Whether the text numbered <paramref name="text"/> holds every one of the folded needles, each starting and ending on a
    /// word's bounds (<see cref="SearchWords.HoldsBounded"/>: "de" never matches inside "decide").
    /// </summary>
    public bool Holds(int text, byte[][] needles)
    {
        var span = TextOf(text);
        foreach (var needle in needles)
        {
            if (!SearchWords.HoldsBounded(span, needle))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The folded text numbered <paramref name="text"/>, its glued words set apart (<see cref="SearchWords.Segment"/>).</summary>
    public ReadOnlySpan<byte> TextOf(int text)
    {
        var start = text == 0 ? 0 : _ends[text - 1];
        return _bytes.AsSpan(start, _ends[text] - start);
    }

    /// <summary>The folded heading of the text numbered <paramref name="text"/> (<see cref="SearchWords.HeadingOf"/>), or empty.</summary>
    public ReadOnlySpan<byte> HeadingOf(int text)
    {
        var start = text == 0 ? 0 : _headingEnds[text - 1];
        return _headings.AsSpan(start, _headingEnds[text] - start);
    }

    /// <summary>Adds texts one by one, each distinct text once, and numbers them in the order first added.</summary>
    internal sealed class Builder
    {
        private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
        private readonly List<int> _ends = [];
        private byte[] _buffer = new byte[1 << 16];
        private int _length;
        private readonly List<byte> _headings = [];
        private readonly List<int> _headingEnds = [];

        /// <summary>The number of <paramref name="text"/>, folded and added when it is not held yet.</summary>
        public int Add(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (_numbers.TryGetValue(text, out var number))
            {
                return number;
            }

            // The text a search matches: its glued words set apart, then folded (SearchWords.Rule); its heading, alike.
            var folded = TextFolding.Fold(SearchWords.Segment(text));
            _headings.AddRange(Encoding.UTF8.GetBytes(TextFolding.Fold(SearchWords.Segment(SearchWords.HeadingOf(text)))));
            _headingEnds.Add(_headings.Count);
            var count = Encoding.UTF8.GetByteCount(folded);
            if ((long)_length + count > Array.MaxLength)
            {
                throw new InvalidDataException("The folded searchable text of one language passes the largest array this runtime holds.");
            }

            if (_length + count > _buffer.Length)
            {
                Array.Resize(ref _buffer, (int)Math.Min(Array.MaxLength, Math.Max((long)_buffer.Length * 2, (long)_length + count)));
            }

            _length += Encoding.UTF8.GetBytes(folded, _buffer.AsSpan(_length));
            _ends.Add(_length);
            number = _ends.Count - 1;
            _numbers[text] = number;
            return number;
        }

        /// <summary>The texts added, held; the builder's own copy of each original text is let go.</summary>
        public FoldedSearchTexts Build()
        {
            var built = new FoldedSearchTexts(_buffer.AsSpan(0, _length).ToArray(), _ends.ToArray(), _headings.ToArray(), _headingEnds.ToArray());
            _numbers.Clear();
            _buffer = [];
            return built;
        }
    }
}
