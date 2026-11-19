namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>
/// The retrieval brief (brick C's first view): what retrieval needs and nothing else. "Retrieval should be broad. Generation
/// should be selective." [6A]
/// </summary>
/// <param name="Concepts">The concepts to find, by id.</param>
/// <param name="Role">The role of the law the question names beside the concept (the actor), or null.</param>
/// <param name="Kind">The kind of answer expected: its pattern is one of the detectors.</param>
/// <param name="UsedKeywords">The rows of the dictionary the question's own words matched (a reader's word that matched is swept too).</param>
/// <param name="Literals">The question's own words that no table read, folded ("chaque", "annee"): found on a line, they count for it.</param>
/// <param name="Words">The question's own words (its matching copy), for the heading a question writes out whole.</param>
public sealed record V4Brief(
    IReadOnlyList<string> Concepts,
    string? Role,
    string Kind,
    IReadOnlyList<int> UsedKeywords,
    IReadOnlyList<string>? Literals = null,
    IReadOnlyList<V4Word>? Words = null);

/// <summary>
/// One candidate of the audit table (brick I): a line, the detectors that found it, the words that matched, its place in the
/// outline, and its score. The score orders candidates and is never shown: "what reaches production is the validated line,
/// never the score." [2D]
/// </summary>
public sealed class V4Candidate
{
    internal V4Candidate(V4Line line, V4Article article, List<string> detectors, List<int> keywords, double score, List<V4Value> values)
    {
        Line = line;
        Article = article;
        Detectors = detectors;
        Keywords = keywords;
        Score = score;
        Values = values;
    }

    public V4Line Line { get; }

    public V4Article Article { get; }

    /// <summary>title, keyword, cooccurrence, type_pattern, answer_words, definition: the detectors that found the line.</summary>
    public IReadOnlyList<string> Detectors { get; }

    /// <summary>The rows of the dictionary whose words are on the line.</summary>
    public IReadOnlyList<int> Keywords { get; }

    public double Score { get; }

    /// <summary>The typed values of the expected kind written on the line.</summary>
    public IReadOnlyList<V4Value> Values { get; }

    /// <summary>The role the arbiter gave it (primary, supporting, tangential, discarded), or null before it ran.</summary>
    public string? Role { get; set; }

    /// <summary>The arbiter's reason, one code of a closed list, or null.</summary>
    public string? Reason { get; set; }
}

/// <summary>What one detector found, counted: the plan lines show these numbers.</summary>
public sealed record V4DetectorCount(string Id, bool Ran, int Lines, int Articles, string? SkippedBecause);

/// <summary>One row of the sweep (brick I, step 5): a keyword and every line of the act that holds it. "NOT top-k. NOT scored." [B3]</summary>
public sealed record V4SweepRow(int Keyword, string Concept, int Lines, int Articles);

/// <summary>
/// The result object of retrieval (brick I): the candidates in order, the detectors that ran and those skipped with the
/// reason, the sweep, and whether one line clearly leads. "Persist this object and you can re-run generation later, on the
/// same retrieval, without paying for retrieval again." [7C]
/// </summary>
public sealed class V4RetrievalResult
{
    internal V4RetrievalResult(
        V4ActTables tables,
        List<V4Candidate> candidates,
        List<V4DetectorCount> detectors,
        List<V4SweepRow> sweep,
        List<string> titleArticles,
        bool clearLead)
    {
        Tables = tables;
        Candidates = candidates;
        Detectors = detectors;
        Sweep = sweep;
        TitleArticles = titleArticles;
        ClearLead = clearLead;
    }

    public V4ActTables Tables { get; }

    /// <summary>Every candidate, the best first.</summary>
    public IReadOnlyList<V4Candidate> Candidates { get; }

    public IReadOnlyList<V4DetectorCount> Detectors { get; }

    public IReadOnlyList<V4SweepRow> Sweep { get; }

    /// <summary>The articles whose own heading holds a word of the concept, in the act's order.</summary>
    public IReadOnlyList<string> TitleArticles { get; }

    /// <summary>Whether the first candidate clearly leads and holds the expected form (the margin of the fast route).</summary>
    public bool ClearLead { get; }

