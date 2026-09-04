namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The pinned EUR-Lex vocabulary behind stage 2 item E4 (ledger row <c>REL-002</c>): the relation
/// predicates, the reified annotation properties those relations carry, and the two authority
/// lists their qualifier values are drawn from.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is grounded in retained bytes, and what is not.</b> The retained fixture
/// <c>amends-located-axioms.json</c> (2968 bytes, sha256
/// <c>d3353e41e9091b202970dae3ef5ec7be063a5b6dc5afcf30c12ada2b4fe01ffd</c>) is a SPARQL SELECT
/// result whose columns are <c>src</c>, <c>tgt</c>, <c>loc</c>, <c>start</c>, <c>linktype</c> and
/// <c>role</c>. A SELECT returns bound values, never the predicate IRIs that bound them, so the
/// fixture grounds the <b>values</b> in <see cref="LocationAuthorityListUri"/> and
/// <see cref="RoleAuthorityListUri"/> exactly, byte for byte, and grounds <b>none</b> of the
/// predicate or annotation IRIs below. Those come from the canary event
/// <c>lex-event-20260904T175313280Z-e99a2a04ab2e44fb8bc5a5aa66d14451</c>, which names the five
/// distinct annotation properties on an amends axiom (its own digest 57e7814db94e13f5) and the
/// forward and inverse relation predicates (digests 58c50d8c78ab80c9 and 21732a68993ff562), and
/// from <c>lex-event-20260904T174651520Z-392411cf4e9446e2aa76bd3be3cc2c8a</c>, which names
/// <c>resource_legal_repeals_resource_legal</c> among the eight axiom-annotated predicates on the
/// GDPR work (digest daa5ed5518886be6) and reads one repeals axiom in full (digest
/// 4701a3361ff09048).
/// </para>
/// <para>
/// <b>The one deliberately weaker claim.</b> Those events name the annotation properties by local
/// name only, as <c>annotation#start_of_validity</c> and its four siblings. They never quote the
/// namespace in full. <see cref="AnnotationNamespace"/> therefore follows the single annotation
/// IRI this repository already pins, <c>http://publications.europa.eu/ontology/annotation#</c>,
/// used for <c>type_of_date</c> in <c>EuDateAxiomTests</c>. The local names are observed; the
/// namespace is this repository's existing spelling carried forward, and it has not been
/// independently verified against the wire for these five properties. This is the same honesty
/// <see cref="EuNalSchemeIdentity"/> already records for the fd_335 host, and it is stated here
/// rather than left for a reader to discover.
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
/// walks and preserves that closing rule's direction, so the shape v2 proved is carried over
/// rather than reinvented.
/// </para>
/// <para>
/// IMPROVE: v2 flattened relation edges to untyped rows, which is why its relations were present
/// as JSON but unindexed and why it has no EU equivalent of <c>cited_by</c>, a defect review/23
/// section 10 records against itself. E4 types the target state
/// (<see cref="EuRelationTargetState"/>) and the axiom qualifiers
/// (<see cref="EuLocatedAmendmentAxiom"/>, <see cref="EuRepealEdge"/>) instead of flattening
/// them, so a qualifier survives the edge rather than being dropped on the way into a row. It
/// also types the publisher assertion and its derived inverse as two different types rather than
/// one row with a direction column, which is what makes admissibility decidable.
/// </para>
/// <para>
/// REFUSE: v2 mixed derived inverses into the same bundle as publisher assertions. Here the two
/// are separate types, and only <see cref="EuPublisherRelationEdge"/> implements
/// <see cref="IEuFactsEvidenceCarrier"/>. <see cref="EuDerivedInverseRelationEdge"/> does not and
/// never will, so a derived inverse cannot reach an evidence bundle typed against that marker at
/// all. REL-002's own criterion is that derived edges are excluded from bundles, and this is that
/// exclusion made structural rather than documented.
/// </para>
/// <para>
/// <b>Recorded as incomplete.</b> review/22 section 3 renders a location as the bare
/// <c>{AR} 54 {PA} 1 {PTA} (e)</c> shape. Against the live values that rendering is incomplete in
/// three ways at once: it drops the authority IRI each token actually carries inline, it is
/// silent on <c>role2</c> and therefore on the second authority list entirely, and it does not
/// mention that <c>start_of_validity</c> arrives in two different date formats. The canary events
/// cited above govern the shape, not that prose.
/// </para>
/// </remarks>
public static class EuAmendmentRelationVocabulary
{
    private const string CdmNamespace = "http://publications.europa.eu/ontology/cdm#";

    /// <summary>
    /// The CDM annotation namespace. See the type remarks: the local names below are observed and
    /// this prefix is this repository's existing spelling, not an independently verified one.
    /// </summary>
    public const string AnnotationNamespace = "http://publications.europa.eu/ontology/annotation#";

    /// <summary>
    /// The amendment predicate, amender to amended. The only direction the store materialises.
    /// </summary>
    public const string AmendsPredicateUri = CdmNamespace + "resource_legal_amends_resource_legal";

    /// <summary>
    /// The inverse amendment predicate. Never read: an unfiltered store-wide query on this exact
    /// predicate returned zero rows (canary digest 21732a68993ff562), while the forward predicate
    /// returned rows immediately (digest 58c50d8c78ab80c9). An edge on this predicate can only
    /// ever be locally derived, which is why <see cref="EuPublisherRelationEdge"/> refuses this
    /// predicate outright.
    /// </summary>
    public const string AmendedByPredicateUri =
        CdmNamespace + "resource_legal_amended_by_resource_legal";

    /// <summary>The repeal predicate, repealing act to repealed act.</summary>
    public const string RepealsPredicateUri = CdmNamespace + "resource_legal_repeals_resource_legal";

    /// <summary>The consolidated act to the act it is based on.</summary>
    public const string ConsolidatedBasedOnPredicateUri =
        CdmNamespace + "act_consolidated_based_on_resource_legal";

    /// <summary>The consolidated act to the act it consolidates.</summary>
    public const string ConsolidatedConsolidatesPredicateUri =
        CdmNamespace + "act_consolidated_consolidates_resource_legal";

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
