using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// D1-04c item 2: the production <see cref="IScopeReductionEvidenceResolver"/> the interface's own
/// doc comment says is "expected to come from whichever slice wires a live acquisition run's held
/// evidence into <see cref="VerifiedLuxembourgSourceProfile.ReduceScope"/> for the first time" -- this
/// is that slice. Unlike every prior implementation in this codebase (the test-only
/// <c>LuxembourgQueryExecutionAdapterTests.PermissiveEvidenceResolver</c>, <c>FixedAdmittedSetEvidenceResolver</c>
/// and <c>AlwaysRefusingEvidenceResolver</c>), this type admits nothing on a caller's say-so: every
/// admission question is answered against evidence this exact run independently holds, computed by
/// <see cref="CreateAsync"/> before any admission question is ever asked, never trusted from a
/// caller-supplied set.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of re-verification, matched to what this resolver can actually reach without a second,
/// independent custody dependency (Decision 78) or an assembly-boundary door Decision 80 refuses
/// (<see cref="ScopeManifestCanonicalWriter"/>'s own selector/rule-evaluation digest methods are
/// <c>internal</c> to <c>Lex.V3.Contracts</c>, so this resolver, living in <c>Lex.V3.Ingest</c>,
/// cannot recompute <see cref="ScopeSelectorObservationBinding.SelectorEvidenceSha256"/> or
/// <see cref="ScopeRuleEvaluationBinding.RuleEvaluationSha256"/> bit-for-bit; only
/// <see cref="ScopeManifestCanonicalWriter.ComputeObjectRefSha256"/> is public, and this type uses
/// exactly that):
/// </para>
/// <list type="bullet">
/// <item>
/// Object identity: every binding's <c>ObjectRefSha256</c> must equal
/// <see cref="ScopeManifestCanonicalWriter.ComputeObjectRefSha256"/> of one of the
/// <see cref="LuxembourgResourceObservation.ObjectRef"/> values this exact run derived (from its own
/// independently reopened and re-verified census/assertion rows -- see
/// <see cref="LuxembourgQueryExecutionAdapter.BuildResourceObservations"/>), never a caller-hand-transcribed
/// admitted set. A binding naming a resource this run never actually observed is refused.
/// </item>
/// <item>
/// Evidence-artifact custody: a <see cref="ScopeSelectorObservationBinding.EvidenceArtifactRef"/> is
/// admitted only when <see cref="CreateAsync"/> already reopened that exact digest from
/// <paramref name="custodyStore" /> through <see cref="CustodyRestore.ReadByDigestCheckedAsync"/> --
/// the same checked-read door <see cref="LuxembourgQueryExecutionAdapter"/> itself reopens pages and
/// rows through -- and the read succeeded. A digest this run's own custody cannot produce back is
/// never admitted, regardless of what the binding claims.
/// </item>
/// </list>
/// <para>
/// <see cref="IsSelectorNotApplicableAdmitted"/> and <see cref="IsRuleEvaluationAdmitted"/> carry no
/// custody-backed evidence artifact of their own (only an object identity and opaque, internally
/// computed digests this assembly cannot recompute), so object identity is the only independent
/// check available to them; a hand-shaped digest this resolver cannot verify further is admitted only
/// syntactically, exactly as every prior implementation of this interface in this codebase already
/// does. <see cref="IsCompleteEnumerationAdmitted"/> goes one step further than every prior
/// implementation: it requires the observed-object count to equal this run's own derived object
/// count, not merely that the complete-enumeration reference matches.
/// </para>
/// </remarks>
public sealed class LuxembourgProductionScopeReductionEvidenceResolver : IScopeReductionEvidenceResolver
{
    private readonly IReadOnlySet<string> _derivedObjectRefSha256Values;
    private readonly IReadOnlySet<SourceArtifactRef> _custodyConfirmedEvidenceArtifacts;

