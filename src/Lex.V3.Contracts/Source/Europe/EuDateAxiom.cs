namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The closed semantic role of one EU <c>owl:Axiom</c>-qualified date. Stage 2 item E1, ledger row
/// <c>SRC-013</c>.
/// </summary>
/// <remarks>
/// <para>
/// Minted only from the NAL <c>fd_335</c> date-qualifier tokens
/// <c>review/23-research-temporal.md</c> section 3 actually records as observed on GDPR
/// <c>owl:Axiom</c> reifications: <c>EV</c> ("Entry into force"), <c>MA</c> ("Application") and
/// <c>AU+TARD</c> ("At the latest", qualifying <c>resource_legal_date_deadline</c>). Plus
/// <see cref="EndOfValidity"/>, which review/23 evidences by predicate and open sentinel rather
/// than by a qualifier example.
/// </para>
/// <para>
/// No member exists here that the measured inventory does not show. A signature-date role named
/// in this lane's own ledger row title ("EU typed EV, MA, end-of-validity, signature and publisher
/// deadline axioms") is deliberately absent: review/23 section 3 names
/// <c>resource_legal_date_signature</c> only as a bare CDM property in its property list, and no
/// <c>owl:Axiom</c> qualifier example for it appears anywhere in that document. The coordinator's
/// scope ruling on this lane (event
/// <c>lex-event-20260904T010504388Z-80d5ddb2fd6148139c2b56bdcfd73fcd</c>) struck it for exactly
/// that reason: "no enum member may exist that the measured inventory ... does not show." It is
/// not restored here; a future lane with an actual observed signature qualifier adds it then.
/// </para>
/// <para>
/// <see cref="Lex.V3.Contracts.Facts.DateSemanticRole"/> is an adjacent, earlier, cross-publisher
/// vocabulary that already carries a <c>SignatureDate</c> member and folds an evidence-less
/// transposition attempt into a plain <c>PublisherDeadline</c> with no separate named outcome for
/// the attempt itself. This type does not extend, wrap or correct that one. The two are disjoint
/// by the same scope ruling that produced this lane ("new files only ... disjoint from item 15,
/// item 17, and the adapters"); reconciling them is a decision for whoever owns that shared layer.
/// </para>
/// </remarks>
public enum EuDateAxiomRole
{
    /// <summary>
    /// No fd_335 qualifier axiom was present, or its raw token is outside the pinned set this
    /// fixture set records. Never assigned from the order two dates appear in, from which
    /// predicate looks similar, or from any other positional signal: only
    /// <see cref="EuDateAxiomRow.RawQualifierCode"/> and, for <see cref="EndOfValidity"/> alone,
    /// <see cref="EuDateAxiomRow.SourcePredicateUri"/> ever select a role.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// fd_335 "EV". GDPR: <c>resource_legal_date_entry-into-force 2016-05-24</c>, comment
    /// "DATPUB +20 V ART 99".
    /// </summary>
    EntryIntoForce,

    /// <summary>
    /// fd_335 "MA". GDPR: <c>resource_legal_date_entry-into-force 2018-05-25</c>, the same
    /// multi-valued predicate as <see cref="EntryIntoForce"/>, distinguished only by this
    /// qualifier.
    /// </summary>
    Application,

    /// <summary>
    /// fd_335 "AU+TARD". GDPR: <c>resource_legal_date_deadline 2020-05-25</c>, "ART 97". A row in
    /// this role is a plain publisher deadline. Whether it is also a transposition deadline is a
    /// separate, derived question -- see <see cref="EuTranspositionDeadlineClassification"/> --
    /// never answered by this role alone.
    /// </summary>
    Deadline,

    /// <summary>
    /// <c>resource_legal_date_end-of-validity</c>. review/23 records no owl:Axiom fd_335 qualifier
    /// example for this predicate, so this role is evidenced by the source predicate identity
    /// itself (and, when open, by the pinned <c>9999-12-31</c> sentinel), never by a qualifier
    /// token. A row cannot reach this role while also carrying a recognized qualifier code.
    /// </summary>
    EndOfValidity,
}

