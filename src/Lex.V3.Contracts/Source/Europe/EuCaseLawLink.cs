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
/// <b>This is a rework, not the original E6 head.</b> The design objection
/// (coordination/EVENTS.md event <c>lex-event-20260904T044207644Z-8b9be4b0357f4f798a4489b562d2f1e7</c>)
/// held that REL-005 and R4 line 547
/// ("A Cellar case relation without ECLI remains under its Cellar or CELEX identity with typed
/// <c>ecli_missing</c> across every accepted case-link family") are about the <b>case's own</b>
/// ECLI, and that in the only direction review/23-research-temporal.md actually evidences with a
/// worked instance (section 7, line 91: "Schrems II case-law_interpretes_resource_legal lists
/// 31995L0046, 32016R0679, 12007P/TXT and Charter articles"), the judgment is the edge's
/// <b>subject</b>, not its target. The prior head proved <see cref="EcliState.EcliPresent"/> and
/// <see cref="EcliState.EcliNotInThisSet"/> only through <c>work_cites_work</c> with a judgment
/// placed at the target, a shape review/23 does not evidence with a worked instance and the file
/// inferred only from the absence of an explicit restriction on that predicate, while on the
/// evidenced direction the judgment's own ECLI sat unread in the source identity set and the
/// underlying <see cref="RelationFact"/> reported <see cref="EcliState.EcliNotApplicable"/>
/// (correctly, because <see cref="RelationFact"/>'s own ECLI-state field always describes its
/// <b>target</b>, and the target of <c>case-law_interpretes_resource_legal</c> is a
/// <c>resource_legal</c>, never a case). <see cref="CaseSide"/> and <see cref="CaseEcliState"/>
/// below are the fix: the case's own ECLI state, read from whichever side of the edge actually
/// carries the case.
/// </para>
/// <para>
/// <b>The ledger's "ecli_missing" and the wire token, reconciled in one place.</b> R4 and the
/// requirement ledger both use the name <c>ecli_missing</c>. No wire token by that name is ever
/// minted anywhere in this codebase. The Facts layer's <see cref="EcliState.EcliNotInThisSet"/> is
/// the correct, provable implementation of what R4 actually needs (a typed state, the edge never
/// dropped, no ECLI ever invented): it states that <b>this identity set</b> carries no ECLI, never
/// that the publisher has none. Coverage of exactly that distinction already exists in the merged
/// Facts layer
/// (<c>tests/Lex.V3.Tests/Facts/FactsHostileTests.cs</c>,
/// <c>EcliNotInThisSetDescribesTheSetRatherThanThePublisher</c>, which asserts the wire vocabulary
/// never carries the string <c>ecli_missing</c>). This file's own fixtures
/// (<c>EuCaseLawLinkTests.cs</c>) repeat the same proof against real EU case-law shapes, now on the
/// direction review/23 actually evidences. The scope ruling accepting this mapping is recorded at
/// coordination/EVENTS.md event
/// <c>lex-event-20260904T040310991Z-dc5a156f7293412b9680a24f44182bc5</c>.
/// </para>
/// <para>
/// <b>Why this is a thin binding rather than a parallel vocabulary.</b> Mirrors the precedent
/// <see cref="EuDateAxiomBinding"/> set for Stage 2 item E1: the already-merged Facts relation
/// layer already carries the ECLI-state machinery (<see cref="EcliState"/>), the held/not-held
/// state of a target's body (<see cref="TargetBodyScope"/>), and the exactly-one-edge-shape
/// invariant. Nothing here reimplements any of that. What review/23-research-temporal.md section 3
/// (the CDM FRBR class and predicate lists, lines 50 and 54), section 7 (case law and judgment
/// text, lines 91 and 92) and section 10 (article-level granularity, line 109) show is missing
/// from Facts, because it is EU-case-law-specific rather than a property every
/// <see cref="RelationFact"/> across both
/// publishers needs, is added here instead of there: which real CDM predicates this lane vouches
/// for (<see cref="EuCaseLawPredicateVocabulary"/>), which side of an edge is actually the case
/// (<see cref="EuCaseLawLinkCaseSide"/>), that the judgment text behind a case-law link is never
/// held or fetched by this contract (<see cref="EuJudgmentBodyDisposition"/>), and that the link is
/// always act-level, never article-level (<see cref="EuCaseLawGranularity"/>). Facts/RelationFact.cs
/// and Facts/FactsVocabulary.cs are untouched by this lane: everything this binding needs
/// (<see cref="OfficialIdentitySet.IsCase"/>, <see cref="OfficialIdentitySet.Has"/>,
/// <see cref="OfficialIdentitySet.Value"/>, and the three-state invariant shape
/// <see cref="RelationFact"/>'s own constructor already enforces against its target) already
/// exists there; this binding computes the same three-state shape a second time, independently,
/// against whichever side is the case, rather than needing Facts to grow a source-side accessor of
/// its own.
/// </para>
/// <para>
/// <b>Which side is the case, decided once and carried explicitly.</b> Neither
/// <see cref="RelationFact"/> nor <see cref="OfficialIdentitySet"/> names this: a caller holding a
/// finished <see cref="RelationFact"/> has no field to read that says which side the publisher
/// asserted as the case-law work, only the means to re-derive it by calling
/// <see cref="OfficialIdentitySet.IsCase"/> on both ends again, ad hoc, every time. <see cref="Create"/>
/// decides it once, from the two identity sets it was actually given, and <see cref="CaseSide"/>
/// carries the answer as an explicit, inspectable field. A link where <b>neither</b> side is a case
/// is refused outright, named for what it is: not a case-law link at all. A link where <b>both</b>
/// sides are a case (a judgment naming another judgment as its own identity set would have to,
/// which no predicate pinned here has a worked instance of) is refused too, rather than picking a
/// side arbitrarily and silently: this binding names exactly one case side, and an edge with two
/// candidates has none it can name honestly.
/// </para>
/// <para>
/// <b>Why the ECLI state is computed, never caller-supplied, and mirrors <see cref="RelationFact"/>'s
/// own invariant.</b> <see cref="Create"/> takes no <see cref="EcliState"/> parameter and no
/// <see cref="EuCaseLawLinkCaseSide"/> parameter. <see cref="RelationFact"/>'s own constructor
/// already checks a caller-supplied state against its target's identity set and refuses a mismatch
/// (<see cref="EcliState.EcliPresent"/> requires a declared ECLI in that set;
/// <see cref="EcliState.EcliNotInThisSet"/> requires none, and requires the target be a case;
/// <see cref="EcliState.EcliNotApplicable"/> requires the target not be a case), but that check only
/// ever looks at the target. <see cref="Create"/> mirrors the identical three-state shape against
/// whichever side <see cref="CaseSide"/> names as the case, computing <see cref="CaseEcliState"/>
/// from that side's own identity set: present when it declares an ECLI, not-in-this-set when it does
/// not (the "not a case" arm of the mirrored invariant is enforced by <see cref="CaseSide"/>'s own
/// derivation, since a side is only ever named the case after it has already proven
/// <see cref="OfficialIdentitySet.IsCase"/>). When the case is the edge's target,
/// <see cref="RelationFact"/>'s own <c>TargetEcliState</c> already carries this exact question, and
/// <see cref="Create"/> sets it from the identical local value it also stores as
/// <see cref="CaseEcliState"/> (see <see cref="Create"/>'s own <c>factTargetEcliState</c>), so the
/// two fields are equal by construction, not by proof: an assertion comparing one against the
/// other would pass for any value the shared computation produced, tautologically. What
/// <c>EuCaseLawLinkTests</c> (<c>WhenTheCaseIsTheTargetTheDerivedStateEqualsRelationFactsOwnTargetEcliState</c>)
/// actually checks instead is each field independently against the fixture's own known ECLI
/// literal, so a defect in the shared computation itself would still be caught. When the case is
/// the edge's source, <see cref="RelationFact"/>'s own <c>TargetEcliState</c>
/// describes the <i>other</i>, non-case side instead, and per its own invariant that can only ever
/// be <see cref="EcliState.EcliNotApplicable"/>; <see cref="CaseEcliState"/> is what actually answers
/// REL-005 in that direction.
/// </para>
/// <para>
/// <b>Why two predicates, not one, and why not a third.</b> The scope ruling's second precision
/// asks for "the real CDM predicate (case law interpretes resource legal) on the edge." Review/23
/// section 3, line 54 lists it among the observed CDM predicates
/// (<c>case-law_interpretes_resource_legal</c>, alongside <c>resource_legal_amended_by_case-law</c>
/// and <c>case-law_declares_void_by_preliminary_ruling_resource_legal</c>), and section 7, line 91
/// gives the one worked instance this lane has: Schrems II interpreting the GDPR, Directive
/// 95/46, the TFEU and Charter articles, with the case as the edge's subject throughout. This
/// binding does <b>not</b> hard-code that direction as a per-predicate assumption the way the prior
/// head did (case always source, <c>resource_legal</c> always target, unchecked): <see cref="CaseSide"/>
/// is derived the same way regardless of which pinned predicate is used, by asking
/// <see cref="OfficialIdentitySet.IsCase"/> of both ends actually supplied, which is strictly more
/// defensive than trusting a predicate's documented convention to hold. <c>work_cites_work</c> is
/// pinned as the second predicate because review/23 section 3, line 54 also lists it as a real,
/// generic CDM predicate (not restricted to any one FRBR class pair), and section 7, line 91
/// evidences it directly against this exact GDPR case-law question ("2,257 works cite it via
/// <c>work_cites_work</c> ... including CA summary records and orders with ECLI"): a real predicate
/// whose direction is not structurally fixed the way <c>case-law_interpretes_resource_legal</c>'s
/// is. <c>EuCaseLawLinkTests</c> uses it to exercise <see cref="CaseSide"/> and the refusals below
/// on a case-at-target shape, disclosed there as synthetic scaffolding rather than a worked
/// instance, because review/23 gives no specific example placing a case at <c>work_cites_work</c>'s
/// target. Two further predicates section 3, line 54 also names,
/// <c>resource_legal_amended_by_case-law</c> and
/// <c>case-law_declares_void_by_preliminary_ruling_resource_legal</c>, would likewise place a case
/// at either end, but neither carries a worked instance example anywhere in review/23: pinning
/// either here would let a caller assert a specific judicial outcome (an act ruled void or amended
/// by a named case) this lane has no evidence for, which is a materially stronger and more easily
/// misleading claim than a citation. They are deliberately left out.
/// </para>
/// <para>
/// <b>The judgment-body disposition applies to the case side, wherever it sits.</b>
/// <see cref="TargetBodyScope"/> always names the scope of the edge's own target
/// (<see cref="RelationFact.TargetBodyScope"/>'s own remarks), independent of which side
/// <see cref="CaseSide"/> names as the case. A case's own text is never held or fetched under any
/// condition (<see cref="EuJudgmentBodyDisposition.LinkOnlyNeverHeldOrFetched"/>, always), so a case
/// sitting at the target can never legitimately pair with
/// <see cref="TargetBodyScope.BodyInScopeHeld"/>: that pairing would claim the judgment's own body
/// is held, which this contract never does. <see cref="Create"/> refuses that exact pairing. When
/// the case is the source instead, <see cref="TargetBodyScope"/> describes the ordinary,
/// non-case target (the interpreted or cited act), whose full text this corpus may legitimately
/// hold, so <see cref="TargetBodyScope.BodyInScopeHeld"/> stays admissible there (the
/// <c>SchremsIiInterpretsGdpr</c> fixture in <c>EuCaseLawLinkTests.cs</c> uses exactly that).
/// </para>
/// <para>
/// <b>Granularity is fixed, not a parameter.</b> <see cref="Create"/>'s <see cref="Granularity"/>
/// output is always the same value, because both predicates pinned here relate whole works to whole
/// works (never to an article or paragraph within one: review/23 line 109 names
/// <c>reference_to_modified_location</c>, a different, not-yet-exploited mechanism, as the one that
/// would carry article-level detail). An enum member that a mutation could flip without any test
/// noticing is exactly the shape this codebase's own remarks elsewhere warn against (see
/// <see cref="DateOpenSentinel"/>'s removed <c>not_yet_determined</c> member), so
/// <see cref="EuCaseLawGranularity"/> declares no alternative this binding can never produce.
/// </para>
/// </remarks>
public sealed class EuCaseLawLinkBinding : IEuFactsEvidenceCarrier
{
    private EuCaseLawLinkBinding(
        RelationFact fact,
        EuCaseLawGranularity granularity,
        EuJudgmentBodyDisposition judgmentBodyDisposition,
        EuCaseLawLinkCaseSide caseSide,
        EcliState caseEcliState)
    {
        Fact = fact;
        Granularity = granularity;
        JudgmentBodyDisposition = judgmentBodyDisposition;
        CaseSide = caseSide;
        CaseEcliState = caseEcliState;
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
    /// Which side of <see cref="Fact"/>'s edge is the case-law work: the publisher's subject or
    /// its object. Decided once in <see cref="Create"/> from the two identity sets it was given,
    /// never re-inferred elsewhere. See the type remarks.
    /// </summary>
    public EuCaseLawLinkCaseSide CaseSide { get; }

    /// <summary>
    /// The case's own ECLI state, read from whichever side <see cref="CaseSide"/> names, mirroring
    /// <see cref="RelationFact"/>'s own three-state invariant. This is the value REL-005 actually
    /// asks for: when <see cref="CaseSide"/> is <see cref="EuCaseLawLinkCaseSide.Target"/> it is
    /// provably equal to <see cref="Fact"/>'s own <see cref="RelationFact.TargetEcliState"/>; when
    /// it is <see cref="EuCaseLawLinkCaseSide.Source"/>, <see cref="Fact"/>'s own field describes
    /// the other, non-case side instead (always <see cref="EcliState.EcliNotApplicable"/> there),
    /// and this property is the one that answers the question.
    /// </summary>
    public EcliState CaseEcliState { get; }

    /// <summary>The case side's own ECLI literal, or <c>null</c> where <see cref="CaseEcliState"/>
    /// is not <see cref="EcliState.EcliPresent"/>.</summary>
    public string? CaseEcli() =>
        (CaseSide == EuCaseLawLinkCaseSide.Source ? Fact.PublisherAsserted!.Source : Fact.PublisherAsserted!.Target)
            .Value(FactsIdentifierFamily.Ecli);

    /// <summary>
    /// The only path that mints a binding. Builds the <see cref="PublisherRelation"/> and the
    /// <see cref="RelationFact"/> it wraps in one step, computing <see cref="CaseSide"/> and
    /// <see cref="CaseEcliState"/> exactly once from <paramref name="source"/> and
    /// <paramref name="target"/> themselves rather than accepting either as an input a caller could
    /// get wrong.
    /// </summary>
    /// <param name="source">
    /// The edge's subject exactly as the publisher asserted it.
    /// </param>
    /// <param name="target">
    /// The edge's object exactly as the publisher asserted it.
    /// </param>
    /// <param name="predicateUri">
    /// Must be one of <see cref="EuCaseLawPredicateVocabulary"/>'s pinned predicates. Any other
    /// value, including a syntactically valid but unpinned absolute URI, is refused.
    /// </param>
    /// <param name="targetBodyScope">
    /// Whether <paramref name="target"/>'s own body is held. Always names the target's own scope
    /// (see the type remarks), so it can never legitimately claim
    /// <see cref="TargetBodyScope.BodyInScopeHeld"/> when the target is the case side.
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
        // Both are dereferenced below (IsCase()) before PublisherRelation's own constructor ever
        // runs, so both need an explicit guard here: relying on PublisherRelation's own null check
        // for either would surface as a NullReferenceException out of that dereference instead of
        // the ArgumentNullException a caller of this door should see.
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(qualifiedAxioms);

        if (predicateUri is null || !EuCaseLawPredicateVocabulary.Pinned.Contains(predicateUri))
        {
            throw new ArgumentException(
                $"\"{predicateUri}\" is not one of the pinned, review/23-evidenced EU case-law " +
                "predicates.",
                nameof(predicateUri));
        }

        // Which side is the case, decided once here from the two identity sets actually supplied,
        // never per-predicate convention. See the type remarks.
        var caseSide = (source.IsCase(), target.IsCase()) switch
        {
            (true, false) => EuCaseLawLinkCaseSide.Source,
            (false, true) => EuCaseLawLinkCaseSide.Target,
            (false, false) => throw new ArgumentException(
                "Neither the source nor the target identity set is a case; this edge is not a " +
                "case-law link.",
                nameof(source)),
            (true, true) => throw new ArgumentException(
                "Both the source and the target identity sets are a case; this binding names " +
                "exactly one case side, and an edge with two candidates has none it can name " +
                "honestly.",
                nameof(source)),
        };

        var caseIdentity = caseSide == EuCaseLawLinkCaseSide.Source ? source : target;

        // Mirrors RelationFact's own three-state invariant (RelationFact.cs's constructor)
        // exactly, but read from whichever side is actually the case. The "not a case" arm of
        // that invariant is already enforced above: caseIdentity is only ever the side that has
        // already proven IsCase().
        var caseEcliState = caseIdentity.Has(FactsIdentifierFamily.Ecli)
            ? EcliState.EcliPresent
            : EcliState.EcliNotInThisSet;

        // TargetBodyScope always names the target's own body, regardless of which side is the
        // case (see the type remarks). A case's text is always link-only, so a case at the target
        // can never pair with BodyInScopeHeld.
        if (caseSide == EuCaseLawLinkCaseSide.Target &&
            targetBodyScope == TargetBodyScope.BodyInScopeHeld)
        {
            throw new ArgumentException(
                "The case sits at the target and judgment text is always link-only, so a case " +
                "target can never pair with TargetBodyScope.BodyInScopeHeld.",
                nameof(targetBodyScope));
        }

        // RelationFact's own EcliState field always describes the target. When the case is the
        // target, this assigns it the identical caseEcliState value computed above, so the two
        // fields agree by construction rather than by an independently checked proof; the type
        // remarks on this class explain why EuCaseLawLinkTests checks each field against the
        // fixture's own known ECLI literal instead of against each other. When the case is the
        // source, the target is, by caseSide's own derivation above, not a case, so
        // RelationFact's own invariant requires EcliNotApplicable there.
        var factTargetEcliState = caseSide == EuCaseLawLinkCaseSide.Target
            ? caseEcliState
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
            factTargetEcliState,
            publisherRelation,
            null,
            null);

