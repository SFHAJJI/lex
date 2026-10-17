using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lex.V3.Api;

/// <summary>What an article number is read as: one number, a range or group of numbers one article element holds, or no number of the act.</summary>
internal enum ArticleNumberReading
{
    Number,
    Range,
    Group,
    Quoted,
}

/// <summary>
/// One article number, read (<see cref="ArticleNumbers"/>): its key, which two forms of one number share and two numbers never do
/// (<c>L.121-6</c>, <c>1</c>, <c>42ter</c>, <c>III</c>, <c>unique</c>, <c>141 à 144</c>); how the act's own language prints it
/// (<c>L. 121-6</c>, <c>1er</c>); and, for a range or a group, the numbers it holds.
/// </summary>
internal sealed record ArticleNumber(string Key, string Printed, ArticleNumberReading Reading, IReadOnlyList<string> Members);

/// <summary>
/// The fixed table of article numbers (the plan's item 4.2; the specification of record's R90: no protected token dropped). A
/// citation's article number and each article id an index holds are read to one key by the table <c>article-numbers.json</c>
/// (the data: the citation words, the forms of the first article, the Latin suffixes and their synonyms, the code letters,
/// and every shape of article id the indexes hold, with examples), and a citation opens the article whose key is its key:
/// <c>1er</c>, <c>premier</c> and <c>1</c> are one number; <c>L. 121-6</c>, <c>L.121-6</c> and <c>L 121-6</c> are one number;
/// <c>42</c> is never <c>42ter</c>, <c>42bis</c> or <c>42-1</c>, and <c>3</c> is never <c>III</c>. Nothing is near: two keys are
/// equal or they are two numbers. The table's own test holds it to every article id the population's indexes hold.
/// </summary>
internal static class ArticleNumbers
{
    internal const string TableResource = "Lex.V3.Api.ArticleNumbers.json";

    private static readonly Table Data = Table.Load();

    /// <summary>The table's rule, as the answers that use it state it.</summary>
    internal static string Rule => Data.Rule;

    /// <summary>The words a citation names an article with (folded: <c>article</c>, <c>art</c>, <c>artikel</c>), longest first.</summary>
    internal static IReadOnlyList<string> CitationWords => Data.CitationWords;

    /// <summary>The table as the API holds it, for its test: the id shapes and their examples, and the citation examples.</summary>
    internal static JsonElement TableDocument => Data.Document.RootElement;

    /// <summary>
    /// A citation's article number, as a reader writes it after "article" or "art.": <c>1</c>, <c>1er</c>, <c>premier</c>,
    /// <c>L. 121-6</c>, <c>42 ter</c>, <c>III</c>, <c>unique</c>, <c>141 à 144</c>. Null when the text is no number the table
    /// reads.
    /// </summary>
    public static ArticleNumber? FromCitation(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim().TrimEnd('.').Trim();
        if (value.Length == 0)
        {
            return null;
        }

        return ReadSpan(value, separators: "\\s+");
    }

    /// <summary>
    /// The number a Luxembourg article id names, by the table's id shapes: <c>art_1er</c> is 1, <c>art_l_121-6</c> is L.121-6,
    /// <c>art_2_champ_d'application</c> is 2, <c>art_141_à_144</c> is the range 141 à 144, and <c>art_4)_art_13</c> (a quoted
    /// article of an amended act) is <see cref="ArticleNumberReading.Quoted"/>. Null for an id of no shape the table holds.
    /// </summary>
    public static ArticleNumber? FromLuxembourgId(string publisherId)
    {
        ArgumentNullException.ThrowIfNull(publisherId);
        if (QuotedInAmendingPoint.IsMatch(publisherId) || publisherId.StartsWith("art__art_", StringComparison.Ordinal))
        {
            return new ArticleNumber(publisherId, publisherId, ArticleNumberReading.Quoted, []);
        }

        string body;
        if (publisherId.StartsWith("article_", StringComparison.Ordinal))
        {
            body = publisherId["article_".Length..];
        }
        else if (publisherId.StartsWith("Article ", StringComparison.Ordinal))
        {
            body = publisherId["Article ".Length..].TrimEnd('.');
        }
        else if (publisherId.StartsWith("art_", StringComparison.Ordinal))
        {
            body = publisherId["art_".Length..];
        }
        else if (publisherId.StartsWith("art", StringComparison.Ordinal))
        {
            body = publisherId["art".Length..];
        }
        else
        {
            return null;
        }

        if (body.Length == 0)
        {
            return null;
        }

        // A dated id: the number before the date (read first, so the date is never taken for a second number).
        if (DatedId.Match(body) is { Success: true } dated && IsCalendarDay(dated.Groups["date"].Value) &&
            ReadOne(dated.Groups["number"].Value) is { } numbered)
        {
            return numbered;
        }

        // A range or a group the publisher writes as one article element, by its separators.
        if (ReadSpan(body, separators: "_") is { Reading: ArticleNumberReading.Range or ArticleNumberReading.Group } span)
        {
            return span;
        }

        if (ReadOne(body) is { } whole)
        {
            return whole;
        }

        // A heading after the number: the shortest leading part that is a number, followed by "_" and a letter.
        for (var at = body.IndexOf('_'); at > 0; at = body.IndexOf('_', at + 1))
        {
            if (at + 1 < body.Length && char.IsLetter(body[at + 1]) && ReadOne(body[..at]) is { } headed)
            {
                return headed;
            }
        }

        return null;
    }

