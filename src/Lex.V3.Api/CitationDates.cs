using System.Globalization;
using System.Text.RegularExpressions;

namespace Lex.V3.Api;

/// <summary>A date a citation names, read strictly: the text as typed and the calendar date it names (yyyy-MM-dd).</summary>
internal sealed record CitationDate(string AsTyped, string Date);

/// <summary>
/// The dates a citation writes (the plan's item 4.2; R90: no date dropped), read strictly and only in these forms:
/// <c>2021-03-15</c> (its month and day on one or two digits); <c>15/03/2021</c>, <c>15.03.2021</c> and <c>15-03-2021</c> (the day first, as Luxembourg and the Union
/// write it); <c>15 mars 2021</c>, <c>1er mars 2021</c>, <c>15 March 2021</c>, <c>15. März 2021</c>; and <c>March 15, 2021</c>.
/// A month is a month's full name in French, English or German; the year has four digits. A text in one of these shapes that
/// names no day of the calendar (31 février, 2021-02-29) is said to be one (<see cref="IsDateShaped"/>) and is never read as a
/// near date; a text in none of them is no date.
/// </summary>
internal static class CitationDates
{
    private static readonly IReadOnlyDictionary<string, int> Months = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // French, folded (no accents, lower case).
        ["janvier"] = 1, ["fevrier"] = 2, ["mars"] = 3, ["avril"] = 4, ["mai"] = 5, ["juin"] = 6,
        ["juillet"] = 7, ["aout"] = 8, ["septembre"] = 9, ["octobre"] = 10, ["novembre"] = 11, ["decembre"] = 12,
        // English.
        ["january"] = 1, ["february"] = 2, ["march"] = 3, ["april"] = 4, ["may"] = 5, ["june"] = 6,
        ["july"] = 7, ["august"] = 8, ["september"] = 9, ["october"] = 10, ["november"] = 11, ["december"] = 12,
        // German ("März" folds to "marz"; "Maerz" is its spelling without the umlaut).
        ["januar"] = 1, ["februar"] = 2, ["marz"] = 3, ["maerz"] = 3, ["juni"] = 6, ["juli"] = 7,
        ["oktober"] = 10, ["dezember"] = 12,
    };

    /// <summary>The shapes a date is written in, as one pattern a citation's grammar embeds (no groups of its own).</summary>
    internal const string Shape =
        @"(?:[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}" +
        @"|[0-9]{1,2}[./-][0-9]{1,2}[./-][0-9]{4}" +
        @"|(?:1er|1re|[0-9]{1,2}(?:st|nd|rd|th|\.)?)\s+\p{L}+\s+[0-9]{4}" +
        @"|\p{L}+\s+[0-9]{1,2}(?:st|nd|rd|th)?,?\s+[0-9]{4})";

    // The year, then the month and the day on one or two digits each, as the shape reads them (2012-1-5 names its day).
    private static readonly Regex Iso = new(@"^(?<y>[0-9]{4})-(?<m>[0-9]{1,2})-(?<d>[0-9]{1,2})$", RegexOptions.CultureInvariant);

    private static readonly Regex DayFirst = new(@"^(?<d>[0-9]{1,2})(?<s>[./-])(?<m>[0-9]{1,2})\k<s>(?<y>[0-9]{4})$", RegexOptions.CultureInvariant);

    private static readonly Regex DayMonthYear = new(
        @"^(?<d>1er|1re|[0-9]{1,2})(?:st|nd|rd|th|\.)?\s+(?<month>\p{L}+)\s+(?<y>[0-9]{4})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex MonthDayYear = new(
        @"^(?<month>\p{L}+)\s+(?<d>[0-9]{1,2})(?:st|nd|rd|th)?,?\s+(?<y>[0-9]{4})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AnyShape = new("^" + Shape + "$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Whether <paramref name="text"/> is written in one of the shapes, whether or not it names a calendar day.</summary>
    public static bool IsDateShaped(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim();
        if (!AnyShape.IsMatch(value))
        {
            return false;
        }

        // A day, a word and a year are a date's shape only when the word is a month.
        if (DayMonthYear.Match(value) is { Success: true } named)
        {
            return Months.ContainsKey(ArticleNumbers.Fold(named.Groups["month"].Value));
        }

        if (MonthDayYear.Match(value) is { Success: true } american)
        {
            return Months.ContainsKey(ArticleNumbers.Fold(american.Groups["month"].Value));
        }

        return true;
    }

    /// <summary>The calendar date <paramref name="text"/> names, or null when it names none (or is in no shape this reads).</summary>
    public static CitationDate? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim();
        string? day = null, month = null, year = null;
        if (Iso.Match(value) is { Success: true } iso)
        {
            (year, month, day) = (iso.Groups["y"].Value, iso.Groups["m"].Value, iso.Groups["d"].Value);
        }
        else if (DayFirst.Match(value) is { Success: true } numeric)
        {
            (year, month, day) = (numeric.Groups["y"].Value, numeric.Groups["m"].Value, numeric.Groups["d"].Value);
        }
        else if (DayMonthYear.Match(value) is { Success: true } named &&
                 Months.TryGetValue(ArticleNumbers.Fold(named.Groups["month"].Value), out var namedMonth))
        {
            var written = named.Groups["d"].Value;
            day = written.StartsWith('1') && written.Length == 3 ? "1" : written;
            (year, month) = (named.Groups["y"].Value, namedMonth.ToString(CultureInfo.InvariantCulture));
        }
        else if (MonthDayYear.Match(value) is { Success: true } american &&
                 Months.TryGetValue(ArticleNumbers.Fold(american.Groups["month"].Value), out var americanMonth))
        {
            (year, month, day) = (american.Groups["y"].Value, americanMonth.ToString(CultureInfo.InvariantCulture), american.Groups["d"].Value);
        }

        if (year is null || month is null || day is null ||
            !int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ||
            !int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var d) ||
            y < 1000 || m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, m))
        {
            return null;
        }

        return new CitationDate(value, new DateOnly(y, m, d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
