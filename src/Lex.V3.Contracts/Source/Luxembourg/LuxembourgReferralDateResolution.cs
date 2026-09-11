namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// What a draft's referral date turned out to be, or why it is not a fact. Closed.
/// </summary>
/// <remarks>
/// FIVE STATES, NOT TWO. "Present" and "absent" cannot carry this question: a draft reaches several
/// opinion events and only one of them is an <c>OpinionRequest</c>, so most edges are neither an
/// answer nor a missing answer. Collapsing them would mistype every <c>avce</c>, <c>avis</c> and
/// <c>disp</c> edge a draft carries - roughly four in five of them, measured.
/// </remarks>
public enum LuxembourgReferralDateState
{
    /// <summary>
    /// The publisher holds a referral date for this draft, through a proven request.
    /// </summary>
    Fact = 1,

    /// <summary>
    /// The request is proven and its graph is complete, and it holds no referral date.
    /// </summary>
    /// <remarks>
    /// The only state that may be concluded from silence, and only because the delivery that was
    /// silent COULD have carried it: a complete broad-predicate enumeration over a subject the
    /// publisher itself typed <c>OpinionRequest</c>. Nothing weaker earns this.
    /// </remarks>
    DerivedAbsence = 2,

    /// <summary>
    /// The draft reaches no opinion request at all, so nothing was traversed.
    /// </summary>
    /// <remarks>
    /// Not an absence. A draft with no <c>hasOpinion</c> edge to a proven request was never asked
    /// the question, and 1 of the 650 drafts in the retained sample is in exactly this position.
    /// Reporting it as "no referral date" would be the false absence S2-A03 forbids.
    /// </remarks>
    DraftSideGap = 3,

    /// <summary>
    /// The draft reaches a target that is not a proven member of the request class.
    /// </summary>
    /// <remarks>
    /// ITS OWN STATE, and the one most easily lost. <c>hasOpinion</c> reaches other opinion classes
    /// - the retained sample shows <c>avce</c>, <c>avis</c> and <c>disp</c> targets alongside the
    /// single <c>sace</c> - so a target outside the class is neither an absence nor a contradiction.
    /// It is a role this run did not prove, and it must not be folded into
    /// <see cref="DraftSideGap"/>: the draft DID reach something, and saying otherwise would hide
    /// which edge was unresolved.
    /// </remarks>
    TargetRoleGap = 4,

    /// <summary>
    /// A referral date was asserted directly of the draft, which no run has proved it can hold.
    /// </summary>
    /// <remarks>
    /// Retained as typed drift, never a fact. <c>referralDate</c> is declared on
    /// <c>OpinionRequest</c>; a triple asserting it of an <c>InitialDraft</c> says that subject
    /// holds a class role nothing proved, and admitting it because the IRI is in the accepted
    /// vocabulary is how this family previously widened its own authority. S2-A05 requires drift to
    /// fail closed into typed evidence.
    /// </remarks>
    Drift = 5,
}

/// <summary>
/// Why a traversal step could not be taken, when the reason is the evidence rather than the data.
/// </summary>
public enum LuxembourgReferralEvidenceRefusal
{
    None = 0,

    /// <summary>The draft-to-target row was not verified against its own delivered key.</summary>
    EdgeRowNotKeyVerified = 1,

    /// <summary>The edge row was not bound to the draft batch citation that delivered it.</summary>
    EdgeNotBoundToItsDraftCitation = 2,

    /// <summary>The target was not a bare IRI: a literal, a blank node, or a qualified term.</summary>
    TargetNotABareIri = 3,

    /// <summary>The request graph's own citation was absent or incomplete.</summary>
    RequestGraphCitationIncomplete = 4,

    /// <summary>
    /// The request's delivered <c>rdf:type</c> row was missing.
    /// </summary>
    /// <remarks>
    /// The class filter in the query is NOT type evidence. A broad-predicate graph over a
    /// mandatory-class subject necessarily returns that subject's own type triple, so requiring the
    /// delivered row costs nothing and closes the gap between "we filtered on it" and "the
    /// publisher said it".
    /// </remarks>
    DeliveredTypeRowMissing = 5,

