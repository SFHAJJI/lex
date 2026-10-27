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

    private FoldedSearchTexts(byte[] bytes, int[] ends)
    {
        _bytes = bytes;
        _ends = ends;
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
            var one = TextFolding.Fold(needle);
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

    /// <summary>Whether the text numbered <paramref name="text"/> holds every one of the folded needles.</summary>
    public bool Holds(int text, byte[][] needles)
    {
        var start = text == 0 ? 0 : _ends[text - 1];
        var span = _bytes.AsSpan(start, _ends[text] - start);
        foreach (var needle in needles)
        {
            if (span.IndexOf(needle) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Adds texts one by one, each distinct text once, and numbers them in the order first added.</summary>
    internal sealed class Builder
    {
        private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
        private readonly List<int> _ends = [];
        private byte[] _buffer = new byte[1 << 16];
        private int _length;

        /// <summary>The number of <paramref name="text"/>, folded and added when it is not held yet.</summary>
        public int Add(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (_numbers.TryGetValue(text, out var number))
            {
                return number;
            }

            var folded = TextFolding.Fold(text);
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
            var built = new FoldedSearchTexts(_buffer.AsSpan(0, _length).ToArray(), _ends.ToArray());
            _numbers.Clear();
            _buffer = [];
            return built;
        }
    }
}
