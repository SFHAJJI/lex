using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-04c item 2: <see cref="LuxembourgProductionScopeReductionEvidenceResolver"/> is the first
/// production (non-test-fixture) implementation of <see cref="IScopeReductionEvidenceResolver"/> in
/// this codebase. Unlike <c>LuxembourgQueryExecutionAdapterTests.PermissiveEvidenceResolver</c>
/// (admits anything structurally well-formed), <c>FixedAdmittedSetEvidenceResolver</c> (admits a
/// caller-hand-transcribed digest set) and <c>AlwaysRefusingEvidenceResolver</c> (proves refusal
/// propagation only), this type must derive what it admits from evidence it independently holds:
/// the run's own real observations, and a custody-checked reopen of every evidence artifact
/// reference. These tests exercise it directly, against a real <see cref="ICustodyStore"/>, rather
/// than through the full adapter (constructing the resolver requires the adapter's own internal,
/// not-yet-exposed <c>observations</c>/<c>evidenceArtifacts</c>, so a unit-level construction is the
/// faithful way to prove this class's own logic without duplicating a live census+assertion run).
/// </summary>
[TestClass]
public sealed class LuxembourgProductionScopeReductionEvidenceResolverTests
{
    private static readonly SourceArtifactRef IdentityProfileRef = new(
        "urn:uuid:aaaaaaaa-0000-0000-0000-000000000001", new string('a', 64));

    private static readonly SourceArtifactRef CompleteEnumerationRef = new(
        "urn:uuid:aaaaaaaa-0000-0000-0000-000000000002", new string('b', 64));

    private const string DerivedSubjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
    private const string ForeignSubjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";

    [TestMethod]
    public async Task IsSelectorObservationAdmittedRequiresBothARealDerivedObjectAndARealCustodyDigest()
    {
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var writtenBytes = "real evidence bytes this run actually wrote"u8.ToArray();
        var receipt = await store.CreateAsync(writtenBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var writtenEvidenceRef = new SourceArtifactRef(
            "urn:uuid:aaaaaaaa-0000-0000-0000-000000000010", receipt.Reference.ContentSha256);
        var neverWrittenEvidenceRef = new SourceArtifactRef(
            "urn:uuid:aaaaaaaa-0000-0000-0000-000000000011", new string('f', 64));

        var observation = BuildObservation(DerivedSubjectUri);
        var resolver = await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(
            store, CompleteEnumerationRef, [observation], [writtenEvidenceRef], CancellationToken.None);

        var derivedObjectRefSha256 = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef);
        var foreignObjectRefSha256 = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
            BuildObservation(ForeignSubjectUri).ObjectRef);
        var syntacticDigest = new string('c', 64);

        // The real, positive case: a genuinely derived object and a genuinely custody-held evidence
        // artifact -- both independently re-verified, never trusted from the binding alone.
        Assert.IsTrue(
            resolver.IsSelectorObservationAdmitted(new ScopeSelectorObservationBinding(
                ScopeSelectorEvidenceKind.ObservedValueSet, derivedObjectRefSha256, 0,
                new SourceRegistryMemberRef(IdentityProfileRef, "selector.record"),
                IdentityProfileRef, IdentityProfileRef, writtenEvidenceRef, syntacticDigest)),
            "a real derived object with a real custody-held evidence artifact must be admitted");

        // Discrimination one: the object is real, but the evidence artifact was never written to
        // this run's custody. AlwaysRefusingEvidenceResolver could never distinguish this from the
        // positive case above (it refuses both); this resolver must refuse only this one.
        Assert.IsFalse(
            resolver.IsSelectorObservationAdmitted(new ScopeSelectorObservationBinding(
                ScopeSelectorEvidenceKind.ObservedValueSet, derivedObjectRefSha256, 0,
                new SourceRegistryMemberRef(IdentityProfileRef, "selector.record"),
                IdentityProfileRef, IdentityProfileRef, neverWrittenEvidenceRef, syntacticDigest)),
            "an evidence artifact this run's custody cannot reopen must never be admitted");

