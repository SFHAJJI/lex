using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// One EU relation edge the publisher asserted, bound onto the Facts layer's own
/// <see cref="PublisherRelation"/> and <see cref="RelationFact"/> rather than re-declared beside
/// them. Stage 2 item E4, ledger row <c>REL-002</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a rework, and the shape it replaces is the point.</b> The first version of E4
/// (931d6e1b, and the type split at 1d8cbe5d on top of it) declared its own <c>Source</c>,
/// <c>Target</c>, <c>PredicateUri</c> and <c>QualifiedAxioms</c>, which is a parallel relation
/// vocabulary beside <see cref="PublisherRelation"/>. The design verdict
/// <c>lex-event-20260904T192820932Z-4101310a2b7a482d87330f1eda1ec14a</c> named three concrete
/// costs of that shape, and each is answered here rather than argued with.
/// </para>
/// <para>
/// One: it carried no source observation id, so a live ingester could not record which observation
/// produced an edge. <see cref="PublisherRelation.SourceObservationId"/> now carries it, and
/// <see cref="Create"/> cannot be called without one.
/// </para>
/// <para>
/// Two: it pinned no predicate set, so its factory accepted any absolute URI. Predicates now come
/// from <see cref="EuAmendmentRelationVocabulary.AssertedPredicates"/>, closed, exactly as E6's
/// <see cref="EuCaseLawLinkBinding"/> refuses outside its own pinned set.
/// </para>
/// <para>
/// Three: its derived inverse needed only a different inverted-from URI, where the Facts layer's
/// <see cref="DerivedInverseRelation"/> already demanded an <see cref="ObservedInverseAxiom"/>
/// naming the ontology and version, a matching forward predicate and swapped endpoints. The
/// <c>owl:inverseOf</c> declaration grounded at
/// <c>lex-event-20260904T191531228Z-116c5e971e374b63a2350b481945b1d6</c> is precisely the axiom
/// that type exists to carry. See <see cref="EuDerivedAmendmentInverse"/>.
/// </para>
/// <para>
/// <b>Admissibility.</b> This binding implements <see cref="IEuFactsEvidenceCarrier"/>, as E1's
/// <see cref="EuDateAxiomBinding"/> and E6's <see cref="EuCaseLawLinkBinding"/> do, because every
/// edge it can hold is a publisher assertion: <see cref="Create"/> builds a
/// <see cref="RelationFact"/> whose kind is always
/// <see cref="RelationAssertionKind.PublisherAsserted"/>. A derived inverse is a
/// <see cref="DerivedInverseRelation"/>, a Facts record implementing no marker, so it cannot enter
/// a bundle typed against the marker. That is REL-002's exclusion, and it now rests on the Facts
/// layer's own type rather than on an argument about which E4 types implement an interface.
/// </para>
/// <para>
/// <b>The target's ECLI state is computed, never accepted.</b> <see cref="RelationFact"/> checks it
/// against the target's own identity set, so passing it in would only create a value a caller can
/// get wrong. <see cref="Create"/> derives it the same way E6 does, from the target itself.
/// </para>
/// </remarks>
public sealed class EuRelationEdgeBinding : IEuFactsEvidenceCarrier
{
    private EuRelationEdgeBinding(RelationFact fact)
    {
        Fact = fact;
    }

    /// <summary>
    /// The relation fact this binding qualifies. The single home for the edge: its endpoints,
    /// predicate, observation id and qualifiers are read from here, never mirrored onto properties
    /// of this class that could drift from it.
    /// </summary>
    public RelationFact Fact { get; }

    /// <summary>The publisher assertion inside <see cref="Fact"/>. Never null for this binding.</summary>
    public PublisherRelation Asserted => Fact.PublisherAsserted!;

    /// <summary>
    /// The only path that mints a binding. Builds the <see cref="PublisherRelation"/> and the
    /// <see cref="RelationFact"/> in one step, exactly as <see cref="EuCaseLawLinkBinding.Create"/>
    /// does.
    /// </summary>
    /// <param name="source">The edge's subject exactly as the publisher asserted it.</param>
    /// <param name="target">The edge's object exactly as the publisher asserted it.</param>
    /// <param name="predicateUri">
    /// Must be one of <see cref="EuAmendmentRelationVocabulary.AssertedPredicates"/>. Any other
    /// value, including a syntactically valid but unpinned absolute URI, is refused by name.
    /// </param>
    /// <param name="targetBodyScope">Whether <paramref name="target"/>'s own body is held.</param>
    /// <param name="qualifiedAxioms">
    /// Every <c>owl:Axiom</c> qualifier the publisher attached to this edge, in order, or an empty
    /// list. For a located amendment these carry the location, the validity dates, the role and the
    /// link-target type as observed.
    /// </param>
    /// <param name="sourceObservationId">The custody coordinate for the observation this edge came from.</param>
    public static EuRelationEdgeBinding Create(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        string predicateUri,
        TargetBodyScope targetBodyScope,
        IReadOnlyList<QualifiedAxiom> qualifiedAxioms,
        string sourceObservationId)
    {
        // Both are dereferenced below before PublisherRelation's own constructor runs, so each
        // needs its own guard here or the caller sees a NullReferenceException instead of the
        // ArgumentNullException this door owes them.
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(qualifiedAxioms);

        if (predicateUri is null ||
            !EuAmendmentRelationVocabulary.AssertedPredicates.Contains(predicateUri))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(predicateUri)}\" is not one of the pinned "
                    + "E4 asserted relation predicates. The inverse amendment predicate is "
                    + "deliberately not among them: it returns zero rows store-wide, so an edge on "
                    + "it is a derived inverse and is built by EuDerivedAmendmentInverse.",
                nameof(predicateUri));
        }

        // Mirrors RelationFact's own three-state invariant, read from the target, which is the side
        // that field describes. Computed rather than accepted: a caller-supplied value here can
        // only ever agree with the identity set or be wrong about it.
        var targetEcliState = target.IsCase()
            ? target.Has(FactsIdentifierFamily.Ecli)
                ? EcliState.EcliPresent
                : EcliState.EcliNotInThisSet
            : EcliState.EcliNotApplicable;

        var asserted = new PublisherRelation(
            PublisherRelation.Identity,
            source,
            target,
            predicateUri,
            sourceObservationId,
            qualifiedAxioms);

        var fact = new RelationFact(
            RelationFact.Identity,
            RelationAssertionKind.PublisherAsserted,
            targetBodyScope,
            targetEcliState,
            asserted,
            null,
            null);

        return new EuRelationEdgeBinding(fact);
    }
}
