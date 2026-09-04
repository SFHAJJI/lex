using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The inverse of a publisher assertion, derived locally because the publisher's store does not
/// materialise that direction.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type does not implement <see cref="IEuFactsEvidenceCarrier"/>, and never will.</b> That
/// omission is the point of the type, so it is stated here rather than left as a gap a later
/// reader could tidy away by "making it consistent" with
/// <see cref="EuPublisherRelationEdge"/>. REL-002 excludes derived edges from evidence bundles.
/// A bundle correctly typed against the marker therefore cannot hold one of these at all, which
/// makes the exclusion structural rather than a documented convention. Ruled at
/// <c>lex-event-20260904T190136614Z-26f124d9e6d246348b54b6719e22a63a</c>; Decisions 25 and 26
/// require the inverse to be derived and labelled derived.
/// </para>
/// <para>
/// <b>Adding the marker to this type would be a defect, not an improvement.</b> It would let a
/// locally computed edge be presented with the same authority as a publisher assertion, which is
/// exactly the v2 behaviour E4 refuses: v2 mixed derived inverses into the same bundle as
/// publisher assertions, leaving no way for a reader to tell which was which.
/// </para>
/// <para>
/// <b>A derived inverse cannot exist without the assertion it inverts.</b> The only way to build
/// one is <see cref="From"/>, which takes the real forward edge and swaps its endpoints itself.
/// There is no path that accepts a free-floating source and target, so a derived inverse whose
/// endpoints do not correspond to any publisher assertion is unconstructible rather than merely
/// discouraged. The Facts layer's own <see cref="DerivedInverseRelation"/> records the equivalent
/// endpoint equalities as reader-only invariants, because JSON Schema cannot express an equality
/// between two distant instance locations; this type is not a wire contract, so it can enforce
/// them by construction instead.
/// </para>
/// </remarks>
public sealed class EuDerivedInverseRelationEdge
{
    private EuDerivedInverseRelationEdge(
        EuPublisherRelationEdge derivedFrom,
        string predicateUri,
        EuRelationTargetState targetState)
    {
        DerivedFrom = derivedFrom;
        PredicateUri = predicateUri;
        TargetState = targetState;
    }

    /// <summary>The publisher assertion this edge was inverted from. Always present.</summary>
    public EuPublisherRelationEdge DerivedFrom { get; }

    /// <summary>The inverse predicate this derived edge is stated on.</summary>
    public string PredicateUri { get; }

    /// <summary>The forward predicate this edge was inverted from.</summary>
    public string InvertedFromPredicateUri => DerivedFrom.PredicateUri;

    /// <summary>The source of the inverse edge, which is the forward assertion's target.</summary>
    public OfficialIdentitySet Source => DerivedFrom.Target;

    /// <summary>The target of the inverse edge, which is the forward assertion's source.</summary>
    public OfficialIdentitySet Target => DerivedFrom.Source;

    /// <summary>
    /// How <see cref="Target"/> stands. Stated separately from the forward edge's own target
    /// state, because the inverse points at the other end and that end is a different act.
    /// </summary>
    public EuRelationTargetState TargetState { get; }

    /// <summary>
    /// Derives the inverse of a real publisher assertion. The endpoints are taken from
    /// <paramref name="derivedFrom"/> and cannot be supplied independently.
    /// </summary>
    public static EuDerivedInverseRelationEdge From(
        EuPublisherRelationEdge derivedFrom,
        string inversePredicateUri,
        EuRelationTargetState targetState)
    {
        ArgumentNullException.ThrowIfNull(derivedFrom);
        ArgumentNullException.ThrowIfNull(inversePredicateUri);

        if (!Enum.IsDefined(targetState))
        {
            throw new ArgumentException(
                $"{targetState} is not a declared EuRelationTargetState member.",
                nameof(targetState));
        }

        if (!Uri.TryCreate(inversePredicateUri, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(inversePredicateUri)}\" is not an absolute "
                    + "inverse predicate URI.",
                nameof(inversePredicateUri));
        }

        if (string.Equals(inversePredicateUri, derivedFrom.PredicateUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A derived inverse cannot be stated on the same predicate as the assertion it "
                    + "inverts, because that would claim the publisher materialised this "
                    + "direction.",
                nameof(inversePredicateUri));
        }

        return new EuDerivedInverseRelationEdge(derivedFrom, inversePredicateUri, targetState);
    }
}
