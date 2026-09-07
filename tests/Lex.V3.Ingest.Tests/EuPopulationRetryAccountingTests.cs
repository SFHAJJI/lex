using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The deferred second-attempt counter checks itself, because it was wrong in a run that passed.
/// </summary>
/// <remarks>
/// <para>
/// WHAT WENT WRONG, and why a green run did not show it. The deferred pass wrote each second
/// attempt back with <c>records[records.IndexOf(record)]</c>. <c>SeedRecord</c> is a record type,
/// so that is an equality search rather than a positional write, and the population report ended up
/// naming three seeds as having taken a second attempt while five per-seed files on disk carried
/// <c>attempts=2</c>. Every seed still reached, so every assertion in the run held and nothing
/// failed; the undercount surfaced only when the report was reconciled by hand against the files it
/// was built from.
/// </para>
/// <para>
/// That counter is not decoration. It exists so that an inflated retry rate would be visible as a
/// finding about our own traffic rather than the publisher's, which means a counter that silently
/// undercounts is worse than no counter at all: it reports restraint we did not exercise. The
/// repair carries the index instead of searching for it, and this guard asserts the property the
/// search was silently breaking.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuPopulationRetryAccountingTests
{
    private static EuStageOnePopulationRun.SeedRecord Record(string celex, int attempts) =>
        new(
            (celex, "http://publications.europa.eu/resource/cellar/" + celex),
            0,
            attempts,
            true,
            null,
            null,
            new JsonObject(),
            new JsonObject());

    [TestMethod]
    public void ExactlyTheDeferredSeedsCarryASecondAttempt()
    {
        var records = new[]
        {
            Record("A", 1), Record("B", 2), Record("C", 1), Record("D", 2),
        };

        EuStageOnePopulationRun.AssertDeferredAccounting(records, [1, 3]);
    }

    [TestMethod]
    public void ADeferredSeedWhoseWriteBackMissedIsRefused()
    {
        // The exact shape of the defect: two seeds were deferred and a second attempt ran for both,
        // but only one write-back landed, so the counter would have reported one.
        var records = new[]
        {
            Record("A", 1), Record("B", 2), Record("C", 1), Record("D", 1),
        };

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => EuStageOnePopulationRun.AssertDeferredAccounting(records, [1, 3]));
        Assert.IsTrue(
            error.Message.Contains("deferred [1,3]", StringComparison.Ordinal),
            "the refusal must name what was deferred, so the miss can be located.");
        Assert.IsTrue(
            error.Message.Contains("attempts>1 at [1]", StringComparison.Ordinal),
            "and what was actually written back.");
    }

    [TestMethod]
    public void ASeedRetriedWithoutBeingDeferredIsRefused()
    {
        // The converse, which matters just as much: a retry nobody authorised is a claim about our
        // own traffic that the population must not make silently.
        var records = new[] { Record("A", 1), Record("B", 2), Record("C", 2) };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => EuStageOnePopulationRun.AssertDeferredAccounting(records, [1]));
    }

    [TestMethod]
    public void AThirdAttemptIsRefusedRatherThanCountedAsASecond()
    {
        // FINDING 1, and the reviewer proved it against my first repair: checking only which
        // positions retried let Attempts=3 read as an ordinary second attempt. The ceiling is
        // exactly one extra request, and it is the whole reason this field is trustworthy as a
        // statement about our own traffic.
        var records = new[] { Record("A", 1), Record("B", 3), Record("C", 1) };

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => EuStageOnePopulationRun.AssertDeferredAccounting(records, [1]));
        Assert.IsTrue(
            error.Message.Contains("one-extra-attempt ceiling", StringComparison.Ordinal),
            "the refusal must say the ceiling was exceeded, not merely that a set disagreed.");
        Assert.IsTrue(
            error.Message.Contains("1:3", StringComparison.Ordinal),
            "and name the position and the count it actually reached.");
    }

    [TestMethod]
    public void ADeferredSeedThatWasNeverRerunIsRefused()
    {
        // The converse of the ceiling: deferred but still at one attempt means the write-back
        // never happened for that seed, which is the original defect's own signature.
        var records = new[] { Record("A", 1), Record("B", 1) };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => EuStageOnePopulationRun.AssertDeferredAccounting(records, [1]));
    }

    // ---- FINDING 2: the write-back loop itself, driven with no publisher. ----

    private static EuStageOnePopulationRun.SeedRecord Refused(string celex) =>
        new((celex, "http://publications.europa.eu/resource/cellar/" + celex),
            0, 1, false, null, null, new JsonObject(), new JsonObject());

    [TestMethod]
    public async Task TheDeferredPassRerunsEachSelectedSeedAtItsOwnPositionExactlyOnce()
    {
        // This is the loop the observed defect happened in. Until the deferred pass was extracted
        // behind an injectable runner it was reachable only through a two-hour publisher run, so
        // deleting its call site stayed green -- which the reviewer demonstrated rather than argued.
        var records = new List<EuStageOnePopulationRun.SeedRecord>
        {
            Record("A", 1), Refused("B"), Record("C", 1), Refused("D"),
        };
        var runFor = new List<string>();

        var touched = await EuStageOnePopulationRun.RunDeferredSecondAttemptsAsync(
            records,
            [1, 3],
            (record, index) =>
            {
                runFor.Add($"{record.Seed.Celex}@{index}");
                return Task.FromResult(Record(record.Seed.Celex, 2));
            });

        CollectionAssert.AreEqual(new[] { "B@1", "D@3" }, runFor.ToArray(),
            "each selected seed is rerun once, at its own index.");
        CollectionAssert.AreEqual(new[] { 1, 3 }, touched.ToArray());
        CollectionAssert.AreEqual(
            new[] { "A", "B", "C", "D" },
            records.Select(record => record.Seed.Celex).ToArray(),
            "the write-back replaces in place: no seed moves, is duplicated, or is lost.");
        CollectionAssert.AreEqual(
            new[] { 1, 2, 1, 2 },
            records.Select(record => record.Attempts).ToArray());
    }

    [TestMethod]
    public async Task AWriteBackThatLandsOnTheWrongPositionIsRefused()
    {
        // The exact mechanism of the original defect, reproduced deliberately: the rerun result is
        // written somewhere other than the seed it belongs to. Before the accounting checked
        // itself, a run like this reported a lower retry count and passed.
        var records = new List<EuStageOnePopulationRun.SeedRecord>
        {
            Record("A", 1), Refused("B"), Record("C", 1),
        };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            EuStageOnePopulationRun.RunDeferredSecondAttemptsAsync(
                records,
                [1],
                (record, _) =>
                {
                    // Answer with a record that leaves the deferred seed untouched.
                    records[0] = Record("A", 2);
                    return Task.FromResult(record);
                }));
    }
}