/// <summary>
/// Whether a date's raw lexical value is the EUR-Lex open-end sentinel. Typed rather than a bare
/// bool: a value that merely resembles the pinned literal must not collapse silently into "not
/// open" (nor into "open"), so it lands on <see cref="Unresolved"/> instead of a default.
/// </summary>
public enum EuDateOpenSentinelState
{
    /// <summary>The raw lexical value's date part is not the pinned literal <c>9999-12-31</c>.</summary>
    Closed,

    /// <summary>
    /// The raw lexical value's date part is exactly <c>9999-12-31</c>, followed by nothing, "Z",
    /// or a well formed signed XSD timezone offset.
    /// </summary>
    OpenSentinel,

    /// <summary>
    /// No raw lexical value was given, or its date part is exactly <c>9999-12-31</c> but the
    /// trailing text is not a well formed XSD timezone suffix. Neither "the sentinel" nor "an
    /// ordinary date": this state exists so such a value is never defaulted into
    /// <see cref="Closed"/>.
    /// </summary>
    Unresolved,
}

/// <summary>The precision actually present in a EU date's raw lexical value.</summary>
public enum EuDatePrecision
{
    Year,
    YearMonth,
    YearMonthDay,
}

/// <summary>
/// The pinned NAL scheme and the exact predicate/label pairing review/23 records for each
/// evidenced fd_335 token. <see cref="EuDateAxiomRow"/> classifies against this table and this
/// table alone.
/// </summary>
public static class EuDateQualifierVocabulary
{
    /// <summary>The NAL scheme every pinned token below is drawn from.</summary>
    public const string SchemeIdentity = "fd_335";

    private const string Cdm = "http://publications.europa.eu/ontology/cdm#";

    /// <summary>
    /// The one multi-valued predicate review/23 shows carrying both the EV and MA qualifiers.
    /// </summary>
    public const string EntryIntoForceAndApplicationPredicateUri =
        Cdm + "resource_legal_date_entry-into-force";

    /// <summary>The predicate review/23 shows carrying the AU+TARD qualifier.</summary>
    public const string DeadlinePredicateUri = Cdm + "resource_legal_date_deadline";

    /// <summary>
    /// The predicate review/23 shows carrying the <c>9999-12-31</c> open sentinel, with no
    /// observed qualifier example.
    /// </summary>
    public const string EndOfValidityPredicateUri = Cdm + "resource_legal_date_end-of-validity";

    /// <summary>One pinned fd_335 token's expected predicate, label and role, together.</summary>
    internal sealed record Pin(string PredicateUri, string Label, EuDateAxiomRole Role);

    /// <summary>
    /// The exactly three fd_335 tokens review/23 evidences. Adding a fourth here without an
    /// observed <c>owl:Axiom</c> example in review/23 repeats the mistake this lane's scope ruling
    /// corrected for the signature role.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, Pin> PinnedQualifiers =
        new Dictionary<string, Pin>(StringComparer.Ordinal)
        {
            ["EV"] = new Pin(
                EntryIntoForceAndApplicationPredicateUri, "Entry into force", EuDateAxiomRole.EntryIntoForce),
            ["MA"] = new Pin(
                EntryIntoForceAndApplicationPredicateUri, "Application", EuDateAxiomRole.Application),
            ["AU+TARD"] = new Pin(
                DeadlinePredicateUri, "At the latest", EuDateAxiomRole.Deadline),
        };
}

/// <summary>
/// One EU date exactly as R4 requires it be kept: the publisher's raw lexical value, RDF
/// datatype, precision, source predicate, axiom reference, raw qualifier, parsed authority
/// identity, publisher comment, semantic role and open-sentinel state, together, with a role that
/// can never be guessed from date order or from field position.
/// </summary>
/// <remarks>
/// <para>
/// Candidate 5 R4: "Every date retains raw lexical value, RDF datatype, precision, source
/// predicate, axiom, raw qualifier, parsed authority identity, publisher comment, semantic role,
/// and open-sentinel state. Unknown or missing qualifiers remain typed unknown. Date order never
/// supplies a role." This type carries exactly that list as named fields, and its constructor is
/// the only path that can produce one: <see cref="Role"/> is never a caller-supplied parameter,
/// only ever computed here from <see cref="RawQualifierCode"/> and, for the one predicate-evidenced
/// exception, <see cref="SourcePredicateUri"/>. There is no parameter carrying a date's position
/// relative to any other date, so nothing here could read one even if it wanted to.
/// </para>
/// <para>
/// A contract-only slice: no live SPARQL call, no adapter, no wire schema version. Fixtures are
/// hand built from review/23's own quoted shapes.
/// </para>
/// </remarks>
public sealed class EuDateAxiomRow
{
    private const string SentinelDatePart = "9999-12-31";

