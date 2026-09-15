using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuProductionScopeReductionEvidenceResolverTests
{
    private static readonly SourceArtifactRef IdentityProfileRef = new(
        "urn:uuid:bbbbbbbb-0000-0000-0000-000000000001", new string('a', 64));

    private static readonly SourceArtifactRef CompleteEnumerationRef = new(
        "urn:uuid:bbbbbbbb-0000-0000-0000-000000000002", new string('b', 64));

    [TestMethod]
    public async Task SelectorObservationRequiresAnObservedObjectAndCustodyBackedEvidence()
    {
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var receipt = await store.CreateAsync(
            "retained Union interpretation profile"u8.ToArray(),
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        var retainedEvidence = new SourceArtifactRef(
            "urn:uuid:bbbbbbbb-0000-0000-0000-000000000010",
            receipt.Reference.ContentSha256);
        var absentEvidence = new SourceArtifactRef(
            "urn:uuid:bbbbbbbb-0000-0000-0000-000000000011",
            new string('f', 64));
        var observed = BuildObject("http://publications.europa.eu/resource/cellar/observed");
        var foreign = BuildObject("http://publications.europa.eu/resource/cellar/foreign");
        var resolver = await EuProductionScopeReductionEvidenceResolver.CreateAsync(
            store,
            CompleteEnumerationRef,
            [observed],
            [new EuScopeReductionEvidenceObservation(IdentityProfileRef, [retainedEvidence])],
            CancellationToken.None);
        var resolverWithAbsentEvidence = await EuProductionScopeReductionEvidenceResolver.CreateAsync(
            store,
            CompleteEnumerationRef,
            [observed],
            [new EuScopeReductionEvidenceObservation(IdentityProfileRef, [absentEvidence])],
            CancellationToken.None);
        var observedDigest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observed);
        var foreignDigest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(foreign);

        Assert.IsTrue(resolver.IsSelectorObservationAdmitted(Selector(observedDigest, IdentityProfileRef)));
        Assert.IsFalse(
            resolverWithAbsentEvidence.IsSelectorObservationAdmitted(Selector(observedDigest, IdentityProfileRef)),
            "a shaped digest that this run's custody cannot reopen must be refused");
        Assert.IsFalse(
            resolver.IsSelectorObservationAdmitted(Selector(foreignDigest, IdentityProfileRef)),
            "a custody-backed artifact cannot admit an object absent from this run's decoded observations");
    }

    [TestMethod]
    public async Task ObjectOnlyBindingsAndCompleteEnumerationUseTheRunDerivedIdentitySet()
    {
        var store = new RoutedHttpAcquisitionSessionAuditTests.RecordingCustodyStore { RefuseFallback = true };
        var observed = BuildObject("http://publications.europa.eu/resource/cellar/observed");
        var foreign = BuildObject("http://publications.europa.eu/resource/cellar/foreign");
        var resolver = await EuProductionScopeReductionEvidenceResolver.CreateAsync(
            store, CompleteEnumerationRef, [observed], [], CancellationToken.None);
        var observedDigest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observed);
        var foreignDigest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(foreign);
        var member = new SourceRegistryMemberRef(IdentityProfileRef, "selector.record");
        var rule = new SourceRegistryMemberRef(IdentityProfileRef, "projection.record");

        Assert.IsTrue(resolver.IsSelectorNotApplicableAdmitted(new ScopeSelectorNotApplicableBinding(
            observedDigest, 0, member, IdentityProfileRef, IdentityProfileRef, 0, rule)));
        Assert.IsFalse(resolver.IsSelectorNotApplicableAdmitted(new ScopeSelectorNotApplicableBinding(
            foreignDigest, 0, member, IdentityProfileRef, IdentityProfileRef, 0, rule)));

        Assert.IsTrue(resolver.IsRuleEvaluationAdmitted(new ScopeRuleEvaluationBinding(
            observedDigest, new string('c', 64), 0, rule, IdentityProfileRef, IdentityProfileRef,
            new string('d', 64))));
        Assert.IsFalse(resolver.IsRuleEvaluationAdmitted(new ScopeRuleEvaluationBinding(
            foreignDigest, new string('c', 64), 0, rule, IdentityProfileRef, IdentityProfileRef,
            new string('d', 64))));

        Assert.IsTrue(resolver.IsCompleteEnumerationAdmitted(new ScopeCompleteEnumerationBinding(
            CompleteEnumerationRef, IdentityProfileRef, IdentityProfileRef, 1, new string('e', 64))));
        Assert.IsFalse(resolver.IsCompleteEnumerationAdmitted(new ScopeCompleteEnumerationBinding(
            CompleteEnumerationRef, IdentityProfileRef, IdentityProfileRef, 2, new string('e', 64))));
        Assert.IsFalse(resolver.IsCompleteEnumerationAdmitted(new ScopeCompleteEnumerationBinding(
            new SourceArtifactRef("urn:uuid:bbbbbbbb-0000-0000-0000-000000000099", new string('9', 64)),
            IdentityProfileRef, IdentityProfileRef, 1, new string('e', 64))));
    }

    [TestMethod]
    public void PublicRunDoorDoesNotAcceptCallerSuppliedEvidenceAuthority()
    {
        var publicRunMethods = typeof(EuQueryExecutionAdapter).GetMethods()
            .Where(method => method.Name == nameof(EuQueryExecutionAdapter.RunAsync));

        Assert.IsFalse(
            publicRunMethods.Any(method => method.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(IScopeReductionEvidenceResolver))),
            "production callers must not be able to supply scope-reduction admission answers");
    }

    private static ScopeSelectorObservationBinding Selector(
        string objectDigest,
        SourceArtifactRef evidence) =>
        new(
            ScopeSelectorEvidenceKind.ObservedValueSet,
            objectDigest,
            0,
            new SourceRegistryMemberRef(IdentityProfileRef, "selector.record"),
            IdentityProfileRef,
            IdentityProfileRef,
            evidence,
            new string('c', 64));

    private static SourceObjectRef BuildObject(string sourceId) =>
        new(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(IdentityProfileRef, "legal_resource"),
            sourceId,
            sourceId,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(sourceId))),
            IdentityProfileRef,
            null);
}
