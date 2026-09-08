using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>Why a delivered family-A axiom could not become an <see cref="EuDateAxiomBinding"/>.</summary>
/// <remarks>
/// Closed, and deliberately carries no wire tokens, matching its sibling
/// <see cref="EuCellarObjectDecodeRefusal"/>: a vocabulary where some members are declared and
/// others are not is the half-declared shape this repository's census guards refuse.
/// </remarks>
public enum EuReifiedAxiomDecodeRefusal
{
    None = 0,

    /// <summary>The row's axiom column was not an IRI, so the node cannot be referenced.</summary>
    AxiomNodeNotAnIri = 1,

    /// <summary>The axiom named no <c>owl:annotatedSource</c>, or named one that is not an IRI.</summary>
    AnnotatedSourceMissingOrNotAnIri = 2,

    /// <summary>The axiom named no admitted date predicate through <c>owl:annotatedProperty</c>.</summary>
    AnnotatedPropertyMissingOrNotAdmitted = 3,

    /// <summary>The axiom did not declare itself an <c>owl:Axiom</c>.</summary>
    AxiomTypeMissingOrNotOwlAxiom = 4,

    /// <summary>The axiom carried no <c>owl:annotatedTarget</c> literal to read a date from.</summary>
    AnnotatedTargetMissingOrNotALiteral = 5,

    /// <summary>The annotated target literal carried a datatype this reader cannot type a precision from.</summary>
    AnnotatedTargetDatatypeNotADateShape = 6,

    /// <summary>The fd_335 carrier was not the publisher's <c>{CODE|IRI}</c> shape.</summary>
    QualifierTermMalformed = 7,

    /// <summary>The fd_335 carrier's embedded authority IRI disagrees with its own code.</summary>
    QualifierAuthorityDisagreesWithItsCode = 8,

    /// <summary>The accepted binding contract refused this axiom's own combination of terms.</summary>
    BindingRefusedByTheAcceptedContract = 9,
}

/// <summary>
/// Turns family A's delivered rows into the accepted <see cref="EuDateAxiomBinding"/> surface.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS DOOR IS FOR. The E1 contracts were accepted long before anything produced them:
/// <see cref="EuDateAxiomBinding.Create"/> was reachable only from doc comments. Family A now asks
/// the publisher for the reified date axioms; this reads what came back.
/// </para>
/// <para>
/// EVERY PROPERTY THE PUBLISHER SENT IS RETAINED, INCLUDING THE ONES THIS CONTRACT DOES NOT MODEL.
/// The family deliberately asks <c>?axiom ?predicate ?value</c> without naming the properties, so a
/// property added or renamed upstream arrives here rather than vanishing at the query. An arriving
/// property that this decoder does not read by name is not dropped: it is carried verbatim into
/// <see cref="QualifiedAxiom.Qualifiers"/>, which is a list precisely because a publisher may
/// repeat one predicate with different values. That is what S2-A05's "fails closed into typed
/// evidence" requires of an unmodelled term - it becomes evidence, not silence. Two of them,
/// <c>quality_issue</c> and <c>error_message</c>, are the publisher's own doubt about the very date
/// being read, and a reader that discarded them would be asserting a date the publisher flagged.
/// </para>
/// <para>
/// THE LABEL IS OURS AND IS NEVER PRESENTED AS THE PUBLISHER'S. A live probe established that the
/// endpoint supplies no fd_335 label anywhere: the carrier is a literal, so there is no node to
/// follow. <see cref="EuDateAxiomBinding.Create"/> nonetheless demands a label whenever the code is
/// pinned, and it must ordinal-match the pin. So the label is read from
/// <see cref="EuDateQualifierVocabulary.PinnedQualifiers"/> - this codebase's own accepted table -
/// and supplied only for a pinned code. It is a local resolution of a publisher token, and
/// <see cref="ParsedByAuthority"/> is what says so.
/// </para>
/// <para>
/// AN UNPINNED CODE IS NOT A REFUSAL, AND THIS WAS MEASURED. A live run found <c>{PE|...}</c> on a
/// genuine entry-into-force axiom - one in three of the first axioms sampled. Refusing codes
/// outside the pinned three would therefore discard real publisher assertions. The accepted
/// contract already answers this: <c>ComputeRole</c> types an absent or unpinned code as
/// <see cref="DateSemanticRole.RoleNotStatedByPublisher"/> and says so in its own words. This door
/// refuses a qualifier term that is MALFORMED - one that is not the publisher's own
/// <c>{CODE|IRI}</c> shape, or whose embedded authority IRI disagrees with the code beside it -
/// which is a different thing from a code this table has not pinned.
/// </para>
/// <para>
/// A MISSING QUALIFIER IS ORDINARY TRAFFIC. The same live run found the carrier on two of three
/// axioms, so the null-qualifier path is not an edge case: it resolves through the contract's own
/// predicate-derived roles for end-of-validity and signature, and to
/// <see cref="DateSemanticRole.RoleNotStatedByPublisher"/> otherwise.
/// </para>
/// <para>
/// A CONTRACT REFUSAL IS A STATEMENT, NOT A CRASH. <c>Create</c> throws
/// <see cref="ArgumentException"/> for combinations the publisher can genuinely deliver - a pinned
/// code on a predicate it is not evidenced for, for instance. Letting that escape would end a
/// two-hour acquisition with a stack trace instead of a typed disposition, so it is caught and
/// returned as <see cref="EuReifiedAxiomDecodeRefusal.BindingRefusedByTheAcceptedContract"/>.
/// </para>
/// </remarks>
public static class EuReifiedAxiomDecode
{
    /// <summary>
    /// Who performed the reading, which is this codebase and never the publisher.
    /// </summary>
    /// <remarks>
    /// Deliberately non-resolvable, under the reserved <c>.invalid</c> TLD this repository already
    /// uses for the same purpose in <see cref="EuWatermarkWitnessPlan"/>: it identifies a decision
    /// procedure that lives here, not a web resource, and an authority that looked resolvable would
    /// invite a reader to fetch it. The accepted fact contract requires https, so https it is.
    /// </remarks>
    public const string ParsedByAuthority = "https://lex.invalid/authority/eu-date-axiom-decode/1";

