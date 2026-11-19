using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One act of the law index: its address, its title as published, the names it is called by and the weights of its words.</summary>
/// <param name="Work">The act's address on this site ("/lu-legilux/code-travail").</param>
/// <param name="Names">The names a question may call it by, already normalised ("code du travail").</param>
/// <param name="Words">The weight of each stemmed word that routes to the act, in hundredths.</param>
public sealed record V4LawAct(string Work, string Title, IReadOnlyList<string> Names, int Articles, IReadOnlyDictionary<string, int> Words);

/// <summary>An act the router kept for a question: its score in hundredths, and whether the question writes one of its names.</summary>
public sealed record V4RoutedAct(V4LawAct Act, int ScoreCents, bool Named);

/// <summary>An article the pre-filter kept for the outline call, with the line that is shown of it.</summary>
public sealed record V4OutlineArticle(V4Article Article, string Brief);

/// <summary>
/// The law index (<c>lex-v4-law-index/1</c>): every Luxembourg act Lex holds, by the words that route a question to it. It
/// is data, made outside this build from the mounted corpus and read from the bytes committed beside this file; the router
/// over it is code only, and must give what the reference (router.py beside the index's source) gives for the same file.
/// "Code filters, the model reads a closed list, code checks."
/// </summary>
public sealed class V4LawIndex
{
    public const string Schema = "lex-v4-law-index/1";
    private const string Resource = "Lex.V3.Contracts.Assistant.Bricks.LawIndex.json";

    // The letters of U+00C0 to U+017F that a canonical decomposition leaves whole (no base letter and mark): they are
    // outside [a-z0-9] and become a space, as in the reference. "œ" and "æ" are written out before this is asked.
    private const string Undecomposed = "ÆÐ×ØÞßæð÷øþĐđĦħıĲĳĸĿŀŁłŉŊŋŒœŦŧſ";

    private static readonly Lazy<V4LawIndex?> EmbeddedValue = new(Load);

