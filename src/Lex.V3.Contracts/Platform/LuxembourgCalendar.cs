namespace Lex.V3.Contracts.Platform;

/// <summary>
/// "Today" in Luxembourg: the calendar date in Europe/Luxembourg at an instant (the specification of record's R13), from
/// the European Union's summer-time rule coded here, never from the operating system's zone database. The release image
/// carries none: .NET there knows no zone but UTC and says nothing, so a date read from its local clock would be
/// yesterday's in Luxembourg every night after 22:00 UTC in summer and 23:00 UTC in winter (plan 0.8).
/// </summary>
/// <remarks>
/// <para>
/// The rule: Central European Time, UTC+01:00, and summer time, UTC+02:00, from 01:00 UTC on the last Sunday of March to
/// 01:00 UTC on the last Sunday of October (Directive 2000/84/EC, Articles 2 and 3; the same dates since 1996). An
/// instant at a change already has the new offset. The date at an instant is the calendar date of the instant plus its
/// offset; nothing here turns a local time back into an instant, so neither the hour that October repeats nor the hour
/// that March skips can arise. It is held for the instants from <see cref="ValidFrom"/> (before 1996 summer time ended in
/// September) to <see cref="ValidUntil"/>, and refuses any other.
/// </para>
/// <para>
/// One table, <c>tests/Lex.V3.Tests/Fixtures/LuxembourgToday/luxembourg-today.json</c>, holds this and its twin in the web
/// (<c>web/scripts/temporal.mjs</c>) to the same dates and offsets around every clock change and local midnight from 2026
/// to 2040; the web's test holds the table itself to the IANA zone database, and <c>LuxembourgCalendarTests</c> holds
/// this to the host's zone database at every hour of the range, wherever the host has one. If the Union or Luxembourg
/// changes the rule, the zone database follows and those comparisons fail: the rule is then changed here.
/// </para>
/// </remarks>
public static class LuxembourgCalendar
{
    /// <summary>The first instant the rule is held for: 1996-01-01T00:00:00Z.</summary>
    public static readonly DateTimeOffset ValidFrom = new(1996, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The first instant past the rule's range: 2100-01-01T00:00:00Z.</summary>
    public static readonly DateTimeOffset ValidUntil = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The calendar date in Luxembourg at <paramref name="instant"/>.</summary>
    public static DateOnly DateAt(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.UtcDateTime + OffsetAt(instant));

    /// <summary>Luxembourg's offset from UTC at <paramref name="instant"/>: two hours in summer time, one otherwise.</summary>
    public static TimeSpan OffsetAt(DateTimeOffset instant)
    {
        if (instant < ValidFrom || instant >= ValidUntil)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instant), instant, "Luxembourg's date is held from 1996-01-01T00:00:00Z to 2100-01-01T00:00:00Z.");
        }

        var year = instant.UtcDateTime.Year;
        return instant >= SummerTimeStarts(year) && instant < SummerTimeEnds(year)
            ? TimeSpan.FromHours(2)
            : TimeSpan.FromHours(1);
    }

    /// <summary>When summer time starts in <paramref name="year"/>: 01:00 UTC on the last Sunday of March.</summary>
    public static DateTimeOffset SummerTimeStarts(int year) => LastSundayAtOneUtc(year, 3);

    /// <summary>When summer time ends in <paramref name="year"/>: 01:00 UTC on the last Sunday of October.</summary>
    public static DateTimeOffset SummerTimeEnds(int year) => LastSundayAtOneUtc(year, 10);

    private static DateTimeOffset LastSundayAtOneUtc(int year, int month)
    {
        if (year < ValidFrom.Year || year >= ValidUntil.Year)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Luxembourg's summer time is held from 1996 to 2099.");
        }

        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var sunday = last.AddDays(-(int)last.DayOfWeek);
        return new DateTimeOffset(sunday.Year, sunday.Month, sunday.Day, 1, 0, 0, TimeSpan.Zero);
    }
}
