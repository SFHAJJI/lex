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
}
