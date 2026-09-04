using System.Net;
using System.Text;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
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
            // Defect 3's own fix drives a real witness traversal from the census bound
            // (watermarkLexical, this same root) on every delivered run now, not just when a test is
            // specifically about the witness. Scripted here as a clean confirmed-empty traversal:
            // nothing changed between the census bound and this run's own send.
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessEmptyTraversalScript(
                    rootIri, watermarkLexical)),
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
            EuAcquisitionTestFixture.BuildRendererSource(9),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1009),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            evidenceResolver,
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail} " +
            $"decode={result.DecodeRefusal} offendingIri={result.DecodeOffendingIri} snapshot={result.DecodeSnapshotRefusal}");

        // Defect 3's own fix, proven here too: the witness endpoint was actually reached, and both
        // scripted responses (the confirmed-empty traversal) were consumed.
        Assert.AreEqual(2, handler.OccurrenceCountFor("Witness"), "the witness's own confirmed-empty traversal must send exactly two requests.");

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

        // ---- Precision three: the witness is frozen at the census bound. ----
        Assert.IsNotNull(result.WatermarkWitnessPlan);
        Assert.AreEqual(watermarkLexical, result.WatermarkWitnessPlan!.StartPosition.WatermarkLexical);
        Assert.AreEqual(rootIri, result.WatermarkWitnessPlan.StartPosition.CanonicalEntryKey);
        Assert.AreEqual(EuWatermarkWitnessPlan.OfficialCellarSparqlEndpoint, result.WatermarkWitnessPlan.Endpoint);

        // Defect 3's own driving assertion: the frozen witness is actually reconciled against this
        // run's own primary enumeration, not left untouched. Decision 81's first cut has nothing of
        // its own to reconcile against yet, so the reconciliation's own checked-termination count is
        // zero -- a clean pass by construction, per EuPrimaryEnumerationWitnessReconciliation's own
        // remarks, never a missing-data refusal.
        Assert.IsNotNull(result.WitnessReconciliation);
        Assert.AreEqual(0, result.WitnessReconciliation!.CheckedTerminationCount);
        Assert.AreSame(result.RootBinding, result.WitnessReconciliation.Primary);

        // D1-06c-EU defect 4: the one Minted row this run produced (the seed's own root, the only
        // object O contains here) actually went through the routed session and was classified as a
        // real 200, held under this run's own floor. The receipt's own content digest is the real
        // GDPR xhtml canary's, proving this was the actual fetched body, not a placeholder.
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.HasCount(1, result.DocumentAcquisitionOutcomesByOrdinal!);
        var acquisitionOutcome = result.DocumentAcquisitionOutcomesByOrdinal[0];
        Assert.IsNotNull(acquisitionOutcome.Receipt);
        Assert.IsNull(acquisitionOutcome.Refusal);
        Assert.AreEqual(
            "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d",
            acquisitionOutcome.Receipt!.Reference.ContentSha256);
        Assert.AreEqual(806864, acquisitionOutcome.Receipt.Reference.ByteLength);
    }

    /// <summary>
    /// D1-06c-EU defect 4's own required test: "a successful fetch whose resulting receipt's
    /// classified floor is below what this run requires must be refused, not silently accepted as
    /// Held." The document-fetch GET itself completes as a real 200 (the default scripted response);
    /// only the custody write for that one body's own digest is unenforced. The whole run refuses,
    /// naming the row it happened on, rather than quietly returning a Held outcome the store never
    /// actually protected.
    /// </summary>
    [TestMethod]
    public async Task ASuccessfulDocumentFetchWhoseBodyCustodyWriteIsUnenforcedRefusesTheWholeRunRatherThanHoldingIt()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000801.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
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
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness", EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, watermarkLexical)),
        };

        // The real GDPR xhtml canary body's own digest, the one this run's default document-fetch
        // response actually serves: unenforcing exactly that digest's write (and no other -- the
        // scope manifest and the document-fetch evidence document keep their own real floor) proves
        // this refusal fires on the body's own custody write specifically.
        const string bodyDigest = "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d";
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
            unenforceDigest: digest => string.Equals(digest, bodyDigest, StringComparison.Ordinal));
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(801));
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(802));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(803));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(804));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(805),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1805),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNotNull(result.Refusal, "an unenforced document body must refuse the whole run, not deliver silently.");
        Assert.AreEqual(EuQueryExecutionRefusal.DocumentBodyNotHeld, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, "no retention floor");
        Assert.IsNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.IsNull(result.ScopeManifestReceipt);
    }

    /// <summary>
    /// D1-06c-EU fix one (SCOPE_RULING lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93
    /// item 1): a document-fetch GET that completes for real but classifies as the named 404 business
    /// refusal now has a faithful member in the widened <see cref="Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason"/>
    /// vocabulary (<see cref="Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed"/>),
    /// so it becomes this one object's own <c>PendingAcquisition</c> cause rather than refusing the
    /// whole run: before this fix this exact scenario refused the entire run as
    /// <see cref="EuQueryExecutionRefusal.DocumentFetchOutcomeNotRepresentable"/> (see that member's
    /// own remarks for what genuinely remains unrepresentable after this fix).
    /// </summary>
    [TestMethod]
    public async Task AClassified404DocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000901.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
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
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness", EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, watermarkLexical)),
        };

        // The real 214-byte GDPR pdfa2a 404 body, the same retained canary
        // EuDocumentFetchReachabilityTests.GdprPdfa2aReachabilityMatchesTheRealObserved404WithNoRedirect
        // loads and re-hashes on every run (Fixtures/EuDocumentFetch/gdpr-pdfa2a-404-body.bin).
        var real404Body = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-pdfa2a-404-body.bin"));
        Assert.AreEqual(214, real404Body.Length, "must be exactly the retained canary's own byte length.");
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            scripts,
            documentFetchResponse: request =>
                EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.NotFound, real404Body));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(901));
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(902));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(903));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(904));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(905),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1905),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt, "the manifest is still written and held.");
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.HasCount(1, result.DocumentAcquisitionOutcomesByOrdinal!);
        var outcome = result.DocumentAcquisitionOutcomesByOrdinal[0];
        Assert.IsNull(outcome.Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed, outcome.Refusal);
    }

    /// <summary>
    /// D1-06c-EU fix one, the third of the three EU-route causes the ruling names: a redirect to a
    /// well-formed absolute-HTTPS target whose origin genuinely differs from the document-fetch
    /// route's own first hop (<see cref="Lex.V3.Contracts.Source.Http.HttpRouteIncompleteReason.RedirectTargetOriginNotAdmitted"/>,
    /// the same structural edge case <see cref="EuDocumentFetchReachabilityTests.OffOriginRedirectIsRefusedAsATypedRouteOutcomeNeverFollowed"/>
    /// proves at the route level) also becomes this one object's own <c>PendingAcquisition</c> cause
    /// rather than refusing the whole run.
    /// </summary>
    [TestMethod]
    public async Task ARedirectTargetOriginNotAdmittedDocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000911.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
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
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness", EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, watermarkLexical)),
        };

        // The same synthetic off-origin target EuDocumentFetchReachabilityTests uses: the office
        // never actually redirects off its own host, so this stays a deliberately labelled structural
        // edge case, exactly as that file's own remarks say.
        const string offOriginTarget = "https://not-publications.europa.eu.example.invalid/elsewhere";
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            scripts,
            documentFetchResponse: request =>
                EuAcquisitionTestFixture.BinaryResponse(
                    request, HttpStatusCode.SeeOther, [], location: offOriginTarget));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(911));
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(912));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(913));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(914));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(915),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1915),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt, "the manifest is still written and held.");
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.HasCount(1, result.DocumentAcquisitionOutcomesByOrdinal!);
        var outcome = result.DocumentAcquisitionOutcomesByOrdinal[0];
        Assert.IsNull(outcome.Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.RedirectTargetOriginNotAdmitted, outcome.Refusal);
    }

    /// <summary>
    /// D1-06c-EU fixes one and two together (SCOPE_RULING
    /// lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93): a run over two real objects
    /// (this seed's own root, and one discovered consolidated state) whose document fetches come back
    /// differently -- the root's own GET succeeds for real (the retained GDPR xhtml canary, exactly
    /// the bytes/digest <see cref="AFullRunOverOneSeedWithNoDiscoveredStatesDeliversWithRealMeasuredCounts"/>
    /// already established), the state's own GET 404s for real (the retained GDPR pdfa2a canary body,
    /// exactly the bytes <see cref="AClassified404DocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun"/>
    /// already established). Fix one: the run completes -- the state's own route-level refusal never
    /// blocks the root's own success in the same run. Fix two: after the run, a real
    /// <see cref="Lex.V3.Contracts.Source.Corpus.CorpusRecordSet"/> exists in custody, reopened
    /// through its own checked door (<see cref="Lex.V3.Contracts.Source.Corpus.VerifiedCorpusRecordSet.ParseAndVerify"/>,
    /// called by <see cref="Lex.V3.Ingest.CorpusRecordSetWriter.WriteAsync"/> itself, exposed here on
    /// <see cref="EuQueryExecutionResult.CorpusRecordSet"/>), naming both objects.
    /// </summary>
    /// <remarks>
    /// Both records land as <c>NotHeld</c> (body axis <c>TypedQuarantine</c>), not <c>Held</c> /
    /// <c>PendingAcquisition</c>, for a reason this test does not claim to fix: <c>EuCellarObjectDecode</c>
    /// never derives a manifestation-format observation for any object today (see this file's own
    /// remarks at its <c>format stays null; D1-05d's own manifestation slice is what could ever set
    /// it</c> comment), so <c>EuScopeProfile.ReduceBody</c>'s format contribution is always
    /// <c>typed_quarantine</c> and the body axis's own worst-wins join is always <c>typed_quarantine</c>
    /// too, for every EU object, regardless of what this run's own document fetch actually returned.
    /// Building real Format/Rights derivation is D1-05d's own separately owned work, not fix one or
    /// fix two's. What this test proves is exactly fix one and fix two's own claims: the real,
    /// per-object fetch history (<see cref="EuQueryExecutionResult.DocumentAcquisitionOutcomesByOrdinal"/>)
    /// genuinely differs between the two objects, the run completes rather than refusing, and the
    /// record set naming both is genuinely written to and reopened from custody -- <see cref="CorpusRecordSetWriterTests"/>
    /// already proves the writer itself correctly turns a supplied Held/Refused outcome into the
    /// matching record shape when the manifest's own body axis admits it; this test proves the ADAPTER
    /// now actually calls that writer, not that D1-05d's own body-admission gap is closed.
    /// </remarks>
    [TestMethod]
    public async Task AMixedRunWithOneRouteLevelRefusalAndOneHeldFetchCompletesAndWritesARecordSetNamingBoth()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        // A synthetic but validly Cellar-shaped IRI (no embedded '/' after the origin prefix, exactly
        // the ps-id shape EuDocumentFetchAddress.TryCreate admits), so this discovered state mints its
        // own real Minted fetch address and its own real GET, distinct from the root's.
        var stateIri = rootIri + "-refused-state";
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000921.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var censusRows = new[] { EuAcquisitionTestFixture.CensusFamilyRow(seed.Celex, rootIri, stateIri) };

        var rootOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();

        // Mirrors AClosureWithDiscoveredStatesIsComputedFromThisFixturesOwnRowsNotAppendixA's own
        // state shape: work_has_resource-type -> CONSOLID_ACT (EuContentClass.Consolidation) and this
        // state's own act_consolidated_based_on_resource_legal edge agreeing with the census family's
        // own base, or decode refuses ConsolidatedBasedOnEdgeDisagreesWithFamily.
        var stateOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.WorkHasResourceType
                    ? ConsolidatedActResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (
                predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ConsolidatedBasedOnPredicate
                    ? rootIri
                    : (string?)null)))
            .ToArray();

        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, rootOutcomes)
            .Concat(EuAcquisitionTestFixture.SortedObjectFactRows(stateIri, stateOutcomes))
            .ToArray();
        Assert.AreEqual(26, pRows.Length, "13 predicate outcomes for each of 2 objects (root + 1 state).");

        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri) };
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", censusRows.Length, censusRows, EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Length, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness", EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, watermarkLexical)),
        };

        // The exact same two real retained canary bodies this file's own two single-object tests
        // already established: the GDPR xhtml 200 (the ClassifyingHandler's own default document-fetch
        // response) for the root, and the GDPR pdfa2a 404 for the discovered state -- distinguished by
        // request path, since the two objects mint two distinct Cellar keys.
        var real404Body = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-pdfa2a-404-body.bin"));
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            scripts,
            documentFetchResponse: request =>
                request.RequestUri!.AbsolutePath.EndsWith("-refused-state", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.NotFound, real404Body)
                    : EuAcquisitionTestFixture.BinaryResponse(
                        request, HttpStatusCode.OK,
                        File.ReadAllBytes(Path.Combine(
                            AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-xhtml-200-body.bin")),
                        "application/xhtml+xml;charset=UTF-8"));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(921));

        var closureObjects = new[] { rootIri, stateIri };
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, closureObjects,
            EuAcquisitionTestFixture.BuildRendererSource(922));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, closureObjects,
            EuAcquisitionTestFixture.BuildRendererSource(923));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(924));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(929),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1929),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        // ---- Fix one: the state's own route-level refusal never blocks the root's own success. ----
        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail} " +
            $"decode={result.DecodeRefusal} offendingIri={result.DecodeOffendingIri} snapshot={result.DecodeSnapshotRefusal}");
        Assert.AreEqual(2, result.ObservedObjectCount, "root + the one discovered state.");
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.HasCount(2, result.DocumentAcquisitionOutcomesByOrdinal!);

        var heldOutcomes = result.DocumentAcquisitionOutcomesByOrdinal.Values
            .Where(static outcome => outcome.Receipt is not null).ToArray();
        var refusedOutcomes = result.DocumentAcquisitionOutcomesByOrdinal.Values
            .Where(static outcome => outcome.Refusal is not null).ToArray();
        Assert.HasCount(1, heldOutcomes, "exactly the root's own real 200.");
        Assert.HasCount(1, refusedOutcomes, "exactly the state's own real 404.");
        Assert.AreEqual(
            "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d",
            heldOutcomes[0].Receipt!.Reference.ContentSha256,
            "the held receipt's own digest must be the real retained GDPR xhtml canary's.");
        Assert.AreEqual(806864, heldOutcomes[0].Receipt!.Reference.ByteLength);
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed, refusedOutcomes[0].Refusal);

        // ---- Fix two: a real CorpusRecordSet was written and reopened from custody, naming both. ----
        Assert.IsNotNull(result.CorpusRecordSetRef);
        Assert.IsNotNull(result.CorpusRecordSet);
        var records = result.CorpusRecordSet!.Set.Records;
        Assert.AreEqual(2, records.Count);
        Assert.AreEqual(result.CorpusRecordSet.Set.ManifestRef, records[0].ManifestRef);
        Assert.AreEqual(result.CorpusRecordSet.Set.RunIdentity, records[0].RunIdentity);
        Assert.AreEqual(result.CorpusRecordSet.Set.ManifestRef, records[1].ManifestRef);
        Assert.AreEqual(result.CorpusRecordSet.Set.RunIdentity, records[1].RunIdentity);

        var publisherUris = records.Select(static record => record.ObjectRef.PublisherUri)
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var expectedUris = new[] { rootIri, stateIri }.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(
            expectedUris, publisherUris, "the reopened set must name exactly this run's own two objects.");

        // Both land NotHeld/TypedQuarantine today -- this remark's own explanation of D1-05d's still-
        // open format-derivation gap, not a claim fix one or fix two closes it.
        foreach (var record in records)
        {
            Assert.AreEqual(Lex.V3.Contracts.Source.Corpus.CorpusBodyRecordKind.NotHeld, record.Body.Kind);
            Assert.AreEqual(ScopeDisposition.TypedQuarantine, record.Body.NotHeldReason);
        }
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
            EuAcquisitionTestFixture.BuildRendererSource(80),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1080),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.RecordFormNotResolved, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, seed.Celex);
        // This refusal fires before defect 3's witness traversal is ever reached.
        Assert.AreEqual(0, handler.OccurrenceCountFor("Witness"));
    }

    private const string ConsolidatedActResourceType =
        "http://publications.europa.eu/resource/authority/resource-type/CONSOLID_ACT";

    /// <summary>
    /// Required fold-in 1. Unlike every other test in this file, the expected closure here is NOT
    /// "Appendix A compared with itself": the two discovered states are literals this test itself
    /// invents and delivers through the census family's own rows, and every assertion below (the
    /// observed object count, which roots the primary enumeration binds) is computed from what this
    /// fixture actually delivered, never copied back out of <see cref="EuAppendixASeedMap"/>. Only the
    /// one seed's own root identity comes from Appendix A, because <c>requestedCelex</c> must name a
    /// real admitted seed by construction; everything downstream of that -- the closure, the object
    /// count, the root binding -- is this fixture's own arithmetic.
    /// </summary>
    [TestMethod]
    public async Task AClosureWithDiscoveredStatesIsComputedFromThisFixturesOwnRowsNotAppendixA()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        var state1Iri = rootIri + "/state-1";
        var state2Iri = rootIri + "/state-2";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var censusRows = new[]
        {
            EuAcquisitionTestFixture.CensusFamilyRow(seed.Celex, rootIri, state1Iri),
            EuAcquisitionTestFixture.CensusFamilyRow(seed.Celex, rootIri, state2Iri),
        };

        var rootOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();

        // Every discovered state must derive EuContentClass.Consolidation from its own
        // work_has_resource-type outcome, or EuCellarObjectDecode.TryDecode refuses
        // ContentClassClosurePositionMismatch: a state's closure position requires it. Each state must
        // also assert its own ConsolidatedBasedOn edge back to the root through family P, matching
        // what the census family already established for it (state consolidated_based_on base), or
        // decode refuses ConsolidatedBasedOnEdgeDisagreesWithFamily: two independently delivered
        // families describing the same relation must agree.
        var stateOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.WorkHasResourceType
                    ? ConsolidatedActResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (
                predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ConsolidatedBasedOnPredicate
                    ? rootIri
                    : (string?)null)))
            .ToArray();

        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, rootOutcomes)
            .Concat(EuAcquisitionTestFixture.SortedObjectFactRows(state1Iri, stateOutcomes))
            .Concat(EuAcquisitionTestFixture.SortedObjectFactRows(state2Iri, stateOutcomes))
            .ToArray();
        Assert.AreEqual(39, pRows.Length, "13 predicate outcomes for each of 3 objects (root + 2 states).");

        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000003.0001.01/DOC_1";
        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri) };
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", censusRows.Length, censusRows, EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Length, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessEmptyTraversalScript(
                    rootIri, watermarkLexical)),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(21));

        var closureObjects = new[] { rootIri, state1Iri, state2Iri };
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, closureObjects,
            EuAcquisitionTestFixture.BuildRendererSource(22));

        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, closureObjects,
            EuAcquisitionTestFixture.BuildRendererSource(23));

        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(24));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(29),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1029),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail} " +
            $"decode={result.DecodeRefusal} offendingIri={result.DecodeOffendingIri} snapshot={result.DecodeSnapshotRefusal}");
        Assert.AreEqual(2, handler.OccurrenceCountFor("Witness"));

        // This fixture delivered exactly 2 census rows (state-1 and state-2), 39 P rows, 1 X row and
        // 1 W row and nothing else, so every one of these measured counts -- including the observed
        // object count of 3 (root + those 2 states) below -- is this test's own arithmetic over its
        // own delivered rows, never a value read back out of Appendix A.
        var byRows = result.FamilyOutcomes
            .Where(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven)
            .Select(static outcome => outcome.DeliveredRowCount!.Value)
            .OrderBy(static value => value)
            .ToArray();
        CollectionAssert.AreEqual(new long[] { 1, 1, 2, 39 }, byRows);
        Assert.AreEqual(3, result.ObservedObjectCount, "root + the 2 states this fixture itself delivered.");
        Assert.IsNotNull(result.RootBinding);
        CollectionAssert.AreEqual(new[] { rootIri }, result.RootBinding!.DiscoveredRoots.ToArray());
    }

    /// <summary>
    /// Required fold-in 2 / defect 1's own driving test. Before the fix, <c>FilterByClosureColumn</c>
    /// silently dropped this row (a P row naming an object outside the seed's closure) with a
    /// <c>continue</c>, so the run delivered successfully with the row simply missing. After the fix
    /// the row reaches <see cref="EuCellarObjectDecode.TryDecode"/>, which refuses it by name.
    /// </summary>
    [TestMethod]
    public async Task AnOutOfClosurePRowIsRefusedByNameRatherThanSilentlyDropped()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        var outOfClosureIri = rootIri + "/zzz-out-of-closure";
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000004.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes)
            .Append(EuAcquisitionTestFixture.ObjectFactRow(
                outOfClosureIri, EuAcquisitionTestFixture.ResourceLegalType,
                EuAcquisitionTestFixture.RegulationResourceType))
            .ToArray();

        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri) };
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Length, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(31));

        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri, outOfClosureIri],
            EuAcquisitionTestFixture.BuildRendererSource(32));

        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(33));

        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(34));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(35),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1035),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.ObjectDecodeRefused, result.Refusal!.Code);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ObjectFactRowNotInClosure, result.DecodeRefusal);
        Assert.AreEqual(outOfClosureIri, result.DecodeOffendingIri);
    }

    /// <summary>
    /// Required fold-in 3 / defect 2's own driving test. Before the fix,
    /// <c>CollectRootWatermarkObservations</c> added any root it saw with no check that it was a
    /// member of this run's own primary enumeration; after the fix a root W names that this run never
    /// discovered is refused by name.
    /// </summary>
    [TestMethod]
    public async Task AWRowNamingARootOutsideOIsRefused()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        var otherSeed = EuAppendixASeedMap.SeedsInCelexOrder[1];
        var otherRootIri = EuPackRootCanonicalForm.TryCanonicalize(otherSeed.WorkRoot, out _)!;
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000005.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes);

        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri) };

        // The W batch this run requests must name a real Appendix A root (EuObjectFactsDiscoveryPlan's
        // own root-watermark binder requires every batch member to be one of the 82 seeds), but this
        // run's own census only ever requests seed[0] -- so otherRootIri is a real root, just not one
        // THIS primary enumeration ever discovered, which is exactly the identity-binding gap defect 2
        // closes. Delivered in ascending key_1 order, whichever of the two IRIs that is.
        var wRows = string.CompareOrdinal(rootIri, otherRootIri) <= 0
            ? new[]
              {
                  EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical),
                  EuAcquisitionTestFixture.RootWatermarkRow(otherRootIri, watermarkLexical),
              }
            : new[]
              {
                  EuAcquisitionTestFixture.RootWatermarkRow(otherRootIri, watermarkLexical),
                  EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical),
              };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(41));

        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(42));

        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(43));

        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri, otherRootIri],
            EuAcquisitionTestFixture.BuildRendererSource(44));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(45),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1045),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.RootWatermarkBindingRefused, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, otherRootIri);
    }

    /// <summary>
    /// Required fold-in 4. Pass B's own census row differs from pass A's (a different discovered
    /// state), so <see cref="EnumerationDeliveryComparison"/>'s own comparison disagrees between the
    /// two passes. This is NOT <see cref="EuEnumerationRefusal.DeliveryProofRefused"/> (that is the
    /// executor's own refusal for when the Core comparison itself throws structurally): a plain
    /// disagreement between two otherwise well-formed passes still mints a receipt, and it is
    /// <see cref="AbsenceFamilyEnumerationProof.TryCreate"/>, reached through the adapter's own
    /// <c>TryRecordOutcome</c>, that refuses to prove the family's whole enumeration from it -- with
    /// <see cref="AbsenceFamilyEnumerationProofRefusal.PassesDeliveredDifferentSelections"/>.
    /// </summary>
    [TestMethod]
    public async Task APassBThatDiffersFromPassAIsRefusedAsPassesDeliveredDifferentSelections()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        var state1Iri = rootIri + "/state-1";
        var state2Iri = rootIri + "/state-2";

        var rowA = EuAcquisitionTestFixture.CensusFamilyRow(seed.Celex, rootIri, state1Iri);
        var rowB = EuAcquisitionTestFixture.CensusFamilyRow(seed.Celex, rootIri, state2Iri);

        var script = new EuAcquisitionTestFixture.FamilyScript("Census", new[]
        {
            EuAcquisitionTestFixture.EuCountJson(1),
            EuAcquisitionTestFixture.CensusFamilyRowsJson(new[] { rowA }),
            EuAcquisitionTestFixture.EmptyRowsJson(EuAcquisitionTestFixture.CensusFamilyProjection),
            EuAcquisitionTestFixture.EuCountJson(1),
            EuAcquisitionTestFixture.CensusFamilyRowsJson(new[] { rowB }),
            EuAcquisitionTestFixture.EmptyRowsJson(EuAcquisitionTestFixture.CensusFamilyProjection),
        });

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = script,
        };
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(51));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [],
            EuAcquisitionTestFixture.BuildRendererSource(52),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1052),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.CensusFamilyNotProven, result.Refusal!.Code);
        Assert.AreEqual(1, result.FamilyOutcomes.Count);
        var outcome = result.FamilyOutcomes[0];
        Assert.AreEqual(EuFamilyEnumerationOutcomeKind.ProofRefused, outcome.Kind);
        Assert.AreEqual(
            AbsenceFamilyEnumerationProofRefusal.PassesDeliveredDifferentSelections, outcome.ProofRefusal);
    }

    /// <summary>
    /// Required fold-in 5. Executor-level <c>PartitionRequired</c> (proven separately in
    /// <c>EuRepeatedEnumerationExecutorTests</c>) reported as a refused family all the way up through
    /// <see cref="EuQueryExecutionAdapter.RunAsync"/>'s own result shape, not just the executor's own
    /// internal outcome.
    /// </summary>
    [TestMethod]
    public async Task AnAdapterLevelPartitionRequiredBatchIsReportedAsARefusedFamily()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 1_000_000, [], EuAcquisitionTestFixture.CensusFamilyProjection),
        };
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(61));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [],
            EuAcquisitionTestFixture.BuildRendererSource(62),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1062),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.CensusFamilyNotProven, result.Refusal!.Code);
        Assert.AreEqual(1, result.FamilyOutcomes.Count);
        var outcome = result.FamilyOutcomes[0];
        Assert.AreEqual(EuFamilyEnumerationOutcomeKind.ExecutorRefused, outcome.Kind);
        Assert.IsNotNull(outcome.ExecutorRefusal);
        Assert.AreEqual(EuEnumerationRefusal.PartitionRequired, outcome.ExecutorRefusal!.Code);
    }

    /// <summary>
    /// Defect 3's own driving test, half one: proves the frozen witness plan is actually SENT over
    /// HTTP through the real EU transport rather than reconciled against an assumed-empty result. The
    /// scripted transport's own witness lane throws (<c>KeyNotFoundException</c> from the dictionary
    /// indexer inside <see cref="EuAcquisitionTestFixture.ClassifyingHandler"/>) if a witness request
    /// arrives without a scripted response, so simply not crashing already proves the query reached
    /// the transport layer; this test also asserts the exact real dispatch count so a silent
    /// zero-request "success" cannot pass unnoticed. Reverting to the old
    /// <c>Array.Empty&lt;EuFeedEntryTermination&gt;()</c> shortcut this ticket replaces would send
    /// nothing to family "Witness" at all, so the assertion below observably fails first: verified by
    /// temporarily reverting <see cref="EuQueryExecutionAdapter.RunAsync"/>'s own witness section and
    /// re-running this test, which then fails with
    /// <c>handler.OccurrenceCountFor("Witness")</c> equal to 0, not 2.
    /// </summary>
    [TestMethod]
    public async Task TheWitnessQueryIsActuallyDispatchedOverHttpRatherThanAssumedEmpty()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000071.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
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
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, watermarkLexical)),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(701));
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(702));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(703));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(704));

        Assert.AreEqual(0, handler.OccurrenceCountFor("Witness"), "no witness request should have been sent before the run.");

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(705),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1705),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        // The real driving assertion: the witness endpoint was actually reached by a real HTTP-shaped
        // request through the transport layer, exactly twice (the confirmed-empty traversal's own
        // first request plus its one empty-successor confirmation), never zero.
        Assert.AreEqual(2, handler.OccurrenceCountFor("Witness"));
        Assert.IsNotNull(result.WitnessTerminations);
        Assert.AreEqual(0, result.WitnessTerminations!.Count, "nothing changed since the census bound in this script.");
    }

    /// <summary>
    /// Defect 3's own driving test, half two: proves that a real delivered witness row -- one this
    /// run's own traversal genuinely observed beyond the census bound -- reconciles honestly as
    /// <see cref="EuFeedTerminal.UnresolvedOrAmbiguous"/> with
    /// <see cref="EuFeedUnresolvedCause.IdentityResolutionDidNotClose"/>, because no identity resolver
    /// exists in this codebase yet (see <see cref="EuFeedEntryObservation"/>'s own remarks). This is
    /// the honest-non-fabrication behavior this ticket requires: a real row, honestly unresolved,
    /// never a guessed resolution and never a silently dropped row.
    /// </summary>
    [TestMethod]
    public async Task ADeliveredWitnessRowWithNoIdentityResolutionReconcilesAsHonestlyUnresolved()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000072.0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";
        const string newEntryIri = "http://publications.europa.eu/resource/cellar/22222222-2222-2222-2222-222222222222";
        const string newWatermarkLexical = "2026-01-02T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
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
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessOneNewEntryTraversalScript(
                    rootIri, watermarkLexical, newEntryIri, newWatermarkLexical)),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(711));
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(712));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(713));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(714));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(715),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1715),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(3, handler.OccurrenceCountFor("Witness"), "one page carrying the new entry plus two confirming empty-successor requests.");

        Assert.IsNotNull(result.WitnessTerminations);
        Assert.AreEqual(1, result.WitnessTerminations!.Count, "exactly the one real entry this run's own traversal observed beyond the census bound.");
        var termination = result.WitnessTerminations[0];
        Assert.AreEqual(newEntryIri, termination.Entry.CanonicalEntryKey);
        Assert.AreEqual(EuFeedTerminal.UnresolvedOrAmbiguous, termination.Terminal);
        Assert.AreEqual(EuFeedUnresolvedCause.IdentityResolutionDidNotClose, termination.UnresolvedCause);
        Assert.AreEqual(0, termination.InPack.Count);
        Assert.AreEqual(0, termination.OutOfPack.Count);

        Assert.IsNotNull(result.WitnessReconciliation);
        Assert.AreEqual(1, result.WitnessReconciliation!.CheckedTerminationCount);
    }

    /// <summary>
    /// Defect 6's own driving test. Before the fix, a family-W row whose <c>value_kind</c> was not
    /// <c>"literal"</c> was silently skipped with a bare <c>continue</c>, contradicting defect 2's own
    /// already-shipped claim that every W row either binds by identity or is refused by name. After
    /// the fix it is refused, naming both the offending root and the actual non-literal value kind.
    /// </summary>
    [TestMethod]
    public async Task AWRowWithANonLiteralValueKindIsRefusedRatherThanSilentlyDropped()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)!;
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000073.0001.01/DOC_1";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes);
        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, expressionIri) };
        // Defect 6's own driving row: value_kind "unbound", not "literal" -- the real page template's
        // own FILTER NOT EXISTS shape for a root that carries no cmr:lastModificationDate at all.
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkUnboundRow(rootIri) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(721));
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(722));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(723));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(724));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(725),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1725),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.RootWatermarkBindingRefused, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, rootIri);
        StringAssert.Contains(result.Refusal.Detail, "unbound");
        // This refusal fires before defect 3's witness traversal is ever reached.
        Assert.AreEqual(0, handler.OccurrenceCountFor("Witness"));
    }

    /// <summary>
    /// D1-06c-EU, item 3: "The EU adapter mints a real fetch address for every EU row it produces."
    /// Direct, isolated proof of the adapter's own <c>MintFetchAddress</c> integration point (the
    /// private static method <see cref="EuQueryExecutionAdapter.RunAsync"/> calls per object),
    /// against a Cellar-authority object shaped exactly the way real WEMI decode output is (per
    /// <c>EuWemiIdentityBoundary</c>'s own <c>CellarOrigins</c> constant): <c>Authority = Cellar</c>,
    /// <c>CanonicalKey</c> the WEMI key. No full end-to-end custody re-open is needed here because
    /// <see cref="EuDocumentFetchAddressTests"/> already proves the minted address's own shape in
    /// full; this test proves only that the adapter reaches it with the right inputs.
    /// </summary>
    [TestMethod]
    public void MintFetchAddressProducesARealCellarAddressForACellarAuthorityObjectAndNotMintedOtherwise()
    {
        var method = typeof(EuQueryExecutionAdapter).GetMethod(
            "MintFetchAddress",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new AssertFailedException("EuQueryExecutionAdapter.MintFetchAddress is missing.");

        var evidenceRef = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000f1",
            new string('b', 64));
        var entityKind = new SourceRegistryMemberRef(evidenceRef, "eu_cellar_manifestation");
        const string canonicalKey = "00000000-0000-0000-0000-000000000001.0001.01";
        var canonicalKeySha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalKey)));
        var cellarObject = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            entityKind,
            "https://publications.europa.eu/resource/cellar/" + canonicalKey,
            canonicalKey,
            canonicalKeySha256,
            evidenceRef,
            null);

        // Defect 4's own fix: MintFetchAddress now returns the real EuDocumentFetchAddress alongside
        // its manifest projection (a ValueTuple, since it stays private and this door is reflection
        // only), so the caller can actually drive this route's own GET for a Minted row.
        var mintedResult = (System.Runtime.CompilerServices.ITuple)method.Invoke(null, [cellarObject])!;
        var mintedFetchAddress = (ScopeManifestFetchAddress)mintedResult[0]!;
        var mintedAddress = (EuDocumentFetchAddress?)mintedResult[1];
        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, mintedFetchAddress.Status);
        Assert.AreEqual(EuDocumentFetchAddress.AdmittedHost, mintedFetchAddress.Host);
        Assert.AreEqual("cellar/" + canonicalKey, mintedFetchAddress.ResourcePath);
        Assert.AreEqual("application/xhtml+xml", mintedFetchAddress.AcceptMediaType);
        Assert.AreEqual("eng", mintedFetchAddress.AcceptLanguage);
        Assert.IsNotNull(mintedAddress);
        Assert.AreEqual("cellar", mintedAddress!.PsName);
        Assert.AreEqual(canonicalKey, mintedAddress.PsId);
        Assert.AreEqual(EuManifestationMediaType.XhtmlXml, mintedAddress.MediaType);
        Assert.AreEqual(EuDocumentLanguage.Eng, mintedAddress.Language);

        var joluxKey = "jolux:id:legal-instrument:123";
        var joluxKeySha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(joluxKey)));
        var nonCellarObject = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            entityKind,
            "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo",
            joluxKey,
            joluxKeySha256,
            evidenceRef,
            null);
        var notMintedResult = (System.Runtime.CompilerServices.ITuple)method.Invoke(null, [nonCellarObject])!;
        var notMinted = (ScopeManifestFetchAddress)notMintedResult[0]!;
        Assert.AreEqual(ScopeManifestFetchAddressStatus.NotMinted, notMinted.Status);
        Assert.AreEqual(
            ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet,
            notMinted.NotMintedReason);
        Assert.IsNull(notMintedResult[1]);
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
