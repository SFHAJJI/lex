using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// One relation edge together with everything needed to read it honestly: how it came to exist,
/// whether its target's body is held, and how the target stands with respect to ECLI.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one of the three edge shapes is present, and <see cref="Kind"/> must agree with which
/// one, so a document claiming <c>publisher_asserted</c> while holding a locally derived view
/// fails to deserialize instead of being read as a publisher claim.
/// </para>
/// <para>
/// A target with official identity and no held body is an ordinary state, not an error. Edges
/// are never dropped because the thing they point at is outside the held corpus.
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
        PublisherRelation? publisherAsserted,
        DerivedInverseRelation? ontologyAuthorizedInverse,
        LocalInboundView? localInboundView)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The relation fact schema must be version 1.", nameof(schema));
        }

        FactsValidation.RequireDefined(kind, nameof(kind));
        FactsValidation.RequireDefined(targetBodyScope, nameof(targetBodyScope));
        FactsValidation.RequireDefined(targetEcliState, nameof(targetEcliState));

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

        var carried = publisherAsserted?.Target
            ?? ontologyAuthorizedInverse?.Target
            ?? localInboundView!.Target;

        // The ECLI state is checked against the target's own identity set rather than taken on
        // trust. Candidate 1 carried a loose `target_ecli` string that belonged to nothing: an
        // ECLI from an unrelated case could be attached to a Luxembourg statute and nothing
        // objected, and there was no way to say "this thing does not have ECLIs" at all.
        var declaredEcli = carried.Value(FactsIdentifierFamily.Ecli);
        switch (targetEcliState)
        {
            case EcliState.EcliPresent when declaredEcli is null:
                throw new ArgumentException(
                    "An edge stating ecli_present must carry the ECLI in the target identity set.",
                    nameof(targetEcliState));

            case EcliState.EcliMissing when declaredEcli is not null:
                throw new ArgumentException(
                    "An edge stating ecli_missing cannot carry an ECLI in the target identity set.",
                    nameof(targetEcliState));

            // ecli_missing is an absence claim about the publisher, so it requires a complete
            // enumeration. A partial read cannot tell "the publisher published no ECLI" from
            // "this reader kept only the CELEX row", and the second is not a fact about the law.
            case EcliState.EcliMissing
                when carried.Enumeration != IdentifierEnumeration.Complete:
                throw new ArgumentException(
                    "ecli_missing claims the publisher has no ECLI, so it requires a complete "
                        + "identifier enumeration rather than a partial read.",
                    nameof(targetEcliState));

            case EcliState.EcliNotApplicable
                when carried.Enumeration != IdentifierEnumeration.Complete:
                throw new ArgumentException(
                    "ecli_not_applicable is also an absence claim and requires a complete "
                        + "identifier enumeration.",
                    nameof(targetEcliState));

            case EcliState.EcliMissing when !carried.IsCase:
                throw new ArgumentException(
                    "ecli_missing states that a case has no published ECLI, so the target must be a case.",
                    nameof(targetEcliState));

            case EcliState.EcliNotApplicable when declaredEcli is not null:
                throw new ArgumentException(
                    "An edge stating ecli_not_applicable cannot carry an ECLI.",
                    nameof(targetEcliState));

            case EcliState.EcliNotApplicable when carried.IsCase:
                throw new ArgumentException(
                    "The target is a case, so ECLI applies to it and the state cannot be not_applicable.",
                    nameof(targetEcliState));
        }

        Schema = schema;
        Kind = kind;
        TargetBodyScope = targetBodyScope;
        TargetEcliState = targetEcliState;
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
    /// edge's own <c>Target</c>, never the forward assertion underneath it.
    /// </remarks>
    public TargetBodyScope TargetBodyScope { get; }

    /// <summary>
    /// How the carried edge's target stands with respect to ECLI, checked against that target's
    /// identity set rather than declared freely.
    /// </summary>
    public EcliState TargetEcliState { get; }

    public PublisherRelation? PublisherAsserted { get; }

    public DerivedInverseRelation? OntologyAuthorizedInverse { get; }

    public LocalInboundView? LocalInboundView { get; }

    /// <summary>The target of whichever edge this fact carries.</summary>
    [JsonIgnore]
    public OfficialIdentitySet CarriedTarget =>
        PublisherAsserted?.Target ?? OntologyAuthorizedInverse?.Target ?? LocalInboundView!.Target;

    /// <summary>The target's ECLI, which exists only inside its identity set.</summary>
    [JsonIgnore]
    public string? TargetEcli => CarriedTarget.Value(FactsIdentifierFamily.Ecli);
}
