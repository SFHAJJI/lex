using System.Text.Json.Serialization;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// One EU case-law link exactly as R4/REL-005 requires it be kept, built on the already-merged
/// Facts relation layer (<see cref="RelationFact"/>, <see cref="EcliState"/>,
/// <see cref="TargetBodyScope"/>) rather than a parallel vocabulary. Stage 2 item E6, ledger row
/// <c>REL-005</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ledger's "ecli_missing" and the wire token, reconciled in one place.</b> R4
/// (coordination/D1-01-OFFICIAL-SOURCE-BOUNDARY-CANDIDATE-5-2026-08-31.md: "A Cellar case relation
/// without ECLI remains under its Cellar or CELEX identity with typed <c>ecli_missing</c>...") and
/// the requirement ledger both use the name <c>ecli_missing</c>. No wire token by that name is ever
/// minted anywhere in this codebase. The Facts layer's <see cref="EcliState.EcliNotInThisSet"/> is
/// the correct, provable implementation of what R4 actually needs (a typed state, the edge never
/// dropped, no ECLI ever invented): it states that <b>this identity set</b> carries no ECLI, never
/// that the publisher has none. Coverage of exactly that distinction already exists in the merged
/// Facts layer
/// (<c>tests/Lex.V3.Tests/Facts/FactsHostileTests.cs</c>,
/// <c>EcliNotInThisSetDescribesTheSetRatherThanThePublisher</c>, which asserts the wire vocabulary
/// never carries the string <c>ecli_missing</c>). This file's own fixtures
/// (<c>EuCaseLawLinkTests.cs</c>) repeat the same proof against real EU case-law shapes rather than
/// leave it evidenced only by Luxembourg-flavoured examples. The scope ruling accepting this
/// mapping is recorded at coordination/EVENTS.md event
/// <c>lex-event-20260904T040310991Z-dc5a156f7293412b9680a24f44182bc5</c>.
/// </para>
/// <para>
/// <b>Why this is a thin binding rather than a parallel vocabulary.</b> Mirrors the precedent
/// <see cref="EuDateAxiomBinding"/> set for Stage 2 item E1: the already-merged Facts relation
/// layer already carries the ECLI-state machinery (<see cref="EcliState"/>), the held/not-held
/// state of a target's body (<see cref="TargetBodyScope"/>), and the exactly-one-edge-shape
/// invariant. Nothing here reimplements any of that. What review/23-research-temporal.md sections
/// 7 and 11 show is missing from Facts, because it is EU-case-law-specific rather than a property
/// every <see cref="RelationFact"/> across both publishers needs, is added here instead of there:
/// which real CDM predicates this lane vouches for (<see cref="EuCaseLawPredicateVocabulary"/>),
/// that the judgment text behind a case-law link is never held or fetched by this contract
/// (<see cref="EuJudgmentBodyDisposition"/>), and that the link is always act-level, never
/// article-level (<see cref="EuCaseLawGranularity"/>). Facts/RelationFact.cs and
/// Facts/FactsVocabulary.cs are untouched by this lane.
/// </para>
/// <para>
/// <b>Why the ECLI state is computed, never caller-supplied.</b> <see cref="Create"/> takes no
/// <see cref="EcliState"/> parameter at all. <see cref="RelationFact"/>'s own constructor already
/// checks a caller-supplied state against the target's identity set and refuses a mismatch, but
/// this binding removes the chance of ever supplying the wrong one to begin with: it reads
/// <c>target.IsCase()</c> and <c>target.Has(FactsIdentifierFamily.Ecli)</c> (both
/// <see cref="OfficialIdentitySet"/>'s own members; no new ECLI shape check is written here, per
/// the scope ruling's fourth precision) and derives exactly one of the three states, in exactly
/// one place.
/// </para>
/// <para>
/// <b>Why two predicates, not one, and why not a third.</b> The scope ruling's second precision
/// asks for "the real CDM predicate (case law interpretes resource legal) on the edge." That
/// predicate's own name fixes its direction by the publisher's own domain/range convention
/// evidenced throughout review/23 section 7 (<c>{domain}_{predicate}_{range}</c>, e.g.
/// <c>measure_national_implementing_implements_resource_legal</c>): the case-law work is always
/// the subject and the <c>resource_legal</c> is always the object, and <c>resource_legal</c> is
/// itself a distinct CDM legal subclass from <c>case-law</c> (section 7's FRBR class list). A
/// <see cref="RelationFact"/> checks <see cref="EcliState"/> against the carried edge's
/// <b>target</b>, so an edge asserted in this predicate's real direction can only ever put a
/// non-case at the target and can only ever type-check as
/// <see cref="EcliState.EcliNotApplicable"/>: the <c>SchremsIiInterpretsGdpr</c> fixture in
/// <c>EuCaseLawLinkTests.cs</c> proves exactly this, with both the case and the interpreted act's
/// identities real
/// (review/23-research-temporal.md section 11: "Schrems II case-law_interpretes_resource_legal
/// lists 31995L0046, 32016R0679, 12007P/TXT and Charter articles"). Proving
/// <see cref="EcliState.EcliPresent"/> and <see cref="EcliState.EcliNotInThisSet"/> against a real
/// EU case therefore needs a second real predicate whose direction can place a case at the target.
/// <c>work_cites_work</c> is pinned for this: review/23 section 7 lists it among the observed CDM
/// predicates and section 11 evidences it directly and generically ("2,257 works cite it via
/// <c>work_cites_work</c> ... including CA summary records and orders with ECLI"), with no
/// restriction to any one FRBR class pair, so a case-law work citing another case-law work is a
/// real, ordinary shape of this predicate. Two further predicates section 7 also names,
/// <c>resource_legal_amended_by_case-law</c> and
/// <c>case-law_declares_void_by_preliminary_ruling_resource_legal</c>, would likewise place a case
/// at the target, but neither carries a worked instance example anywhere in review/23: pinning
/// either here would let a caller assert a specific judicial outcome (an act ruled void or amended
/// by a named case) this lane has no evidence for, which is a materially stronger and more easily
/// misleading claim than a citation. They are deliberately left out.
/// </para>
/// <para>
/// <b>Granularity and judgment-text disposition are fixed, not parameters.</b> Both of
/// <see cref="Create"/>'s outputs on this axis are always the same value, because both predicates
/// pinned here relate whole works to whole works (never to an article or paragraph within one:
/// review/23 line 109 names <c>reference_to_modified_location</c>, a different, not-yet-exploited
/// mechanism, as the one that would carry article-level detail), and because review/23 section 11
/// records that judgment text "has judgment-date semantics, not validity intervals, matching the
/// Lex roadmap note that CJEU is a separate source class": this lane never holds or fetches
/// judgment text under any condition. An enum member that a mutation could flip without any test
/// noticing is exactly the shape this codebase's own remarks elsewhere warn against (see
/// <see cref="DateOpenSentinel"/>'s removed <c>not_yet_determined</c> member), so neither enum
/// declares an alternative this binding can never produce.
/// </para>
/// </remarks>
public sealed class EuCaseLawLinkBinding
{
    private EuCaseLawLinkBinding(
        RelationFact fact,
        EuCaseLawGranularity granularity,
        EuJudgmentBodyDisposition judgmentBodyDisposition)
    {
        Fact = fact;
        Granularity = granularity;
        JudgmentBodyDisposition = judgmentBodyDisposition;
    }

