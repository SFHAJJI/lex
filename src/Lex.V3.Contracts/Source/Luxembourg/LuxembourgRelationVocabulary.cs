using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The closed Luxembourg (JOLux) relation-predicate vocabulary. Eighteen, per Decision 65: the LU
/// acquisition scope corrects Candidate 6's seventeen-member enumeration, which omitted
/// <c>jolux:cites</c>, because the V3 work dossier requires Cites and Cited-by and a bounded
/// inventory measured 77,653 asserted citation edges. This ruling changes acquisition scope only;
/// it does not turn an unacquired family into an empty set (Decision 64 remains controlling).
/// </summary>
/// <remarks>
/// <para>
/// The eighteen members and their declaration order are transcribed from the already-merged
/// <see cref="VerifiedLuxembourgSourceProfile"/>'s <c>RelationPredicate</c> vocabulary rows (this
/// file's sibling in the same directory, not edited by this slice), which is itself the exact
/// closed set Decision 65 names: Candidate 6's seventeen plus <c>cites</c>. Nine of the eighteen
/// (<see cref="HasIndirectImpact"/> through <see cref="ImpactConsolidatedByExpression"/>) are the
/// <c>jolux:LegalResourceImpact</c> family: Legilux's machine-readable "tableau des modifications"
/// dates, types and links each amendment to the consolidation that absorbed it
/// (review/22-research-relations.md section 4).
/// </para>
/// <para>
/// Tokens are the exact JOLux local predicate name (the full IRI is this local name appended to
/// <c>http://data.legilux.public.lu/resource/ontology/jolux#</c>), matching the convention the
/// merged source profile already uses for its own vocabulary rows.
/// </para>
/// </remarks>
public enum LuxembourgRelationPredicate
{
    [JsonStringEnumMemberName("modifies")]
    Modifies = 1,

    [JsonStringEnumMemberName("repeals")]
    Repeals = 2,

    [JsonStringEnumMemberName("rectifies")]
    Rectifies = 3,

    [JsonStringEnumMemberName("basedOn")]
    BasedOn = 4,

    [JsonStringEnumMemberName("transposes")]
    Transposes = 5,

    [JsonStringEnumMemberName("modifiedTempBy")]
    ModifiedTempBy = 6,

    [JsonStringEnumMemberName("hasIndirectImpact")]
    HasIndirectImpact = 7,

    [JsonStringEnumMemberName("legalAnalysisHasLegalResourceImpact")]
    LegalAnalysisHasLegalResourceImpact = 8,

    [JsonStringEnumMemberName("impactFromLegalResource")]
    ImpactFromLegalResource = 9,

    [JsonStringEnumMemberName("impactToLegalResource")]
    ImpactToLegalResource = 10,

    [JsonStringEnumMemberName("impactToExpression")]
    ImpactToExpression = 11,

    [JsonStringEnumMemberName("legalResourceImpactHasDateEntryInForce")]
    LegalResourceImpactHasDateEntryInForce = 12,

    [JsonStringEnumMemberName("legalResourceImpactHasType")]
    LegalResourceImpactHasType = 13,

    [JsonStringEnumMemberName("impactConsolidatedBy")]
    ImpactConsolidatedBy = 14,

    [JsonStringEnumMemberName("impactConsolidatedByExpression")]
    ImpactConsolidatedByExpression = 15,

    [JsonStringEnumMemberName("basicAct")]
    BasicAct = 16,

    /// <summary>
    /// Decision 58(b): retained with asserted direction and typed semantics. Never amendment
    /// attribution, never part of the boundary-date matching algorithm. Like every other predicate
    /// in this vocabulary, this codebase pins no ontology-authorized inverse for it: a grep of the
    /// entire coordination pack found no JOLux inverse mapping pinned for any predicate at all (see
    /// <see cref="Cites"/>'s remarks), so it may only ever carry
    /// <see cref="LuxembourgRelationAuthority.PublisherAsserted"/> authority, or a
    /// <see cref="LuxembourgRelationAuthority.LocalInboundView"/> that (like any other family's)
    /// claims no publisher predicate.
    /// </summary>
    [JsonStringEnumMemberName("consolidates")]
    Consolidates = 17,

