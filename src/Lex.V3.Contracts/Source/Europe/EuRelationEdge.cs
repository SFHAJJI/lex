using System.Collections.ObjectModel;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// How the target of an EU relation edge stands: its body is held, its body is not held, or the
/// target could not be resolved at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not <see cref="TargetBodyScope"/>.</b> That enum answers a different question
/// with three different answers: held, in scope and not held, and outside scope. All three of
/// them presume the target was resolved. <see cref="Unresolved"/> is the state where that
/// presumption fails, and it has no spelling in <see cref="TargetBodyScope"/> at all. Reusing
/// that enum would force an unresolved target to be reported as <c>body_outside_scope</c>, which
/// claims a scope decision nobody made.
/// </para>
/// <para>
/// The two enums are therefore about different things and neither subsumes the other. The
/// V3 rule against parallel vocabularies is a rule against a second spelling of one fact, and
/// this is a second fact. See the honest caveat in the remarks on <see cref="EuRelationEdge"/>:
/// merging the two into one four-state vocabulary would be the better end state and is not
/// reachable from this slice, which may not edit <c>FactsVocabulary.cs</c>.
/// </para>
/// </remarks>
public enum EuRelationTargetState
{
    /// <summary>The target resolved and its body is held.</summary>
    Held,

    /// <summary>The target resolved and its body is not held. An ordinary state, never an error.</summary>
    Unheld,

    /// <summary>
    /// The target did not resolve. The edge is still carried, because the publisher asserted it
    /// and dropping it would lose a publisher fact, but nothing may be claimed about what it
    /// points at.
    /// </summary>
    Unresolved,
}

/// <summary>
/// Whether an edge is one the publisher materialised, or one derived locally by inverting a
/// publisher assertion.
/// </summary>
public enum EuRelationMaterialisation
{
    /// <summary>The publisher's store returns this edge on this predicate in this direction.</summary>
    PublisherMaterialised,

    /// <summary>
    /// Locally derived by inverting a publisher assertion. Carries no publisher authority and is
    /// excluded from evidence bundles.
    /// </summary>
    LocallyDerivedInverse,
}

/// <summary>
/// One typed EU relation edge: its predicate, which direction actually carries it, how its target
/// stands, and the publisher's own axiom qualifiers kept whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>Forward-only materialisation, recorded on the type.</b> An unfiltered store-wide query on
/// <c>cdm:resource_legal_amends_resource_legal</c> returns rows immediately (canary
/// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c>, digest
/// 58c50d8c78ab80c9), and the same query on its inverse
/// <c>cdm:resource_legal_amended_by_resource_legal</c> returns zero (digest 21732a68993ff562).
/// Only one direction exists in the store. So an edge on the inverse predicate can only ever be
/// locally derived, and <see cref="Create"/> refuses to call one publisher-materialised. Decisions
/// 25 and 26: the inverse is derived, and it is labelled derived.
/// </para>
/// <para>
/// <b>The absence claim above is bounded exactly as it was made.</b> It is one predicate, one
/// direction, unfiltered across the store, at the EU SPARQL endpoint, on 2026-09-04. It is not a
/// claim that no amendment relation reaches any act in the inbound direction by some other route.
/// The distinction matters: an earlier canary on this same subject generalised from one work to
/// the endpoint and was wrong, and that error is what event
/// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c> exists to correct.
/// </para>
/// <para>
/// <b>Derived edges cannot reach an evidence bundle.</b> No type in this slice implements
/// <see cref="IEuFactsEvidenceCarrier"/>, so a bundle typed against that marker cannot hold one.
/// This is REL-002's exclusion criterion made structural, and it is the v2 behaviour E4 refuses:
/// v2 mixed derived inverses into the same bundle as publisher assertions.
/// </para>
/// <para>
/// <b>A known limit of this slice, stated rather than hidden.</b> This edge does not implement
/// <see cref="IEuFactsEvidenceCarrier"/> even where it carries a publisher assertion, because the
/// marker's implementers are pinned as a closed set of exactly two by a committed test this lane
/// may not edit. Admitting a publisher-materialised EU relation edge as evidence is therefore
/// still to do, and it needs that pin widened deliberately rather than by a side effect of this
/// slice.
/// </para>
/// </remarks>
public sealed class EuRelationEdge
{
    private EuRelationEdge(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        string predicateUri,
        EuRelationMaterialisation materialisedDirection,
        string? invertedFromPredicateUri,
        EuRelationTargetState targetState,
        IReadOnlyList<QualifiedAxiom> qualifiedAxioms)
    {
        Source = source;
        Target = target;
        PredicateUri = predicateUri;
        MaterialisedDirection = materialisedDirection;
        InvertedFromPredicateUri = invertedFromPredicateUri;
        TargetState = targetState;
        QualifiedAxioms = qualifiedAxioms;
    }

    /// <summary>The edge's source, in the direction this edge is stated.</summary>
    public OfficialIdentitySet Source { get; }

