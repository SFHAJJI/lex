using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgQueryExecutionAdapterTests
{
    private const string TransposesPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#transposes";
    private const string TransposingMeasure =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
    private const string TransposedEuWork =
        "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";

    [TestMethod]
    public async Task AVerifiedPublisherTransposesRowBecomesOnlyTheLegiluxBridgeSide()
    {
        var execution = await TransposesExecutionAsync(
            RelationTriplesRowsJson((TransposingMeasure, TransposesPredicate, TransposedEuWork)));

        var result = LuxembourgTranspositionProducer.Produce(execution);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(1, result.Relations!);
        var relation = result.Relations![0];
        Assert.AreEqual(TransposedEuWork, relation.EuWorkUri);
        Assert.AreEqual(TransposingMeasure, relation.LegiluxMeasureUri);
        Assert.AreEqual(EuTranspositionAssertedBy.Legilux, relation.Acquisition.AssertedBy);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, relation.Acquisition.Acquisition);
        Assert.AreEqual(TransposingMeasure, relation.Acquisition.Sides[0]!.NationalMeasureUri);
        Assert.IsNull(relation.Acquisition.Sides[0].MemberStateDisclaimer);
        Assert.IsNull(relation.Acquisition.Sides[0].MemberStateDisclaimerSourceUri);
        Assert.AreEqual(
            execution.RelationFamilyAcquisitions.Single(value =>
                value.PredicateIri == TransposesPredicate).CompletionEvidence!.AcquisitionRunRef,
            relation.Acquisition.CompletionEvidenceRef);
    }

    [TestMethod]
    public async Task ACompletedEmptyTransposesFamilyIsProvenAbsenceRatherThanAnOpenQuestion()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, EmptyRelationFamilyDeliveringHandler()), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);
        var execution = await adapter.RunAsync(
            [(partitionRequest, witness, null)], RelationFamilyKey, null, null,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(),
            CancellationToken.None);

        var result = LuxembourgTranspositionProducer.Produce(execution);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsEmpty(result.Relations!);
        var acquisition = result.ForAssertedEuWork(TransposedEuWork);
        Assert.AreEqual(EuTranspositionAssertedBy.Legilux, acquisition.AssertedBy);
        Assert.IsTrue(acquisition.ProvesAbsence());
    }

    [TestMethod]
    public async Task AnAdapterRefusalCannotBeReadAsACompletedEmptyLegiluxSet()
    {
        var execution = await TransposesExecutionAsync(RelationTriplesRowsJson(
            (TransposingMeasure,
                "http://data.legilux.public.lu/resource/ontology/jolux#futureRelation",
                TransposedEuWork)));

        Assert.AreEqual(LuxembourgQueryExecutionRefusal.RelationRowPredicateNotAdmitted, execution.Refusal?.Code);
        var result = LuxembourgTranspositionProducer.Produce(execution);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(LuxembourgTranspositionProductionRefusal.QueryExecutionRefused, result.Refusal);
        Assert.ThrowsExactly<InvalidOperationException>(() => result.ForAssertedEuWork(TransposedEuWork));
    }

    private static async Task<LuxembourgQueryExecutionResult> TransposesExecutionAsync(string rows)
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                request, LuxembourgAcquisitionTestFixture.CountJson(1)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(request, rows),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(request, RelationAssertionsRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);
        return await adapter.RunAsync(
            [(partitionRequest, witness, null)], RelationFamilyKey, null, null,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(),
            CancellationToken.None);
    }
}
