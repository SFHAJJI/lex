using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Where a resumed population says its replayed units were observed, when more than one journal is involved: a resume of
/// a run that was itself resuming dates them from the first run of the chain, a resume of a run that only renewed a
/// retained EU population leaves that population to the run that acquired it, and a mount's report states each half's
/// resumption on its own. Each run has its own clock, an hour after the one before, so a window taken from the wrong run
/// shows.
/// </summary>
public sealed partial class EuFirstMountAcquisitionTests
{
    [TestMethod]
    public async Task AResumeOfAnInterruptedResumeDatesItsReplayedUnitsFromTheFirstRun()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();

        // Run A: the consolidated fixture stopped at its second package.
        byte[] journalA;
        using (var stream = new MemoryStream())
        {
            await InterruptConsolidatedAsync(store, (header, budget) =>
                new AcquisitionJournal(stream, header, budget, new EuAcquisitionTestFixture.FixedTimeProvider()));
            journalA = stream.ToArray();
        }

        // Run B resumes A an hour later and is stopped in turn, at its first package request: its journal re-journals A's
        // units under its own lines and names A's resume record.
        var clockB = new EuAcquisitionTestFixture.FixedTimeProvider(TimeSpan.FromHours(1));
        var resumeA = await AcquisitionResume.OpenAsync(store, journalA, AcquisitionJournal.CurrentSource(), ResumeArguments,
            clockB, CancellationToken.None);
        var (europe, luxembourg) = await RenderersFromJournalAsync(store, resumeA);
        byte[] journalB;
        using (var stream = new MemoryStream())
        {
            using var stop = new CancellationTokenSource();
            using var handlerB = new ResumeRecordingHandler(ConsolidatedHandler(), ConsolidatedExpressions(), interruptAtPackageRequest: 1, stop);
            var budgetB = EuAcquisitionTestFixture.TestWireBudget();
            await using (var journal = new AcquisitionJournal(stream, new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(),
                ResumeArguments, budgetB.Limit, RendererRoles(europe, luxembourg), resumeA.Continuation), budgetB, clockB))
            {
                var acquisition = new EuFirstMountAcquisition(store, clockB, handlerB, journal, resumeA);
                Assert.IsNull(await acquisition.PrepareResumeAsync([ConsolidatedSeed], europe, null, CancellationToken.None));
                await Assert.ThrowsAsync<OperationCanceledException>(() =>
                    acquisition.RunAsync([ConsolidatedSeed], europe, budgetB, stop.Token));
            }

