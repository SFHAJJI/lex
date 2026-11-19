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
        try
        {
            return Read(memory.ToArray());
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // A file that is not of the schema leaves the build with no law index: the wider reading is then off, and
            // the assistant answers as it did. A test holds the file to its schema, so this is not met in a release.
            return null;
        }
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
            if (element.TryGetProperty("names", out var called) && called.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in called.EnumerateArray())
                {
                    // Stored already normalised; normalised again, which changes nothing of such a name.
                    var written = Normalise(name.GetString()!);
                    if (written.Length > 0)
                    {
                        names.Add(written);
                    }
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

    /// <summary>The numbers of the acts a question writes a name of, as whole words of its matching copy.</summary>
    private HashSet<int> NamedIn(string question)
    {
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

        return named;
    }

    /// <summary>
    /// The acts a question names, whatever their score: what is read when the question names a code or a law, and
    /// nothing else is. In the order of the index, at most <see cref="MaxActs"/>.
    /// </summary>
    public List<V4RoutedAct> Named(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var numbers = new List<int>(NamedIn(question));
        numbers.Sort();
        var acts = new List<V4RoutedAct>();
        foreach (var number in numbers)
        {
            if (acts.Count < MaxActs)
            {
                acts.Add(new V4RoutedAct(Acts[number], NameBonusCents, true));
            }
        }

        return acts;
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

        var named = NamedIn(question);
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
    public List<V4OutlineArticle> Prefilter(V4ActTables tables, IReadOnlyList<string> questionTokens) =>
        Prefilter(tables, questionTokens, ArticleKeep, null);

    /// <summary>
    /// The same, with the number kept said by the caller and, when <paramref name="admit"/> is given, among the articles it
    /// admits only: the best <paramref name="keep"/> of them by the same score, in the order of the document.
    /// </summary>
    public List<V4OutlineArticle> Prefilter(V4ActTables tables, IReadOnlyList<string> questionTokens, int keep, Func<V4Article, bool>? admit)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(questionTokens);
        var counts = CountsOf(tables);
        var total = counts.Articles.Count;
        var scored = new List<(double Score, int Position)>(total);
        for (var position = 0; position < total; position++)
        {
            var (candidate, heading, text) = counts.Articles[position];
            if (admit is not null && !admit(candidate))
            {
                continue;
            }

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

        keep = Math.Max(0, keep);
        if (scored.Count > keep)
        {
            scored.Sort(static (left, right) => left.Score != right.Score ? right.Score.CompareTo(left.Score) : left.Position.CompareTo(right.Position));
            scored.RemoveRange(keep, scored.Count - keep);
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

    // ---- The parts of a big act (code only). The mount holds an act's articles and no heading of its books, titles or
    // chapters; a code that numbers its articles by its parts says them in the numbers ("L. 233-4": book 2, title 3,
    // chapter 3; "710-5" in the companies law). A part is the run of articles that share the number before the hyphen.

    private readonly ConditionalWeakTable<V4ActTables, List<V4LawSection>> _sections = new();

    /// <summary>
    /// The parts of an act as its article numbers say them, in the order of the document, each with what a reader of a
    /// table of contents would be shown of it: the range of its articles, how its first article begins, and the words that
    /// mark it among the other parts. When the numbers say no parts (fewer than six in ten articles carry one, or fewer
    /// than five parts: the Code civil and the Code pénal number their articles in one run), the parts are runs of
    /// <see cref="RunLength"/> consecutive articles, with keys that start with <c>run</c>. Empty for an act of fewer than
    /// five runs.
    /// </summary>
    public IReadOnlyList<V4LawSection> Sections(V4ActTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (_sections.TryGetValue(tables, out var made))
        {
            return made;
        }

        made = SectionsOf(tables, drop: 0, run: 0);
        if (made.Count > 250)
        {
            made = SectionsOf(tables, drop: 1, run: 0);
        }

        // A code that numbers its articles in one run (the Code civil, the Code pénal) is cut in runs of consecutive
        // articles: its books and chapters are runs of the document too, and a run is shown as a part is.
        if (made.Count == 0)
        {
            made = SectionsOf(tables, drop: 0, run: RunLength);
        }

        _sections.AddOrUpdate(tables, made);
        return made;
    }

    /// <summary>The part an article's identifier names ("art_l_233-4": "l233"; "art_710-5": "710"), or null when it names none.</summary>
    internal static string? SectionKey(string id, int drop)
    {
        var at = id.StartsWith("article_", StringComparison.Ordinal) ? 8 : id.StartsWith("art_", StringComparison.Ordinal) ? 4 : id.StartsWith("art", StringComparison.Ordinal) ? 3 : -1;
        if (at < 0 || at >= id.Length)
        {
            return null;
        }

        var letter = string.Empty;
        if (char.IsAsciiLetterLower(id[at]) && at + 1 < id.Length && (id[at + 1] == '_' || char.IsAsciiDigit(id[at + 1])))
        {
            letter = id[at].ToString();
            at += id[at + 1] == '_' ? 2 : 1;
        }

        var start = at;
        while (at < id.Length && char.IsAsciiDigit(id[at]))
        {
            at++;
        }

        var digits = at - start;
        if (digits < 3 || at >= id.Length || id[at] != '-')
        {
            return null;
        }

        return letter + id[start..(at - Math.Min(drop, digits - 2))];
    }

    /// <summary>How many consecutive articles make a part of an act whose numbers say none.</summary>
    public const int RunLength = 20;

    private List<V4LawSection> SectionsOf(V4ActTables tables, int drop, int run)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<V4Article>>(StringComparer.Ordinal);
        var all = 0;
        var keyed = 0;
        foreach (var article in tables.Articles)
        {
            if (article.Status == "empty")
            {
                continue;
            }

            all++;
            if ((run > 0 ? "run" + (((all - 1) / run) + 1).ToString(CultureInfo.InvariantCulture) : SectionKey(article.Id, drop)) is not { } key)
            {
                continue;
            }

            keyed++;
            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
                order.Add(key);
            }

            group.Add(article);
        }

        if (order.Count < 5 || keyed * 10 < all * 6)
        {
            return [];
        }

        // The words of each part, and in how many parts each word stands.
        var words = new List<Dictionary<string, (int Articles, string Surface)>>(order.Count);
        var standing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in order)
        {
            var held = new Dictionary<string, (int Articles, string Surface)>(StringComparer.Ordinal);
            foreach (var article in groups[key])
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in tables.TextLinesOf(article))
                {
                    foreach (var (token, start, end) in TokensAt(line.Text))
                    {
                        if (token.Length >= 4 && seen.Add(token))
                        {
                            held[token] = held.TryGetValue(token, out var before) ? (before.Articles + 1, before.Surface) : (1, line.Text[start..end].ToLowerInvariant());
                        }
                    }
                }
            }

            foreach (var token in held.Keys)
            {
                standing[token] = standing.GetValueOrDefault(token) + 1;
            }

            words.Add(held);
        }

        var sections = new List<V4LawSection>(order.Count);
        for (var at = 0; at < order.Count; at++)
        {
            var articles = groups[order[at]];
            var marked = new List<(double Score, string Token, string Surface)>();
            foreach (var (token, (count, surface)) in words[at])
            {
                // A word that stands in one article only says little of a part of several articles.
                var weight = count >= 2 || articles.Count < 4 ? 1.0 : 0.25;
                marked.Add((weight * count * Math.Log(1 + ((double)order.Count / standing[token])), token, surface));
            }

            marked.Sort(static (left, right) => left.Score != right.Score ? right.Score.CompareTo(left.Score) : string.CompareOrdinal(left.Token, right.Token));
            var marks = new List<string>();
            foreach (var (_, _, surface) in marked)
            {
                if (marks.Count < 10 && !marks.Contains(surface, StringComparer.Ordinal))
                {
                    marks.Add(surface);
                }
            }

            string? starts = null;
            foreach (var article in articles)
            {
                foreach (var line in tables.TextLinesOf(article))
                {
                    if (Lead(Flat(line.Text)) is { Length: >= 25 } lead)
                    {
                        starts = Window(lead, 0);
                        break;
                    }
                }

                if (starts is not null)
                {
                    break;
                }
            }

            var first = Numbered(articles[0]);
            var last = Numbered(articles[^1]);
            sections.Add(new V4LawSection(order[at], articles.AsReadOnly(), first == last ? first : first + " à " + last, starts ?? string.Empty, string.Join(", ", marks)));
        }

        return sections;
    }

    private static string Numbered(V4Article article)
    {
        // The label as published may carry the opening of an amendment mark after the number ("L. 010-1.\n(").
        var label = article.Label ?? article.Id;
        var cut = label.IndexOfAny(['\n', '\r', '(']);
        label = (cut > 0 ? label[..cut] : label).Trim().TrimEnd('.').Trim();
        return label.StartsWith("Art.", StringComparison.OrdinalIgnoreCase) ? label[4..].Trim() : label.StartsWith("Article", StringComparison.OrdinalIgnoreCase) ? label[7..].Trim() : label;
    }

    // ---- Everyday words and the law's words (aliases.json, made beside the index: "virer" is "licenciement" in the code).

    private const string AliasResource = "Lex.V3.Contracts.Assistant.Bricks.LawAliases.json";

    private static readonly Lazy<(Dictionary<string, string[]> Map, string StemRule, string Sha256)?> AliasValue = new(LoadAliases);

    /// <summary>The digest of the alias file this build carries, or null when it carries none of the schema.</summary>
    public static string? AliasesSha256 => AliasValue.Value?.Sha256;

    private static (Dictionary<string, string[]> Map, string StemRule, string Sha256)? LoadAliases()
    {
        using var stream = typeof(V4LawIndex).Assembly.GetManifestResourceStream(AliasResource);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "lex-v4-law-aliases/1")
            {
                return null;
            }

            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var alias in root.GetProperty("aliases").EnumerateObject())
            {
                var targets = new List<string>();
                foreach (var target in alias.Value.EnumerateArray())
                {
                    if (target.GetString() is { Length: > 0 } word)
                    {
                        targets.Add(word);
                    }
                }

                map[alias.Name] = [.. targets];
            }

            return (map, root.GetProperty("stem_rule").GetString() ?? string.Empty, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The question's tokens with, after them, the law's words for each everyday word the alias file knows, each once.
    /// Used to find articles inside an act, never to route to one. The tokens themselves when the build carries no alias
    /// file of this index's stem rule.
    /// </summary>
    public List<string> Extended(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var extended = new List<string>(tokens);
        if (AliasValue.Value is not { } aliases || aliases.StemRule != StemRule)
        {
            return extended;
        }

        foreach (var token in tokens)
        {
            if (aliases.Map.TryGetValue(token, out var targets))
            {
                foreach (var target in targets)
                {
                    if (!extended.Contains(target, StringComparer.Ordinal))
                    {
                        extended.Add(target);
                    }
                }
            }
        }

        return extended;
    }

    /// <summary>The text of an article around the first word of the question it holds, about <see cref="BriefLength"/> characters; else its first words.</summary>
    private string Around(V4ActTables tables, V4Article article, HashSet<string> asked)
    {
        string? first = null;
        string? any = null;
        foreach (var line in tables.TextLinesOf(article))
        {
            var text = Flat(line.Text);
            any ??= text;
            if (first is null && Lead(text) is { Length: >= 25 } lead)
            {
                first = lead;
            }

            foreach (var (token, start, _) in TokensAt(text))
            {
                if (asked.Contains(token))
                {
                    return Window(text, start);
                }
            }
        }

        return (first ?? any) is { } shown ? Window(shown, 0) : string.Empty;
    }

    // The mark of an amending act that the publisher's text carries before an article's own words ("L. 4 juin 2020 ) Le
    // droit de former ..."): not what the article says, and never the line shown of it.
    private static readonly System.Text.RegularExpressions.Regex AmendmentMark = new(
        @"^(?:[()\s]*(?:L\.|Loi|Règl\.|R\.|Arr\.)\s[^()]{0,60}\d{4}\s*\)[()\s]*)+",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>A text on one line: every run of white space is one space.</summary>
    private static string Flat(string text)
    {
        var flat = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = flat.Length > 0;
            }
            else
            {
                if (space)
                {
                    flat.Append(' ');
                }

                space = false;
                flat.Append(character);
            }
        }

        return flat.ToString();
    }

    /// <summary>A flat line without the mark of an amending act it starts with.</summary>
    private static string Lead(string flat)
    {
        try
        {
            return AmendmentMark.Replace(flat, string.Empty, 1).TrimStart(')', '(', ' ');
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return flat;
        }
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

/// <summary>A part of a big act, as its article numbers say it: its articles in order, and what is shown of it in place of a heading.</summary>
/// <param name="Key">The number the articles share ("l233", "710"), or "run" and the place of a run of consecutive articles.</param>
/// <param name="Range">The first and the last article, by their labels.</param>
/// <param name="Starts">How the first article of the part begins.</param>
/// <param name="Words">The words that mark the part among the others, the most telling first.</param>
public sealed record V4LawSection(string Key, IReadOnlyList<V4Article> Articles, string Range, string Starts, string Words);
