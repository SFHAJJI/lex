using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Derives the inverse of an asserted EU amendment edge, authorised by the publisher's own
/// <c>owl:inverseOf</c> declaration.
/// </summary>
/// <remarks>
/// <para>
/// <b>The publisher declares this inverse; this package does not invent it.</b> The probe
/// <c>lex-event-20260904T191531228Z-116c5e971e374b63a2350b481945b1d6</c> (digest
/// 2e010919fde5842e) read <c>cdm:resource_legal_amended_by_resource_legal</c>'s own declaration in
/// the store's ontology, and it carries
/// <c>owl:inverseOf cdm:resource_legal_amends_resource_legal</c>. Only the amends direction is
/// materialised with triples, which a separate store-wide query established. So the office states
/// the two predicates are inverses while publishing only one of them.
/// </para>
/// <para>
/// <b>This returns a Facts <see cref="DerivedInverseRelation"/>, not an E4 type.</b> That record
/// already demands everything an honest inverse needs and enforces it in its own constructor: an
/// <see cref="ObservedInverseAxiom"/> that authorises exactly this inversion in this direction,
/// an inverted predicate equal to the forward assertion's predicate, and endpoints that are the
/// forward assertion's endpoints swapped. An earlier version of E4 built a parallel type that
/// required only a different inverted-from URI, which is strictly weaker; the design verdict
/// <c>lex-event-20260904T192820932Z-4101310a2b7a482d87330f1eda1ec14a</c> is that the Facts layer
/// already had the right shape and E4 built beside it.
/// </para>
/// <para>
/// <b>Not admissible evidence, structurally.</b> <see cref="DerivedInverseRelation"/> implements no
/// marker interface, so a bundle typed against <see cref="IEuFactsEvidenceCarrier"/> cannot hold
/// one. REL-002 excludes derived edges from bundles, and that exclusion now rests on the Facts
/// layer's own type rather than on which E4 types happen to implement an interface. It is worth
/// naming what is excluded: an inverse the publisher <b>declares</b> and declines to materialise,
/// which is narrower and more defensible than excluding an edge of this package's construction.
/// </para>
/// <para>
/// <b>One pair only.</b> The amends and amended-by predicates are the only pair with an observed
/// <c>owl:inverseOf</c> declaration. Nothing here derives an inverse for repeals or for either
/// consolidation predicate, because no axiom has been read authorising one, and
/// <see cref="ObservedInverseAxiom"/> exists precisely so that an unwitnessed inversion cannot be
/// constructed.
/// </para>
/// </remarks>
public static class EuDerivedAmendmentInverse
{
    /// <summary>
    /// Derives the inverse of an asserted amendment edge.
    /// </summary>
    /// <param name="forward">
    /// The asserted edge to invert. Must be on
    /// <see cref="EuAmendmentRelationVocabulary.AmendsPredicateUri"/>: that is the only predicate
    /// with an observed inverse declaration.
    /// </param>
    /// <param name="axiomObservationId">
    /// The custody coordinate for the observation that read the <c>owl:inverseOf</c> declaration.
    /// Distinct from the edge's own observation id: the axiom and the edge are two different
    /// readings, and a single id would claim one observation produced both.
    /// </param>
    public static DerivedInverseRelation From(
        EuRelationEdgeBinding forward,
        string axiomObservationId)
    {
        ArgumentNullException.ThrowIfNull(forward);

        var asserted = forward.Asserted;
        if (!string.Equals(
                asserted.PredicateUri,
                EuAmendmentRelationVocabulary.AmendsPredicateUri,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(asserted.PredicateUri)}\" has no observed "
                    + "owl:inverseOf declaration. Only "
                    + EuAmendmentRelationVocabulary.AmendsPredicateUri
                    + " has one, so only an amendment edge can be inverted here.",
                nameof(forward));
        }

        var axiom = new ObservedInverseAxiom(
            EuAmendmentRelationVocabulary.OntologyUri,
            EuAmendmentRelationVocabulary.OntologyVersion,
            EuAmendmentRelationVocabulary.AmendsPredicateUri,
            EuAmendmentRelationVocabulary.AmendedByPredicateUri,
            axiomObservationId);

        // The endpoints are taken from the forward assertion and swapped here, so a caller cannot
        // supply endpoints that do not correspond to it. DerivedInverseRelation checks the same
        // thing again in its own constructor; both are cheap and the second is the one that holds
        // for a document arriving off the wire rather than through this door.
        return new DerivedInverseRelation(
            DerivedInverseRelation.Identity,
            asserted.Target,
            asserted.Source,
            EuAmendmentRelationVocabulary.AmendedByPredicateUri,
            EuAmendmentRelationVocabulary.AmendsPredicateUri,
            axiom,
            asserted);
    }
}
