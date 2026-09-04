namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The pinned EUR-Lex vocabulary behind stage 2 item E4 (ledger row <c>REL-002</c>): the relation
/// predicates, the reified annotation properties those relations carry, the two authority lists
/// their qualifier values are drawn from, and the ontology identity that authorises the one
/// derived inverse.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is grounded in retained bytes, and what is not.</b> The retained fixture
/// <c>amends-located-axioms.json</c> (2968 bytes, sha256
/// <c>d3353e41e9091b202970dae3ef5ec7be063a5b6dc5afcf30c12ada2b4fe01ffd</c>) is a SPARQL SELECT
/// result whose columns are <c>src</c>, <c>tgt</c>, <c>loc</c>, <c>start</c>, <c>linktype</c> and
/// <c>role</c>. A SELECT returns bound values, never the predicate IRIs that bound them, so the
/// fixture grounds the values in <see cref="LocationAuthorityListUri"/> and
/// <see cref="RoleAuthorityListUri"/> exactly, byte for byte, and grounds none of the predicate or
/// annotation IRIs below.
/// </para>
/// <para>
/// <b>The two amendment predicates and the ontology identity are grounded in bytes.</b> The probe
/// <c>lex-event-20260904T191531228Z-116c5e971e374b63a2350b481945b1d6</c> read both predicates from
/// the publisher's own store: <see cref="AmendedByPredicateUri"/> is declared there as an
/// <c>owl:ObjectProperty</c>, <c>rdfs:subPropertyOf cdm:amended_by</c>, with <c>rdfs:domain</c>
/// and <c>rdfs:range</c> both <c>cdm:resource_legal</c>, and carrying
/// <c>owl:inverseOf cdm:resource_legal_amends_resource_legal</c> (digest 2e010919fde5842e);
/// <see cref="AmendsPredicateUri"/> is confirmed as an exact IRI from a real triple (digest
/// 7599b577820d8ba0). Two follow-up probes ground the ontology:
/// <c>lex-event-20260904T193110376Z-b18eba465155421cbe2130d622b45cec</c> read its identity,
/// giving <see cref="OntologyUri"/> and <see cref="OntologyVersion"/> (digest
/// 6c918b286291c621944ec20b409ac794b25128f53dd39529fc07c55174f4bba9), and
/// <c>lex-event-20260904T193414533Z-2542714d22dc4ff19302247a7738aefb</c> established that the
/// declaration and that version resolve in the same named graph.
/// </para>
/// <para>
/// <b>Three relation predicates and the five annotation names still rest on canary prose.</b>
/// <see cref="RepealsPredicateUri"/> is named in
/// <c>lex-event-20260904T174651520Z-392411cf4e9446e2aa76bd3be3cc2c8a</c>, among the eight
/// axiom-annotated predicates on the GDPR work (digest daa5ed5518886be6) and in one repeals axiom
/// read in full (digest 4701a3361ff09048). <see cref="ConsolidatedBasedOnPredicateUri"/> and
/// <see cref="ConsolidatedConsolidatesPredicateUri"/> are named in the E4 scope ruling. The five
/// annotation local names come from
/// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c> (digest
/// 57e7814db94e13f5), which quotes them by local name only; <see cref="AnnotationNamespace"/>
/// therefore follows the single annotation IRI this repository already pins, used for
/// <c>type_of_date</c> in <c>EuDateAxiomTests</c>, and the namespace has not been verified against
/// the wire for these five properties. E4 live must confirm all four by bytes before any ingest.
/// </para>
/// <para>
/// <b>KEEP, IMPROVE, REFUSE against v2</b>, as the E4 scope ruling
/// <c>lex-event-20260904T183047960Z-5d333e769fa04864a6650984281eaaf1</c> requires be stated.
/// </para>
/// <para>
/// KEEP: v2's consolidation query shape, and its rule closing each dated state at the next
/// consolidation date minus one day, from <c>src/Lex.Sources.EurLex/EurLexAdapter.cs</c>
/// <c>ConsolidationsQuery</c> in the v2 repository, proven in review/22 section 3.
/// <see cref="EuConstituentClosure"/> walks the same two consolidation predicates that query
/// walks and preserves that closing rule's direction.
/// </para>
/// <para>
/// IMPROVE: v2 flattened relation edges to untyped rows, which is why its relations were present
/// as JSON but unindexed and why it has no EU equivalent of <c>cited_by</c>, a defect review/23
/// section 10 records against itself. E4 types the qualifiers
/// (<see cref="EuStructuralLocation"/>, <see cref="EuAuthorityQualifiedToken"/>,
/// <see cref="EuValidityDate"/>) instead of flattening them, so a qualifier survives the edge
/// rather than being dropped on the way into a row.
/// </para>
/// <para>
/// REFUSE: v2 mixed derived inverses into the same bundle as publisher assertions. Here an
/// asserted edge is an <see cref="EuRelationEdgeBinding"/>, which implements
/// <see cref="IEuFactsEvidenceCarrier"/>, and a derived inverse is a
/// <see cref="Lex.V3.Contracts.Facts.DerivedInverseRelation"/>, a Facts record that implements no
/// marker at all, so a bundle typed against the marker cannot hold one. REL-002's criterion is
/// that derived edges are excluded from bundles, and this is that exclusion made structural.
/// </para>
/// <para>
/// <b>Recorded as incomplete.</b> review/22 section 3 renders a location as a bare token sequence
/// with no authority IRIs. Against the live values that rendering is incomplete in three ways at
/// once: it drops the authority IRI each token actually carries inline, it is silent on
/// <c>role2</c> and therefore on the second authority list entirely, and it does not mention that
/// <c>start_of_validity</c> arrives in two different date formats. The canary events cited above
/// govern the shape, not that prose.
/// </para>
/// </remarks>
public static class EuAmendmentRelationVocabulary
{
    /// <summary>
    /// The CDM annotation namespace. See the type remarks: the local names below are observed and
    /// this prefix is this repository's existing spelling, not an independently verified one.
    /// </summary>
    public const string AnnotationNamespace = "http://publications.europa.eu/ontology/annotation#";

