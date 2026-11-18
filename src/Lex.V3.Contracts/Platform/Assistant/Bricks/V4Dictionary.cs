using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One concept of the dictionary (brick D, the table <c>concepts</c>).</summary>
/// <param name="Kind">concept, or role (a party the law names: a closed set, typed in whole).</param>
/// <param name="Definition">One sentence, so a model can choose between two concepts that share a word. Never shown to a reader.</param>
/// <param name="ValueRequired">A line counts for this concept only next to a value (rule 5: prescription, rétractation).</param>
/// <param name="Complete">Whether the word set is judged complete: only then may zero hits support "the act does not use these terms".</param>
public sealed record V4Concept(
    string Id,
    string Kind,
    string LabelFr,
    string LabelEn,
    string Definition,
    IReadOnlyList<string> HomeActs,
    IReadOnlyList<string> UsualAnswer,
    bool ValueRequired,
    bool Complete);

/// <summary>One keyword of a concept (brick D, the table <c>concept_keywords</c>).</summary>
/// <param name="Row">The row's number in the file's order: what a trace names instead of the words.</param>
/// <param name="Word">law (the act's own word), reader (a reader's word) or answer (how the law answers).</param>
/// <param name="Priority">primary for the law's word, secondary for a reader's.</param>
/// <param name="Source">law_text or proposal.</param>
public sealed record V4Keyword(
    int Row,
    string Concept,
    string Language,
    string Keyword,
    string Word,
    string Priority,
    double Weight,
    bool Pattern,
    string Source,
    V4Key Key);

/// <summary>One kind of answer (brick D, the table <c>answer_types</c>): its shape, the context it needs and the cues that ask for it.</summary>
public sealed record V4AnswerType(string Id, string Shape, string Context, IReadOnlyList<V4Key> Cues);

/// <summary>A keyword of the dictionary found in a text: which row, and which words of the text it covers.</summary>
public sealed record V4DictionaryHit(V4Keyword Keyword, int FirstWord, int WordCount, int Start, int End);

/// <summary>
/// The expert dictionary for private insurance (brick D), <c>lex-v4-dictionary/1</c>, read from the bytes committed beside this
/// file. Two tables, concepts and their keywords, and the kinds of answer. "The dictionary could be written in an afternoon by
/// sitting with the underwriter." [M1] Here the first rows are the law's own words, counted on the acts; the readers' words are
/// proposals until a person validates them, which the file says of itself.
/// </summary>
public sealed class V4Dictionary
{
    public const string Schema = "lex-v4-dictionary/1";
    private const string Resource = "Lex.V3.Contracts.Assistant.Bricks.InsuranceDictionary.json";

    /// <summary>The order in which the kinds of answer are tried on a question's cues: the narrower kind first.</summary>
    public static readonly ReadOnlyCollection<string> KindOrder = new(["definition", "duration", "amount", "reference", "list", "rule", "date", "text"]);

    private readonly Dictionary<string, V4Concept> _concepts;
    private readonly HashSet<string> _terms;
    private readonly V4Keyword[] _lookup;

    private V4Dictionary(
        string version,
        string sha256,
        List<V4Concept> concepts,
        List<V4Keyword> keywords,
        List<V4AnswerType> types,
        List<string> shapes,
        List<(V4Key Short, string Long)> abbreviations)
    {
        Version = version;
        Sha256 = sha256;
        Concepts = concepts.AsReadOnly();
        Keywords = keywords.AsReadOnly();
        AnswerTypes = types.AsReadOnly();
        AnswerShapes = shapes.AsReadOnly();
        Abbreviations = abbreviations.AsReadOnly();
        _concepts = concepts.ToDictionary(static concept => concept.Id, StringComparer.Ordinal);
        _terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in concepts)
        {
            _terms.Add(concept.LabelFr);
            _terms.Add(concept.LabelEn);
        }

        foreach (var keyword in keywords)
        {
            _terms.Add(keyword.Keyword);
        }

