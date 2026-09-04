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
/// this is a second fact. Merging the two into one four-state vocabulary would be the better end
/// state and is not reachable from this slice, which may not edit <c>FactsVocabulary.cs</c>.
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
/// One EU relation edge the publisher itself materialised: its predicate, how its target stands,
/// and the publisher's own axiom qualifiers kept whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is admissible evidence</b> and says so by implementing
/// <see cref="IEuFactsEvidenceCarrier"/>, exactly as E6's <c>EuCaseLawLinkBinding</c> and E1's
/// <c>EuDateAxiomBinding</c> do. Every edge it can hold is a publisher assertion, which is what
/// makes that admissible. Ruled at
/// <c>lex-event-20260904T190136614Z-26f124d9e6d246348b54b6719e22a63a</c>.
/// </para>
/// <para>
/// <b>Publisher assertion and derived inverse are two types, not one type with a flag.</b> The
/// first version of this slice carried both in one type discriminated by an
/// <c>EuRelationMaterialisation</c> enum. That shape cannot express the admissibility rule at all:
/// the marker is implemented by a type, so a single type carrying both would either admit derived
/// inverses to evidence bundles or exclude publisher assertions from them. Splitting them also
/// makes the distinction unfalsifiable by a mutation, because there is no longer a flag to flip.
/// See <see cref="EuDerivedInverseRelationEdge"/>, which deliberately does not implement the
/// marker.
/// </para>
/// <para>
/// <b>Forward-only materialisation.</b> An unfiltered store-wide query on
/// <c>cdm:resource_legal_amends_resource_legal</c> returns rows immediately (canary
/// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c>, digest
/// 58c50d8c78ab80c9), and the same query on its inverse
/// <c>cdm:resource_legal_amended_by_resource_legal</c> returns zero (digest 21732a68993ff562).
/// Only one direction exists in the store, so <see cref="Create"/> refuses the inverse predicate
/// outright: no edge on it can be a publisher assertion. Decisions 25 and 26.
/// </para>
/// <para>
/// <b>The absence claim above is bounded exactly as it was made.</b> It is one predicate, one
/// direction, unfiltered across the store, at the EU SPARQL endpoint, on 2026-09-04. It is not a
/// claim that no amendment relation reaches any act in the inbound direction by some other route.
/// An earlier canary on this same subject generalised from one work to the endpoint and was
/// wrong, which is what that correction event exists to record.
/// </para>
/// </remarks>
public sealed class EuPublisherRelationEdge : IEuFactsEvidenceCarrier
{
    private EuPublisherRelationEdge(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        string predicateUri,
        EuRelationTargetState targetState,
        IReadOnlyList<QualifiedAxiom> qualifiedAxioms)
    {
        Source = source;
        Target = target;
        PredicateUri = predicateUri;
        TargetState = targetState;
        QualifiedAxioms = qualifiedAxioms;
    }

    /// <summary>The edge's source, in the direction the publisher asserted it.</summary>
    public OfficialIdentitySet Source { get; }

    /// <summary>The edge's target, in the direction the publisher asserted it.</summary>
    public OfficialIdentitySet Target { get; }

    /// <summary>The publisher's own predicate IRI, never a local name.</summary>
    public string PredicateUri { get; }

    /// <summary>How <see cref="Target"/> stands.</summary>
    public EuRelationTargetState TargetState { get; }

    /// <summary>
    /// The publisher's axioms in order, repeats included. A list and not a dictionary, for the
    /// reason <see cref="QualifiedAxiom"/> gives: one predicate may be attached more than once
    /// and a dictionary keeps one of them.
    /// </summary>
    public IReadOnlyList<QualifiedAxiom> QualifiedAxioms { get; }

    /// <summary>
    /// Builds a publisher-asserted edge, refusing the one predicate the store does not
    /// materialise in this direction.
    /// </summary>
    public static EuPublisherRelationEdge Create(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        string predicateUri,
        EuRelationTargetState targetState,
        IReadOnlyList<QualifiedAxiom> qualifiedAxioms)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(predicateUri);
        ArgumentNullException.ThrowIfNull(qualifiedAxioms);

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

        if (string.Equals(
                predicateUri,
                EuAmendmentRelationVocabulary.AmendedByPredicateUri,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{EuAmendmentRelationVocabulary.AmendedByPredicateUri} returns zero rows "
                    + "store-wide, so no edge on it is a publisher assertion. An inverse edge is "
                    + "an EuDerivedInverseRelationEdge, which is not admissible evidence.",
                nameof(predicateUri));
        }

        var axioms = qualifiedAxioms.ToArray();
        if (Array.IndexOf(axioms, null) >= 0)
        {
            throw new ArgumentException("An axiom entry cannot be null.", nameof(qualifiedAxioms));
        }

        return new EuPublisherRelationEdge(
            source,
            target,
            predicateUri,
            targetState,
            new ReadOnlyCollection<QualifiedAxiom>(axioms));
    }
}
