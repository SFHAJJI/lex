using Lex.V3.Contracts.Source.Absence;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The resource observations a scope resolution may read, and the evidence that they came from a
/// proven enumeration. Decision 80's shape: holding an instance IS the proof, because the only door
/// onto this type requires the run's own
/// <see cref="AbsenceFamilyEnumerationProof"/> for the assertion family the observations were
/// derived from.
/// </summary>
/// <remarks>
/// This type exists because of a defect worth remembering rather than a preference. The body join
/// needed to know that the assertion family behind its candidates was proven and re-verified, and
/// that fact is a RUN LEVEL PRECONDITION established by the adapter before any observation exists.
/// Expressed as a runtime guard inside the join it degenerated into comparing a reference against
/// itself, a condition that could never fail while reading downstream as a guarantee. A property
/// that belongs in a constructor cannot be rescued by asserting it later
/// (RULING lex-event-20260904T204900861Z-6b737927d58a409dab05149aa28052e5).
/// <para>
/// The proof is not a token a caller can mint at will: <see cref="AbsenceFamilyEnumerationProof"/>
/// has one private constructor and one <c>TryCreate</c> door of its own, which refuses anything
/// that is not a real, complete, receipted family enumeration. So a hand-built topology cannot
/// reach the join without a genuine proof in hand, and no condition anywhere has to be named after
/// the guarantee.
/// </para>
/// </remarks>
public sealed class LuxembourgProvenResourceObservations
{
    private LuxembourgProvenResourceObservations(
        IReadOnlyList<AbsenceFamilyEnumerationProof> assertionFamilyProofs,
        IReadOnlyList<LuxembourgResourceObservation> observations)
    {
        AssertionFamilyProofs = assertionFamilyProofs;
        AssertionFamilyProof = assertionFamilyProofs.Count == 1 ? assertionFamilyProofs[0] : null;
        Observations = observations;
    }

    /// <summary>
    /// The proof for a single-partition observation set. Null for no family or multiple members;
    /// <see cref="AssertionFamilyProofs"/> retains the entire proof set in every case.
    /// </summary>
    public AbsenceFamilyEnumerationProof? AssertionFamilyProof { get; }

    public IReadOnlyList<AbsenceFamilyEnumerationProof> AssertionFamilyProofs { get; }

    public IReadOnlyList<LuxembourgResourceObservation> Observations { get; }

    /// <summary>
    /// The only door. A caller without a real family proof cannot produce this type, and therefore
    /// cannot reach a scope resolution or the body join at all.
    /// </summary>
    public static LuxembourgProvenResourceObservations RequireProven(
        AbsenceFamilyEnumerationProof assertionFamilyProof,
        IReadOnlyList<LuxembourgResourceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(assertionFamilyProof);
        return RequireAllProven([assertionFamilyProof], observations);
    }

    /// <summary>
    /// Retains every independently proven assertion partition contributing to the observations.
    /// The adapter must reopen all rows and verify the declared scope before calling this door.
    /// These are separate partition acquisitions, not a fabricated single acquisition run.
    /// </summary>
    public static LuxembourgProvenResourceObservations RequireAllProven(
        IReadOnlyList<AbsenceFamilyEnumerationProof> assertionFamilyProofs,
        IReadOnlyList<LuxembourgResourceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(assertionFamilyProofs);
        ArgumentNullException.ThrowIfNull(observations);
        var proofs = LuxembourgSourceValidation.Copy(assertionFamilyProofs, nameof(assertionFamilyProofs));
        if (proofs.Count == 0 || proofs.Select(static proof => proof.FamilyKey).Distinct(StringComparer.Ordinal).Count() != proofs.Count)
            throw new ArgumentException("Assertion proofs must name a non-empty set of distinct partition members.", nameof(assertionFamilyProofs));
        // Independent acquisitions mint distinct profile resource IDs for identical profile bytes.
        // Retain those IDs in each proof; compatibility depends on the checked content digest.
        if (proofs.Any(proof => proof.SourceProfileRef.Sha256 != proofs[0].SourceProfileRef.Sha256 ||
            proof.InterpretationProfileRef.Sha256 != proofs[0].InterpretationProfileRef.Sha256))
            throw new ArgumentException("Assertion proofs must use one source and interpretation profile.", nameof(assertionFamilyProofs));
        return new(proofs, LuxembourgSourceValidation.Copy(observations, nameof(observations)));
    }

    /// <summary>
    /// The empty run: this run designated no resource family, so there is nothing to prove and
    /// nothing to resolve. This is NOT a hole in the door. It is the one shape that carries no
    /// observations at all, so it can reach no candidate, no manifestation and no body; a caller
    /// wanting an observation still needs <see cref="RequireProven"/> and therefore still needs a
    /// real proof.
    /// </summary>
    public static LuxembourgProvenResourceObservations NoFamilyDesignated() => new([], []);
}
