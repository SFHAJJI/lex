using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// The inverse of a publisher assertion, derived only where the publisher's own ontology
/// authorizes that inverse.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AuthorizingOntologyStatementUri"/> and <see cref="InverseOfPredicateUri"/> are both
/// required and there is no constructor that omits them. That is what makes an invented inverse
/// unrepresentable rather than merely discouraged: to state an inverse you must name the
/// publisher statement that permits it, and to fabricate one you would have to fabricate that too.
/// </para>
/// <para>
/// The forward assertion this was derived from travels with it, so a reader can always get back
/// to the edge the publisher actually served.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DerivedInverseRelation
{
    public const string Identity = FactsSchemaIds.DerivedInverseRelation;

    [JsonConstructor]
    public DerivedInverseRelation(
        string schema,
        OfficialIdentity source,
        OfficialIdentity target,
        string predicateUri,
        string inverseOfPredicateUri,
        string authorizingOntologyStatementUri,
        PublisherRelation derivedFrom)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The derived inverse schema must be version 1.", nameof(schema));
        }

        if (!FactsValidation.IsAbsoluteUri(predicateUri))
        {
            throw new ArgumentException(
                "An inverse predicate must be an absolute URI.",
                nameof(predicateUri));
        }

        if (!FactsValidation.IsAbsoluteUri(inverseOfPredicateUri))
        {
            throw new ArgumentException(
                "The predicate this inverts must be an absolute URI.",
                nameof(inverseOfPredicateUri));
        }

        if (!FactsValidation.IsAbsoluteUri(authorizingOntologyStatementUri))
        {
            throw new ArgumentException(
                "An inverse must name the absolute URI of the ontology statement that authorizes it.",
                nameof(authorizingOntologyStatementUri));
        }

        ArgumentNullException.ThrowIfNull(derivedFrom);
        if (!string.Equals(inverseOfPredicateUri, derivedFrom.PredicateUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The inverted predicate must be the predicate of the assertion it was derived from.",
                nameof(inverseOfPredicateUri));
        }

        Schema = schema;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        PredicateUri = predicateUri;
        InverseOfPredicateUri = inverseOfPredicateUri;
        AuthorizingOntologyStatementUri = authorizingOntologyStatementUri;
        DerivedFrom = derivedFrom;
    }

    public string Schema { get; }

    public OfficialIdentity Source { get; }

    public OfficialIdentity Target { get; }

    public string PredicateUri { get; }

    public string InverseOfPredicateUri { get; }

    /// <summary>
    /// The publisher ontology statement that authorizes this inversion. Without it the edge
    /// cannot be constructed at all.
    /// </summary>
    public string AuthorizingOntologyStatementUri { get; }

    public PublisherRelation DerivedFrom { get; }
}