        return new EuCaseLawLinkBinding(
            fact,
            EuCaseLawGranularity.ActLevel,
            EuJudgmentBodyDisposition.LinkOnlyNeverHeldOrFetched,
            caseSide,
            caseEcliState);
    }
}

/// <summary>
/// Which side of an <see cref="EuCaseLawLinkBinding"/>'s edge is the case-law work.
/// </summary>
/// <remarks>
/// No member here carries a <c>JsonStringEnumMemberName</c> wire token: this side marker is not
/// serialised anywhere today (nothing wraps <see cref="EuCaseLawLinkBinding"/> itself on the
/// wire; only <see cref="EuCaseLawLinkBinding.Fact"/> is), so there is no wire form to pin one
/// against. What is pinned instead, in <c>EuCaseLawLinkTests</c>, is the exact member set and the
/// one place that can hand one out, the same technique
/// <c>RepeatedEnumerationRowsOpenRefusal</c> (Source/Core) uses for the same reason.
/// </remarks>
public enum EuCaseLawLinkCaseSide
{
    /// <summary>The edge's source is the case-law work.</summary>
    Source = 1,

    /// <summary>The edge's target is the case-law work.</summary>
    Target = 2,
}

/// <summary>
/// The exact, closed set of real EU case-law CDM predicates review/23-research-temporal.md
/// evidences with a worked instance, and the only predicates <see cref="EuCaseLawLinkBinding.Create"/>
/// accepts.
/// </summary>
/// <remarks>
/// review/23 section 3, line 54 also names <c>case-law_declares_void_by_preliminary_ruling_resource_legal</c>
/// and <c>resource_legal_amended_by_case-law</c>. Neither is pinned here: section 7, line 91 gives
/// a worked instance for <c>case-law_interpretes_resource_legal</c> (Schrems II) and evidences
/// <c>work_cites_work</c> generically (2,257 citations to the GDPR, including items with ECLI), but
/// no worked instance for either of the other two, and admitting a predicate with no observed
/// instance would let a caller assert a specific judicial outcome (an act ruled void, or amended, by
/// a named case) this lane cannot evidence.
/// </remarks>
public static class EuCaseLawPredicateVocabulary
{
    /// <summary>
    /// "Schrems II case-law_interpretes_resource_legal lists 31995L0046, 32016R0679, 12007P/TXT
    /// and Charter articles" (review/23 section 7, line 91). Named in the CDM predicate list at
    /// section 3, line 54.
    /// </summary>
    public const string CaseLawInterpretesResourceLegalPredicateUri =
        EuConsolidationDiscoveryPlan.Cdm + "case-law_interpretes_resource_legal";

