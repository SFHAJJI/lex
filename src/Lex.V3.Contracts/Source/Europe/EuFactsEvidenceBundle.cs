namespace Lex.V3.Contracts.Source.Europe;

/// <summary>Why a candidate was not admitted into an EU Facts evidence bundle. Closed.</summary>
public enum EuFactsEvidenceAdmissionRefusal
{
    None = 0,

    /// <summary>
    /// The candidate does not implement <see cref="IEuFactsEvidenceCarrier"/>, so it is not
    /// Facts-layer evidence for a legal record.
    /// </summary>
    /// <remarks>
    /// This is the refusal <see cref="EuLegislationSummary"/> reaches. REL-005 types a Summary of
    /// EU Legislation as explanatory rather than law, and E7 requires that exclusion to be proven
    /// by a bundle that cannot carry one rather than asserted in prose.
    /// </remarks>
    CandidateIsNotAFactsEvidenceCarrier = 1,

    /// <summary>A null candidate. Refused rather than skipped, so the count cannot silently shrink.</summary>
    CandidateWasNull = 2,
}

/// <summary>
/// A bundle of EU Facts-layer evidence. Its members are typed as
/// <see cref="IEuFactsEvidenceCarrier"/> and never as any concrete binding, which is what makes a
/// non-carrier's exclusion a property of the type system rather than a convention.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE EXISTS. <see cref="IEuFactsEvidenceCarrier"/> has said since it was written that
/// "every future evidence bundle demands this marker", in the future tense, because no bundle
/// existed. The exclusion of <see cref="EuLegislationSummary"/> was therefore proven only by a
/// reflective assertion that the summary does not implement the marker. That is a true statement
/// about a type and it is not what REL-005 asks for: E7 is done when the type and the exclusion are
/// proven by a bundle that CANNOT CARRY ONE — a compile-time or door-level exclusion with a test
/// that tries and is refused, not a documented convention. A bundle that does not exist refuses
/// nothing.
/// </para>
/// <para>
/// TWO DOORS, AND WHY BOTH. <see cref="Create"/> is the typed door: its element type is
/// <see cref="IEuFactsEvidenceCarrier"/>, so passing a summary is not a runtime refusal but a
/// compile error — the strongest form the ruling allows, and the one a producer with concrete
/// bindings in hand goes through. <see cref="TryAdmit"/> is the door-level exclusion for evidence
/// that does not arrive statically typed. Both exist because the compile-time exclusion cannot be
/// exercised by a test — code that does not compile cannot be run — so without a door that accepts
/// a wider candidate and refuses it, "tries and is refused" has nothing to try.
/// </para>
/// <para>
/// REFUSED, NEVER FILTERED. <see cref="TryAdmit"/> refuses the whole call when any candidate is not
/// a carrier. It does not admit the carriers and drop the rest. Dropping would hand back a bundle
/// that looks complete and is not, and the discarded record would leave nothing behind to notice —
/// the false absence S2-A05 exists to prevent, and the same defect, in the same shape, that this
/// seat shipped and had found against it on the E8 procedure-event contract.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DECIDE. An empty bundle is admitted and asserts nothing about completeness.
/// Whether an evidence set may be empty, and what retained proof that would need, is Decision 64's
/// question about an acquisition, not this type's about a container. Answering it here would mint a
/// completeness rule out of a slice that was asked only to make an exclusion structural.
/// </para>
/// </remarks>
public sealed class EuFactsEvidenceBundle
{
    private EuFactsEvidenceBundle(IReadOnlyList<IEuFactsEvidenceCarrier> members) => Members = members;

    /// <summary>
    /// The admitted evidence, in the order supplied. Typed as the marker, never as a concrete
    /// binding: a member typed this way carries no one binding type, so this bundle does not become
    /// an unpinned second holder of anything E1, E4 or E6 already pins.
    /// </summary>
    public IReadOnlyList<IEuFactsEvidenceCarrier> Members { get; }

    /// <summary>How many pieces of evidence this bundle holds.</summary>
    public int Count => Members.Count;

    /// <summary>
    /// The typed door. A candidate that does not implement <see cref="IEuFactsEvidenceCarrier"/>
    /// cannot be passed here at all, which is the compile-time half of the exclusion.
    /// </summary>
    public static EuFactsEvidenceBundle Create(IReadOnlyList<IEuFactsEvidenceCarrier> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var admitted = new IEuFactsEvidenceCarrier[members.Count];
        for (var index = 0; index < members.Count; index++)
        {
            admitted[index] = members[index]
                ?? throw new ArgumentException("A bundle member cannot be null.", nameof(members));
        }

        return new EuFactsEvidenceBundle(Array.AsReadOnly(admitted));
    }

    /// <summary>
    /// The door-level exclusion, for candidates that are not statically known to be evidence.
    /// Refuses with a typed reason and names the offending type; never admits a subset.
    /// </summary>
    public static EuFactsEvidenceBundle? TryAdmit(
        IReadOnlyList<object?> candidates,
        out EuFactsEvidenceAdmissionRefusal refusal,
        out string? offendingTypeName)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        refusal = EuFactsEvidenceAdmissionRefusal.None;
        offendingTypeName = null;

        var admitted = new IEuFactsEvidenceCarrier[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate is null)
            {
                refusal = EuFactsEvidenceAdmissionRefusal.CandidateWasNull;
                return null;
            }

            if (candidate is not IEuFactsEvidenceCarrier carrier)
            {
                refusal = EuFactsEvidenceAdmissionRefusal.CandidateIsNotAFactsEvidenceCarrier;
                offendingTypeName = candidate.GetType().FullName;
                return null;
            }

            admitted[index] = carrier;
        }

        return new EuFactsEvidenceBundle(Array.AsReadOnly(admitted));
    }
}
