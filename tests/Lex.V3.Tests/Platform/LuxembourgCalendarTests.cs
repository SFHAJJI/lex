using System.Globalization;
using System.Text.Json;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

/// <summary>
/// "Today" in Luxembourg by the coded EU rule (<see cref="LuxembourgCalendar"/>, plan 0.8), held to the table the web's
/// twin (<c>web/scripts/temporal.mjs</c>) is held to, and to the host's zone database where the host has one. In the
/// image-conditions lane there is none, so the table is the only measure there, and the rule meets it with no zone data.
/// </summary>
[TestClass]
public sealed class LuxembourgCalendarTests
{
    private static JsonDocument Table() =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "LuxembourgToday", "luxembourg-today.json")));

    private static DateTimeOffset Instant(JsonElement row, string name) =>
        DateTimeOffset.Parse(row.GetProperty(name).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static string Offset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    [TestMethod]
    public void EveryInstantOfTheSharedTableHasItsLuxembourgDate()
    {
        using var table = Table();
        var rows = table.RootElement.GetProperty("instants").EnumerateArray().ToArray();
        Assert.HasCount(191, rows);
        foreach (var row in rows)
        {
            Assert.AreEqual(
                row.GetProperty("date").GetString(),
                LuxembourgCalendar.DateAt(Instant(row, "instant")).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                $"{row.GetProperty("instant").GetString()}: {row.GetProperty("case").GetString()}");
        }
    }

    [TestMethod]
    public void EveryInstantOfTheSharedTableHasItsOffset()
    {
        using var table = Table();
        foreach (var row in table.RootElement.GetProperty("instants").EnumerateArray())
        {
            Assert.AreEqual(
                row.GetProperty("offset").GetString(),
                Offset(LuxembourgCalendar.OffsetAt(Instant(row, "instant"))),
                $"{row.GetProperty("instant").GetString()}: {row.GetProperty("case").GetString()}");
        }
    }

    [TestMethod]
    public void SummerTimeStartsAndEndsWhereTheSharedTableSays()
    {
        using var table = Table();
        var years = table.RootElement.GetProperty("summer_time").EnumerateArray().ToArray();
        Assert.HasCount(17, years);
        foreach (var year in years)
        {
            var value = year.GetProperty("year").GetInt32();
            Assert.AreEqual(Instant(year, "starts"), LuxembourgCalendar.SummerTimeStarts(value), $"{value}: starts");
            Assert.AreEqual(Instant(year, "ends"), LuxembourgCalendar.SummerTimeEnds(value), $"{value}: ends");
        }
    }

    [TestMethod]
    public void AnInstantOutsideTheRulesRangeIsRefused()
    {
        using var table = Table();
        var refused = table.RootElement.GetProperty("refused").EnumerateArray().ToArray();
        Assert.HasCount(4, refused);
        foreach (var row in refused)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => LuxembourgCalendar.DateAt(Instant(row, "instant")), row.GetProperty("case").GetString());
        }
    }

    /// <summary>
    /// The drift alarm: should the Union or Luxembourg change the rule, the zone database follows and this fails, and the
    /// rule is changed. Every hour from 1996 to 2099, and the tick before each change. Not measured without zone data.
    /// </summary>
    [TestMethod]
    public void TheRuleIsTheHostZoneDatabaseAtEveryHourOfItsRange()
    {
        var zone = ImageConditions.LuxembourgZoneOrNull();
        if (zone is null)
        {
            Assert.Inconclusive("Not measured here: this host has no zone database (the release image's conditions).");
        }

        var instants = new List<DateTimeOffset>();
        for (var instant = LuxembourgCalendar.ValidFrom; instant < LuxembourgCalendar.ValidUntil; instant = instant.AddHours(1))
        {
            instants.Add(instant);
        }

        for (var year = LuxembourgCalendar.ValidFrom.Year; year < LuxembourgCalendar.ValidUntil.Year; year++)
        {
            instants.Add(LuxembourgCalendar.SummerTimeStarts(year).AddTicks(-1));
            instants.Add(LuxembourgCalendar.SummerTimeEnds(year).AddTicks(-1));
        }

        Assert.HasCount(911_872, instants);
        var differing = instants
            .Where(instant =>
                LuxembourgCalendar.OffsetAt(instant) != zone.GetUtcOffset(instant) ||
                LuxembourgCalendar.DateAt(instant) != DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime))
            .Take(5)
            .Select(static instant => instant.ToString("O", CultureInfo.InvariantCulture))
            .ToArray();
        Assert.IsEmpty(differing, string.Join(", ", differing));
    }
}