    /// <summary>
    /// "2,257 works cite it via work_cites_work ... including CA summary records and orders with
    /// ECLI" (review/23 section 7, line 91). Named in the CDM predicate list at section 3, line 54,
    /// generic across every FRBR class the publisher mints.
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
/// <remarks>
/// No member here carries a <c>JsonStringEnumMemberName</c> wire token, for the same reason
/// <see cref="EuCaseLawLinkCaseSide"/> carries none: nothing serialises
/// <see cref="EuCaseLawLinkBinding"/> or this property on the wire today. What is pinned instead,
/// in <c>EuCaseLawLinkTests</c>, is the exact member set and the one place that can hand one out.
/// </remarks>
public enum EuCaseLawGranularity
{
    /// <summary>The link is to the act (or case) as a whole, never to a specific article.</summary>
    ActLevel = 1,
}

/// <summary>
/// Whether the judgment text behind an EU case-law link is held or fetched by this contract.
/// </summary>
/// <remarks>
/// Carries exactly one member for the same reason as <see cref="EuCaseLawGranularity"/>:
/// review/23 section 7, line 92 records that judgment text "has judgment-date semantics, not
/// validity intervals, matching the Lex roadmap note that CJEU is a separate source class," and
/// this binding never holds or fetches one under any condition, so no alternative member could
/// ever be produced.
/// </remarks>
/// <remarks>
/// No member here carries a <c>JsonStringEnumMemberName</c> wire token, for the same reason
/// <see cref="EuCaseLawLinkCaseSide"/> carries none: nothing serialises
/// <see cref="EuCaseLawLinkBinding"/> or this property on the wire today. What is pinned instead,
/// in <c>EuCaseLawLinkTests</c>, is the exact member set and the one place that can hand one out.
/// </remarks>
public enum EuJudgmentBodyDisposition
{
    /// <summary>
    /// The link names the judgment (by its case-law identity) and never holds or fetches its
    /// text. Only the link's own metadata is asserted.
    /// </summary>
    LinkOnlyNeverHeldOrFetched = 1,
}