    /// <summary>
    /// The amendment predicate, amender to amended. The only direction the store materialises.
    /// </summary>
    /// <remarks>
    /// Confirmed as an exact IRI from a real triple in the publisher's store (probe
    /// <c>lex-event-20260904T191531228Z-116c5e971e374b63a2350b481945b1d6</c>, digest
    /// 7599b577820d8ba0).
    /// </remarks>
    public const string AmendsPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "resource_legal_amends_resource_legal";

    /// <summary>
    /// The inverse amendment predicate. Never read: an unfiltered store-wide query on this exact
    /// predicate returned zero rows (canary digest 21732a68993ff562), while the forward predicate
    /// returned rows immediately (digest 58c50d8c78ab80c9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Declared by the publisher, not inferred.</b> The probe
    /// <c>lex-event-20260904T191531228Z-116c5e971e374b63a2350b481945b1d6</c> (digest
    /// 2e010919fde5842e) read this property's own declaration in the store's ontology: an
    /// <c>owl:ObjectProperty</c>, <c>rdfs:subPropertyOf cdm:amended_by</c>, with
    /// <c>rdfs:domain</c> and <c>rdfs:range</c> both <c>cdm:resource_legal</c>, and
    /// <c>owl:inverseOf cdm:resource_legal_amends_resource_legal</c>. That declaration is exactly
    /// the axiom <see cref="Lex.V3.Contracts.Facts.ObservedInverseAxiom"/> exists to carry, and
    /// <see cref="EuDerivedAmendmentInverse"/> carries it.
    /// </para>
    /// <para>
    /// <b>This spelling is no longer load bearing, and that is deliberate.</b> An earlier version
    /// of E4 refused an inverse-predicate edge by comparing against this exact string, so a
    /// misspelling here silently disarmed the guard and nothing said a word. The closed
    /// <see cref="AssertedPredicates"/> set removed that fragility rather than protecting it: an
    /// unpinned predicate is now refused whatever it is spelled, so no misspelling of this constant
    /// can open a door. <b>Do not reintroduce a string-equality refusal against this constant</b>;
    /// that would restore the fragile shape the closed set replaced. The spelling still matters to
    /// <see cref="Lex.V3.Contracts.Facts.ObservedInverseAxiom"/>, which names it as the predicate
    /// the inversion maps to, and <c>EuAmendmentRelationTests</c> pins it there as an independently
    /// transcribed literal.
    /// </para>
    /// </remarks>
    public const string AmendedByPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "resource_legal_amended_by_resource_legal";

