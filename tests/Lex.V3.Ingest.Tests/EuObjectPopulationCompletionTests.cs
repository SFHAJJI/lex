using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
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
        Assert.AreEqual(run.ObservedObjectCount, population.ObjectCount,
            "a real run's corpus set is exactly the objects the run says it observed, so the size "
                + "check refuses nothing that is correct.");
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

    /// <summary>
    /// THE HOSTILE INPUT THE TYPE'S OWN CLAIM INVITES. The set handed in here is not malformed: it
    /// is strictly ordered, names no object twice, and canonicalizes to the digest its own artifact
    /// reference carries, so every door it passes through admits it. It is simply one object short
    /// while the run still says how many it observed. Taking no list from a caller is not enough
    /// when the run itself is assembled through a public door that accepts the count and the set
    /// independently.
    /// </summary>
    [TestMethod]
    public async Task AVerifiedButShortenedCorpusSetIsNotThisRunsPopulation()
    {
        var run = await CompleteEuropeAsync();
        var shortened = WithoutItsFirstRecord(run.CorpusRecordSet!);
        Assert.AreEqual(
            run.CorpusRecordSet!.Set.Records.Count - 1,
            shortened.Set.Records.Count,
            "the hostile set really is one object shorter.");

        var population = EuObjectPopulationCompletion.TryClose(
            Rebuild(run, corpusRecordSet: shortened), out var refusal, out var detail);

        Assert.IsNull(population, "a shortened population was accepted as complete.");
        Assert.AreEqual(EuObjectPopulationRefusal.PopulationIsNotEveryObservedObject, refusal);
        StringAssert.Contains(
            detail,
            run.ObservedObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---- Fixtures. ----

    /// <summary>
    /// The same set less its first record, re-canonicalized and re-verified so it arrives through
    /// <see cref="VerifiedCorpusRecordSet.ParseAndVerify"/> exactly as a real one would. Dropping the
    /// FIRST record keeps the strict ordinal ordering the set's own constructor requires, so nothing
    /// about this input is invalid except its size.
    /// </summary>
    private static VerifiedCorpusRecordSet WithoutItsFirstRecord(VerifiedCorpusRecordSet original)
    {
        var set = original.Set;
        var shortened = new CorpusRecordSet(
            set.Schema, set.ManifestRef, set.RunIdentity, set.Records.Skip(1).ToList());
        using var canonical = new MemoryStream();
        var sha256 = CorpusRecordSetCanonicalWriter.Write(canonical, shortened);
        return VerifiedCorpusRecordSet.ParseAndVerify(
            new SourceArtifactRef(set.RunIdentity.ResourceId, sha256), canonical.ToArray());
    }

    private static Task<EuQueryExecutionResult> CompleteEuropeAsync() =>
        EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));

    private static EuQueryExecutionResult Rebuild(
        EuQueryExecutionResult run,
        IReadOnlyDictionary<int, EuMintedRowAccounting>? mintedRows = null,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? outcomes = null,
        VerifiedCorpusRecordSet? corpusRecordSet = null) =>
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
            corpusRecordSet ?? run.CorpusRecordSet!,
            run.CorrigendumTripwires!);
}
