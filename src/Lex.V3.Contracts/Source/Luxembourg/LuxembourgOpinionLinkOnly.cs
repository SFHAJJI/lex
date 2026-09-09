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

    /// <summary>
    /// The locator is not in one of the official host and path families actually observed.
    /// </summary>
    /// <remarks>
    /// An earlier version of this door accepted any absolute bare HTTP(S) URI and stamped it with
    /// the link-only disposition. That projected one host's measured robots result onto every host
    /// on the internet: <c>https://example.com/opinion.pdf</c> would have been carried as an
    /// official Conseil d'État locator with <c>example.com</c> as its host. Robots evidence is per
    /// host and per path, so admission is too.
    /// </remarks>
    DocumentLocatorIsNotAnAdmittedOfficialFamily = 7,
}

/// <summary>What a host's own robots policy says about the family a locator belongs to.</summary>
/// <remarks>
/// A typed state rather than a boolean, because the observed families are not in the same position
/// and flattening them would be the same projection this vocabulary exists to prevent. "No stated
/// policy" is not permission granted in writing, and a reader deciding whether to follow a link
/// deserves to see which of the two they have.
/// </remarks>
public enum LuxembourgOpinionHostRobotsState
{
    /// <summary>The host publishes a policy and this family's paths are not disallowed by it.</summary>
    PermittedByStatedPolicy = 1,

    /// <summary>The host publishes no policy at all, which is a different fact from permission.</summary>
    NoStatedPolicy = 2,
}

/// <summary>
/// One official origin and path family the opinion documents were observed on, with that origin's
/// own robots position.
/// </summary>
/// <remarks>
/// <para>
/// <b>The origin is scheme, host and port together, never the host alone.</b> A robots policy and a
/// delivery observation are properties of the origin that answered them. <c>http://</c> is a
/// different origin from <c>https://</c> and can reach a different service; so can an arbitrary
/// port. Every probe recorded on <see cref="LuxembourgOpinionLinkOnlyVocabulary.AdmittedHostFamilies"/>
/// was made over <c>https</c> on the default port, so that is exactly what is admitted.
/// </para>
/// <para>
/// This is the second repair of this table and both had the same cause: a measurement generalised
/// past what was measured. The first admitted any bare absolute HTTP(S) URI, projecting
/// conseil-etat's robots result onto every host on the internet. The second bound host and path, so
/// <c>http://conseil-etat.public.lu/content/dam/...</c> and
/// <c>https://conseil-etat.public.lu:444/content/dam/...</c> still passed under a permission neither
/// origin had granted. Both were found in review rather than here.
/// </para>
/// </remarks>
public sealed record LuxembourgOpinionHostFamily(
    string Scheme,
    string Host,
    int Port,
    string PathPrefix,
    LuxembourgOpinionHostRobotsState RobotsState);