    /// <summary>The candidates sent to a model: the best lines, at most two of one article, so the cases of a rule are not crowded out.</summary>
    public List<V4Candidate> Shortlist(int most)
    {
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);
        var list = new List<V4Candidate>();
        foreach (var candidate in Candidates)
        {
            var count = taken.GetValueOrDefault(candidate.Article.Id);
            if (count >= 2)
            {
                continue;
            }

            taken[candidate.Article.Id] = count + 1;
            list.Add(candidate);
            if (list.Count == most)
            {
                break;
            }
        }

        // The first paragraph under a heading that names the concept is kept with its article, whatever its own score: it
        // states what the others qualify ("La durée du contrat est fixée par les parties.").
        foreach (var lead in Candidates.Where(static candidate => candidate.Detectors.Contains("article_lead")))
        {
            if (list.Contains(lead) || !list.Any(candidate => candidate.Article.Id == lead.Article.Id))
            {
                continue;
            }

            var last = list.FindLastIndex(candidate => candidate.Article.Id == lead.Article.Id);
            if (list.Count(candidate => candidate.Article.Id == lead.Article.Id) >= 2)
            {
                list[last] = lead;
            }
            else if (list.Count < most)
            {
                list.Insert(last + 1, lead);
            }
        }

        return list.OrderByDescending(static candidate => candidate.Score).ThenBy(static candidate => candidate.Article.Position).ThenBy(static candidate => candidate.Line.Index).ToList();
    }
}

/// <summary>
/// Retrieval as filtering on two tables (brick I): "retrieval is a filtering problem on two structured tables (line_df and
/// toc_df), not a search problem." [7A] Every detector is code. What counts is words sitting together: "the point is
/// co-occurrence, not frequency." [7B]
/// </summary>
public static class V4Retrieval
{
    /// <summary>The margin the first candidate must hold over the second for the fast route. Tuned on the insurance questions; "The threshold is not a universal constant." [9ter]</summary>
    public const double Margin = 3.0;

    /// <summary>Runs the detectors of a brief on one act's tables and fills the audit table.</summary>
    /// <summary>Where <paramref name="words"/> hold <paramref name="run"/> as one unbroken run: the first word's place, or -1.</summary>
    private static int RunAt(List<string> words, List<string> run)
    {
        for (var at = 0; at + run.Count <= words.Count; at++)
        {
            var same = true;
            for (var offset = 0; offset < run.Count && same; offset++)
            {
                same = string.Equals(words[at + offset], run[offset], StringComparison.Ordinal);
            }

            if (same)
            {
                return at;
            }
        }

        return -1;
    }

    private static bool Holds(List<string> words, List<string> run) => RunAt(words, run) >= 0;

    /// <summary>
    /// The headings of the act (of two words or more) that the question writes out whole, each with where its words stand
    /// in the question: the article, the first word and how many.
    /// </summary>
    public static List<(string Article, int First, int Count)> HeadingsWrittenOut(V4ActTables tables, IReadOnlyList<V4Word> words)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(words);
        var said = words.Select(static word => V4Text.Stem(word.Text)).ToList();
        var found = new List<(string, int, int)>();
        foreach (var article in tables.Articles.Where(static article => article.HeadingWords.Count >= 2))
        {
            var at = RunAt(said, article.HeadingWords.Select(static word => V4Text.Stem(word.Text)).ToList());
            if (at >= 0)
            {
                found.Add((article.Id, at, article.HeadingWords.Count));
            }
        }

