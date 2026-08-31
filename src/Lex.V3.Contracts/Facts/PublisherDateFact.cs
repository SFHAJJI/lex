using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// A publisher date together with everything the publisher attached to it and everything we
/// inferred, kept separate from each other.
/// </summary>
/// <remarks>
/// <para>
/// The division is the point. <see cref="SourcePredicateUri"/>, <see cref="Axiom"/>,
/// <see cref="RawQualifier"/> and <see cref="PublisherComment"/> are the publisher's. The
/// <see cref="SemanticRole"/> is a reading, and <see cref="ParsedByAuthority"/> names who made
/// it, so no interpretation is ever anonymous.
/// </para>
/// <para>
/// A role is never inferred from the order dates appear in a document. Where the publisher's
/// vocabulary does not pin the role down, the role is
/// <see cref="DateSemanticRole.RoleNotStatedByPublisher"/> and stays that way.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublisherDateFact
{
    public const string Identity = FactsSchemaIds.PublisherDateFact;

    [JsonConstructor]
    public PublisherDateFact(
        string schema,
        OfficialIdentitySet subject,
        PublisherDate date,
        string sourcePredicateUri,
        QualifiedAxiom axiom,
        string? rawQualifier,
        string? publisherComment,
        DateSemanticRole semanticRole,
        TranspositionEvidence transpositionEvidence,
        string parsedByAuthority,
        string sourceObservationId)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The publisher date fact schema must be version 1.", nameof(schema));
        }

        FactsValidation.RequireDefined(semanticRole, nameof(semanticRole));
        FactsValidation.RequireDefined(transpositionEvidence, nameof(transpositionEvidence));

        if (!FactsValidation.IsAbsoluteUri(sourcePredicateUri))
        {
            throw new ArgumentException(
                "A date fact must carry the publisher predicate as an absolute URI.",
                nameof(sourcePredicateUri));
        }

        // The parsing authority must be resolvable by a reader who wants to know who made the
        // reading, so the scheme is frozen to https rather than left to UriKind.Absolute, which
        // also admits mailto:, urn: and file: and would let an authority be unresolvable, local
        // to one machine, or not an address at all.
        if (!FactsValidation.IsHttpsUri(parsedByAuthority))
        {
            throw new ArgumentException(
                "A date fact must name the parsing authority as an absolute https URI.",
                nameof(parsedByAuthority));
        }

        // A generic publisher deadline is not a transposition deadline. It becomes one only on
        // directive-specific qualifier or NIM evidence, and that evidence travels with the fact so
        // a reader can see why the stronger reading was taken.
        if (semanticRole == DateSemanticRole.TranspositionDeadline &&
            transpositionEvidence == TranspositionEvidence.None)
        {
            throw new ArgumentException(
                "A transposition deadline requires directive-qualifier or NIM evidence.",
                nameof(transpositionEvidence));
        }

        // The converse, and Candidate 3 stopped one role short of it. Evidence present must mean
        // the role IS a transposition deadline, not merely that it is some deadline: a
        // publisher_deadline carrying directive-qualifier evidence is a date that says "here is
        // the proof this is a transposition deadline" while declaring it is not one.
        //
        //   publisher_deadline      <=> evidence == none
        //   transposition_deadline  <=> evidence in { directive_qualifier, nim_record }
        if (transpositionEvidence != TranspositionEvidence.None &&
            semanticRole != DateSemanticRole.TranspositionDeadline)
        {
            throw new ArgumentException(
                $"Transposition evidence cannot accompany the {semanticRole} role.",
                nameof(transpositionEvidence));
        }

        ArgumentNullException.ThrowIfNull(date);

        // An open end is a statement that validity has no end. Attaching it to a document date
        // or a publication date would be a claim the publisher never made and that no calendar
        // could satisfy.
        if (date.OpenSentinel == DateOpenSentinel.OpenEnded &&
            semanticRole is not (DateSemanticRole.EndOfValidity or
                DateSemanticRole.RoleNotStatedByPublisher))
        {
            throw new ArgumentException(
                $"The open-end sentinel cannot carry the {semanticRole} role.",
                nameof(semanticRole));
        }

        Schema = schema;
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Date = date;
        SourcePredicateUri = sourcePredicateUri;
        Axiom = axiom ?? throw new ArgumentNullException(nameof(axiom));
        RawQualifier = rawQualifier;
        PublisherComment = publisherComment;
        SemanticRole = semanticRole;
        TranspositionEvidence = transpositionEvidence;
        ParsedByAuthority = parsedByAuthority;
        SourceObservationId = SourceObservation.Require(
            sourceObservationId, nameof(sourceObservationId));
    }

    public string Schema { get; }

    public OfficialIdentitySet Subject { get; }

    public PublisherDate Date { get; }

    public string SourcePredicateUri { get; }

    public QualifiedAxiom Axiom { get; }

    /// <summary>The publisher's own qualifier text, unparsed.</summary>
    public string? RawQualifier { get; }

    /// <summary>The publisher's own comment on this date, unparsed.</summary>
    public string? PublisherComment { get; }

    public DateSemanticRole SemanticRole { get; }

    /// <summary>
    /// What justified reading a deadline as a transposition deadline, or
    /// <see cref="TranspositionEvidence.None"/>.
    /// </summary>
    public TranspositionEvidence TranspositionEvidence { get; }

    /// <summary>Who produced the reading in <see cref="SemanticRole"/>.</summary>
    public string ParsedByAuthority { get; }

    public string SourceObservationId { get; }
}
