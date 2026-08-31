using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// A relation exactly as the publisher asserted it, in the direction it was asserted.
/// </summary>
/// <remarks>
/// The predicate is carried as the publisher's own absolute URI and is never mapped onto a local
/// name. Relabelling loses the distinction between two publisher predicates that a local
/// vocabulary happens to render with one word, and that loss is unrecoverable downstream.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublisherRelation
{
    public const string Identity = FactsSchemaIds.PublisherRelation;

    [JsonConstructor]
    public PublisherRelation(
        string schema,
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        string predicateUri,
        SourceObservationReference observation,
        IReadOnlyList<QualifiedAxiom> qualifiedAxioms)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The publisher relation schema must be version 1.", nameof(schema));
        }

        if (!FactsValidation.IsAbsoluteUri(predicateUri))
        {
            throw new ArgumentException(
                "A relation predicate must be the publisher's absolute URI.",
                nameof(predicateUri));
        }

        ArgumentNullException.ThrowIfNull(qualifiedAxioms);
        var axioms = qualifiedAxioms.ToArray();
        if (Array.IndexOf(axioms, null) >= 0)
        {
            throw new ArgumentException("An axiom entry cannot be null.", nameof(qualifiedAxioms));
        }

        Schema = schema;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        PredicateUri = predicateUri;
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        QualifiedAxioms = Array.AsReadOnly(axioms);
    }

    public string Schema { get; }

    public OfficialIdentitySet Source { get; }

    public OfficialIdentitySet Target { get; }

    public string PredicateUri { get; }

    public SourceObservationReference Observation { get; }

    /// <summary>
    /// Every axiom the publisher attached, in order, including repeated remote axiom identities.
    /// </summary>
    public IReadOnlyList<QualifiedAxiom> QualifiedAxioms { get; }
}
