using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// An inverse mapping actually observed in the publisher's pinned ontology.
/// </summary>
/// <remarks>
/// <para>
/// Candidate 2 required only that an inverse name <i>some</i> absolute URI as its authorizing
/// statement, and that URI was checked against nothing. A probe built
/// <c>https://example.invalid/invented-inverse</c> authorized by
/// <c>https://example.invalid/not-an-ontology-statement</c> and it was accepted. The fixture was
/// no better: it passed <c>owl:inverseOf</c> itself as the alleged statement, which is the name
/// of the relationship rather than an observation that the pair holds.
/// </para>
/// <para>
/// This type carries the mapping as a fact: which predicate inverts to which, in which ontology,
/// at which version, witnessed by which observation. An inverse can then be checked against the
/// axiom instead of against a string that merely looks official.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ObservedInverseAxiom
{
    [JsonConstructor]
    public ObservedInverseAxiom(
        string ontologyUri,
        string ontologyVersion,
        string subjectPredicateUri,
        string objectPredicateUri,
        string sourceObservationId)
    {
        foreach (var (value, name) in new[]
                 {
                     (ontologyUri, nameof(ontologyUri)),
                     (subjectPredicateUri, nameof(subjectPredicateUri)),
                     (objectPredicateUri, nameof(objectPredicateUri)),
                 })
        {
            if (!FactsValidation.IsAbsoluteUri(value))
            {
                throw new ArgumentException("An inverse axiom URI must be absolute.", name);
            }
        }

        if (!FactsValidation.IsOpaqueIdentity(ontologyVersion))
        {
            throw new ArgumentException(
                "An inverse axiom must name the ontology version it was observed at.",
                nameof(ontologyVersion));
        }

        if (string.Equals(subjectPredicateUri, objectPredicateUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A predicate is not the inverse of itself.",
                nameof(objectPredicateUri));
        }

        OntologyUri = ontologyUri;
        OntologyVersion = ontologyVersion;
        SubjectPredicateUri = subjectPredicateUri;
        ObjectPredicateUri = objectPredicateUri;
        SourceObservationId = SourceObservation.Require(
            sourceObservationId, nameof(sourceObservationId));
    }

    public string OntologyUri { get; }

    public string OntologyVersion { get; }

    /// <summary>The predicate the publisher asserts.</summary>
    public string SubjectPredicateUri { get; }

    /// <summary>The predicate it inverts to.</summary>
    public string ObjectPredicateUri { get; }

    public string SourceObservationId { get; }

    /// <summary>Whether this axiom authorizes exactly the given inversion, in that direction.</summary>
    public bool Authorizes(string forwardPredicateUri, string inversePredicateUri) =>
        string.Equals(SubjectPredicateUri, forwardPredicateUri, StringComparison.Ordinal) &&
        string.Equals(ObjectPredicateUri, inversePredicateUri, StringComparison.Ordinal);
}

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
        ObservedInverseAxiom authorizingAxiom,
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

        ArgumentNullException.ThrowIfNull(authorizingAxiom);

        // The axiom must authorize exactly this inversion, in this direction. Naming an axiom is
        // not enough; an unrelated true axiom would otherwise authorize any invention.
        if (!authorizingAxiom.Authorizes(inverseOfPredicateUri, predicateUri))
        {
            throw new ArgumentException(
                "The observed axiom does not map this forward predicate to this inverse predicate.",
                nameof(authorizingAxiom));
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
        AuthorizingAxiom = authorizingAxiom;
        DerivedFrom = derivedFrom;
    }

    public string Schema { get; }

    public OfficialIdentitySet Source { get; }

    public OfficialIdentitySet Target { get; }

    public string PredicateUri { get; }

    public string InverseOfPredicateUri { get; }

    /// <summary>
    /// The observed ontology axiom that authorizes this exact inversion. Without an axiom that
    /// maps this forward predicate to this inverse predicate, the edge cannot be constructed.
    /// </summary>
    public ObservedInverseAxiom AuthorizingAxiom { get; }

    public PublisherRelation DerivedFrom { get; }
}