            Assert.AreEqual(1, handlerB.Count("package"), "run B stopped at its first package request");
            journalB = stream.ToArray();
        }

        var a = new AcquisitionResume(journalA);
        var b = new AcquisitionResume(journalB);
        Assert.IsTrue(b.StartedAt > a.LastJournaledAt, "each run has its own window, so taking the wrong one shows");

        // Run C resumes B two hours after A and completes.
        var clockC = new EuAcquisitionTestFixture.FixedTimeProvider(TimeSpan.FromHours(2));
        var resumeB = await AcquisitionResume.OpenAsync(store, journalB, AcquisitionJournal.CurrentSource(), ResumeArguments,
            clockC, CancellationToken.None);
        using var handler = new ResumeRecordingHandler(ConsolidatedHandler(), ConsolidatedExpressions());
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        EuFirstMountAcquisitionResult result;
        using (var stream = new MemoryStream())
        {
            await using var journal = new AcquisitionJournal(stream, new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(),
                ResumeArguments, budget.Limit, RendererRoles(europe, luxembourg), resumeB.Continuation), budget, clockC);
            var acquisition = new EuFirstMountAcquisition(store, clockC, handler, journal, resumeB);
            Assert.IsNull(await acquisition.PrepareResumeAsync([ConsolidatedSeed], europe, null, CancellationToken.None));
            result = await acquisition.RunAsync([ConsolidatedSeed], europe, budget, CancellationToken.None);
        }

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.AreEqual(0, handler.Count("adapter"), "the adapter run A acquired is replayed through B's journal");

        // The replayed units were observed by A and B: the window starts at A's first line and ends at B's last, and the
        // spend bounds are both runs' together.
        var resumption = result.Resumption ?? throw new AssertFailedException("A resumed population says so in its catalog.");
        Assert.AreEqual(resumeB.JournalSha256, resumption.JournalSha256);
        Assert.AreEqual(2, resumption.PreviousRuns);
        Assert.AreEqual(a.StartedAt, resumption.PreviousStartedAt, "the window starts with the first run, not with the run that resumed it");
        Assert.AreEqual(b.LastJournaledAt, resumption.PreviousLastJournaledAt);
        Assert.AreEqual(a.LastWireSpent + b.LastWireSpent, resumption.PreviousWireSpentAtLeast);
        Assert.AreEqual(a.Header.WireCeiling + b.Header.WireCeiling, resumption.PreviousWireCeiling);

        // The catalog's reopen holds that resumption to the resume record it names.
        var reopened = await EuFirstMountAcquisition.ReopenAsync(store, result.CheckpointRef!, [ConsolidatedSeed], CancellationToken.None);
        Assert.IsTrue(reopened.Delivered, reopened.Detail);
        Assert.AreEqual(ContractJson.Serialize(resumption), ContractJson.Serialize(reopened.Resumption));

        // A journal whose header names a resume record for another line of the journal it resumed does not describe its
        // chain, and refuses before anything is held or requested.
        var resumedFrom = b.Header.ResumedFrom ?? throw new AssertFailedException("Run B's journal names the journal it resumed.");
        var foreign = await RejournalAsync(journalB, b.Header with { ResumedFrom = resumedFrom with { LastSeq = resumedFrom.LastSeq + 1 } },
            static _ => { });
        var error = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => AcquisitionResume.OpenAsync(store, foreign,
            AcquisitionJournal.CurrentSource(), ResumeArguments, clockC, CancellationToken.None));
        StringAssert.Contains(error.Message, "describes another journal");
    }

    [TestMethod]
    public async Task AResumeOfARunThatOnlyRenewedARetainedPopulationLeavesItToTheRunThatAcquiredIt()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var europe = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var luxembourg = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);

        // The EU run acquires the population and is not interrupted.
        var acquired = await Acquisition(store, ConsolidatedHandler()).RunAsync([ConsolidatedSeed], europe,
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(acquired.Delivered, $"{acquired.Refusal}: {acquired.Detail}");
        var retained = acquired.CheckpointRef!;
        var arguments = AcquisitionJournal.DigestArguments([ConsolidatedSeed], LuxembourgFirstMountAcquisitionTests.ActRange, null, "raw", retained);

        // A later run reuses it, as the Luxembourg runner does, journals the renewed catalog and is interrupted afterwards.
        var clockB = new EuAcquisitionTestFixture.FixedTimeProvider(TimeSpan.FromHours(1));
        byte[] journalB;
        using (var stream = new MemoryStream())
        {
            var budget = EuAcquisitionTestFixture.TestWireBudget();
            await using (var journal = new AcquisitionJournal(stream, new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(),
                arguments, budget.Limit, RendererRoles(europe, luxembourg), null), budget, clockB))
            {
                var renewed = await new EuFirstMountAcquisition(store, clockB, ConsolidatedHandler(), journal).ReuseAsync(retained,
                    [ConsolidatedSeed], europe.DocumentFetch.CopyBytes(), budget, CancellationToken.None);
                Assert.IsTrue(renewed.Delivered, $"{renewed.Refusal}: {renewed.Detail}");
            }

            journalB = stream.ToArray();
        }

        // Its resume reuses the catalog that run journaled. The EU population was observed by the run that acquired it, not
        // by the interrupted run: the EU half names no resumption.
        var clockC = new EuAcquisitionTestFixture.FixedTimeProvider(TimeSpan.FromHours(2));
        var resume = await AcquisitionResume.OpenAsync(store, journalB, AcquisitionJournal.CurrentSource(), arguments, clockC,
            CancellationToken.None);
        using var handler = new ResumeRecordingHandler(ConsolidatedHandler(), ConsolidatedExpressions());
        EuFirstMountAcquisitionResult result;
        using (var stream = new MemoryStream())
        {
            var budget = EuAcquisitionTestFixture.TestWireBudget();
            await using var journal = new AcquisitionJournal(stream, new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(),
                arguments, budget.Limit, RendererRoles(europe, luxembourg), resume.Continuation), budget, clockC);
            var acquisition = new EuFirstMountAcquisition(store, clockC, handler, journal, resume);
            var journaled = await acquisition.PrepareResumeAsync([ConsolidatedSeed], null, retained, CancellationToken.None)
                ?? throw new AssertFailedException("The interrupted run journaled its renewed catalog.");
            Assert.AreNotEqual(retained, journaled);
            result = await acquisition.ReuseAsync(journaled, [ConsolidatedSeed], europe.DocumentFetch.CopyBytes(), budget,
                CancellationToken.None);
        }

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        CollectionAssert.AreEqual(new[] { "rights" }, handler.Kinds.Where(static kind => kind != "robots").ToArray(),
            "only this build's own rights notice went to the publisher");
        Assert.IsNull(result.Resumption, "the interrupted run only renewed the population; it did not observe it");
        var reopened = await EuFirstMountAcquisition.ReopenAsync(store, result.CheckpointRef!, [ConsolidatedSeed], CancellationToken.None);
        Assert.IsTrue(reopened.Delivered, reopened.Detail);
        Assert.IsNull(reopened.Resumption);
    }

    [TestMethod]
    public void AResumedMountsReportStatesEachHalfsOwnResumption()
    {
        var europe = SyntheticResumption('e', runs: 2, startedHoursAfterEpoch: 0, phase: AcquisitionJournal.EuropeFormexPackagePhase);
        var luxembourg = SyntheticResumption('l', runs: 1, startedHoursAfterEpoch: 30, phase: AcquisitionJournal.LuxembourgDocumentPhase);

        var both = JsonNode.Parse(JsonSerializer.Serialize(V3CorpusMountWriter.Resumption(europe, luxembourg)))!;
        foreach (var (half, expected) in new[] { ("europe", europe), ("luxembourg", luxembourg) })
        {
            var node = both[half]!;
            Assert.AreEqual(expected.JournalSha256, node["resumedFrom"]!["journalSha256"]!.GetValue<string>(), half);
            Assert.AreEqual(expected.Record.Sha256, node["resumedFrom"]!["record"]!["Sha256"]!.GetValue<string>(), half);
            Assert.AreEqual(expected.PreviousRuns, node["earlierRuns"]!["runs"]!.GetValue<int>(), half);
            Assert.AreEqual(expected.PreviousStartedAt, node["earlierRuns"]!["startedAt"]!.GetValue<DateTimeOffset>(), half);
            Assert.AreEqual(expected.PreviousLastJournaledAt, node["earlierRuns"]!["lastJournaledAt"]!.GetValue<DateTimeOffset>(), half);
            Assert.AreEqual(expected.PreviousWireSpentAtLeast, node["earlierRuns"]!["wireSpentAtLeast"]!.GetValue<int>(), half);
            Assert.AreEqual(expected.ResumedAt, node["resumedAt"]!.GetValue<DateTimeOffset>(), half);
            Assert.AreEqual(expected.Phases[0].Phase, node["phases"]![0]!["phase"]!.GetValue<string>(), half);
        }

        // No window or journal is stated for the mount as a whole.
        Assert.IsNull(both["resumedFrom"]);
        Assert.IsNull(both["earlierRuns"]);
        StringAssert.Contains(both["statement"]!.GetValue<string>(), "This population was not observed in one window.");

        // A half that was not resumed names no resumption, and a mount resumed in neither half reports none at all.
        var one = JsonNode.Parse(JsonSerializer.Serialize(V3CorpusMountWriter.Resumption(null, luxembourg)))!;
        Assert.IsNull(one["europe"]);
        Assert.AreEqual(luxembourg.JournalSha256, one["luxembourg"]!["resumedFrom"]!["journalSha256"]!.GetValue<string>());
        Assert.IsNull(V3CorpusMountWriter.Resumption(null, null));
    }

    private static AcquisitionResumption SyntheticResumption(char digit, int runs, int startedHoursAfterEpoch, string phase)
    {
        var epoch = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        return new AcquisitionResumption(
            new Lex.V3.Contracts.Source.Core.SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string(digit == 'e' ? 'a' : 'b', 64)),
            new string(digit == 'e' ? 'c' : 'd', 64), 41 + runs, epoch.AddHours(startedHoursAfterEpoch),
            epoch.AddHours(startedHoursAfterEpoch + 5), 100 * runs, 45000 * runs, runs, epoch.AddHours(startedHoursAfterEpoch + 6),
            [new AcquisitionResumedPhase(phase, 3, 2)], 7, epoch.AddHours(startedHoursAfterEpoch + 9));
    }

    // The renderer sources a resumed run reopens from its journal, after the checkout is checked to hold the same bytes.
    private static async Task<(EuRendererSources Europe, LuxembourgRendererSources Luxembourg)> RenderersFromJournalAsync(
        ICustodyStore store, AcquisitionResume resume)
    {
        var held = await resume.VerifyRenderersAsync(CheckoutRoot(),
            [.. EuRendererSources.RendererFiles, .. LuxembourgRendererSources.RendererFiles], CancellationToken.None);
        var europe = await EuRendererSources.FromCustodyAsync(store,
            EuRendererSources.RendererFiles.ToDictionary(file => file, file => held[file], StringComparer.Ordinal), CancellationToken.None);
        var luxembourg = await LuxembourgRendererSources.FromCustodyAsync(store,
            LuxembourgRendererSources.RendererFiles.ToDictionary(file => file, file => held[file], StringComparer.Ordinal), CancellationToken.None);
        return (europe, luxembourg);
    }
}
