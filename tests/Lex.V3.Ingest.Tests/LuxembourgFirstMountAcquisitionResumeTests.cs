using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Resuming an interrupted Luxembourg acquisition from its progress journal. The journal names the scope and plan
/// identity, the vocabulary checkpoint, each proven family and each executed document or Gazette GET; a resumed run
/// admits each through the checked reader a full replay uses, provided it still binds to this run's plan, range,
/// renderer and selection, and acquires again whatever does not. Nothing the journal names is let through on its say.
/// </summary>
public sealed partial class LuxembourgFirstMountAcquisitionTests
{
    private static readonly string LuxembourgResumeArguments = AcquisitionJournal.DigestArguments(
        [], LuxembourgActRange.WholePopulation, null, "raw", null);

    [TestMethod]
    public async Task AnInterruptedPopulationResumesFetchingOnlyWhatItsJournalDoesNotName()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), LuxembourgResumeArguments, budget.Limit,
            LuxembourgRendererRoles(renderers), null);

        // The interrupted run: the two-work population stopped at its second document GET, as a kill would stop it. Its
        // journal names the scope, the vocabulary, the three families and the first document; the GET in flight has no
        // line.
        byte[] interruptedJournal;
        using (var stream = new MemoryStream())
        {
            using var stop = new CancellationTokenSource();
            using var interrupted = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true, includeBlankNode: true);
            using var interrupting = new InterruptingDocumentHandler(interrupted, interruptAtDocument: 2, stop);
            await using (var journal = new AcquisitionJournal(stream, header, budget, new LuxembourgAcquisitionTestFixture.FixedTimeProvider()))
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => new LuxembourgFirstMountAcquisition(store,
                        new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), interrupting, journal)
                    .RunAsync(LuxembourgActRange.WholePopulation, renderers, budget, stop.Token));
            }

            Assert.HasCount(1, interrupted.DocumentRequests, "the run stopped at its second document GET");
            interruptedJournal = stream.ToArray();
        }

        var time = new LuxembourgAcquisitionTestFixture.FixedTimeProvider();
        var resume = await AcquisitionResume.OpenAsync(store, interruptedJournal, AcquisitionJournal.CurrentSource(),
            LuxembourgResumeArguments, time, CancellationToken.None);
        Assert.AreEqual(1, resume.Count(AcquisitionJournal.LuxembourgScopePhase));
        Assert.AreEqual(1, resume.Count(AcquisitionJournal.LuxembourgVocabularyPhase));
        Assert.AreEqual(3, resume.Count(AcquisitionJournal.LuxembourgQueryFamilyPhase));
        Assert.AreEqual(1, resume.Count(AcquisitionJournal.LuxembourgDocumentPhase));
        Assert.AreEqual(0, resume.Count(AcquisitionJournal.LuxembourgGazettePhase));

        // The resumed run sends no SPARQL at all (the vocabulary and every family are restored from their checkpoints)
        // and fetches only the document the interrupted run had not.
        using var handler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true, includeBlankNode: true);
        var resumedBudget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        LuxembourgFirstMountAcquisitionResult resumed;
        byte[] continuedJournal;
        using (var stream = new MemoryStream())
        {
            await using (var journal = new AcquisitionJournal(stream, header with { ResumedFrom = resume.Continuation }, resumedBudget, time))
            {
                resumed = await new LuxembourgFirstMountAcquisition(store, time, handler, journal, resume)
                    .RunAsync(LuxembourgActRange.WholePopulation, renderers, resumedBudget, CancellationToken.None);
            }

            continuedJournal = stream.ToArray();
        }

        Assert.IsTrue(resumed.Delivered, $"{resumed.Refusal}: {resumed.Detail}");
        Assert.IsEmpty(handler.FamiliesSeen, "the vocabulary and every family were restored, not enumerated");
        Assert.HasCount(1, handler.DocumentRequests, "only the GET in flight at the stop went to the publisher");
        Assert.AreEqual(2, resumed.Run!.CorpusRecordSet!.Set.Records.Count(static record => record.Body.Kind == CorpusBodyRecordKind.Held));

        // The catalog says it resumed, from what, and what each phase replayed and acquired live; its reopen holds that
        // to the resume record and rederives the same population.
        var resumption = resumed.Resumption ?? throw new AssertFailedException("A resumed acquisition says so in its catalog.");
        Assert.AreEqual(resume.JournalSha256, resumption.JournalSha256);
        Assert.AreEqual(resume.Record, resumption.Record);
        CollectionAssert.AreEqual(
            new[]
            {
                AcquisitionJournal.LuxembourgScopePhase + " 1 0", AcquisitionJournal.LuxembourgVocabularyPhase + " 1 0",
                AcquisitionJournal.LuxembourgQueryFamilyPhase + " 3 0", AcquisitionJournal.LuxembourgCoverLeafPhase + " 0 0",
                AcquisitionJournal.LuxembourgDocumentPhase + " 1 1",
                AcquisitionJournal.LuxembourgGazettePhase + " 0 0",
            },
            Tallies(resumption));
        var reopened = await LuxembourgFirstMountAcquisition.ReopenAsync(store, resumed.CheckpointRef!,
            LuxembourgActRange.WholePopulation, CancellationToken.None);
        Assert.IsTrue(reopened.Delivered, reopened.Detail);
        Assert.AreEqual(ContractJson.Serialize(resumption), ContractJson.Serialize(reopened.Resumption));
        Assert.AreEqual(resumed.Run.CorpusRecordSetRef, reopened.Run!.CorpusRecordSetRef);
        Assert.AreEqual(resumed.AknInventory!.IdentitySha256, reopened.AknInventory!.IdentitySha256);

        // The resumed run's own journal names the journal it resumed and every unit, replayed or live.
        var continued = new AcquisitionResume(continuedJournal);
        Assert.AreEqual(resume.JournalSha256, continued.Header.ResumedFrom!.JournalSha256);
        Assert.AreEqual(1, continued.Count(AcquisitionJournal.LuxembourgScopePhase));
        Assert.AreEqual(1, continued.Count(AcquisitionJournal.LuxembourgVocabularyPhase));
        Assert.AreEqual(3, continued.Count(AcquisitionJournal.LuxembourgQueryFamilyPhase));
        Assert.AreEqual(2, continued.Count(AcquisitionJournal.LuxembourgDocumentPhase));
    }

    [TestMethod]
    public async Task AJournaledFamilyThatDoesNotRestoreForItsRangeIsEnumeratedAgain()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var arguments = AcquisitionJournal.DigestArguments([], null, LuxembourgPopulationScope.Legislative, "raw", null);
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), arguments, budget.Limit,
            LuxembourgRendererRoles(renderers), null);

        // A complete run's journal, as a kill after its last unit would leave it, with the loi range's census and
        // assertion families' retained covers exchanged: each still reads back, and neither restores for the range it is
        // offered.
        byte[] journal;
        using (var stream = new MemoryStream())
        {
            using var complete = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
            await using (var writer = new AcquisitionJournal(stream, header, budget, new LuxembourgAcquisitionTestFixture.FixedTimeProvider()))
            {
                var captured = await new LuxembourgFirstMountAcquisition(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
                    complete, writer).RunPopulationAsync(LuxembourgPopulationScope.Legislative, renderers, budget, CancellationToken.None);
                Assert.IsTrue(captured.Delivered, captured.Detail);
                Assert.IsNull(captured.Resumption, "a run that did not resume writes its catalog unchanged");
            }

            journal = await EuFirstMountAcquisitionTests.RejournalAsync(stream.ToArray(), header, units =>
            {
                var census = units.Single(static unit =>
                    unit.Phase == AcquisitionJournal.LuxembourgQueryFamilyPhase && unit.Key == "legislative-loi-s");
                var assertions = units.Single(static unit =>
                    unit.Phase == AcquisitionJournal.LuxembourgQueryFamilyPhase && unit.Key == "legislative-loi-a");
                var checkpoint = census.Payload["checkpoint"]!.DeepClone();
                census.Payload["checkpoint"] = assertions.Payload["checkpoint"]!.DeepClone();
                assertions.Payload["checkpoint"] = checkpoint;
                // Without their delivered leaves too: a family whose leaves restore is not enumerated again (see
                // AFamilyWhoseRecordDoesNotRestoreIsRebuiltFromItsDeliveredLeaves).
                units.RemoveAll(static unit => unit.Phase == AcquisitionJournal.LuxembourgCoverLeafPhase &&
                    (unit.Key.StartsWith("legislative-loi-s#", StringComparison.Ordinal) ||
                        unit.Key.StartsWith("legislative-loi-a#", StringComparison.Ordinal)));
            });
        }

        var time = new LuxembourgAcquisitionTestFixture.FixedTimeProvider();
        var resume = await AcquisitionResume.OpenAsync(store, journal, AcquisitionJournal.CurrentSource(), arguments, time,
            CancellationToken.None);
        var journaledDocuments = resume.Count(AcquisitionJournal.LuxembourgDocumentPhase);
        var journaledGazette = resume.Count(AcquisitionJournal.LuxembourgGazettePhase);
        Assert.AreEqual(9, resume.Count(AcquisitionJournal.LuxembourgQueryFamilyPhase));
        using var handler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
        var resumedBudget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        LuxembourgFirstMountAcquisitionResult resumed;
        using (var stream = new MemoryStream())
        {
            await using var continued = new AcquisitionJournal(stream, header with { ResumedFrom = resume.Continuation }, resumedBudget, time);
            resumed = await new LuxembourgFirstMountAcquisition(store, time, handler, continued, resume)
                .RunPopulationAsync(LuxembourgPopulationScope.Legislative, renderers, resumedBudget, CancellationToken.None);
        }

        // Only the two exchanged families went back to the publisher; the vocabulary and the other seven were restored.
        // What the documents and Gazette replayed or fetched again depends on whether the selection those two families
        // rebuild is the same; either way the tallies account for every GET the publisher saw.
        Assert.IsTrue(resumed.Delivered, $"{resumed.Refusal}: {resumed.Detail}");
        CollectionAssert.AreEquivalent(new[] { "S", "A" }, handler.FamiliesSeen.ToArray());
        var resumption = resumed.Resumption ?? throw new AssertFailedException("A resumed acquisition says so in its catalog.");
        var phases = resumption.Phases.ToDictionary(static phase => phase.Phase, StringComparer.Ordinal);
        Assert.AreEqual(1, phases[AcquisitionJournal.LuxembourgScopePhase].Replayed);
        Assert.AreEqual(1, phases[AcquisitionJournal.LuxembourgVocabularyPhase].Replayed);
        Assert.AreEqual(7, phases[AcquisitionJournal.LuxembourgQueryFamilyPhase].Replayed);
        Assert.AreEqual(2, phases[AcquisitionJournal.LuxembourgQueryFamilyPhase].Live);
        var documents = phases[AcquisitionJournal.LuxembourgDocumentPhase];
        var gazette = phases[AcquisitionJournal.LuxembourgGazettePhase];
        Assert.AreEqual(journaledDocuments, documents.Replayed + documents.Live);
        Assert.AreEqual(journaledGazette, gazette.Replayed + gazette.Live);
        Assert.HasCount(documents.Live + gazette.Live, handler.DocumentRequests);

        // A resumed population's catalog is lex-lu-first-mount-acquisition/4, and its reopen carries the resumption.
        var reopened = await LuxembourgFirstMountAcquisition.ReopenPopulationAsync(store, resumed.CheckpointRef!,
            LuxembourgPopulationScope.Legislative, CancellationToken.None);
        Assert.IsTrue(reopened.Delivered, reopened.Detail);
        Assert.AreEqual(ContractJson.Serialize(resumption), ContractJson.Serialize(reopened.Resumption));
        Assert.AreEqual(resumed.PopulationScopeManifestRef, reopened.PopulationScopeManifestRef);
        Assert.AreEqual(resumed.Run!.CorpusRecordSetRef, reopened.Run!.CorpusRecordSetRef);
    }

    [TestMethod]
    public async Task AFamilyWhoseRecordDoesNotRestoreIsRebuiltFromItsDeliveredLeaves()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var arguments = AcquisitionJournal.DigestArguments([], null, LuxembourgPopulationScope.Legislative, "raw", null);
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), arguments, budget.Limit,
            LuxembourgRendererRoles(renderers), null);

        // A complete run's journal, as a kill after its last unit would leave it, without two families' records: only
        // their delivered leaves say where their covers are.
        var removed = new[] { "legislative-loi-s", "legislative-loi-a" };
        byte[] journal;
        int leafLines;
        using (var stream = new MemoryStream())
        {
            using var complete = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
            await using (var writer = new AcquisitionJournal(stream, header, budget, new LuxembourgAcquisitionTestFixture.FixedTimeProvider()))
            {
                var captured = await new LuxembourgFirstMountAcquisition(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
                    complete, writer).RunPopulationAsync(LuxembourgPopulationScope.Legislative, renderers, budget, CancellationToken.None);
                Assert.IsTrue(captured.Delivered, captured.Detail);
            }

            leafLines = 0;
            journal = await EuFirstMountAcquisitionTests.RejournalAsync(stream.ToArray(), header, units =>
            {
                units.RemoveAll(unit => unit.Phase == AcquisitionJournal.LuxembourgQueryFamilyPhase && removed.Contains(unit.Key));
                leafLines = units.Count(unit => unit.Phase == AcquisitionJournal.LuxembourgCoverLeafPhase &&
                    removed.Any(family => unit.Key.StartsWith(family + "#", StringComparison.Ordinal)));
            });
        }

        Assert.IsTrue(leafLines >= 2, $"both families' covers journaled their delivered leaves ({leafLines})");
        var time = new LuxembourgAcquisitionTestFixture.FixedTimeProvider();
        var resume = await AcquisitionResume.OpenAsync(store, journal, AcquisitionJournal.CurrentSource(), arguments, time,
            CancellationToken.None);
        using var handler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
        LuxembourgFirstMountAcquisitionResult resumed;
        byte[] continuedJournal;
        using (var stream = new MemoryStream())
        {
            await using (var continued = new AcquisitionJournal(stream, header with { ResumedFrom = resume.Continuation },
                LuxembourgAcquisitionTestFixture.TestWireBudget(), time))
            {
                resumed = await new LuxembourgFirstMountAcquisition(store, time, handler, continued, resume)
                    .RunPopulationAsync(LuxembourgPopulationScope.Legislative, renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(),
                        CancellationToken.None);
            }

            continuedJournal = stream.ToArray();
        }

        // No family went back to the publisher: the two were rebuilt from their leaves, each restored through its own
        // checkpoint and proven again, and the leaves were journaled again for a resume of this run.
        Assert.IsTrue(resumed.Delivered, $"{resumed.Refusal}: {resumed.Detail}");
        Assert.IsEmpty(handler.FamiliesSeen, "every family was restored, two of them from their delivered leaves");
        var phases = resumed.Resumption!.Phases.ToDictionary(static phase => phase.Phase, StringComparer.Ordinal);
        Assert.AreEqual(leafLines, phases[AcquisitionJournal.LuxembourgCoverLeafPhase].Replayed);
        Assert.AreEqual(0, phases[AcquisitionJournal.LuxembourgCoverLeafPhase].Live);
        Assert.AreEqual(7, phases[AcquisitionJournal.LuxembourgQueryFamilyPhase].Replayed);
        Assert.AreEqual(leafLines, new AcquisitionResume(continuedJournal).Count(AcquisitionJournal.LuxembourgCoverLeafPhase));

        // Its catalog reopens on its own: the covers rebuilt from restored leaves verify as a full replay verifies them.
        var reopened = await LuxembourgFirstMountAcquisition.ReopenPopulationAsync(store, resumed.CheckpointRef!,
            LuxembourgPopulationScope.Legislative, CancellationToken.None);
        Assert.IsTrue(reopened.Delivered, reopened.Detail);
        Assert.AreEqual(resumed.Run!.CorpusRecordSetRef, reopened.Run!.CorpusRecordSetRef);

        // A resume of the resumed run, again without the two families' records, finds their leaves in that run's journal.
        var chained = await EuFirstMountAcquisitionTests.RejournalAsync(continuedJournal, header with { ResumedFrom = resume.Continuation },
            units => units.RemoveAll(unit => unit.Phase == AcquisitionJournal.LuxembourgQueryFamilyPhase && removed.Contains(unit.Key)));
        using var thirdHandler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
        var (third, _) = await ResumeLegislativeAsync(store, renderers, arguments, header, chained, thirdHandler);
        Assert.IsTrue(third.Delivered, $"{third.Refusal}: {third.Detail}");
        Assert.IsEmpty(thirdHandler.FamiliesSeen, "the third run restored every family too, two of them from their leaves again");
        Assert.AreEqual(leafLines, third.Resumption!.Phases.Single(static phase => phase.Phase == AcquisitionJournal.LuxembourgCoverLeafPhase).Replayed);
    }

    [TestMethod]
    public async Task ALeafLineThatDoesNotBindEndsThePrefixAndItsFamilyIsEnumeratedAgain()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var (arguments, header, complete) = await CompleteLegislativeJournalAsync(store, renderers);

        // Without the loi range's census and assertion records, and with their first leaves' checkpoints exchanged: each
        // still reads back, and neither restores under its own line's run and profile. Each prefix ends at its first
        // leaf, and no later line of those families is used.
        var removed = new[] { "legislative-loi-s", "legislative-loi-a" };
        var journal = await EuFirstMountAcquisitionTests.RejournalAsync(complete, header, units =>
        {
            units.RemoveAll(unit => unit.Phase == AcquisitionJournal.LuxembourgQueryFamilyPhase && removed.Contains(unit.Key));
            var census = units.Single(static unit => unit.Phase == AcquisitionJournal.LuxembourgCoverLeafPhase && unit.Key == "legislative-loi-s#0");
            var assertions = units.Single(static unit => unit.Phase == AcquisitionJournal.LuxembourgCoverLeafPhase && unit.Key == "legislative-loi-a#0");
            var checkpoint = census.Payload["checkpoint"]!.DeepClone();
            census.Payload["checkpoint"] = assertions.Payload["checkpoint"]!.DeepClone();
            assertions.Payload["checkpoint"] = checkpoint;
        });

        using var handler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
        var (resumed, continuedJournal) = await ResumeLegislativeAsync(store, renderers, arguments, header, journal, handler);
        Assert.IsTrue(resumed.Delivered, $"{resumed.Refusal}: {resumed.Detail}");
        CollectionAssert.AreEquivalent(new[] { "S", "A" }, handler.FamiliesSeen.Distinct().ToArray(),
            "only the two families went back to the publisher");
        var phases = resumed.Resumption!.Phases.ToDictionary(static phase => phase.Phase, StringComparer.Ordinal);
        Assert.AreEqual(0, phases[AcquisitionJournal.LuxembourgCoverLeafPhase].Replayed, "no leaf of either family was restored");
        Assert.AreEqual(7, phases[AcquisitionJournal.LuxembourgQueryFamilyPhase].Replayed);
        var live = phases[AcquisitionJournal.LuxembourgCoverLeafPhase].Live;
        Assert.IsGreaterThanOrEqualTo(2, live, "both covers were delivered again, leaf by leaf");
        Assert.AreEqual(live, new AcquisitionResume(continuedJournal).Count(AcquisitionJournal.LuxembourgCoverLeafPhase));
    }

    [TestMethod]
    public async Task ALeafWhoseHeldQueriesAnotherRendererRenderedEndsThePrefix()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var (arguments, header, complete) = await CompleteLegislativeJournalAsync(store, renderers);

        // A second query renderer (the same source with one more line, held in custody), and a resume under it. The loi
        // range's census and assertion records are gone, and their leaf lines name the second renderer: every field of
        // each line now agrees with the resumed request, and its checkpoint restores, bounds and proves its leaf. Only
        // the count queries it holds, which the first renderer rendered, do not.
        var bytes = renderers.Query.CopyBytes().ToArray().Concat("\n// a second renderer\n"u8.ToArray()).ToArray();
        var held = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var second = new LuxembourgRendererSources(MachineQueryRendererSource.Open(
            new SourceArtifactRef("urn:uuid:" + Guid.NewGuid().ToString("D"), held.Reference.ContentSha256), bytes), renderers.DocumentFetch);
        var removed = new[] { "legislative-loi-s", "legislative-loi-a" };
        var forged = 0;
        var journal = await EuFirstMountAcquisitionTests.RejournalAsync(complete, header, units =>
        {
            units.RemoveAll(unit => unit.Phase == AcquisitionJournal.LuxembourgQueryFamilyPhase && removed.Contains(unit.Key));
            foreach (var unit in units.Where(unit => unit.Phase == AcquisitionJournal.LuxembourgCoverLeafPhase &&
                removed.Any(family => unit.Key.StartsWith(family + "#", StringComparison.Ordinal))))
            {
                unit.Payload["renderer"] = JsonNode.Parse(ContractJson.Serialize(second.Query.Reference));
                forged++;
            }
        });

        Assert.IsGreaterThanOrEqualTo(2, forged);
        using var handler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
        var (resumed, _) = await ResumeLegislativeAsync(store, second, arguments, header with { Renderers = LuxembourgRendererRoles(second) },
            journal, handler);
        Assert.IsTrue(resumed.Delivered, $"{resumed.Refusal}: {resumed.Detail}");
        var phases = resumed.Resumption!.Phases.ToDictionary(static phase => phase.Phase, StringComparer.Ordinal);
        Assert.AreEqual(0, phases[AcquisitionJournal.LuxembourgCoverLeafPhase].Replayed,
            "a leaf whose held count queries this request's renderer does not render is not restored");
    }

    /// <summary>A complete legislative population run's journal (both works), as a kill after its last unit would leave it.</summary>
    private static async Task<(string Arguments, AcquisitionJournalHeader Header, byte[] Journal)> CompleteLegislativeJournalAsync(
        ICustodyStore store, LuxembourgRendererSources renderers)
    {
        var arguments = AcquisitionJournal.DigestArguments([], null, LuxembourgPopulationScope.Legislative, "raw", null);
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), arguments, budget.Limit,
            LuxembourgRendererRoles(renderers), null);
        using var stream = new MemoryStream();
        using var complete = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
        await using (var writer = new AcquisitionJournal(stream, header, budget, new LuxembourgAcquisitionTestFixture.FixedTimeProvider()))
        {
            var captured = await new LuxembourgFirstMountAcquisition(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(),
                complete, writer).RunPopulationAsync(LuxembourgPopulationScope.Legislative, renderers, budget, CancellationToken.None);
            Assert.IsTrue(captured.Delivered, captured.Detail);
        }

        return (arguments, header, stream.ToArray());
    }

    /// <summary>Resumes a legislative population run from <paramref name="journal"/> through <paramref name="handler"/>.</summary>
    private static async Task<(LuxembourgFirstMountAcquisitionResult Result, byte[] Journal)> ResumeLegislativeAsync(ICustodyStore store,
        LuxembourgRendererSources renderers, string arguments, AcquisitionJournalHeader header, byte[] journal, LuxembourgFamilyHandler handler)
    {
        var time = new LuxembourgAcquisitionTestFixture.FixedTimeProvider();
        var resume = await AcquisitionResume.OpenAsync(store, journal, AcquisitionJournal.CurrentSource(), arguments, time,
            CancellationToken.None);
        using var stream = new MemoryStream();
        LuxembourgFirstMountAcquisitionResult resumed;
        await using (var continued = new AcquisitionJournal(stream, header with { ResumedFrom = resume.Continuation },
            LuxembourgAcquisitionTestFixture.TestWireBudget(), time))
        {
            resumed = await new LuxembourgFirstMountAcquisition(store, time, handler, continued, resume)
                .RunPopulationAsync(LuxembourgPopulationScope.Legislative, renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(),
                    CancellationToken.None);
        }

        return (resumed, stream.ToArray());
    }

    private static AcquisitionJournalRenderer[] LuxembourgRendererRoles(LuxembourgRendererSources renderers) =>
    [
        new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[0], renderers.Query.Reference),
        new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[1], renderers.DocumentFetch.Reference),
    ];

    private static string[] Tallies(AcquisitionResumption resumption) => resumption.Phases
        .Select(static phase => string.Create(CultureInfo.InvariantCulture, $"{phase.Phase} {phase.Replayed} {phase.Live}"))
        .ToArray();

    /// <summary>
    /// A transport around the family handler that stops the run at its Nth document GET by cancelling it there, before
    /// the GET reaches the publisher.
    /// </summary>
    private sealed class InterruptingDocumentHandler(HttpMessageHandler inner, int interruptAtDocument, CancellationTokenSource stop)
        : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(inner, disposeHandler: false);
        private int _documents;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath != "/robots.txt" &&
                Interlocked.Increment(ref _documents) == interruptAtDocument)
            {
                await stop.CancelAsync().ConfigureAwait(false);
                throw new OperationCanceledException(stop.Token);
            }

            return await _inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
