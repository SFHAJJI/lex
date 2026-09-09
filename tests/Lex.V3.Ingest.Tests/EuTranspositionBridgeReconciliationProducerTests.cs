using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuTranspositionBridgeReconciliationProducerTests
{
    private const string Cellar =
        "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";
    private const string OtherCellar =
        "http://publications.europa.eu/resource/cellar/33333333-3333-4333-8333-333333333333";
    private const string EuEli = "http://data.europa.eu/eli/dir/2020/1/oj";
    private const string OtherEuEli = "http://data.europa.eu/eli/dir/2020/2/oj";
    private const string LocalEu = "http://data.legilux.public.lu/eli/dir_ue/2020/1/jo";
    private const string Measure1 =
        "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo";
    private const string Measure2 =
        "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/02/a2/jo";
    private const string Measure3 =
        "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/03/a3/jo";
    private static readonly SourceArtifactRef LegiluxCompletion = Ref('a');
    private static readonly SourceArtifactRef IdentityCompletion = Ref('b');
    private static readonly SourceArtifactRef NimCompletion = Ref('c');

    [TestMethod]
    public async Task EveryPublisherAssertionSurvivesTheExactEliReconciliationInItsOwnColumn()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var result = await new EuTranspositionBridgeReconciliationProducer(store).ProduceAsync(
            Legilux(LegiluxRelation(Measure1), LegiluxRelation(Measure2)),
            Identities(Identity(Measure1), Identity(Measure2)),
            Nim(NimRelation(Cellar, EuEli, Measure1, 'd'), NimRelation(Cellar, EuEli, Measure2, 'e')),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(2, result.Reconciliations!);
        Assert.IsTrue(result.Reconciliations![0].IsDerived());
        Assert.AreEqual(LocalEu, result.Reconciliations[0].LegiluxLocalEuWorkUri);
        Assert.AreEqual(Cellar, result.Reconciliations[0].CellarWorkUri);
        Assert.AreEqual(IdentityCompletion, result.Reconciliations[0].LegiluxIdentityCompletionEvidenceRef);
        Assert.AreEqual(NimCompletion, result.Reconciliations[0].NimCompletionEvidenceRef);

        var row = result.Population!.Rows!.Single();
        Assert.AreEqual(Cellar, row.Bridge.EuWorkUri);
        Assert.HasCount(2, row.Bridge.Legilux.Sides);
        Assert.HasCount(2, row.Bridge.Nim.Sides);
        Assert.HasCount(2, row.Bridge.NormalisedEliJoins);
        Assert.HasCount(2, row.NormalisedEliJoinEvidenceReceipts);
        Assert.AreEqual(2, store.CreateCallCount);
    }

    [TestMethod]
    public async Task ARelationWithoutOneExactPublisherIdentityRefusesInsteadOfDisappearing()
    {
        var result = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1), LegiluxRelation(Measure2)),
            Identities(Identity(Measure1)),
            Nim(NimRelation(Cellar, EuEli, Measure1, 'd')));

        Assert.AreEqual(
            EuTranspositionBridgeReconciliationRefusal.LegiluxIdentityNotSingular,
            result.Refusal);
        Assert.IsNull(result.Population);
    }

    [TestMethod]
    public async Task AnUnusedIdentityObservationRefusesInsteadOfBeingSilentlyDiscarded()
    {
        var result = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)),
            Identities(Identity(Measure1), Identity(Measure3)),
            Nim(NimRelation(Cellar, EuEli, Measure1, 'd')));

        Assert.AreEqual(
            EuTranspositionBridgeReconciliationRefusal.IdentityObservationUnused,
            result.Refusal);
    }

    [TestMethod]
    public async Task OneEuEliCannotNameTwoCellarWorks()
    {
        var result = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)),
            Identities(Identity(Measure1)),
            Nim(
                NimRelation(Cellar, EuEli, Measure1, 'd'),
                NimRelation(OtherCellar, EuEli, Measure2, 'e')));

        Assert.AreEqual(
            EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
            result.Refusal);
    }

    [TestMethod]
    public async Task PublisherWorkKindOrEliDisagreementIsTypedRatherThanChosen()
    {
        const string RegulationEli = "http://data.europa.eu/eli/reg/2020/1/oj";
        var wrongKind = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)),
            Identities(Identity(Measure1, RegulationEli)),
            Nim(NimRelation(Cellar, RegulationEli, Measure1, 'd', EuWorkKind.Regulation)));
        var wrongEli = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)),
            Identities(Identity(Measure1)),
            Nim(NimRelation(Cellar, OtherEuEli, Measure1, 'd')));

        Assert.AreEqual(EuTranspositionBridgeReconciliationRefusal.LegiluxIdentityContradictsNim, wrongKind.Refusal);
        Assert.AreEqual(EuTranspositionBridgeReconciliationRefusal.LegiluxIdentityNotInNimPopulation, wrongEli.Refusal);
    }

    [TestMethod]
    public async Task ACellarDirectiveWhosePublisherEliNamesARegulationRefusesBeforeReconciliation()
    {
        var result = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)),
            Identities(Identity(Measure1)),
            Nim(NimRelation(
                Cellar,
                "http://data.europa.eu/eli/reg/2021/1187/oj",
                Measure1,
                'd')));

        Assert.AreEqual(
            EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
            result.Refusal);
        Assert.IsNull(result.Population);
    }

    [TestMethod]
    public async Task ADelegatedDirectiveRetainsItsRawTypeAcrossExactEliReconciliation()
    {
        const string DelegatedEli = "http://data.europa.eu/eli/dir_del/2020/1/oj";
        var result = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)),
            Identities(Identity(Measure1, DelegatedEli)),
            Nim(NimRelation(
                Cellar,
                DelegatedEli,
                Measure1,
                'd',
                EuWorkKind.Directive,
                EuNationalImplementingMeasureDiscoveryPlan.DelegatedDirectiveResourceTypeIri)));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.AreEqual(EuWorkKind.Directive, result.Population!.Rows!.Single().Bridge.WorkKind);
    }

    [TestMethod]
    public async Task OneCellarWorkCannotChooseBetweenTwoRawPublisherTypes()
    {
        const string DelegatedEli = "http://data.europa.eu/eli/dir_del/2020/1/oj";
        var result = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)),
            Identities(Identity(Measure1, DelegatedEli)),
            Nim(
                NimRelation(
                    Cellar,
                    DelegatedEli,
                    Measure1,
                    'd',
                    publisherWorkTypeIri:
                        EuNationalImplementingMeasureDiscoveryPlan.DelegatedDirectiveResourceTypeIri),
                NimRelation(
                    Cellar,
                    DelegatedEli,
                    Measure2,
                    'e',
                    publisherWorkTypeIri:
                        EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri)));

        Assert.AreEqual(
            EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
            result.Refusal);
        Assert.IsNull(result.Population);
    }

    [TestMethod]
    public async Task OnePublisherIdentityCannotBeBothAdmittedAndOutOfE5WorkKind()
    {
        var exclusion = new EuNationalImplementingMeasureOutOfE5WorkKindExclusion(
            Cellar,
            EuEli,
            EuNationalImplementingMeasureDiscoveryPlan.DecisionResourceTypeIri,
            Cellar,
            "72020D0001",
            EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri,
            null,
            NimCompletion);
        var nim = EuNationalImplementingMeasureProductionResult.Success(
            [NimRelation(Cellar, EuEli, Measure1, 'd')], [exclusion], NimCompletion, 4);

        var result = await ProduceAsync(
            Legilux(LegiluxRelation(Measure1)), Identities(Identity(Measure1)), nim);

        Assert.AreEqual(
            EuTranspositionBridgeReconciliationRefusal.NimWorkIdentityNotConsistent,
            result.Refusal);
        Assert.IsNull(result.Population);
    }

    [TestMethod]
    public async Task ARefusedSourceCannotBecomeACompletedEmptyPopulation()
    {
        var result = await ProduceAsync(
            LuxembourgTranspositionProductionResult.Refused(
                LuxembourgTranspositionProductionRefusal.QueryExecutionRefused, "refused"),
            Identities(),
            Nim());

        Assert.AreEqual(EuTranspositionBridgeReconciliationRefusal.SourceNotDelivered, result.Refusal);
        Assert.IsNull(result.Population);
    }

    private static Task<EuTranspositionBridgeReconciliationResult> ProduceAsync(
        LuxembourgTranspositionProductionResult legilux,
        LuxembourgTranspositionIdentityProductionResult identities,
        EuNationalImplementingMeasureProductionResult nim) =>
        new EuTranspositionBridgeReconciliationProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).ProduceAsync(
                legilux, identities, nim, CancellationToken.None);

    private static LuxembourgTranspositionProductionResult Legilux(
        params LuxembourgTranspositionRelation[] relations) =>
        LuxembourgTranspositionProductionResult.Success(relations, LegiluxCompletion);

    private static LuxembourgTranspositionRelation LegiluxRelation(string measure) =>
        new(LocalEu, measure,
            new EuTranspositionSourceAcquisition(
                EuTranspositionAssertedBy.Legilux,
                EuRelationAcquisitionState.Complete,
                [new EuTranspositionSide(
                    EuTranspositionAssertedBy.Legilux, measure, Ref('f'), null, null)],
                LegiluxCompletion));

    private static LuxembourgTranspositionIdentityProductionResult Identities(
        params LuxembourgTranspositionIdentityRelation[] relations) =>
        LuxembourgTranspositionIdentityProductionResult.Success(relations, IdentityCompletion, 4);

    private static LuxembourgTranspositionIdentityRelation Identity(
        string measure,
        string euEli = EuEli) =>
        new(measure, LocalEu, euEli, EliKind(euEli, EuWorkKind.Directive), IdentityCompletion);

    private static EuNationalImplementingMeasureProductionResult Nim(
        params EuNationalImplementingMeasureRelation[] relations) =>
        EuNationalImplementingMeasureProductionResult.Success(relations, [], NimCompletion, 4);

    private static EuNationalImplementingMeasureRelation NimRelation(
        string cellar,
        string euEli,
        string measure,
        char evidence,
        EuWorkKind kind = EuWorkKind.Directive,
        string? publisherWorkTypeIri = null) =>
        new(
            cellar,
            CellarKind(cellar, euEli, kind),
            publisherWorkTypeIri ?? (kind == EuWorkKind.Regulation
                ? EuNationalImplementingMeasureDiscoveryPlan.RegulationResourceTypeIri
                : EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri),
            $"http://publications.europa.eu/resource/cellar/{evidence}{new string(evidence, 7)}-{evidence}{new string(evidence, 3)}-4{new string(evidence, 3)}-8{new string(evidence, 3)}-{new string(evidence, 12)}",
            "72020L0001",
            EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri,
            measure.Replace("https://", "http://", StringComparison.Ordinal),
            new EuTranspositionSourceAcquisition(
                EuTranspositionAssertedBy.Nim,
                EuRelationAcquisitionState.Complete,
                [new EuTranspositionSide(
                    EuTranspositionAssertedBy.Nim, measure, Ref(evidence),
                    EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri)],
                NimCompletion));

    private static EuWorkKindAssertion CellarKind(string cellar, string eli, EuWorkKind kind) =>
        new(new OfficialIdentitySet(PublisherId.EuEurLex,
            [
                new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, cellar),
                new OfficialIdentifier(FactsIdentifierFamily.Eli, eli),
            ]), kind);

    private static EuWorkKindAssertion EliKind(string eli, EuWorkKind kind) =>
        new(new OfficialIdentitySet(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.Eli, eli)]), kind);

    private static SourceArtifactRef Ref(char value) =>
        new($"urn:uuid:{new string(value, 8)}-{new string(value, 4)}-4{new string(value, 3)}-8{new string(value, 3)}-{new string(value, 12)}",
            new string(value, 64));
}
