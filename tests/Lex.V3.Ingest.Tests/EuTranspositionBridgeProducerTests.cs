using System.Security.Cryptography;
using System.Text.Json;
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
    private const string NimLuMeasure = "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo";
    private const string OtherLuMeasure = "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/02/a2/jo";
    private static readonly SourceArtifactRef Evidence = new("urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", new string('a', 64));
    private static readonly SourceArtifactRef LegiluxEvidence = new("urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", new string('b', 64));
    private static readonly SourceArtifactRef NimEvidence = new("urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", new string('c', 64));

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
        Assert.IsNotNull(result.Bridge.NormalisedEliJoin);
        Assert.AreEqual(LuMeasure, result.Bridge.NormalisedEliJoin.NormalisedEli);
        Assert.IsTrue(result.Bridge.NormalisedEliJoin.IsDerived());

        var evidenceBytes = result.CopyNormalisedEliJoinEvidenceBytes();
        Assert.IsNotNull(evidenceBytes);
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(evidenceBytes)),
            result.Bridge.NormalisedEliJoin.EvidenceRef.Sha256);
        Assert.AreEqual(
            ContentDerivedIdentity.DeriveUuidUrn(
                "lex-v3/eu-transposition-normalised-eli-join/1",
                evidenceBytes),
            result.Bridge.NormalisedEliJoin.EvidenceRef.ResourceId);
        using var evidence = JsonDocument.Parse(evidenceBytes);
        Assert.AreEqual(EuWork, evidence.RootElement.GetProperty("eu_work_uri").GetString());
        Assert.AreEqual(LuMeasure, evidence.RootElement.GetProperty("legilux_eli").GetString());
        Assert.AreEqual(NimLuMeasure, evidence.RootElement.GetProperty("nim_eli").GetString());
        Assert.AreEqual(
            LegiluxEvidence.ResourceId,
            evidence.RootElement.GetProperty("legilux_evidence_resource_id").GetString());
        Assert.AreEqual(
            NimEvidence.ResourceId,
            evidence.RootElement.GetProperty("nim_evidence_resource_id").GetString());
    }

    [TestMethod]
    public void DifferentPublisherUrisRemainSeparateWithoutInventingAJoin()
    {
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Directive),
            Legilux(LegiluxSide()), NimWithEli(OtherLuMeasure, NimSide(OtherLuMeasure)));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.AreEqual(LuMeasure, result.Bridge!.Legilux.Side!.NationalMeasureUri);
        Assert.AreEqual(OtherLuMeasure, result.Bridge.Nim.Side!.NationalMeasureUri);
        Assert.IsNull(result.Bridge.NormalisedEliJoin);
        Assert.IsNull(result.CopyNormalisedEliJoinEvidenceBytes());
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
        NimWithEli(NimLuMeasure, columns);

    private static EuNationalImplementingMeasureProductionResult NimWithEli(
        string? eli,
        params EuTranspositionSourceAcquisition[] columns) =>
        EuNationalImplementingMeasureProductionResult.Success(columns.Select(column =>
            new EuNationalImplementingMeasureRelation(EuWork, EuWork, "72020L0001", "https://example.invalid/implements", eli, column)).ToArray(), Evidence, 0);

    private static EuTranspositionSourceAcquisition LegiluxSide() =>
        new(EuTranspositionAssertedBy.Legilux, EuRelationAcquisitionState.Complete,
            new EuTranspositionSide(EuTranspositionAssertedBy.Legilux, LuMeasure, LegiluxEvidence, null, null), Evidence);

    private static EuTranspositionSourceAcquisition NimSide(string nationalMeasureUri = NimLuMeasure) =>
        new(EuTranspositionAssertedBy.Nim, EuRelationAcquisitionState.Complete,
            new EuTranspositionSide(EuTranspositionAssertedBy.Nim, nationalMeasureUri, NimEvidence,
                EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri), Evidence);

    private static EuTranspositionSourceAcquisition ProvenAbsent(EuTranspositionAssertedBy assertedBy) =>
        new(assertedBy, EuRelationAcquisitionState.Complete, null, Evidence);
}
