using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgTranspositionIdentityProducerTests
{
    private const string Measure =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2024/01/01/a1/jo";
    private const string LocalEuWork =
        "http://data.legilux.public.lu/eli/dir_ue/2020/284/jo";
    private const string EuEli = "http://data.europa.eu/eli/dir/2020/284/oj";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:40dd2f35-4ce3-43da-a541-76d3625b0461", new string('d', 64));

    [TestMethod]
    public async Task TheProductionPathConsumesAReopenedLegiluxIdentityRow()
    {
        var plan = LuxembourgTranspositionIdentityDiscoveryPlan.Create();
        var page = PageJson(plan.CreateDeliveryProfile().ProjectionVariables);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
            {
                1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(1),
                2 or 4 => page,
                _ => throw new AssertFailedException("No request is admitted after both passes complete."),
            }));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgTranspositionIdentityProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var renderer = LuxembourgAcquisitionTestFixture.BuildRendererSource(4141);
        const string planResourceId = "urn:uuid:69a68d18-fbb0-4624-aa56-615142034de5";
        var request = new LuxembourgTranspositionIdentityRunRequest(plan, [EuEli], planResourceId, renderer);
        var witness = LuxembourgSourceWitness();

        var result = await producer.RunAsync(request, witness, CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(1, result.Relations!);
        Assert.AreEqual(4, result.ProductRequestCount);
        Assert.AreEqual(EuEli, result.Relations![0].EuEli);
        var workKind = result.Relations[0].WorkKindAssertion;
        Assert.IsNotNull(workKind);
        Assert.AreEqual(EuWorkKind.Directive, workKind.Kind);
        Assert.AreEqual(PublisherId.EuEurLex, workKind.Work.Publisher);
        Assert.AreEqual(EuEli, workKind.Work.Value(FactsIdentifierFamily.Eli));
        Assert.IsNotNull(result.CompletionEvidenceRef);
    }

    [TestMethod]
    public async Task ACompletedEmptyProductionRunIsAProvedEmptyIdentitySet()
    {
        var plan = LuxembourgTranspositionIdentityDiscoveryPlan.Create();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
            {
                1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(0),
                2 or 4 => EmptyPageJson(plan.CreateDeliveryProfile().ProjectionVariables),
                _ => throw new AssertFailedException("No request is admitted after both passes complete."),
            }));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgTranspositionIdentityProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var renderer = LuxembourgAcquisitionTestFixture.BuildRendererSource(4142);
        const string planResourceId = "urn:uuid:5a42b133-ebf0-4937-98fd-e65df71e89f7";
        var request = new LuxembourgTranspositionIdentityRunRequest(plan, [EuEli], planResourceId, renderer);
        var witness = LuxembourgSourceWitness();

        var result = await producer.RunAsync(request, witness, CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(0, result.Relations!);
        Assert.IsNotNull(result.CompletionEvidenceRef);
        Assert.AreEqual(4, result.ProductRequestCount);
    }

    [TestMethod]
    public async Task CompletedDisjointBatchesBecomeOneEvidenceBoundIdentityPopulation()
    {
        const string secondEli = "http://data.europa.eu/eli/dir/2021/123/oj";
        const string secondMeasure =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2024/01/02/a2/jo";
        const string secondLocalWork =
            "http://data.legilux.public.lu/eli/dir_ue/2021/123/jo";
        var firstEvidence = new SourceArtifactRef(
            "urn:uuid:42585634-a1b3-4f1d-8bdc-95b01addb60d", new string('1', 64));
        var secondEvidence = new SourceArtifactRef(
            "urn:uuid:154afec8-247e-443f-b400-4b846c5ab49f", new string('2', 64));
        var first = LuxembourgTranspositionIdentityProductionResult.Success(
            [Relation(Measure, LocalEuWork, EuEli, firstEvidence)], firstEvidence, 4);
        var second = LuxembourgTranspositionIdentityProductionResult.Success(
            [Relation(secondMeasure, secondLocalWork, secondEli, secondEvidence)], secondEvidence, 6);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();

        var result = await new LuxembourgTranspositionIdentityPopulationProducer(store).ProduceAsync(
            [
                new LuxembourgTranspositionIdentityCompletedBatch([EuEli], first),
                new LuxembourgTranspositionIdentityCompletedBatch([secondEli], second),
            ],
            "urn:uuid:8eecf902-d49e-4117-9a82-73144c0b9215",
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(2, result.Relations!);
        Assert.AreEqual(10, result.ProductRequestCount);
        Assert.IsNotNull(result.CompletionEvidenceRef);
        Assert.AreNotEqual(firstEvidence, result.CompletionEvidenceRef);
        Assert.AreNotEqual(secondEvidence, result.CompletionEvidenceRef);
        CollectionAssert.AreEquivalent(
            new[] { firstEvidence, secondEvidence },
            result.Relations!.Select(static relation => relation.CompletionEvidenceRef).ToArray());
        Assert.AreEqual(1, store.CreateCallCount);
        var receipt = await store.ReadByDigestAsync(
            result.CompletionEvidenceRef.Sha256, CancellationToken.None);
        var json = Encoding.UTF8.GetString(receipt.Span);
        StringAssert.Contains(json, "lex-luxembourg-transposition-identity-population/1");
        StringAssert.Contains(json, firstEvidence.Sha256);
        StringAssert.Contains(json, secondEvidence.Sha256);
    }

    [TestMethod]
    public async Task EmptyCompletedBatchesRemainValidButRowsOutsideTheirSelectionRefuse()
    {
        const string secondEli = "http://data.europa.eu/eli/dir/2021/123/oj";
        var emptyEvidence = new SourceArtifactRef(
            "urn:uuid:69c18cf9-201f-4e39-abf7-3bf41e58b2d2", new string('3', 64));
        var rowEvidence = new SourceArtifactRef(
            "urn:uuid:32cadfff-5c1e-44f2-931c-753e8c164c68", new string('4', 64));
        var empty = LuxembourgTranspositionIdentityProductionResult.Success([], emptyEvidence, 4);
        var outside = LuxembourgTranspositionIdentityProductionResult.Success(
            [Relation(Measure, LocalEuWork, secondEli, rowEvidence)], rowEvidence, 4);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgTranspositionIdentityPopulationProducer(store);

        var validEmpty = await producer.ProduceAsync(
            [new LuxembourgTranspositionIdentityCompletedBatch([EuEli], empty)],
            "urn:uuid:b086325a-6c64-45e1-8b6e-c10c64921622",
            CancellationToken.None);
        var refused = await producer.ProduceAsync(
            [new LuxembourgTranspositionIdentityCompletedBatch([EuEli], outside)],
            "urn:uuid:f022b9fd-8aa4-4f1d-ac5c-dedb86f58318",
            CancellationToken.None);

        Assert.IsTrue(validEmpty.Delivered, validEmpty.Detail);
        Assert.HasCount(0, validEmpty.Relations!);
        Assert.AreEqual(LuxembourgTranspositionIdentityProductionRefusal.BatchPopulationRefused,
            refused.Refusal);
        Assert.IsNull(refused.Relations);
    }

    [TestMethod]
    public async Task ACompositeReceiptCustodyFailureRefusesThePopulationAfterCountingTheBatchRequests()
    {
        var evidence = new SourceArtifactRef(
            "urn:uuid:87a184b6-896e-452f-b81b-9d1343f74b99", new string('5', 64));
        var batch = LuxembourgTranspositionIdentityProductionResult.Success(
            [Relation(Measure, LocalEuWork, EuEli, evidence)], evidence, 4);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
            failWriteDigest: static (_, _) => true);

        var result = await new LuxembourgTranspositionIdentityPopulationProducer(store).ProduceAsync(
            [new LuxembourgTranspositionIdentityCompletedBatch([EuEli], batch)],
            "urn:uuid:0428f22e-d51a-4daf-a432-669c6856e830",
            CancellationToken.None);

        Assert.AreEqual(
            LuxembourgTranspositionIdentityProductionRefusal.CompositeEvidenceNotHeld,
            result.Refusal);
        Assert.AreEqual(4, result.ProductRequestCount);
        Assert.IsNull(result.Relations);
        Assert.IsNull(result.CompletionEvidenceRef);
    }

    [TestMethod]
    public void AVerifiedPublisherRowRetainsBothLegiluxCoordinatesAndTheEuAssertion()
    {
        var result = Decode(Row());

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(1, result.Relations!);
        var relation = result.Relations![0];
        Assert.AreEqual(Measure, relation.NationalMeasureUri);
        Assert.AreEqual(LocalEuWork, relation.LocalEuWorkUri);
        Assert.AreEqual(EuEli, relation.EuEli);
        Assert.AreEqual(Evidence, relation.CompletionEvidenceRef);
        Assert.AreEqual(PublisherId.EuEurLex, relation.WorkKindAssertion!.Work.Publisher);
        Assert.AreEqual(EuEli, relation.WorkKindAssertion.Work.Value(FactsIdentifierFamily.Eli));
        Assert.AreEqual(EuWorkKind.Directive, relation.WorkKindAssertion.Kind);
    }

    [TestMethod]
    public void AnOfficialAdministrativeMeasureIsRetainedAsANationalImplementingMeasure()
    {
        const string AdministrativeMeasure =
            "http://data.legilux.public.lu/eli/etat/adm/amin/2026/07/13/b3284/jo";

        var result = Decode(Row(measure: AdministrativeMeasure));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.AreEqual(AdministrativeMeasure, result.Relations!.Single().NationalMeasureUri);
    }

    [TestMethod]
    public void APublisherIriOutsideTheTwoNationalMeasureFamiliesIsStillRefused()
    {
        var result = Decode(Row(measure: LocalEuWork));

        Assert.AreEqual(LuxembourgTranspositionIdentityProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "national-measure ELI");
        Assert.IsNull(result.Relations);
    }

    [TestMethod]
    public void MissingIdentityRefusesButMissingOptionalClassificationDoesNotInventOne()
    {
        var missingIdentity = Decode(Row(euEli: null));
        var missingKind = Decode(Row(workKind: null));

        Assert.AreEqual(
            LuxembourgTranspositionIdentityProductionRefusal.RowNotAdmitted,
            missingIdentity.Refusal);
        StringAssert.Contains(missingIdentity.Detail, "eu_eli");
        Assert.IsTrue(missingKind.Delivered, missingKind.Detail);
        Assert.IsNull(missingKind.Relations!.Single().WorkKindAssertion);
    }

    [TestMethod]
    public void ForeignIdentityAndUnsupportedClassReachTheTypedRefusal()
    {
        var foreign = Decode(Row(euEli: "https://example.invalid/eli/dir/2020/284/oj"));
        var unsupported = Decode(Row(workKind:
            "http://data.legilux.public.lu/resource/ontology/jolux#EURegulation"));

        Assert.AreEqual(LuxembourgTranspositionIdentityProductionRefusal.RowNotAdmitted, foreign.Refusal);
        StringAssert.Contains(foreign.Detail, "EU publisher ELI");
        Assert.AreEqual(LuxembourgTranspositionIdentityProductionRefusal.RowNotAdmitted, unsupported.Refusal);
        StringAssert.Contains(unsupported.Detail, "EUDirective");
    }

    [TestMethod]
    public void ARegulationEliCannotBecomeAPublisherDirectiveAssertion()
    {
        var result = Decode(Row(euEli: "http://data.europa.eu/eli/reg/2020/284/oj"));

        Assert.AreEqual(LuxembourgTranspositionIdentityProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "directive");
        Assert.IsNull(result.Relations);
    }

    [TestMethod]
    public void ALocalTargetMappedToTwoEuIdentitiesIsRefusedRatherThanChosen()
    {
        var result = Decode(
            Row(),
            Row(euEli: "http://data.europa.eu/eli/dir/2020/285/oj"));

        Assert.AreEqual(
            LuxembourgTranspositionIdentityProductionRefusal.AmbiguousIdentity,
            result.Refusal);
        Assert.IsNull(result.Relations);
    }

    [TestMethod]
    public void CursorSubstitutionCannotChangeThePublisherClaim()
    {
        var result = Decode(Row(key3: "http://data.europa.eu/eli/dir/2020/999/oj"));

        Assert.AreEqual(LuxembourgTranspositionIdentityProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "key_3");
    }

    [TestMethod]
    public void ACompletedIdentityAcquisitionProjectsTheIndependentLegiluxColumn()
    {
        var identities = Decode(Row());

        var result = LuxembourgTranspositionProducer.Produce(identities);

        Assert.IsTrue(result.Delivered, result.Detail);
        var relation = result.Relations!.Single();
        Assert.AreEqual(Measure, relation.LegiluxMeasureUri);
        Assert.AreEqual(LocalEuWork, relation.EuWorkUri);
        Assert.AreEqual(Evidence, relation.EuWorkIdentityEvidenceRef);
        Assert.AreEqual(Evidence, relation.Acquisition.CompletionEvidenceRef);
        Assert.AreEqual(Measure, relation.Acquisition.Sides.Single().NationalMeasureUri);
    }

    [TestMethod]
    public void ARefusedIdentityAcquisitionCannotBecomeACompletedEmptyLegiluxColumn()
    {
        var identities = Decode(Row(euEli: null));

        var result = LuxembourgTranspositionProducer.Produce(identities);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(LuxembourgTranspositionProductionRefusal.QueryExecutionRefused, result.Refusal);
        Assert.IsNull(result.Relations);
    }

    private static LuxembourgTranspositionIdentityProductionResult Decode(
        params RepeatedEnumerationRow[] rows) =>
        LuxembourgTranspositionIdentityProducer.DecodeRows(
            rows,
            LuxembourgTranspositionIdentityDiscoveryPlan.Create().CreateDeliveryProfile(),
            Evidence);

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(4143);
        return plan.BindCount(
            planResourceId,
            "urn:uuid:22dc1df7-77e0-4680-94d7-99f113da97bd",
            "urn:uuid:4e0e59a8-bbf8-466a-8a55-44104bd97f84",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(4143)).Request;
    }

    private static RepeatedEnumerationRow Row(
        string? euEli = EuEli,
        string? workKind = LuxembourgTranspositionIdentityDiscoveryPlan.EuDirectiveClassIri,
        string? key3 = null,
        string measure = Measure)
    {
        static RepeatedEnumerationRdfTerm Plain(string value) =>
            RepeatedEnumerationRdfTerm.Literal(value, null, null);
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(measure),
            RepeatedEnumerationRdfTerm.Iri(LocalEuWork),
            euEli is null ? RepeatedEnumerationRdfTerm.Unbound() : RepeatedEnumerationRdfTerm.Iri(euEli),
            workKind is null ? RepeatedEnumerationRdfTerm.Unbound() : RepeatedEnumerationRdfTerm.Iri(workKind),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            Plain(measure),
            Plain(LocalEuWork),
            Plain(key3 ?? euEli ?? string.Empty),
            Plain(workKind ?? string.Empty),
        };
        var keys = terms[5..9];
        return new RepeatedEnumerationRow(terms, keys, keys);
    }

    private static LuxembourgTranspositionIdentityRelation Relation(
        string measure,
        string localWork,
        string euEli,
        SourceArtifactRef evidence) =>
        new(measure, localWork, euEli, null, evidence);

    private static string PageJson(IReadOnlyList<string> projection)
    {
        static object Iri(string value) => new Dictionary<string, string>
        {
            ["type"] = "uri",
            ["value"] = value,
        };
        static object Literal(string value, string? datatype = null)
        {
            var term = new Dictionary<string, string> { ["type"] = "literal", ["value"] = value };
            if (datatype is not null)
            {
                term["datatype"] = datatype;
            }
            return term;
        }

        var binding = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["measure"] = Iri(Measure),
            ["local_eu_work"] = Iri(LocalEuWork),
            ["eu_work_kind"] = Iri(LuxembourgTranspositionIdentityDiscoveryPlan.EuDirectiveClassIri),
            ["multiplicity"] = Literal("1", XsdInteger),
            ["key_1"] = Literal(Measure),
            ["key_2"] = Literal(LocalEuWork),
            ["eu_eli"] = Iri(EuEli),
            ["key_3"] = Literal(EuEli),
            ["key_4"] = Literal(LuxembourgTranspositionIdentityDiscoveryPlan.EuDirectiveClassIri),
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = projection },
            results = new { distinct = false, ordered = true, bindings = new[] { binding } },
        });
    }

    private static string EmptyPageJson(IReadOnlyList<string> projection) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = projection },
            results = new { distinct = false, ordered = true, bindings = Array.Empty<object>() },
        });
}
