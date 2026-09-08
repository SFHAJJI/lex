using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E8, the EU half: procedure events observed through the dossier predicate.
/// </summary>
/// <remarks>
/// <para>
/// The authority for this shape is the question catalogue's row 58: "35 dated typed
/// <c>event_legal</c> records via the inverse predicate <c>event_legal_part_of_dossier</c> (248,683
/// events corpus-wide) … ingest procedure events (query the inverse predicate, <b>tolerate mixed
/// event vocabularies</b>)". Those figures are the authority's and are not measured here.
/// </para>
/// <para>
/// The guards below are mostly about that last clause. A closed enum of event types would have been
/// the natural thing to write and would have been an invention; these prove the type is carried
/// rather than classified, while the things the authority DOES state — that the records are dated
/// and typed — stay required.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuProcedureEventTests
{
    private const string Cdm = "http://publications.europa.eu/ontology/cdm#";
    private const string Dossier = "http://publications.europa.eu/resource/cellar/11111111-1111-4111-8111-111111111111";
    private const string Event = "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string Observation = "urn:uuid:00000000-0000-4000-8000-0000000000e1";

    private static EuProcedureEventObservation? Create(
        out EuProcedureEventRefusal refusal,
        string? eventIri = Event,
        string? dossierIri = Dossier,
        IReadOnlyList<string>? types = null,
        string? date = "2024-03-13",
        string? datatype = XsdDate) =>
        EuProcedureEventObservation.TryCreate(
            eventIri, dossierIri, types ?? [Cdm + "event_legal_vote"], date, datatype, Observation, out refusal);

    [TestMethod]
    public void AWellFormedProcedureEventIsObservedWithItsOwnTerms()
    {
        var observed = Create(out var refusal);

        Assert.AreEqual(EuProcedureEventRefusal.None, refusal);
        Assert.IsNotNull(observed);
        Assert.AreEqual(Event, observed!.EventIri);
        Assert.AreEqual("2024-03-13", observed.RawDateLexical);
        Assert.AreEqual(DatePrecision.YearMonthDay, observed.DatePrecision);
        Assert.AreEqual(Observation, observed.SourceObservationId);
        Assert.AreEqual(Dossier, observed.DossierIdentity.Identifiers[0].RawValue);
    }

    /// <summary>
    /// The central constraint: an event type nothing here recognises is RETAINED, not refused and
    /// not reclassified.
    /// </summary>
    /// <remarks>
    /// The authority says to tolerate mixed event vocabularies. A closed type enum would refuse or
    /// silently recategorise real publisher events whose type this codebase never anticipated,
    /// which is the false absence S2-A05 exists to prevent — and unlike an ordinary guess it would
    /// look correct, because the events it dropped would simply not be there to notice.
    /// </remarks>
    [TestMethod]
    public void AnEventTypeThisCodebaseHasNeverSeenIsRetainedVerbatim()
    {
        const string Unheard = "http://example.invalid/some-other-ontology#committee_referral";

        var observed = Create(out var refusal, types: [Unheard]);

        Assert.AreEqual(EuProcedureEventRefusal.None, refusal, "an unrecognised type is not a malformed one.");
        Assert.IsNotNull(observed);
        CollectionAssert.AreEqual(new[] { Unheard }, observed!.ObservedTypeIris.ToArray());
    }

    /// <summary>
    /// Several types on one event are all kept, in delivery order. "Mixed vocabularies" includes
    /// one node carrying types from more than one of them.
    /// </summary>
    [TestMethod]
    public void EveryTypeOnOneEventIsKeptInDeliveryOrder()
    {
        string[] types =
        [
            Cdm + "event_legal",
            Cdm + "event_legal_adoption",
            "http://example.invalid/other#reading_first",
        ];

        var observed = Create(out var refusal, types: types);

        Assert.AreEqual(EuProcedureEventRefusal.None, refusal);
        Assert.IsNotNull(observed);
        CollectionAssert.AreEqual(
            types, observed!.ObservedTypeIris.ToArray(),
            "no type may be dropped or reordered on the way in.");
    }

    /// <summary>
    /// Tolerating an unknown vocabulary is not the same as tolerating no type at all: the authority
    /// describes these as dated TYPED records.
    /// </summary>
    [TestMethod]
    public void AnUntypedEventIsRefusedEvenThoughUnknownTypesAreAdmitted()
    {
        Assert.IsNull(Create(out var empty, types: []));
        Assert.AreEqual(EuProcedureEventRefusal.EventTypeMissing, empty);

        // A list that is present but carries nothing usable is NOT the same fact as an absent one,
        // and no longer reports it. The publisher did declare a type here; it cannot be carried.
        Assert.IsNull(Create(out var unusable, types: ["not-an-iri", ""]));
        Assert.AreEqual(EuProcedureEventRefusal.EventTypeNotAnIri, unusable);
    }

    /// <summary>
    /// A malformed type beside a well-formed one refuses the row. It is never dropped so that the
    /// remaining types can be delivered as though they were all the publisher declared.
    /// </summary>
    /// <remarks>
    /// This is the defect Codex found on head 4e798694 and it is worth stating plainly, because the
    /// first draft of this file argued at length against closing the type vocabulary and then
    /// silently discarded terms anyway. <c>Where(IsAbsoluteHttpIri)</c> kept every good type and
    /// removed the bad one, so a row declaring <c>[event_legal, "not-an-iri"]</c> arrived looking
    /// like a clean single-typed event. That is the same false absence a closed enum produces,
    /// reached by a different route, and it is worse for being invisible: the discarded term left
    /// nothing behind to notice.
    /// </remarks>
    [TestMethod]
    public void AMalformedTypeBesideAGoodOneRefusesTheRowRatherThanVanishing()
    {
        var observed = Create(out var refusal, types: [Cdm + "event_legal", "not-an-iri"]);

        Assert.IsNull(observed, "the good type must not be delivered as if it were the only one declared.");
        Assert.AreEqual(EuProcedureEventRefusal.EventTypeNotAnIri, refusal);
    }

    /// <summary>
    /// A date valid for its datatype's SHAPE but impossible in the calendar is refused.
    /// </summary>
    /// <remarks>
    /// The second finding on 4e798694. The datatype was checked and the value never was, so
    /// <c>"2024-02-30"^^xsd:date</c> was delivered reporting
    /// <see cref="DatePrecision.YearMonthDay"/> — a precision claim about a day that does not
    /// exist. Precision describes the value, so a value that cannot support it makes the claim
    /// false rather than approximate. 2024 is a leap year and 29 February exists, which is why the
    /// 30th is the honest probe here.
    /// </remarks>
    [TestMethod]
    public void ADateThatCannotExistIsRefusedEvenThoughItsDatatypeIsOneWeAccept()
    {
        Assert.IsNull(Create(out var impossible, date: "2024-02-30"));
        Assert.AreEqual(EuProcedureEventRefusal.EventDateNotValidAtItsPrecision, impossible);

        // The neighbouring real date still passes, so the guard is not simply refusing February.
        Assert.IsNotNull(Create(out var leapDay, date: "2024-02-29"));
        Assert.AreEqual(EuProcedureEventRefusal.None, leapDay);

        // A value shaped for a different precision than its datatype declares is refused too.
        Assert.IsNull(Create(out var wrongShape, date: "2024-03"));
        Assert.AreEqual(EuProcedureEventRefusal.EventDateNotValidAtItsPrecision, wrongShape);
    }

    /// <summary>
    /// Each declared refusal is reachable from a delivered shape, so none is decoration.
    /// </summary>
    /// <remarks>
    /// The final assertion is the guard: it fails if a member is added to
    /// <see cref="EuProcedureEventRefusal"/> without a case here that drives it.
    /// </remarks>
    [TestMethod]
    public void EveryDeclaredRefusalIsReachableFromSomeDeliveredShape()
    {
        var reached = new List<EuProcedureEventRefusal>();

        Assert.IsNull(Create(out var notAnIri, eventIri: "not-an-iri"));
        reached.Add(notAnIri);

        Assert.IsNull(Create(out var dossierBad, dossierIri: "urn:not-http:x"));
        reached.Add(dossierBad);

        Assert.IsNull(Create(out var notACellarWork, dossierIri: "https://example.org/dossier/1"));
        reached.Add(notACellarWork);

        Assert.IsNull(Create(out var untyped, types: []));
        reached.Add(untyped);

        Assert.IsNull(Create(out var badDatatype, datatype: "http://www.w3.org/2001/XMLSchema#string"));
        reached.Add(badDatatype);

        Assert.IsNull(Create(out var badTypeTerm, types: ["not-an-iri"]));
        reached.Add(badTypeTerm);

        Assert.IsNull(Create(out var impossibleDate, date: "2024-02-30"));
        reached.Add(impossibleDate);

        Assert.IsNull(Create(out var noDate, date: null));
        reached.Add(noDate);

        CollectionAssert.AreEqual(
            Enum.GetValues<EuProcedureEventRefusal>()
                .Where(static member => member != EuProcedureEventRefusal.None)
                .ToArray(),
            reached.Distinct().Order().ToArray(),
            "every declared refusal must be driven by a case above, each reaching a distinct one.");
    }

    /// <summary>
    /// A dossier that IS an IRI but is not a Cellar work is refused with a typed reason, not with an
    /// escaped exception.
    /// </summary>
    /// <remarks>
    /// This is the guard for the catch around the identity construction, and it needs to exist
    /// separately from the reachability sweep above because the two dossier refusals are reached
    /// through different code. <c>urn:not-http:x</c> never gets as far as the identity contract; it
    /// is turned away by the IRI check at the top. Only a well-formed http IRI that the accepted
    /// contract declines — a real possibility, since the publisher is not obliged to keep every
    /// dossier under the Cellar authority — actually enters the try.
    ///
    /// Without this, deleting the try/catch leaves every other guard here passing, which is exactly
    /// what happened when it was checked: the catch was decoration, and a delivered row would have
    /// ended the run with a stack trace where a typed refusal belongs.
    /// </remarks>
    [TestMethod]
    public void AnIriTheIdentityContractWillNotCarryIsRefusedRatherThanThrown()
    {
        var observed = Create(out var refusal, dossierIri: "https://example.org/dossier/1");

        Assert.IsNull(observed);
        Assert.AreEqual(
            EuProcedureEventRefusal.DossierNotACellarWork, refusal,
            "an IRI the contract declines is not the same fact as a value that is not an IRI.");
    }

    /// <summary>
    /// Precision comes from the datatype the publisher sent and is never widened.
    /// </summary>
    [TestMethod]
    public void PrecisionIsReadFromTheDatatypeRatherThanAssumed()
    {
        (string Datatype, string Lexical, DatePrecision Expected)[] cases =
        [
            (XsdDate, "2024-03-13", DatePrecision.YearMonthDay),
            ("http://www.w3.org/2001/XMLSchema#gYearMonth", "2024-03", DatePrecision.YearMonth),
            ("http://www.w3.org/2001/XMLSchema#gYear", "2024", DatePrecision.Year),
        ];

        foreach (var (datatype, lexical, expected) in cases)
        {
            var observed = Create(out var refusal, date: lexical, datatype: datatype);

            Assert.AreEqual(EuProcedureEventRefusal.None, refusal, datatype);
            Assert.IsNotNull(observed, datatype);
            Assert.AreEqual(expected, observed!.DatePrecision, datatype);
            Assert.AreEqual(datatype, observed.DateDatatypeIri, datatype);
        }
    }

    /// <summary>
    /// The access path is pinned, because that part IS closed by the authority.
    /// </summary>
    [TestMethod]
    public void TheAccessPathTermsAreExactlyTheOnesTheAuthorityNames()
    {
        // Written out in full rather than rebuilt from the same Cdm prefix the contract uses: a
        // comparison between two compile-time constants is folded away and pins nothing, which is
        // what MSTEST0032 objects to and it is right to.
        CollectionAssert.AreEqual(
            new[]
            {
                "http://publications.europa.eu/ontology/cdm#event_legal_part_of_dossier",
                "http://publications.europa.eu/ontology/cdm#event_legal",
                "http://publications.europa.eu/ontology/cdm#dossier",
            },
            new[]
            {
                EuProcedureEventVocabulary.PartOfDossierPredicateUri,
                EuProcedureEventVocabulary.EventLegalClassIri,
                EuProcedureEventVocabulary.DossierClassIri,
            });
    }
}
