using System.Collections.ObjectModel;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a constituent closure could not be validated. These are the four cases Candidate 5 R4
/// names, and a closure that hits any of them blocks.
/// </summary>
public enum EuConstituentClosureRefusal
{
    /// <summary>The closure was validated. No refusal.</summary>
    None,

    /// <summary>
    /// A member did not resolve. Nothing can be said about a step whose act is unknown, including
    /// that the rest of the chain is unaffected by it.
    /// </summary>
    UnresolvedMember,

    /// <summary>The chain revisits an act, so it is not a backward walk and has no beginning.</summary>
    CyclicChain,

    /// <summary>
    /// A member is based on a different act entirely, sharing no identifier with the root. R7's
    /// walk is under one root identity, and this member is under another.
    /// </summary>
    CrossRootMember,

    /// <summary>
    /// The evidence disagrees with itself in a way nothing here explains: a member that overlaps
    /// the root's identity without matching it, or a step consolidating an act that is not the one
    /// before it in the chain.
    /// </summary>
    UnexplainedMismatch,
}

/// <summary>
/// One consolidation step: a consolidated act, the act it is based on, and the act it consolidates.
/// </summary>
/// <remarks>
/// The two predicates are the ones the E4 scope ruling names,
/// <c>act_consolidated_based_on_resource_legal</c> and
/// <c>act_consolidated_consolidates_resource_legal</c>. They answer different questions: what the
/// original act is, and what this consolidation supersedes. A closure that reads only one of them
/// cannot tell a legitimate chain from two chains interleaved.
/// </remarks>
public sealed class EuConstituentStep
{
    private EuConstituentStep(
        OfficialIdentitySet consolidatedAct,
        OfficialIdentitySet basedOn,
        OfficialIdentitySet consolidates,
        EuRelationTargetState targetState)
    {
        ConsolidatedAct = consolidatedAct;
        BasedOn = basedOn;
        Consolidates = consolidates;
        TargetState = targetState;
    }

    /// <summary>The consolidated act this step is.</summary>
    public OfficialIdentitySet ConsolidatedAct { get; }

    /// <summary>The act this consolidation is based on, by <c>act_consolidated_based_on_resource_legal</c>.</summary>
    public OfficialIdentitySet BasedOn { get; }

    /// <summary>The act this consolidation consolidates, by <c>act_consolidated_consolidates_resource_legal</c>.</summary>
    public OfficialIdentitySet Consolidates { get; }

    /// <summary>How this step's own act stands.</summary>
    public EuRelationTargetState TargetState { get; }

    /// <summary>Builds one step.</summary>
    public static EuConstituentStep Create(
        OfficialIdentitySet consolidatedAct,
        OfficialIdentitySet basedOn,
        OfficialIdentitySet consolidates,
        EuRelationTargetState targetState)
    {
        ArgumentNullException.ThrowIfNull(consolidatedAct);
        ArgumentNullException.ThrowIfNull(basedOn);
        ArgumentNullException.ThrowIfNull(consolidates);
        if (!Enum.IsDefined(targetState))
        {
            throw new ArgumentException(
                $"{targetState} is not a declared EuRelationTargetState member.",
                nameof(targetState));
        }

        return new EuConstituentStep(consolidatedAct, basedOn, consolidates, targetState);
    }
}

/// <summary>
/// The transitive closure of consolidation steps under one root act, or a typed refusal saying why
/// it could not be validated.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unvalidatable closure blocks; it never answers partially.</b> That is the whole point of
/// this type, and it is enforced structurally rather than by convention: on a refusal
/// <see cref="Chain"/> throws. There is no property, no out parameter and no factory that hands
/// back the steps validated before the refusal. A partial consolidation chain is the most
/// dangerous shape this system can produce, because it looks exactly like a complete one and
/// silently answers "what was in force" with a history that stops early.
/// </para>
/// <para>
/// <b>KEEP from v2.</b> The walk direction and the two predicates are v2's consolidation query
/// shape, from <c>src/Lex.Sources.EurLex/EurLexAdapter.cs</c> <c>ConsolidationsQuery</c> in the v2
/// repository, proven in review/22 section 3, together with its rule closing each dated state at
/// the next consolidation date minus one day. That rule is a property of the chain's order, so it
/// is only sound on a chain validated end to end, which is the second reason this type refuses to
/// return a partial one: a partial chain would close its last state at a date that is not the next
/// consolidation.
/// </para>
/// <para>
/// <b>Identity comparison is <see cref="OfficialIdentitySet.SameIdentity"/></b>, this repository's
/// canonical order-independent comparison, and not a looser overlap test. That choice has a
/// visible consequence worth stating: two sets naming the same act with different numbers of
/// identifiers do not match. Rather than quietly widening the comparison, that case is separated
/// out and reported as <see cref="EuConstituentClosureRefusal.UnexplainedMismatch"/>, because a
/// set that overlaps the root without equalling it is exactly evidence disagreeing with itself.
/// </para>
/// </remarks>
public sealed class EuConstituentClosure
{
    private readonly IReadOnlyList<EuConstituentStep>? _chain;

