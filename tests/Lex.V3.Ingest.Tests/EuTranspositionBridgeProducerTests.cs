using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuTranspositionBridgeProducerTests
{
    private const string EuWork = "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";
    private const string OtherWork = "http://publications.europa.eu/resource/cellar/33333333-3333-4333-8333-333333333333";
    private const string LuMeasure = "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo";
    private static readonly SourceArtifactRef Evidence = new("urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", new string('a', 64));

    [TestMethod]
    public void OneDeliveredColumnFromEachPublisherBecomesOneUnmergedBridge()
    {
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Directive),
            Legilux(LegiluxSide()), Nim(NimSide()));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsNotNull(result.Bridge);
        Assert.AreEqual(EuTranspositionAssertedBy.Legilux, result.Bridge!.Legilux.AssertedBy);
        Assert.AreEqual(EuTranspositionAssertedBy.Nim, result.Bridge.Nim.AssertedBy);
        Assert.AreNotSame(result.Bridge.Legilux.Side, result.Bridge.Nim.Side);
        Assert.IsNull(result.Bridge.NormalisedEliJoin, "no derived join is minted without separate join evidence.");
    }

    [TestMethod]
    public void ARegulationWithCompletedEmptyColumnsGetsTheTypedNotTransposableAnswer()
    {
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Regulation),
            Legilux(ProvenAbsent(EuTranspositionAssertedBy.Legilux)),
            Nim(ProvenAbsent(EuTranspositionAssertedBy.Nim)));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.AreEqual(EuTransposability.NotTransposable, result.Bridge!.Transposability);
    }

    [TestMethod]
    public void MultipleAssertionsFromOnePublisherAreRefusedRatherThanMergedOrChosen()
    {
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Directive),
            Legilux(LegiluxSide(), LegiluxSide()), Nim(NimSide()));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuTranspositionBridgeProductionRefusal.LegiluxNotSingular, result.Refusal);
        Assert.IsNull(result.Bridge);
    }

    [TestMethod]
    public void AWorkKindAssertionForAnotherWorkCannotClassifyThisBridge()
    {
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(OtherWork, EuWorkKind.Directive),
            Legilux(LegiluxSide()), Nim(NimSide()));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuTranspositionBridgeProductionRefusal.WorkKindNotForEuWork, result.Refusal);
    }

    [TestMethod]
    public void ARefusedSourceCannotBeReadAsAnEmptyCompletedColumn()
    {
        var legiluxRefused = EuTranspositionBridgeProducer.Produce(
            EuWork,
            Kind(EuWork, EuWorkKind.Directive),
            LuxembourgTranspositionProductionResult.Refused(
                LuxembourgTranspositionProductionRefusal.QueryExecutionRefused, "refused"),
            Nim(NimSide()));
        Assert.AreEqual(EuTranspositionBridgeProductionRefusal.LegiluxNotDelivered, legiluxRefused.Refusal);

        var nimRefused = EuTranspositionBridgeProducer.Produce(
            EuWork,
            Kind(EuWork, EuWorkKind.Directive),
            Legilux(LegiluxSide()),
            EuNationalImplementingMeasureProductionResult.Refused(
                EuNationalImplementingMeasureProductionRefusal.EnumerationRefused, "refused", 0));
        Assert.AreEqual(EuTranspositionBridgeProductionRefusal.NimNotDelivered, nimRefused.Refusal);
    }

    [TestMethod]
    public void MultipleNimAssertionsAndARegulationContradictionAreTypedRefusals()
    {
        var multipleNim = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Directive),
            Legilux(LegiluxSide()), Nim(NimSide(), NimSide()));
        Assert.AreEqual(EuTranspositionBridgeProductionRefusal.NimNotSingular, multipleNim.Refusal);

        var regulation = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Regulation),
            Legilux(LegiluxSide()), Nim(NimSide()));
        Assert.AreEqual(EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind, regulation.Refusal);
    }

    private static EuWorkKindAssertion Kind(string work, EuWorkKind kind) =>
        new(new OfficialIdentitySet(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, work)]), kind);

    private static LuxembourgTranspositionProductionResult Legilux(params EuTranspositionSourceAcquisition[] columns) =>
        LuxembourgTranspositionProductionResult.Success(columns.Select(column =>
            new LuxembourgTranspositionRelation(EuWork, LuMeasure, column)).ToArray(), Evidence);

    private static EuNationalImplementingMeasureProductionResult Nim(params EuTranspositionSourceAcquisition[] columns) =>
        EuNationalImplementingMeasureProductionResult.Success(columns.Select(column =>
            new EuNationalImplementingMeasureRelation(EuWork, EuWork, "72020L0001", "https://example.invalid/implements", LuMeasure, column)).ToArray(), Evidence, 0);

    private static EuTranspositionSourceAcquisition LegiluxSide() =>
        new(EuTranspositionAssertedBy.Legilux, EuRelationAcquisitionState.Complete,
            new EuTranspositionSide(EuTranspositionAssertedBy.Legilux, LuMeasure, Evidence, null, null), Evidence);

    private static EuTranspositionSourceAcquisition NimSide() =>
        new(EuTranspositionAssertedBy.Nim, EuRelationAcquisitionState.Complete,
            new EuTranspositionSide(EuTranspositionAssertedBy.Nim, LuMeasure, Evidence,
                EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri), Evidence);

    private static EuTranspositionSourceAcquisition ProvenAbsent(EuTranspositionAssertedBy assertedBy) =>
        new(assertedBy, EuRelationAcquisitionState.Complete, null, Evidence);
}