/// <summary>
/// The closed part of the Conseil d'État opinion surface: the proven JOLux access path, the measured
/// licence position, and the criterion that would reverse the link-only decision.
/// </summary>
public static class LuxembourgOpinionLinkOnlyVocabulary
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";

    /// <summary>The port every recorded probe was made on. Named so the table states an origin.</summary>
    private const int HttpsDefaultPort = 443;

    /// <summary>The JOLux class of an opinion event. 13,009 observed.</summary>
    public const string OpinionConseilEtatClassIri = Jolux + "OpinionConseilEtat";

    /// <summary>The draft-to-opinion edge. 36,169 opinion events observed across its families.</summary>
    public const string HasOpinionPredicateIri = Jolux + "hasOpinion";

    /// <summary>The opinion-to-document edge. 6,393 observed.</summary>
    public const string HasResultingOpinionDocumentPredicateIri = Jolux + "hasResultingOpinionDocument";

    /// <summary>The opinion's own date. 9,144 observed.</summary>
    public const string OpinionDatePredicateIri = Jolux + "opinionDate";

    /// <summary>
    /// The official origin and path families the opinion documents were observed on, each carrying
    /// that origin's own robots position. Closed: a locator outside these is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every entry is a measurement rather than an expectation, and the three are deliberately not
    /// flattened into one rule:
    /// </para>
    /// <para>
    /// <c>conseil-etat.public.lu</c> publishes a policy whose <c>User-agent: *</c> block disallows
    /// only query-string shapes — <c>Disallow: /*?*</c> with one narrow <c>Allow: /*?b=*</c> — so a
    /// bare <c>/content/dam/</c> path is permitted. A `HEAD` of the proven locator returned `200
    /// application/pdf`.
    /// </para>
    /// <para>
    /// <c>legilux.public.lu</c> publishes a policy that disallows an explicit list for
    /// <c>User-agent: *</c> — <c>/publications-regroupees</c>, <c>/eli/etat/adm/</c>, <c>/search</c>,
    /// <c>/reg_ue/</c>, <c>/dir_ue/</c>, <c>*.svg</c>, <c>*.docx</c> and named documents.
    /// <c>/filestore/</c> is not among them, so it is permitted. That host refuses `HEAD` with 403
    /// while serving a range GET, which is a delivery quirk rather than a robots position.
    /// </para>
    /// <para>
    /// <c>wdocs-pub.chd.lu</c> answers <c>404</c> for <c>robots.txt</c>. It states no policy, which
    /// is recorded as exactly that. The pack's note that "robots.txt is an allow-list" describes
    /// <c>www.chd.lu</c>, a different host from the one serving the documents.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<LuxembourgOpinionHostFamily> AdmittedHostFamilies { get; } =
        Array.AsReadOnly(new[]
        {
            new LuxembourgOpinionHostFamily(
                "https", "conseil-etat.public.lu", HttpsDefaultPort, "/content/dam/",
                LuxembourgOpinionHostRobotsState.PermittedByStatedPolicy),
            new LuxembourgOpinionHostFamily(
                "https", "legilux.public.lu", HttpsDefaultPort, "/filestore/",
                LuxembourgOpinionHostRobotsState.PermittedByStatedPolicy),
            new LuxembourgOpinionHostFamily(
                "https", "wdocs-pub.chd.lu", HttpsDefaultPort, "/docs/",
                LuxembourgOpinionHostRobotsState.NoStatedPolicy),
        });

    /// <summary>The admitted family a locator belongs to, or null when it belongs to none.</summary>
    /// <remarks>
    /// All four parts are checked because all four are what was measured. <see cref="Uri.Scheme"/>
    /// and <see cref="Uri.Host"/> arrive normalised, and <see cref="Uri.Port"/> is the effective
    /// port, so <c>https://host/x</c> and <c>https://host:443/x</c> are the same origin and both
    /// admitted, while <c>http://host/x</c> and <c>https://host:444/x</c> are different origins and
    /// both refused.
    /// </remarks>
    public static LuxembourgOpinionHostFamily? FamilyFor(Uri locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        foreach (var family in AdmittedHostFamilies)
        {
            if (string.Equals(locator.Scheme, family.Scheme, StringComparison.Ordinal) &&
                string.Equals(locator.Host, family.Host, StringComparison.OrdinalIgnoreCase) &&
                locator.Port == family.Port &&
                locator.AbsolutePath.StartsWith(family.PathPrefix, StringComparison.Ordinal))
            {
                return family;
            }
        }

        return null;
    }

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
        LuxembourgOpinionHostFamily documentFamily,
        string rawOpinionDateLexical,
        string opinionDateDatatypeIri,
        DatePrecision opinionDatePrecision,
        string sourceObservationId)
    {
        OpinionIri = opinionIri;
        DocumentLocator = documentLocator;
        DocumentFamily = documentFamily;
        RawOpinionDateLexical = rawOpinionDateLexical;
        OpinionDateDatatypeIri = opinionDateDatatypeIri;
        OpinionDatePrecision = opinionDatePrecision;
        SourceObservationId = sourceObservationId;
    }

    /// <summary>The JOLux opinion node's own IRI.</summary>
    public string OpinionIri { get; }

    /// <summary>The official document locator, exactly as the publisher stated it.</summary>
    public string DocumentLocator { get; }

    /// <summary>
    /// The admitted official family this locator belongs to, carrying that host's own robots
    /// position. Resolved from the locator rather than accepted as a parameter, so a caller cannot
    /// assert a permission the host never gave.
    /// </summary>
    public LuxembourgOpinionHostFamily DocumentFamily { get; }

    /// <summary>The locator's host, read from its admitted family.</summary>
    public string DocumentHost => DocumentFamily.Host;

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

        // Admission is per origin and per path, because the robots evidence is. Accepting any bare
        // absolute URI here would carry an arbitrary host under a permission only conseil-etat
        // actually granted; accepting any scheme or port on an admitted host would carry a
        // different service under the same permission, since the probes were https on 443.
        var family = LuxembourgOpinionLinkOnlyVocabulary.FamilyFor(locator);
        if (family is null)
        {
            refusal = LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotAnAdmittedOfficialFamily;
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
            family,
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
