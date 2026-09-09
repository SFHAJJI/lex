using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E8's live half, slice two: the executor entry point and the producer that turn
/// delivered Conseil d'État opinion rows into link-only records.
/// </summary>
/// <remarks>
/// The admit-path guard is written first here, deliberately. The EU case-law family reached
/// integration able to refuse correctly and unable ever to succeed — every end-to-end guard it had
/// drove a refusal, so a parameter-ordering defect that made every honest delivery refuse was
/// invisible until review. A family whose tests only ask it to say no has not been tested.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionProducerTests
{
    private const string Opinion = "http://data.legilux.public.lu/resource/opinion/62629";
    private const string Locator =
        "https://conseil-etat.public.lu/content/dam/conseil_etat/fr/avis/2026/17072026/62629-avis.pdf";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string Date = "2026-07-17";

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:8c14f7b2-3d69-4e50-a7c1-25b0e9d34f68", new string('c', 64));

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        LuxembourgOpinionDiscoveryPlan.Create().CreateDeliveryProfile();

    private static RepeatedEnumerationRdfTerm Iri(string value) =>
        RepeatedEnumerationRdfTerm.Iri(value);

    private static RepeatedEnumerationRdfTerm Literal(string value, string? datatype = null) =>
        RepeatedEnumerationRdfTerm.Literal(value, datatype, null);

    private static RepeatedEnumerationRdfTerm Unbound() => RepeatedEnumerationRdfTerm.Unbound();

    /// <summary>One delivered row, with every term and every marker chosen independently.</summary>
    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm? document = null,
        string documentKind = "iri",
        RepeatedEnumerationRdfTerm? date = null,
        string dateKind = "literal",
        string opinion = Opinion)
    {
        var documentTerm = document ?? Iri(Locator);
        var dateTerm = date ?? Literal(Date, XsdDate);
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            Iri(opinion),
            documentTerm, Literal(documentKind),
            dateTerm, Literal(dateKind),
            Literal("1", XsdInteger),
            Literal(opinion), Literal(documentTerm.Value ?? ""), Literal(dateTerm.Value ?? ""),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static LuxembourgOpinionProductionResult Decode(params RepeatedEnumerationRow[] rows) =>
        LuxembourgOpinionProducer.DecodeRows(rows, Profile(), Evidence);

    /// <summary>A delivered opinion carrying a locator and a date becomes a link-only record.</summary>
    [TestMethod]
    public void ADeliveredOpinionWithBothHalvesBecomesALinkOnlyRecord()
    {
        var result = Decode(Row());

        Assert.AreEqual(LuxembourgOpinionProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.AdmittedRecords());
        Assert.IsEmpty(result.ExcludedEvents());

        var record = result.AdmittedRecords()[0];
        Assert.AreEqual(Opinion, record.OpinionIri);
        Assert.AreEqual(Locator, record.DocumentLocator);
        Assert.AreEqual(Date, record.RawOpinionDateLexical);
        Assert.AreEqual("conseil-etat.public.lu", record.DocumentHost);
        Assert.AreEqual(
            Evidence.ResourceId, record.SourceObservationId,
            "the record cites the coordinate its own terms were read from.");
        Assert.AreEqual(LuScopeTerminalState.Point, LuxembourgOpinionLinkOnlyRecord.Disposition);
    }

    /// <summary>
    /// An opinion the publisher holds no document for is kept and typed, never dropped.
    /// </summary>
    /// <remarks>
    /// This is the majority shape: 6,393 of 13,009 events carry a resulting document. Dropping them
    /// would report a corpus half its real size while looking complete, and refusing the page over
    /// them would return nothing at all. The exclusion says which half was missing, and carries no
    /// contract refusal, because the record's door was never reached.
    /// </remarks>
    [TestMethod]
    public void AnOpinionWithNoDocumentIsKeptAsATypedExclusionBesideTheRecordsThatHaveOne()
    {
        var result = Decode(
            Row(),
            Row(document: Unbound(), documentKind: LuxembourgOpinionDiscoveryPlan.UnboundKind,
                opinion: "http://data.legilux.public.lu/resource/opinion/70001"));

        Assert.AreEqual(LuxembourgOpinionProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.AdmittedRecords());

        var excluded = result.ExcludedEvents();
        Assert.HasCount(1, excluded);
        Assert.AreEqual("http://data.legilux.public.lu/resource/opinion/70001", excluded[0].OpinionIri);
        Assert.IsFalse(excluded[0].DocumentDelivered);
        Assert.IsTrue(excluded[0].DateDelivered);
        Assert.AreEqual(
            LuxembourgOpinionLocatorRefusal.None, excluded[0].Refusal,
            "nothing was refused: the publisher simply holds no document.");
    }

    /// <summary>An opinion with no date is kept too, and says so.</summary>
    [TestMethod]
    public void AnOpinionWithNoDateIsKeptAndNamesTheMissingHalf()
    {
        var result = Decode(
            Row(date: Unbound(), dateKind: LuxembourgOpinionDiscoveryPlan.UnboundKind));

        Assert.AreEqual(LuxembourgOpinionProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsEmpty(result.AdmittedRecords());
        Assert.HasCount(1, result.ExcludedEvents());
        Assert.IsTrue(result.ExcludedEvents()[0].DocumentDelivered);
        Assert.IsFalse(result.ExcludedEvents()[0].DateDelivered);
    }

    /// <summary>
    /// A locator on an origin the robots evidence does not cover is excluded with the contract's
    /// own reason, not silently and not by refusing the page.
    /// </summary>
    /// <remarks>
    /// The distinction this keeps is between "the publisher holds no document" and "a document
    /// exists and this product may not carry a link to it". Both leave the opinion without a record
    /// and they are different facts, so the exclusion carries the contract's typed refusal for the
    /// second and <see cref="LuxembourgOpinionLocatorRefusal.None"/> for the first.
    /// </remarks>
    [TestMethod]
    public void ALocatorOutsideTheAdmittedOriginsIsExcludedWithTheContractsOwnReason()
    {
        var result = Decode(Row(document: Iri("https://example.com/opinion.pdf")));

        Assert.AreEqual(LuxembourgOpinionProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsEmpty(result.AdmittedRecords());
        Assert.HasCount(1, result.ExcludedEvents());
        Assert.AreEqual(
            LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotAnAdmittedOfficialFamily,
            result.ExcludedEvents()[0].Refusal);
        Assert.IsTrue(
            result.ExcludedEvents()[0].DocumentDelivered,
            "the publisher did deliver a document; this product may not link to it.");
    }

    /// <summary>
    /// A kind marker naming the wrong term kind refuses the row, even when both agree something
    /// was delivered.
    /// </summary>
    /// <remarks>
    /// The markers take four values. An agreement check that asked only "does the marker say
    /// unbound" would let an IRI carrying a <c>literal</c> marker agree with itself, which is the
    /// collapse this seat shipped on the E6 producer and had found by attacking its own head. Both
    /// markers are checked, because a guard that covered only one would leave the other's whole
    /// value space unobserved.
    /// </remarks>
    [TestMethod]
    public void AMarkerNamingTheWrongTermKindRefusesTheRow()
    {
        var documentMarker = Decode(Row(documentKind: "literal"));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, documentMarker.Refusal);
        StringAssert.Contains(documentMarker.Detail!, "document");

        var dateMarker = Decode(Row(dateKind: "iri"));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, dateMarker.Refusal);
        StringAssert.Contains(dateMarker.Detail!, "opinion_date");

        // An absent term whose marker claims something arrived, and the reverse.
        var absentTermBoundMarker = Decode(Row(document: Unbound(), documentKind: "iri"));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, absentTermBoundMarker.Refusal);

        var boundTermAbsentMarker = Decode(
            Row(documentKind: LuxembourgOpinionDiscoveryPlan.UnboundKind));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, boundTermAbsentMarker.Refusal);
    }

    /// <summary>One malformed row refuses the whole production; the good rows are not kept.</summary>
    /// <remarks>
    /// The opposite decision from the exclusions above, and the difference is whether the delivery
    /// can be believed at all. A marker contradicting its term means it cannot, so admitting the
    /// rest would hand back a set that looks complete and is not.
    /// </remarks>
    [TestMethod]
    public void OneMalformedRowRefusesTheWholeProductionRatherThanFilteringIt()
    {
        var result = Decode(Row(), Row(dateKind: "iri"), Row());

        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, result.Refusal);
        Assert.IsNull(result.Records, "two good rows must not be delivered as though they were all of them.");
    }

    /// <summary>A refused production cannot be read as a run that found nothing.</summary>
    /// <remarks>
    /// Both readers throw, because a caller that fell back to the exclusion list on a refused run
    /// would be reading the same false emptiness by a different route.
    /// </remarks>
    [TestMethod]
    public void ARefusedProductionCannotBeReadAsAnEmptyResult()
    {
        var refused = Decode(Row(dateKind: "iri"));

        Assert.ThrowsExactly<InvalidOperationException>(() => refused.AdmittedRecords());
        Assert.ThrowsExactly<InvalidOperationException>(() => refused.ExcludedEvents());
    }

    /// <summary>
    /// The family runs end to end and produces a record, not merely a refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the admit path, and it is the guard the EU case-law family did not have. That family
    /// bound <c>pass_id</c> ahead of its selection while the delivery proof compares the ordered
    /// roles as selection-then-pass, so every honest delivery reached <c>DeliveryProofRefused</c>.
    /// It could refuse correctly and could never succeed, and nothing observed it until review,
    /// because every end-to-end guard it had drove a refusal.
    /// </para>
    /// <para>
    /// Asserting <c>Delivered</c> and a real record — rather than that one particular refusal did
    /// not occur — is what makes this observe the whole path: the plan's rendered query, the
    /// executor's two passes, the delivery proof, the reopened page evidence and the decode.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheFamilyRunsEndToEndAndProducesARecord()
    {
        var plan = LuxembourgOpinionDiscoveryPlan.Create();
        var page = PageJson(plan.CreateDeliveryProfile().ProjectionVariables);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
            {
                1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(1),
                2 or 4 => page,
                _ => throw new AssertFailedException("No request is admitted after both passes complete."),
            }));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgOpinionProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var request = new LuxembourgOpinionRunRequest(
            plan, "urn:uuid:5d2b9e14-6f37-4a80-b1c5-83e0da476f29",
            LuxembourgAcquisitionTestFixture.BuildRendererSource(8801));

        var result = await producer.RunAsync(request, LuxembourgSourceWitness(), CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.HasCount(1, result.AdmittedRecords());
        Assert.AreEqual(Locator, result.AdmittedRecords()[0].DocumentLocator);
        Assert.AreEqual(4, result.ProductRequestCount);
        Assert.IsNotNull(result.CompletionEvidenceRef);
        Assert.AreEqual(
            result.CompletionEvidenceRef!.ResourceId,
            result.AdmittedRecords()[0].SourceObservationId,
            "the record cites the run's own completion coordinate, not a fixture constant.");
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(8802);
        return plan.BindCount(
            planResourceId,
            "urn:uuid:6e3ca025-7048-4b91-c2d6-94f1eb587a3a",
            "urn:uuid:7f4db136-8159-4ca2-d3e7-a502fc698b4b",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(8802)).Request;
    }

    private static string PageJson(IReadOnlyList<string> projection)
    {
        static object IriTerm(string value) => new Dictionary<string, string>
        {
            ["type"] = "uri",
            ["value"] = value,
        };
        static object LiteralTerm(string value, string? datatype = null)
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
            ["opinion"] = IriTerm(Opinion),
            ["document"] = IriTerm(Locator),
            ["document_kind"] = LiteralTerm("iri"),
            ["opinion_date"] = LiteralTerm(Date, XsdDate),
            ["date_kind"] = LiteralTerm("literal"),
            ["multiplicity"] = LiteralTerm("1", XsdInteger),
            ["key_1"] = LiteralTerm(Opinion),
            ["key_2"] = LiteralTerm(Locator),
            ["key_3"] = LiteralTerm(Date),
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = projection },
            results = new { distinct = false, ordered = true, bindings = new[] { binding } },
        });
    }
}
