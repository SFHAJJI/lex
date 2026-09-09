using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E8, live half for procedure events, slice three: delivered rows becoming
/// observations.
/// </summary>
/// <remarks>
/// The plan asks the question, the executor runs it, and this reads the answer. It is the only
/// place in this family where several delivered rows become one fact, because the plan refuses to
/// concatenate the declared types so that each reaches this decoder as its own term.
/// </remarks>
[TestClass]
public sealed class EuProcedureEventProducerTests
{
    private const string Dossier = "http://publications.europa.eu/resource/cellar/1f7ba2c8-4d59-11ec-91ac-01aa75ed71a1";
    private const string Event = "http://publications.europa.eu/resource/cellar/a3d90b16-4d59-11ec-91ac-01aa75ed71a1";
    private const string OtherEvent = "http://publications.europa.eu/resource/cellar/b4ea1c27-4d59-11ec-91ac-01aa75ed71a1";
    private const string FirstType = "http://publications.europa.eu/ontology/cdm#event_legal_type_one";
    private const string SecondType = "http://publications.europa.eu/ontology/cdm#event_legal_type_two";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:3c7e9f21-8b45-4d06-a19e-5f2b7c48d031", new string('b', 64));

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        EuProcedureEventDiscoveryPlan.Create().CreateDeliveryProfile();

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("eu-procedure-event-producer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:4b0f7c26-9d31-4e58-a07b-13c58fe2a904",
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))),
            bytes);
    }

    private static RepeatedEnumerationRdfTerm Iri(string value) =>
        RepeatedEnumerationRdfTerm.Iri(value);

    private static RepeatedEnumerationRdfTerm Literal(string value) =>
        RepeatedEnumerationRdfTerm.Literal(value, null, null);

    /// <summary>
    /// A date literal as the publisher actually delivers one: carrying its datatype.
    /// </summary>
    /// <remarks>
    /// The fixture used to send a PLAIN literal while the <c>date_datatype</c> column said
    /// <c>xsd:date</c>, which no honest delivery does — both come from the same term, one through
    /// <c>DATATYPE()</c>. Writing the mutations is what surfaced it: a producer reading only the
    /// column would have passed every test here while being unable to notice the two disagreeing.
    /// </remarks>
    private static RepeatedEnumerationRdfTerm TypedDate(string value, string datatype = XsdDate) =>
        RepeatedEnumerationRdfTerm.Literal(value, datatype, null);

    /// <summary>
    /// The grouped count as SPARQL delivers it: an <c>xsd:integer</c> literal.
    /// </summary>
    /// <remarks>
    /// The fixture used to emit a PLAIN literal here, which no <c>COUNT(*)</c> produces. Codex found
    /// the matching hole in the producer, which checked the digits and not the datatype — a positive
    /// <c>xsd:string</c> was accepted as a count. Fixture and guard were weak in the same place, so
    /// neither could reveal the other.
    /// </remarks>
    private static RepeatedEnumerationRdfTerm Count(long value) =>
        RepeatedEnumerationRdfTerm.Literal(
            value.ToString(System.Globalization.CultureInfo.InvariantCulture), XsdInteger, null);

    private static RepeatedEnumerationRdfTerm Unbound() =>
        RepeatedEnumerationRdfTerm.Unbound();

    /// <summary>The marker the plan's own BIND produces for a term of this kind.</summary>
    private static string Marker(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => "iri",
        RepeatedEnumerationRdfTermKind.Literal => "literal",
        RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
        _ => "unbound",
    };

    private static string Datatype(RepeatedEnumerationRdfTerm term) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal ? term.Datatype ?? string.Empty : string.Empty;

    private static string Language(RepeatedEnumerationRdfTerm term) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal ? term.Language ?? string.Empty : string.Empty;

    /// <summary>
    /// One delivered row, with every term, marker and cursor key chosen independently so a test can
    /// put them in disagreement on purpose.
    /// </summary>
    /// <remarks>
    /// Independence is the point. A builder that derived the markers and keys from the terms could
    /// never express the deliveries this producer exists to refuse, and every disagreement guard
    /// would be unreachable from its own fixture.
    /// </remarks>
    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm? eventTerm = null,
        string? eventKind = null,
        RepeatedEnumerationRdfTerm? typeTerm = null,
        string? typeKind = null,
        RepeatedEnumerationRdfTerm? dateTerm = null,
        string? dateKind = null,
        string? dateDatatype = null,
        string dossier = Dossier,
        RepeatedEnumerationRdfTerm? multiplicity = null,
        string? typeDatatype = null,
        string? typeLanguage = null,
        string? key1 = null,
        string? key2 = null,
        string? key3 = null,
        string? key4 = null,
        string? key7 = null,
        string? key8 = null,
        string? key9 = null,
        string? key10 = null)
    {
        var subject = eventTerm ?? Iri(Event);
        var count = multiplicity ?? Count(1);
        var type = typeTerm ?? Iri(FirstType);
        var date = dateTerm ?? TypedDate("2021-11-24");
        // Defaults to whatever the term itself carries, so an honest row is coherent by
        // construction; a test that wants them to disagree passes dateDatatype explicitly.
        var datatype = dateDatatype ??
            (date.Kind == RepeatedEnumerationRdfTermKind.Literal ? date.Datatype ?? string.Empty : string.Empty);

        var typeLanguageColumn = typeLanguage ?? Language(type);
        var typeDatatypeColumn = typeDatatype ?? Datatype(type);
        var dateLanguageColumn = Language(date);

        // Every marker and key defaults to what the term itself says, so an honest row is COHERENT
        // BY CONSTRUCTION and a test that wants a contradiction has to ask for exactly one. The
        // builder used to hard-code "iri" for the type's kind key regardless of the term, so a
        // blank-node test refused for the key rather than for the blank node - it passed while the
        // rule it named was unreachable. A surviving mutation is what showed that.
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            subject,
            Literal(eventKind ?? Marker(subject)),
            Iri(dossier),
            type,
            Literal(typeKind ?? Marker(type)),
            Literal(typeDatatypeColumn),
            Literal(typeLanguageColumn),
            date,
            Literal(dateKind ?? Marker(date)),
            Literal(datatype),
            Literal(dateLanguageColumn),
            count,
            Literal(key1 ?? subject.Value ?? string.Empty),
            Literal(key2 ?? Marker(subject)),
            Literal(key3 ?? type.Value ?? string.Empty),
            Literal(key4 ?? Marker(type)),
            Literal(typeDatatypeColumn),
            Literal(typeLanguageColumn),
            Literal(key7 ?? dossier),
            Literal(key8 ?? date.Value ?? string.Empty),
            Literal(key9 ?? Marker(date)),
            Literal(key10 ?? datatype),
            Literal(dateLanguageColumn),
        };

        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static EuProcedureEventProductionResult Decode(params RepeatedEnumerationRow[] rows) =>
        EuProcedureEventProducer.DecodeRows(rows, Profile(), [Dossier], Evidence);

    /// <summary>
    /// The whole chain runs: executor, proof, verified rows, observations. Nothing is supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE GUARD THAT MAKES THE FAMILY REACHED BY SOMETHING. Every other test here calls the
    /// internal decoder with rows a test built and an evidence reference a test invented. That
    /// proves the decoding and proves nothing about whether this family can be run at all — and
    /// before <see cref="EuProcedureEventProducer.RunAsync"/> existed it could not be: the plan, the
    /// executor entry point and the decoder were three parts joined by no caller in <c>src/</c>.
    /// </para>
    /// <para>
    /// It is also what satisfies Candidate 5 R5.3's second clause. The completion evidence these
    /// observations cite is the RUN'S own, taken from the enumeration proof, and the rows reached
    /// the decoder through <c>VerifiedRepeatedEnumerationRows.TryOpen</c> rather than from a caller.
    /// A test asserting the observations exist would pass without either; this asserts the evidence
    /// reference is the one the run produced, which nothing but a real run can supply.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheProducerRunsTheFamilyEndToEndAndCitesTheRunsOwnEvidence()
    {
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["ProcedureEvent"] = EuAcquisitionTestFixture.ScriptFor(
                "ProcedureEvent",
                2,
                [
                    EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"),
                    EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, SecondType, "2021-11-24"),
                ],
                EuAcquisitionTestFixture.ProcedureEventProjection),
        };

        var producer = new EuProcedureEventProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

        var result = await producer.RunAsync(
            new EuProcedureEventRunRequest(
                EuProcedureEventDiscoveryPlan.Create(),
                [Dossier],
                "urn:uuid:c17d4e83-2f60-4b95-8a1e-6d9074bf3c52",
                RendererSource()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuProcedureEventProductionRefusal.None, result.Refusal,
            $"the family must be runnable end to end: {result.Refusal} {result.Detail}");
        Assert.HasCount(1, result.Observations!);

        var observation = result.Observations![0];
        CollectionAssert.AreEqual(
            new[] { FirstType, SecondType }, observation.ObservedTypeIris.ToArray(),
            "the two delivered rows grouped back into one event with both declared types.");

        Assert.IsNotNull(result.CompletionEvidenceRef);
        Assert.AreEqual(
            result.CompletionEvidenceRef!.ResourceId, observation.SourceObservationId,
            "the custody coordinate is the run's own, not one a caller invented.");
        Assert.IsGreaterThan(0, result.ProductRequestCount);

        // The coverage this run publishes is what it ASKED, in the plan's canonical form.
        Assert.HasCount(1, result.EventsOf(Dossier));
    }

    /// <summary>
    /// An event's two declared types become one observation carrying both, in delivery order.
    /// </summary>
    /// <remarks>
    /// The admit path, asserted first. This is the whole reason the plan refuses to concatenate and
    /// the executor refuses to group: the two rows exist so each type reaches this decoder as its
    /// own term, and this is where the cost of that decision is paid back.
    /// </remarks>
    [TestMethod]
    public void OneEventsTwoDeclaredTypesBecomeOneObservationCarryingBoth()
    {
        var result = Decode(
            Row(typeTerm: Iri(FirstType)),
            Row(typeTerm: Iri(SecondType)));

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.Observations!);

        var observation = result.Observations![0];
        Assert.AreEqual(Event, observation.EventIri);
        CollectionAssert.AreEqual(
            new[] { FirstType, SecondType }, observation.ObservedTypeIris.ToArray(),
            "every declared type is kept, in the order the publisher delivered them.");
        Assert.AreEqual("2021-11-24", observation.RawDateLexical);
        Assert.AreEqual(XsdDate, observation.DateDatatypeIri);
        Assert.AreEqual(Evidence.ResourceId, observation.SourceObservationId);
    }

    /// <summary>Two events stay two, and neither borrows the other's types.</summary>
    [TestMethod]
    public void TwoEventsAreGroupedApartRatherThanPooled()
    {
        var result = Decode(
            Row(typeTerm: Iri(FirstType)),
            Row(eventTerm: Iri(OtherEvent), typeTerm: Iri(SecondType), key1: OtherEvent));

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(2, result.Observations!);
        Assert.HasCount(
            1, result.Observations!.Single(value => value.EventIri == Event).ObservedTypeIris);
        Assert.HasCount(
            1, result.Observations!.Single(value => value.EventIri == OtherEvent).ObservedTypeIris);
    }

    /// <summary>
    /// An untyped event is excluded with the contract's own reason, and its neighbours survive.
    /// </summary>
    /// <remarks>
    /// The plan asks for the untyped event by name, so it is an answer rather than a gap. Refusing
    /// the whole production over it would discard events the publisher did deliver; dropping it
    /// silently would be the defect the plan's own repair exists to prevent, moved one layer down.
    /// </remarks>
    [TestMethod]
    public void AnUntypedEventIsExcludedWithItsReasonRatherThanSinkingTheRun()
    {
        var result = Decode(
            Row(typeTerm: Iri(FirstType)),
            Row(eventTerm: Iri(OtherEvent), typeTerm: Unbound(), typeKind: "unbound", key1: OtherEvent));

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.Observations!);
        Assert.AreEqual(Event, result.Observations![0].EventIri);

        Assert.HasCount(1, result.ExcludedEvents!);
        Assert.AreEqual(OtherEvent, result.ExcludedEvents![0].EventIri);
        Assert.AreEqual(
            EuProcedureEventRefusal.EventTypeMissing, result.ExcludedEvents![0].Refusal,
            "the reason comes from the contract rather than being re-derived here.");
    }

    /// <summary>An undated event is excluded with its own reason, not the type's.</summary>
    [TestMethod]
    public void AnUndatedEventIsExcludedWithItsOwnReason()
    {
        var result = Decode(Row(dateTerm: Unbound(), dateKind: "unbound"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsEmpty(result.Observations!);
        Assert.HasCount(1, result.ExcludedEvents!);
        Assert.AreEqual(
            EuProcedureEventRefusal.EventDateMissingOrNotALiteral,
            result.ExcludedEvents![0].Refusal);
    }

    /// <summary>
    /// A type term delivered as a literal is a publisher fact, not a broken delivery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contract names this <c>EventTypeNotAnIri</c> and refuses the observation over it, which
    /// is a judgment about what the publisher said. So the row is carried through rather than
    /// refused here, and the offending term is retained in the exclusion — an exclusion that
    /// reported a type list with the malformed member missing would describe a delivery that never
    /// happened.
    /// </para>
    /// <para>
    /// This is the row that makes <c>EventTypeNotAnIri</c> reachable at all. It was a production
    /// path no delivery could produce until the plan asked for the branch and this decoder stopped
    /// filtering.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ATypeDeliveredAsALiteralReachesTheContractRatherThanBeingFiltered()
    {
        var result = Decode(
            Row(typeTerm: Iri(FirstType)),
            Row(typeTerm: Literal("not-an-iri"), typeKind: "literal", key3: "not-an-iri"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsEmpty(result.Observations!);
        Assert.HasCount(1, result.ExcludedEvents!);
        Assert.AreEqual(
            EuProcedureEventRefusal.EventTypeNotAnIri, result.ExcludedEvents![0].Refusal);
        CollectionAssert.AreEqual(
            new[] { FirstType, "not-an-iri" },
            result.ExcludedEvents![0].ObservedTypeIris.ToArray(),
            "the offending term is retained; an exclusion that hid it would misdescribe the delivery.");
    }

    /// <summary>A marker disagreeing with its term refuses the production whole.</summary>
    /// <remarks>
    /// A disagreement means one of the two is wrong and this reader has no basis for preferring
    /// either. Every marker's whole value space is compared, so a literal term carrying an
    /// <c>iri</c> marker cannot agree with itself the way a collapsed unbound-only check would let
    /// it.
    /// </remarks>
    [TestMethod]
    public void AMarkerThatDisagreesWithItsTermRefusesTheDelivery()
    {
        var result = Decode(Row(typeTerm: Literal("x"), typeKind: "iri", key3: "x"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "type_kind");
    }

    /// <summary>A marker that is not the query's own plain literal is not the query's marker.</summary>
    [TestMethod]
    public void AMarkerThatIsNotAnUnqualifiedPlainLiteralIsRefused()
    {
        var profile = Profile();
        var row = Row();
        var terms = row.Terms.ToList();
        var ordinal = profile.ProjectionVariables.ToList().IndexOf("type_kind");
        terms[ordinal] = Iri("iri");

        var result = EuProcedureEventProducer.DecodeRows(
            [new RepeatedEnumerationRow(terms, terms, terms)], profile, [Dossier], Evidence);

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
    }

    /// <summary>
    /// A cursor key that does not key the terms its own row delivered refuses the production.
    /// </summary>
    /// <remarks>
    /// The keys are the page's proof of what it delivered and in what order. Ignoring them lets a
    /// verified page prove one tuple while this producer emits another, which is a false packet
    /// claim rather than a decoding preference.
    /// </remarks>
    [TestMethod]
    public void ACursorKeyThatDoesNotKeyItsOwnRowIsRefused()
    {
        var result = Decode(Row(key7: "http://publications.europa.eu/resource/cellar/other"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "key_7");
    }

    /// <summary>
    /// Corrupting ANY ONE of the eleven cursor keys refuses the row, naming that key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as a sweep because the individual guards left holes. Mutations that dropped the
    /// event's kind key and the type's language key both survived a suite that checked two keys by
    /// hand: the honest value of several of these is the empty string, so a producer ignoring them
    /// agrees with a fixture that also leaves them empty, and neither side ever disagrees.
    /// </para>
    /// <para>
    /// The six kind and qualifier keys are the ones this most protects. They exist because two terms
    /// can share every lexical form and still be different facts — the whole content of the plan's
    /// second review round — so a producer that verified only the lexical five would be ignoring the
    /// result of that round while appearing to check the cursor.
    /// </para>
    /// <para>
    /// Driven off the live profile rather than a written list, so a key added to the plan is covered
    /// here the day it appears instead of the day someone remembers.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void CorruptingAnySingleCursorKeyRefusesTheRowAndNamesIt()
    {
        var profile = Profile();
        var keys = profile.CursorVariables;

        Assert.IsGreaterThan(
            10, keys.Count, "this family keys eleven positions; a smaller cursor is a repair lost.");

        // A row rich enough that no key's honest value is empty by accident: a language-tagged
        // literal type carries both qualifiers, and the date carries its datatype.
        static RepeatedEnumerationRow Honest() => Row(
            typeTerm: RepeatedEnumerationRdfTerm.Literal("some-type", null, "en"));

        foreach (var key in keys)
        {
            var ordinal = profile.ProjectionVariables.ToList().IndexOf(key);
            Assert.IsGreaterThan(-1, ordinal, $"{key} is projected.");

            var terms = Honest().Terms.ToList();
            var honestValue = terms[ordinal].Value ?? string.Empty;
            terms[ordinal] = Literal(honestValue + "-not-what-was-delivered");

            var result = EuProcedureEventProducer.DecodeRows(
                [new RepeatedEnumerationRow(terms, terms, terms)], profile, [Dossier], Evidence);

            Assert.AreEqual(
                EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal,
                $"{key} was corrupted and the row was still admitted, so that key keys nothing.");
            StringAssert.Contains(
                result.Detail!, key,
                $"the refusal must name {key} rather than some other key that happened to differ.");
        }
    }

    /// <summary>The empty key an unbound term contributes is asserted, not skipped.</summary>
    [TestMethod]
    public void TheEmptyKeyAnUnboundTermContributesIsCheckedRatherThanSkipped()
    {
        var result = Decode(
            Row(dateTerm: Unbound(), dateKind: "unbound", key8: "2021-11-24"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "key_8");
    }

    /// <summary>Rows of one event naming two dossiers are a contradiction, not a choice.</summary>
    [TestMethod]
    public void OneEventNamingTwoDossiersRefusesRatherThanPickingOne()
    {
        const string OtherDossier = "http://publications.europa.eu/resource/cellar/9999ffff-4d59-11ec-91ac-01aa75ed71a1";

        var result = EuProcedureEventProducer.DecodeRows(
            [
                Row(typeTerm: Iri(FirstType)),
                Row(typeTerm: Iri(SecondType), dossier: OtherDossier, key7: OtherDossier),
            ],
            Profile(),
            [Dossier, OtherDossier],
            Evidence);

        Assert.AreEqual(
            EuProcedureEventProductionRefusal.EventDossierNotConsistent, result.Refusal);
    }

    /// <summary>Rows of one event stating two dates are the same class of contradiction.</summary>
    [TestMethod]
    public void OneEventStatingTwoDatesRefusesRatherThanPickingOne()
    {
        var result = Decode(
            Row(typeTerm: Iri(FirstType)),
            Row(typeTerm: Iri(SecondType), dateTerm: TypedDate("2020-01-01"), key8: "2020-01-01"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.EventDateNotConsistent, result.Refusal);
    }

    /// <summary>A blank-node type has no lexical identity to carry, so the delivery is refused.</summary>
    /// <remarks>
    /// Distinct from the literal case on purpose. A literal type is a term the contract can judge
    /// and reject by name; a blank node is one this reader cannot carry at all, which is a statement
    /// about the delivery rather than about the publisher's facts.
    /// </remarks>
    [TestMethod]
    public void ABlankNodeTypeIsRefusedRatherThanExcluded()
    {
        var result = Decode(Row(typeTerm: RepeatedEnumerationRdfTerm.BlankNode("b0")));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(
            result.Detail!, "blank node",
            "refused for being a blank node, not incidentally for some key that disagrees.");
    }

    /// <summary>
    /// Asking about a dossier this run never asked about refuses rather than answering empty.
    /// </summary>
    /// <remarks>
    /// An empty list would let a caller read "this dossier has no events" out of a run that never
    /// looked, which is a different answer from a proven absence and indistinguishable once returned.
    /// </remarks>
    [TestMethod]
    public void ADossierThisRunNeverAskedAboutIsRefusedRatherThanAnsweredEmpty()
    {
        var result = Decode(Row());

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.EventsOf(Dossier));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => result.EventsOf("http://publications.europa.eu/resource/cellar/never-asked"));
    }

    /// <summary>A refused production answers no question about any dossier.</summary>
    [TestMethod]
    public void ARefusedProductionAnswersNothing()
    {
        var result = Decode(Row(typeTerm: Literal("x"), typeKind: "iri", key3: "x"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        Assert.ThrowsExactly<InvalidOperationException>(() => result.EventsOf(Dossier));
        Assert.ThrowsExactly<InvalidOperationException>(() => result.ExcludedEventsOf(Dossier));
    }

    /// <summary>
    /// A date term whose own datatype contradicts its projected column refuses the delivery.
    /// </summary>
    /// <remarks>
    /// The column is what the canonical key is built from, so a term disagreeing with it is a row
    /// that keys as one fact and decodes as another — the page proves a tuple this producer would
    /// not emit. Both come from the same term in an honest delivery, one through <c>DATATYPE()</c>,
    /// so they cannot disagree unless something is wrong.
    /// </remarks>
    [TestMethod]
    public void ADateWhoseDatatypeContradictsItsProjectedColumnIsRefused()
    {
        var result = Decode(Row(
            dateTerm: TypedDate("2021-11-24", "http://www.w3.org/2001/XMLSchema#gYear"),
            dateDatatype: XsdDate));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "date_datatype");
    }

    /// <summary>The same rule holds for the type term's qualifiers.</summary>
    /// <remarks>
    /// The type's datatype and language columns were added to the plan in review, precisely so two
    /// literal types with one lexical value could be told apart. A producer that did not check them
    /// against their term would let the distinction those keys exist for be stated falsely.
    /// </remarks>
    [TestMethod]
    public void ATypeWhoseLanguageContradictsItsProjectedColumnIsRefused()
    {
        var result = Decode(Row(
            typeTerm: RepeatedEnumerationRdfTerm.Literal("x", null, "en"),
            typeLanguage: string.Empty));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "type_language");
    }

    /// <summary>An unbound term carries no qualifiers, and its columns must say so too.</summary>
    [TestMethod]
    public void AnUnboundTermWhoseColumnsClaimQualifiersIsRefused()
    {
        var result = Decode(Row(
            dateTerm: Unbound(), dateKind: "unbound", dateDatatype: XsdDate, key10: XsdDate));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "date_datatype");
    }

    /// <summary>
    /// A row naming a dossier this run never asked about refuses, rather than being admitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Codex found this at head <c>f8a3b5c0</c> and it is the sharpest defect this family has had.
    /// The producer decoded every row, admitted it, and then published the CALLER'S requested set as
    /// <c>DossiersAskedAbout</c> — as though that set were proven coverage of what came back. A run
    /// asking about A and delivered an event of B returned success, and <c>EventsOf(A)</c> then
    /// answered an evidenced EMPTY set while the sole admitted observation belonged to B.
    /// </para>
    /// <para>
    /// A false absence that looks proven is worse than an error, because nothing downstream can tell
    /// it from a real one. The executor makes the same check on the delivery it drives; that did not
    /// cover this, because <c>DecodeRows</c> is a public entry point that can be handed rows from
    /// anywhere.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ARowNamingADossierThisRunNeverAskedAboutRefusesRatherThanBeingAdmitted()
    {
        const string NeverRequested =
            "http://publications.europa.eu/resource/cellar/00000000-1111-2222-3333-444444444444";

        var result = EuProcedureEventProducer.DecodeRows(
            [Row(dossier: NeverRequested)], Profile(), [Dossier], Evidence);

        Assert.AreEqual(
            EuProcedureEventProductionRefusal.DeliveredDossierOutsideRequestedPartition,
            result.Refusal,
            "an event of an unrequested dossier must not be admitted under this run's coverage.");
        StringAssert.Contains(result.Detail!, NeverRequested);
    }

    /// <summary>
    /// A foreign row is caught wherever it sits in the delivery, not only when it comes first.
    /// </summary>
    /// <remarks>
    /// The membership check walks every decoded row. A check that looked only at the first would
    /// pass the sibling guard above, which supplies a single foreign row — so that guard alone
    /// cannot tell "every row is checked" from "the first row is checked". A surviving mutation
    /// showed exactly that.
    /// </remarks>
    [TestMethod]
    public void AForeignRowIsRefusedEvenWhenAValidRowPrecedesIt()
    {
        const string NeverRequested =
            "http://publications.europa.eu/resource/cellar/00000000-1111-2222-3333-444444444444";

        var result = EuProcedureEventProducer.DecodeRows(
            [
                Row(dossier: Dossier),
                Row(eventTerm: Iri(OtherEvent), dossier: NeverRequested),
            ],
            Profile(), [Dossier], Evidence);

        Assert.AreEqual(
            EuProcedureEventProductionRefusal.DeliveredDossierOutsideRequestedPartition,
            result.Refusal);
        StringAssert.Contains(result.Detail!, NeverRequested);
    }

    /// <summary>
    /// A requested dossier that returned no events answers a PROVEN EMPTY list, not an error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole reason <c>DossiersAskedAbout</c> is the REQUESTED set and not the delivered
    /// one. "We asked and the publisher held nothing" is a real answer and the only kind this family
    /// can give about a dossier with no events; publishing the delivered set instead would make that
    /// dossier indistinguishable from one nobody asked about, and turn a proven absence into a
    /// throw.
    /// </para>
    /// <para>
    /// A surviving mutation published the delivered set as coverage and nothing noticed, because
    /// every other test asks only about dossiers that did return rows.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ARequestedDossierWithNoEventsAnswersAProvenEmptyListRatherThanThrowing()
    {
        const string AlsoRequested =
            "http://publications.europa.eu/resource/cellar/7c3e5a91-4d59-11ec-91ac-01aa75ed71a1";

        var result = EuProcedureEventProducer.DecodeRows(
            [Row(dossier: Dossier)], Profile(), [Dossier, AlsoRequested], Evidence);

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.EventsOf(Dossier));
        Assert.IsEmpty(
            result.EventsOf(AlsoRequested),
            "asked about and nothing held is an answer this run is entitled to give.");
        Assert.IsEmpty(result.ExcludedEventsOf(AlsoRequested));
    }

    /// <summary>
    /// The companion admit case: a row naming a dossier that WAS requested is admitted.
    /// </summary>
    /// <remarks>
    /// Without this the membership check could refuse everything and the guard above would still
    /// pass, which is the failure mode the case-law family shipped with.
    /// </remarks>
    [TestMethod]
    public void ARowNamingARequestedDossierIsStillAdmitted()
    {
        var result = EuProcedureEventProducer.DecodeRows(
            [Row(dossier: Dossier)], Profile(), [Dossier], Evidence);

        Assert.AreEqual(EuProcedureEventProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.EventsOf(Dossier));
    }

    /// <summary>
    /// A count that is not an <c>xsd:integer</c> is not the term <c>COUNT(*)</c> delivers.
    /// </summary>
    /// <remarks>
    /// A positive <c>xsd:string</c> passed every earlier guard: the kind was right, the digits
    /// parsed, the value was positive. Only the datatype said it was not a count.
    /// </remarks>
    [TestMethod]
    public void ACountCarryingTheWrongDatatypeIsRefused()
    {
        var result = Decode(Row(
            multiplicity: RepeatedEnumerationRdfTerm.Literal("3", XsdString, null)));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "multiplicity");
    }

    // A count carrying BOTH an xsd:integer datatype and a language tag has no test, and the reason
    // is worth writing down rather than leaving as an absence. RepeatedEnumerationRdfTerm's own
    // constructor refuses the combination outright - "Only one literal datatype or language
    // qualifier is allowed", RepeatedEnumerationDeliveryProof.cs:78 - so the term cannot be built
    // and no delivery can present it. The producer's `term.Language is not null` clause is therefore
    // unreachable behind its datatype check: redundant rather than load bearing. It is kept because
    // every sibling producer spells the guard the same way, and deleting it here alone would make
    // E8 differ from the LU family for no behavioural gain. Mutating that clause away produces an
    // EQUIVALENT mutant, which is why one survives the sweep by construction and not by omission.

    /// <summary>The grouped count is read rather than ignored.</summary>
    [TestMethod]
    public void TheGroupedCountIsReadRatherThanIgnored()
    {
        var result = Decode(Row(multiplicity: Count(0)));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "multiplicity");
    }
}