        return found;
    }

    public static V4RetrievalResult Run(V4Brief brief, V4ActTables tables, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(dictionary);
        var concepts = brief.Concepts.Select(dictionary.Concept).Where(static concept => concept is not null).Select(static concept => concept!).ToList();
        var search = concepts.SelectMany(concept => dictionary.KeywordsOf(concept.Id, "law", "reader")).ToList();
        var answerWords = concepts.SelectMany(concept => dictionary.KeywordsOf(concept.Id, "answer")).ToList();
        var roleKeys = brief.Role is null ? [] : dictionary.KeywordsOf(brief.Role, "law").ToList();

        // The other parties the law names: a line whose own verb has one of them as its subject states that party's right.
        var otherRoleKeys = brief.Role is null
            ? []
            : dictionary.Concepts.Where(concept => concept.Kind == "role" && concept.Id != brief.Role).SelectMany(concept => dictionary.KeywordsOf(concept.Id, "law")).ToList();
        var valueRequired = concepts.Any(static concept => concept.ValueRequired);
        var valueKind = brief.Kind is "duration" or "date" or "amount";

        // The outline first (the title detector): "Trust the TOC on exact title matches. The author wrote that title." [7C]
        var lawKeys = search.Where(static keyword => keyword.Word == "law").ToList();
        var titleArticles = tables.Articles
            .Where(article => article.HeadingWords.Count > 0 && lawKeys.Any(keyword => keyword.Key.In(article.HeadingWords)))
            .Select(static article => article.Id)
            .ToList();
        var titled = titleArticles.ToHashSet(StringComparer.Ordinal);

        // The heading that holds the very words the question used ("absence de bénéficiaire") names its article, not a
        // neighbour's.
        var usedKeys = lawKeys.Where(keyword => brief.UsedKeywords.Contains(keyword.Row)).ToList();
        var named = tables.Articles
            .Where(article => article.HeadingWords.Count > 0 && usedKeys.Any(keyword => keyword.Key.In(article.HeadingWords)))
            .Select(static article => article.Id)
            .ToHashSet(StringComparer.Ordinal);

        // "Trust the TOC on exact title matches. The author wrote that title." [7C] A heading that is, word for word, the
        // key the question used ("Absence de bénéficiaire", "Délai de prescription") is the article asked for.
        var exact = tables.Articles
            .Where(article => usedKeys.Any(keyword => keyword.Key.Length == article.HeadingWords.Count && keyword.Key.Occurrences(article.HeadingWords).Any(static occurrence => occurrence.First == 0)))
            .Select(static article => article.Id)
            .ToHashSet(StringComparer.Ordinal);

        // The question writes an article's heading out whole ("en cas d'erreur sur l'âge de l'assuré"): the author wrote
        // that title and the reader repeated it, so the article under it is the one asked for, whatever else its lines hold.
        var said = (brief.Words ?? []).Select(static word => V4Text.Stem(word.Text)).ToList();
        var quoted = tables.Articles
            .Where(article => article.HeadingWords.Count >= 2 && Holds(said, article.HeadingWords.Select(static word => V4Text.Stem(word.Text)).ToList()))
            .Select(static article => article.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (quoted.Count > 1)
        {
            // Several headings written out: the one whose words the act writes least is what is asked ("erreur sur l'âge de
            // l'assuré"); a heading the act repeats on many lines says where ("dans une assurance sur la vie").
            var stems = tables.Lines.Select(static line => line.Words.Select(static word => V4Text.Stem(word.Text)).ToList()).ToList();
            var written = quoted.ToDictionary(
                static id => id,
                id =>
                {
                    var run = tables.Article(id)!.HeadingWords.Select(static word => V4Text.Stem(word.Text)).ToList();
                    return stems.Count(line => Holds(line, run));
                },
                StringComparer.Ordinal);
            var least = written.Values.Min();
            quoted = written.Where(pair => pair.Value == least).Select(static pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        }

        // A heading that also names another concept of the dictionary ("Résiliation après sinistre", "Mise en demeure et
        // faculté de résiliation") is a special case of the one asked; the heading that names it alone is the general one.
        var asked = concepts.Select(static concept => concept.Id).Append(brief.Role ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        var otherKeys = dictionary.Keywords.Where(keyword => keyword.Word == "law" && keyword.Key.Length >= 2 && !asked.Contains(keyword.Concept) && dictionary.Concept(keyword.Concept)!.Kind != "role").ToList();
        var special = tables.Articles
            .Where(article => titled.Contains(article.Id) && otherKeys.Any(keyword => keyword.Key.In(article.HeadingWords)) &&
                !lawKeys.Any(keyword => keyword.Key.Length >= 2 && keyword.Key.In(article.HeadingWords)))
            .Select(static article => article.Id)
            .ToHashSet(StringComparer.Ordinal);

        // Two concepts asked together ("résilier" and "prime impayée") meet in the article that holds both.
        var both = new HashSet<string>(StringComparer.Ordinal);
        var bothInHeading = new HashSet<string>(StringComparer.Ordinal);
        if (concepts.Count >= 2)
        {
            foreach (var article in tables.Articles)
            {
                var lines = tables.LinesOf(article).Where(static line => line.Kind != "label").ToList();
                IEnumerable<V4Keyword> Keys(V4Concept concept) => dictionary.KeywordsOf(concept.Id, "law", "reader")
                    .Where(keyword => keyword.Word == "law" || brief.UsedKeywords.Contains(keyword.Row));
                if (concepts.All(concept => Keys(concept).Any(keyword => lines.Any(line => keyword.Key.In(line.Words)))))
                {
                    both.Add(article.Id);
                }

                // The heading itself names both ("Mise en demeure et faculté de résiliation"): the article is about them together.
                if (article.HeadingWords.Count > 0 && concepts.All(concept => Keys(concept).Any(keyword => keyword.Key.In(article.HeadingWords))))
                {
                    bothInHeading.Add(article.Id);
                }
            }
        }

        var unnamed = tables.References.Where(static reference => reference.Kind == "unnamed_cases").Select(static reference => reference.FromLine).ToHashSet(StringComparer.Ordinal);
        var literals = (brief.Literals ?? []).Where(static word => word.Length >= 5).Select(V4Text.Stem).Distinct(StringComparer.Ordinal).ToList();
        var candidates = new List<V4Candidate>();
        var counts = new Dictionary<string, (int Lines, HashSet<string> Articles)>(StringComparer.Ordinal)
        {
            ["keyword"] = (0, new(StringComparer.Ordinal)),
            ["cooccurrence"] = (0, new(StringComparer.Ordinal)),
            ["type_pattern"] = (0, new(StringComparer.Ordinal)),
            ["answer_words"] = (0, new(StringComparer.Ordinal)),
            ["lexicon"] = (0, new(StringComparer.Ordinal)),
        };
        void Count(string detector, V4Line line)
        {
            var (lines, articles) = counts[detector];
            articles.Add(line.Article);
            counts[detector] = (lines + 1, articles);
        }

        foreach (var article in tables.Articles)
        {
            if (article.Status == "empty")
            {
                continue;
            }

            var inQuoted = quoted.Contains(article.Id);
            var inTitled = titled.Contains(article.Id) || inQuoted;
            var leadLine = tables.TextLinesOf(article).FirstOrDefault();
            foreach (var line in tables.TextLinesOf(article))
            {
                var matched = search.Where(keyword => keyword.Key.In(line.Words)).ToList();
                var role = roleKeys.Count > 0 && roleKeys.Any(keyword => keyword.Key.In(line.Words));
                if (role)
                {
                    Count("lexicon", line);
                }

                var answers = answerWords.Any(keyword => keyword.Key.In(line.Words));
                var typed = V4Patterns.HasPattern(brief.Kind) && V4Patterns.Confirms(brief.Kind, line.Folded);
                var values = valueKind ? V4Patterns.Values(brief.Kind, line.Folded) : [];
                var hasValue = values.Count > 0;
                var holds = matched.Count > 0;

                // A line is a candidate when it holds a word of the concept; or when it stands under a heading that does, and
                // has the form of the answer or the words the law answers with (the rule often sits under the heading without
                // repeating its word).
                var leads = ReferenceEquals(line, leadLine);
                if (!holds && !(inTitled && (typed || answers)) && !(inQuoted && leads))
                {
                    continue;
                }

                var detectors = new List<string>();
                var score = 0.0;
                if (holds)
                {
                    detectors.Add("keyword");
                    Count("keyword", line);
                    score += matched.Any(static keyword => keyword.Word == "law") ? 3.0 : 2.0;
                }

                if (inTitled)
                {
                    detectors.Add("title");
                    score += exact.Contains(article.Id) || inQuoted ? 6.0 : named.Contains(article.Id) ? 4.0 : 2.0;
                    if (inQuoted && leads)
                    {
                        detectors.Add("heading_quoted");
                        score += 5.0;
                    }

                    if (special.Contains(article.Id) && !bothInHeading.Contains(article.Id))
                    {
                        detectors.Add("special_heading");
                        score -= 1.5;
                    }

                    // The first paragraph under a heading that names the concept states the article's own rule.
                    if ((holds || inQuoted) && leads)
                    {
                        detectors.Add("article_lead");
                        score += 1.5;
                    }
                }

                if (holds && bothInHeading.Contains(article.Id))
                {
                    detectors.Add("concepts_together");
                    score += 2.5;
                }
                else if (holds && both.Contains(article.Id))
                {
                    detectors.Add("concepts_in_article");
                    score += 1.0;
                }

                if (holds && role)
                {
                    detectors.Add("cooccurrence");
                    Count("cooccurrence", line);
                    score += 2.0;
                }
                else if (holds && valueRequired && hasValue)
                {
                    detectors.Add("cooccurrence");
                    Count("cooccurrence", line);
                    score += 1.0;
                }

                if (typed)
                {
                    detectors.Add("type_pattern");
                    if (holds)
                    {
                        Count("type_pattern", line);
                    }

                    score += 2.0;
                }
                else if (valueKind)
                {
                    // Rule 5: a concept that needs a value counts only next to a value.
                    score -= valueRequired ? 2.0 : 1.0;
                }

                if (answers)
                {
                    detectors.Add("answer_words");
                    Count("answer_words", line);
                    score += 1.5;
                }

                // The question's own literal words on the line ("chaque année"): direct keywords, beside the dictionary's.
                var literal = holds ? literals.Count(stem => line.Words.Any(word => word.Text.Length >= 4 && V4Text.Stem(word.Text) == stem)) : 0;
                if (literal > 0)
                {
                    detectors.Add("literal_words");
                    score += 1.5 * Math.Min(literal, 2);
                }

                // The role is the subject of the rule's own verb, right before the concept ("l'assureur ne peut résilier"):
                // the line answers "[role] peut [concept] ?" in its own words.
                if (holds && role && brief.Kind is "rule" or "list" && DirectForm(line, roleKeys, matched))
                {
                    detectors.Add("direct_form");
                    score += 1.5;
                }
                else if (holds && brief.Kind is "rule" or "list" && otherRoleKeys.Count > 0 && DirectForm(line, otherRoleKeys, matched))
                {
                    // "le preneur d'assurance a le droit de résilier": the rule of another party than the one asked about.
                    detectors.Add("other_party");
                    score -= 2.0;
                }

                // A line that sends to "the cases provided by the law" states the general rule, by its own words.
                if (holds && role && brief.Kind is "rule" or "list" && unnamed.Contains(line.Id))
                {
                    detectors.Add("general_rule");
                    score += 3.0;
                }

                candidates.Add(new V4Candidate(line, article, detectors, matched.Select(static keyword => keyword.Row).ToList(), score, values));
            }
        }

        candidates = candidates
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Article.Position)
            .ThenBy(static candidate => candidate.Line.Index)
            .ToList();

        var clear = candidates.Count > 0
            && candidates[0].Detectors.Contains("keyword")
            && (!V4Patterns.HasPattern(brief.Kind) || candidates[0].Detectors.Contains("type_pattern"))
            && (!valueKind || candidates[0].Values.Count == 1)
            && (candidates.Count == 1 || candidates[0].Score - candidates[1].Score >= Margin);

        var detectorCounts = new List<V4DetectorCount>
        {
            new("title", true, 0, titleArticles.Count, null),
            new("keyword", true, counts["keyword"].Lines, counts["keyword"].Articles.Count, null),
            roleKeys.Count > 0 || valueRequired
                ? new("cooccurrence", true, counts["cooccurrence"].Lines, counts["cooccurrence"].Articles.Count, null)
                : new("cooccurrence", false, 0, 0, "no_actor_and_no_value"),
            V4Patterns.HasPattern(brief.Kind)
                ? new("type_pattern", true, counts["type_pattern"].Lines, counts["type_pattern"].Articles.Count, null)
                : new("type_pattern", false, 0, 0, "kind_has_no_pattern"),
            answerWords.Count > 0
                ? new("answer_words", true, counts["answer_words"].Lines, counts["answer_words"].Articles.Count, null)
                : new("answer_words", false, 0, 0, "concept_lists_none"),
            roleKeys.Count > 0
                ? new("lexicon", true, counts["lexicon"].Lines, counts["lexicon"].Articles.Count, null)
                : new("lexicon", false, 0, 0, "no_actor"),
            new("embeddings", false, 0, 0, "off_by_default"),
        };

        return new V4RetrievalResult(tables, candidates, detectorCounts, Sweep(brief, tables, dictionary), titleArticles, clear);
    }

    private static readonly HashSet<string> Modals = new(StringComparer.Ordinal)
    {
        "peut", "peuvent", "doit", "doivent", "a", "ont", "ne", "n", "pas", "le", "la", "droit", "de", "du", "dispose", "disposent", "faculte", "est", "sont", "tenu", "tenus", "libre",
    };

    /// <summary>Whether, on a line, the role is followed within a few words by a modal and then by the concept's own word.</summary>
    private static bool DirectForm(V4Line line, List<V4Keyword> roleKeys, List<V4Keyword> matched)
    {
        foreach (var roleKey in roleKeys)
        {
            foreach (var (first, count) in roleKey.Key.Occurrences(line.Words))
            {
                var after = first + count;
                var modal = false;
                var at = after;
                while (at < Math.Min(after + 6, line.Words.Count) && Modals.Contains(line.Words[at].Text))
                {
                    modal |= line.Words[at].Text is "peut" or "peuvent" or "doit" or "doivent" or "droit" or "faculte" or "tenu" or "tenus" or "libre";
                    at++;
                }

                if (modal && matched.Any(keyword => keyword.Key.Occurrences(line.Words).Any(occurrence => occurrence.First == at)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The sweep (step 5): for each concept, every one of the law's words for it and every reader's word the question used,
    /// counted on all the lines of the act. For a list it is the candidate set; for an absence it is the proof.
    /// </summary>
    public static List<V4SweepRow> Sweep(V4Brief brief, V4ActTables tables, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(dictionary);
        var rows = new List<V4SweepRow>();
        foreach (var conceptId in brief.Concepts)
        {
            foreach (var keyword in dictionary.KeywordsOf(conceptId, "law", "reader"))
            {
                if (keyword.Word == "reader" && !(keyword.Language == "fr" && brief.UsedKeywords.Contains(keyword.Row)))
                {
                    continue;
                }

                var lines = 0;
                var articles = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in tables.Lines)
                {
                    if (line.Kind != "label" && keyword.Key.In(line.Words))
                    {
                        lines++;
                        articles.Add(line.Article);
                    }
                }

                rows.Add(new V4SweepRow(keyword.Row, conceptId, lines, articles.Count));
            }
        }

        return rows;
    }

    /// <summary>How many lines of the act hold a word (by its stem): a reader's word that is nowhere in the act is reported as such.</summary>
    public static int LinesHolding(V4ActTables tables, string foldedWord)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(foldedWord);
        var stem = V4Text.Stem(foldedWord);
        return tables.Lines.Count(line => line.Kind != "label" && line.Words.Any(word => word.Text == foldedWord || (word.Text.Length >= 4 && V4Text.Stem(word.Text) == stem)));
    }

    /// <summary>
    /// The context of an anchor (step 4), sized by the kind of answer: the lines of the same numbered point for a rule or a
    /// value, the whole article for a list, never more than <paramref name="most"/> lines. "the context's range always
    /// contains the anchor's range." [7C]
    /// </summary>
    public static List<V4Line> ContextOf(V4ActTables tables, V4Line anchor, string kind, int most)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(anchor);
        var article = tables.Article(anchor.Article)!;
        var lines = tables.TextLinesOf(article).ToList();
        var chosen = kind == "list"
            ? lines
            : lines.Where(line => line.Point == anchor.Point || Math.Abs(line.Index - anchor.Index) <= 1).ToList();
        if (chosen.Count > most)
        {
            var at = chosen.FindIndex(line => line.Index == anchor.Index);
            var from = Math.Clamp(at - (most / 2), 0, chosen.Count - most);
            chosen = chosen.GetRange(from, most);
        }

        return chosen;
    }

    /// <summary>The terms an act defines that a question's words name, the longest term first: each with the lines that define it.</summary>
    public static List<(V4Definition Definition, int Start, int End)> DefinedTerms(V4ActTables tables, IReadOnlyList<V4Word> words)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(words);
        var found = new List<(V4Definition, int, int)>();
        var taken = new bool[words.Count];

        // A term of one word that is also a common word of questions ("compte", "prime") is read only at its own length.
        foreach (var definition in tables.Definitions.OrderByDescending(static definition => definition.TermFolded.Length))
        {
            var key = V4Key.Read(definition.TermFolded);
            if (key is null)
            {
                continue;
            }

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

                found.Add((definition, words[at].Start, words[at + count - 1].End));
            }
        }

        found.Sort(static (left, right) => left.Item2.CompareTo(right.Item2));
        return found;
    }
}
