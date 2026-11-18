using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>A typed value found on a line by its pattern: where its words are on the line, and what kind of value it is.</summary>
public readonly record struct V4Value(string Type, int Start, int Length);

/// <summary>
/// The patterns that confirm a kind of answer on a line (brick D, the registry of answer types [6B]; the legal kinds are
/// ours). All are code over the folded copy of a line, so a position found here is the same position in the law's own text.
/// Numbers come in words and in digits in the same law ("quatorze jours calendrier", "30 jours calendrier"): the list of
/// number words is whole.
/// </summary>
public static class V4Patterns
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private const string Units = "un|une|deux|trois|quatre|cinq|six|sept|huit|neuf|dix|onze|douze|treize|quatorze|quinze|seize|dix-sept|dix-huit|dix-neuf";
    private const string Tens = "vingt|trente|quarante|cinquante|soixante-dix|soixante|quatre-vingt-dix|quatre-vingts?|cent";
    private const string NumberWords = "(?:(?:" + Tens + ")(?:(?:[- ]et[- ]|[- ])(?:" + Units + "))?|" + Units + ")";
    private const string Number = @"(?:\d{1,4}|" + NumberWords + ")";

    private static readonly Regex DurationPattern = new(
        @"(?<![\p{L}\d-])" + Number + @"\s+(?:jours?(?:\s+(?:calendrier|ouvrables?|francs))?|mois|ans?|annees?|semaines?|heures?)(?![\p{L}\d])",
        Options, Timeout);

    private static readonly Regex DatePattern = new(
        @"(?<![\p{L}\d])(?:1er|\d{1,2})\s+(?:janvier|fevrier|mars|avril|mai|juin|juillet|aout|septembre|octobre|novembre|decembre)\s+\d{4}(?!\d)|(?<![\d./])\d{1,2}[./]\d{1,2}[./]\d{4}(?![\d./])",
        Options, Timeout);

    private static readonly Regex AmountPattern = new(
        @"(?<![\p{L}\d])\d[\d .,]*\s*(?:euros?|eur|€|francs?|%|pour cent|pour-cent)(?![\p{L}\d])|(?<![\p{L}\d-])" + NumberWords + @"\s+pour cent(?![\p{L}\d])",
        Options, Timeout);

    private static readonly Regex RulePattern = new(
        @"(?<![\p{L}])(?:ne\s+peu(?:t|vent)|peu(?:t|vent)|a le droit|ont le droit|le droit de|la faculte de|doi(?:t|vent)|est tenu|sont tenus|est tenue|sauf|hormis|interdit|ne saurai(?:t|ent)|il est defendu|est nul|sont nuls|est nulle|dispose du droit|est libre de|a la liberte)(?![\p{L}])",
        Options, Timeout);

    private static readonly Regex DefinitionPattern = new(
        @"on entend par|est repute|sont reputes|est reputee|sont reputees|est considere comme|sont consideres comme|est consideree comme",
        Options, Timeout);

    private static readonly Regex ReferencePattern = new(@"(?<![\p{L}])articles?\s+\d", Options, Timeout);

    private static readonly Regex LeftToContractPattern = new(
        @"(?:fixe|prevu|determine|stipule|convenu|indique)e?s?\s+(?:par|dans|au)\s+le\s+contrat|sauf convention contraire|a defaut de convention|sauf stipulation contraire",
        Options, Timeout);

    private static readonly Regex ConditionPattern = new(
        @"(?<![\p{L}])(?:si|lorsque|lorsqu|a condition|pour autant que|sauf|hormis|a moins que|dans le cas ou|en cas de|toutefois|sous reserve)(?![\p{L}])",
        Options, Timeout);

    /// <summary>The kinds of answer a pattern can confirm on a line.</summary>
    public static bool HasPattern(string kind) => kind is "duration" or "date" or "amount" or "rule" or "definition" or "reference" or "list";

    /// <summary>Whether a folded line holds the form an answer of this kind takes.</summary>
    public static bool Confirms(string kind, string folded)
    {
        ArgumentNullException.ThrowIfNull(folded);
        return kind switch
        {
            "duration" => DurationPattern.IsMatch(folded),
            "date" => DatePattern.IsMatch(folded) || DurationPattern.IsMatch(folded),
            "amount" => AmountPattern.IsMatch(folded),
            "rule" or "list" => RulePattern.IsMatch(folded),
            "definition" => DefinitionPattern.IsMatch(folded),
            "reference" => ReferencePattern.IsMatch(folded),
            _ => false,
        };
    }

    /// <summary>The typed values of a kind written on a folded line, in order: each the exact words, by position.</summary>
    public static List<V4Value> Values(string kind, string folded)
    {
        ArgumentNullException.ThrowIfNull(folded);
        var pattern = kind switch
        {
            "duration" => DurationPattern,
            "date" => DatePattern,
            "amount" => AmountPattern,
            _ => null,
        };
        var values = new List<V4Value>();
        if (pattern is null)
        {
            return values;
        }

        foreach (Match match in pattern.Matches(folded))
        {
            values.Add(new V4Value(kind, match.Index, match.Length));
        }

        return values;
    }

    /// <summary>Whether a line leaves the matter to the contract ("dans le délai fixé par le contrat"): a value absent on purpose.</summary>
    public static bool LeftToContract(string folded) => LeftToContractPattern.IsMatch(folded);

    /// <summary>Whether a line states a condition or an exception ("si", "sauf", "hormis", "toutefois").</summary>
    public static bool StatesCondition(string folded) => ConditionPattern.IsMatch(folded);
}
