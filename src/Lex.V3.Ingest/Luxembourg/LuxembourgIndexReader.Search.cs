namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// The search's folded copy of the index's searchable text (the plan's item 4.4): per language, every article with the state
/// that holds it, and each distinct text folded once (<see cref="FoldedSearchTexts"/>), read in one pass of the immutable
/// index and held for the life of the reader. A search folds its needles and matches them there, so capitals and accents
/// never decide a hit, over the indexes already built: the index holds no folded column, and none is needed.
/// </summary>
public sealed partial class LuxembourgIndexReader
{
    private readonly object _searchTextsGate = new();
    private readonly Dictionary<string, LuxembourgSearchTexts> _searchTexts = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the folded copy of every language the capability manifest measured searchable text in, so that no search pays
    /// it and none waits behind it. The server calls it once the mount is open.
    /// </summary>
    public void WarmSearch()
    {
        foreach (var language in SearchableLanguages())
        {
            _ = SearchTextsOf(language);
        }
    }

    /// <summary>
    /// The folded copy of one language's searchable text (<see cref="LuxembourgIndexQueries.SearchTexts"/>), read once: every
    /// article of a state in that language, ordered by work key, publisher date, article identity and state digest (the order
    /// a search answers its hits in), each with the number of its folded text. Its own read-only connection on the reader's
    /// private, verified copy, as every scan has, so the read does not hold the connection every other operation waits on.
    /// </summary>
    internal LuxembourgSearchTexts SearchTextsOf(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        lock (_searchTextsGate)
        {
            if (_searchTexts.TryGetValue(language, out var held))
            {
                return held;
            }

            // One string for each repeated value (a work key, a date, a state digest, a publisher id), so the copy holds each
            // value once and not once per article.
            var shared = new Dictionary<string, string>(StringComparer.Ordinal);
            string Shared(string value)
            {
                if (!shared.TryGetValue(value, out var one))
                {
                    shared[value] = one = value;
                }

                return one;
            }

            var texts = new FoldedSearchTexts.Builder();
            var rows = new List<(LuxembourgIndexSearchHit Hit, int Text)>();
            using (var connection = LuxembourgIndexBuilder.OpenForServing(_path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = LuxembourgIndexQueries.SearchTexts;
                command.Parameters.AddWithValue("$language", language);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add((
                        new LuxembourgIndexSearchHit(
                            Shared(reader.GetString(0)), Shared(reader.GetString(1)), Shared(reader.GetString(2)), reader.GetString(3),
                            Shared(reader.GetString(4)), reader.IsDBNull(5) ? null : Shared(reader.GetString(5))),
                        texts.Add(reader.GetString(6))));
                }
            }

            rows.Sort(static (left, right) =>
            {
                var compared = string.CompareOrdinal(left.Hit.WorkKey, right.Hit.WorkKey);
                if (compared == 0)
                {
                    compared = string.CompareOrdinal(left.Hit.ApplicabilityDate, right.Hit.ApplicabilityDate);
                }

                if (compared == 0)
                {
                    compared = string.CompareOrdinal(left.Hit.ArticleIdentitySha256, right.Hit.ArticleIdentitySha256);
                }

                return compared != 0 ? compared : string.CompareOrdinal(left.Hit.StateSha256, right.Hit.StateSha256);
            });
            var built = new LuxembourgSearchTexts(
                rows.Select(static row => row.Hit).ToArray(),
                rows.Select(static row => row.Text).ToArray(),
                texts.Build());
            _searchTexts[language] = built;
            return built;
        }
    }
}

/// <summary>
/// One language's folded searchable text as a search reads it: every article of a state in that language in the search's
/// order, and each article's folded text, held once per distinct text.
/// </summary>
internal sealed class LuxembourgSearchTexts
{
    private readonly LuxembourgIndexSearchHit[] _articles;
    private readonly int[] _textOf;
    private readonly FoldedSearchTexts _texts;

    internal LuxembourgSearchTexts(LuxembourgIndexSearchHit[] articles, int[] textOf, FoldedSearchTexts texts)
    {
        _articles = articles;
        _textOf = textOf;
        _texts = texts;
    }

    /// <summary>How many articles of states are held.</summary>
    public int ArticleCount => _articles.Length;

    /// <summary>How many distinct texts they hold.</summary>
    public int TextCount => _texts.Count;

    /// <summary>The bytes the folded texts take.</summary>
    public long FoldedByteCount => _texts.ByteCount;

    /// <summary>
    /// Every article, of the named work's states when one is named, whose folded text holds every folded needle, in the
    /// search's order; each distinct text is matched once.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexSearchHit> Find(IReadOnlyList<string> needles, string? workKey)
    {
        var folded = FoldedSearchTexts.NeedlesOf(needles);
        if (folded is null)
        {
            return [];
        }

        var (from, to) = workKey is null ? (0, _articles.Length) : RangeOf(workKey);
        var known = new sbyte[_texts.Count];
        var found = new List<LuxembourgIndexSearchHit>();
        for (var index = from; index < to; index++)
        {
            var text = _textOf[index];
            if (known[text] == 0)
            {
                known[text] = _texts.Holds(text, folded) ? (sbyte)1 : (sbyte)-1;
            }

            if (known[text] > 0)
            {
                found.Add(_articles[index]);
            }
        }

        return found;
    }

    /// <summary>The run of one work's articles, which the search's order keeps together.</summary>
    private (int From, int To) RangeOf(string workKey)
    {
        int Bound(bool after)
        {
            var low = 0;
            var high = _articles.Length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                var compared = string.CompareOrdinal(_articles[middle].WorkKey, workKey);
                if (compared < 0 || (after && compared == 0))
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        return (Bound(after: false), Bound(after: true));
    }
}
