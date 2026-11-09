using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>A run of the question the intake masked: an e-mail, a phone number, an IBAN or a national number.</summary>
public sealed record V3MaskedRun(int Start, int End);

/// <summary>
/// The question as the pipeline holds it: NFC, at most <see cref="V3AssistantParsers.MaximumQuestionLength"/> UTF-16 units,
/// and its masked copy, the only text a planner is given (R33: code strips obvious identifiers first). The mask replaces each
/// character of a run with U+2022, so every offset reads the same in both.
/// </summary>
public sealed record V3QuestionIntake(string Text, string Masked, ReadOnlyCollection<V3MaskedRun> Masks)
{
    public bool Touches(int start, int end) => Masks.Any(run => start < run.End && run.Start < end);
}

/// <summary>A civil date the parser read in the question, named by an opaque reference (<c>d1</c>, <c>d2</c>, ...).</summary>
public sealed record V3ParsedDate(string Ref, int Start, int End, string Iso);

/// <summary>Two dates the question joins as a range, named <c>r1</c>, ...; a plan reads <c>r1.from</c> and <c>r1.to</c>.</summary>
public sealed record V3ParsedRange(string Ref, int Start, int End, string From, string To);

/// <summary>A function cue (R15) the parser found in a part of the question, with where it is.</summary>
public sealed record V3ApplicationCue(V3ApplicationFunction Function, int Start, int End);

/// <summary>
/// The deterministic parsers that replace a model router (R34): the intake and its mask, the question's language, its dates
/// and ranges, and the function cues of the advice boundary (R15). No model is consulted and nothing is guessed: what the
/// closed lists do not read is not read.
/// </summary>
public static class V3AssistantParsers
{
    /// <summary>The longest question the assistant reads, in UTF-16 units (R45(f): question text is untrusted).</summary>
    public const int MaximumQuestionLength = 1000;

    private const char MaskCharacter = '•';
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex[] IdentifierPatterns =
    [
        // An e-mail address.
        new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", Options, Timeout),
        // An IBAN, grouped or not (LU28 0019 4006 4475 0000).
        new(@"(?<![A-Za-z0-9])[A-Z]{2}\d{2}(?: ?[A-Z0-9]{4}){3,7}(?: ?[A-Z0-9]{1,4})?(?![A-Za-z0-9])", Options, Timeout),
        // A phone number in international form.
        new(@"(?<![\w/])(?:\+|00)[1-9][\d ./\-]{6,18}\d(?!\w)", Options, Timeout),
        // A national number of 13 digits, grouped 4, 4, 5 or not.
        new(@"(?<!\d)\d{4} ?\d{4} ?\d{5}(?!\d)", Options, Timeout),
        // A matricule-like run of 11 to 13 digits, alone (a national, social security or identity number).
        new(@"(?<![\d\-/.])\d{11,13}(?![\d\-/.])", Options, Timeout),
        // A Luxembourg phone number written in three groups of three.
        new(@"(?<![\d\-])\d{3} \d{3} \d{3}(?![\d\-])", Options, Timeout),
    ];

    /// <summary>
    /// The question admitted, or null when it is blank or longer than <see cref="MaximumQuestionLength"/>. The text is put in
    /// Unicode normalisation form C first, so every offset a plan names is an offset of the NFC text.
    /// </summary>
    public static V3QuestionIntake? Intake(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var text = question.Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumQuestionLength)
        {
            return null;
        }

