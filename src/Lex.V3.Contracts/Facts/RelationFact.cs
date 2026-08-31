using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// One relation edge together with everything needed to read it honestly: how it came to exist,
/// whether its target's body is held, and whether the target carries an ECLI.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one of the three edge shapes is present, and <see cref="Kind"/> must agree with which
/// one. Carrying the discriminator and the payload separately, then checking them against each
/// other, means a document claiming <c>publisher_asserted</c> while holding a locally derived
/// view fails to deserialize instead of being read as a publisher claim.
/// </para>
/// <para>
/// A target with official identity and no held body is an ordinary state here, not an error.
/// Edges are never dropped because the thing they point at is outside the held corpus: the
/// publisher asserted the edge, so the edge is a fact regardless of what we hold.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RelationFact
{
    public const string Identity = FactsSchemaIds.RelationFact;

    [JsonConstructor]
    public RelationFact(
        string schema,
        RelationAssertionKind kind,
        TargetBodyScope targetBodyScope,
        EcliState targetEcliState,
        string? targetEcli,
        PublisherRelation? publisherAsserted,
        DerivedInverseRelation? ontologyAuthorizedInverse,
        LocalInboundView? localInboundView)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The relation fact schema must be version 1.", nameof(schema));
        }

        var present = (publisherAsserted is null ? 0 : 1) +
            (ontologyAuthorizedInverse is null ? 0 : 1) +
            (localInboundView is null ? 0 : 1);
        if (present != 1)
        {
            throw new ArgumentException(
                "A relation fact must carry exactly one edge shape.",
                nameof(schema));
        }

        var actual = publisherAsserted is not null
            ? RelationAssertionKind.PublisherAsserted
            : ontologyAuthorizedInverse is not null
                ? RelationAssertionKind.OntologyAuthorizedInverse
                : RelationAssertionKind.LocalInboundView;
        if (actual != kind)
        {
            throw new ArgumentException(
                "The declared assertion kind must match the edge shape actually carried.",
                nameof(kind));
        }

        switch (targetEcliState)
        {
            case EcliState.EcliPresent when !FactsValidation.IsOpaqueIdentity(targetEcli):
                throw new ArgumentException(
                    "An edge stating ecli_present must carry the ECLI.",
                    nameof(targetEcli));

            // ecli_missing means the publisher served no ECLI. Carrying one anyway would be an
            // invented identifier wearing a state that says it does not exist.
            case EcliState.EcliMissing when targetEcli is not null:
                throw new ArgumentException(
                    "An edge stating ecli_missing cannot carry an ECLI.",
                    nameof(targetEcli));
        }

        Schema = schema;
        Kind = kind;
        TargetBodyScope = targetBodyScope;
        TargetEcliState = targetEcliState;
        TargetEcli = targetEcli;
        PublisherAsserted = publisherAsserted;
        OntologyAuthorizedInverse = ontologyAuthorizedInverse;
        LocalInboundView = localInboundView;
    }

    public string Schema { get; }

    public RelationAssertionKind Kind { get; }

    /// <summary>
    /// The body scope of the <c>Target</c> of the edge carried here, whichever edge that is.
    /// </summary>
    /// <remarks>
    /// Naming the referent matters because a derived inverse swaps the endpoints: its
    /// <c>Target</c> is the original assertion's source. This field always follows the carried
    /// edge's own <c>Target</c>, never the forward assertion underneath it. A local inbound view
    /// has a single <c>Target</c> and there is no ambiguity there.
    /// </remarks>
    public TargetBodyScope TargetBodyScope { get; }

    /// <summary>
    /// The ECLI state of the <c>Target</c> of the edge carried here, on the same rule as
    /// <see cref="TargetBodyScope"/>.
    /// </summary>
    public EcliState TargetEcliState { get; }

    public string? TargetEcli { get; }

    public PublisherRelation? PublisherAsserted { get; }

    public DerivedInverseRelation? OntologyAuthorizedInverse { get; }

    public LocalInboundView? LocalInboundView { get; }
}