    private static readonly IReadOnlyDictionary<string, EuDatePrecision> PrecisionByDatatype =
        new Dictionary<string, EuDatePrecision>(StringComparer.Ordinal)
        {
            ["http://www.w3.org/2001/XMLSchema#gYear"] = EuDatePrecision.Year,
            ["http://www.w3.org/2001/XMLSchema#gYearMonth"] = EuDatePrecision.YearMonth,
            ["http://www.w3.org/2001/XMLSchema#date"] = EuDatePrecision.YearMonthDay,
        };

    /// <summary>The only path that mints a row.</summary>
    /// <param name="rawLexicalValue">The publisher's date value exactly as served, e.g. "2016-05-24".</param>
    /// <param name="rdfDatatypeUri">One of the three accepted CDM XSD date datatype URIs.</param>
    /// <param name="precision">The precision <paramref name="rdfDatatypeUri"/> expresses. Must agree with it.</param>
    /// <param name="sourcePredicateUri">The exact CDM predicate this date was observed on.</param>
    /// <param name="axiomReference">The reified <c>owl:Axiom</c> statement's own identity or reference.</param>
    /// <param name="rawQualifierCode">The fd_335 token exactly as observed (e.g. "EV", "AU+TARD"), or null.</param>
    /// <param name="parsedAuthorityLabel">The fd_335 label exactly as observed (e.g. "Entry into force"), or null.</param>
    /// <param name="publisherComment">The publisher's <c>comment_on_date</c> text, e.g. "DATPUB +20 V ART 99", or null.</param>
    public EuDateAxiomRow(
        string rawLexicalValue,
        string rdfDatatypeUri,
        EuDatePrecision precision,
        string sourcePredicateUri,
        string axiomReference,
        string? rawQualifierCode,
        string? parsedAuthorityLabel,
        string? publisherComment)
    {
        RawLexicalValue = ContractValidation.RequireIdentifier(rawLexicalValue, nameof(rawLexicalValue));

        var validatedDatatypeUri = ContractValidation.RequireIdentifier(rdfDatatypeUri, nameof(rdfDatatypeUri));
        var validatedPrecision = ContractValidation.RequireDefined(precision, nameof(precision));
        if (!PrecisionByDatatype.TryGetValue(validatedDatatypeUri, out var datatypePrecision) ||
            datatypePrecision != validatedPrecision)
        {
            throw new ArgumentException(
                $"\"{validatedDatatypeUri}\" does not express {validatedPrecision} precision under " +
                "the three accepted CDM XSD date datatypes.",
                nameof(precision));
        }

        RdfDatatypeUri = validatedDatatypeUri;
        Precision = validatedPrecision;
        SourcePredicateUri = ContractValidation.RequireIdentifier(sourcePredicateUri, nameof(sourcePredicateUri));
        AxiomReference = ContractValidation.RequireIdentifier(axiomReference, nameof(axiomReference));

        var qualifier = NormalizeOptional(rawQualifierCode, nameof(rawQualifierCode));
        var label = NormalizeOptional(parsedAuthorityLabel, nameof(parsedAuthorityLabel));

        if (qualifier is not null &&
            EuDateQualifierVocabulary.PinnedQualifiers.TryGetValue(qualifier, out var pin))
        {
            if (!string.Equals(SourcePredicateUri, pin.PredicateUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"fd_335 \"{qualifier}\" is only evidenced on {pin.PredicateUri}, not on " +
                    $"{SourcePredicateUri}.",
                    nameof(sourcePredicateUri));
            }

            if (!string.Equals(label, pin.Label, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"fd_335 \"{qualifier}\" is pinned to the label \"{pin.Label}\" in " +
                    $"{EuDateQualifierVocabulary.SchemeIdentity}; " +
                    (label is null ? "no label was given." : $"\"{label}\" does not match."),
                    nameof(parsedAuthorityLabel));
            }

            Role = pin.Role;
        }
        else if (qualifier is null &&
                 string.Equals(
                     SourcePredicateUri, EuDateQualifierVocabulary.EndOfValidityPredicateUri,
                     StringComparison.Ordinal))
        {
            if (label is not null)
            {
                throw new ArgumentException(
                    "review/23 records no fd_335 qualifier example for end-of-validity; a parsed " +
                    "authority label cannot be asserted for it.",
                    nameof(parsedAuthorityLabel));
            }

            Role = EuDateAxiomRole.EndOfValidity;
        }
        else
        {
            // Absent, or present but outside the pinned set: typed unknown either way, and never
            // reclassified by which predicate it happens to sit on or which date value it holds.
            Role = EuDateAxiomRole.Unknown;
        }

        RawQualifierCode = qualifier;
        ParsedAuthorityLabel = label;
        PublisherComment = NormalizeOptional(publisherComment, nameof(publisherComment));
        OpenSentinelState = ComputeSentinelState(RawLexicalValue);
    }