        var runs = new List<V3MaskedRun>();
        foreach (var pattern in IdentifierPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                runs.Add(new V3MaskedRun(match.Index, match.Index + match.Length));
            }
        }

        var masked = text.ToCharArray();
        foreach (var run in runs)
        {
            for (var at = run.Start; at < run.End; at++)
            {
                masked[at] = MaskCharacter;
            }
        }

        // Runs two patterns both read (a phone number's groups inside its international form) are one run.
        var merged = new List<V3MaskedRun>();
        foreach (var run in runs.OrderBy(static run => run.Start).ThenBy(static run => run.End))
        {
            if (merged.Count > 0 && run.Start <= merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, run.End) };
            }
            else
            {
                merged.Add(run);
            }
        }

        return new V3QuestionIntake(text, new string(masked), merged.AsReadOnly());
    }

    // ---- Language ---------------------------------------------------------------------------------------------------

    private static readonly Dictionary<V3QuestionLanguage, HashSet<string>> LanguageWords = new()
    {
        [V3QuestionLanguage.French] = new(StringComparer.Ordinal)
        {
            "le", "la", "les", "des", "du", "est", "une", "dans", "pour", "sur", "pas", "ne", "au", "aux", "cette", "ces", "mon",
            "ma", "mes", "quel", "quelle", "quels", "quelles", "comment", "pourquoi", "dit", "disait", "loi", "selon", "peut",
            "je", "il", "elle", "nous", "vous", "avec", "qui", "quoi", "et", "l'", "d'", "qu'", "que", "puis", "dois", "être",
        },
        [V3QuestionLanguage.English] = new(StringComparer.Ordinal)
        {
            "the", "is", "are", "was", "were", "what", "which", "who", "how", "does", "do", "did", "say", "says", "said", "law",
            "of", "in", "on", "at", "for", "to", "and", "or", "my", "can", "could", "should", "may", "this", "that", "these",
            "from", "with", "between", "under", "about", "i", "be",
        },
        [V3QuestionLanguage.German] = new(StringComparer.Ordinal)
        {
            "der", "die", "das", "und", "ist", "was", "wie", "welche", "welcher", "gesetz", "gesetzes", "nach", "von", "vom",
            "zum", "zur", "im", "ein", "eine", "einen", "nicht", "mit", "sagt", "darf", "ich", "des", "dem", "artikel",
        },
        [V3QuestionLanguage.Luxembourgish] = new(StringComparer.Ordinal)
        {
            "ass", "wat", "wéi", "vum", "vun", "dat", "net", "ech", "huet", "sinn", "fir", "mat", "soll", "den", "eng", "een",
            "wann", "kee", "seet", "gëtt",
        },
        [V3QuestionLanguage.Portuguese] = new(StringComparer.Ordinal)
        {
            "os", "um", "uma", "é", "qual", "quais", "como", "diz", "disse", "lei", "artigo", "segundo", "pode", "posso", "devo",
            "não", "da", "dos", "das", "no", "na", "nos", "nas", "em", "para", "com", "meu", "minha", "o", "que",
        },
    };

    private static readonly Regex WordPattern = new(@"[\p{L}]+['’]?", Options, Timeout);

    /// <summary>
    /// The language a question is written in: the language whose closed word list the question's words meet most often, when
    /// it meets it strictly more often than any other; otherwise <see cref="V3QuestionLanguage.Undetermined"/>.
    /// </summary>
    public static V3QuestionLanguage LanguageOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var counts = LanguageWords.Keys.ToDictionary(static language => language, static _ => 0);
        foreach (Match match in WordPattern.Matches(text.ToLowerInvariant()))
        {
            var word = match.Value.Replace('’', '\'');
            foreach (var (language, words) in LanguageWords)
            {
                if (words.Contains(word))
                {
                    counts[language]++;
                }
            }
        }

        var ranked = counts.OrderByDescending(static pair => pair.Value).ThenBy(static pair => pair.Key).ToArray();
        return ranked[0].Value > 0 && ranked[0].Value > ranked[1].Value ? ranked[0].Key : V3QuestionLanguage.Undetermined;
    }

    /// <summary>
    /// The answering locale for a question while <c>ask/1</c> carries the question alone (docs/ANSWER-DOSSIER-1.md §3): French
    /// or English as written; null for German, Luxembourgish and Portuguese, which are answered
    /// <c>localization_unavailable</c> (R43); English when the language is undetermined (a driver decision, STATUS-WEB.md).
    /// </summary>
    public static V3AnswerLocale? LocaleFor(V3QuestionLanguage language) => language switch
    {
        V3QuestionLanguage.French => V3AnswerLocale.Fr,
        V3QuestionLanguage.English or V3QuestionLanguage.Undetermined => V3AnswerLocale.En,
        _ => null,
    };

    // ---- Dates and ranges -------------------------------------------------------------------------------------------

    private static readonly string[] FrenchMonths =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    private static readonly string[] EnglishMonths =
        ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];

    private static readonly Regex IsoDate = new(@"(?<!\d)(\d{4})-(\d{2})-(\d{2})(?!\d)", Options, Timeout);
    private static readonly Regex NumericDate = new(@"(?<![\d./])(\d{1,2})[./](\d{1,2})[./](\d{4})(?![\d./])", Options, Timeout);
    private static readonly Regex FrenchWordDate = new(
        @"(?<!\d)(1er|\d{1,2})\s+(janvier|février|fevrier|mars|avril|mai|juin|juillet|août|aout|septembre|octobre|novembre|décembre|decembre)\s+(\d{4})(?!\d)",
        Options | RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex EnglishWordDate = new(
        @"(?<!\d)(\d{1,2})(?:st|nd|rd|th)?\s+(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{4})(?!\d)",
        Options | RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex EnglishMonthFirstDate = new(
        @"(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{1,2})(?:st|nd|rd|th)?,?\s+(\d{4})(?!\d)",
        Options | RegexOptions.IgnoreCase, Timeout);

    private static readonly HashSet<string> RangeConnectors = new(StringComparer.Ordinal)
    {
        "and", "to", "until", "through", "et", "et le", "et au", "au", "à", "jusqu'au", "jusqu’au", "-", "–",
    };

    /// <summary>The civil dates written in the question, in order of position, each named d1, d2, ...; invalid dates are not read.</summary>
    public static ReadOnlyCollection<V3ParsedDate> DatesIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<(int Start, int End, DateOnly Date)>();
        void Add(Match match, string year, string month, string day)
        {
            if (int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1800 and <= 2100 &&
                int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 12 &&
                int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d >= 1 && d <= DateTime.DaysInMonth(y, m) &&
                !found.Any(existing => match.Index < existing.End && existing.Start < match.Index + match.Length))
            {
                found.Add((match.Index, match.Index + match.Length, new DateOnly(y, m, d)));
            }
        }

        foreach (Match match in IsoDate.Matches(text))
        {
            Add(match, match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        }

        foreach (Match match in NumericDate.Matches(text))
        {
            Add(match, match.Groups[3].Value, match.Groups[2].Value, match.Groups[1].Value);
        }

        foreach (Match match in FrenchWordDate.Matches(text))
        {
            var month = FrenchMonthNumber(match.Groups[2].Value);
            var day = match.Groups[1].Value.Equals("1er", StringComparison.OrdinalIgnoreCase) ? "1" : match.Groups[1].Value;
            Add(match, match.Groups[3].Value, month.ToString(CultureInfo.InvariantCulture), day);
        }

        foreach (Match match in EnglishWordDate.Matches(text))
        {
            var month = Array.IndexOf(EnglishMonths, match.Groups[2].Value.ToLowerInvariant()) + 1;
            Add(match, match.Groups[3].Value, month.ToString(CultureInfo.InvariantCulture), match.Groups[1].Value);
        }

        foreach (Match match in EnglishMonthFirstDate.Matches(text))
        {
            var month = Array.IndexOf(EnglishMonths, match.Groups[1].Value.ToLowerInvariant()) + 1;
            Add(match, match.Groups[3].Value, month.ToString(CultureInfo.InvariantCulture), match.Groups[2].Value);
        }

        return found
            .OrderBy(static date => date.Start)
            .Select(static (date, at) => new V3ParsedDate(
                "d" + (at + 1).ToString(CultureInfo.InvariantCulture),
                date.Start,
                date.End,
                date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>Two consecutive dates the question joins by a connector (and, to, until, et, au, ...), the first not after the second.</summary>
    public static ReadOnlyCollection<V3ParsedRange> RangesIn(string text, IReadOnlyList<V3ParsedDate> dates)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dates);
        var ranges = new List<V3ParsedRange>();
        for (var at = 0; at + 1 < dates.Count; at++)
        {
            var between = text[dates[at].End..dates[at + 1].Start].Trim().ToLowerInvariant();
            if (RangeConnectors.Contains(between) && string.CompareOrdinal(dates[at].Iso, dates[at + 1].Iso) <= 0)
            {
                ranges.Add(new V3ParsedRange(
                    "r" + (ranges.Count + 1).ToString(CultureInfo.InvariantCulture),
                    dates[at].Start,
                    dates[at + 1].End,
                    dates[at].Iso,
                    dates[at + 1].Iso));
                at++;
            }
        }

        return ranges.AsReadOnly();
    }

    private static int FrenchMonthNumber(string month)
    {
        var folded = month.ToLowerInvariant() switch
        {
            "fevrier" => "février",
            "aout" => "août",
            "decembre" => "décembre",
            var other => other,
        };
        return Array.IndexOf(FrenchMonths, folded) + 1;
    }

    // ---- The function test's cues (R15) -----------------------------------------------------------------------------

    private static readonly (V3ApplicationFunction Function, Regex Pattern)[] CuePatterns =
    [
        // Chooses among legal options.
        (V3ApplicationFunction.ChoosesAmongOptions, new(@"\bshould\b|\bis it (?:better|wiser|best)\b|\bwhich (?:option|choice|route)\b|\brecommend", Options | RegexOptions.IgnoreCase, Timeout)),
        (V3ApplicationFunction.ChoosesAmongOptions, new(@"\bdevr(?:ais|ait|ions|iez|aient)\b|\bdoi[st]-(?:je|il|elle|on)\b|\bdoivent-(?:ils|elles)\b|\bfaut-il\b|\bvau(?:t|drait)-il mieux\b|\bque (?:dois|doit|doivent)\b|\bconseill|\brecommand", Options | RegexOptions.IgnoreCase, Timeout)),

        // Concludes on a person's situation.
        (V3ApplicationFunction.ConcludesOnSituation, new(@"\b(?:can|could|may)\s+(?!you\b)(?:[\w'’]+\s+){0,4}?(?:be\s+)?(?:fired|dismissed|evicted|sued|deported|expelled|prosecuted|fined|charged|refused|forced|required|obliged)\b", Options | RegexOptions.IgnoreCase, Timeout)),
        (V3ApplicationFunction.ConcludesOnSituation, new(@"\bis it (?:legal|lawful|illegal|unlawful|allowed|permitted|forbidden)\b|\b(?:am|is|are)\s+(?:[\w'’]+\s+){0,3}?(?:entitled|allowed|permitted|obliged|required|liable|eligible)\b|\b(?:have|has)\s+(?:the\s+)?right\s+to\b|\bin (?:my|his|her|their|our|your|this) (?:case|situation)\b", Options | RegexOptions.IgnoreCase, Timeout)),
        (V3ApplicationFunction.ConcludesOnSituation, new(@"\b(?:peu[xt]|peuvent|puis|pourrai[st]?|pourrait|pourraient)-(?:je|tu|il|elle|on|nous|vous|ils|elles)\b|\b(?:ai|as|a|avons|avez|ont)(?:-je|-tu|-t-il|-t-elle|-t-on|-nous|-vous|-ils|-elles)?\s+(?:le\s+)?droit\s+(?:de|d['’]|à|au)|\best-(?:il|ce)\s+(?:légal|licite|illégal|illicite|permis|autorisé|interdit)\b|\bdans (?:mon|ma|son|sa|leur|notre|votre|ce|cette) (?:cas|situation)\b|\bpeu(?:t|vent|x)\s+(?:me|te|le|la|les|nous|vous|l['’])\s*(?:licenci|expuls|congédi|renvoy|sanctionn)", Options | RegexOptions.IgnoreCase, Timeout)),

        // Predicts a decision-maker.
        (V3ApplicationFunction.PredictsDecisionMaker, new(@"\b(?:will|would)\s+(?:a|the)\s+(?:court|judge|tribunal|administration|authority|ministry)\b|\bchances?\s+(?:of|to)\s+(?:win|winning|succeed|succeeding|lose|losing)\b|\bwhat are (?:my|the|his|her|their) chances\b", Options | RegexOptions.IgnoreCase, Timeout)),
        (V3ApplicationFunction.PredictsDecisionMaker, new(@"\b(?:juge|tribunal|cour|administration)\s+(?:va-t-(?:il|elle)|va|vont|pourrait|donnera|accordera|décidera)\b|\b(?:mes|ses|leurs|nos|vos) chances\b|\bchances? de (?:gagner|réussir|l['’]emporter|perdre)\b", Options | RegexOptions.IgnoreCase, Timeout)),

        // Computes deadlines or amounts from the asker's facts.
        (V3ApplicationFunction.ComputesFromFacts, new(@"\bhow much (?:will|would|do|does|can|should|could|must|am|is|are)\b|\bhow (?:many|long)\b.*\b(?:have|left|get|receive|owe|pay)\b|\bcalculat|\bdeadline (?:for|of) (?:my|his|her|their|our|me|him|them)\b", Options | RegexOptions.IgnoreCase, Timeout)),
        (V3ApplicationFunction.ComputesFromFacts, new(@"\bcombien\b.*\b(?:vais-je|va-t-(?:il|elle)|vont-(?:ils|elles)|toucher|recevoir|payer|dois-je|doit-(?:il|elle)|ai-je|a-t-(?:il|elle))\b|\bcalcul|\bquel (?:est le )?délai (?:ai-je|a-t-(?:il|elle)|dois-je|me reste|lui reste)\b|\bjusqu['’]à quand (?:puis|peut|ai|a)\b", Options | RegexOptions.IgnoreCase, Timeout)),
    ];

    /// <summary>
    /// The function cues in <c>text[start, end)</c>, in order of position (R15). Each pattern names a function, never a
    /// grammatical person: a question about "I" and its twin about "an employee" meet the same pattern.
    /// </summary>
    public static ReadOnlyCollection<V3ApplicationCue> ApplicationCues(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (start < 0 || end > text.Length || start >= end)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The part is not a span of the question.");
        }

        var part = text[start..end];
        var cues = new List<V3ApplicationCue>();
        foreach (var (function, pattern) in CuePatterns)
        {
            foreach (Match match in pattern.Matches(part))
            {
                cues.Add(new V3ApplicationCue(function, start + match.Index, start + match.Index + match.Length));
            }
        }

        return cues.OrderBy(static cue => cue.Start).ThenBy(static cue => cue.Function).ToList().AsReadOnly();
    }
}
