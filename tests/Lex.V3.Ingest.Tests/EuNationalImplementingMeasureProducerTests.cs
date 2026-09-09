using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
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
    private const string EuEli = "http://data.europa.eu/eli/dir/2020/1/oj";
    private const string DelegatedDirectiveEli = "http://data.europa.eu/eli/dir_del/2020/1/oj";
    private const string ImplementingDirectiveEli = "http://data.europa.eu/eli/dir_impl/2020/1/oj";
    private const string DecisionEli = "http://data.europa.eu/eli/dec/2020/1/oj";
    private const string FrameworkDecisionEli = "http://data.europa.eu/eli/dec_framw/2020/1/oj";
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
        Assert.IsTrue(result.ForEuWork(EuWork).ProvesAbsence());
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
        Assert.AreEqual(Eli, result.Relations![0].Acquisition.Sides[0]!.NationalMeasureUri);
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
        Assert.AreEqual(EuWorkKind.Directive, relation.WorkKindAssertion.Kind);
        Assert.AreEqual(EuWork, relation.WorkKindAssertion.Work.Value(FactsIdentifierFamily.CellarWorkUri));
        Assert.AreEqual(EuEli, relation.WorkKindAssertion.Work.Value(FactsIdentifierFamily.Eli));
        Assert.AreEqual(Nim, relation.NimWorkUri);
        Assert.AreEqual("72020L0001", relation.NimCelex);
        Assert.AreEqual(Eli, relation.LegiluxEli);
        Assert.AreEqual(EuTranspositionAssertedBy.Nim, relation.Acquisition.AssertedBy);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, relation.Acquisition.Acquisition);
        Assert.AreEqual(Eli, relation.Acquisition.Sides[0]!.NationalMeasureUri);
        Assert.AreEqual(EuMemberStateDisclaimer.Text, relation.Acquisition.Sides[0].MemberStateDisclaimer);
        Assert.AreEqual(EuMemberStateDisclaimer.SourceUri, relation.Acquisition.Sides[0].MemberStateDisclaimerSourceUri);
        Assert.AreEqual(Evidence, relation.Acquisition.CompletionEvidenceRef);
    }

    [TestMethod]
    public void ACompletedEmptyFamilyIsAProvenAbsenceForAnEuWork()
    {
        var result = Decode();

        Assert.IsTrue(result.Delivered);
        var acquisition = result.ForEuWork(EuWork);
        Assert.IsEmpty(acquisition.Sides);
        Assert.IsTrue(acquisition.ProvesAbsence());
        Assert.AreEqual(Evidence, acquisition.CompletionEvidenceRef);
        Assert.ThrowsExactly<ArgumentException>(() => result.ForEuWork("https://example.invalid/not-cellar"));
    }

    [TestMethod]
    public void PublisherIriAndUnboundEliShapesAreBothConsumedWithoutInventingAnEli()
    {
        var iri = Decode(Row(eliKind: "iri", eliIsIri: true));
        var unbound = Decode(Row(eli: null, eliKind: "unbound"));

        Assert.IsTrue(iri.Delivered, iri.Detail);
        Assert.AreEqual(Eli, iri.Relations![0].LegiluxEli);
        Assert.AreEqual(Eli, iri.Relations[0].Acquisition.Sides[0]!.NationalMeasureUri);

        Assert.IsTrue(unbound.Delivered, unbound.Detail);
        Assert.IsNull(unbound.Relations![0].LegiluxEli);
        Assert.AreEqual(Nim, unbound.Relations[0].Acquisition.Sides[0]!.NationalMeasureUri);
    }

    [TestMethod]
    public void AnUnboundMarkerCannotDiscardABoundPublisherEli()
    {
        var result = Decode(Row(eliKind: "unbound", key7: string.Empty));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "eli must be an absolute publisher URI term or explicitly unbound");
    }

    [TestMethod]
    public void ARefusedRunCannotBeReadAsACompletedEmptyNimSet()
    {
        var result = Decode(Row(country:
            "http://publications.europa.eu/resource/authority/country/BEL"));

        Assert.ThrowsExactly<InvalidOperationException>(() => result.ForEuWork(EuWork));
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
        var wrongEli = Decode(Row(key5: "https://example.invalid/substituted"));
        var wrongKind = Decode(Row(key6: EuNationalImplementingMeasureDiscoveryPlan.RegulationResourceTypeIri));
        var wrongMeasure = Decode(Row(key7: "https://example.invalid/substituted"));

        Assert.IsFalse(wrongEli.Delivered);
        StringAssert.Contains(wrongEli.Detail, "key_5");
        Assert.IsFalse(wrongKind.Delivered);
        StringAssert.Contains(wrongKind.Detail, "key_6");
        Assert.IsFalse(wrongMeasure.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, wrongMeasure.Refusal);
        StringAssert.Contains(wrongMeasure.Detail, "key_7");
    }

    [TestMethod]
    public void APageKeyThatDoesNotEncodeTheSevenPublisherKeysIsRefused()
    {
        var result = Decode(Row(pageKey: "substituted-page-key"));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "page_key");
    }

    [TestMethod]
    public void TheTargetWorkKindAndEliMustBeExactPublisherEvidence()
    {
        var wrongKind = Decode(Row(
            euWorkKind: "http://publications.europa.eu/resource/authority/resource-type/DEC",
            key6: EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri));
        var wrongEli = Decode(Row(euWorkEli: "https://example.invalid/eli/dir/2020/1/oj"));
        var missingKind = Decode(Row(euWorkKind: null));
        var missingEli = Decode(Row(euWorkEli: null));

        Assert.IsFalse(wrongKind.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, wrongKind.Refusal);
        StringAssert.Contains(wrongKind.Detail, "eu_work_kind");
        StringAssert.Contains(
            wrongKind.Detail,
            "http://publications.europa.eu/resource/authority/resource-type/DEC",
            "The typed refusal must retain the exact unadmitted publisher value for diagnosis.");
        Assert.IsFalse(wrongEli.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, wrongEli.Refusal);
        StringAssert.Contains(wrongEli.Detail, "eu_work_eli");
        Assert.IsFalse(missingKind.Delivered);
        StringAssert.Contains(missingKind.Detail, "eu_work_kind");
        Assert.IsFalse(missingEli.Delivered);
        StringAssert.Contains(missingEli.Detail, "eu_work_eli");

        var regulation = Decode(Row(
            euWorkEli: "http://data.europa.eu/eli/reg/2020/1/oj",
            euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.RegulationResourceTypeIri));
        Assert.IsTrue(regulation.Delivered, regulation.Detail);
        Assert.AreEqual(EuWorkKind.Regulation, regulation.Relations![0].WorkKindAssertion.Kind);
    }

    [TestMethod]
    public void AuthorityNamedDirectiveSubtypesMapToDirectiveAndRetainTheirRawType()
    {
        var delegated = Decode(Row(
            euWorkEli: DelegatedDirectiveEli,
            euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.DelegatedDirectiveResourceTypeIri));
        var implementing = Decode(Row(
            euWorkEli: ImplementingDirectiveEli,
            euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.ImplementingDirectiveResourceTypeIri));

        Assert.IsTrue(delegated.Delivered, delegated.Detail);
        var delegatedRelation = delegated.Relations!.Single();
        Assert.AreEqual(EuWorkKind.Directive, delegatedRelation.WorkKindAssertion.Kind);
        Assert.AreEqual(
            EuNationalImplementingMeasureDiscoveryPlan.DelegatedDirectiveResourceTypeIri,
            delegatedRelation.PublisherWorkTypeIri);
        Assert.IsEmpty(delegated.OutOfE5WorkKindExclusions!);

        Assert.IsTrue(implementing.Delivered, implementing.Detail);
        var implementingRelation = implementing.Relations!.Single();
        Assert.AreEqual(EuWorkKind.Directive, implementingRelation.WorkKindAssertion.Kind);
        Assert.AreEqual(
            EuNationalImplementingMeasureDiscoveryPlan.ImplementingDirectiveResourceTypeIri,
            implementingRelation.PublisherWorkTypeIri);
        Assert.IsEmpty(implementing.OutOfE5WorkKindExclusions!);
    }

    [TestMethod]
    public void DecisionsAreEvidenceBoundOutOfE5WorkKindExclusionsRatherThanAbsences()
    {
        var decision = Decode(Row(
            euWorkEli: DecisionEli,
            euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.DecisionResourceTypeIri));
        var framework = Decode(Row(
            euWorkEli: FrameworkDecisionEli,
            euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.FrameworkDecisionResourceTypeIri));

        Assert.IsTrue(decision.Delivered, decision.Detail);
        Assert.IsEmpty(decision.Relations!);
        var excluded = decision.OutOfE5WorkKindExclusions!.Single();
        Assert.AreEqual("out_of_e5_work_kind", excluded.Disposition);
        Assert.AreEqual(DecisionEli, excluded.EuWorkEli);
        Assert.AreEqual(EuNationalImplementingMeasureDiscoveryPlan.DecisionResourceTypeIri,
            excluded.PublisherWorkTypeIri);
        Assert.AreEqual(Evidence, excluded.EvidenceRef);
        Assert.ThrowsExactly<InvalidOperationException>(() => decision.ForEuWork(EuWork));

        Assert.IsTrue(framework.Delivered, framework.Detail);
        Assert.AreEqual(EuNationalImplementingMeasureDiscoveryPlan.FrameworkDecisionResourceTypeIri,
            framework.OutOfE5WorkKindExclusions!.Single().PublisherWorkTypeIri);
    }

    [TestMethod]
    public void RawPublisherTypeAndEliFamilyMustAgreeBeforeMappingOrExclusion()
    {
        var result = Decode(Row(
            euWorkEli: DecisionEli,
            euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.DelegatedDirectiveResourceTypeIri));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, "publisher eu_work_kind and ELI family disagree");
    }

    [TestMethod]
    public void AnExistingDirectiveEliContradictionRemainsObservableForReconciliation()
    {
        var result = Decode(Row(
            euWorkEli: "http://data.europa.eu/eli/reg/2021/1187/oj",
            euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri));

        Assert.IsTrue(result.Delivered, result.Detail);
        var relation = result.Relations!.Single();
        Assert.AreEqual(EuWorkKind.Directive, relation.WorkKindAssertion.Kind);
        Assert.AreEqual(
            EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
            relation.PublisherWorkTypeIri);
    }

    [TestMethod]
    public void AnUnruledPublisherTypeStillReachesTheTypedRefusal()
    {
        const string Other =
            "http://publications.europa.eu/resource/authority/resource-type/OTHER";
        var result = Decode(Row(euWorkKind: Other));

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual(EuNationalImplementingMeasureProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail, Other);
        Assert.IsNull(result.Relations);
        Assert.IsNull(result.OutOfE5WorkKindExclusions);
    }

    [TestMethod]
    public void EveryVerifiedRowEntersExactlyOneRuledPartition()
    {
        var result = Decode(
            Row(),
            Row(
                euWorkEli: DecisionEli,
                euWorkKind: EuNationalImplementingMeasureDiscoveryPlan.DecisionResourceTypeIri));

        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.HasCount(1, result.Relations!);
        Assert.HasCount(1, result.OutOfE5WorkKindExclusions!);
    }

    private static EuNationalImplementingMeasureProductionResult Decode(params RepeatedEnumerationRow[] rows) =>
        EuNationalImplementingMeasureProducer.DecodeRows(
            rows,
            EuNationalImplementingMeasureDiscoveryPlan.Create().CreateDeliveryProfile(),
            Evidence);

    private static RepeatedEnumerationRow Row(
        string country = EuNationalImplementingMeasureDiscoveryPlan.LuxembourgCountryIri,
        string predicate = EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri,
        string? key7 = null,
        string? key5 = null,
        string? key6 = null,
        string? pageKey = null,
        string? eli = Eli,
        string eliKind = "literal",
        bool eliIsIri = false,
        string nimCelex = "72020L0001",
        string? euWorkEli = EuEli,
        string? euWorkKind = EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri)
    {
        RepeatedEnumerationRdfTerm Plain(string value) =>
            RepeatedEnumerationRdfTerm.Literal(value, null, null);
        var cursor5 = key5 ?? euWorkEli ?? string.Empty;
        var cursor6 = key6 ?? euWorkKind ?? string.Empty;
        var cursor7 = key7 ?? eli ?? string.Empty;
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(Nim),
            RepeatedEnumerationRdfTerm.Iri(country),
            Plain(nimCelex),
            RepeatedEnumerationRdfTerm.Iri(predicate),
            RepeatedEnumerationRdfTerm.Iri(EuWork),
            euWorkEli is null
                ? RepeatedEnumerationRdfTerm.Unbound()
                : RepeatedEnumerationRdfTerm.Literal(
                    euWorkEli, "http://www.w3.org/2001/XMLSchema#anyURI", null),
            euWorkKind is null
                ? RepeatedEnumerationRdfTerm.Unbound()
                : RepeatedEnumerationRdfTerm.Iri(euWorkKind),
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
            Plain(cursor5),
            Plain(cursor6),
            Plain(cursor7),
            Plain(pageKey ?? PageKey(Nim, nimCelex, predicate, EuWork, cursor5, cursor6, cursor7)),
        };
        var keys = terms[17..18];
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
            ["eu_work_eli"] = Literal(EuEli, "http://www.w3.org/2001/XMLSchema#anyURI"),
            ["eu_work_kind"] = Iri(EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri),
            ["eli"] = Literal(Eli),
            ["eli_kind"] = Literal("literal"),
            ["multiplicity"] = Literal("1", XsdInteger),
            ["key_1"] = Literal(Nim),
            ["key_2"] = Literal("72020L0001"),
            ["key_3"] = Literal(EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri),
            ["key_4"] = Literal(EuWork),
            ["key_5"] = Literal(EuEli),
            ["key_6"] = Literal(EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri),
            ["key_7"] = Literal(Eli),
            ["page_key"] = Literal(PageKey(
                Nim,
                "72020L0001",
                EuNationalImplementingMeasureDiscoveryPlan.ImplementsResourceLegalPredicateIri,
                EuWork,
                EuEli,
                EuNationalImplementingMeasureDiscoveryPlan.DirectiveResourceTypeIri,
                Eli)),
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = projection },
            results = new { distinct = false, ordered = true, bindings = new[] { binding } },
        });
    }

    private static string PageKey(params string[] parts) =>
        string.Join('|', parts.Select(Uri.EscapeDataString));
}