    /// <summary>
    /// The edge was delivered, and its target carries no lexical form to name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE REFUSAL THAT CANNOT NAME ITS TARGET, and the reason the nameability invariant has an
    /// exception at all. The broad-predicate acquisition retains whatever the publisher sends, and
    /// an empty literal is a value RDF permits: the draft DID reach an edge, and that edge's target
    /// has nothing to call it by.
    /// </para>
    /// <para>
    /// It is not <see cref="LuxembourgReferralDateState.DraftSideGap"/>. A draft with no
    /// <c>hasOpinion</c> row at all and a draft whose edge led to an unnameable target are different
    /// facts about the publisher, and collapsing them loses a delivered edge - the conservation
    /// failure S2-A03 forbids, arriving through a tidy-looking default.
    /// </para>
    /// </remarks>
    TargetCarriesNoLexicalForm = 6,
}

/// <summary>
/// One draft-to-request traversal step, and what it is entitled to conclude.
/// </summary>
/// <remarks>
/// <para>
/// THE PRODUCERS DO NOT DECIDE THIS. The OpinionRequest family is request-scoped: it enumerates a
/// class and reads what its members hold, and it never learns which draft reaches which request.
/// The draft-graph family holds the edges and never learns what the targets are. This is the
/// composite that cites both, and it is deliberately a separate decision from either acquisition -
/// making the draft edge an input to the request producer would let a draft-side fact change what
/// that producer acquires.
/// </para>
/// <para>
/// EVERY REQUIREMENT IS A REFUSAL, NOT A FILTER. A step that cannot satisfy one of them does not
/// silently produce nothing; it produces a typed reason. The difference matters because "no fact"
/// and "no evidence" are the two answers this whole slice exists to keep apart.
/// </para>
/// </remarks>
public sealed record LuxembourgReferralDateStep
{
    private LuxembourgReferralDateStep(
        string draftIri,
        string? targetIri,
        LuxembourgReferralDateState state,
        LuxembourgReferralEvidenceRefusal refusal,
        IReadOnlyList<string> referralDateValues)
    {
        // THE NAMEABILITY INVARIANT, HELD HERE RATHER THAN AT EACH DOOR. Every state either names a
        // target or cannot have one, and which it is defines the state:
        //
        //   TargetRoleGap   the draft reached something whose role is unproven. Its whole point is
        //                   that the edge stays nameable, so an unnamed one is not this state - it
        //                   is a draft-side gap wearing the wrong label.
        //   DraftSideGap    nothing was reached, so there is nothing to name.
        //   Drift           asserted of the draft itself; no target was traversed.
        //   Fact and
        //   DerivedAbsence  concluded from a proven request, which is named by construction.
        //
        // Resolve enforced this for the paths through it and TargetRoleUnproven did not, so a
        // caller could mint a TargetRoleGap with an empty target and lose the one fact that
        // separates it from the state beside it. It is enforced HERE and only here: a duplicate
        // guard on the factory made this unreachable, and deleting this check then left every test
        // green because nothing could get past the outer guard to reach it. A second check that no
        // test can fail is not defence in depth, it is an untested claim.
        // THE ONE EXCEPTION, AND IT IS NARROW. An edge whose delivered target carries no lexical
        // form was still reached, so it is not a draft-side gap - but there is nothing to name it
        // by, and synthesising one would invent a traversal target the publisher never sent. Only
        // this refusal may leave the target unnamed; a TargetRoleGap with Refusal.None still must
        // name one, which is the defect this invariant was written for.
        //
        // THERE IS NO CONVERSE CHECK, and that is measured rather than assumed. A guard refusing a
        // NAMED target alongside this refusal was written first and deleted: the only factory that
        // produces the refusal passes null, so nothing could reach it and no test could fail it.
        // It becomes necessary again the moment a second door can mint this refusal.
        var namesATarget = state is LuxembourgReferralDateState.TargetRoleGap
            or LuxembourgReferralDateState.Fact
            or LuxembourgReferralDateState.DerivedAbsence;
        var mayLeaveItUnnamed =
            refusal is LuxembourgReferralEvidenceRefusal.TargetCarriesNoLexicalForm;

        if (namesATarget && !mayLeaveItUnnamed && string.IsNullOrEmpty(targetIri))
        {
            throw new ArgumentException(
                $"{state} names the target it reached; an unnamed one is a different state.",
                nameof(targetIri));
        }

        if (!namesATarget && !mayLeaveItUnnamed && targetIri is not null)
        {
            throw new ArgumentException(
                $"{state} reached no target, so naming one would assert a traversal that did not "
                + "happen.", nameof(targetIri));
        }

        DraftIri = draftIri;
        TargetIri = targetIri;
        State = state;
        Refusal = refusal;
        ReferralDateValues = referralDateValues;
    }