    public string RawLexicalValue { get; }

    public string RdfDatatypeUri { get; }

    public EuDatePrecision Precision { get; }

    public string SourcePredicateUri { get; }

    /// <summary>The reified <c>owl:Axiom</c> statement's own identity or reference.</summary>
    public string AxiomReference { get; }

    /// <summary>The fd_335 token exactly as observed (e.g. "EV", "AU+TARD"), or null if none.</summary>
    public string? RawQualifierCode { get; }

    /// <summary>The fd_335 label exactly as observed (e.g. "Entry into force"), or null if none.</summary>
    public string? ParsedAuthorityLabel { get; }

    /// <summary>The publisher's <c>comment_on_date</c> text, or null if none.</summary>
    public string? PublisherComment { get; }

    /// <summary>Computed only from <see cref="RawQualifierCode"/> and <see cref="SourcePredicateUri"/>.</summary>
    public EuDateAxiomRole Role { get; }

    public EuDateOpenSentinelState OpenSentinelState { get; }

    private static string? NormalizeOptional(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : ContractValidation.RequireIdentifier(value, parameterName);

    private static EuDateOpenSentinelState ComputeSentinelState(string rawLexicalValue)
    {
        if (string.IsNullOrEmpty(rawLexicalValue))
        {
            return EuDateOpenSentinelState.Unresolved;
        }

        string datePart;
        string suffix;
        if (rawLexicalValue.Length > SentinelDatePart.Length &&
            rawLexicalValue.StartsWith(SentinelDatePart, StringComparison.Ordinal))
        {
            datePart = rawLexicalValue[..SentinelDatePart.Length];
            suffix = rawLexicalValue[SentinelDatePart.Length..];
        }
        else
        {
            datePart = rawLexicalValue;
            suffix = string.Empty;
        }

        if (!string.Equals(datePart, SentinelDatePart, StringComparison.Ordinal))
        {
            return EuDateOpenSentinelState.Closed;
        }

        return IsWellFormedXsdTimezoneSuffix(suffix)
            ? EuDateOpenSentinelState.OpenSentinel
            : EuDateOpenSentinelState.Unresolved;
    }

    /// <summary>Empty, "Z", or a signed hh:mm offset within the XSD timezone range.</summary>
    private static bool IsWellFormedXsdTimezoneSuffix(string suffix)
    {
        if (suffix.Length == 0 || suffix == "Z")
        {
            return true;
        }

        if (suffix.Length != 6 || suffix[0] is not ('+' or '-') || suffix[3] != ':')
        {
            return false;
        }

        for (var index = 1; index < 6; index++)
        {
            if (index == 3)
            {
                continue;
            }

            if (suffix[index] is < '0' or > '9')
            {
                return false;
            }
        }

        var hours = int.Parse(suffix.Substring(1, 2));
        var minutes = int.Parse(suffix.Substring(4, 2));
        return hours <= 14 && minutes <= 59 && (hours < 14 || minutes == 0);
    }
}

/// <summary>
/// The outcome of asking whether one <see cref="EuDateAxiomRow"/> is a transposition deadline.
/// </summary>
/// <remarks>
/// Candidate 5 R4: "<c>transposition_deadline</c> requires directive-specific publisher evidence."
/// This is a derived classification, never a publisher token: a row's own <see cref="EuDateAxiomRole"/>
/// never contains a transposition-deadline member, because the axiom alone never carries that
/// evidence. The coordinator's scope ruling on this lane additionally requires that a Deadline-role
/// row with no such evidence produce its own named, typed outcome for the attempt, rather than
/// silently reporting nothing beyond the row's already-plain <see cref="EuDateAxiomRole.Deadline"/>.
/// </remarks>
public enum EuTranspositionDeadlineOutcome
{
    /// <summary>The row's own role is not <see cref="EuDateAxiomRole.Deadline"/>; the question does not apply.</summary>
    NotADeadline,

