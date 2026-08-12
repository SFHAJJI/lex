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

    /// <summary>Any expression that names a DAY, in any of the forms this product actually
    /// receives. Its presence means the user reached for a specific instant somewhere in the turn,
    /// and the guard stands down: the user's own words authorize that instant, exactly as the work
    /// resolver only authorizes works the user's own words named.</summary>
    private static readonly Regex DayAndMonth = new(
        @"(?<!\d)\d{4}-\d{2}-\d{2}(?!\d)"
        + @"|(?<!\d)\d{1,2}[/.]\d{1,2}[/.]\d{2,4}(?!\d)"
        + @"|(?<!\w)\d{1,2}(er|st|nd|rd|th)?\s+(janvier|f[ée]vrier|mars|avril|mai|juin|juillet|ao[ûu]t|septembre|octobre|novembre|d[ée]cembre|january|february|march|april|may|june|july|august|september|october|november|december)(?!\w)"
        + @"|(?<!\w)(janvier|f[ée]vrier|mars|avril|mai|juin|juillet|ao[ûu]t|septembre|octobre|novembre|d[ée]cembre|january|february|march|april|may|june|july|august|september|october|november|december)\s+\d{1,2}(er|st|nd|rd|th)?(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The year a planned point-in-time date was derived from rather than stated, or null when the
    /// plan is authorized.
    ///
    /// <para>Three conditions, all required. The turn carries a bare year. The turn carries no
    /// day-and-month expression anywhere. And the planned date falls inside that year while its
    /// literal text is absent from the turn. That last clause is what keeps "on 31 December 2024"
    /// working, and it is the same principle as the work resolver's: a value may bind only when
    /// the user's own words produced it.</para>
    /// </summary>
    internal static int? DerivedYear(string turn, string? date)
    {
        if (string.IsNullOrWhiteSpace(turn) || string.IsNullOrWhiteSpace(date)) return null;
        if (!DateOnly.TryParseExact(date, OperationArguments.IsoDateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var planned))
            return null;
        if (turn.Contains(date, StringComparison.Ordinal)) return null;
        if (DayAndMonth.IsMatch(turn)) return null;
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