    /// <summary>
    /// Decision 65's correction to Candidate 6: acquisition scope now includes <c>cites</c> because
    /// the product dossier requires answering citation walks in both directions ("Cites and
    /// Cited-by").
    /// </summary>
    /// <remarks>
    /// An earlier version of this slice claimed <c>cited_by</c> as a pinned "ontology-authorized
    /// inverse" of this predicate. That claim did not hold up: a grep of the entire coordination
    /// pack found no accepted text that pins a JOLux inverse for <c>cites</c>. The only pinned
    /// inverse pair found anywhere in the pack is the EU CDM's <c>work_cites_work</c> /
    /// <c>work_cited_by_work</c> (<c>coordination/measurements/D1-EU-CDM-ONTOLOGY-IDENTITY-2026-09-01.md</c>),
    /// a different ontology, a different namespace, and a different publisher's pinned
    /// <c>owl:inverseOf</c> assertion; it authorizes nothing about JOLux. Candidate 5 R4 (lines
    /// 537-554) is explicit that "an inverse is derived only when an exact inverse mapping is
    /// frozen from the pinned ontology. Otherwise V3 may expose a generic locally derived inbound
    /// view that is never labeled with a publisher predicate." With no JOLux pin found, Decision
    /// 65's "Cited-by" requirement is met the second way: <c>cited_by</c> is a
    /// <see cref="LuxembourgRelationAuthority.LocalInboundView"/>, exactly like the other seventeen
    /// families, carrying no publisher-predicate label and no ontology-authorized-inverse claim.
    /// <see cref="LuxembourgLocalInboundView"/> is the optional derived_from record a downstream
    /// representation may attach to name which exact family a local view transposes.
    /// </remarks>
    [JsonStringEnumMemberName("cites")]
    Cites = 18,
}

/// <summary>
/// Whose claim a Luxembourg relation edge is. Mirrors <c>EuRelationAuthority</c>
/// (<c>src/Lex.V3.Contracts/EuScopeDimensions.cs</c>) so the two publishers share one vocabulary
/// shape for the same underlying distinction: a derived inverse and a publisher assertion are
/// different facts about the world, and only one of them can be checked against the publisher.
/// </summary>
/// <remarks>
/// Unlike <c>EuRelationAuthority</c>, this vocabulary has no <c>ontology_authorized_inverse</c>
/// member. EU's CDM ontology has a pinned <c>owl:inverseOf</c> assertion for its own citation pair
/// (<c>work_cites_work</c> / <c>work_cited_by_work</c>); no equivalent pin exists anywhere in the
/// accepted JOLux text for any predicate (see <see cref="LuxembourgRelationPredicate.Cites"/>'s
/// remarks). Carrying the member here without a pin behind it was the defect: a type that claims
/// "ontology-authorized" for a mapping nobody's ontology authorizes.
/// </remarks>
public enum LuxembourgRelationAuthority
{
    /// <summary>The publisher asserts this edge in this direction.</summary>
    [JsonStringEnumMemberName("publisher_asserted")]
    PublisherAsserted = 1,

    /// <summary>
    /// Computed locally from held edges. Permanently unlabelled with any publisher predicate and
    /// excluded from evidence export. R4: "Otherwise V3 may expose a generic locally derived
    /// inbound view that is never labeled with a publisher predicate." A caller using this
    /// authority may optionally attach a <see cref="LuxembourgLocalInboundView"/> naming the exact
    /// family it transposes (R4: "each derived inverse is exactly one transpose with a
    /// derived_from edge").
    /// </summary>
    [JsonStringEnumMemberName("local_inbound_view")]
    LocalInboundView = 2,
}

