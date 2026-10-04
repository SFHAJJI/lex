using Lex.V3.Contracts;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A journaled Gazette listing GET is replayed, not fetched again, when a resumed run's Gazette selection binds to the
/// same document phase: the Gazette half of the claim that each executed document or Gazette GET is reopened when it
/// still binds. The fixture is the Gazette checkpoint capture's first shape: one document GET, then one Gazette GET.
/// </summary>
public sealed partial class LuxembourgGazetteAcquisitionTests
{
    [TestMethod]
    public async Task AJournaledGazetteGetIsReplayedWithoutARequest()
    {
        var arguments = AcquisitionJournal.DigestArguments([], null, LuxembourgPopulationScope.Legislative, "raw", null);
        var budget = LuxembourgAcquisitionTestFixture.TestWireBudget();
        var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), arguments, budget.Limit, [], null);
        GazetteCapture capture;
        byte[] journal;
        using (var stream = new MemoryStream())
        {
            await using (var writer = new AcquisitionJournal(stream, header, budget, new LuxembourgAcquisitionTestFixture.FixedTimeProvider()))
            {
                capture = await CaptureGazetteAsync(0, progress: new LuxembourgAcquisitionProgress(writer, null));
            }

            journal = stream.ToArray();
        }

        Assert.AreEqual(2, capture.Handler.Documents, "the interrupted run fetched one document and one Gazette listing");
        var time = new LuxembourgAcquisitionTestFixture.FixedTimeProvider();
        var resume = await AcquisitionResume.OpenAsync(capture.Store, journal, AcquisitionJournal.CurrentSource(), arguments, time,
            CancellationToken.None);
        Assert.AreEqual(1, resume.Count(AcquisitionJournal.LuxembourgDocumentPhase));
        Assert.AreEqual(1, resume.Count(AcquisitionJournal.LuxembourgGazettePhase));

        // The resumed phases, over the same selection: the document GET reopens, so the Gazette selection binds to the
        // same held routes and its GET reopens too.
        var handler = new GazetteCheckpointHandler(0);
        var progress = new LuxembourgAcquisitionProgress(null, resume);
        var adapter = new LuxembourgQueryExecutionAdapter(capture.Store,
            new LuxembourgRepeatedEnumerationExecutor(capture.Store, time, handler), capture.Profile)
        {
            Progress = progress,
        };
        var documents = await adapter.RunDocumentAcquisitionWithCheckpointAsync(capture.Manifest, capture.Addresses,
            capture.Renderer, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNull(documents.Data.Refusal, documents.Data.Refusal?.Detail);
        var gazette = await adapter.RunGazetteAcquisitionWithCheckpointAsync(capture.Resolved, capture.Manifest, capture.Addresses,
            documents.Data.HeldEvidenceByOrdinal!, capture.Renderer, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsNull(gazette.Data.Refusal, gazette.Data.Refusal?.Detail);

        Assert.AreEqual(0, handler.Documents, "neither the document nor the Gazette listing went to the publisher again");
        CollectionAssert.AreEqual(
            new[]
            {
                FormattableString.Invariant($"{AcquisitionJournal.LuxembourgDocumentPhase} 1 0"),
                FormattableString.Invariant($"{AcquisitionJournal.LuxembourgGazettePhase} 1 0"),
            },
            progress.Phases(AcquisitionJournal.LuxembourgDocumentPhase, AcquisitionJournal.LuxembourgGazettePhase)
                .Select(static phase => FormattableString.Invariant($"{phase.Phase} {phase.Replayed} {phase.Live}")).ToArray());
        Assert.AreEqual(capture.ResultJson,
            ContractJson.Serialize(new { gazette.Data.Sets, gazette.Data.FetchRefusals, gazette.Data.ContradictoryLegalValues }));
    }
}
