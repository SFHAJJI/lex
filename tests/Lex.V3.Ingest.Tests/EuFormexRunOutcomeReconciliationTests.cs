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

    /// <summary>
    /// EVERY CLAUSE OF THE RUN BINDING, ONE AT A TIME.
    /// <see cref="AnotherRunsPopulationCannotStandInForThisRunsEpisode"/> supplies a population from a
    /// wholly different run, so every clause of the binding disagrees at once and the composite gate
    /// refuses on whichever fires first. That proves the gate and establishes no clause inside it:
    /// neutralise any single one and the suite stays green, which means a refactor could delete one
    /// and nothing would notice.
    /// </summary>
    /// <remarks>
    /// Each case below agrees with the run in every dimension except the one under test, so only the
    /// clause named can be what refuses. That is the difference between proving a gate and proving
    /// the reasons it is built from.
    /// </remarks>
    [TestMethod]
    public async Task ADifferentSemanticDerivationAloneRefuses()
    {
        var run = await CompleteEuropeAsync();
        var supplied = VaryingOneThing(
            RunProduction(run), derivationBytes: Encoding.UTF8.GetBytes("a-different-derivation"));

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [PopulationOf(supplied)], out var refusal, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees, refusal);
    }

    [TestMethod]
    public async Task ADifferentEpisodeAloneRefuses()
    {
        var run = await CompleteEuropeAsync();
        var supplied = VaryingOneThing(
            RunProduction(run), episodeBytes: Encoding.UTF8.GetBytes("a-different-episode"));

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [PopulationOf(supplied)], out var refusal, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees, refusal);
    }

    /// <summary>
    /// Same derivation, same episode, same bytes: only the durable write receipt differs. This is the
    /// clause that says the population came from THIS run's retention rather than from an identical
    /// computation somebody else performed and retained separately.
    /// </summary>
    [TestMethod]
    public async Task ADifferentRetainedDerivationReceiptAloneRefuses()
    {
        var run = await CompleteEuropeAsync();
        var supplied = VaryingOneThing(RunProduction(run), varyDerivationReceipt: true);

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [PopulationOf(supplied)], out var refusal, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees, refusal);
    }

    [TestMethod]
    public async Task ADifferentRetainedEpisodeReceiptAloneRefuses()
    {
        var run = await CompleteEuropeAsync();
        var supplied = VaryingOneThing(RunProduction(run), varyEpisodeReceipt: true);

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [PopulationOf(supplied)], out var refusal, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees, refusal);
    }

    /// <summary>
    /// The derivation and its retention agree; the production was asked about a different set of
    /// objects. Two runs can derive identical expressions from different questions, and the answer is
    /// only this run's answer if the question was this run's question.
    /// </summary>
    [TestMethod]
    public async Task ADifferentAskedObjectSetAloneRefuses()
    {
        var run = await CompleteEuropeAsync();
        var original = RunProduction(run);
        var widened = new HashSet<string>(original.ObjectsAskedAbout!, StringComparer.Ordinal)
        {
            "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000000",
        };

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [PopulationOf(VaryingOneThing(original, objectsAskedAbout: widened))],
            out var refusal, out _));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationProductionDisagrees, refusal);
    }

    /// <summary>
    /// A population that is internally valid and belongs to no batch of this run. Without this the
    /// refusal exists in the vocabulary and nothing can reach it, so a reader would believe a
    /// distinction the type does not in fact draw.
    /// </summary>
    [TestMethod]
    public async Task APopulationBelongingToNoBatchOfThisRunRefusesByName()
    {
        var run = await CompleteEuropeAsync();
        var supplied = VaryingOneThing(RunProduction(run), familyKey: "a-family-this-run-never-asked");

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            run, [PopulationOf(supplied)], out var refusal, out var detail));
        Assert.AreEqual(EuFormexRunOutcomeReconciliationRefusal.PopulationOutsideRun, refusal);
        Assert.AreEqual("a-family-this-run-never-asked", detail);
    }

    private static EuLanguageScopedExpressionProductionResult RunProduction(EuQueryExecutionResult run) =>
        run.CorrigendumTripwires!.ProductionsByFamilyKey.Values.Single().Expressions!;

    /// <summary>
    /// A production equal to <paramref name="original"/> in every dimension the run binding compares,
    /// except the single one the caller names. Reusing the original's own bytes and receipts for the
    /// untouched dimensions is what makes each test about one clause rather than about the gate.
    /// </summary>
    private static EuLanguageScopedExpressionProductionResult VaryingOneThing(
        EuLanguageScopedExpressionProductionResult original,
        string? familyKey = null,
        byte[]? derivationBytes = null,
        byte[]? episodeBytes = null,
        bool varyDerivationReceipt = false,
        bool varyEpisodeReceipt = false,
        IReadOnlySet<string>? objectsAskedAbout = null)
    {
        var derivation = original.Derivation!;
        var chosenDerivationBytes = derivationBytes ?? derivation.DerivationBytes.ToArray();
        var chosenEpisodeBytes = episodeBytes ?? derivation.EpisodeBytes.ToArray();
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
        var rebuilt = (EuLanguageScopedExpressionDerivation)constructor.Invoke(
            [
                familyKey is null
                    ? derivation.ExpressionFactsProof
                    : AbsenceFixtures.Delivery(familyKey, derivation.Expressions.Count).Proof,
                derivation.ObjectFactsProof,
                derivation.Expressions,
                chosenDerivationBytes,
                chosenEpisodeBytes,
            ]);

        return EuLanguageScopedExpressionProductionResult.Success(
            rebuilt,
            varyDerivationReceipt
                ? Receipt(rebuilt.DerivationSha256, chosenDerivationBytes.Length + 1)
                : original.RetainedDerivation!,
            varyEpisodeReceipt
                ? Receipt(rebuilt.EpisodeSha256, chosenEpisodeBytes.Length + 1)
                : original.RetainedEpisode!,
            objectsAskedAbout ?? original.ObjectsAskedAbout!,
            original.ProductRequestCount);
    }

    /// <summary>
    /// THE RUN'S OWN PRODUCTIONS ARE VALIDATED BY A NINE-CLAUSE COMPOSITE THAT REPORTS ONE REFUSAL,
    /// and until these cases nothing reached any of it. Every other test here attacks the SUPPLIED
    /// population; this validates what the run itself carries, which is the side the whole
    /// reconciliation is measured against.
    /// </summary>
    /// <remarks>
    /// Two clauses of that composite are deliberately not covered because no admitted input reaches
    /// them: a production that is not delivered cannot exist, since
    /// <c>EuCorrigendumTripwireCompletion</c>'s own constructor throws on one, and a delivered
    /// production always carries expressions because <c>EuCorrigendumTripwireProductionResult.Success</c>
    /// null-checks them. Declaring those is worth more than a test that cannot be written.
    /// </remarks>
    [TestMethod]
    public async Task ARunProductionWhoseRetainedDerivationDoesNotCarryItsOwnDigestIsInvalid()
    {
        var run = await CompleteEuropeAsync();

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            Rebuild(run, corrigendumTripwires: RunCarrying(run, ForeignDerivationReceipt)),
            PopulationsOf(run), out var refusal, out _));
        Assert.AreEqual(
            EuFormexRunOutcomeReconciliationRefusal.RunExpressionProductionInvalid, refusal);
    }

    [TestMethod]
    public async Task ARunProductionWhoseRetainedEpisodeDoesNotCarryItsOwnDigestIsInvalid()
    {
        var run = await CompleteEuropeAsync();

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            Rebuild(run, corrigendumTripwires: RunCarrying(run, ForeignEpisodeReceipt)),
            PopulationsOf(run), out var refusal, out _));
        Assert.AreEqual(
            EuFormexRunOutcomeReconciliationRefusal.RunExpressionProductionInvalid, refusal);
    }

    /// <summary>
    /// The production is internally consistent; it simply sits in the run's batch map under a key its
    /// own enumeration proof does not name. Without this clause a run could file a batch's production
    /// under another batch's key and every later join would be measured against the wrong derivation.
    /// </summary>
    [TestMethod]
    public async Task ARunProductionFiledUnderAKeyItsOwnProofDoesNotNameIsInvalid()
    {
        var run = await CompleteEuropeAsync();
        var production = run.CorrigendumTripwires!.ProductionsByFamilyKey.Single().Value;
        var misfiled = EuCorrigendumTripwireProductionResult.Success(
            DuplicateProductionForFamily(production.Expressions!, "the-proof-says-this-batch"),
            production.TripwireSet!,
            production.RetainedTripwire!,
            production.RetainedTripwireLineage!,
            production.ProductRequestCount);
        var filedElsewhere = new EuCorrigendumTripwireCompletion(
            new HashSet<string>(["but-it-is-filed-under-this-one"], StringComparer.Ordinal),
            new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal)
            {
                ["but-it-is-filed-under-this-one"] = misfiled,
            });

        Assert.IsNull(EuFormexRunOutcomeReconciliation.TryClose(
            Rebuild(run, corrigendumTripwires: filedElsewhere),
            PopulationsOf(run), out var refusal, out var detail));
        Assert.AreEqual(
            EuFormexRunOutcomeReconciliationRefusal.RunExpressionProductionInvalid, refusal);
        Assert.AreEqual("but-it-is-filed-under-this-one", detail);
    }

    /// <summary>
    /// The run's own single batch, rebuilt with one retention receipt replaced. The receipt still
    /// exists and is still well formed; it simply carries a content digest that is not the digest of
    /// the thing it claims to retain.
    /// </summary>
    private static EuCorrigendumTripwireCompletion RunCarrying(
        EuQueryExecutionResult run,
        Func<EuLanguageScopedExpressionProductionResult, EuLanguageScopedExpressionProductionResult> doctor)
    {
        var production = run.CorrigendumTripwires!.ProductionsByFamilyKey.Single();
        var doctored = EuCorrigendumTripwireProductionResult.Success(
            doctor(production.Value.Expressions!),
            production.Value.TripwireSet!,
            production.Value.RetainedTripwire!,
            production.Value.RetainedTripwireLineage!,
            production.Value.ProductRequestCount);
        return new EuCorrigendumTripwireCompletion(
            new HashSet<string>([production.Key], StringComparer.Ordinal),
            new Dictionary<string, EuCorrigendumTripwireProductionResult>(StringComparer.Ordinal)
            {
                [production.Key] = doctored,
            });
    }

    private static EuLanguageScopedExpressionProductionResult ForeignDerivationReceipt(
        EuLanguageScopedExpressionProductionResult original) =>
        EuLanguageScopedExpressionProductionResult.Success(
            original.Derivation!,
            Receipt(new string('9', 64), 1),
            original.RetainedEpisode!,
            original.ObjectsAskedAbout!,
            original.ProductRequestCount);

    private static EuLanguageScopedExpressionProductionResult ForeignEpisodeReceipt(
        EuLanguageScopedExpressionProductionResult original) =>
        EuLanguageScopedExpressionProductionResult.Success(
            original.Derivation!,
            original.RetainedDerivation!,
            Receipt(new string('9', 64), 1),
            original.ObjectsAskedAbout!,
            original.ProductRequestCount);

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

    internal static EuFormexRunOutcomeReconciliation CompleteForEnvelope(
        EuQueryExecutionResult run,
        EuFormexPackageOutcome? replacement = null) =>
        EuFormexRunOutcomeReconciliation.TryClose(
            run,
            PopulationsOf(run, replacement),
            out var refusal,
            out var detail)
        ?? throw new AssertFailedException($"{refusal}: {detail}");

    private static IReadOnlyList<EuFormexPackageOutcomePopulation> PopulationsOf(
        EuQueryExecutionResult run) => PopulationsOf(run, replacement: null);

    private static IReadOnlyList<EuFormexPackageOutcomePopulation> PopulationsOf(
        EuQueryExecutionResult run,
        EuFormexPackageOutcome? replacement)
    {
        var replacementCount = 0;
        var populations = run.CorrigendumTripwires!.ProductionsByFamilyKey
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => PopulationOf(pair.Value.Expressions!, replacement, ref replacementCount))
            .ToArray();
        if (replacement is not null && replacementCount != 1)
        {
            throw new AssertFailedException(
                $"The replacement Formex outcome matched {replacementCount} run expressions instead of one.");
        }
        return populations;
    }

    private static EuFormexPackageOutcomePopulation PopulationOf(
        EuLanguageScopedExpressionProductionResult production)
    {
        var replacementCount = 0;
        return PopulationOf(production, replacement: null, ref replacementCount);
    }

    private static EuFormexPackageOutcomePopulation PopulationOf(
        EuLanguageScopedExpressionProductionResult production,
        EuFormexPackageOutcome? replacement,
        ref int replacementCount)
    {
        var enumerations = production.Derivation!.Expressions.Select(static expression =>
            EuFormexManifestationEnumerationResult.Success(
                expression,
                [new EuExpressionManifestationType(
                    expression.Identity.PublisherExpressionId + ".01",
                    "fmx4",
                    1,
                    "offline-test-observation")],
                Proof(),
                0,
                LuxembourgAcquisitionTestFixture.TestBudgetSnapshot())).ToArray();
        var eligibility = EuFormexEligibilityPopulation.TryCreate(
            production, enumerations, out var eligibilityRefusal, out var eligibilityDetail)
            ?? throw new AssertFailedException($"{eligibilityRefusal}: {eligibilityDetail}");
        var outcomes = new List<EuFormexPackageOutcome>(eligibility.Enumerations.Count);
        foreach (var enumeration in eligibility.Enumerations)
        {
            if (replacement is not null
                && replacement.ExpressionIdentity == enumeration.ExpressionIdentity)
            {
                replacementCount++;
                outcomes.Add(replacement);
                continue;
            }
            outcomes.Add(EuFormexPackageOutcome.Refused(
                enumeration.Expression,
                EuDocumentFetchAttemptRefusal.ObservationNotExecuted,
                "offline fixture"));
        }
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
            run.CorpusRecordSetReceipt!,
            run.CorpusRecordSet!,
            corrigendumTripwires ?? run.CorrigendumTripwires!);
}