/// <summary>
/// One locally computed inbound view's derived_from link: the exact one publisher-asserted family
/// it transposes, and the local descriptive label (if any) it is shown under.
/// </summary>
/// <remarks>
/// <para>
/// R4 (Candidate 5, lines 537-554): "Each derived inverse is exactly one transpose with a
/// derived_from edge." This record is that link for the locally-computed case: a single enum
/// property, so a caller cannot name two families for one view, structurally the same "exactly
/// one" discipline R4 asks for, applied to the only kind of derived inverse this codebase actually
/// builds for JOLux today (see <see cref="LuxembourgRelationPredicate.Cites"/>'s remarks: no
/// ontology-authorized inverse survives here because none is pinned in the accepted text).
/// </para>
/// <para>
/// <see cref="InverseLabel"/> is a local, descriptive term only (Decision 65's own "Cited-by"
/// language for <see cref="LuxembourgRelationPredicate.Cites"/>), never a publisher predicate:
/// checked in <c>LuxembourgRelationVocabularyTests.InverseLabelsNeverShareAPublisherPredicate</c>.
/// It also is not the same thing as the <c>"cited_by"</c> MCP operation id in
/// <c>V3ContractVocabulary.OperationIds</c>; that is an unrelated vocabulary in a different
/// namespace that happens to spell one of its members the same way.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuxembourgLocalInboundView
{
    /// <summary>
    /// Every wire token a real publisher predicate carries, across both closed vocabularies. Built
    /// once from the enums themselves (not a second hand-transcribed list) so an addition to either
    /// vocabulary is picked up automatically rather than left for this guard to fall behind.
    /// </summary>
    private static readonly IReadOnlySet<string> PublisherPredicateTokens = BuildPublisherPredicateTokens();

    [JsonConstructor]
    public LuxembourgLocalInboundView(LuxembourgRelationPredicate derivedFrom, string inverseLabel)
    {
        DerivedFrom = ContractValidation.RequireDefined(derivedFrom, nameof(derivedFrom));
        InverseLabel = ContractValidation.RequireIdentifier(inverseLabel, nameof(inverseLabel));

        // Fold-in: RequireIdentifier only bounds the label to printable ASCII, so
        // new LuxembourgLocalInboundView(Cites, "modifies") constructed even though this type's own
        // documentation says a label is never a publisher predicate. A label equal to any relation
        // or assertion wire token is refused here instead of merely being documented as wrong.
        if (PublisherPredicateTokens.Contains(InverseLabel))
        {
            throw new ArgumentException(
                $"\"{InverseLabel}\" is a real publisher predicate token; a locally derived " +
                "inbound view's label must never collide with one.",
                nameof(inverseLabel));
        }
    }

    /// <summary>The exact one publisher-asserted family this view is the transpose of.</summary>
    public LuxembourgRelationPredicate DerivedFrom { get; }

    /// <summary>A local descriptive label. Never a publisher predicate token.</summary>
    public string InverseLabel { get; }

    private static IReadOnlySet<string> BuildPublisherPredicateTokens()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var predicate in Enum.GetValues<LuxembourgRelationPredicate>())
        {
            tokens.Add(ContractJson.Serialize(predicate).Trim('"'));
        }

        foreach (var predicate in Enum.GetValues<LuxembourgAssertionPredicate>())
        {
            tokens.Add(ContractJson.Serialize(predicate).Trim('"'));
        }

        return tokens;
    }
}

/// <summary>The closed Luxembourg relation vocabularies, enumerable rather than hand-counted.</summary>
public static class LuxembourgRelationVocabulary
{
    /// <summary>Every relation predicate. Eighteen.</summary>
    public static IReadOnlyList<LuxembourgRelationPredicate> Predicates { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgRelationPredicate>());

    /// <summary>Every relation authority. Two.</summary>
    public static IReadOnlyList<LuxembourgRelationAuthority> Authorities { get; } =
        Array.AsReadOnly(Enum.GetValues<LuxembourgRelationAuthority>());
}
