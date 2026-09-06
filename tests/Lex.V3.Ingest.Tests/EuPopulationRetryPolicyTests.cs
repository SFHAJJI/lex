using System.Text.Json.Nodes;
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

    [TestMethod]
    public void AWitnessRefusalIsClassifiedFromItsOwnStatus()
    {
        // The gap the first gated run found: seed 12016E/TXT refused witness_traversal_refused
        // with every family proved, so the family clause had nothing to inspect and the seed was
        // not retried. The witness carries its own status now, and it is read rather than parsed
        // out of the whole-run refusal's prose.
        Assert.IsTrue(WitnessRefused(503), "a witness 503 is the publisher being unavailable.");
        Assert.IsTrue(WitnessRefused(500), "the bottom of the range counts.");
        Assert.IsFalse(WitnessRefused(404), "a witness 404 is not unavailability.");
        Assert.IsFalse(
            WitnessRefused(null),
            "a witness refusal naming no status states no publisher failure, which is exactly the "
                + "shape that was unreadable before the status was carried.");
    }

    [TestMethod]
    public void ADeferredAttemptsReportCarriesTheFirstAttemptsIndexWithoutStealingItsParent()
    {
        // THE BUG THIS STANDS AGAINST COST A COMPLETE 82-SEED RUN. A JsonNode may have one parent.
        // The first attempt's index is already a child of the first attempt's own report, so
        // attaching it to the second attempt's report throws InvalidOperationException. That threw
        // out of the deferred pass, past the catch that only covered the RUN, and the population
        // wrote all 82 measured seed files and then produced no report at all.
        //
        // Driven through the real builder, with a first report actually built first, because the
        // node only acquires a parent by being put into one. A test that passed a fresh index would
        // pass under the defect.
        var seed = ("32014R0600", "http://publications.europa.eu/resource/cellar/fixture");
        var firstIndex = new JsonObject { ["wholeRunRefusalCode"] = "object_facts_family_not_proven" };
        var firstReport = EuStageOnePopulationRun.BuildSeedReport(
            seed, 0, 1000, false, 1, null, firstIndex, null);
        Assert.IsNotNull(firstReport["index"], "the first report must own its own index.");

        var secondIndex = new JsonObject { ["wholeRunRefusalCode"] = null };
        var secondReport = EuStageOnePopulationRun.BuildSeedReport(
            seed, 0, 2000, true, 2, null, secondIndex, firstIndex);

        Assert.AreEqual(
            "object_facts_family_not_proven",
            secondReport["firstAttemptIndex"]?["wholeRunRefusalCode"]?.GetValue<string>(),
            "the second attempt's report must carry the first attempt's own refusal.");
        Assert.IsNotNull(
            firstReport["index"],
            "carrying the first attempt's index forward must not detach it from the first report, "
                + "which is what a move rather than a clone would do.");
        Assert.AreEqual(2, secondReport["attempts"]!.GetValue<int>());
    }

    private static bool WitnessRefused(int? terminalStatus) =>
        EuStageOnePopulationRun.IsPublisherUnavailable(
            EuQueryExecutionResult.Refused(
                Topology(),
                [],
                new EuQueryExecutionRefusalDetail(
                    EuQueryExecutionRefusal.WitnessTraversalRefused,
                    "a synthetic witness refusal for the retry policy test."),
                witnessTraversalRefusal: new EuWitnessTraversalRefusalDetail(
                    EuWitnessTraversalRefusal.StatusNotAdmitted, null, terminalStatus)));

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