    private LuxembourgProductionScopeReductionEvidenceResolver(
        SourceArtifactRef completeEnumerationRef,
        IReadOnlySet<string> derivedObjectRefSha256Values,
        IReadOnlySet<SourceArtifactRef> custodyConfirmedEvidenceArtifacts)
    {
        CompleteEnumerationRef = completeEnumerationRef;
        _derivedObjectRefSha256Values = derivedObjectRefSha256Values;
        _custodyConfirmedEvidenceArtifacts = custodyConfirmedEvidenceArtifacts;
    }

    public SourceArtifactRef CompleteEnumerationRef { get; }

    /// <summary>
    /// The only door onto this type. Does the real work up front, once, rather than per admission
    /// question: computes this run's own derived object-identity set from
    /// <paramref name="observations"/> (never a caller-supplied list -- these are the adapter's own
    /// independently re-derived <see cref="LuxembourgResourceObservation"/> values), and attempts a
    /// custody-checked reopen of every distinct <paramref name="evidenceArtifacts"/> reference,
    /// recording which ones this run's own custody can actually produce back. A digest that cannot be
    /// reopened is silently excluded from the confirmed set here, not thrown: it is ordinary, expected
    /// evidence for <see cref="IsSelectorObservationAdmitted"/> to then refuse, not a construction
    /// failure of this resolver itself.
    /// </summary>
    public static async Task<LuxembourgProductionScopeReductionEvidenceResolver> CreateAsync(
        ICustodyStore custodyStore,
        SourceArtifactRef completeEnumerationRef,
        IReadOnlyList<LuxembourgResourceObservation> observations,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(completeEnumerationRef);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(evidenceArtifacts);

        var derivedObjectRefSha256Values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            derivedObjectRefSha256Values.Add(
                ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef));
        }

        var custodyConfirmedEvidenceArtifacts = new HashSet<SourceArtifactRef>();
        foreach (var artifact in evidenceArtifacts.Distinct())
        {
            ArgumentNullException.ThrowIfNull(artifact);
            if (await IsReopenableFromCustodyAsync(custodyStore, artifact, cancellationToken)
                .ConfigureAwait(false))
            {
                custodyConfirmedEvidenceArtifacts.Add(artifact);
            }
        }

        return new LuxembourgProductionScopeReductionEvidenceResolver(
            completeEnumerationRef, derivedObjectRefSha256Values, custodyConfirmedEvidenceArtifacts);
    }

    public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _derivedObjectRefSha256Values.Contains(binding.ObjectRefSha256) &&
            IsSyntacticSha256(binding.SelectorEvidenceSha256) &&
            _custodyConfirmedEvidenceArtifacts.Contains(binding.EvidenceArtifactRef);
    }

    public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _derivedObjectRefSha256Values.Contains(binding.ObjectRefSha256);
    }

    public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _derivedObjectRefSha256Values.Contains(binding.ObjectRefSha256) &&
            IsSyntacticSha256(binding.SelectorSetSha256) &&
            IsSyntacticSha256(binding.RuleEvaluationSha256);
    }

    public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.CompleteEnumerationRef == CompleteEnumerationRef &&
            binding.ObservedObjectCount == _derivedObjectRefSha256Values.Count;
    }

    /// <summary>
    /// The exact checked-read pattern <see cref="LuxembourgQueryExecutionAdapter"/> and
    /// <see cref="RepeatedEnumerationDeliveryReopenGlue"/> already use to reopen pages and the scope
    /// manifest itself: <see cref="CustodyRestore.ReadByDigestCheckedAsync"/> proves the store holds
    /// bytes that hash to the exact digest, not merely that some bytes exist under that key. A read
    /// failure is not this resolver's failure; it means the caller's evidence artifact reference names
    /// something this run's own custody cannot back, which is exactly the case
    /// <see cref="IsSelectorObservationAdmitted"/> must refuse.
    /// </summary>
    private static async Task<bool> IsReopenableFromCustodyAsync(
        ICustodyStore custodyStore, SourceArtifactRef artifact, CancellationToken cancellationToken)
    {
        try
        {
            _ = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, artifact.Sha256, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
            when (exception is CustodyIntegrityException or CustodyRequiredException or CustodyPolicyException)
        {
            return false;
        }
    }

    private static bool IsSyntacticSha256(string value) =>
        value.Length == 64 &&
        value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
