using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuTranspositionBridgePopulationProducerTests
{
    private const string Directive =
        "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";
    private const string Regulation =
        "http://publications.europa.eu/resource/cellar/33333333-3333-4333-8333-333333333333";
    private const string OutsideScope =
        "http://publications.europa.eu/resource/cellar/44444444-4444-4444-8444-444444444444";
    private const string LegiluxEli =
        "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo";
    private const string NimEli =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo";
    private static readonly SourceArtifactRef Completion = Ref('a');
    private static readonly SourceArtifactRef LegiluxEvidence = Ref('b');
    private static readonly SourceArtifactRef NimEvidence = Ref('c');

    [TestMethod]
    public async Task EveryDeclaredWorkIsProducedAndDerivedJoinEvidenceIsHeldByDigest()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuTranspositionBridgePopulationProducer(store);

        var result = await producer.ProduceAsync(
            [Kind(Regulation, EuWorkKind.Regulation), Kind(Directive, EuWorkKind.Directive)],
            Legilux(Relation(Directive, LegiluxEli, LegiluxSide())),
            Nim(NimRelation(Directive, NimEli, NimSide())),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsNotNull(result.Rows);
        Assert.HasCount(2, result.Rows);
        Assert.AreEqual(Directive, result.Rows[0].Bridge.EuWorkUri);
        Assert.AreEqual(Regulation, result.Rows[1].Bridge.EuWorkUri);
        Assert.HasCount(1, result.Rows[0].NormalisedEliJoinEvidenceReceipts);
        Assert.AreEqual(
            result.Rows[0].Bridge.NormalisedEliJoins[0].EvidenceRef.Sha256,
            result.Rows[0].NormalisedEliJoinEvidenceReceipts[0].Reference.ContentSha256);
        Assert.IsEmpty(result.Rows[1].NormalisedEliJoinEvidenceReceipts);
        Assert.AreEqual(EuTransposability.NotTransposable, result.Rows[1].Bridge.Transposability);
        Assert.AreEqual(1, store.CreateCallCount);
    }

    [TestMethod]
    public async Task EvidenceBoundOutOfE5WorkKindRowsSurviveTheCompletedPopulation()
    {
        var decision = new EuNationalImplementingMeasureOutOfE5WorkKindExclusion(
            OutsideScope,
            "http://data.europa.eu/eli/dec/2020/1/oj",
            EuNationalImplementingMeasureDiscoveryPlan.DecisionResourceTypeIri,
            OutsideScope,
            "72020D0001",
            EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri,
            null,
            Completion);
        var nim = EuNationalImplementingMeasureProductionResult.Success([], [decision], [], Completion, 0);

        var result = await new EuTranspositionBridgePopulationProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).ProduceAsync(
                [Kind(Directive, EuWorkKind.Directive)], Legilux(), nim, CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        var retained = result.OutOfE5WorkKindExclusions!.Single();
        Assert.AreSame(decision, retained);
        Assert.AreEqual("out_of_e5_work_kind", retained.Disposition);
    }

    [TestMethod]
    public async Task EvidenceBoundPublisherCoordinateConflictsSurviveTheCompletedPopulation()
    {
        var conflict = new EuNationalImplementingMeasurePublisherCoordinatesConflict(
            OutsideScope,
            "http://data.europa.eu/eli/reg/2021/1187/oj",
            EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
            OutsideScope,
            "72021L1187LUX_202303648",
            EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri,
            LegiluxEli,
            Completion);
        var nim = EuNationalImplementingMeasureProductionResult.Success(
            [], [], [conflict], Completion, 0);

        var result = await new EuTranspositionBridgePopulationProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).ProduceAsync(
                [Kind(Directive, EuWorkKind.Directive)], Legilux(), nim, CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        var retained = result.PublisherCoordinateConflicts!.Single();
        Assert.AreSame(conflict, retained);
        Assert.AreEqual("publisher_coordinates_conflict", retained.Disposition);
    }

    [TestMethod]
    public async Task AnEmptyOrDuplicateDeclaredScopeCannotMasqueradeAsACompletePopulation()
    {
        var producer = new EuTranspositionBridgePopulationProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore());

        var empty = await producer.ProduceAsync([], Legilux(), Nim(), CancellationToken.None);
        var duplicate = await producer.ProduceAsync(
            [Kind(Directive, EuWorkKind.Directive), Kind(Directive, EuWorkKind.Directive)],
            Legilux(), Nim(), CancellationToken.None);

        Assert.AreEqual(EuTranspositionBridgePopulationRefusal.WorkScopeEmpty, empty.Refusal);
        Assert.AreEqual(EuTranspositionBridgePopulationRefusal.WorkScopeNotUnique, duplicate.Refusal);
    }

    [TestMethod]
    public async Task ASourceObservationOutsideTheDeclaredScopeIsRefusedRatherThanDiscarded()
    {
        var producer = new EuTranspositionBridgePopulationProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore());

        var result = await producer.ProduceAsync(
            [Kind(Directive, EuWorkKind.Directive)],
            Legilux(Relation(Directive, LegiluxEli, LegiluxSide()),
                Relation(OutsideScope, LegiluxEli, LegiluxSide())),
            Nim(),
            CancellationToken.None);

        Assert.AreEqual(EuTranspositionBridgePopulationRefusal.SourceWorkOutsideScope, result.Refusal);
        Assert.IsNull(result.Rows);
    }

    [TestMethod]
    public async Task APerWorkBridgeRefusalRefusesThePopulationBeforeAnyDerivedEvidenceWrite()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuTranspositionBridgePopulationProducer(store);

        var result = await producer.ProduceAsync(
            [Kind(Directive, EuWorkKind.Directive)],
            Legilux(Relation(Directive, LegiluxEli, LegiluxSide()),
                Relation(Directive, LegiluxEli, LegiluxSide())),
            Nim(NimRelation(Directive, NimEli, NimSide())),
            CancellationToken.None);

        Assert.AreEqual(EuTranspositionBridgePopulationRefusal.BridgeRefused, result.Refusal);
        Assert.AreEqual(0, store.CreateCallCount);
        Assert.IsNull(result.Rows);
    }

    [TestMethod]
    public async Task ARefusedSourceCannotBecomeACompletedEmptyPopulationColumn()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuTranspositionBridgePopulationProducer(store);

        var result = await producer.ProduceAsync(
            [Kind(Directive, EuWorkKind.Directive)],
            LuxembourgTranspositionProductionResult.Refused(
                LuxembourgTranspositionProductionRefusal.QueryExecutionRefused, "publisher refused"),
            Nim(),
            CancellationToken.None);

        Assert.AreEqual(EuTranspositionBridgePopulationRefusal.SourceNotDelivered, result.Refusal);
        Assert.AreEqual(0, store.CreateCallCount);
        Assert.IsNull(result.Rows);
    }

    [TestMethod]
    public async Task AJoinEvidenceCustodyFailureRefusesThePopulation()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
            failWriteDigest: static (_, _) => true);
        var producer = new EuTranspositionBridgePopulationProducer(store);

        var result = await producer.ProduceAsync(
            [Kind(Directive, EuWorkKind.Directive)],
            Legilux(Relation(Directive, LegiluxEli, LegiluxSide())),
            Nim(NimRelation(Directive, NimEli, NimSide())),
            CancellationToken.None);

        Assert.AreEqual(EuTranspositionBridgePopulationRefusal.JoinEvidenceNotHeld, result.Refusal);
        Assert.IsNull(result.Rows);
    }

    [TestMethod]
    public async Task AReceiptForDifferentBytesRefusesThePopulationInsteadOfEscapingAsAnException()
    {
        var producer = new EuTranspositionBridgePopulationProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
                substituteReceiptDigest: static (_, _) => true));

        var result = await producer.ProduceAsync(
            [Kind(Directive, EuWorkKind.Directive)],
            Legilux(Relation(Directive, LegiluxEli, LegiluxSide())),
            Nim(NimRelation(Directive, NimEli, NimSide())),
            CancellationToken.None);

        Assert.AreEqual(EuTranspositionBridgePopulationRefusal.JoinEvidenceReceiptMismatch, result.Refusal);
        Assert.IsNull(result.Rows);
    }

    [TestMethod]
    public async Task APopulationRowRequiresJoinAndMatchingReceiptTogether()
    {
        var joined = EuTranspositionBridgeProducer.Produce(
            Directive,
            Kind(Directive, EuWorkKind.Directive),
            Legilux(Relation(Directive, LegiluxEli, LegiluxSide())),
            Nim(NimRelation(Directive, NimEli, NimSide()))).Bridge!;
        var unjoined = EuTranspositionBridgeProducer.Produce(
            Directive,
            Kind(Directive, EuWorkKind.Directive),
            Legilux(Relation(Directive, LegiluxEli, LegiluxSide())),
            Nim()).Bridge!;
        var receipt = await new EuAcquisitionTestFixture.EuInMemoryCustodyStore().CreateAsync(
            new byte[] { 0xff }, CustodyClass.NightlyFloor90d, CancellationToken.None);

        Assert.ThrowsExactly<ArgumentException>(
            () => new EuTranspositionBridgePopulationRow(joined, []));
        Assert.ThrowsExactly<ArgumentException>(
            () => new EuTranspositionBridgePopulationRow(unjoined, [receipt]));
        Assert.ThrowsExactly<ArgumentException>(
            () => new EuTranspositionBridgePopulationRow(joined, [receipt]));
    }

    private static EuWorkKindAssertion Kind(string work, EuWorkKind kind) =>
        new(new OfficialIdentitySet(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, work)]), kind);

    private static LuxembourgTranspositionProductionResult Legilux(
        params LuxembourgTranspositionRelation[] relations) =>
        LuxembourgTranspositionProductionResult.Success(relations, Completion);

    private static EuNationalImplementingMeasureProductionResult Nim(
        params EuNationalImplementingMeasureRelation[] relations) =>
        EuNationalImplementingMeasureProductionResult.Success(relations, [], [], Completion, 0);

    private static LuxembourgTranspositionRelation Relation(
        string work, string measure, EuTranspositionSourceAcquisition acquisition) =>
        new(work, measure, acquisition, Completion);

    private static EuNationalImplementingMeasureRelation NimRelation(
        string work, string? eli, EuTranspositionSourceAcquisition acquisition) =>
        new(work, Kind(work, EuWorkKind.Directive),
            EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri, work, "72020L0001",
            EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri, eli, acquisition);

    private static EuTranspositionSourceAcquisition LegiluxSide() =>
        new(EuTranspositionAssertedBy.Legilux, EuRelationAcquisitionState.Complete,
            [new EuTranspositionSide(EuTranspositionAssertedBy.Legilux, LegiluxEli,
                LegiluxEvidence, null, null)], Completion);

    private static EuTranspositionSourceAcquisition NimSide() =>
        new(EuTranspositionAssertedBy.Nim, EuRelationAcquisitionState.Complete,
            [new EuTranspositionSide(EuTranspositionAssertedBy.Nim, NimEli,
                NimEvidence, EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri)], Completion);

    private static SourceArtifactRef Ref(char value) =>
        new($"urn:uuid:{value}{value}{value}{value}{value}{value}{value}{value}-{value}{value}{value}{value}-4{value}{value}{value}-8{value}{value}{value}-{new string(value, 12)}",
            new string(value, 64));
}