        // Rule: the longest key first, so a phrase is read whole before any of its words is read alone.
        _lookup = keywords.Where(static keyword => keyword.Word != "answer")
            .OrderByDescending(static keyword => keyword.Key.Length)
            .ThenByDescending(static keyword => keyword.Keyword.Length)
            .ThenBy(static keyword => keyword.Row)
            .ToArray();
    }

    /// <summary>The insurance dictionary this build carries.</summary>
    public static V4Dictionary Insurance { get; } = Load();

    public string Version { get; }

    /// <summary>The SHA-256 of the file's bytes: what a trace records, so an answer names the dictionary it was made with.</summary>
    public string Sha256 { get; }

    public ReadOnlyCollection<V4Concept> Concepts { get; }

    public ReadOnlyCollection<V4Keyword> Keywords { get; }

    public ReadOnlyCollection<V4AnswerType> AnswerTypes { get; }

    public ReadOnlyCollection<string> AnswerShapes { get; }

    public ReadOnlyCollection<(V4Key Short, string Long)> Abbreviations { get; }

    public V4Concept? Concept(string id) => _concepts.GetValueOrDefault(id);

    public V4Keyword? Keyword(int row) => row >= 0 && row < Keywords.Count ? Keywords[row] : null;

    public V4AnswerType? AnswerType(string id) => AnswerTypes.FirstOrDefault(type => type.Id == id);

    /// <summary>The keywords of a concept of the given kinds of word, in the file's order.</summary>
    public IEnumerable<V4Keyword> KeywordsOf(string concept, params string[] words) =>
        Keywords.Where(keyword => keyword.Concept == concept && (words.Length == 0 || words.Contains(keyword.Word, StringComparer.Ordinal)));

    /// <summary>Whether a text is a term this dictionary writes (a concept's label or a keyword), exactly: what a sentence may show as one.</summary>
    public bool IsTerm(string value) => value is not null && _terms.Contains(value);

    /// <summary>
    /// The dictionary lookup (brick C, step 3; "pandas filter, no model" [6C]): the keys found in a text's words, the longest
    /// key first, on word bounds, each word of the text read by one key at most.
    /// </summary>
    public List<V4DictionaryHit> Lookup(IReadOnlyList<V4Word> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var taken = new bool[words.Count];
        var hits = new List<V4DictionaryHit>();
        foreach (var keyword in _lookup)
        {
            foreach (var (at, count) in keyword.Key.Occurrences(words))
            {
                var free = true;
                for (var offset = 0; offset < count && free; offset++)
                {
                    free = !taken[at + offset];
                }

                if (!free)
                {
                    continue;
                }

                for (var offset = 0; offset < count; offset++)
                {
                    taken[at + offset] = true;
                }

                hits.Add(new V4DictionaryHit(keyword, at, count, words[at].Start, words[at + count - 1].End));
            }
        }

        hits.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return hits;
    }

    private static V4Dictionary Load()
    {
        using var stream = typeof(V4Dictionary).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("The insurance dictionary is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    /// <summary>Reads a dictionary from its bytes, refusing a file that is not of the schema or names a concept twice.</summary>
    public static V4Dictionary Read(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var document = JsonDocument.Parse(utf8);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != Schema)
        {
            throw new InvalidDataException("Not a lex-v4-dictionary/1.");
        }

        var concepts = new List<V4Concept>();
        var keywords = new List<V4Keyword>();
        foreach (var element in root.GetProperty("concepts").EnumerateArray())
        {
            var id = element.GetProperty("id").GetString()!;
            if (id.Length == 0 || !id.All(static character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_') || concepts.Any(concept => concept.Id == id))
            {
                throw new InvalidDataException($"The concept '{id}' is not a lower-case token, or is named twice.");
            }

            concepts.Add(new V4Concept(
                id,
                element.GetProperty("kind").GetString()!,
                element.GetProperty("label_fr").GetString()!,
                element.GetProperty("label_en").GetString()!,
                element.GetProperty("definition").GetString()!,
                Strings(element, "home_acts"),
                Strings(element, "usual_answer"),
                element.GetProperty("value_required").GetBoolean(),
                element.GetProperty("complete").GetBoolean()));
            foreach (var (member, language, word, priority, weight, source) in new[]
                     {
                         ("law_fr", "fr", "law", "primary", 1.0, "law_text"),
                         ("reader_fr", "fr", "reader", "secondary", 0.6, "proposal"),
                         ("reader_en", "en", "reader", "secondary", 0.6, "proposal"),
                         ("answer_fr", "fr", "answer", "secondary", 0.8, "law_text"),
                     })
            {
                foreach (var keyword in Strings(element, member))
                {
                    var key = V4Key.Read(keyword) ?? throw new InvalidDataException($"The keyword '{keyword}' of '{id}' holds no word.");
                    keywords.Add(new V4Keyword(keywords.Count, id, language, keyword, word, priority, weight, keyword.Contains('*', StringComparison.Ordinal), source, key));
                }
            }
        }

        var types = new List<V4AnswerType>();
        foreach (var element in root.GetProperty("answer_types").EnumerateArray())
        {
            var cues = Strings(element, "cues_fr").Concat(Strings(element, "cues_en")).Select(V4Key.Read).Where(static key => key is not null).Select(static key => key!).ToList();
            types.Add(new V4AnswerType(element.GetProperty("id").GetString()!, element.GetProperty("shape").GetString()!, element.GetProperty("context").GetString()!, cues));
        }

        var shapes = Strings(root, "answer_shapes").ToList();
        if (types.Any(type => !shapes.Contains(type.Shape, StringComparer.Ordinal)) || KindOrder.Any(kind => types.All(type => type.Id != kind)))
        {
            throw new InvalidDataException("An answer type names a shape outside the closed list, or a kind of answer has no row.");
        }

        if (concepts.Any(concept => concept.UsualAnswer.Any(kind => !KindOrder.Contains(kind))))
        {
            throw new InvalidDataException("A concept names a kind of answer the table does not hold.");
        }

        var abbreviations = root.GetProperty("abbreviations").EnumerateArray()
            .Select(static element => (V4Key.Read(element.GetProperty("short").GetString()!)!, element.GetProperty("long").GetString()!))
            .ToList();
        return new V4Dictionary(
            root.GetProperty("version").GetString()!,
            Convert.ToHexStringLower(SHA256.HashData(utf8)),
            concepts,
            keywords,
            types,
            shapes,
            abbreviations);
    }

    private static List<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(static item => item.GetString()!).ToList()
            : [];
}
