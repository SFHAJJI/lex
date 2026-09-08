using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a delivered procedure-event observation was refused. Closed.
/// </summary>
public enum EuProcedureEventRefusal
{
    None = 0,

    /// <summary>The event node was not an IRI, so there is nothing to name the observation by.</summary>
    EventNotAnIri = 1,

    /// <summary>The dossier the event declares itself part of was not an IRI.</summary>
    DossierNotAnIri = 2,

    /// <summary>
    /// The event declared no type at all. A typed event with an unrecognised type is admitted; an
    /// untyped one is not, because the authority's own description is "dated typed records".
    /// </summary>
    EventTypeMissing = 3,

    /// <summary>The event's date literal carried a datatype this reader cannot type a precision from.</summary>
    EventDateNotADateShape = 4,

    /// <summary>The event carried no date literal, or one that was not a literal at all.</summary>
    EventDateMissingOrNotALiteral = 5,

    /// <summary>
    /// The dossier WAS an IRI, and the accepted identity contract still would not carry it as a
    /// Cellar work.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="DossierNotAnIri"/> on purpose. Folding the two together would put
    /// "not an IRI" on a value that is plainly an IRI, and a typed reason that misdescribes the fact
    /// it reports is worse than an untyped one: it is read, believed, and never questioned. The
    /// publisher can deliver a perfectly good IRI under a host or path this codebase does not accept
    /// as a Cellar work, and that is what this says.
    /// </remarks>
    DossierNotACellarWork = 6,

    /// <summary>
    /// The event declared a type that is not an absolute HTTP IRI, so it cannot be carried as one.
    /// </summary>
    /// <remarks>
    /// The row is refused rather than delivered without that term. Keeping the well-formed types and
    /// discarding the malformed one would hand a reader a type list that looks complete and is not,
    /// and the discarded term would not be there to notice — the same false absence a closed type
    /// enum would have produced, arrived at by a different route. Tolerating an unknown vocabulary
    /// is not tolerating an unrepresentable term.
    /// </remarks>
    EventTypeNotAnIri = 7,

    /// <summary>
    /// The date literal carried a datatype this reader accepts, and a value that is not valid at the
    /// precision that datatype implies.
    /// </summary>
    /// <remarks>
    /// Checking the datatype alone would let <c>"2024-02-30"^^xsd:date</c> through carrying
    /// <see cref="DatePrecision.YearMonthDay"/>, which asserts a day that does not exist. The
    /// precision is a claim about the value, so the value has to support it.
    /// </remarks>
    EventDateNotValidAtItsPrecision = 8,
}

/// <summary>
/// The closed part of the EU procedure-event surface: the access path, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Only three terms here are closed, and they are closed because the authority names them. The
/// event's own TYPE is deliberately absent from this vocabulary; see
/// <see cref="EuProcedureEventObservation.ObservedTypeIris"/> for why pinning it would be an
/// invention rather than a contract.
/// </para>
/// <para>
/// The predicate runs from the event to its dossier, so a dossier's events are found by querying it
/// in the inverse direction — which is exactly how the authority describes reaching them: "35 dated
/// typed <c>event_legal</c> records via the inverse predicate <c>event_legal_part_of_dossier</c>
/// (248,683 events corpus-wide)". That figure is the authority's own and has not been measured here.
/// </para>
/// </remarks>
public static class EuProcedureEventVocabulary
{
    /// <summary>The event names its dossier; a dossier's events are reached by asking this inversely.</summary>
    public const string PartOfDossierPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "event_legal_part_of_dossier";

    /// <summary>The CDM FRBR class of the node this observation is about.</summary>
    public const string EventLegalClassIri = EuConsolidationDiscoveryPlan.Cdm + "event_legal";

    /// <summary>The CDM FRBR class of the node an event declares itself part of.</summary>
    public const string DossierClassIri = EuConsolidationDiscoveryPlan.Cdm + "dossier";
}

