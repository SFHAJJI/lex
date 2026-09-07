using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A population run's deferred second attempts must be readable in the accounting that reports
/// them.
/// </summary>
/// <remarks>
/// <para>
/// THE RUN THAT MADE THIS NECESSARY. Run 8 at <c>e7ee8b35</c> ran five deferred second attempts and
/// its report named three. Both facts are on disk: the run's custody root holds 87 directories, 82
/// first attempts and a <c>-attempt-2</c> for each of 12012E/TXT, 32015L2366, 32016L0680,
/// 32016R1011 and 32022L2523, while <c>seedsNeedingASecondAttempt</c> reads 3 and the aggregate
/// rows for 12012E/TXT and 32022L2523 read <c>attempts: 1</c>. Those two seeds' own per-seed files
/// read <c>attempts: 2</c> and still carry the first attempt's refusal, so the evidence survived
/// per seed and was lost in the aggregate.
/// </para>
/// <para>
/// WHY THE RUN COULD NOT SEE IT. Nothing threw, no seed was lost, the report held 82 distinct
/// celexes and 82 distinct ordinals, and the population passed green. The only symptom was a number
/// that was too small, in the one field whose entire purpose is to disclose how much extra traffic
/// this run put on the publisher. A number wrong low in a disclosure field is not something a later
/// reader can detect, which is why this is pinned against what actually ran rather than trusted.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuPopulationSecondAttemptAccountingTests
{
    [TestMethod]
    public void TheShapeRunEightActuallyProducedIsCaught()
    {
        var records = EightyTwoSeeds();
        records[0] = ("12012E/TXT", 1);
        records[35] = ("32015L2366", 2);
        records[37] = ("32016L0680", 2);
        records[40] = ("32016R1011", 2);
        records[64] = ("32022L2523", 1);

        var violations = EuStageOnePopulationRun.DeferredSecondAttemptViolations(
            records, RunEightsFiveDeferredSeeds);

        Assert.IsNotEmpty(
            violations,
            "run 8 ran five second attempts and recorded three, and this is the check that has to "
                + "say so.");
        Assert.IsTrue(
            violations.Any(static violation => violation.Contains("12012E/TXT", StringComparison.Ordinal)),
            $"12012E/TXT's lost second attempt must be named: {string.Join("; ", violations)}");
        Assert.IsTrue(
            violations.Any(static violation => violation.Contains("32022L2523", StringComparison.Ordinal)),
            $"32022L2523's lost second attempt must be named: {string.Join("; ", violations)}");
        Assert.IsTrue(
            violations.Any(static violation => violation.Contains("5 second attempts ran and 3", StringComparison.Ordinal)),
            $"the disclosed retry count must be named as wrong: {string.Join("; ", violations)}");
        Assert.IsFalse(
            violations.Any(static violation => violation.Contains("32015L2366", StringComparison.Ordinal)),
            "the three second attempts that did land must not be reported as violations.");
    }

    [TestMethod]
    public void ARunWhereEverySecondAttemptLandedIsSilent()
    {
        // The same five seeds, each readable at attempts=2. This is what run 8 should have
        // produced, and it must pass, or the check above would be satisfied by a rule that simply
        // always fails.
        var records = EightyTwoSeeds();
        foreach (var (slot, celex) in RunEightsFiveDeferredSeeds)
        {
            records[slot] = (celex, 2);
        }

        Assert.IsEmpty(
            EuStageOnePopulationRun.DeferredSecondAttemptViolations(records, RunEightsFiveDeferredSeeds),
            "a run whose every deferred second attempt is readable in its own seed's record has "
                + "nothing to report.");
    }

    [TestMethod]
    public void ASecondAttemptWrittenIntoAnotherSeedsSlotIsNamedWithBothSeeds()
    {
        // The other way a slot-addressed replacement can go wrong: the retry lands, and lands on
        // the wrong seed. That corrupts a seed that was never retried, so both names belong in the
        // violation rather than only the one that was retried.
        var records = EightyTwoSeeds();
        records[35] = ("32016L0680", 2);

        var violations = EuStageOnePopulationRun.DeferredSecondAttemptViolations(
            records, [(35, "32015L2366")]);

        Assert.IsNotEmpty(violations, "a retry landing on the wrong seed is a violation.");
        Assert.IsTrue(
            violations.Any(static violation =>
                violation.Contains("32015L2366", StringComparison.Ordinal)
                && violation.Contains("32016L0680", StringComparison.Ordinal)),
            $"both the retried seed and the seed it overwrote must be named: {string.Join("; ", violations)}");
    }

    [TestMethod]
    public void ASecondAttemptAddressedOutsideThePopulationIsCaughtRatherThanThrown()
    {
        // A slot outside the run is a harness fault, and it must be reported as one rather than
        // ending the population with an index exception after two hours of publisher traffic.
        var violations = EuStageOnePopulationRun.DeferredSecondAttemptViolations(
            EightyTwoSeeds(), [(82, "32015L2366")]);

        Assert.IsNotEmpty(violations, "a slot outside the population is a violation.");
        Assert.IsTrue(
            violations.Any(static violation => violation.Contains("outside the 82 seeds", StringComparison.Ordinal)),
            $"the out-of-range slot must be named: {string.Join("; ", violations)}");
    }

    [TestMethod]
    public void ASecondAttemptRecordedForASeedThatNeverRanOneIsCaught()
    {
        // The count runs in both directions. A record claiming a retry that no deferred seed asked
        // for would overstate this run's traffic, and that is a finding too.
        var records = EightyTwoSeeds();
        records[7] = ("seed-7", 2);

        Assert.IsNotEmpty(
            EuStageOnePopulationRun.DeferredSecondAttemptViolations(records, []),
            "a recorded second attempt that no deferred seed ran must not pass unremarked.");
    }

    /// <summary>The five seeds run 8 actually retried, at the ordinals its own report gives them.</summary>
    private static readonly (int Slot, string Celex)[] RunEightsFiveDeferredSeeds =
    [
        (0, "12012E/TXT"),
        (35, "32015L2366"),
        (37, "32016L0680"),
        (40, "32016R1011"),
        (64, "32022L2523"),
    ];

    private static (string Celex, int Attempts)[] EightyTwoSeeds() =>
        [.. Enumerable.Range(0, 82).Select(static ordinal => ($"seed-{ordinal}", 1))];
}
