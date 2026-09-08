using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuNationalImplementingMeasureProducerTests
{
    private const string Nim =
        "http://publications.europa.eu/resource/cellar/11111111-1111-4111-8111-111111111111";
    private const string EuWork =
        "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";
    private const string Eli = "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:311b1d41-f2ad-42fa-b55f-f109c92d08c4", new string('a', 64));

    [TestMethod]
    public async Task TheProductionPathTurnsACompletedEmptyPublisherRunIntoProvenAbsence()
    {
        var plan = EuNationalImplementingMeasureDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], profile.ProjectionVariables.ToArray()),
        };
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuNationalImplementingMeasureProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await producer.RunAsync(
            new EuNationalImplementingMeasureRunRequest(
                plan,
                "urn:uuid:fc8a5082-9008-48b2-a9b6-a396e2351911",
                EuAcquisitionTestFixture.BuildRendererSource(414)),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(0, result.Relations!);
        Assert.AreEqual(4, result.ProductRequestCount);
        Assert.IsTrue(result.ForEuWork(EuWork)[0].ProvesAbsence());
    }

    [TestMethod]
    public async Task TheProductionPathConsumesAReopenedPublisherRowRatherThanCallerSuppliedData()
    {
        var plan = EuNationalImplementingMeasureDiscoveryPlan.Create();
        var page = NimPageJson(plan.CreateDeliveryProfile().ProjectionVariables);
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = new(
                "Census",
                [
                    EuAcquisitionTestFixture.EuCountJson(1), page,
                    EuAcquisitionTestFixture.EuCountJson(1), page,
                ]),
        };
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuNationalImplementingMeasureProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await producer.RunAsync(
            new EuNationalImplementingMeasureRunRequest(
                plan,
                "urn:uuid:7f56656f-a46d-40e1-be0a-75ed12ce879a",
                EuAcquisitionTestFixture.BuildRendererSource(415)),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(1, result.Relations!);
        Assert.AreEqual(Eli, result.Relations![0].Acquisition.Side!.NationalMeasureUri);
        Assert.AreEqual(4, result.ProductRequestCount);
    }

    [TestMethod]
    public void AVerifiedLuxembourgSectorSevenRowBecomesTheAcceptedNimSide()
    {
        var result = Decode(Row());

        Assert.IsTrue(result.Delivered);
        Assert.HasCount(1, result.Relations!);
        var relation = result.Relations![0];
        Assert.AreEqual(EuWork, relation.EuWorkUri);
        Assert.AreEqual(Nim, relation.NimWorkUri);
        Assert.AreEqual("72020L0001", relation.NimCelex);
        Assert.AreEqual(Eli, relation.LegiluxEli);
        Assert.AreEqual(EuTranspositionAssertedBy.Nim, relation.Acquisition.AssertedBy);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, relation.Acquisition.Acquisition);
        Assert.AreEqual(Eli, relation.Acquisition.Side!.NationalMeasureUri);
        Assert.AreEqual(EuMemberStateDisclaimer.Text, relation.Acquisition.Side.MemberStateDisclaimer);
        Assert.AreEqual(EuMemberStateDisclaimer.SourceUri, relation.Acquisition.Side.MemberStateDisclaimerSourceUri);
        Assert.AreEqual(Evidence, relation.Acquisition.CompletionEvidenceRef);
    }

    [TestMethod]
    public void ACompletedEmptyFamilyIsAProvenAbsenceForAnEuWork()
    {
        var result = Decode();

        Assert.IsTrue(result.Delivered);
        var acquisitions = result.ForEuWork(EuWork);
        Assert.HasCount(1, acquisitions);
        Assert.IsNull(acquisitions[0].Side);
        Assert.IsTrue(acquisitions[0].ProvesAbsence());
        Assert.AreEqual(Evidence, acquisitions[0].CompletionEvidenceRef);
        Assert.ThrowsExactly<ArgumentException>(() => result.ForEuWork("https://example.invalid/not-cellar"));
    }

    [TestMethod]
    public void PublisherIriAndUnboundEliShapesAreBothConsumedWithoutInventingAnEli()
    {
        var iri = Decode(Row(eliKind: "iri", eliIsIri: true));
        var unbound = Decode(Row(eli: null, eliKind: "unbound"));

        Assert.IsTrue(iri.Delivered, iri.Detail);
        Assert.AreEqual(Eli, iri.Relations![0].LegiluxEli);
        Assert.AreEqual(Eli, iri.Relations[0].Acquisition.Side!.NationalMeasureUri);

        Assert.IsTrue(unbound.Delivered, unbound.Detail);
        Assert.IsNull(unbound.Relations![0].LegiluxEli);
        Assert.AreEqual(Nim, unbound.Relations[0].Acquisition.Side!.NationalMeasureUri);
    }

    [TestMethod]
    public void ARowOutsideLuxembourgReachesTheExistingFailClosedBoundary()
    {
        var result = Decode(Row(country:
            "http://publications.europa.eu/resource/authority/country/BEL"));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "outside the admitted Luxembourg family");
        Assert.IsNull(result.Relations);
    }

    [TestMethod]
    public void ANationalMeasureOutsideSectorSevenReachesTheFailClosedBoundary()
    {
        var result = Decode(Row(nimCelex: "32020L0001"));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "sector-7 CELEX");
        Assert.IsNull(result.Relations);
    }

    [TestMethod]
    public void AnUnadmittedRelationPredicateIsNotSilentlyDiscarded()
    {
        var result = Decode(Row(predicate:
            "http://publications.europa.eu/ontology/cdm#measure_national_implementing_repeals_resource_legal"));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "two admitted publisher predicates");
        Assert.IsNull(result.Relations);
    }

    [TestMethod]
    public void ARowWhoseCursorDoesNotNameItsPublisherValuesIsRefused()
    {
        var result = Decode(Row(key5: "https://example.invalid/substituted"));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "key_5");
    }

    private static EuNationalImplementingMeasureProductionResult Decode(params RepeatedEnumerationRow[] rows) =>
        EuNationalImplementingMeasureProducer.DecodeRows(
            rows,
            EuNationalImplementingMeasureDiscoveryPlan.Create().CreateDeliveryProfile(),
            Evidence);

    private static RepeatedEnumerationRow Row(
        string country = EuNationalImplementingMeasureDiscoveryPlan.LuxembourgCountryIri,
        string predicate = EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri,
        string? key5 = null,
        string? eli = Eli,
        string eliKind = "literal",
        bool eliIsIri = false,
        string nimCelex = "72020L0001")
    {
        RepeatedEnumerationRdfTerm Plain(string value) =>
            RepeatedEnumerationRdfTerm.Literal(value, null, null);
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(Nim),
            RepeatedEnumerationRdfTerm.Iri(country),
            Plain(nimCelex),
            RepeatedEnumerationRdfTerm.Iri(predicate),
            RepeatedEnumerationRdfTerm.Iri(EuWork),
            eli is null
                ? RepeatedEnumerationRdfTerm.Unbound()
                : eliIsIri
                    ? RepeatedEnumerationRdfTerm.Iri(eli)
                    : RepeatedEnumerationRdfTerm.Literal(eli, null, null),
            Plain(eliKind),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            Plain(Nim),
            Plain(nimCelex),
            Plain(predicate),
            Plain(EuWork),
            Plain(key5 ?? eli ?? string.Empty),
        };
        var keys = terms[8..13];
        return new RepeatedEnumerationRow(terms, keys, keys);
    }

    private static string NimPageJson(IReadOnlyList<string> projection)
    {
        object Iri(string value) => new Dictionary<string, string>
        {
            ["type"] = "uri",
            ["value"] = value,
        };
        object Literal(string value, string? datatype = null)
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
            ["nim"] = Iri(Nim),
            ["country"] = Iri(EuNationalImplementingMeasureDiscoveryPlan.LuxembourgCountryIri),
            ["nim_celex"] = Literal("72020L0001"),
            ["implements_predicate"] = Iri(
                EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri),
            ["eu_work"] = Iri(EuWork),
            ["eli"] = Literal(Eli),
            ["eli_kind"] = Literal("literal"),
            ["multiplicity"] = Literal("1", XsdInteger),
            ["key_1"] = Literal(Nim),
            ["key_2"] = Literal("72020L0001"),
            ["key_3"] = Literal(EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri),
            ["key_4"] = Literal(EuWork),
            ["key_5"] = Literal(Eli),
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = projection },
            results = new { distinct = false, ordered = true, bindings = new[] { binding } },
        });
    }
}
