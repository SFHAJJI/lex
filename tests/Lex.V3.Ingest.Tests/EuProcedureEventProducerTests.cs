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

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:3c7e9f21-8b45-4d06-a19e-5f2b7c48d031", new string('b', 64));

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        EuProcedureEventDiscoveryPlan.Create().CreateDeliveryProfile();

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
        string multiplicity = "1",
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
            Literal(multiplicity),
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

    /// <summary>The grouped count is read rather than ignored.</summary>
    [TestMethod]
    public void TheGroupedCountIsReadRatherThanIgnored()
    {
        var result = Decode(Row(multiplicity: "0"));

        Assert.AreEqual(EuProcedureEventProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "multiplicity");
    }
}
