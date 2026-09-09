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

    private static RepeatedEnumerationRdfTerm Literal(
        string value, string? datatype = null, string? language = null) =>
        RepeatedEnumerationRdfTerm.Literal(value, datatype, language);

    private static RepeatedEnumerationRdfTerm Unbound() => RepeatedEnumerationRdfTerm.Unbound();

    private static RepeatedEnumerationRdfTerm Blank(string value) =>
        RepeatedEnumerationRdfTerm.BlankNode(value);

    /// <summary>
    /// One delivered row, coherent by construction unless a test asks for one contradiction.
    /// </summary>
    /// <remarks>
    /// Every marker, qualifier column and cursor key defaults to what the term itself says, so an
    /// honest row needs no arranging. The builder used to hard-code the markers and the three keys,
    /// which meant a test wanting a wrong kind had to remember to move the key too — and, more to
    /// the point, no row it built could ever carry two literals differing only in qualifier, which
    /// is the collision this family's keyset now exists to separate.
    /// </remarks>
    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm? document = null,
        string? documentKind = null,
        RepeatedEnumerationRdfTerm? date = null,
        string? dateKind = null,
        string opinion = Opinion,
        RepeatedEnumerationRdfTerm? documentKindTerm = null,
        RepeatedEnumerationRdfTerm? dateKindTerm = null,
        RepeatedEnumerationRdfTerm? multiplicity = null,
        string? documentDatatype = null,
        string? documentLanguage = null,
        string? dateDatatype = null,
        string? dateLanguage = null,
        RepeatedEnumerationRdfTerm? key1 = null,
        RepeatedEnumerationRdfTerm? key2 = null,
        RepeatedEnumerationRdfTerm? key3 = null,
        RepeatedEnumerationRdfTerm? key4 = null,
        RepeatedEnumerationRdfTerm? key5 = null,
        RepeatedEnumerationRdfTerm? key6 = null,
        RepeatedEnumerationRdfTerm? key7 = null,
        RepeatedEnumerationRdfTerm? key8 = null,
        RepeatedEnumerationRdfTerm? key9 = null,
        RepeatedEnumerationRdfTerm? key10 = null)
    {
        var opinionTerm = Iri(opinion);
        var documentTerm = document ?? Iri(Locator);
        var dateTerm = date ?? Literal(Date, XsdDate);
        var documentDatatypeColumn = documentDatatype ?? Qualifier(documentTerm, static t => t.Datatype);
        var documentLanguageColumn = documentLanguage ?? Qualifier(documentTerm, static t => t.Language);
        var dateDatatypeColumn = dateDatatype ?? Qualifier(dateTerm, static t => t.Datatype);
        var dateLanguageColumn = dateLanguage ?? Qualifier(dateTerm, static t => t.Language);

        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            opinionTerm, Literal(Marker(opinionTerm)),
            documentTerm, documentKindTerm ?? Literal(documentKind ?? Marker(documentTerm)),
            Literal(documentDatatypeColumn), Literal(documentLanguageColumn),
            dateTerm, dateKindTerm ?? Literal(dateKind ?? Marker(dateTerm)),
            Literal(dateDatatypeColumn), Literal(dateLanguageColumn),
            multiplicity ?? Literal("1", XsdInteger),
            key1 ?? Literal(opinion),
            key2 ?? Literal(Marker(opinionTerm)),
            key3 ?? Literal(documentTerm.Value ?? ""),
            key4 ?? Literal(Marker(documentTerm)),
            key5 ?? Literal(documentDatatypeColumn),
            key6 ?? Literal(documentLanguageColumn),
            key7 ?? Literal(dateTerm.Value ?? ""),
            key8 ?? Literal(Marker(dateTerm)),
            key9 ?? Literal(dateDatatypeColumn),
            key10 ?? Literal(dateLanguageColumn),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static string Marker(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => "iri",
        RepeatedEnumerationRdfTermKind.Literal => "literal",
        RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
        _ => LuxembourgOpinionDiscoveryPlan.UnboundKind,
    };

    private static string Qualifier(
        RepeatedEnumerationRdfTerm term, Func<RepeatedEnumerationRdfTerm, string?> select) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal ? select(term) ?? string.Empty : string.Empty;

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

    /// <summary>
    /// A delivered term of the wrong RDF kind is malformed, however plausible its lexical value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found in review on head <c>38d83056</c>. Agreeing with a marker established only WHICH kind
    /// arrived; it did not establish that the kind is one this contract can read. The producer then
    /// passed <c>document.Value</c> to a string-only door, so a <b>literal</b> whose lexical value
    /// happened to be an admitted HTTPS URL became an admitted link-only record with
    /// <c>Refusal.None</c> — the publisher's own term authority discarded at the last step.
    /// </para>
    /// <para>
    /// A locator is an IRI and a date is a literal. Any other bound kind is a delivery this plan
    /// cannot have produced, so it is malformed rather than merely unrepresentable, and it refuses
    /// the production rather than being filed as an ordinary excluded fact.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ADeliveredTermOfTheWrongRdfKindIsMalformedHoweverPlausibleItsValue()
    {
        // The exact shape from the review: a literal that reads like the admitted locator.
        var literalLocator = Decode(Row(document: Literal(Locator), documentKind: "literal"));
        Assert.AreEqual(
            LuxembourgOpinionProductionRefusal.RowNotAdmitted, literalLocator.Refusal,
            "a literal is not a locator, however much its lexical value looks like one.");
        StringAssert.Contains(literalLocator.Detail!, "publisher IRI");

        var blankLocator = Decode(
            Row(document: Blank("b0"), documentKind: "unsupported_blank_node"));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, blankLocator.Refusal);

        var iriDate = Decode(Row(date: Iri("http://example.com/2026-07-17"), dateKind: "iri"));
        Assert.AreEqual(
            LuxembourgOpinionProductionRefusal.RowNotAdmitted, iriDate.Refusal,
            "a date that is not a literal cannot carry a lexical value at a precision.");
        StringAssert.Contains(iriDate.Detail!, "publisher literal");

        var blankDate = Decode(Row(date: Blank("b1"), dateKind: "unsupported_blank_node"));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, blankDate.Refusal);
    }

    /// <summary>
    /// A kind marker is read as a term, so a marker that is not the query's plain literal refuses.
    /// </summary>
    /// <remarks>
    /// The plan binds both markers with a <c>BIND</c> over string constants, so each always arrives
    /// as an unqualified plain literal. Comparing only <c>marker.Value</c> admitted an IRI-valued
    /// marker whose lexical value happened to read <c>iri</c> — a term that did not come from this
    /// query at all.
    /// </remarks>
    [TestMethod]
    public void AMarkerThatIsNotTheQuerysOwnPlainLiteralRefusesTheRow()
    {
        var iriMarker = Decode(Row(documentKindTerm: Iri("iri")));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, iriMarker.Refusal);

        var typedMarker = Decode(Row(dateKindTerm: Literal("literal", XsdDate)));
        Assert.AreEqual(
            LuxembourgOpinionProductionRefusal.RowNotAdmitted, typedMarker.Refusal,
            "a datatyped marker is not the unqualified plain literal the BIND produces.");
    }

    /// <summary>
    /// The publisher-computed count and the three cursor keys are read, not ignored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plan groups on <c>multiplicity</c> and orders and paginates on <c>key_1..key_3</c>. A
    /// decoder that never reads them lets the verified page prove one tuple while the producer emits
    /// another — the page's own proof and the record would then describe different rows.
    /// </para>
    /// <para>
    /// The empty key for an unbound term is asserted rather than skipped, because "" is the value
    /// the plan's own <c>COALESCE</c> contributes and a decoder that ignored it would accept any
    /// key for a record-less opinion.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Two dates sharing a lexical value and differing in datatype are keyed apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE COLLISION THIS KEYSET WAS REPAIRED FOR. The document and the date used to be keyed by
    /// <c>STR()</c> alone, so these two rows were distinct grouped rows sharing every canonical key.
    /// Source/Core requires canonical keys unique and cursors strictly increasing, so such a pair
    /// either refuses the whole page or cannot be paged across a boundary — and this family retains
    /// the date's datatype on its record, so they really are different facts.
    /// </para>
    /// <para>
    /// It is the same defect found twice in review on the procedure-event plan, and it was present
    /// here at the same time. Nothing observed it because the keys were internally consistent and no
    /// fixture row ever carried two literals differing only in a qualifier.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TwoDatesSharingALexicalValueAndDifferingInDatatypeAreKeptApart()
    {
        const string XsdGYearMonth = "http://www.w3.org/2001/XMLSchema#gYearMonth";

        var profile = Profile();
        var dated = Row(date: Literal(Date, XsdDate));
        var otherDatatype = Row(date: Literal(Date, XsdGYearMonth));

        string KeyOf(RepeatedEnumerationRow row, string key) =>
            row.Terms[profile.ProjectionVariables.ToList().IndexOf(key)].Value ?? string.Empty;

        Assert.AreEqual(
            KeyOf(dated, "key_7"), KeyOf(otherDatatype, "key_7"),
            "the lexical key cannot tell them apart, which is why it alone was not enough.");
        Assert.AreNotEqual(
            KeyOf(dated, "key_9"), KeyOf(otherDatatype, "key_9"),
            "the datatype key does, and that is the whole repair.");

        // Both are readable rows: the keyset separates them rather than refusing either.
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.None, Decode(dated).Refusal);
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.None, Decode(otherDatatype).Refusal);
    }

    /// <summary>
    /// Corrupting any one of the ten cursor keys refuses the row and names that key.
    /// </summary>
    /// <remarks>
    /// A sweep driven off the live profile rather than ten near-identical tests, so a key added
    /// later is covered the day it appears. The honest value of several of these is a marker or an
    /// empty qualifier, and a producer ignoring one would agree with a fixture that also left it
    /// empty — two mutations survived the procedure-event family on exactly that.
    /// </remarks>
    [TestMethod]
    public void CorruptingAnySingleCursorKeyRefusesTheRowAndNamesIt()
    {
        var profile = Profile();
        var keys = profile.CursorVariables;

        Assert.HasCount(10, keys, "this family keys ten positions since the qualifier repair.");

        foreach (var key in keys)
        {
            var ordinal = profile.ProjectionVariables.ToList().IndexOf(key);
            Assert.IsGreaterThan(-1, ordinal, $"{key} is projected.");

            // The ordinary honest row. A delivered document must be an IRI here — that is the
            // contract's own rule, and a literal one is refused before any key is read — so several
            // qualifier keys are legitimately empty. That costs this sweep nothing, because the
            // corruption APPENDS rather than blanks: an empty key becomes non-empty and a producer
            // ignoring it still fails.
            var terms = Row(date: Literal(Date, XsdDate)).Terms.ToList();
            terms[ordinal] = Literal((terms[ordinal].Value ?? string.Empty) + "-not-delivered");

            var result = LuxembourgOpinionProducer.DecodeRows(
                [new RepeatedEnumerationRow(terms, terms, terms)], profile, Evidence);

            Assert.AreEqual(
                LuxembourgOpinionProductionRefusal.RowNotAdmitted, result.Refusal,
                $"{key} was corrupted and the row was still admitted, so that key keys nothing.");
            StringAssert.Contains(
                result.Detail!, key,
                $"the refusal must name {key} rather than another key that happened to differ.");
        }
    }

    [TestMethod]
    public void TheGroupedCountAndTheCursorKeysAreReadRatherThanIgnored()
    {
        var zeroCount = Decode(Row(multiplicity: Literal("0", XsdInteger)));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, zeroCount.Refusal);
        StringAssert.Contains(zeroCount.Detail!, "multiplicity");

        var untypedCount = Decode(Row(multiplicity: Literal("1")));
        Assert.AreEqual(
            LuxembourgOpinionProductionRefusal.RowNotAdmitted, untypedCount.Refusal,
            "the grouped count arrives as a typed xsd:integer, not a plain literal.");

        var wrongOpinionKey = Decode(Row(
            key1: Literal("http://data.legilux.public.lu/resource/opinion/99999")));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, wrongOpinionKey.Refusal);
        StringAssert.Contains(wrongOpinionKey.Detail!, "key_1");

        var wrongDocumentKey = Decode(Row(key2: Literal("https://example.com/other.pdf")));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, wrongDocumentKey.Refusal);
        StringAssert.Contains(wrongDocumentKey.Detail!, "key_2");

        var wrongDateKey = Decode(Row(key3: Literal("2020-01-01")));
        Assert.AreEqual(LuxembourgOpinionProductionRefusal.RowNotAdmitted, wrongDateKey.Refusal);
        StringAssert.Contains(wrongDateKey.Detail!, "key_3");

        // An unbound term contributes "" to its key. A non-empty key there is a contradiction.
        var absentDocumentNonEmptyKey = Decode(Row(
            document: Unbound(),
            documentKind: LuxembourgOpinionDiscoveryPlan.UnboundKind,
            key2: Literal(Locator)));
        Assert.AreEqual(
            LuxembourgOpinionProductionRefusal.RowNotAdmitted, absentDocumentNonEmptyKey.Refusal,
            "an absent document cannot carry a non-empty key_2.");
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
            ["opinion_kind"] = LiteralTerm("iri"),
            ["document"] = IriTerm(Locator),
            ["document_kind"] = LiteralTerm("iri"),
            ["document_datatype"] = LiteralTerm(""),
            ["document_language"] = LiteralTerm(""),
            ["opinion_date"] = LiteralTerm(Date, XsdDate),
            ["date_kind"] = LiteralTerm("literal"),
            ["date_datatype"] = LiteralTerm(XsdDate),
            ["date_language"] = LiteralTerm(""),
            ["multiplicity"] = LiteralTerm("1", XsdInteger),
            ["key_1"] = LiteralTerm(Opinion),
            ["key_2"] = LiteralTerm("iri"),
            ["key_3"] = LiteralTerm(Locator),
            ["key_4"] = LiteralTerm("iri"),
            ["key_5"] = LiteralTerm(""),
            ["key_6"] = LiteralTerm(""),
            ["key_7"] = LiteralTerm(Date),
            ["key_8"] = LiteralTerm("literal"),
            ["key_9"] = LiteralTerm(XsdDate),
            ["key_10"] = LiteralTerm(""),
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = projection },
            results = new { distinct = false, ordered = true, bindings = new[] { binding } },
        });
    }
}