    private EuConstituentClosure(
        OfficialIdentitySet root,
        IReadOnlyList<EuConstituentStep>? chain,
        EuConstituentClosureRefusal refusal,
        string? refusedDetail)
    {
        Root = root;
        _chain = chain;
        Refusal = refusal;
        RefusedDetail = refusedDetail;
    }

    /// <summary>The root act the closure walks under.</summary>
    public OfficialIdentitySet Root { get; }

    /// <summary>Which R4 case blocked this closure, or <see cref="EuConstituentClosureRefusal.None"/>.</summary>
    public EuConstituentClosureRefusal Refusal { get; }

    /// <summary>What was refused, naming the offending step. <c>null</c> when validated.</summary>
    public string? RefusedDetail { get; }

    /// <summary>Whether the closure validated end to end.</summary>
    public bool IsValidated => Refusal == EuConstituentClosureRefusal.None;

    /// <summary>
    /// The validated chain, oldest step first.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The closure was refused. There is deliberately no way to read the steps that validated
    /// before the refusal.
    /// </exception>
    public IReadOnlyList<EuConstituentStep> Chain =>
        _chain ?? throw new InvalidOperationException(
            $"This constituent closure was refused ({Refusal}: {RefusedDetail}). A refused "
                + "closure has no chain to read, partial or otherwise.");

    /// <summary>
    /// Validates a consolidation chain under one root, oldest step first, and blocks on any of
    /// R4's four cases.
    /// </summary>
    public static EuConstituentClosure Validate(
        OfficialIdentitySet root,
        IReadOnlyList<EuConstituentStep> steps)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(steps);

        var ordered = steps.ToArray();
        if (Array.IndexOf(ordered, null) >= 0)
        {
            throw new ArgumentException("A step cannot be null.", nameof(steps));
        }

        var seen = new List<OfficialIdentitySet> { root };
        for (var index = 0; index < ordered.Length; index++)
        {
            var step = ordered[index];

            if (step.TargetState == EuRelationTargetState.Unresolved)
            {
                return Refuse(
                    root,
                    EuConstituentClosureRefusal.UnresolvedMember,
                    $"step {index} did not resolve");
            }

            // A revisit is checked before the two identity comparisons, because a cycle also shows
            // up as a mismatch and the cycle is the more specific and more useful answer.
            if (seen.Any(step.ConsolidatedAct.SameIdentity))
            {
                return Refuse(
                    root,
                    EuConstituentClosureRefusal.CyclicChain,
                    $"step {index} revisits an act already in the chain");
            }

            if (!step.BasedOn.SameIdentity(root))
            {
                return Refuse(
                    root,
                    Overlaps(step.BasedOn, root)
                        ? EuConstituentClosureRefusal.UnexplainedMismatch
                        : EuConstituentClosureRefusal.CrossRootMember,
                    Overlaps(step.BasedOn, root)
                        ? $"step {index} is based on an act overlapping the root without matching it"
                        : $"step {index} is based on an act sharing no identifier with the root");
            }

            var expected = index == 0 ? root : ordered[index - 1].ConsolidatedAct;
            if (!step.Consolidates.SameIdentity(expected))
            {
                return Refuse(
                    root,
                    EuConstituentClosureRefusal.UnexplainedMismatch,
                    $"step {index} consolidates an act that is not the one before it in the chain");
            }

            seen.Add(step.ConsolidatedAct);
        }

        return new EuConstituentClosure(
            root,
            new ReadOnlyCollection<EuConstituentStep>(ordered),
            EuConstituentClosureRefusal.None,
            refusedDetail: null);
    }

    private static EuConstituentClosure Refuse(
        OfficialIdentitySet root,
        EuConstituentClosureRefusal refusal,
        string detail) =>
        new(root, chain: null, refusal, detail);

    /// <summary>Whether two sets share any raw identifier value under the same family.</summary>
    private static bool Overlaps(OfficialIdentitySet left, OfficialIdentitySet right)
    {
        foreach (var identifier in left.Identifiers)
        {
            if (string.Equals(
                    right.Value(identifier.Family),
                    identifier.RawValue,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
