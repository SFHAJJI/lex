using System.Globalization;
using System.Text.RegularExpressions;

namespace Lex.Ask;

/// <summary>
/// Re-deriving the INSTANT from the user's own words, the way work resolution already re-derives
/// the instrument.
///
/// <para>The planner prompt used to say "expand a bare year to its full inclusive calendar
/// boundary" without narrowing that to a range, and the model read it onto a single point-in-time
/// slot: "what did Article 92 of the CRR require in 2024" was planned as as_of with
/// date=2024-12-31. Either boundary would have been equally wrong, and both silently select a
/// version of the law.</para>
///
/// <para><see cref="OperationArguments"/> cannot catch this and must not be widened to. It
/// receives the action name and the planner's argument object and nothing else, and from inside
/// that gate <c>date=2024-12-31</c> for "in 2024" is byte-identical to <c>date=2024-12-31</c> for
/// "on 31 December 2024". Handing it the user's question would destroy the property that makes it
/// testable. The vantage point is wrong, not the design, so the check lives here, where the plan
/// is first held beside the turn that produced it.</para>
///
/// <para>A prompt rule is not an invariant. This exact rule already failed once in production, and
/// a reworded version of it can fail again silently, which is why this exists as well as the
/// rewritten prompt and not instead of it.</para>
/// </summary>
internal static class DateIntentGuard
{
    /// <summary>A four-digit year standing alone. The lookarounds are what keep it off the year
    /// inside an identifier the user did type in full, and off a five-digit CELEX number.</summary>
    private static readonly Regex BareYear = new(
        @"(?<!\d)(1[89]|20)\d{2}(?!\d)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Every shape in which this product actually receives a full calendar day. Each
    /// alternative captures the day, the month and the year, because the guard needs the DATE a
    /// phrase denotes and not merely the fact that some date was written.</summary>
    private static readonly Regex StatedDay = new(
        @"(?<!\d)(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?!\d)"
        + @"|(?<!\d)(?<d>\d{1,2})[/.](?<m>\d{1,2})[/.](?<y>\d{4})(?!\d)"
        + @"|(?<!\w)(?<d>\d{1,2})(er|st|nd|rd|th)?\s+(?<mon>janvier|f[ée]vrier|mars|avril|mai|juin|juillet|ao[ûu]t|septembre|octobre|novembre|d[ée]cembre|january|february|march|april|may|june|july|august|september|october|november|december)\s+(?<y>\d{4})(?!\d)"
        + @"|(?<!\w)(?<mon>janvier|f[ée]vrier|mars|avril|mai|juin|juillet|ao[ûu]t|septembre|octobre|novembre|d[ée]cembre|january|february|march|april|may|june|july|august|september|october|november|december)\s+(?<d>\d{1,2})(er|st|nd|rd|th)?,?\s+(?<y>\d{4})(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["janvier"] = 1, ["january"] = 1,
        ["fevrier"] = 2, ["février"] = 2, ["fébrier"] = 2, ["february"] = 2,
        ["mars"] = 3, ["march"] = 3,
        ["avril"] = 4, ["april"] = 4,
        ["mai"] = 5, ["may"] = 5,
        ["juin"] = 6, ["june"] = 6,
        ["juillet"] = 7, ["july"] = 7,
        ["aout"] = 8, ["août"] = 8, ["august"] = 8,
        ["septembre"] = 9, ["september"] = 9,
        ["octobre"] = 10, ["october"] = 10,
        ["novembre"] = 11, ["november"] = 11,
        ["decembre"] = 12, ["décembre"] = 12, ["december"] = 12,
    };

    /// <summary>Every full day the turn actually states, as dates rather than as evidence that
    /// some date was written somewhere.</summary>
    private static HashSet<DateOnly> StatedDays(string turn)
    {
        var stated = new HashSet<DateOnly>();
        foreach (Match match in StatedDay.Matches(turn))
        {
            if (!int.TryParse(match.Groups["y"].Value, CultureInfo.InvariantCulture, out var year))
                continue;
            if (!int.TryParse(match.Groups["d"].Value, CultureInfo.InvariantCulture, out var day))
                continue;
            int month;
            if (match.Groups["mon"].Success)
            {
                if (!Months.TryGetValue(match.Groups["mon"].Value, out month)) continue;
            }
            else if (!int.TryParse(match.Groups["m"].Value, CultureInfo.InvariantCulture, out month))
                continue;
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) continue;
            stated.Add(new DateOnly(year, month, day));
        }
        return stated;
    }

    /// <summary>
    /// The year a planned point-in-time date was derived from rather than stated, or null when the
    /// plan is authorized.
    ///
    /// <para>Two conditions. The planned date is not one the turn states. And the turn carries a
    /// bare year equal to the planned year. A value may bind only when the user's own words
    /// produced it, which is the same principle the work resolver applies to instruments.</para>
    ///
    /// <para>The stand-down is deliberately keyed to the planned date and not to the presence of
    /// any date at all. It used to return early whenever a day-and-month appeared ANYWHERE in the
    /// turn, on the reasoning that the user had reached for a specific instant. Legal citations
    /// break that reasoning, because they carry dates that name an instrument rather than an
    /// instant: the CRR's official title contains "of 26 June 2013", and a Luxembourg statute is
    /// normally cited as "loi du 12 novembre 2004". Asking "what did Article 92 of [full CRR
    /// title] require in 2024" therefore disarmed the guard and let 2024-12-31 bind silently, so
    /// the citation forms most likely to confuse the resolver were also the ones that removed this
    /// protection. Requiring the stated day to BE the planned date closes that: a date belonging
    /// to a citation can never authorize a different date, while "on 31 December 2024" still
    /// authorizes 2024-12-31 exactly as before.</para>
    /// </summary>
    internal static int? DerivedYear(string turn, string? date)
    {
        if (string.IsNullOrWhiteSpace(turn) || string.IsNullOrWhiteSpace(date)) return null;
        if (!DateOnly.TryParseExact(date, OperationArguments.IsoDateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var planned))
            return null;
        if (StatedDays(turn).Contains(planned)) return null;
        foreach (Match match in BareYear.Matches(turn))
            if (int.TryParse(match.Value, CultureInfo.InvariantCulture, out var year)
                && year == planned.Year)
                return year;
        return null;
    }

    internal static string FirstDayOf(int year) =>
        new DateOnly(year, 1, 1).ToString(
            OperationArguments.IsoDateFormat, CultureInfo.InvariantCulture);

    internal static string LastDayOf(int year) =>
        new DateOnly(year, 12, 31).ToString(
            OperationArguments.IsoDateFormat, CultureInfo.InvariantCulture);
}
