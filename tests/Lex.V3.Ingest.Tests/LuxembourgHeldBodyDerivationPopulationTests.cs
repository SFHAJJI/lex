using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
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
