using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgHeldBodyDerivationPopulationTests
{
    [TestMethod]
    public async Task TheProductionRunCarriesTheSelectedFormatBesideTheExactHeldCorpusBody()
    {
        var run = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();

        var population = run.HeldBodyDerivationPopulation;

        Assert.IsNotNull(population);
        Assert.AreSame(run.CorpusRecordSet, population.CorpusRecordSet);
        var input = population.Inputs.Single();
        Assert.AreEqual(CorpusBodyRecordKind.Held, input.CorpusRecord.Body.Kind);
        Assert.AreEqual(LuxembourgUserFormatToken.PdfA, input.Address.UserFormatToken);
        Assert.AreEqual(
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/pdfa",
            input.SelectedWemiCandidate.ManifestationIri);
        Assert.AreEqual(
            input.Address.StoreFileUri.Value.AbsoluteUri,
            input.SelectedWemiCandidate.ItemIri);
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.StructurallyConsistent,
            input.SelectedWemiCandidate.Disposition);
        Assert.AreEqual(input.CorpusRecord.ObjectOrdinal, input.ObjectOrdinal);
        Assert.AreSame(input.CorpusRecord.Body.Receipt, input.Receipt);
        Assert.AreEqual(
            input.CorpusRecord.Body.Receipt!.Reference.ContentSha256,
            input.Receipt.Reference.ContentSha256);
    }

    [TestMethod]
    public async Task AHeldRecordWithoutItsSelectedAddressIsRefused()
    {
        var run = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();

        var population = LuxembourgHeldBodyDerivationPopulation.TryCreate(
            run.CorpusRecordSet!, run.DocumentAcquisitionOutcomesByOrdinal!,
            new Dictionary<Contracts.Source.Core.SourceObjectRef, LuxembourgDocumentFetchAddress>(),
            out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(
            LuxembourgHeldBodyDerivationPopulationRefusal.HeldRecordHasNoSelectedAddress,
            refusal);
        Assert.AreEqual(
            run.HeldBodyDerivationPopulation!.Inputs.Single().CorpusRecord.ObjectRef.PublisherUri,
            detail);
    }

    [TestMethod]
    public async Task AHeldRecordWithoutItsHeldOutcomeIsRefused()
    {
        var run = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();

        var population = LuxembourgHeldBodyDerivationPopulation.TryCreate(
            run.CorpusRecordSet!,
            new Dictionary<int, CorpusAcquisitionOutcome>(),
            Addresses(run),
            out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(
            LuxembourgHeldBodyDerivationPopulationRefusal.HeldRecordHasNoHeldOutcome,
            refusal);
        Assert.AreEqual(
            run.HeldBodyDerivationPopulation!.Inputs.Single().ObjectOrdinal.ToString(), detail);
    }

    [TestMethod]
    public async Task AHeldOutcomeOutsideTheVerifiedCorpusIsRefused()
    {
        var run = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();
        var outcomes = run.DocumentAcquisitionOutcomesByOrdinal!.ToDictionary();
        outcomes[int.MaxValue] = outcomes.Values.Single(static outcome => outcome.Receipt is not null);

        var population = LuxembourgHeldBodyDerivationPopulation.TryCreate(
            run.CorpusRecordSet!, outcomes, Addresses(run), out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(
            LuxembourgHeldBodyDerivationPopulationRefusal.HeldOutcomeHasNoRecord,
            refusal);
        Assert.AreEqual(int.MaxValue.ToString(), detail);
    }

    [TestMethod]
    public async Task AHeldOutcomeWhoseVerifiedRecordIsNotHeldIsRefused()
    {
        var run = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();
        var original = run.HeldBodyDerivationPopulation!.Inputs.Single().CorpusRecord;
        var pending = new CorpusRecord(
            original.Schema,
            original.ObjectRef,
            original.ObjectOrdinal,
            original.RecordDisposition,
            original.BodyDisposition,
            original.RelationDisposition,
            original.SupportingDocumentDisposition,
            CorpusBodyRecord.PendingAcquisition(CorpusBodyPendingAcquisitionReason.NotYetAcquired()),
            original.ManifestRef,
            original.RunIdentity);
        var rebuilt = new CorpusRecordSet(
            run.CorpusRecordSet!.Set.Schema,
            run.CorpusRecordSet.Set.ManifestRef,
            run.CorpusRecordSet.Set.RunIdentity,
            [pending]);
        using var bytes = new MemoryStream();
        var digest = CorpusRecordSetCanonicalWriter.Write(bytes, rebuilt);
        var reference = new Contracts.Source.Core.SourceArtifactRef(
            run.CorpusRecordSetRef!.ResourceId, digest);
        var verified = VerifiedCorpusRecordSet.ParseAndVerify(reference, bytes.ToArray());

        var population = LuxembourgHeldBodyDerivationPopulation.TryCreate(
            verified,
            run.DocumentAcquisitionOutcomesByOrdinal!,
            Addresses(run),
            out var refusal,
            out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(
            LuxembourgHeldBodyDerivationPopulationRefusal.HeldOutcomeRecordIsNotHeld,
            refusal);
        Assert.AreEqual(original.ObjectOrdinal.ToString(), detail);
    }

    [TestMethod]
    public async Task AReceiptOtherThanTheVerifiedCorpusRecordsReceiptIsRefused()
    {
        var run = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();
        var input = run.HeldBodyDerivationPopulation!.Inputs.Single();
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var otherReceipt = await store.CreateAsync(
            "different body"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var outcomes = run.DocumentAcquisitionOutcomesByOrdinal!.ToDictionary();
        outcomes[input.ObjectOrdinal] = CorpusAcquisitionOutcome.Held(otherReceipt);

        var population = LuxembourgHeldBodyDerivationPopulation.TryCreate(
            run.CorpusRecordSet!, outcomes, Addresses(run), out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(
            LuxembourgHeldBodyDerivationPopulationRefusal.HeldReceiptMismatch,
            refusal);
        Assert.AreEqual(input.ObjectOrdinal.ToString(), detail);
    }

    private static IReadOnlyDictionary<Contracts.Source.Core.SourceObjectRef, LuxembourgDocumentFetchAddress>
        Addresses(LuxembourgQueryExecutionResult run) =>
        run.HeldBodyDerivationPopulation!.Inputs.ToDictionary(
            static input => input.CorpusRecord.ObjectRef,
            static input => input.Address);
}
