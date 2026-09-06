using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The population run's one permitted second attempt fires for publisher unavailability and for
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// WHY A RETRY EXISTS AT ALL. The first complete 82-seed run met a terminal 503 on seed 32006L0112
/// at request ordinal 3, and that seed reached its manifest and record set unchanged on a re-run.
/// Decision 67 names a complete 500-to-599 response a publisher server failure, which is a fact
/// about the publisher rather than a defect in this route, so a population gate that stayed red for
/// it would be reporting the publisher's availability as this product's failure.
/// </para>
/// <para>
/// WHY IT NEEDS A TEST OF ITS OWN, and this is the point. A retry is exactly the shape that turns a
/// real refusal into a green run if its condition is even slightly too wide. So the condition is
/// pinned here rather than trusted: every refused family must carry a 5xx. A run mixing one 503 with
/// any other cause is NOT retried, because that other cause is precisely what the population exists
/// to find, and letting one 503 launder it would be the loophole. These cases are built from typed
/// refusal details rather than from a live run, so they hold the rule at every boundary a live run
/// would reach only by accident.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuPopulationRetryPolicyTests
{
    [TestMethod]
    public void EveryRefusedFamilyCarryingAFiveHundredIsPublisherUnavailability()
    {
        Assert.IsTrue(Refused([503]), "a single 503 is the publisher being unavailable.");
        Assert.IsTrue(Refused([500, 503, 599]), "every 5xx boundary counts.");
    }

    [TestMethod]
    public void AMixedRefusalIsNeverPublisherUnavailability()
    {
        // The loophole this exists to close: one 503 beside a real cause must not buy a re-run.
        Assert.IsFalse(
            Refused([503, null]),
            "a 503 beside a refusal carrying no status must not be retried; the second cause is "
                + "what the population run exists to find.");
        Assert.IsFalse(
            Refused([503, 404]),
            "a 503 beside a 404 must not be retried.");
    }

    [TestMethod]
    public void ARefusalOutsideTheFiveHundredsIsNeverPublisherUnavailability()
    {
        Assert.IsFalse(Refused([404]), "a 404 is not the publisher being unavailable.");
        Assert.IsFalse(Refused([499]), "499 is below the range.");
        Assert.IsFalse(Refused([600]), "600 is above the range.");
        Assert.IsFalse(Refused([null]), "a refusal naming no status states no publisher failure.");
    }

    [TestMethod]
    public void ARunWithNoRefusedFamilyIsNeverPublisherUnavailability()
    {
        // Vacuous truth is the other way this predicate could go wrong: "all refused families
        // carry a 5xx" is true of a run with no refused family at all, which must not authorise a
        // second attempt for a whole-run refusal raised somewhere else entirely.
        Assert.IsFalse(
            Refused([]),
            "a run whose families all proved refused for some other reason, and re-running it "
                + "would be re-running a cause this predicate never examined.");
    }

    private static bool Refused(int?[] terminalStatuses) =>
        EuStageOnePopulationRun.IsPublisherUnavailable(
            EuQueryExecutionResult.Refused(
                Topology(),
                terminalStatuses.Select(static (status, index) =>
                    EuFamilyEnumerationOutcome.ExecutorRefused(
                        $"fixture-family-{index}",
                        new EuEnumerationRefusalDetail(
                            EuEnumerationRefusal.StatusNotAdmitted,
                            null, null, status, null, null, null, null, null))).ToArray(),
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.ObjectFactsFamilyNotProven,
                    "a synthetic refusal for the retry policy test.")));

    private static SourceProfileTopology Topology() =>
        new(SourceCoreSchemaIds.SourceProfileTopology,
            new SourceArtifactRef(
                "urn:uuid:3f1a7c02-9d64-4a1e-8c77-1b2d5e9a4c31",
                "0000000000000000000000000000000000000000000000000000000000000000"),
            new SourceRegistryMemberRef(
                new SourceArtifactRef(
                    "urn:uuid:4a2b8d13-ae75-4b2f-9d88-2c3e6fab5d42",
                    "1111111111111111111111111111111111111111111111111111111111111111"),
                "single_publisher_store"));
}
