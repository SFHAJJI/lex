using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The retention line for built generations (S7-A09): referenced generations indefinitely, the last build of each UTC
/// day for 90 days, and each UTC month's earliest held generation indefinitely, decided from the log alone.
/// </summary>
[TestClass]
public sealed class V3GenerationRetentionTests
{
    private static string Digest(int index) => index.ToString("x64");

    /// <summary>A chained log of builds at the given times: build k is the index build k + 1 names as its predecessor.</summary>
    private static LuxembourgIndexObservation[] Log(params string[] builtAt) =>
        builtAt.Select((time, index) => new LuxembourgIndexObservation(
            index + 1L, new string('c', 64), index == 0 ? null : Digest(index), index + 1L, index + 1L, time)).ToArray();

    private static HashSet<string> Set(params string[] values) => values.ToHashSet(StringComparer.Ordinal);

    [TestMethod]
    public void TheLastBuildOfEachDayIsKeptNinetyDaysAndEachMonthsEarliestIndefinitely()
    {
        // Builds 1-2 on 1 July, 3 on 2 July, 4 on 3 August, the mounted build 5 on 30 October: 2 July is more than 90 days
        // before it, 3 August less.
        var log = Log("2026-07-01T08:00:00Z", "2026-07-01T20:00:00Z", "2026-07-02T08:00:00Z", "2026-08-03T08:00:00Z", "2026-10-30T08:00:00Z");
        var all = Set(Digest(1), Digest(2), Digest(3), Digest(4));
        var decision = V3GenerationRetention.Decide(log, all, Set());

        Assert.AreEqual(V3GenerationRetention.PolicyId, decision.PolicyId);
        Assert.AreEqual("2026-10-30T08:00:00Z", decision.EvaluatedAt, "now is the mounted build's own time, never the clock");
        var kept = decision.Retained.ToDictionary(static generation => generation.Observation);
        // Build 1: July's earliest, so its keeper; build 2 is its day's last but older than 90 days; build 3 likewise.
        CollectionAssert.AreEqual(new[] { V3GenerationRetention.MonthlyKeeper }, kept[1].Reasons.ToArray());
        CollectionAssert.AreEqual(new[] { V3GenerationRetention.Nightly, V3GenerationRetention.MonthlyKeeper }, kept[4].Reasons.ToArray(),
            "the last of its day within 90 days, and August's earliest");
        CollectionAssert.AreEqual(new long[] { 2, 3 }, decision.Dropped.Select(static generation => generation.Observation).ToArray());
        Assert.AreEqual(0, decision.Absent.Count);
    }

    [TestMethod]
    public void AReferencedGenerationIsKeptWhateverItsAgeAndAnAbsentOneIsReportedNotClaimed()
    {
        var log = Log("2026-07-01T08:00:00Z", "2026-07-01T20:00:00Z", "2026-07-02T08:00:00Z", "2026-10-30T08:00:00Z");
        var decision = V3GenerationRetention.Decide(log, Set(Digest(2), Digest(3)), Set(Digest(2)));

        var kept = decision.Retained.ToDictionary(static generation => generation.Observation);
        CollectionAssert.AreEqual(new[] { V3GenerationRetention.Referenced, V3GenerationRetention.MonthlyKeeper }, kept[2].Reasons.ToArray(),
            "referenced, and July's earliest held generation now that build 1 is gone");
        Assert.IsFalse(kept.ContainsKey(3), "older than 90 days, not referenced, not the keeper");
        CollectionAssert.AreEqual(new long[] { 1 }, decision.Absent.Select(static generation => generation.Observation).ToArray(),
            "build 1 is no longer held: it is absent, never claimed retained");
    }

    [TestMethod]
    public void AnEarlierBuildOfTheMountedBuildsDayIsNotItsDaysLast()
    {
        var log = Log("2026-10-01T08:00:00Z", "2026-10-01T09:00:00Z", "2026-10-01T10:00:00Z");
        var decision = V3GenerationRetention.Decide(log, Set(Digest(1), Digest(2)), Set());
        CollectionAssert.AreEqual(new long[] { 1 }, decision.Retained.Select(static generation => generation.Observation).ToArray(),
            "October's keeper only: neither earlier build is the last of the mounted build's day");
        CollectionAssert.AreEqual(new long[] { 2 }, decision.Dropped.Select(static generation => generation.Observation).ToArray());
    }

    [TestMethod]
    public void AGenesisLogHasNoGeneration()
    {
        var decision = V3GenerationRetention.Decide(Log("2026-10-01T08:00:00Z"), Set(), Set());
        Assert.AreEqual(0, decision.Retained.Count + decision.Dropped.Count + decision.Absent.Count);
        Assert.ThrowsExactly<ArgumentException>(() => V3GenerationRetention.Decide([], Set(), Set()));
    }
}