    /// <summary>The relation fact this binding qualifies.</summary>
    public RelationFact Fact { get; }

    /// <summary>Always <see cref="EuCaseLawGranularity.ActLevel"/>. See the type remarks.</summary>
    public EuCaseLawGranularity Granularity { get; }

    /// <summary>
    /// Always <see cref="EuJudgmentBodyDisposition.LinkOnlyNeverHeldOrFetched"/>. See the type
    /// remarks.
    /// </summary>
    public EuJudgmentBodyDisposition JudgmentBodyDisposition { get; }

    /// <summary>
    /// The only path that mints a binding. Builds the <see cref="PublisherRelation"/> and the
    /// <see cref="RelationFact"/> it wraps in one step, computing <see cref="EcliState"/> exactly
    /// once from <paramref name="target"/> itself rather than accepting it as an input a caller
    /// could get wrong.
    /// </summary>
    /// <param name="source">
    /// The edge's subject exactly as the publisher asserted it (the case-law work for
    /// <see cref="EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri"/>;
    /// either work for <see cref="EuCaseLawPredicateVocabulary.WorkCitesWorkPredicateUri"/>).
    /// </param>
    /// <param name="target">
    /// The edge's object exactly as the publisher asserted it. Its own identity set decides the
    /// resulting <see cref="EcliState"/>: a case with a declared ECLI reaches
    /// <see cref="EcliState.EcliPresent"/>, a case with none reaches
    /// <see cref="EcliState.EcliNotInThisSet"/>, and anything that is not a case reaches
    /// <see cref="EcliState.EcliNotApplicable"/>.
    /// </param>
    /// <param name="predicateUri">
    /// Must be one of <see cref="EuCaseLawPredicateVocabulary"/>'s pinned predicates. Any other
    /// value, including a syntactically valid but unpinned absolute URI, is refused.
    /// </param>
    /// <param name="targetBodyScope">
    /// Whether <paramref name="target"/>'s own body is held, independent of the judgment-text
    /// disposition this binding always fixes: for the
    /// <see cref="EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri"/>
    /// direction the target is the interpreted act, whose full text this corpus may hold.
    /// </param>
    /// <param name="qualifiedAxioms">
    /// Every <c>owl:Axiom</c> qualifier the publisher attached to this edge, or an empty list.
    /// Neither pinned predicate has an observed qualifier example in review/23.
    /// </param>
    /// <param name="sourceObservationId">The custody coordinate for the observation this edge came from.</param>
    public static EuCaseLawLinkBinding Create(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        string predicateUri,
        TargetBodyScope targetBodyScope,
        IReadOnlyList<QualifiedAxiom> qualifiedAxioms,
        string sourceObservationId)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(qualifiedAxioms);