    /// <summary>The fd_335 concept-scheme base every qualifier IRI the publisher sends is under.</summary>
    internal const string Fd335ConceptBase =
        "http://publications.europa.eu/resource/authority/fd_335/";

    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdGYearMonth = "http://www.w3.org/2001/XMLSchema#gYearMonth";
    private const string XsdGYear = "http://www.w3.org/2001/XMLSchema#gYear";

    private const string TypeOfDateIri =
        EuAmendmentRelationVocabulary.AnnotationNamespace + "type_of_date";

    private const string CommentOnDateIri =
        EuAmendmentRelationVocabulary.AnnotationNamespace + "comment_on_date";

    /// <summary>
    /// Reads every axiom the family delivered for one work. Returns null on the first refusal,
    /// naming it and the term that caused it, exactly as this repository's other decode doors do.
    /// </summary>
    public static IReadOnlyList<EuDateAxiomBinding>? TryDecode(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        string sourceObservationId,
        out EuReifiedAxiomDecodeRefusal refusal,
        out string? offendingValue)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObservationId);
        refusal = EuReifiedAxiomDecodeRefusal.None;
        offendingValue = null;

        // One row per (axiom, property), so the axiom's whole shape is assembled here rather than
        // assumed to arrive in one row. Absence rows carry no axiom and are the family's typed
        // "this work reifies nothing", which is a delivered fact rather than a decode failure.
        var byAxiom = new Dictionary<string, List<(string Predicate, RepeatedEnumerationRdfTerm Value)>>(
            StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var row in rows)
        {
            var kind = Term(row, profile, "value_kind");
            if (kind.Kind == RepeatedEnumerationRdfTermKind.Literal &&
                string.Equals(kind.Value, "unbound", StringComparison.Ordinal))
            {
                continue;
            }

            var axiom = Term(row, profile, "axiom");
            if (axiom.Kind != RepeatedEnumerationRdfTermKind.Iri || axiom.Value is null)
            {
                refusal = EuReifiedAxiomDecodeRefusal.AxiomNodeNotAnIri;
                offendingValue = axiom.Value;
                return null;
            }

            var predicate = Term(row, profile, "predicate");
            if (predicate.Kind != RepeatedEnumerationRdfTermKind.Iri || predicate.Value is null)
            {
                refusal = EuReifiedAxiomDecodeRefusal.AxiomNodeNotAnIri;
                offendingValue = predicate.Value;
                return null;
            }

            if (!byAxiom.TryGetValue(axiom.Value, out var terms))
            {
                terms = [];
                byAxiom[axiom.Value] = terms;
                order.Add(axiom.Value);
            }

            terms.Add((predicate.Value, Term(row, profile, "value")));
        }

        var bindings = new List<EuDateAxiomBinding>(order.Count);
        foreach (var axiomIri in order)
        {
            var binding = DecodeOne(
                axiomIri, byAxiom[axiomIri], sourceObservationId, out refusal, out offendingValue);
            if (binding is null)
            {
                return null;
            }

            bindings.Add(binding);
        }

        return bindings;
    }

    private static EuDateAxiomBinding? DecodeOne(
        string axiomIri,
        IReadOnlyList<(string Predicate, RepeatedEnumerationRdfTerm Value)> terms,
        string sourceObservationId,
        out EuReifiedAxiomDecodeRefusal refusal,
        out string? offendingValue)
    {
        refusal = EuReifiedAxiomDecodeRefusal.None;
        offendingValue = null;

        var source = Single(terms, EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri);
        if (source is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null })
        {
            refusal = EuReifiedAxiomDecodeRefusal.AnnotatedSourceMissingOrNotAnIri;
            offendingValue = axiomIri;
            return null;
        }

        var property = Single(terms, EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri);
        if (property is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null } ||
            !EuDateQualifierVocabulary.DatePredicateUris.Contains(property.Value, StringComparer.Ordinal))
        {
            refusal = EuReifiedAxiomDecodeRefusal.AnnotatedPropertyMissingOrNotAdmitted;
            offendingValue = property?.Value ?? axiomIri;
            return null;
        }

        // Family A acquires a node on annotatedSource plus an admitted annotatedProperty and does
        // NOT require the declared type, so that a node missing or misstating it is retained rather
        // than erased at the query. This is where that retained evidence is judged.
        var declaredType = Single(terms, EuObjectFactsDiscoveryPlan.RdfTypePredicateIri);
        if (declaredType is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null } ||
            !string.Equals(
                declaredType.Value, EuObjectFactsDiscoveryPlan.OwlAxiomClassIri, StringComparison.Ordinal))
        {
            refusal = EuReifiedAxiomDecodeRefusal.AxiomTypeMissingOrNotOwlAxiom;
            offendingValue = declaredType?.Value ?? axiomIri;
            return null;
        }

        var target = Single(terms, EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri);
        if (target is not { Kind: RepeatedEnumerationRdfTermKind.Literal, Value: not null })
        {
            refusal = EuReifiedAxiomDecodeRefusal.AnnotatedTargetMissingOrNotALiteral;
            offendingValue = axiomIri;
            return null;
        }

        if (!TryPrecision(target.Datatype, out var precision))
        {
            refusal = EuReifiedAxiomDecodeRefusal.AnnotatedTargetDatatypeNotADateShape;
            offendingValue = target.Datatype ?? axiomIri;
            return null;
        }

        string? rawQualifierCode = null;
        var carrier = Single(terms, TypeOfDateIri);
        if (carrier is { Kind: RepeatedEnumerationRdfTermKind.Literal, Value: not null })
        {
            if (!TryParseQualifier(carrier.Value, out rawQualifierCode, out var carrierRefusal))
            {
                refusal = carrierRefusal;
                offendingValue = carrier.Value;
                return null;
            }
        }

        // Ours, from the accepted table, and only for a code that table pins. Never the publisher's.
        var qualifierLabel = rawQualifierCode is not null &&
            EuDateQualifierVocabulary.PinnedQualifiers.TryGetValue(rawQualifierCode, out var pin)
                ? pin.Label
                : null;

        var comment = Single(terms, CommentOnDateIri);

        try
        {
            return EuDateAxiomBinding.Create(
                new OfficialIdentitySet(
                    PublisherId.EuEurLex,
                    [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, source.Value)]),
                target.Value,
                target.Datatype!,
                precision,
                property.Value,
                new QualifiedAxiom(axiomIri, RetainedQualifiers(terms)),
                rawQualifierCode,
                qualifierLabel,
                comment is { Kind: RepeatedEnumerationRdfTermKind.Literal } ? comment.Value : null,
                ParsedByAuthority,
                sourceObservationId);
        }
        catch (ArgumentException exception)
        {
            refusal = EuReifiedAxiomDecodeRefusal.BindingRefusedByTheAcceptedContract;
            offendingValue = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// Every property the publisher sent on this axiom, carried verbatim so an unmodelled or
    /// renamed one becomes evidence rather than silence.
    /// </summary>
    private static IReadOnlyList<AxiomQualifier> RetainedQualifiers(
        IReadOnlyList<(string Predicate, RepeatedEnumerationRdfTerm Value)> terms) =>
        [.. terms
            .Where(static term => term.Value is { Kind: not RepeatedEnumerationRdfTermKind.Unbound, Value: not null })
            .Select(static term => new AxiomQualifier(term.Predicate, term.Value.Value!))];

    /// <summary>
    /// Parses the publisher's own fd_335 carrier shape, <c>{CODE|IRI}</c>.
    /// </summary>
    /// <remarks>
    /// Measured, not assumed: the endpoint returns
    /// <c>{EV|http://publications.europa.eu/resource/authority/fd_335/EV}</c> - the code and its
    /// authority IRI together, brace-delimited and pipe-separated. The accepted contract's own
    /// fixtures carry the bare <c>EV</c>, which is this parse's OUTPUT rather than the wire form.
    /// Both halves are checked against each other, because a carrier whose IRI names a different
    /// concept from its code is not a code this reader can trust either half of.
    /// </remarks>
    internal static bool TryParseQualifier(
        string carrier, out string? code, out EuReifiedAxiomDecodeRefusal refusal)
    {
        code = null;
        refusal = EuReifiedAxiomDecodeRefusal.None;
        if (carrier.Length < 3 || carrier[0] != '{' || carrier[^1] != '}')
        {
            refusal = EuReifiedAxiomDecodeRefusal.QualifierTermMalformed;
            return false;
        }

        var inner = carrier[1..^1];
        var pipe = inner.IndexOf('|');
        if (pipe <= 0 || pipe == inner.Length - 1 ||
            inner.IndexOf('|', pipe + 1) >= 0)
        {
            refusal = EuReifiedAxiomDecodeRefusal.QualifierTermMalformed;
            return false;
        }

        var candidate = inner[..pipe];
        var authority = inner[(pipe + 1)..];
        if (!string.Equals(authority, Fd335ConceptBase + candidate, StringComparison.Ordinal))
        {
            refusal = EuReifiedAxiomDecodeRefusal.QualifierAuthorityDisagreesWithItsCode;
            return false;
        }

        code = candidate;
        return true;
    }

    private static bool TryPrecision(string? datatype, out DatePrecision precision)
    {
        switch (datatype)
        {
            case XsdDate:
                precision = DatePrecision.YearMonthDay;
                return true;
            case XsdGYearMonth:
                precision = DatePrecision.YearMonth;
                return true;
            case XsdGYear:
                precision = DatePrecision.Year;
                return true;
            default:
                precision = DatePrecision.YearMonthDay;
                return false;
        }
    }

    /// <summary>
    /// The one term for a property, or null when the axiom carries none. A property the publisher
    /// repeats is read as its first occurrence here and retained in full by
    /// <see cref="RetainedQualifiers"/>, so a repetition is never silently reduced to one.
    /// </summary>
    private static RepeatedEnumerationRdfTerm? Single(
        IReadOnlyList<(string Predicate, RepeatedEnumerationRdfTerm Value)> terms, string predicate)
    {
        foreach (var term in terms)
        {
            if (string.Equals(term.Predicate, predicate, StringComparison.Ordinal))
            {
                return term.Value;
            }
        }

        return null;
    }

    /// <summary>Looks up one projection variable's term by name, never by a literal index.</summary>
    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string variableName)
    {
        var index = -1;
        for (var candidate = 0; candidate < profile.ProjectionVariables.Count; candidate++)
        {
            if (string.Equals(profile.ProjectionVariables[candidate], variableName, StringComparison.Ordinal))
            {
                index = candidate;
                break;
            }
        }

        if (index < 0 || index >= row.Terms.Count)
        {
            throw new ArgumentException(
                $"'{variableName}' is not readable from this profile's projection.", nameof(variableName));
        }

        return row.Terms[index];
    }
}
