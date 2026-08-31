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
        OfficialIdentity subject,
        PublisherDate date,
        string sourcePredicateUri,
        QualifiedAxiom axiom,
        string? rawQualifier,
        string? publisherComment,
        DateSemanticRole semanticRole,
        string parsedByAuthority,
        SourceObservationReference observation)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The publisher date fact schema must be version 1.", nameof(schema));
        }

        if (!FactsValidation.IsAbsoluteUri(sourcePredicateUri))
        {
            throw new ArgumentException(
                "A date fact must carry the publisher predicate as an absolute URI.",
                nameof(sourcePredicateUri));
        }

        if (!FactsValidation.IsOpaqueIdentity(parsedByAuthority))
        {
            throw new ArgumentException(
                "A date fact must name the authority that produced its parsed reading.",
                nameof(parsedByAuthority));
        }

        Schema = schema;
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Date = date ?? throw new ArgumentNullException(nameof(date));
        SourcePredicateUri = sourcePredicateUri;
        Axiom = axiom ?? throw new ArgumentNullException(nameof(axiom));
        RawQualifier = rawQualifier;
        PublisherComment = publisherComment;
        SemanticRole = semanticRole;
        ParsedByAuthority = parsedByAuthority;
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
    }

    public string Schema { get; }

    public OfficialIdentity Subject { get; }

    public PublisherDate Date { get; }

    public string SourcePredicateUri { get; }

    public QualifiedAxiom Axiom { get; }

    /// <summary>The publisher's own qualifier text, unparsed.</summary>
    public string? RawQualifier { get; }

    /// <summary>The publisher's own comment on this date, unparsed.</summary>
    public string? PublisherComment { get; }

    public DateSemanticRole SemanticRole { get; }

    /// <summary>Who produced the reading in <see cref="SemanticRole"/>.</summary>
    public string ParsedByAuthority { get; }

    public SourceObservationReference Observation { get; }
}