    /// <summary>
    /// The number an EU provision id names (the EU index's ids: the number on at least three digits, then its letters in
    /// capitals): <c>006</c> is 6, <c>038L</c> is 38l. Null for any other id.
    /// </summary>
    public static ArticleNumber? FromEuropeId(string publisherId)
    {
        ArgumentNullException.ThrowIfNull(publisherId);
        var match = EuropeId.Match(publisherId);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups["n"].Value.TrimStart('0');
        var key = (number.Length == 0 ? "0" : number) + match.Groups["l"].Value.ToLowerInvariant();
        return new ArticleNumber(key, key, ArticleNumberReading.Number, []);
    }

    /// <summary>
    /// The ids, of <paramref name="held"/>, that a citation of <paramref name="cited"/> opens, by the table's rule: those whose
    /// key is the cited key; when none, and the cited number is one number, the ranges and groups that hold it. A quoted article
    /// is never opened, and an id the table cannot read is never opened.
    /// </summary>
    public static (IReadOnlyList<string> Ids, bool WithinGroup) Opened(
        ArticleNumber cited, IReadOnlyList<string> held, Func<string, ArticleNumber?> read)
    {
        ArgumentNullException.ThrowIfNull(cited);
        ArgumentNullException.ThrowIfNull(held);
        var readings = held.Select(id => (Id: id, Number: read(id))).Where(static entry => entry.Number is not null).ToArray();
        var exact = readings
            .Where(entry => entry.Number!.Reading != ArticleNumberReading.Quoted &&
                string.Equals(entry.Number.Key, cited.Key, StringComparison.Ordinal))
            .Select(static entry => entry.Id).Distinct(StringComparer.Ordinal).ToArray();
        if (exact.Length > 0 || cited.Reading != ArticleNumberReading.Number)
        {
            return (exact, false);
        }

        var within = readings
            .Where(entry => entry.Number!.Reading is ArticleNumberReading.Range or ArticleNumberReading.Group &&
                entry.Number.Members.Contains(cited.Key, StringComparer.Ordinal))
            .Select(static entry => entry.Id).Distinct(StringComparer.Ordinal).ToArray();
        return (within, within.Length > 0);
    }

    // ---------------------------------------------------------------------------------------------------------------------

    private static readonly Regex QuotedInAmendingPoint = new(@"^art_[0-9]+\)_art_", RegexOptions.CultureInvariant);

    private static readonly Regex DatedId = new(@"^(?<number>.+)_(?<date>[0-9]{8})$", RegexOptions.CultureInvariant);

    private static readonly Regex EuropeId = new(@"^(?<n>[0-9]{3,})(?<l>[A-Z]{0,3})$", RegexOptions.CultureInvariant);

