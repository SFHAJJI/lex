namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a primary enumeration could not be reconciled against the witness. Closed.
/// </summary>
public enum EuPrimaryWitnessReconciliationRefusal
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>
    /// The primary enumeration's <see cref="EuPrimaryEnumerationRootBinding.ClosureQueryPlanRef"/>
    /// and the witness's <see cref="EuFeedRootIntersection.ClosureMatrixRef"/> carry the same
    /// digest. R3.2 requires "a structurally different authoritative root or traversal path and a
    /// different producer symbol from the primary enumeration"; two identities that collide mean
    /// the same query plan is standing in for both sides, which is repeating one store's own
    /// traversal with a different projection, exactly what R3.2 says is "not publisher-independent
    /// evidence".
    /// </summary>
    ClosureIdentityNotStructurallyIndependentFromWitness = 1,

    /// <summary>
    /// A termination's in-pack component names a root the primary enumeration itself never
    /// discovered. R3 already requires an in-pack or mixed-scope projection to reconcile to its
    /// discovered family; this is the same reconciliation one level up, against the primary
    /// enumeration's own root set rather than its family index, because the witness is
    /// reconciliation evidence <em>for</em> the primary enumeration and not an independent source of
    /// root membership.
    /// </summary>
    WitnessInPackRootMissingFromPrimaryEnumeration = 2,
}

/// <summary>
/// Joins D1-05's own primary enumeration (<see cref="EuPrimaryEnumerationRootBinding"/>) against
/// the positive-change witness's terminal classification (<see cref="EuFeedRootIntersection"/>,
/// <see cref="EuFeedEntryTermination"/>), as the ruling requires: "the witness reconciles against
/// [the primary enumeration] as required independent evidence."
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a one-way check. The witness's in-pack and mixed-scope terminals assert
/// that specific Work roots changed and are in the 82-root pack; the primary enumeration is the
/// closure query that is supposed to have discovered those same roots independently. A witness
/// terminal naming an in-pack root the primary enumeration never found is not corroborated, and this
/// type refuses the reconciliation rather than silently trusting the witness's own say-so about pack
/// membership -- which is exactly the shape R3.2 forbids ("the primary enumeration is never the
/// witness") read from the other direction.
/// </para>
/// <para>
/// The out-of-pack component of a mixed-scope or out-of-pack terminal is never checked here: R3 and
/// R7 are explicit that an out-of-pack positive is "retained positive-only evidence" that "cannot add
/// a seed, root, closure row, body, or capability", so it has no membership claim against the primary
/// enumeration to reconcile in the first place.
/// </para>
/// </remarks>
public sealed class EuPrimaryEnumerationWitnessReconciliation
{
    private EuPrimaryEnumerationWitnessReconciliation(
        EuPrimaryEnumerationRootBinding primary,
        EuFeedRootIntersection witness,
        int checkedTerminationCount)
    {
        Primary = primary;
        Witness = witness;
        CheckedTerminationCount = checkedTerminationCount;
    }

    /// <summary>The primary enumeration this reconciliation checked the witness against.</summary>
    public EuPrimaryEnumerationRootBinding Primary { get; }

    /// <summary>The witness binding this reconciliation checked.</summary>
    public EuFeedRootIntersection Witness { get; }

    /// <summary>
    /// How many terminations this reconciliation was given, not how many were actually checked
    /// against the primary enumeration. <see cref="TryReconcile"/> only ever compares an in-pack or
    /// mixed-scope terminal's <see cref="EuFeedEntryTermination.InPack"/> roots; an out-of-pack or
    /// unresolved-or-ambiguous terminal is skipped entirely (R3/R7: neither carries a membership
    /// claim to corroborate) and still counts here. This is a total over the supplied rows, mirroring
    /// <see cref="EuFeedTerminalReconciliation.CanonicalEntryCount"/>'s own role one level up, never a
    /// count of rows this type actually validated.
    /// </summary>
    public int CheckedTerminationCount { get; }

    /// <summary>The only path that mints a reconciliation.</summary>
    /// <param name="primary">D1-05's own primary enumeration.</param>
    /// <param name="witness">The witness binding.</param>
    /// <param name="terminations">Every termination the witness classifier produced for this cut.</param>
    /// <param name="refusal">Why no reconciliation exists, when none does.</param>
    public static EuPrimaryEnumerationWitnessReconciliation? TryReconcile(
        EuPrimaryEnumerationRootBinding primary,
        EuFeedRootIntersection witness,
        IReadOnlyList<EuFeedEntryTermination> terminations,
        out EuPrimaryWitnessReconciliationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(terminations);

        var rows = terminations.ToArray();
        if (Array.Exists(rows, static row => row is null))
        {
            throw new ArgumentException("A termination cannot be null.", nameof(terminations));
        }

        if (string.Equals(
                primary.ClosureQueryPlanRef.Sha256,
                witness.ClosureMatrixRef.Sha256,
                StringComparison.Ordinal))
        {
            refusal = EuPrimaryWitnessReconciliationRefusal
                .ClosureIdentityNotStructurallyIndependentFromWitness;
            return null;
        }

        foreach (var row in rows)
        {
            if (row.Terminal is not (EuFeedTerminal.InPack or EuFeedTerminal.MixedScope))
            {
                continue;
            }

            foreach (var root in row.InPack)
            {
                if (!primary.Contains(root))
                {
                    refusal = EuPrimaryWitnessReconciliationRefusal
                        .WitnessInPackRootMissingFromPrimaryEnumeration;
                    return null;
                }
            }
        }

        refusal = EuPrimaryWitnessReconciliationRefusal.None;
        return new EuPrimaryEnumerationWitnessReconciliation(primary, witness, rows.Length);
    }
}