        if (predicateUri is null || !EuCaseLawPredicateVocabulary.Pinned.Contains(predicateUri))
        {
            throw new ArgumentException(
                $"\"{predicateUri}\" is not one of the pinned, review/23-evidenced EU case-law " +
                "predicates.",
                nameof(predicateUri));
        }

        // The only place a case-law link's EcliState is decided. No caller ever supplies it, so
        // there is no channel through which the wrong state could reach the underlying
        // RelationFact even by mistake. The shape check is OfficialIdentitySet's own
        // (IsCase, Has), never a new one, per the scope ruling's fourth precision.
        var ecliState = target.IsCase()
            ? target.Has(FactsIdentifierFamily.Ecli) ? EcliState.EcliPresent : EcliState.EcliNotInThisSet
            : EcliState.EcliNotApplicable;

        var publisherRelation = new PublisherRelation(
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
            ecliState,
            publisherRelation,
            null,
            null);

        return new EuCaseLawLinkBinding(
            fact, EuCaseLawGranularity.ActLevel, EuJudgmentBodyDisposition.LinkOnlyNeverHeldOrFetched);
    }
}

/// <summary>
/// The exact, closed set of real EU case-law CDM predicates review/23-research-temporal.md
/// evidences with a worked instance, and the only predicates <see cref="EuCaseLawLinkBinding.Create"/>
/// accepts.
/// </summary>
/// <remarks>
/// review/23 section 7 also names <c>case-law_declares_void_by_preliminary_ruling_resource_legal</c>
/// and <c>resource_legal_amended_by_case-law</c>. Neither is pinned here: section 11 gives a worked
/// instance for <c>case-law_interpretes_resource_legal</c> (Schrems II) and for <c>work_cites_work</c>
/// (2,257 citations to GDPR), but no worked instance for either of the other two, and admitting a
/// predicate with no observed instance would let a caller assert a specific judicial outcome (an
/// act ruled void, or amended, by a named case) this lane cannot evidence.
/// </remarks>
public static class EuCaseLawPredicateVocabulary
{
    /// <summary>
    /// "Schrems II case-law_interpretes_resource_legal lists 31995L0046, 32016R0679, 12007P/TXT
    /// and Charter articles" (review/23 section 11). Subject is always the case-law work, object
    /// is always a <c>resource_legal</c>, so an edge on this predicate can never place a case at
    /// its target.
    /// </summary>
    public const string CaseLawInterpretesResourceLegalPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "case-law_interpretes_resource_legal";

    /// <summary>
    /// "2,257 works cite it via work_cites_work ... including CA summary records and orders with
    /// ECLI" (review/23 section 11). Generic across every FRBR class the publisher mints, so a
    /// case-law work citing another case-law work is a real, ordinary shape of this predicate and
    /// the one this lane uses to place a case at an edge's target.
    /// </summary>
    public const string WorkCitesWorkPredicateUri = EuConsolidationDiscoveryPlan.Cdm + "work_cites_work";

    internal static readonly IReadOnlyCollection<string> Pinned = new HashSet<string>(
        [CaseLawInterpretesResourceLegalPredicateUri, WorkCitesWorkPredicateUri],
        StringComparer.Ordinal);
}

/// <summary>
/// Whether an EU case-law link points at a whole act or at one article or paragraph within it.
/// </summary>
/// <remarks>
/// Carries exactly one member. Both predicates <see cref="EuCaseLawPredicateVocabulary"/> pins
/// relate whole works to whole works; review/23 line 109 names
/// <c>reference_to_modified_location</c>, a separate and not-yet-exploited mechanism, as the one
/// that would carry article-level detail. No member for that case is declared here because
/// <see cref="EuCaseLawLinkBinding.Create"/> has no path that could ever produce one, and an enum
/// member no code path can reach is the unreachable-guard shape this codebase's own remarks
/// elsewhere warn against.
/// </remarks>
public enum EuCaseLawGranularity
{
    /// <summary>The link is to the act (or case) as a whole, never to a specific article.</summary>
    [JsonStringEnumMemberName("act_level")]
    ActLevel = 1,
}

/// <summary>
/// Whether the judgment text behind an EU case-law link is held or fetched by this contract.
/// </summary>
/// <remarks>
/// Carries exactly one member for the same reason as <see cref="EuCaseLawGranularity"/>:
/// review/23 section 11 records that judgment text "has judgment-date semantics, not validity
/// intervals, matching the Lex roadmap note that CJEU is a separate source class," and this
/// binding never holds or fetches one under any condition, so no alternative member could ever be
/// produced.
/// </remarks>
public enum EuJudgmentBodyDisposition
{
    /// <summary>
    /// The link names the judgment (by its case-law identity) and never holds or fetches its
    /// text. Only the link's own metadata is asserted.
    /// </summary>
    [JsonStringEnumMemberName("link_only_never_held_or_fetched")]
    LinkOnlyNeverHeldOrFetched = 1,
}