    private readonly HashSet<string> _stopwords;
    private readonly Dictionary<string, List<(int Act, int Cents)>> _postings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Name, int Act)>> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, V4LawAct> _byWork = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nameWords = new(StringComparer.Ordinal);

    private V4LawIndex(string version, string sha256, string stemRule, int nameBonusCents, int minScoreCents, int keepRatioCents, int maxActs, int articleKeep, HashSet<string> stopwords, List<V4LawAct> acts)
    {
        Version = version;
        Sha256 = sha256;
        StemRule = stemRule;
        NameBonusCents = nameBonusCents;
        MinScoreCents = minScoreCents;
        KeepRatioCents = keepRatioCents;
        MaxActs = maxActs;
        ArticleKeep = articleKeep;
        _stopwords = stopwords;
        Acts = acts.AsReadOnly();
        for (var number = 0; number < acts.Count; number++)
        {
            var act = acts[number];
            _byWork[act.Work] = act;
            foreach (var (word, cents) in act.Words)
            {
                if (!_postings.TryGetValue(word, out var list))
                {
                    _postings[word] = list = [];
                }

                list.Add((number, cents));
            }

            foreach (var name in act.Names)
            {
                var space = name.IndexOf(' ', StringComparison.Ordinal);
                var first = space < 0 ? name : name[..space];
                if (!_names.TryGetValue(first, out var list))
                {
                    _names[first] = list = [];
                }

                list.Add((name, number));
                foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    _nameWords.Add(word);
                }
            }
        }
    }

    /// <summary>The law index this build carries, or null when the build carries none.</summary>
    public static V4LawIndex? Embedded => EmbeddedValue.Value;

    public string Version { get; }

    public string Sha256 { get; }

    /// <summary>strip_sx, prefix6 or strip_sx_prefix7.</summary>
    public string StemRule { get; }

    public int NameBonusCents { get; }

    public int MinScoreCents { get; }

    public int KeepRatioCents { get; }

    public int MaxActs { get; }

    /// <summary>The most articles of one act the outline call is given.</summary>
    public int ArticleKeep { get; }

    public ReadOnlyCollection<V4LawAct> Acts { get; }

    public V4LawAct? Act(string work) => _byWork.GetValueOrDefault(work);

    /// <summary>Whether a normalised word is a word of a name the index calls an act by.</summary>
    public bool NamesWord(string word) => _nameWords.Contains(word);

    private static V4LawIndex? Load()
    {
        using var stream = typeof(V4LawIndex).Assembly.GetManifestResourceStream(Resource);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    /// <summary>Reads an index from its bytes, refusing a file that is not of the schema.</summary>
    public static V4LawIndex Read(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var document = JsonDocument.Parse(utf8);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != Schema)
        {
            throw new InvalidDataException("Not a lex-v4-law-index/1.");
        }

        var rule = root.GetProperty("stem_rule").GetString()!;
        if (rule is not ("strip_sx" or "prefix6" or "strip_sx_prefix7"))
        {
            throw new InvalidDataException("The law index names a stem rule this build does not know.");
        }

        var stopwords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in root.GetProperty("stopwords").EnumerateArray())
        {
            stopwords.Add(word.GetString()!);
        }

        var acts = new List<V4LawAct>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.GetProperty("acts").EnumerateArray())
        {
            var work = element.GetProperty("work").GetString()!;
            if (!seen.Add(work))
            {
                throw new InvalidDataException("The law index names an act twice.");
            }

            var names = new List<string>();
            foreach (var name in element.GetProperty("names").EnumerateArray())
            {
                // Stored already normalised; normalised again, which changes nothing of such a name.
                var written = Normalise(name.GetString()!);
                if (written.Length > 0)
                {
                    names.Add(written);
                }
            }

            var words = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var word in element.GetProperty("words").EnumerateObject())
            {
                words[word.Name] = Cents(word.Value.GetDouble());
            }

            acts.Add(new V4LawAct(
                work,
                element.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString()! : work,
                names,
                element.TryGetProperty("articles", out var articles) && articles.TryGetInt32(out var count) ? count : 0,
                words));
        }

        return new V4LawIndex(
            root.GetProperty("version").GetString()!,
            Convert.ToHexStringLower(SHA256.HashData(utf8)),
            rule,
            Cents(root.GetProperty("name_bonus").GetDouble()),
            Cents(root.GetProperty("min_score").GetDouble()),
            Cents(root.GetProperty("keep_ratio").GetDouble()),
            root.GetProperty("max_acts").GetInt32(),
            root.GetProperty("article_keep").GetInt32(),
            stopwords,
            acts);
    }

    private static int Cents(double value) => (int)Math.Round(value * 100, MidpointRounding.ToEven);

    /// <summary>
    /// What one character gives in the matching copy: itself for [a-z0-9], its base letter for an accented Latin letter,
    /// two letters for "œ" and "æ", nothing for a combining mark, and a space for everything else. Written out, and not
    /// asked of string.Normalize: the API runs with InvariantGlobalization, where a normalisation decomposes nothing.
    /// </summary>
    private static void Append(StringBuilder into, char character)
    {
        var lower = char.ToLowerInvariant(character);
        if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
        {
            into.Append(lower);
        }
        else if (lower == 'œ')
        {
            into.Append("oe");
        }
        else if (lower == 'æ')
        {
            into.Append("ae");
        }
        else if (lower is >= '̀' and <= 'ͯ')
        {
            // A combining mark of a text already decomposed: dropped.
        }
        else if (lower is >= 'À' and < 'ƀ' && !Undecomposed.Contains(lower, StringComparison.Ordinal))
        {
            var folded = V4Text.Fold(lower.ToString());
            into.Append(folded[0] is >= 'a' and <= 'z' ? folded[0] : ' ');
        }
        else
        {
            into.Append(' ');
        }
    }

    /// <summary>The matching copy of a text: lower case, accents off, everything outside [a-z0-9] a space, runs of spaces one space, none at either end.</summary>
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var raw = new StringBuilder(text.Length + 8);
        foreach (var character in text)
        {
            Append(raw, character);
        }

        var collapsed = new StringBuilder(raw.Length);
        var pending = false;
        for (var at = 0; at < raw.Length; at++)
        {
            if (raw[at] == ' ')
            {
                pending = collapsed.Length > 0;
                continue;
            }

            if (pending)
            {
                collapsed.Append(' ');
                pending = false;
            }

            collapsed.Append(raw[at]);
        }

        return collapsed.ToString();
    }

    /// <summary>A word of the matching copy as the index keeps it, or null for a word that is not kept (short, all digits, a stopword).</summary>
    public string? Token(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length < 3 || _stopwords.Contains(word))
        {
            return null;
        }

        var digits = true;
        foreach (var character in word)
        {
            digits &= character is >= '0' and <= '9';
        }

        if (digits)
        {
            return null;
        }

        if (StemRule == "prefix6")
        {
            return word.Length > 6 ? word[..6] : word;
        }

        var stem = word.Length > 4 && word[^1] is 's' or 'x' ? word[..^1] : word;
        return StemRule == "strip_sx_prefix7" && stem.Length > 7 ? stem[..7] : stem;
    }

    /// <summary>The tokens of a text, each once, in the order they first appear.</summary>
    public List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in Normalise(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Token(word) is { } token && seen.Add(token))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    /// <summary>
    /// The words of a text as the index keeps them, each with where it stands in the text as written: what the pre-filter
    /// counts, and what a line shown of an article is cut around.
    /// </summary>
    public List<(string Token, int Start, int End)> TokensAt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<(string, int, int)>();
        var word = new StringBuilder();
        var one = new StringBuilder(2);
        var start = 0;
        for (var at = 0; at <= text.Length; at++)
        {
            one.Clear();
            if (at < text.Length)
            {
                Append(one, text[at]);
                if (one.Length == 0)
                {
                    continue;
                }
            }

            if (at == text.Length || one[0] == ' ')
            {
                if (word.Length > 0 && Token(word.ToString()) is { } token)
                {
                    found.Add((token, start, at));
                }

                word.Clear();
                continue;
            }

            if (word.Length == 0)
            {
                start = at;
            }

            word.Append(one);
        }

        return found;
    }

    /// <summary>
    /// The acts kept for a question, the best first: the sum of the weights of the question's distinct tokens, a bonus once
    /// when the question writes one of the act's names as whole words; ties by address; the acts at or above the floor and
    /// at or above the ratio of the best, at most <see cref="MaxActs"/>. Empty when no act passes.
    /// </summary>
    public List<V4RoutedAct> Route(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var cents = new Dictionary<int, int>();
        foreach (var token in Tokens(question))
        {
            if (_postings.TryGetValue(token, out var list))
            {
                foreach (var (act, weight) in list)
                {
                    cents[act] = cents.GetValueOrDefault(act) + weight;
                }
            }
        }

        var normalised = Normalise(question);
        var padded = " " + normalised + " ";
        var named = new HashSet<int>();
        foreach (var word in normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (_names.TryGetValue(word, out var list))
            {
                foreach (var (name, act) in list)
                {
                    if (!named.Contains(act) && padded.Contains(" " + name + " ", StringComparison.Ordinal))
                    {
                        named.Add(act);
                    }
                }
            }
        }

        foreach (var act in named)
        {
            cents[act] = cents.GetValueOrDefault(act) + NameBonusCents;
        }

        var ranked = new List<(int Act, int Cents)>(cents.Count);
        foreach (var (act, score) in cents)
        {
            ranked.Add((act, score));
        }

        ranked.Sort((left, right) => left.Cents != right.Cents
            ? right.Cents.CompareTo(left.Cents)
            : string.CompareOrdinal(Acts[left.Act].Work, Acts[right.Act].Work));
        var kept = new List<V4RoutedAct>();
        for (var at = 0; at < ranked.Count && at < MaxActs; at++)
        {
            var (act, score) = ranked[at];
            if (score >= MinScoreCents && (long)score * 100 >= (long)KeepRatioCents * ranked[0].Cents)
            {
                kept.Add(new V4RoutedAct(Acts[act], score, named.Contains(act)));
            }
        }

        return kept;
    }

    // ---- The article pre-filter (code only): inside one act the router kept, the articles whose heading or text hold the
    // question's words, scored as the reference scores them, so that the outline call is given a short list of a long code.

    /// <summary>About how many characters of an article with no heading the outline call is shown.</summary>
    public const int BriefLength = 110;

    private sealed class Counts(List<(V4Article Article, HashSet<string> Heading, Dictionary<string, int> Text)> articles, Dictionary<string, int> holding)
    {
        public List<(V4Article Article, HashSet<string> Heading, Dictionary<string, int> Text)> Articles { get; } = articles;

        /// <summary>For each word, in how many articles the heading or the text hold it.</summary>
        public Dictionary<string, int> Holding { get; } = holding;
    }

    // The counts of a version of an act are made once: the tables of a version are kept by the mount, and these with them.
    private readonly ConditionalWeakTable<V4ActTables, Counts> _counts = new();

    private Counts CountsOf(V4ActTables tables)
    {
        if (_counts.TryGetValue(tables, out var made))
        {
            return made;
        }

        var articles = new List<(V4Article, HashSet<string>, Dictionary<string, int>)>();
        var holding = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var article in tables.Articles)
        {
            if (article.Status == "empty")
            {
                continue;
            }

            var heading = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(article.Heading))
            {
                foreach (var (token, _, _) in TokensAt(article.Heading))
                {
                    heading.Add(token);
                }
            }

            var text = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in tables.TextLinesOf(article))
            {
                foreach (var (token, _, _) in TokensAt(line.Text))
                {
                    text[token] = text.GetValueOrDefault(token) + 1;
                }
            }

            foreach (var token in heading)
            {
                holding[token] = holding.GetValueOrDefault(token) + 1;
            }

            foreach (var token in text.Keys)
            {
                if (!heading.Contains(token))
                {
                    holding[token] = holding.GetValueOrDefault(token) + 1;
                }
            }

            articles.Add((article, heading, text));
        }

        made = new Counts(articles, holding);
        _counts.AddOrUpdate(tables, made);
        return made;
    }

    /// <summary>
    /// The articles of an act the outline call is given, in the order of the document: all of them when the act holds at
    /// most <see cref="ArticleKeep"/>, else the best by score (ties by the order of the document). idf(w) =
    /// ln(1 + N / df(w)); score = sum over the question's distinct tokens of idf(w) * (3 if w is in the heading) +
    /// idf(w) * min(count of w in the text, 3). Each with the line shown of it: its heading, else its text around the
    /// first word of the question it holds.
    /// </summary>
    public List<V4OutlineArticle> Prefilter(V4ActTables tables, IReadOnlyList<string> questionTokens)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(questionTokens);
        var counts = CountsOf(tables);
        var total = counts.Articles.Count;
        var scored = new List<(double Score, int Position)>(total);
        for (var position = 0; position < total; position++)
        {
            var (_, heading, text) = counts.Articles[position];
            var score = 0.0;
            foreach (var token in questionTokens)
            {
                if (!counts.Holding.TryGetValue(token, out var holding) || holding == 0)
                {
                    continue;
                }

                var idf = Math.Log(1 + ((double)total / holding));
                score += (idf * (heading.Contains(token) ? 3 : 0)) + (idf * Math.Min(text.GetValueOrDefault(token), 3));
            }

            scored.Add((score, position));
        }

        if (total > ArticleKeep)
        {
            scored.Sort(static (left, right) => left.Score != right.Score ? right.Score.CompareTo(left.Score) : left.Position.CompareTo(right.Position));
            scored.RemoveRange(ArticleKeep, scored.Count - ArticleKeep);
            scored.Sort(static (left, right) => left.Position.CompareTo(right.Position));
        }

        var asked = new HashSet<string>(questionTokens, StringComparer.Ordinal);
        var kept = new List<V4OutlineArticle>(scored.Count);
        foreach (var (_, position) in scored)
        {
            var article = counts.Articles[position].Article;
            kept.Add(new V4OutlineArticle(article, string.IsNullOrWhiteSpace(article.Heading) ? Around(tables, article, asked) : article.Heading));
        }

        return kept;
    }

    /// <summary>The text of an article around the first word of the question it holds, about <see cref="BriefLength"/> characters; else its first words.</summary>
    private string Around(V4ActTables tables, V4Article article, HashSet<string> asked)
    {
        string? first = null;
        foreach (var line in tables.TextLinesOf(article))
        {
            first ??= line.Text;
            foreach (var (token, start, _) in TokensAt(line.Text))
            {
                if (asked.Contains(token))
                {
                    return Window(line.Text, start);
                }
            }
        }

        return first is null ? string.Empty : Window(first, 0);
    }

    private static string Window(string text, int match)
    {
        if (text.Length <= BriefLength)
        {
            return text;
        }

        // About a third of the window before the word, cut at a space; the rest after it.
        var start = Math.Max(0, Math.Min(match - 36, text.Length - BriefLength));
        if (start > 0)
        {
            var space = text.IndexOf(' ', start);
            start = space >= 0 && space < match ? space + 1 : match;
        }

        var end = Math.Min(text.Length, start + BriefLength);
        if (end < text.Length)
        {
            var space = text.LastIndexOf(' ', end - 1, end - start);
            if (space > start + (BriefLength / 2))
            {
                end = space;
            }
            else if (char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }
        }

        if (start > 0 && char.IsLowSurrogate(text[start]))
        {
            start++;
        }

        return (start > 0 ? "… " : string.Empty) + text[start..end].Trim() + (end < text.Length ? " …" : string.Empty);
    }
}
