using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-05c-2 end to end: one real Appendix A seed, driven through the census family and all three
/// D1-05c-1 object-facts families (P, X, W), decoded, reduced, and written as a held scope manifest,
/// with the first-cut watermark witness frozen at the end. Every count this test asserts is read from
/// the adapter's own result after a real run against the fixture's scripted transport, never asserted
/// or estimated (D1-05c-2 precision six).
/// </summary>
[TestClass]
public sealed class EuQueryExecutionAdapterTests
{
    private static readonly SourceArtifactRef CompleteEnumerationRef = new(
        "urn:uuid:00000000-0000-4000-8000-0000000000f0",
        new string('a', 64));

    [TestMethod]
    public async Task AFullRunOverOneSeedWithNoDiscoveredStatesDeliversWithRealMeasuredCounts()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000001.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        // Family P: all nine object-authority predicates plus all four read relation predicates,
        // exactly one outcome row each (a bound value or the explicit unbound marker) -- the shape
        // EuCellarObjectDecode.TryBuildPredicateObservation and TryBuildRelationFamilyObservation both
        // require per object. resource_legal_type carries the one bound value this test's own
        // TryResolveRecordForm reads back to resolve EuActForm.Regulation.
        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes);

        Assert.AreEqual(13, pRows.Count, "family P must carry exactly 13 predicate outcomes for one object.");

        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri) };
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Count, pRows,
                EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows,
                EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows,
                EuAcquisitionTestFixture.RootWatermarkProjection),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(1));

        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(2));

        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(3));

        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(4));

        var evidenceResolver = new PermissiveEvidenceResolver(CompleteEnumerationRef);

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            evidenceResolver,
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail} " +
            $"decode={result.DecodeRefusal} offendingIri={result.DecodeOffendingIri} snapshot={result.DecodeSnapshotRefusal}");

        // ---- Precision six: real measured counts, never estimated. ----
        Assert.AreEqual(1, result.ObservedObjectCount, "O must be exactly the one root; no states were discovered.");
        Assert.AreEqual(1, result.ObservedExpressionCount, "the Expression set X discovered must be exactly one.");
        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, result.Completion);
        Assert.AreEqual(4, result.FamilyOutcomes.Count, "one census seed plus one batch each of P, X and W.");
        foreach (var outcome in result.FamilyOutcomes)
        {
            Assert.AreEqual(EuFamilyEnumerationOutcomeKind.Proven, outcome.Kind, outcome.FamilyKey);
            Assert.IsNotNull(outcome.DeliveredRowCount);
            Assert.IsTrue(
                outcome.DeliveredRowCount!.Value < EuConsolidationDiscoveryPlan.PublisherDeliveryCeilingRows,
                $"family '{outcome.FamilyKey}' delivered {outcome.DeliveredRowCount} rows, at or above the " +
                $"{EuConsolidationDiscoveryPlan.PublisherDeliveryCeilingRows} ceiling.");
        }

        // The measured delivered-row counts per family, exactly as this run observed them.
        var byRows = result.FamilyOutcomes.Select(static o => o.DeliveredRowCount!.Value).OrderBy(static v => v).ToArray();
        CollectionAssert.AreEqual(new long[] { 0, 1, 1, 13 }, byRows);

        // ---- Precision two: the closure is bound to Appendix A's own 82-seed pack by identity. ----
        Assert.IsNotNull(result.RootBinding);
        CollectionAssert.AreEqual(new[] { rootIri }, result.RootBinding!.DiscoveredRoots.ToArray());
        Assert.IsTrue(result.RootBinding.Contains(rootIri));

        // ---- The manifest was written and holds under the custody floor. ----
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.ScopeManifestCanonicalSha256);
        Assert.AreEqual(0, result.ReductionExclusions.Count, "the current decode never emits an unreducible authority.");

        // ---- Precision three: the witness is frozen at the census bound and never executed. ----
        Assert.IsNotNull(result.WatermarkWitnessPlan);
        Assert.AreEqual(watermarkLexical, result.WatermarkWitnessPlan!.StartPosition.WatermarkLexical);
        Assert.AreEqual("eu-consolidation-root:" + rootIri, result.WatermarkWitnessPlan.StartPosition.CanonicalEntryKey);
        Assert.AreEqual(EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint, result.WatermarkWitnessPlan.Endpoint);
    }

    [TestMethod]
    public async Task AnUnresolvableRecordFormRefusesTheSeedRatherThanGuessing()
    {
        // D1-05c-2's own filled gap: EuCellarObjectDecode.TryDecode requires a caller-resolved
        // EuActForm it does not derive itself. This proves the adapter refuses honestly, naming the
        // seed, rather than defaulting to any one closed member, when family P's own
        // resource_legal_type observation cannot be mapped.
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000002.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";
        const string unmappableResourceType = "http://publications.europa.eu/resource/authority/resource-type/UNKNOWN_CODE";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? unmappableResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes);

        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri) };
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Count, pRows,
                EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows,
                EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows,
                EuAcquisitionTestFixture.RootWatermarkProjection),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(5));
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(6));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(7));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(8));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.RecordFormNotResolved, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, seed.Celex);
    }

    /// <summary>
    /// Structural admission only (well-formed digests, matching complete-enumeration identity),
    /// mirroring <c>LuxembourgQueryExecutionAdapterTests.PermissiveEvidenceResolver</c> exactly: these
    /// tests are not re-proving <see cref="ScopeReducer"/>'s admission correctness, which
    /// <c>ScopeManifestContractTests</c> already covers; they are proving this adapter wires the
    /// already-merged EU scope-reduction pipeline correctly.
    /// </summary>
    private sealed class PermissiveEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            IsSha256(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