    /// <summary>
    /// A Deadline-role row was presented with no directive-specific evidence. The row itself
    /// remains a plain <see cref="EuDateAxiomRole.Deadline"/>; this outcome is the typed record
    /// that the stronger reading was attempted and refused, not silence.
    /// </summary>
    TranspositionDeadlineEvidenceInsufficient,

    /// <summary>
    /// A Deadline-role row carrying directive-specific evidence. Accepted as a transposition
    /// deadline, derived from the underlying row and the named directive.
    /// </summary>
    AcceptedTranspositionDeadline,
}

/// <summary>
/// Directive-specific publisher evidence tying one Deadline-role axiom to a Member State's
/// transposition obligation for that directive. Named so the promotion in
/// <see cref="EuTranspositionDeadlineClassification"/> can be checked against a fact rather than
/// trusted on assertion alone.
/// </summary>
public sealed class EuDirectiveTranspositionEvidence
{
    public EuDirectiveTranspositionEvidence(string directiveIdentity)
    {
        DirectiveIdentity = ContractValidation.RequireIdentifier(directiveIdentity, nameof(directiveIdentity));
    }

    /// <summary>
    /// The directive this deadline transposes, by its own official identity (e.g. a CELEX
    /// number). Not checked against a live directive register in this contract-only slice.
    /// </summary>
    public string DirectiveIdentity { get; }
}

/// <summary>
/// A derived classification of one Deadline-role <see cref="EuDateAxiomRow"/> as a transposition
/// deadline or not.
/// </summary>
/// <remarks>
/// Mirrors the <c>derived_from</c> pattern R4's own inverse-relation rule already uses elsewhere
/// in this codebase (<see cref="Lex.V3.Contracts.Facts.DerivedInverseRelation.DerivedFrom"/>,
/// <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgLocalInboundView.DerivedFrom</c>): the result
/// names exactly what it was derived from rather than standing on its own as a fresh fact.
/// </remarks>
public sealed class EuTranspositionDeadlineClassification
{
    private EuTranspositionDeadlineClassification(
        EuTranspositionDeadlineOutcome outcome,
        EuDateAxiomRow derivedFrom,
        EuDirectiveTranspositionEvidence? evidence)
    {
        Outcome = outcome;
        DerivedFrom = derivedFrom;
        Evidence = evidence;
    }

    public EuTranspositionDeadlineOutcome Outcome { get; }

    /// <summary>The exact row this classification was derived from.</summary>
    public EuDateAxiomRow DerivedFrom { get; }

    /// <summary>
    /// The directive evidence that justified <see cref="EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline"/>,
    /// or null for either other outcome.
    /// </summary>
    public EuDirectiveTranspositionEvidence? Evidence { get; }

    /// <summary>True only for <see cref="EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline"/>.</summary>
    public bool IsAcceptedTranspositionDeadline =>
        Outcome == EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline;

    /// <summary>The only path that classifies a row.</summary>
    public static EuTranspositionDeadlineClassification Classify(
        EuDateAxiomRow row,
        EuDirectiveTranspositionEvidence? evidence)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Role != EuDateAxiomRole.Deadline)
        {
            if (evidence is not null)
            {
                throw new ArgumentException(
                    "Directive evidence can only accompany a Deadline-role row.",
                    nameof(evidence));
            }

            return new EuTranspositionDeadlineClassification(
                EuTranspositionDeadlineOutcome.NotADeadline, row, null);
        }

        return evidence is null
            ? new EuTranspositionDeadlineClassification(
                EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, row, null)
            : new EuTranspositionDeadlineClassification(
                EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline, row, evidence);
    }
}
