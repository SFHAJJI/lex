using System.Globalization;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>
/// One row of the corpus index (brick A): an act, with the three columns a person types or reviews (domain, short names,
/// summary line). The index "holds enough to decide whether a document is a candidate. Once it starts holding answers it is a
/// second copy of the corpus, and the second copy drifts." [14C] So the facts Lex's own index holds (dates, versions, counts,
/// text status) are read from it when a question is answered (<see cref="V4ActFacts"/>) and never copied here.
/// </summary>
/// <param name="Role">base_text or executing_regulation.</param>
/// <param name="Tier">deep (lines, definitions and references are built) or shallow (what the publisher gives). "So tier the work." [14C]</param>
public sealed record V4IndexAct(
    string Work,
    string Publisher,
    string Kind,
    string Date,
    string Role,
    string Tier,
    string Domain,
    string Language,
    int Order,
    IReadOnlyList<string> ShortNames,
    string SummaryFr)
{
    /// <summary>The act's address on this site.</summary>
    public string Address => "/" + Publisher + "/" + Work;
}

/// <summary>An EU act of the domain by the names people give it, with its official address: held or not is read from the mount.</summary>
public sealed record V4IndexEuAct(string Id, string Celex, string NameFr, string NameEn, string Official, IReadOnlyList<string> ShortNames);

/// <summary>What Lex's own index says of an act on a date (brick A's columns filled by code): the version read, and the counts.</summary>
/// <param name="TextStatus">held, or the refusal code the evidence step answers (text_withheld, text_not_available, no_version_for_date, ...).</param>
public sealed record V4ActFacts(string Work, string TextStatus, string? VersionFrom, string? VersionTo, int Versions, int Articles, int Lines);

/// <summary>An act's name found in a question: the act, and the words that name it.</summary>
public sealed record V4ActMention(string? Work, string? EuAct, int Start, int End);

/// <summary>
/// The corpus index for private insurance (brick A), <c>lex-v4-corpus-index/1</c>, read from the bytes committed beside this
/// file: the domain, its acts in the order a router tries them (the base text first), and the EU acts of the domain by name.
/// "A folder of many documents is the same map one level higher" [7quater]: the first cut is a filter on the domain, not a
/// model call.
/// </summary>
public sealed class V4CorpusIndex
{
    public const string Schema = "lex-v4-corpus-index/1";
    private const string Resource = "Lex.V3.Contracts.Assistant.Bricks.CorpusIndex.json";

    private readonly (V4Key Key, string? Work, string? EuAct)[] _names;
    private readonly HashSet<string> _terms;

    private readonly V4Key[] _otherDomain;
    private readonly V4Key[] _domain;

    private V4CorpusIndex(string version, string sha256, List<V4IndexAct> acts, List<V4IndexEuAct> euActs, List<string> otherDomainWords, List<string> domainWords)
    {
        _otherDomain = otherDomainWords.Select(V4Key.Read).Where(static key => key is not null).Select(static key => key!).ToArray();
        _domain = domainWords.Select(V4Key.Read).Where(static key => key is not null).Select(static key => key!).ToArray();
        Version = version;
        Sha256 = sha256;
        Acts = acts.OrderBy(static act => act.Order).ToList().AsReadOnly();
        EuActs = euActs.AsReadOnly();
        _names = acts.SelectMany(static act => act.ShortNames.Select(name => (V4Key.Read(name)!, (string?)act.Work, (string?)null)))
            .Concat(euActs.SelectMany(static act => act.ShortNames.Select(name => (V4Key.Read(name)!, (string?)null, (string?)act.Id))))
            .OrderByDescending(static entry => entry.Item1.Length)
            .ThenByDescending(static entry => entry.Item1.Source.Length)
            .ToArray();
        _terms = new HashSet<string>(euActs.SelectMany(static act => new[] { act.NameFr, act.NameEn }), StringComparer.Ordinal);
    }

    /// <summary>The corpus index this build carries.</summary>
    public static V4CorpusIndex Insurance { get; } = Load();

    public string Version { get; }

    public string Sha256 { get; }

    /// <summary>The acts of the domain, in the order a router tries them.</summary>
    public ReadOnlyCollection<V4IndexAct> Acts { get; }

    public ReadOnlyCollection<V4IndexEuAct> EuActs { get; }

    public V4IndexAct? Act(string work) => Acts.FirstOrDefault(act => act.Work == work);

    public V4IndexEuAct? EuAct(string id) => EuActs.FirstOrDefault(act => act.Id == id);

    /// <summary>Whether a text is a name this index writes for an EU act, exactly: what a sentence may show as one.</summary>
    public bool IsName(string value) => value is not null && _terms.Contains(value);

