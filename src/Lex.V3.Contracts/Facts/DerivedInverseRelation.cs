using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// The inverse of a publisher assertion, derived only where the publisher's own ontology
/// authorizes that inverse.
/// </summary>
/// <remarks>
/// <para>
/// Three bindings make an invented inverse unrepresentable rather than merely discouraged. The
/// authorizing ontology statement must be named. The inverted predicate must equal the forward
/// assertion's predicate. And **the endpoints must be the forward assertion's endpoints,
/// swapped**.
/// </para>
/// <para>
/// Candidate 1 checked only the predicate, so an inverse could name any two identities at all
/// while pointing at an unrelated forward fact as its justification. Codex built exactly that
/// and it was accepted. An inverse whose endpoints are not the forward edge's endpoints is not
/// an inverse of anything.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DerivedInverseRelation
{
    public const string Identity = FactsSchemaIds.DerivedInverseRelation;

    [JsonConstructor]
    public DerivedInverseRelation(
        string schema,
        OfficialIdentitySet source,
        OfficialIdentitySet target,
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

        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(derivedFrom);

        if (!string.Equals(inverseOfPredicateUri, derivedFrom.PredicateUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The inverted predicate must be the predicate of the assertion it was derived from.",
                nameof(inverseOfPredicateUri));
        }

        if (!source.SameIdentity(derivedFrom.Target))
        {
            throw new ArgumentException(
                "An inverse must start at the target of the assertion it was derived from.",
                nameof(source));
        }

        if (!target.SameIdentity(derivedFrom.Source))
        {
            throw new ArgumentException(
                "An inverse must end at the source of the assertion it was derived from.",
                nameof(target));
        }

        Schema = schema;
        Source = source;
        Target = target;
        PredicateUri = predicateUri;
        InverseOfPredicateUri = inverseOfPredicateUri;
        AuthorizingOntologyStatementUri = authorizingOntologyStatementUri;
        DerivedFrom = derivedFrom;
    }

    public string Schema { get; }

    public OfficialIdentitySet Source { get; }

    public OfficialIdentitySet Target { get; }

    public string PredicateUri { get; }

    public string InverseOfPredicateUri { get; }

    /// <summary>
    /// The publisher ontology statement that authorizes this inversion. Without it the edge
    /// cannot be constructed at all.
    /// </summary>
    public string AuthorizingOntologyStatementUri { get; }

    public PublisherRelation DerivedFrom { get; }
}