/// <summary>
/// One EU legislative procedure event as the publisher stated it: which dossier it belongs to, every
/// type it declared, and its date — retained rather than interpreted.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS AN OBSERVATION, NOT AN ACCEPTED FACT, AND THE DISTINCTION IS DELIBERATE. Stage 2's other
/// EU contracts bind onto an already-accepted Facts type: <see cref="EuDateAxiomBinding"/> mints a
/// <see cref="PublisherDateFact"/>, <see cref="EuCaseLawLinkBinding"/> mints a
/// <see cref="RelationFact"/>. There is no accepted Facts type for a procedure event, and inventing
/// one here would be minting an accepted surface out of a source reading. So this carries the
/// publisher's shape and stops there; whether procedure events become an accepted fact is a
/// decision for whoever needs them, made against real observations rather than ahead of any.
/// </para>
/// <para>
/// THE EVENT TYPE IS RETAINED, NEVER PINNED. The authority is explicit that this vocabulary is not
/// closed: "ingest procedure events (query the inverse predicate, <b>tolerate mixed event
/// vocabularies</b>)". A closed enum of event types would therefore be an invention, and worse than
/// an ordinary one — it would refuse or silently reclassify real events whose type this codebase
/// had not anticipated, which is the false-absence shape S2-A05 exists to prevent. Every type IRI
/// the publisher declared is carried verbatim in <see cref="ObservedTypeIris"/>, in delivery order,
/// including several on one event.
/// </para>
/// <para>
/// WHAT IS STILL REFUSED. An event with NO type is refused, because the authority describes these as
/// "dated typed records" and an untyped node is not one. An event declaring a type that is not an
/// absolute HTTP IRI is refused as a whole row, NOT delivered with that term quietly removed: a type
/// list that looks complete and is not would be the same false absence a closed enum produces. A
/// date that is absent, not a literal, carries a datatype outside the three date shapes, or carries
/// a value that is not valid at the precision that datatype implies, is refused too — the precision
/// is a claim about the value, so a value the claim does not fit is not a narrower reading of the
/// date, it is a wrong one. Tolerating a mixed vocabulary means not knowing which types exist; it
/// does not mean accepting a node with no type, an unrepresentable type, or an impossible date.
/// </para>
/// </remarks>
public sealed class EuProcedureEventObservation
{
    private EuProcedureEventObservation(
        string eventIri,
        OfficialIdentitySet dossierIdentity,
        IReadOnlyList<string> observedTypeIris,
        string rawDateLexical,
        string dateDatatypeIri,
        DatePrecision datePrecision,
        string sourceObservationId)
    {
        EventIri = eventIri;
        DossierIdentity = dossierIdentity;
        ObservedTypeIris = observedTypeIris;
        RawDateLexical = rawDateLexical;
        DateDatatypeIri = dateDatatypeIri;
        DatePrecision = datePrecision;
        SourceObservationId = sourceObservationId;
    }

    /// <summary>Who performed the reading. This codebase, never the publisher.</summary>
    public const string ParsedByAuthority = "https://lex.invalid/authority/eu-procedure-event/1";

    /// <summary>The event node's own IRI.</summary>
    public string EventIri { get; }

    /// <summary>The dossier this event declared itself part of.</summary>
    public OfficialIdentitySet DossierIdentity { get; }

    /// <summary>
    /// Every type IRI the publisher declared on this event, verbatim and in delivery order.
    /// </summary>
    /// <remarks>
    /// A list rather than one value, and a list of raw IRIs rather than an enum, because the
    /// authority says the event vocabularies are mixed. A reader that wants a known type matches
    /// against this; a type nobody here has seen is still present to be matched later.
    ///
    /// "Every" is exact. Nothing declared is filtered out on the way in, which is why a term that
    /// cannot be carried as an IRI refuses the row rather than being dropped from this list.
    /// </remarks>
    public IReadOnlyList<string> ObservedTypeIris { get; }

    /// <summary>The event's date exactly as the publisher wrote it.</summary>
    public string RawDateLexical { get; }

    /// <summary>The datatype IRI that date literal carried.</summary>
    public string DateDatatypeIri { get; }