    public string DraftIri { get; }

    /// <summary>The reached target, or null when the draft reached nothing.</summary>
    public string? TargetIri { get; }

    public LuxembourgReferralDateState State { get; }

    /// <summary>Why the evidence was insufficient, when it was.</summary>
    public LuxembourgReferralEvidenceRefusal Refusal { get; }

    /// <summary>
    /// Every distinct referral date this request delivered, in delivered order.
    /// </summary>
    /// <remarks>
    /// EVERY ONE, never a chosen one. A multi-valued property delivers a row per value, and picking
    /// one would be this family inventing a precedence the publisher never stated. Empty for every
    /// state but <see cref="LuxembourgReferralDateState.Fact"/>.
    /// </remarks>
    public IReadOnlyList<string> ReferralDateValues { get; }

    /// <summary>The draft reached no proven request, so nothing was traversed.</summary>
    public static LuxembourgReferralDateStep DraftReachedNothing(string draftIri) =>
        Gap(draftIri, null, LuxembourgReferralDateState.DraftSideGap);

    /// <summary>The draft reached a target whose request role this run did not prove.</summary>
    /// <remarks>
    /// The target is required, and the CONSTRUCTOR is the single place that says so. A guard here as
    /// well would be unreachable: with both, deleting the constructor's check left every test green,
    /// because no test could get past this door to reach it. One enforcement point that a test can
    /// actually exercise beats two where only the outer one is ever proved.
    /// </remarks>
    public static LuxembourgReferralDateStep TargetRoleUnproven(string draftIri, string targetIri) =>
        Gap(draftIri, targetIri, LuxembourgReferralDateState.TargetRoleGap);

    /// <summary>
    /// The draft reached an edge whose delivered target carries no lexical form.
    /// </summary>
    /// <remarks>
    /// Reached, and unnameable. The edge is preserved as a typed refusal rather than folded into
    /// <see cref="DraftReachedNothing"/>, because a draft that reached nothing and a draft whose
    /// edge led nowhere nameable are different facts and only one of them is a missing edge.
    /// </remarks>
    public static LuxembourgReferralDateStep TargetNotNameable(string draftIri)
    {
        ArgumentException.ThrowIfNullOrEmpty(draftIri);
        return new(
            draftIri,
            null,
            LuxembourgReferralDateState.TargetRoleGap,
            LuxembourgReferralEvidenceRefusal.TargetCarriesNoLexicalForm,
            []);
    }

    /// <summary>A referral date asserted directly of the draft: retained drift, never a fact.</summary>
    public static LuxembourgReferralDateStep DirectTripleIsDrift(string draftIri) =>
        Gap(draftIri, null, LuxembourgReferralDateState.Drift);