    /// <summary>The repeal predicate, repealing act to repealed act.</summary>
    public const string RepealsPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "resource_legal_repeals_resource_legal";

    /// <summary>The consolidated act to the act it is based on.</summary>
    public const string ConsolidatedBasedOnPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "act_consolidated_based_on_resource_legal";

    /// <summary>The consolidated act to the act it consolidates.</summary>
    public const string ConsolidatedConsolidatesPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "act_consolidated_consolidates_resource_legal";

    /// <summary>
    /// The closed set of predicates an E4 asserted edge may carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closed for the reason E6's <c>EuCaseLawPredicateVocabulary.Pinned</c> is closed: a binding
    /// accepting any syntactically valid absolute URI accepts a predicate nothing evidenced, and
    /// the resulting edge looks exactly like an evidenced one. Every member traces to an
    /// observation or a ruling named in the type remarks.
    /// </para>
    /// <para>
    /// <see cref="AmendedByPredicateUri"/> is deliberately absent. It returns zero rows store-wide,
    /// so no edge on it is a publisher assertion; it reaches the model only through
    /// <see cref="EuDerivedAmendmentInverse"/>, as a derived inverse authorised by the publisher's
    /// own <c>owl:inverseOf</c> declaration.
    /// </para>
    /// <para>
    /// <b>Closing this set moved which strings are load bearing, and the move is worth stating.</b>
    /// It removed the amended-by spelling's role in any guard (see its own remarks), and it gave
    /// one to three spellings that previously had none: <see cref="RepealsPredicateUri"/>,
    /// <see cref="ConsolidatedBasedOnPredicateUri"/> and
    /// <see cref="ConsolidatedConsolidatesPredicateUri"/> are now compared by string equality, here
    /// and in <see cref="EuConstituentStep.Create"/>. All three still rest on canary prose rather
    /// than on bytes. A wrong spelling in any of them would refuse real publisher data rather than
    /// admit invented data, which is the safer direction of failure, and it is still a reason E4
    /// live must confirm the three by bytes before any ingest.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyCollection<string> AssertedPredicates = new HashSet<string>(
        [
            AmendsPredicateUri,
            RepealsPredicateUri,
            ConsolidatedBasedOnPredicateUri,
            ConsolidatedConsolidatesPredicateUri,
        ],
        StringComparer.Ordinal);

    /// <summary>
    /// The CDM ontology the inverse declaration was read from.
    /// </summary>
    /// <remarks>
    /// Grounded from the store by probe
    /// <c>lex-event-20260904T193110376Z-b18eba465155421cbe2130d622b45cec</c> (digest
    /// 6c918b286291c621944ec20b409ac794b25128f53dd39529fc07c55174f4bba9):
    /// <c>http://publications.europa.eu/ontology/cdm</c> is declared <c>rdf:type owl:Ontology</c>,
    /// carrying <c>owl:versionInfo</c> 4.17.0, a title of "Common Data Model (CDM)" and a date of
    /// 2025-11-17, which confirms review/22's prose in bytes. Note this is the ontology IRI without
    /// the trailing hash that terminates the term namespace
    /// <c>EuConsolidationDiscoveryPlan.Cdm</c>: the ontology is the document, the namespace is the
    /// prefix its terms hang off.
    /// </remarks>
    public const string OntologyUri = "http://publications.europa.eu/ontology/cdm";

    /// <summary>
    /// The CDM version the <c>owl:inverseOf</c> declaration was observed at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version is bound to the declaration by bytes, not by inference.</b> Probe
    /// <c>lex-event-20260904T193414533Z-2542714d22dc4ff19302247a7738aefb</c> ran two GRAPH queries:
    /// the <c>owl:inverseOf</c> triple resolves in exactly one named graph,
    /// <c>http://publications.europa.eu/ontology/cdm</c> (digest 4f6eed0a205e2c1c), and the
    /// ontology's own <c>owl:versionInfo</c> resolves in that same named graph carrying 4.17.0
    /// (digest 069cfe8f658854eb). One graph holds both the declaration and the version describing
    /// it, so every part of the axiom's sentence is a byte someone can re-query. An earlier pass
    /// grounded the two facts separately without connecting them, and said so rather than letting
    /// the axiom imply a binding nobody had checked.
    /// </para>
    /// <para>
    /// <b>The caveat that travels with that.</b> Co-location is established as the store reports it
    /// today. It is <b>not</b> established that the store's graph naming is itself versioned, so a
    /// future CDM release could in principle replace that graph's contents without renaming it.
    /// This constant therefore records the version <i>observed</i>, and nothing here treats the
    /// graph name as a version.
    /// </para>
    /// <para>
    /// <b>A version observed, not a version pinned for acceptance.</b> Nothing here checks a live
    /// ontology against it, and nothing should on a contracts-only slice: if the publisher ships a
    /// later version with the declaration removed, this constant is a record of what was true when
    /// it was read rather than a claim about today, and E4 live is where it is re-read.
    /// </para>
    /// </remarks>
    public const string OntologyVersion = "4.17.0";

    /// <summary>The located amendment's structural location annotation.</summary>
    public const string ReferenceToModifiedLocationUri =
        AnnotationNamespace + "reference_to_modified_location";

    /// <summary>The annotation carrying the date the link starts to hold.</summary>
    public const string StartOfValidityUri = AnnotationNamespace + "start_of_validity";

    /// <summary>
    /// The annotation carrying the date the link stops holding. Absent on all five retained
    /// located axioms and on the retained repeals axiom, so it is genuinely optional.
    /// </summary>
    public const string EndOfValidityUri = AnnotationNamespace + "end_of_validity";

    /// <summary>The annotation carrying the target's link type. <c>MS</c> on every row retained.</summary>
    public const string TypeOfLinkTargetUri = AnnotationNamespace + "type_of_link_target";

    /// <summary>The annotation carrying the amendment role, drawn from fd_375.</summary>
    public const string Role2Uri = AnnotationNamespace + "role2";

    /// <summary>
    /// The structural location code list. Every <c>reference_to_modified_location</c> token is a
    /// member of this list and of no other.
    /// </summary>
    /// <remarks>
    /// Pinned as the <b>list</b> and not as a closed set of members, deliberately. The retained
    /// fixture shows only <c>AN</c> and <c>AR</c>, but the canary event
    /// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c> reads a real location
    /// carrying <c>PTA</c> as well (digest 3c02e412d4093247). Closing the member set to what the
    /// five retained rows happen to show would refuse real publisher data on the third code, so
    /// the authority that is closed is the list, and the member code is carried as observed.
    /// </remarks>
    public const string LocationAuthorityListUri =
        "http://publications.europa.eu/resource/authority/fd_370";

    /// <summary>
    /// The amendment role code list, which is a <b>different</b> list from
    /// <see cref="LocationAuthorityListUri"/>. Observed members: <c>R</c>, <c>J</c> and <c>M</c>.
    /// Pinned as the list for the same reason.
    /// </summary>
    public const string RoleAuthorityListUri =
        "http://publications.europa.eu/resource/authority/fd_375";

    /// <summary>
    /// Validates a <c>type_of_link_target</c> value and returns it unchanged.
    /// </summary>
    /// <remarks>
    /// Carried as observed rather than closed to <c>MS</c>. Every retained row, the five located
    /// amendment axioms and the one repeals axiom, carries <c>MS</c>, and six observations are not
    /// enough to close a publisher vocabulary. Closing it would turn the first other token the
    /// publisher uses into a refusal of real data, which costs more than carrying a token this
    /// slice has not seen.
    /// </remarks>
    public static string RequireLinkTargetType(string typeOfLinkTarget, string parameterName)
    {
        if (!EuAuthorityQualifiedToken.IsAdmittedCode(typeOfLinkTarget))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(typeOfLinkTarget)}\" is not a link-target "
                    + "type: a type is 1 to 64 printable ASCII characters carrying no space, brace "
                    + "or vertical bar.",
                parameterName);
        }

        return typeOfLinkTarget;
    }
}
