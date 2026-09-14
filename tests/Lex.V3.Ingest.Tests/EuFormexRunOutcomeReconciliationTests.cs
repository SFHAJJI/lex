using System.Reflection;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexRunOutcomeReconciliationTests
{
    [TestMethod]
    public async Task EveryRunExpressionCarriesExactlyOneOutcomePopulation()
    {
        var run = await CompleteEuropeAsync();
        var populations = PopulationsOf(run);

        var reconciliation = EuFormexRunOutcomeReconciliation.TryClose(
            run, populations, out var refusal, out var detail);

        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.None, refusal, detail);
        Assert.IsNotNull(reconciliation);
        Assert.AreEqual(run.ObservedExpressionCount, reconciliation.ExpressionCount);
        Assert.AreEqual(run.CorrigendumTripwires!.ProductionsByFamilyKey.Count,
            reconciliation.PopulationsByFamilyKey.Count);
        Assert.IsTrue(reconciliation.Outcomes.All(static outcome =>
            outcome.Kind == EuFormexPackageOutcomeKind.Refused));
    }

    [TestMethod]
    public async Task MissingAndDuplicateBatchPopulationsRefuse()
    {
        var run = await CompleteEuropeAsync();
        var population = PopulationsOf(run).Single();

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [], out var missing, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationMissing, missing);

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [population, population], out var duplicate, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationSuppliedTwice, duplicate);
    }

    [TestMethod]
    public async Task AnotherRunsPopulationCannotStandInForThisRunsEpisode()
    {
        var run = await CompleteEuropeAsync();
        var anotherRun = await CompleteEuropeAsync();
        var foreign = PopulationsOf(anotherRun).Single();

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [foreign], out var refusal, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees, refusal);
    }

    [TestMethod]
    public async Task AShortenedEmbeddedPopulationCannotMatchTheRunsObservedCount()
    {
        var run = await CompleteEuropeAsync();
        var shortened = Rebuild(run, observedExpressionCount: run.ObservedExpressionCount + 1);

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            shortened, PopulationsOf(run), out var refusal, out var detail));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.RunExpressionCountDisagrees, refusal);
        StringAssert.Contains(detail, run.ObservedExpressionCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task ThePerSeedRootAndStateSplitIsNotTheClosureWideIdentityPopulation()
    {
        var run = await CompleteEuropeAsync();
        var split = run.ObservedExpressionsByCelex!.ToDictionary();
        var first = split.First();
        split[first.Key] = new EuObservedExpressionSplit(
            first.Value.OfRootWork + 1, first.Value.OfConsolidatedStates);

        var reconciliation = EuFormexRunOutcomeReconciliation.TryClose(
            Rebuild(run, observedExpressionsByCelex: split),
            PopulationsOf(run), out var refusal, out var detail);

        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.None, refusal, detail);
        Assert.IsNotNull(reconciliation,
            "per-seed root/state counts may overlap across seeds and cannot stand in for closure identities");
    }

    [TestMethod]
    public async Task OneExpressionCannotBeClaimedByTwoRunBatches()
    {
        var run = await CompleteEuropeAsync();
        var production = run.CorrigendumTripwires!.ProductionsByFamilyKey.Single().Value;
        var originalFamilyKey = production.Expressions!.Derivation!.ExpressionFactsProof.FamilyKey;
        var duplicateExpressions = DuplicateProductionForFamily(production.Expressions!, "batch-b");
        var duplicateProduction = EuCorrigendumTripwireProductionResult.Success(
            duplicateExpressions,
            production.TripwireSet!,
            production.RetainedTripwire!,
            production.RetainedTripwireLineage!,
            production.ProductRequestCount);
        var duplicated = new EuCorrigendumTripwireCompletion(
            new HashSet<string>([originalFamilyKey, "batch-b"], StringComparer.Ordinal),
            new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal)
            {
                [originalFamilyKey] = production,
                ["batch-b"] = duplicateProduction,
            });

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            Rebuild(run, corrigendumTripwires: duplicated),
            PopulationsOf(run), out var refusal, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.ExpressionClaimedTwice, refusal);
    }

    private static EuLanguageScopedExpressionProductionResult DuplicateProductionForFamily(
        EuLanguageScopedExpressionProductionResult original,
        string familyKey)
    {
        var derivationBytes = Encoding.UTF8.GetBytes("duplicate-family-derivation");
        var episodeBytes = Encoding.UTF8.GetBytes("duplicate-family-episode");
        var constructor = typeof(EuLanguageScopedExpressionDerivation).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(AbsenceFamilyEnumerationProof),
                typeof(AbsenceFamilyEnumerationProof),
                typeof(IReadOnlyList<LanguageScopedExpression>),
                typeof(byte[]),
                typeof(byte[]),
            ],
            modifiers: null)!;
        var derivation = (EuLanguageScopedExpressionDerivation)constructor.Invoke(
            [
                AbsenceFixtures.Delivery(familyKey, original.Derivation!.Expressions.Count).Proof,
                null,
                original.Derivation.Expressions,
                derivationBytes,
                episodeBytes,
            ]);

        return EuLanguageScopedExpressionProductionResult.Success(
            derivation,
            Receipt(derivation.DerivationSha256, derivationBytes.Length),
            Receipt(derivation.EpisodeSha256, episodeBytes.Length),
            original.ObjectsAskedAbout!,
            original.ProductRequestCount);
    }

    private static DurableBlobWriteReceipt Receipt(string contentSha256, int byteLength)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            contentSha256,
            byteLength,
            CustodyClass.NightlyFloor90d);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                null,
                CustodyProtection.NotEnforced,
                DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
                null));
    }

    private static Task<EuQueryExecutionResult> CompleteEuropeAsync() =>
        EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));

    private static IReadOnlyList<EuFormexPackageOutcomePopulation> PopulationsOf(
        EuQueryExecutionResult run) =>
        run.CorrigendumTripwires!.ProductionsByFamilyKey
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => PopulationOf(pair.Value.Expressions!))
            .ToArray();

    private static EuFormexPackageOutcomePopulation PopulationOf(
        EuLanguageScopedExpressionProductionResult production)
    {
        var enumerations = production.Derivation!.Expressions.Select(static expression =>
            EuFormexManifestationEnumerationResult.Success(
                expression,
                [new EuExpressionManifestationType("fmx4", 1, "offline-test-observation")],
                Proof(),
                0,
                LuxembourgAcquisitionTestFixture.TestBudgetSnapshot())).ToArray();
        var eligibility = EuFormexEligibilityPopulation.TryCreate(
            production, enumerations, out var eligibilityRefusal, out var eligibilityDetail)
            ?? throw new AssertFailedException($"{eligibilityRefusal}: {eligibilityDetail}");
        var outcomes = eligibility.Enumerations.Select(static enumeration =>
            EuFormexPackageOutcome.Refused(
                enumeration.Expression,
                EuDocumentFetchAttemptRefusal.ObservationNotExecuted,
                "offline fixture")).ToArray();
        return EuFormexPackageOutcomePopulation.TryClose(
            eligibility, outcomes, out var outcomeRefusal, out var outcomeDetail)
            ?? throw new AssertFailedException($"{outcomeRefusal}: {outcomeDetail}");
    }

    private static AbsenceFamilyEnumerationProof Proof() =>
        AbsenceFixtures.Delivery("offline-formex-enumeration", 1).Proof;

    private static EuQueryExecutionResult Rebuild(
        EuQueryExecutionResult run,
        int? observedExpressionCount = null,
        IReadOnlyDictionary<string, EuObservedExpressionSplit>? observedExpressionsByCelex = null,
        EuCorrigendumTripwireCompletion? corrigendumTripwires = null) =>
        EuQueryExecutionResult.Delivered(
            run.Topology,
            run.FamilyOutcomes,
            run.ObservedObjectCount,
            observedExpressionCount ?? run.ObservedExpressionCount,
            run.ReductionExclusions,
            run.WatermarkWitnessPlan!,
            run.RootBinding!,
            run.WitnessReconciliation!,
            run.WitnessTerminations!,
            run.ScopeManifestReceipt!,
            run.ScopeManifestCanonicalSha256!,
            run.DocumentAcquisitionOutcomesByOrdinal!,
            run.DocumentLadderResultsByOrdinal!,
            run.ObservedManifestationTypesByCelex!,
            observedExpressionsByCelex ?? run.ObservedExpressionsByCelex!,
            run.MintedRowsByOrdinal!,
            run.DateAxioms,
            run.CorpusRecordSetRef!,
            run.CorpusRecordSet!,
            corrigendumTripwires ?? run.CorrigendumTripwires!);
}