    /// <summary>The precision that datatype implies. Never widened, never guessed.</summary>
    public DatePrecision DatePrecision { get; }

    /// <summary>The custody coordinate these terms were read from.</summary>
    public string SourceObservationId { get; }

    /// <summary>
    /// The only door that mints one. Refuses with a typed reason rather than returning a partial
    /// observation.
    /// </summary>
    public static EuProcedureEventObservation? TryCreate(
        string? eventIri,
        string? dossierIri,
        IReadOnlyList<string>? observedTypeIris,
        string? rawDateLexical,
        string? dateDatatypeIri,
        string sourceObservationId,
        out EuProcedureEventRefusal refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObservationId);
        refusal = EuProcedureEventRefusal.None;

        if (!IsAbsoluteHttpIri(eventIri))
        {
            refusal = EuProcedureEventRefusal.EventNotAnIri;
            return null;
        }

        if (!IsAbsoluteHttpIri(dossierIri))
        {
            refusal = EuProcedureEventRefusal.DossierNotAnIri;
            return null;
        }

        // Every declared term is kept for inspection. Filtering here was the defect: a row declaring
        // [event_legal, "not-an-iri"] was delivered as though event_legal were the only type the
        // publisher stated, and the dropped term left no trace to find.
        var types = observedTypeIris is null ? [] : observedTypeIris.ToArray();
        if (types.Length == 0)
        {
            refusal = EuProcedureEventRefusal.EventTypeMissing;
            return null;
        }

        if (Array.Exists(types, static value => !IsAbsoluteHttpIri(value)))
        {
            refusal = EuProcedureEventRefusal.EventTypeNotAnIri;
            return null;
        }

        if (string.IsNullOrEmpty(rawDateLexical))
        {
            refusal = EuProcedureEventRefusal.EventDateMissingOrNotALiteral;
            return null;
        }

        if (!TryPrecision(dateDatatypeIri, out var precision))
        {
            refusal = EuProcedureEventRefusal.EventDateNotADateShape;
            return null;
        }

        // The datatype says which shape the publisher claims; only the value can say whether the
        // claim holds. The accepted Facts surface already owns that grammar, so it is called rather
        // than restated here — a second spelling of a calendar rule is a second thing to drift.
        if (!PublisherDate.IsValidLexicalValue(rawDateLexical!, precision))
        {
            refusal = EuProcedureEventRefusal.EventDateNotValidAtItsPrecision;
            return null;
        }

        // The accepted identity contract decides what a Cellar work URI is, and it throws for a
        // shape it will not carry. That refusal is caught and typed here rather than allowed to
        // escape: a publisher can deliver an absolute http IRI that is not a Cellar work, and a
        // run must not end with a stack trace where a typed reason belongs.
        OfficialIdentitySet dossierIdentity;
        try
        {
            dossierIdentity = new OfficialIdentitySet(
                PublisherId.EuEurLex,
                [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, dossierIri!)]);
        }
        catch (ArgumentException)
        {
            refusal = EuProcedureEventRefusal.DossierNotACellarWork;
            return null;
        }

        return new EuProcedureEventObservation(
            eventIri!,
            dossierIdentity,
            Array.AsReadOnly(types),
            rawDateLexical!,
            dateDatatypeIri!,
            precision,
            sourceObservationId);
    }

    private static bool IsAbsoluteHttpIri(string? value) =>
        !string.IsNullOrEmpty(value) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    private static bool TryPrecision(string? datatypeIri, out DatePrecision precision)
    {
        switch (datatypeIri)
        {
            case "http://www.w3.org/2001/XMLSchema#date":
                precision = DatePrecision.YearMonthDay;
                return true;
            case "http://www.w3.org/2001/XMLSchema#gYearMonth":
                precision = DatePrecision.YearMonth;
                return true;
            case "http://www.w3.org/2001/XMLSchema#gYear":
                precision = DatePrecision.Year;
                return true;
            default:
                precision = DatePrecision.YearMonthDay;
                return false;
        }
    }
}
