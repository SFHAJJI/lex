using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>Why a delivered Conseil d'État opinion locator was refused. Closed.</summary>
public enum LuxembourgOpinionLocatorRefusal
{
    None = 0,

    /// <summary>The opinion node was not an IRI, so there is nothing to name the record by.</summary>
    OpinionNotAnIri = 1,

    /// <summary>The document locator was not an absolute HTTP(S) URI.</summary>
    DocumentLocatorNotAnAbsoluteHttpUri = 2,

    /// <summary>
    /// The document locator carried a query string or fragment.
    /// </summary>
    /// <remarks>
    /// Measured, not assumed. `conseil-etat.public.lu/robots.txt` disallows every query-string shape
    /// for `User-agent: *` — `Disallow: /*?*` with a single narrow `Allow: /*?b=*` — while bare paths
    /// are permitted. A locator carrying a query is therefore outside what the host allows, and a
    /// link this product publishes must be one a reader may follow.
    /// </remarks>
    DocumentLocatorIsNotRobotsPermitted = 3,

    /// <summary>The opinion carried no date literal, or one that was not a literal at all.</summary>
    OpinionDateMissingOrNotALiteral = 4,

    /// <summary>The opinion date carried a datatype this reader cannot type a precision from.</summary>
    OpinionDateNotADateShape = 5,

    /// <summary>
    /// The date literal was valid for its datatype's shape but not a date that exists.
    /// </summary>
    OpinionDateNotValidAtItsPrecision = 6,
}

/// <summary>
/// The closed part of the Conseil d'État opinion surface: the proven JOLux access path, the measured
/// licence position, and the criterion that would reverse the link-only decision.
/// </summary>
public static class LuxembourgOpinionLinkOnlyVocabulary
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";

    /// <summary>The JOLux class of an opinion event. 13,009 observed.</summary>
    public const string OpinionConseilEtatClassIri = Jolux + "OpinionConseilEtat";

    /// <summary>The draft-to-opinion edge. 36,169 opinion events observed across its families.</summary>
    public const string HasOpinionPredicateIri = Jolux + "hasOpinion";

    /// <summary>The opinion-to-document edge. 6,393 observed.</summary>
    public const string HasResultingOpinionDocumentPredicateIri = Jolux + "hasResultingOpinionDocument";

    /// <summary>The opinion's own date. 9,144 observed.</summary>
    public const string OpinionDatePredicateIri = Jolux + "opinionDate";

    /// <summary>
    /// The condition that would end the link-only disposition, composed from cited authority rather
    /// than authored here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The graduation rule is absolute: "nothing graduates past POINT without an open,
    /// robots-permitted, licence-compatible official machine channel" (Decision 18; the V3 spec's own
    /// line 80; the product spec's line 233). The nearest worked reversal for a licence-unproven
    /// class is "robots permission plus licence triples".
    /// </para>
    /// <para>
    /// Applied here, the two halves are in different states, which is why they are stated separately.
    /// <b>Robots permission is already satisfied</b> on `conseil-etat.public.lu` for bare
    /// `/content/dam/…` paths, and is <b>unstated</b> on `wdocs-pub.chd.lu`, whose `robots.txt`
    /// answers 404. <b>The licence half is unsatisfied on every host</b>: the JOLux manifestation
    /// carries no licence triple, `conseil-etat.public.lu` publishes no reuse statement, and a
    /// probe of the served document returned no licence, rights or `X-Robots-Tag` header.
    /// </para>
    /// <para>
    /// So the licence is the sole blocking condition, and it is checkable rather than a sentiment.
    /// </para>
    /// </remarks>
    public const string ReversalCriterion =
        "Conseil d'Etat opinion text stays link-only. Reversal requires BOTH: a published open "
        + "licence or written reuse statement covering the opinion documents - a licence triple on "
        + "the JOLux manifestation, or a reuse statement on conseil-etat.public.lu - AND robots "
        + "permission for the document path. The robots half is satisfied on conseil-etat.public.lu "
        + "for bare /content/dam/ paths and is unstated on wdocs-pub.chd.lu; the licence half is "
        + "unsatisfied on every known host and is the sole blocking condition.";
}