    /// <summary>
    /// Whether a question holds a word of another domain that shares this one's vocabulary (their trap "Two meanings in one
    /// word." [14C]: assurance is a private contract or the statutory social insurance). Such a question is not answered from
    /// this domain's acts.
    /// </summary>
    public bool OtherDomain(IReadOnlyList<V4Word> words) => _otherDomain.Any(key => key.In(words));

    /// <summary>
    /// Whether a question holds a word of this domain (<c>domain_words</c>: "assurance", "sinistre", "prime", "voiture"...):
    /// what lets the outline be read for a question no dictionary key reads, and keeps it shut for every other subject.
    /// </summary>
    public bool InDomain(IReadOnlyList<V4Word> words) => _domain.Any(key => key.In(words));

    /// <summary>The acts a question names by a short name of the index, the longest name first.</summary>
    public List<V4ActMention> Mentions(IReadOnlyList<V4Word> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var taken = new bool[words.Count];
        var mentions = new List<V4ActMention>();
        foreach (var (key, work, euAct) in _names)
        {
            foreach (var (at, count) in key.Occurrences(words))
            {
                if (Enumerable.Range(at, count).Any(position => taken[position]))
                {
                    continue;
                }

                for (var offset = 0; offset < count; offset++)
                {
                    taken[at + offset] = true;
                }

                mentions.Add(new V4ActMention(work, euAct, words[at].Start, words[at + count - 1].End));
            }
        }

        // An act named by its date in figures ("L. 27.07.1997", "loi du 27/07/97"): three numbers one sign apart that are
        // the date of exactly one act of the index.
        for (var at = 0; at + 2 < words.Count; at++)
        {
            if (taken[at] || taken[at + 1] || taken[at + 2] || words[at + 1].Start - words[at].End != 1 || words[at + 2].Start - words[at + 1].End != 1 ||
                !Figure(words[at].Text, 2, out var day) || !Figure(words[at + 1].Text, 2, out var month) || !(Figure(words[at + 2].Text, 4, out var year) && words[at + 2].Text.Length is 2 or 4))
            {
                continue;
            }

            if (words[at + 2].Text.Length == 2)
            {
                year += year > 30 ? 1900 : 2000;
            }

            var iso = string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}-{day:D2}");
            var dated = Acts.Where(act => act.Date == iso).ToList();
            if (dated.Count == 1)
            {
                taken[at] = taken[at + 1] = taken[at + 2] = true;
                mentions.Add(new V4ActMention(dated[0].Work, null, words[at].Start, words[at + 2].End));
            }
        }

        mentions.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return mentions;

        static bool Figure(string text, int most, out int value)
        {
            value = 0;
            return text.Length <= most && text.All(static letter => letter is >= '0' and <= '9') && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }

    private static V4CorpusIndex Load()
    {
        using var stream = typeof(V4CorpusIndex).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("The corpus index is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    /// <summary>Reads an index from its bytes, refusing a file that is not of the schema.</summary>
    public static V4CorpusIndex Read(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var document = JsonDocument.Parse(utf8);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != Schema)
        {
            throw new InvalidDataException("Not a lex-v4-corpus-index/1.");
        }

        static List<string> Strings(JsonElement element, string name) => element.GetProperty(name).EnumerateArray().Select(static item => item.GetString()!).ToList();
        var acts = root.GetProperty("acts").EnumerateArray().Select(static element => new V4IndexAct(
            element.GetProperty("work").GetString()!,
            element.GetProperty("publisher").GetString()!,
            element.GetProperty("kind").GetString()!,
            element.GetProperty("date").GetString()!,
            element.GetProperty("role").GetString()!,
            element.GetProperty("tier").GetString()!,
            element.GetProperty("domain").GetString()!,
            element.GetProperty("language").GetString()!,
            element.GetProperty("order").GetInt32(),
            Strings(element, "short_names"),
            element.GetProperty("summary_fr").GetString()!)).ToList();
        var euActs = root.GetProperty("eu_acts").EnumerateArray().Select(static element => new V4IndexEuAct(
            element.GetProperty("id").GetString()!,
            element.GetProperty("celex").GetString()!,
            element.GetProperty("name_fr").GetString()!,
            element.GetProperty("name_en").GetString()!,
            element.GetProperty("official").GetString()!,
            Strings(element, "short_names"))).ToList();
        if (acts.Select(static act => act.Work).Distinct(StringComparer.Ordinal).Count() != acts.Count)
        {
            throw new InvalidDataException("The corpus index names an act twice.");
        }

        var other = root.GetProperty("domains").EnumerateArray().SelectMany(static domain => Strings(domain, "other_domain_words")).ToList();
        var domain = root.GetProperty("domains").EnumerateArray().SelectMany(static entry => Strings(entry, "domain_words")).ToList();
        return new V4CorpusIndex(root.GetProperty("version").GetString()!, Convert.ToHexStringLower(SHA256.HashData(utf8)), acts, euActs, other, domain);
    }
}
