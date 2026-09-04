using System.Collections.ObjectModel;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Whether a consolidation step's own act resolved.
/// </summary>
/// <remarks>
/// <b>Not a second spelling of <see cref="TargetBodyScope"/>.</b> That enum answers whether a
/// resolved target's body is held, with three answers that all presume resolution succeeded. This
/// one answers whether it succeeded, which is the question R4's unresolved case is about, and it
/// has exactly two members because there are exactly two answers. An earlier version of E4 carried
/// a three-member EU target-state enum that duplicated <see cref="TargetBodyScope"/>'s held and
/// not-held answers to reach the unresolved one; the design verdict
/// <c>lex-event-20260904T192820932Z-4101310a2b7a482d87330f1eda1ec14a</c> named that class of
/// duplication as the defect, so the overlapping members are gone and only the genuinely new
/// distinction remains.
/// </remarks>
public enum EuConstituentMemberResolution
{
    /// <summary>The step's act resolved and can be reasoned about.</summary>
    Resolved,

    /// <summary>
    /// The step's act did not resolve. Nothing can be said about it, including that the rest of
    /// the chain is unaffected by it.
    /// </summary>
    Unresolved,
}

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
/// One consolidation step: a consolidated act, the act it is based on, and the act it consolidates,
/// each read from a named publisher predicate.
/// </summary>
/// <remarks>
/// <para>
/// The two predicates are the ones the E4 scope ruling names,
/// <see cref="EuAmendmentRelationVocabulary.ConsolidatedBasedOnPredicateUri"/> and
/// <see cref="EuAmendmentRelationVocabulary.ConsolidatedConsolidatesPredicateUri"/>. They answer
/// different questions: what the original act is, and what this consolidation supersedes. A closure
/// reading only one of them cannot tell a legitimate chain from two chains interleaved.
/// </para>
/// <para>
/// <b>A step names the predicate each half was read from</b>, and <see cref="Create"/> refuses any
/// other. Carrying the predicate rather than assuming it means the two constants are bound to the
/// data they describe instead of sitting in the vocabulary unused, and a step built from the wrong
/// predicate is refused by name rather than silently treated as the right one.
/// </para>
/// </remarks>
public sealed class EuConstituentStep
{
    private EuConstituentStep(
        OfficialIdentitySet consolidatedAct,
        OfficialIdentitySet basedOn,
        OfficialIdentitySet consolidates,
        EuConstituentMemberResolution resolution)
    {
        ConsolidatedAct = consolidatedAct;
        BasedOn = basedOn;
        Consolidates = consolidates;
        Resolution = resolution;
    }

    /// <summary>The consolidated act this step is.</summary>
    public OfficialIdentitySet ConsolidatedAct { get; }

    /// <summary>The act this consolidation is based on.</summary>
    public OfficialIdentitySet BasedOn { get; }

    /// <summary>The act this consolidation consolidates.</summary>
    public OfficialIdentitySet Consolidates { get; }

    /// <summary>Whether this step's own act resolved.</summary>
    public EuConstituentMemberResolution Resolution { get; }

    /// <summary>The predicate <see cref="BasedOn"/> was read from. Always the pinned one.</summary>
    public static string BasedOnPredicateUri =>
        EuAmendmentRelationVocabulary.ConsolidatedBasedOnPredicateUri;

    /// <summary>The predicate <see cref="Consolidates"/> was read from. Always the pinned one.</summary>
    public static string ConsolidatesPredicateUri =>
        EuAmendmentRelationVocabulary.ConsolidatedConsolidatesPredicateUri;

    /// <summary>
    /// Builds one step, refusing either half read from a predicate other than the pinned one.
    /// </summary>
    /// <param name="consolidatedAct">The consolidated act this step is.</param>
    /// <param name="basedOn">The act it is based on.</param>
    /// <param name="basedOnPredicateUri">
    /// The predicate <paramref name="basedOn"/> was read from. Must be
    /// <see cref="EuAmendmentRelationVocabulary.ConsolidatedBasedOnPredicateUri"/>.
    /// </param>
    /// <param name="consolidates">The act it consolidates.</param>
    /// <param name="consolidatesPredicateUri">
    /// The predicate <paramref name="consolidates"/> was read from. Must be
    /// <see cref="EuAmendmentRelationVocabulary.ConsolidatedConsolidatesPredicateUri"/>.
    /// </param>
    /// <param name="resolution">Whether <paramref name="consolidatedAct"/> resolved.</param>
    public static EuConstituentStep Create(
        OfficialIdentitySet consolidatedAct,
        OfficialIdentitySet basedOn,
        string basedOnPredicateUri,
        OfficialIdentitySet consolidates,
        string consolidatesPredicateUri,
        EuConstituentMemberResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(consolidatedAct);
        ArgumentNullException.ThrowIfNull(basedOn);
        ArgumentNullException.ThrowIfNull(consolidates);

        if (!string.Equals(basedOnPredicateUri, BasedOnPredicateUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(basedOnPredicateUri)}\" is not "
                    + BasedOnPredicateUri + ", the predicate a based-on member is read from.",
                nameof(basedOnPredicateUri));
        }

        if (!string.Equals(
                consolidatesPredicateUri, ConsolidatesPredicateUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"\"{EuAuthorityQualifiedToken.Describe(consolidatesPredicateUri)}\" is not "
                    + ConsolidatesPredicateUri
                    + ", the predicate a consolidates member is read from.",
                nameof(consolidatesPredicateUri));
        }

        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentException(
                $"{resolution} is not a declared EuConstituentMemberResolution member.",
                nameof(resolution));
        }

        return new EuConstituentStep(consolidatedAct, basedOn, consolidates, resolution);
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

            if (step.Resolution == EuConstituentMemberResolution.Unresolved)
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