    /// <summary>The edge's target, in the direction this edge is stated.</summary>
    public OfficialIdentitySet Target { get; }

    /// <summary>The publisher's own predicate IRI, never a local name.</summary>
    public string PredicateUri { get; }

    /// <summary>Whether the publisher materialised this edge or this process derived it.</summary>
    public EuRelationMaterialisation MaterialisedDirection { get; }

    /// <summary>
    /// For a derived edge, the forward predicate it was inverted from. <c>null</c> for a
    /// publisher-materialised edge, which was inverted from nothing.
    /// </summary>
    public string? InvertedFromPredicateUri { get; }

    /// <summary>How <see cref="Target"/> stands.</summary>
    public EuRelationTargetState TargetState { get; }

    /// <summary>
    /// The publisher's axioms in order, repeats included. A list and not a dictionary, for the
    /// reason <see cref="QualifiedAxiom"/> gives: one predicate may be attached more than once
    /// and a dictionary keeps one of them.
    /// </summary>
    public IReadOnlyList<QualifiedAxiom> QualifiedAxioms { get; }

    /// <summary>Whether this edge is locally derived and therefore carries no publisher authority.</summary>
    public bool IsDerived =>
        MaterialisedDirection == EuRelationMaterialisation.LocallyDerivedInverse;

    /// <summary>
    /// Builds an edge, refusing the direction the store does not materialise and refusing a
    /// derived edge that does not say what it was derived from.
    /// </summary>
    public static EuRelationEdge Create(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        string predicateUri,
        EuRelationMaterialisation materialisedDirection,
        string? invertedFromPredicateUri,
        EuRelationTargetState targetState,
        IReadOnlyList<QualifiedAxiom> qualifiedAxioms)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(predicateUri);
        ArgumentNullException.ThrowIfNull(qualifiedAxioms);

        if (!Enum.IsDefined(materialisedDirection))
        {
            throw new ArgumentException(
                $"{materialisedDirection} is not a declared EuRelationMaterialisation member.",
                nameof(materialisedDirection));
        }

        if (!Enum.IsDefined(targetState))
        {
            throw new ArgumentException(
                $"{targetState} is not a declared EuRelationTargetState member.",
                nameof(targetState));
        }

        if (!Uri.TryCreate(predicateUri, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(predicateUri)}\" is not the publisher's "
                    + "absolute predicate URI.",
                nameof(predicateUri));
        }

        var isInversePredicate = string.Equals(
            predicateUri,
            EuAmendmentRelationVocabulary.AmendedByPredicateUri,
            StringComparison.Ordinal);

        if (isInversePredicate &&
            materialisedDirection == EuRelationMaterialisation.PublisherMaterialised)
        {
            throw new ArgumentException(
                $"{EuAmendmentRelationVocabulary.AmendedByPredicateUri} returns zero rows "
                    + "store-wide, so no edge on it is publisher-materialised. An edge on the "
                    + "inverse predicate must be declared LocallyDerivedInverse.",
                nameof(materialisedDirection));
        }

        switch (materialisedDirection)
        {
            case EuRelationMaterialisation.LocallyDerivedInverse when invertedFromPredicateUri is null:
                throw new ArgumentException(
                    "A derived inverse must name the forward predicate it was inverted from, so a "
                        + "reader can reach the publisher assertion underneath it.",
                    nameof(invertedFromPredicateUri));

            case EuRelationMaterialisation.LocallyDerivedInverse
                when !Uri.TryCreate(invertedFromPredicateUri, UriKind.Absolute, out _):
                throw new ArgumentException(
                    $"\"{EuAuthorityQualifiedToken.Describe(invertedFromPredicateUri)}\" is not an "
                        + "absolute forward predicate URI.",
                    nameof(invertedFromPredicateUri));

            case EuRelationMaterialisation.LocallyDerivedInverse
                when string.Equals(invertedFromPredicateUri, predicateUri, StringComparison.Ordinal):
                throw new ArgumentException(
                    "A derived inverse cannot be inverted from its own predicate.",
                    nameof(invertedFromPredicateUri));

            case EuRelationMaterialisation.PublisherMaterialised when invertedFromPredicateUri is not null:
                throw new ArgumentException(
                    "A publisher-materialised edge was inverted from nothing, so it cannot name a "
                        + "forward predicate it was derived from.",
                    nameof(invertedFromPredicateUri));
        }

        var axioms = qualifiedAxioms.ToArray();
        if (Array.IndexOf(axioms, null) >= 0)
        {
            throw new ArgumentException("An axiom entry cannot be null.", nameof(qualifiedAxioms));
        }

        return new EuRelationEdge(
            source,
            target,
            predicateUri,
            materialisedDirection,
            invertedFromPredicateUri,
            targetState,
            new ReadOnlyCollection<QualifiedAxiom>(axioms));
    }
}