    /// <summary>
    /// The step a complete, proof-bound traversal earns.
    /// </summary>
    /// <remarks>
    /// The four requirements are checked in the order that keeps their reasons distinguishable: the
    /// edge must be real evidence before its target is worth examining, the target must be
    /// addressable before membership means anything, and membership must hold before a delivered
    /// type row is the thing that proves the role rather than a coincidence.
    /// </remarks>
    public static LuxembourgReferralDateStep Resolve(
        string draftIri,
        string targetIri,
        bool edgeRowKeyVerified,
        bool edgeBoundToDraftCitation,
        bool targetIsBareIri,
        bool targetInProvenInventory,
        bool requestGraphCitationComplete,
        bool deliveredTypeRowPresent,
        IReadOnlyList<string> referralDateValues)
    {
        ArgumentException.ThrowIfNullOrEmpty(draftIri);
        ArgumentException.ThrowIfNullOrEmpty(targetIri);
        ArgumentNullException.ThrowIfNull(referralDateValues);

        if (!edgeRowKeyVerified)
        {
            return Refused(draftIri, targetIri, LuxembourgReferralEvidenceRefusal.EdgeRowNotKeyVerified);
        }

        if (!edgeBoundToDraftCitation)
        {
            return Refused(
                draftIri, targetIri, LuxembourgReferralEvidenceRefusal.EdgeNotBoundToItsDraftCitation);
        }

        if (!targetIsBareIri)
        {
            return Refused(draftIri, targetIri, LuxembourgReferralEvidenceRefusal.TargetNotABareIri);
        }

        // NOT A REFUSAL. A target outside the proven class is a fact about this publisher's graph -
        // hasOpinion reaches several opinion classes - so it is the target-role gap rather than an
        // evidence failure. Ordering it here, after the evidence checks, is what keeps "we could not
        // trust the edge" separate from "the edge was fine and led somewhere else".
        if (!targetInProvenInventory)
        {
            return TargetRoleUnproven(draftIri, targetIri);
        }

        if (!requestGraphCitationComplete)
        {
            return Refused(
                draftIri, targetIri, LuxembourgReferralEvidenceRefusal.RequestGraphCitationIncomplete);
        }

        if (!deliveredTypeRowPresent)
        {
            return Refused(
                draftIri, targetIri, LuxembourgReferralEvidenceRefusal.DeliveredTypeRowMissing);
        }

        var values = referralDateValues.ToArray();
        if (values.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(
                "A delivered referral date is a lexical value; an empty one is not a date this "
                + "publisher sent.", nameof(referralDateValues));
        }

        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException(
                "Two identical referral dates on one request are one canonical row delivered twice, "
                + "which fails closed upstream rather than being deduplicated here.",
                nameof(referralDateValues));
        }

        return values.Length == 0
            ? new LuxembourgReferralDateStep(
                draftIri, targetIri, LuxembourgReferralDateState.DerivedAbsence,
                LuxembourgReferralEvidenceRefusal.None, [])
            : new LuxembourgReferralDateStep(
                draftIri, targetIri, LuxembourgReferralDateState.Fact,
                LuxembourgReferralEvidenceRefusal.None, Array.AsReadOnly(values));
    }

    /// <summary>
    /// An evidence failure is a TARGET-ROLE gap carrying its reason, never an absence.
    /// </summary>
    /// <remarks>
    /// The state says the role was not established and the refusal says why. Typing these as
    /// anything absence-shaped would let a custody or binding failure read as a statement about
    /// what the publisher holds.
    /// </remarks>
    private static LuxembourgReferralDateStep Refused(
        string draftIri, string targetIri, LuxembourgReferralEvidenceRefusal refusal) =>
        new(draftIri, targetIri, LuxembourgReferralDateState.TargetRoleGap, refusal, []);

    private static LuxembourgReferralDateStep Gap(
        string draftIri, string? targetIri, LuxembourgReferralDateState state)
    {
        ArgumentException.ThrowIfNullOrEmpty(draftIri);
        return new(draftIri, targetIri, state, LuxembourgReferralEvidenceRefusal.None, []);
    }
}
