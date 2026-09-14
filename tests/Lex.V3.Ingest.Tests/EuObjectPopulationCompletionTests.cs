using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU object population, closed against one run's own evidence. Offline; no publisher traffic.
/// </summary>
/// <remarks>
/// The rule these exist to hold is that the population is what the run OBSERVED, not what it managed
/// to address. An object the ladder cannot reach mints no fetch row, and a completion keyed on rows
/// would drop exactly those objects and call the remainder whole.
/// </remarks>
[TestClass]
public sealed class EuObjectPopulationCompletionTests
{
    [TestMethod]
    public async Task ADeliveredRunClosesOverEveryObjectItObserved()
    {
        var run = await CompleteEuropeAsync();

        var population = EuObjectPopulationCompletion.TryClose(run, out var refusal, out var detail);

        Assert.AreEqual(EuObjectPopulationRefusal.None, refusal, detail);
        Assert.IsNotNull(population);
        Assert.AreEqual(run.CorpusRecordSet!.Set.Records.Count, population.ObjectCount);
        Assert.IsTrue(population.MintedRowCount <= population.ObjectCount,
            "a run cannot address more objects than it observed.");
        Assert.IsTrue(population.AcquisitionOutcomeCount <= population.MintedRowCount,
            "a run cannot fetch more objects than it addressed.");
    }

    /// <summary>
    /// THE RULE THIS TYPE EXISTS FOR. Drop an object's fetch row and it is still a member. A
    /// completion built on minted rows would have reported a smaller population and called it
    /// complete, which is the failure this whole boundary is meant to make impossible.
    /// </summary>
    [TestMethod]
    public async Task AnObjectThatMintedNoFetchRowIsStillAMember()
    {
        var run = await CompleteEuropeAsync();
        var dropped = run.MintedRowsByOrdinal!.Keys.OrderBy(static ordinal => ordinal).First();
        var withoutThatRow = Rebuild(
            run,
            mintedRows: run.MintedRowsByOrdinal!.Where(pair => pair.Key != dropped).ToDictionary(),
            outcomes: run.DocumentAcquisitionOutcomesByOrdinal!
                .Where(pair => pair.Key != dropped).ToDictionary());

        var population = EuObjectPopulationCompletion.TryClose(
            withoutThatRow, out var refusal, out var detail);

        Assert.AreEqual(EuObjectPopulationRefusal.None, refusal, detail);
        Assert.AreEqual(run.CorpusRecordSet!.Set.Records.Count, population!.ObjectCount,
            "the population did not shrink with the fetch row.");
        Assert.IsTrue(population.Members.Any(member => member.ObjectOrdinal == dropped),
            "the object that minted nothing is still a member of the population.");
        Assert.AreEqual(run.MintedRowsByOrdinal.Count - 1, population.MintedRowCount);
    }

    [TestMethod]
    public async Task ARefusedRunHasNoPopulationToClose()
    {
        var run = await CompleteEuropeAsync();
        var refused = EuQueryExecutionResult.Refused(
            run.Topology,
            run.FamilyOutcomes,
            new EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal.CensusFamilyNotProven, "test"));

        Assert.IsNull(EuObjectPopulationCompletion.TryClose(refused, out var refusal, out var detail));
        Assert.AreEqual(EuObjectPopulationRefusal.RunNotComplete, refusal);
        StringAssert.Contains(detail, "the run refused");
    }

    [TestMethod]
    public async Task AMintedRowOutsideThePopulationRefuses()
    {
        var run = await CompleteEuropeAsync();
        var stranger = run.CorpusRecordSet!.Set.Records.Max(static record => record.ObjectOrdinal) + 1;
        var extra = run.MintedRowsByOrdinal!.ToDictionary();
        extra[stranger] = new EuMintedRowAccounting("cellar-key-no-record-holds", false);

        Assert.IsNull(EuObjectPopulationCompletion.TryClose(
            Rebuild(run, mintedRows: extra), out var refusal, out var detail));
        Assert.AreEqual(EuObjectPopulationRefusal.MintedRowOutsidePopulation, refusal);
        StringAssert.Contains(detail, stranger.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task AnOutcomeOutsideThePopulationRefuses()
    {
        var run = await CompleteEuropeAsync();
        var stranger = run.CorpusRecordSet!.Set.Records.Max(static record => record.ObjectOrdinal) + 1;
        var borrowed = run.DocumentAcquisitionOutcomesByOrdinal!.Values.First();
        var extra = run.DocumentAcquisitionOutcomesByOrdinal!.ToDictionary();
        extra[stranger] = borrowed;

        Assert.IsNull(EuObjectPopulationCompletion.TryClose(
            Rebuild(run, outcomes: extra), out var refusal, out var detail));
        Assert.AreEqual(EuObjectPopulationRefusal.OutcomeOutsidePopulation, refusal);
        StringAssert.Contains(detail, stranger.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// An outcome for an object this run never addressed would mean it fetched something no evidence
    /// in the run accounts for.
    /// </summary>
    [TestMethod]
    public async Task AnOutcomeWithoutAMintedRowRefuses()
    {
        var run = await CompleteEuropeAsync();
        var addressed = run.MintedRowsByOrdinal!.Keys.OrderBy(static ordinal => ordinal).First();

        Assert.IsNull(EuObjectPopulationCompletion.TryClose(
            Rebuild(run, mintedRows: run.MintedRowsByOrdinal!.Where(pair => pair.Key != addressed).ToDictionary()),
            out var refusal,
            out var detail));
        Assert.AreEqual(EuObjectPopulationRefusal.OutcomeWithoutMintedRow, refusal);
        StringAssert.Contains(detail, addressed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---- Fixtures. ----

    private static Task<EuQueryExecutionResult> CompleteEuropeAsync() =>
        EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));

    private static EuQueryExecutionResult Rebuild(
        EuQueryExecutionResult run,
        IReadOnlyDictionary<int, EuMintedRowAccounting>? mintedRows = null,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? outcomes = null) =>
        EuQueryExecutionResult.Delivered(
            run.Topology,
            run.FamilyOutcomes,
            run.ObservedObjectCount,
            run.ObservedExpressionCount,
            run.ReductionExclusions,
            run.WatermarkWitnessPlan!,
            run.RootBinding!,
            run.WitnessReconciliation!,
            run.WitnessTerminations!,
            run.ScopeManifestReceipt!,
            run.ScopeManifestCanonicalSha256!,
            outcomes ?? run.DocumentAcquisitionOutcomesByOrdinal!,
            run.DocumentLadderResultsByOrdinal!,
            run.ObservedManifestationTypesByCelex!,
            run.ObservedExpressionsByCelex!,
            mintedRows ?? run.MintedRowsByOrdinal!,
            run.DateAxioms,
            run.CorpusRecordSetRef!,
            run.CorpusRecordSet!,
            run.CorrigendumTripwires!);
}