    private static readonly Regex Roman = new(
        "^(?=[mdclxvi])m{0,3}(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3})$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A span of one or two numbers: one number, or two joined by the table's range word (a range) or group word (a group),
    /// with the separators given (spaces in a citation, underscores in an id).
    /// </summary>
    private static ArticleNumber? ReadSpan(string text, string separators)
    {
        var parts = Regex.Split(text.Trim(), separators, RegexOptions.CultureInvariant)
            .Where(static part => part.Length > 0).ToArray();
        if (parts.Length == 3 && ReadOne(parts[0]) is { Reading: ArticleNumberReading.Number } first &&
            ReadOne(parts[2]) is { Reading: ArticleNumberReading.Number } second &&
            !string.Equals(first.Key, second.Key, StringComparison.Ordinal))
        {
            var joiner = Fold(parts[1]);
            if (Data.RangeWords.Contains(joiner, StringComparer.Ordinal))
            {
                return Range(first, second);
            }

            if (Data.GroupWords.Contains(joiner, StringComparer.Ordinal))
            {
                return Group(first, second);
            }
        }

        // An id's two numbers joined by an underscore alone: a group (art_559_560, scl:ids art_559;art_560).
        if (separators == "_" && parts.Length == 2 && IsPlain(parts[0]) && IsPlain(parts[1]) &&
            ReadOne(parts[0]) is { } left && ReadOne(parts[1]) is { } right &&
            !string.Equals(left.Key, right.Key, StringComparison.Ordinal))
        {
            return Group(left, right);
        }

        return ReadOne(text);
    }

    private static bool IsPlain(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    private static ArticleNumber Range(ArticleNumber first, ArticleNumber second)
    {
        var key = $"{first.Key} à {second.Key}";
        var members = new List<string>();
        if (IsPlain(first.Key) && IsPlain(second.Key) &&
            int.TryParse(first.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var from) &&
            int.TryParse(second.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var to) &&
            from < to && to - from <= 5000)
        {
            for (var number = from; number <= to; number++)
            {
                members.Add(number.ToString(CultureInfo.InvariantCulture));
            }
        }
        else
        {
            members.Add(first.Key);
            members.Add(second.Key);
        }

        return new ArticleNumber(key, $"{first.Printed} à {second.Printed}", ArticleNumberReading.Range, members);
    }

    private static ArticleNumber Group(ArticleNumber first, ArticleNumber second) =>
        new($"{first.Key} et {second.Key}", $"{first.Printed} et {second.Printed}", ArticleNumberReading.Group, [first.Key, second.Key]);

    /// <summary>
    /// One number: <c>unique</c>; <c>premier</c>; a roman number, with <c>er</c> on I and a Latin suffix; or an arabic number,
    /// after a code letter, its hyphenated parts, <c>er</c> on 1, then a Latin suffix or up to three letters. Case and the
    /// spaces, dots and underscores around a code letter or a suffix are not part of it.
    /// </summary>
    private static ArticleNumber? ReadOne(string text)
    {
        var value = text.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        var folded = Fold(value);
        if (Data.Unique.Contains(folded, StringComparer.Ordinal))
        {
            return new ArticleNumber("unique", "unique", ArticleNumberReading.Number, []);
        }

        if (Data.FirstWords.Contains(folded, StringComparer.Ordinal))
        {
            return First();
        }

        var arabic = Arabic.Match(value);
        if (arabic.Success)
        {
            var code = arabic.Groups["code"].Success ? arabic.Groups["code"].Value.ToUpperInvariant() : null;
            if (code is not null && !Data.CodeLetters.Contains(code, StringComparer.Ordinal))
            {
                return null;
            }

            var parts = Regex.Split(arabic.Groups["digits"].Value, @"\s*-\s*", RegexOptions.CultureInvariant)
                .Select(static part => part.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0").ToArray();
            var digits = string.Join("-", parts);
            var ordinal = arabic.Groups["er"].Success;
            if (ordinal && !(digits == "1" && code is null))
            {
                return null;
            }

            var suffix = string.Empty;
            if (arabic.Groups["tail"].Success)
            {
                if (LatinSuffix(arabic.Groups["tail"].Value) is { } latin)
                {
                    suffix = latin;
                }
                else if (arabic.Groups["sep"].Value.Length == 0 && arabic.Groups["tail"].Value.All(char.IsAsciiLetterLower) &&
                         arabic.Groups["tail"].Value.Length <= 3 && code is null)
                {
                    // Letters right after the number (an EU-style 38l): part of the number, never dropped.
                    suffix = arabic.Groups["tail"].Value;
                }
                else
                {
                    return null;
                }
            }

            if (code is null && suffix.Length == 0 && digits == "1")
            {
                return First();
            }

            var key = (code is null ? string.Empty : code + ".") + digits + suffix;
            var printed = (code is null ? string.Empty : code + ". ") + digits + suffix;
            return new ArticleNumber(key, printed, ArticleNumberReading.Number, []);
        }

        var roman = RomanWithTail.Match(value);
        if (roman.Success && Roman.IsMatch(roman.Groups["roman"].Value.ToLowerInvariant()))
        {
            var numeral = roman.Groups["roman"].Value.ToUpperInvariant();
            var ordinal = roman.Groups["er"].Success;
            if (ordinal && numeral != "I")
            {
                return null;
            }

            var suffix = string.Empty;
            if (roman.Groups["tail"].Success)
            {
                if (LatinSuffix(roman.Groups["tail"].Value) is not { } latin)
                {
                    return null;
                }

                suffix = latin;
            }

            // A roman number is written as a number is, in capitals; "Ier" is I.
            return new ArticleNumber(numeral + suffix, numeral + (ordinal ? "er" : string.Empty) + suffix, ArticleNumberReading.Number, []);
        }

        return null;
    }

    private static ArticleNumber First() => new("1", "1er", ArticleNumberReading.Number, []);

    // A code letter (then optional spaces, dots and underscores), the digits and hyphenated parts, "er", and a tail: a Latin
    // suffix (after an optional space, hyphen or underscore) or letters right after the number.
    private static readonly Regex Arabic = new(
        @"^(?:(?<code>[A-Za-z])[\s._]*(?=[0-9]))?(?<digits>[0-9]+(?:\s*-\s*[0-9]+)*)(?<er>er|ER)?(?:(?<sep>[\s_-]?)(?<tail>[A-Za-z]+))?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RomanWithTail = new(
        @"^(?<roman>[IVXLCDMivxlcdm]+?)(?<er>er|ER)?(?:[\s_-]?(?<tail>(?:bis|ter|quater|quinquies|sexies|septies|octies|novies|nonies|decies)))?$",
        RegexOptions.CultureInvariant);

    private static string? LatinSuffix(string text)
    {
        var folded = Fold(text);
        return Data.Latin.TryGetValue(folded, out var canonical) ? canonical : null;
    }

    /// <summary>Lowered, without accents, as the title folding reads a word.</summary>
    internal static string Fold(string text) => Lex.V3.Contracts.Index.TitleFolding.NormalizeTitle(text);

    private static bool IsCalendarDay(string yyyymmdd) =>
        DateOnly.TryParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private sealed record Table(
        string Rule,
        IReadOnlyList<string> CitationWords,
        IReadOnlyList<string> FirstWords,
        IReadOnlyList<string> Unique,
        IReadOnlyList<string> CodeLetters,
        IReadOnlyDictionary<string, string> Latin,
        IReadOnlyList<string> RangeWords,
        IReadOnlyList<string> GroupWords,
        JsonDocument Document)
    {
        public static Table Load()
        {
            using var stream = typeof(ArticleNumbers).Assembly.GetManifestResourceStream(TableResource)
                ?? throw new InvalidOperationException($"The article-number table {TableResource} is not embedded.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "lex-v3-article-number-table/1")
            {
                throw new InvalidDataException("The article-number table is not lex-v3-article-number-table/1.");
            }

            static string[] Words(JsonElement array) =>
                array.EnumerateArray().Select(static value => Fold(value.GetString()!)).ToArray();

            var latin = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in root.GetProperty("latin_suffixes").EnumerateArray())
            {
                var canonical = entry.GetProperty("suffix").GetString()!;
                latin.Add(Fold(canonical), canonical);
                if (entry.TryGetProperty("also", out var also))
                {
                    foreach (var synonym in also.EnumerateArray())
                    {
                        latin.Add(Fold(synonym.GetString()!), canonical);
                    }
                }
            }

            return new Table(
                root.GetProperty("rule").GetString()!,
                Words(root.GetProperty("citation_words")).OrderByDescending(static word => word.Length).ToArray(),
                Words(root.GetProperty("first")),
                Words(root.GetProperty("unique")),
                root.GetProperty("code_letters").EnumerateArray().Select(static value => value.GetString()!).ToArray(),
                latin,
                Words(root.GetProperty("range_words")),
                Words(root.GetProperty("group_words")),
                document);
        }
    }
}
