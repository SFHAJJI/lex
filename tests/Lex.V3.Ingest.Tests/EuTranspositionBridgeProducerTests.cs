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
        Assert.AreNotSame(result.Bridge.Legilux.Sides[0], result.Bridge.Nim.Sides[0]);
        Assert.HasCount(1, result.Bridge.NormalisedEliJoins);
        Assert.AreEqual(LuMeasure, result.Bridge.NormalisedEliJoins[0].NormalisedEli);
        Assert.IsTrue(result.Bridge.NormalisedEliJoins[0].IsDerived());

        var evidenceBytes = result.CopyNormalisedEliJoinEvidenceBytes().Single();
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(evidenceBytes)),
            result.Bridge.NormalisedEliJoins[0].EvidenceRef.Sha256);
        Assert.AreEqual(
            ContentDerivedIdentity.DeriveUuidUrn(
                "lex-v3/eu-transposition-normalised-eli-join/1",
                evidenceBytes),
            result.Bridge.NormalisedEliJoins[0].EvidenceRef.ResourceId);
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
        Assert.AreEqual(LuMeasure, result.Bridge!.Legilux.Sides[0]!.NationalMeasureUri);
        Assert.AreEqual(OtherLuMeasure, result.Bridge.Nim.Sides[0]!.NationalMeasureUri);
        Assert.IsEmpty(result.Bridge.NormalisedEliJoins);
        Assert.IsEmpty(result.CopyNormalisedEliJoinEvidenceBytes());
    }

    [TestMethod]
    public void ForeignPublisherUrisCannotBeRewrittenIntoALegiluxJoin()
    {
        const string legiluxColumn = "https://example.invalid/eli/etat/leg/loi/2020/01/01/a1/jo";
        const string nimColumn = "http://example.invalid/eli/etat/leg/loi/2020/01/01/a1/jo";
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Directive),
            LegiluxWithMeasure(legiluxColumn, LegiluxSide(legiluxColumn)),
            NimWithEli(nimColumn, NimSide(nimColumn)));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.AreEqual(legiluxColumn, result.Bridge!.Legilux.Sides[0]!.NationalMeasureUri);
        Assert.AreEqual(nimColumn, result.Bridge.Nim.Sides[0]!.NationalMeasureUri);
        Assert.IsEmpty(result.Bridge.NormalisedEliJoins);
        Assert.IsEmpty(result.CopyNormalisedEliJoinEvidenceBytes());
    }

    [TestMethod]
    public void EliPathCaseDifferencesRemainDistinct()
    {
        const string caseVariant = "http://data.legilux.public.lu/eli/etat/leg/LOI/2020/01/01/a1/jo";
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Directive),
            Legilux(LegiluxSide()), NimWithEli(caseVariant, NimSide(caseVariant)));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsEmpty(result.Bridge!.NormalisedEliJoins);
        Assert.IsEmpty(result.CopyNormalisedEliJoinEvidenceBytes());
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
    public void MultipleAssertionsStayInTheirPublisherColumnsAndEachExactMatchIsDisclosed()
    {
        const string secondLegilux = "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/02/a2/jo";
        const string secondNim = "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/02/a2/jo";
        var result = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Directive),
            LuxembourgTranspositionProductionResult.Success([
                new LuxembourgTranspositionRelation(EuWork, LuMeasure, LegiluxSide(), Evidence),
                new LuxembourgTranspositionRelation(EuWork, secondLegilux, LegiluxSide(secondLegilux), Evidence),
            ], Evidence),
            EuNationalImplementingMeasureProductionResult.Success([
                NimRelation(NimLuMeasure, NimSide()),
                NimRelation(secondNim, NimSide(secondNim)),
            ], [], [], Evidence, 0));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(2, result.Bridge!.Legilux.Sides);
        Assert.HasCount(2, result.Bridge.Nim.Sides);
        Assert.HasCount(2, result.Bridge.NormalisedEliJoins);
        Assert.HasCount(2, result.CopyNormalisedEliJoinEvidenceBytes());
    }

    [TestMethod]
    public void DistinctRawRowsForOneIdenticalNimAssertionRemainEvidenceButProjectOnce()
    {
        var side = NimSide(EuWork);
        var nim = EuNationalImplementingMeasureProductionResult.Success(
            [
                new EuNationalImplementingMeasureRelation(
                    EuWork, Kind(EuWork, EuWorkKind.Directive),
                    EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
                    EuWork, "72011L0022LUX_187364",
                    EuNationalImplementingMeasureDiscoveryPlan.LegacyImplementsDirectivePredicateIri,
                    null, side),
                new EuNationalImplementingMeasureRelation(
                    EuWork, Kind(EuWork, EuWorkKind.Directive),
                    EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
                    EuWork, "72011L0023LUX_187364",
                    EuNationalImplementingMeasureDiscoveryPlan.LegacyImplementsDirectivePredicateIri,
                    null, side),
            ], [], [], Evidence, 0);

        var result = EuTranspositionBridgeProducer.Produce(
            EuWork,
            Kind(EuWork, EuWorkKind.Directive),
            Legilux(ProvenAbsent(EuTranspositionAssertedBy.Legilux)),
            nim);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(2, nim.Relations!);
        CollectionAssert.AreEqual(
            new[] { "72011L0022LUX_187364", "72011L0023LUX_187364" },
            nim.Relations!.Select(static relation => relation.NimCelex).ToArray());
        Assert.HasCount(1, result.Bridge!.Nim.Sides);
        Assert.AreEqual(EuWork, result.Bridge.Nim.Sides[0].NationalMeasureUri);
    }

    [TestMethod]
    public void RepeatedNimUrisWithDifferentEvidenceStillRefuseInsteadOfChoosingOne()
    {
        var otherEvidence = new SourceArtifactRef(
            "urn:uuid:dddddddd-dddd-4ddd-8ddd-dddddddddddd", new string('d', 64));
        var otherSide = new EuTranspositionSourceAcquisition(
            EuTranspositionAssertedBy.Nim,
            EuRelationAcquisitionState.Complete,
            [new EuTranspositionSide(
                EuTranspositionAssertedBy.Nim, EuWork, otherEvidence,
                EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri)],
            Evidence);
        var nim = EuNationalImplementingMeasureProductionResult.Success(
            [
                new EuNationalImplementingMeasureRelation(
                    EuWork, Kind(EuWork, EuWorkKind.Directive),
                    EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
                    EuWork, "72011L0022LUX_187364",
                    EuNationalImplementingMeasureDiscoveryPlan.LegacyImplementsDirectivePredicateIri,
                    null, NimSide(EuWork)),
                new EuNationalImplementingMeasureRelation(
                    EuWork, Kind(EuWork, EuWorkKind.Directive),
                    EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
                    EuWork, "72011L0023LUX_187364",
                    EuNationalImplementingMeasureDiscoveryPlan.LegacyImplementsDirectivePredicateIri,
                    null, otherSide),
            ], [], [], Evidence, 0);

        var result = EuTranspositionBridgeProducer.Produce(
            EuWork,
            Kind(EuWork, EuWorkKind.Directive),
            Legilux(ProvenAbsent(EuTranspositionAssertedBy.Legilux)),
            nim);

        Assert.AreEqual(
            EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind,
            result.Refusal);
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
    public void ARegulationContradictionIsATypedRefusal()
    {
        var regulation = EuTranspositionBridgeProducer.Produce(EuWork, Kind(EuWork, EuWorkKind.Regulation),
            Legilux(LegiluxSide()), Nim(NimSide()));
        Assert.AreEqual(EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind, regulation.Refusal);
    }

    private static EuWorkKindAssertion Kind(string work, EuWorkKind kind) =>
        new(new OfficialIdentitySet(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, work)]), kind);

    private static LuxembourgTranspositionProductionResult Legilux(params EuTranspositionSourceAcquisition[] columns) =>
        LegiluxWithMeasure(LuMeasure, columns);

    private static LuxembourgTranspositionProductionResult LegiluxWithMeasure(
        string measureUri,
        params EuTranspositionSourceAcquisition[] columns) =>
        LuxembourgTranspositionProductionResult.Success(columns.Select(column =>
            new LuxembourgTranspositionRelation(EuWork, measureUri, column, Evidence)).ToArray(), Evidence);

    private static EuNationalImplementingMeasureProductionResult Nim(params EuTranspositionSourceAcquisition[] columns) =>
        NimWithEli(NimLuMeasure, columns);

    private static EuNationalImplementingMeasureProductionResult NimWithEli(
        string? eli,
        params EuTranspositionSourceAcquisition[] columns) =>
        EuNationalImplementingMeasureProductionResult.Success(columns.Select(column =>
            new EuNationalImplementingMeasureRelation(
                EuWork, Kind(EuWork, EuWorkKind.Directive),
                EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
                EuWork, "72020L0001",
                "https://example.invalid/implements", eli, column)).ToArray(), [], [], Evidence, 0);

    private static EuNationalImplementingMeasureRelation NimRelation(
        string eli,
        EuTranspositionSourceAcquisition column) =>
        new(EuWork, Kind(EuWork, EuWorkKind.Directive),
            EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
            EuWork, "72020L0001",
            "https://example.invalid/implements", eli, column);

    private static EuTranspositionSourceAcquisition LegiluxSide(string nationalMeasureUri = LuMeasure) =>
        new(EuTranspositionAssertedBy.Legilux, EuRelationAcquisitionState.Complete,
            [new EuTranspositionSide(EuTranspositionAssertedBy.Legilux, nationalMeasureUri, LegiluxEvidence, null, null)], Evidence);

    private static EuTranspositionSourceAcquisition NimSide(string nationalMeasureUri = NimLuMeasure) =>
        new(EuTranspositionAssertedBy.Nim, EuRelationAcquisitionState.Complete,
            [new EuTranspositionSide(EuTranspositionAssertedBy.Nim, nationalMeasureUri, NimEvidence,
                EuMemberStateDisclaimer.Text, EuMemberStateDisclaimer.SourceUri)], Evidence);

    private static EuTranspositionSourceAcquisition ProvenAbsent(EuTranspositionAssertedBy assertedBy) =>
        new(assertedBy, EuRelationAcquisitionState.Complete, [], Evidence);
}
