using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// Admits Union scope bindings only against observations and retained evidence from the run that is
/// constructing the manifest. Creation happens before manifest construction, so verification never
/// derives its admission set from the manifest being verified.
/// </summary>
public sealed class EuProductionScopeReductionEvidenceResolver : IScopeReductionEvidenceResolver
{
    private readonly IReadOnlySet<string> _observedObjectRefSha256Values;
    private readonly IReadOnlySet<SourceArtifactRef> _custodyConfirmedEvidenceArtifacts;

    private EuProductionScopeReductionEvidenceResolver(
        SourceArtifactRef completeEnumerationRef,
        IReadOnlySet<string> observedObjectRefSha256Values,
        IReadOnlySet<SourceArtifactRef> custodyConfirmedEvidenceArtifacts)
    {
        CompleteEnumerationRef = completeEnumerationRef;
        _observedObjectRefSha256Values = observedObjectRefSha256Values;
        _custodyConfirmedEvidenceArtifacts = custodyConfirmedEvidenceArtifacts;
    }

    public SourceArtifactRef CompleteEnumerationRef { get; }

    public static async Task<EuProductionScopeReductionEvidenceResolver> CreateAsync(
        ICustodyStore custodyStore,
        SourceArtifactRef completeEnumerationRef,
        IReadOnlyList<SourceObjectRef> observedObjects,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(completeEnumerationRef);
        ArgumentNullException.ThrowIfNull(observedObjects);
        ArgumentNullException.ThrowIfNull(evidenceArtifacts);

        var observedObjectRefSha256Values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observedObject in observedObjects)
        {
            ArgumentNullException.ThrowIfNull(observedObject);
            observedObjectRefSha256Values.Add(
                ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observedObject));
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

        return new EuProductionScopeReductionEvidenceResolver(
            completeEnumerationRef,
            observedObjectRefSha256Values,
            custodyConfirmedEvidenceArtifacts);
    }

    public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _observedObjectRefSha256Values.Contains(binding.ObjectRefSha256) &&
            IsSyntacticSha256(binding.SelectorEvidenceSha256) &&
            _custodyConfirmedEvidenceArtifacts.Contains(binding.EvidenceArtifactRef);
    }

    public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _observedObjectRefSha256Values.Contains(binding.ObjectRefSha256);
    }

    public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return _observedObjectRefSha256Values.Contains(binding.ObjectRefSha256) &&
            IsSyntacticSha256(binding.SelectorSetSha256) &&
            IsSyntacticSha256(binding.RuleEvaluationSha256);
    }

    public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.CompleteEnumerationRef == CompleteEnumerationRef &&
            binding.ObservedObjectCount == _observedObjectRefSha256Values.Count;
    }

    private static async Task<bool> IsReopenableFromCustodyAsync(
        ICustodyStore custodyStore,
        SourceArtifactRef artifact,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await CustodyRestore.ReadByDigestCheckedAsync(
                    custodyStore, artifact.Sha256, cancellationToken)
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