        // Discrimination two: the evidence artifact is real, but the object was never among this
        // run's own derived observations -- a caller naming a resource this run never observed.
        Assert.IsFalse(
            resolver.IsSelectorObservationAdmitted(new ScopeSelectorObservationBinding(
                ScopeSelectorEvidenceKind.ObservedValueSet, foreignObjectRefSha256, 0,
                new SourceRegistryMemberRef(IdentityProfileRef, "selector.record"),
                IdentityProfileRef, IdentityProfileRef, writtenEvidenceRef, syntacticDigest)),
            "an object this run never derived must never be admitted");
    }

    [TestMethod]
    public async Task IsSelectorNotApplicableAdmittedDiscriminatesOnDerivedObjectIdentityAlone()
    {
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var observation = BuildObservation(DerivedSubjectUri);
        var resolver = await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(
            store, CompleteEnumerationRef, [observation], [], CancellationToken.None);
        var derivedObjectRefSha256 = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef);
        var foreignObjectRefSha256 = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
            BuildObservation(ForeignSubjectUri).ObjectRef);
        var ruleMember = new SourceRegistryMemberRef(IdentityProfileRef, "role.body_candidate");

        Assert.IsTrue(
            resolver.IsSelectorNotApplicableAdmitted(new ScopeSelectorNotApplicableBinding(
                derivedObjectRefSha256, 0, new SourceRegistryMemberRef(IdentityProfileRef, "selector.record"),
                IdentityProfileRef, IdentityProfileRef, 0, ruleMember)));
        Assert.IsFalse(
            resolver.IsSelectorNotApplicableAdmitted(new ScopeSelectorNotApplicableBinding(
                foreignObjectRefSha256, 0, new SourceRegistryMemberRef(IdentityProfileRef, "selector.record"),
                IdentityProfileRef, IdentityProfileRef, 0, ruleMember)));
    }

    [TestMethod]
    public async Task IsRuleEvaluationAdmittedRequiresARealDerivedObjectAndSyntacticDigests()
    {
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var observation = BuildObservation(DerivedSubjectUri);
        var resolver = await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(
            store, CompleteEnumerationRef, [observation], [], CancellationToken.None);
        var derivedObjectRefSha256 = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef);
        var ruleMember = new SourceRegistryMemberRef(IdentityProfileRef, "role.body_candidate");
        var syntacticDigest = new string('d', 64);
        var notASha256 = "not-a-sha-256";

        Assert.IsTrue(
            resolver.IsRuleEvaluationAdmitted(new ScopeRuleEvaluationBinding(
                derivedObjectRefSha256, syntacticDigest, 0, ruleMember, IdentityProfileRef, IdentityProfileRef,
                syntacticDigest)));
        Assert.IsFalse(
            resolver.IsRuleEvaluationAdmitted(new ScopeRuleEvaluationBinding(
                derivedObjectRefSha256, notASha256, 0, ruleMember, IdentityProfileRef, IdentityProfileRef,
                syntacticDigest)),
            "a selector-set digest that is not even syntactically a SHA-256 must never be admitted");
    }

    [TestMethod]
    public async Task IsCompleteEnumerationAdmittedRequiresTheExactRefAndTheRealDerivedCount()
    {
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var resolver = await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(
            store, CompleteEnumerationRef, [BuildObservation(DerivedSubjectUri)], [], CancellationToken.None);
        var wrongRef = new SourceArtifactRef(
            "urn:uuid:aaaaaaaa-0000-0000-0000-000000000099", new string('e', 64));
        var sequenceDigest = new string('9', 64);

        Assert.IsTrue(
            resolver.IsCompleteEnumerationAdmitted(new ScopeCompleteEnumerationBinding(
                CompleteEnumerationRef, IdentityProfileRef, IdentityProfileRef, 1, sequenceDigest)));
        Assert.IsFalse(
            resolver.IsCompleteEnumerationAdmitted(new ScopeCompleteEnumerationBinding(
                wrongRef, IdentityProfileRef, IdentityProfileRef, 1, sequenceDigest)),
            "a complete-enumeration reference other than this run's own must never be admitted");
        Assert.IsFalse(
            resolver.IsCompleteEnumerationAdmitted(new ScopeCompleteEnumerationBinding(
                CompleteEnumerationRef, IdentityProfileRef, IdentityProfileRef, 2, sequenceDigest)),
            "an observed-object count other than this run's own real derived count must never be admitted");
    }

    private static LuxembourgResourceObservation BuildObservation(string subjectUri)
    {
        var objectRef = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(IdentityProfileRef, "legal_resource"),
            subjectUri,
            subjectUri,
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(subjectUri))),
            IdentityProfileRef,
            null);
        return new LuxembourgResourceObservation(
            objectRef,
            CompleteEnumerationRef,
            [],
            [],
            new LuxembourgSparqlRightsChannelObservations(CompleteEnumerationRef, CompleteEnumerationRef, []),
            new LuxembourgInFileRightsChannelObservations(CompleteEnumerationRef, CompleteEnumerationRef, []));
    }
}