/// <summary>
/// One Conseil d'État opinion, carried as an official locator and its provenance and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE CANNOT CARRY THE OPINION TEXT, AND THAT IS THE POINT. There is no body field, no
/// content field and no door that accepts one. The opinion PDFs carry no licence triple, so their
/// text is never re-served; the accepted disposition for this class is <c>point</c> — "expose the
/// official identity or locator and typed limitation without holding or claiming the selected
/// payload". Making the text unrepresentable is stronger than declining to fetch it, because a later
/// caller cannot put it here even by mistake.
/// </para>
/// <para>
/// THE DISPOSITION IS FIXED, NOT A PARAMETER. <see cref="Disposition"/> is always
/// <see cref="LuScopeTerminalState.Point"/>. It reuses the closed four-member axis vocabulary rather
/// than minting a fifth name for "link-only", because that vocabulary already closes this axis and a
/// parallel name would be an invention that reads like a decision.
/// </para>
/// <para>
/// THE LOCATOR MUST BE ONE A READER MAY FOLLOW. A locator carrying a query string or fragment is
/// refused, because <c>conseil-etat.public.lu</c>'s robots policy disallows every query shape for
/// <c>User-agent: *</c> while permitting bare paths. That is measured from the live policy, not
/// assumed, and it is the half of the reversal criterion that is already satisfied.
/// </para>
/// <para>
/// WHAT IS NOT DECIDED HERE. Whether opinions may ever be ingested is not this type's question; it
/// is answered by <see cref="LuxembourgOpinionLinkOnlyVocabulary.ReversalCriterion"/>, whose licence
/// half is unsatisfied on every known host. This type records the locator so that the day the
/// licence appears, the decision can be revisited against a stated condition rather than rediscovered.
/// </para>
/// </remarks>
public sealed class LuxembourgOpinionLinkOnlyRecord
{
    private LuxembourgOpinionLinkOnlyRecord(
        string opinionIri,
        string documentLocator,
        string documentHost,
        string rawOpinionDateLexical,
        string opinionDateDatatypeIri,
        DatePrecision opinionDatePrecision,
        string sourceObservationId)
    {
        OpinionIri = opinionIri;
        DocumentLocator = documentLocator;
        DocumentHost = documentHost;
        RawOpinionDateLexical = rawOpinionDateLexical;
        OpinionDateDatatypeIri = opinionDateDatatypeIri;
        OpinionDatePrecision = opinionDatePrecision;
        SourceObservationId = sourceObservationId;
    }

    /// <summary>The JOLux opinion node's own IRI.</summary>
    public string OpinionIri { get; }

    /// <summary>The official document locator, exactly as the publisher stated it.</summary>
    public string DocumentLocator { get; }

    /// <summary>The locator's host, read from the locator rather than accepted as a parameter.</summary>
    public string DocumentHost { get; }

    /// <summary>The opinion's date exactly as the publisher wrote it.</summary>
    public string RawOpinionDateLexical { get; }

    /// <summary>The datatype IRI that date literal carried.</summary>
    public string OpinionDateDatatypeIri { get; }

    /// <summary>The precision that datatype implies. Never widened, never guessed.</summary>
    public DatePrecision OpinionDatePrecision { get; }

    /// <summary>The custody coordinate these terms were read from.</summary>
    public string SourceObservationId { get; }

    /// <summary>
    /// Always <see cref="LuScopeTerminalState.Point"/>. See the type remarks for why this is a
    /// property of the type rather than a value a caller supplies.
    /// </summary>
    public static LuScopeTerminalState Disposition => LuScopeTerminalState.Point;

    /// <summary>The only door that mints one. Refuses with a typed reason rather than partially.</summary>
    public static LuxembourgOpinionLinkOnlyRecord? TryCreate(
        string? opinionIri,
        string? documentLocator,
        string? rawOpinionDateLexical,
        string? opinionDateDatatypeIri,
        string sourceObservationId,
        out LuxembourgOpinionLocatorRefusal refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObservationId);
        refusal = LuxembourgOpinionLocatorRefusal.None;

        if (!IsAbsoluteHttpIri(opinionIri))
        {
            refusal = LuxembourgOpinionLocatorRefusal.OpinionNotAnIri;
            return null;
        }

        if (!IsAbsoluteHttpIri(documentLocator) ||
            !Uri.TryCreate(documentLocator, UriKind.Absolute, out var locator))
        {
            refusal = LuxembourgOpinionLocatorRefusal.DocumentLocatorNotAnAbsoluteHttpUri;
            return null;
        }

        if (!string.IsNullOrEmpty(locator.Query) || !string.IsNullOrEmpty(locator.Fragment))
        {
            refusal = LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotRobotsPermitted;
            return null;
        }

        if (string.IsNullOrEmpty(rawOpinionDateLexical))
        {
            refusal = LuxembourgOpinionLocatorRefusal.OpinionDateMissingOrNotALiteral;
            return null;
        }

        if (!TryPrecision(opinionDateDatatypeIri, out var precision))
        {
            refusal = LuxembourgOpinionLocatorRefusal.OpinionDateNotADateShape;
            return null;
        }

        if (!PublisherDate.IsValidLexicalValue(rawOpinionDateLexical!, precision))
        {
            refusal = LuxembourgOpinionLocatorRefusal.OpinionDateNotValidAtItsPrecision;
            return null;
        }

        return new LuxembourgOpinionLinkOnlyRecord(
            opinionIri!,
            documentLocator!,
            locator.Host,
            rawOpinionDateLexical!,
            opinionDateDatatypeIri!,
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
