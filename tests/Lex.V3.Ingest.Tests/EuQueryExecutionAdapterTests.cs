using System.Net;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Lex.V3.Contracts.Custody;

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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(104));

        var evidenceResolver = new PermissiveEvidenceResolver(CompleteEnumerationRef);

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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
        Assert.AreEqual(
            5, result.FamilyOutcomes.Count, "one census seed plus one batch each of P, X, W and M.");
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
        // D1-05d adds family M's own six delivered rows: the real six-token listing 32003L0088 and
        // four other acts in the band return live.
        CollectionAssert.AreEqual(new long[] { 0, 1, 1, 6, 13 }, byRows);

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

        // D1-06c-EU defect nine (REVIEW_RESULT
        // lex-event-20260904T153119262Z-e51c74bf8710495fbd972b2706509922): the one Minted row this
        // run produced (the seed's own root, the only object O contains here) gets no document-fetch
        // attempt at all, because its own body axis is not AcceptedSelected and defect nine's gate
        // skips it -- before that fix this row was fetched anyway, one GET per Minted row regardless
        // of body axis.
        //
        // D1-05d changes WHY this row is skipped, and the distinction matters. The format axis is no
        // longer the blocker: family M's listing above is real, so this row's format contribution is
        // now AcceptedSelected. What still caps it is this fixture's own family-X rows, which assert
        // only expression_belongs_to_work and never expression_uses_language, so no language
        // Expression is observed at all and the language contribution is TypedQuarantine
        // (publisher_value_absent). ARealPre2004ActRecordsHeldThroughTextHtmlWhereItRecordedNotHeld
        // is the run that supplies an observed English Expression and reaches a real fetch.
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.HasCount(0, result.DocumentAcquisitionOutcomesByOrdinal!);
    }

    /// <summary>
    /// D1-06c-EU defect 4's own required test: "a successful fetch whose resulting receipt's
    /// classified floor is below what this run requires must be refused, not silently accepted as
    /// Held." The document-fetch GET itself completes as a real 200 (the default scripted response);
    /// only the custody write for that one body's own digest is unenforced. The run refuses, naming
    /// the row it happened on, rather than quietly returning a Held outcome the store never actually
    /// protected.
    /// </summary>
    /// <remarks>
    /// D1-06c-EU defect nine's own consequence: this scenario needs a document fetch to be attempted
    /// at all, which needs a genuine <see cref="ScopeDisposition.AcceptedSelected"/> body axis (see
    /// <see cref="BuildAcceptedBodyReductionInput"/>'s own remarks) -- the real decode seam can never
    /// supply one until D1-05d lands -- so this test drives
    /// <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/> directly rather than the full
    /// <see cref="EuQueryExecutionAdapter.RunAsync"/>.
    /// </remarks>
    [TestMethod]
    public async Task ASuccessfulDocumentFetchWhoseBodyCustodyWriteIsUnenforcedIsHeldAndSaysSo()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        // The real GDPR xhtml canary body's own digest, the one ClassifyingHandler's own default
        // document-fetch response actually serves: unenforcing exactly that digest's write proves
        // this refusal fires on the body's own custody write specifically.
        const string bodyDigest = "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d";
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
            unenforceDigest: digest => string.Equals(digest, bodyDigest, StringComparison.Ordinal));
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal));
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (outcomes, ladderResults, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(801),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        // RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c. This used to refuse the
        // WHOLE RUN over one row's body, discarding a body the office had already served and this
        // run had already written. The body is Held, and the record says under which guarantee.
        Assert.IsNull(refusal, $"code={refusal?.Code} detail={refusal?.Detail}");
        Assert.IsNotNull(outcomes);
        var outcome = outcomes!.Values.Single();
        Assert.IsNotNull(outcome.Receipt, "an unenforced store held these bytes and said so.");
        Assert.AreEqual(
            CustodyMembership.RetainedUnenforced,
            CorpusBodyRecord.Held(outcome.Receipt!).Floor,
            "the record derives the weaker class from this very receipt rather than asserting one.");
        _ = ladderResults;
    }

    /// <summary>
    /// The dangerous direction, at the body gate: a body this run CANNOT retain at all still refuses,
    /// and never reappears as a body held under a weaker class.
    /// </summary>
    /// <remarks>
    /// RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c draws exactly this line.
    /// "We stored it under a weaker guarantee" and "we failed to store it" are different facts, and
    /// the second must never quietly become the first. <c>CustodyHold.TryHoldAsync</c> is the one
    /// place that decides which, for both publishers, and it returns no receipt at all on a failure
    /// rather than a receipt with a softer label. The store here refuses the write outright.
    /// </remarks>
    [TestMethod]
    public async Task ADocumentBodyTheStoreCannotRetainAtAllStillRefusesRatherThanHolding()
    {
        var (outcomes, _, refusal) = await RunOneAcceptedBodyFetchAsync(
            store: new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
                failWriteDigest: (digest, occurrence) =>
                    occurrence == 2 && string.Equals(digest, CanaryBodyDigest, StringComparison.Ordinal)));

        Assert.IsNull(outcomes, "a body that cannot be retained must not come back as an outcome.");
        Assert.IsNotNull(refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.DocumentBodyNotRetained, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "the custody write failed");
    }

    /// <summary>
    /// A held record for a body NO CONSUMER CAN FIND BY DIGEST is the user finding no text, and the
    /// per-hold reopen is what stops it becoming one.
    /// </summary>
    /// <remarks>
    /// RULING lex-event-20260904T223559409Z-940e6f5dd5f540598920f6bf7849da47. The store here writes the
    /// object and returns a real receipt, so <c>CreateAsync</c>'s own obligation is satisfied: its
    /// readback goes through <c>ReadAsync(reference)</c>, one class-qualified path at the path the
    /// write just used. What fails is the lookup BY CONTENT ADDRESS ALONE through
    /// <c>CustodyRestore.ReadByDigestCheckedAsync</c>, which is the reader every downstream
    /// consumer uses. The two readbacks prove different properties, which is why the write
    /// obligation does not subsume the reopen and why the reopen stays. Remove it and this test is
    /// the one that goes red.
    /// </remarks>
    [TestMethod]
    public async Task ADocumentBodyNoConsumerCouldFindByDigestRefusesRatherThanHolding()
    {
        var (outcomes, _, refusal) = await RunOneAcceptedBodyFetchAsync(
            store: new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
                loseBytesAfterWriteDigest: (digest, occurrence) =>
                    occurrence == 2 && string.Equals(digest, CanaryBodyDigest, StringComparison.Ordinal)));

        Assert.IsNull(outcomes, "a body nothing can resolve by digest must not be recorded as held.");
        Assert.IsNotNull(refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.DocumentBodyNotRetained, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "could not reproduce those exact");
    }

    /// <summary>
    /// The other half of the same line: the store cannot reproduce the bytes it just wrote. Under
    /// <see cref="Lex.V3.Contracts.Custody.ICustodyStore.CreateAsync"/>'s own stated obligation that
    /// is raised as <see cref="Lex.V3.Contracts.Custody.CustodyIntegrityException"/> from the write
    /// itself, because a receipt exists only after the store has read the bytes back. So this is
    /// caught where the write is caught, which is why <c>CustodyHold</c> no longer reopens.
    /// </summary>
    [TestMethod]
    public async Task ADocumentBodyTheStoreCannotReproduceStillRefusesRatherThanHolding()
    {
        var (outcomes, _, refusal) = await RunOneAcceptedBodyFetchAsync(
            store: new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
                raiseIntegrityOnWriteDigest: (digest, occurrence) =>
                    occurrence == 2 && string.Equals(digest, CanaryBodyDigest, StringComparison.Ordinal)));

        Assert.IsNull(outcomes);
        Assert.IsNotNull(refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.DocumentBodyNotRetained, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "the custody write failed");
        StringAssert.Contains(refusal.Detail, "CustodyIntegrityException");
    }

    /// <summary>
    /// The real GDPR xhtml canary body's own digest, which
    /// <see cref="EuAcquisitionTestFixture.ClassifyingHandler"/>'s default document-fetch response
    /// actually serves. Observed live on 2026-09-04 and again on 2026-09-05 at 806,864 bytes.
    /// </summary>
    private const string CanaryBodyDigest =
        "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d";

    /// <summary>
    /// One accepted-body document fetch through the real
    /// <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/>, against whichever store the
    /// caller wants to script. Extracted so the three custody outcomes at this gate (held unenforced,
    /// write refused, bytes irreproducible) are the same scenario differing only in the store.
    /// </summary>
    private static async Task<(
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? Outcomes,
        IReadOnlyDictionary<int, EuDocumentLadderResult>? LadderResults,
        EuQueryExecutionRefusalDetail? Refusal)>
        RunOneAcceptedBodyFetchAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore store)
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal));
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        return await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(801),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);
    }

    /// <summary>
    /// D1-06c-EU fix one (SCOPE_RULING lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93
    /// item 1): a document-fetch GET that completes for real but classifies as the named 404 business
    /// refusal now has a faithful member in the widened <see cref="Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason"/>
    /// vocabulary (<see cref="Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed"/>),
    /// so it becomes this one object's own <c>PendingAcquisition</c> cause rather than refusing the
    /// whole run.
    /// </summary>
    /// <remarks>
    /// D1-06c-EU defect nine's own consequence: this scenario needs a document fetch to be attempted
    /// at all, which needs a genuine <see cref="ScopeDisposition.AcceptedSelected"/> body axis (see
    /// <see cref="BuildAcceptedBodyReductionInput"/>'s own remarks) that the real decode seam cannot
    /// yet supply until D1-05d lands, so this test drives
    /// <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/> directly rather than the full
    /// <see cref="EuQueryExecutionAdapter.RunAsync"/>.
    /// </remarks>
    [TestMethod]
    public async Task AClassified404DocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        // The real 214-byte GDPR pdfa2a 404 body, the same retained canary
        // EuDocumentFetchReachabilityTests.GdprPdfa2aReachabilityMatchesTheRealObserved404WithNoRedirect
        // loads and re-hashes on every run (Fixtures/EuDocumentFetch/gdpr-pdfa2a-404-body.bin).
        var real404Body = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-pdfa2a-404-body.bin"));
        Assert.AreEqual(214, real404Body.Length, "must be exactly the retained canary's own byte length.");
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            documentFetchResponse: request =>
                EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.NotFound, real404Body));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (outcomes, ladderResults, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(901),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        var outcome = outcomes[0];
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
    /// <remarks>
    /// D1-06c-EU defect nine's own consequence: this scenario needs a document fetch to be attempted
    /// at all, which needs a genuine <see cref="ScopeDisposition.AcceptedSelected"/> body axis (see
    /// <see cref="BuildAcceptedBodyReductionInput"/>'s own remarks) that the real decode seam cannot
    /// yet supply until D1-05d lands, so this test drives
    /// <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/> directly rather than the full
    /// <see cref="EuQueryExecutionAdapter.RunAsync"/>.
    /// </remarks>
    [TestMethod]
    public async Task ARedirectTargetOriginNotAdmittedDocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        // The same synthetic off-origin target EuDocumentFetchReachabilityTests uses: the office
        // never actually redirects off its own host, so this stays a deliberately labelled structural
        // edge case, exactly as that file's own remarks say.
        const string offOriginTarget = "https://not-publications.europa.eu.example.invalid/elsewhere";
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            documentFetchResponse: request =>
                EuAcquisitionTestFixture.BinaryResponse(
                    request, HttpStatusCode.SeeOther, [], location: offOriginTarget));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (outcomes, ladderResults, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(911),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        var outcome = outcomes[0];
        Assert.IsNull(outcome.Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.RedirectTargetOriginNotAdmitted, outcome.Refusal);
    }

    /// <summary>
    /// D1-06c-EU fixes one and two (SCOPE_RULING
    /// lex-event-20260904T141600712Z-0b823f7143154a608f01ec8f757f9e93) together with defect nine
    /// (REVIEW_RESULT lex-event-20260904T153119262Z-e51c74bf8710495fbd972b2706509922): a run over two
    /// real objects whose document fetches come back differently -- one succeeds for real (the
    /// retained GDPR xhtml canary, exactly the bytes/digest
    /// <see cref="AFullRunOverOneSeedWithNoDiscoveredStatesDeliversWithRealMeasuredCounts"/> already
    /// established), the other 404s for real (the retained GDPR pdfa2a canary body, exactly the bytes
    /// <see cref="AClassified404DocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun"/>
    /// already established) -- completes rather than refusing (fix one), and a real
    /// <see cref="Lex.V3.Contracts.Source.Corpus.CorpusRecordSet"/> written by
    /// <see cref="Lex.V3.Ingest.CorpusRecordSetWriter.WriteAsync"/> (fix two) names both as a real
    /// <c>Held</c> record and a real <c>PendingAcquisition</c> record, not <c>NotHeld</c> for both.
    /// </summary>
    /// <remarks>
    /// Drives <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/> and
    /// <see cref="CorpusRecordSetWriter.WriteAsync"/> directly, over a manifest this test builds
    /// through the real, unmodified <see cref="EuScopeProfile.BuildScopeInput"/> and
    /// <see cref="ScopeReducer.Reduce"/> production functions -- not through the full
    /// <see cref="EuQueryExecutionAdapter.RunAsync"/> (census, family P/X/W,
    /// <see cref="EuCellarObjectDecode"/>). That is not a shortcut: nothing in this codebase yet
    /// derives a real <see cref="EuFormatDisposition"/> for a decoded snapshot (see
    /// <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/>'s own remarks), so
    /// <see cref="EuScopeSnapshotReduction.Reduce"/>'s own body-axis join can never reach
    /// <see cref="ScopeDisposition.AcceptedSelected"/> through <c>RunAsync</c>'s real decode seam
    /// until D1-05d lands -- the exact reason this test used to assert <c>NotHeld</c> for both
    /// objects, which the D1-06c-EU defect-nine verdict rejects as leaving the <c>Held</c> and
    /// <c>Refused</c> record paths never exercised through the adapter by any test. This test instead
    /// supplies the four body-axis contributions <see cref="BuildAcceptedBodyReductionInput"/>'s own
    /// remarks list directly (real, valid dispositions, never fabricated placeholders), so the
    /// manifest those production functions produce carries a genuine, honestly-reduced
    /// <c>AcceptedSelected</c> body axis for both objects -- proving the adapter's own fetch gate and
    /// the writer's own record shaping against a real accepted-body run, rather than the always-
    /// <c>TypedQuarantine</c> shape every real EU run produces today.
    /// </remarks>
    [TestMethod]
    public async Task AMixedRunWithOneRouteLevelRefusalAndOneHeldFetchCompletesAndWritesARecordSetNamingBoth()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        // A synthetic but validly Cellar-shaped IRI (no embedded '/' after the origin prefix, exactly
        // the ps-id shape EuDocumentFetchAddress.TryCreate admits), so this second object mints its
        // own real Minted fetch address and its own real GET, distinct from the root's.
        var stateIri = rootIri + "-defect9-refused-state";

        var scopeProfile = EuScopeProfile.BuildBinding();
        var evidenceResolver = new PermissiveEvidenceResolver(CompleteEnumerationRef);
        var (rootInput, rootAddress) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var (stateInput, stateAddress) = BuildAcceptedBodyReductionInput(stateIri, scopeProfile);

        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [rootInput.ObjectRef, stateInput.ObjectRef],
            [rootInput, stateInput],
            evidenceResolver).Manifest;

        // This test's own claim, checked directly rather than assumed from how the inputs above were
        // built: both objects genuinely carry AcceptedSelected on the body axis.
        var bodyAccepted = manifest.Accounting
            .Where(static set => set.Axis == ScopeAxis.Body && set.Disposition == ScopeDisposition.AcceptedSelected)
            .SelectMany(static set => set.ObjectOrdinals)
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { 0, 1 }, bodyAccepted);

        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [rootInput.ObjectRef] = new[] { rootAddress },
            [stateInput.ObjectRef] = new[] { stateAddress },
        };

        // The exact same two real retained canary bodies this file's own two single-object tests
        // already established: the GDPR xhtml 200 for the root, the GDPR pdfa2a 404 for the second
        // object -- distinguished by request path, since the two objects mint two distinct Cellar keys.
        var real404Body = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-pdfa2a-404-body.bin"));
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            documentFetchResponse: request =>
                request.RequestUri!.AbsolutePath.EndsWith("-defect9-refused-state", StringComparison.Ordinal)
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

        var (outcomes, ladderResults, acquisitionRefusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(9001),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        // ---- Fix one, defect nine: the second object's own route-level refusal never blocks the
        // root's own success, and both were genuinely fetched since both carry an accepted body axis.
        // ----
        Assert.IsNull(acquisitionRefusal, acquisitionRefusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(2, outcomes!);

        var heldOutcomes = outcomes.Values.Where(static outcome => outcome.Receipt is not null).ToArray();
        var refusedOutcomes = outcomes.Values.Where(static outcome => outcome.Refusal is not null).ToArray();
        Assert.HasCount(1, heldOutcomes, "exactly the root's own real 200.");
        Assert.HasCount(1, refusedOutcomes, "exactly the second object's own real 404.");
        Assert.AreEqual(
            "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d",
            heldOutcomes[0].Receipt!.Reference.ContentSha256,
            "the held receipt's own digest must be the real retained GDPR xhtml canary's.");
        Assert.AreEqual(806864, heldOutcomes[0].Receipt!.Reference.ByteLength);
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed, refusedOutcomes[0].Refusal);

        // ---- Fix two: CorpusRecordSetWriter (already proven in isolation by
        // CorpusRecordSetWriterTests) turns these two real outcomes into a real Held record and a
        // real PendingAcquisition record, over the real accepted-body manifest this test built. ----
        var manifestRef = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string('b', 64));
        var runIdentityRef = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string('c', 64));
        var recordSetWriter = new CorpusRecordSetWriter(store);
        var writeResult = await recordSetWriter.WriteAsync(
            manifest, manifestRef, runIdentityRef, outcomes, CancellationToken.None);

        Assert.IsNull(writeResult.Refusal, writeResult.Refusal?.Detail);
        Assert.IsNotNull(writeResult.VerifiedSet);
        var records = writeResult.VerifiedSet!.Set.Records;
        Assert.AreEqual(2, records.Count);

        var publisherUris = records.Select(static record => record.ObjectRef.PublisherUri)
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var expectedUris = new[] { rootIri, stateIri }.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(
            expectedUris, publisherUris, "the reopened set must name exactly this test's own two objects.");

        var heldRecord = records.Single(record => record.ObjectRef.PublisherUri == rootIri);
        Assert.AreEqual(Lex.V3.Contracts.Source.Corpus.CorpusBodyRecordKind.Held, heldRecord.Body.Kind);
        Assert.AreEqual(
            "962539af03738bf552319ff4ce42d69e5f95a576307c4dfed7bf87e81b646b9d",
            heldRecord.Body.Receipt!.Reference.ContentSha256);
        Assert.AreEqual(806864, heldRecord.Body.Receipt!.Reference.ByteLength);

        var pendingRecord = records.Single(record => record.ObjectRef.PublisherUri == stateIri);
        Assert.AreEqual(
            Lex.V3.Contracts.Source.Corpus.CorpusBodyRecordKind.PendingAcquisition, pendingRecord.Body.Kind);
        Assert.AreEqual(
            Lex.V3.Contracts.Source.Corpus.CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused,
            pendingRecord.Body.PendingAcquisitionReason!.Kind);
        Assert.AreEqual(
            CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed,
            pendingRecord.Body.PendingAcquisitionReason!.Refusal);
    }

    /// <summary>
    /// Builds one real <see cref="ScopeObjectReductionInput"/> for <paramref name="objectPublisherUri"/>
    /// whose body axis will honestly reduce to <see cref="ScopeDisposition.AcceptedSelected"/>, through
    /// the real, unmodified <see cref="EuScopeProfile.BuildScopeInput"/>. <see cref="EuScopeProfile"/>'s
    /// own <c>ReduceBody</c> is a worst-wins join over four independent contributions (channel,
    /// language, format, rights), so all four are supplied here as real, valid, non-excluded values --
    /// never fabricated placeholders -- exactly the disposition shape
    /// <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/>'s own remarks say
    /// <see cref="EuCellarObjectDecode"/> cannot yet produce for a real object until D1-05d derives a
    /// real format disposition. Supplied directly so this D1-06c-EU defect-nine test does not have to
    /// wait on that separate, later ticket.
    /// </summary>
    private static (ScopeObjectReductionInput Input, EuDocumentFetchAddress Address) BuildAcceptedBodyReductionInput(
        string objectPublisherUri, ScopeProfileBinding scopeProfile)
    {
        var evidenceOrdinals = new Dictionary<SourceArtifactRef, int> { [CompleteEnumerationRef] = 0 };
        var canonicalKey = "eu-defect9:" + objectPublisherUri;
        var canonicalKeySha256 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey)));
        var objectRef = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(CompleteEnumerationRef, "eu_consolidation_root"),
            objectPublisherUri,
            canonicalKey,
            canonicalKeySha256,
            CompleteEnumerationRef,
            null);

        var dispositions = new EuScopeObjectDispositions(
            objectRef,
            EuActForm.Regulation,
            CompleteEnumerationRef,
            new EuChannelDisposition(
                EuChannel.CellarSparqlEndpoint, EuChannelAdmission.Admitted, "defect9_channel",
                "defect9_channel_rule", CompleteEnumerationRef),
            new EuLanguageBodyDisposition(
                EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate, "defect9_language",
                "defect9_language_rule", CompleteEnumerationRef),
            new EuFormatDisposition(
                EuManifestationFormat.Xhtml, EuFormatBodyAdmission.BodyAdmitted, "defect9_format",
                CompleteEnumerationRef),
            new EuRightsDisposition(
                EuContentClass.OriginalLegalText,
                EuRightsDisposition.BasisFor(EuContentClass.OriginalLegalText),
                CompleteEnumerationRef),
            Array.Empty<EuRelationFamilyDisposition>(),
            CompleteEnumerationRef,
            null,
            CompleteEnumerationRef);

        var address = EuDocumentFetchAddress.TryCreate(
                "cellar", ExtractCellarKeyForTest(objectPublisherUri), EuManifestationMediaType.XhtmlXml,
                EuDocumentLanguage.Eng, out _)
            ?? throw new AssertFailedException($"'{objectPublisherUri}' failed to mint a real fetch address.");

        var input = EuScopeProfile.BuildScopeInput(
            scopeProfile, dispositions, evidenceOrdinals, address.ToManifestFetchAddress());
        return (input, address);
    }

    /// <summary>The identical Cellar-origin stripping <c>EuQueryExecutionAdapter.MintFetchAddress</c> applies, reproduced here since that method is private to the adapter.</summary>
    private static string ExtractCellarKeyForTest(string publisherUri)
    {
        const string httpOrigin = "http://publications.europa.eu/resource/cellar/";
        const string httpsOrigin = "https://publications.europa.eu/resource/cellar/";
        if (publisherUri.StartsWith(httpOrigin, StringComparison.Ordinal))
        {
            return publisherUri[httpOrigin.Length..];
        }

        if (publisherUri.StartsWith(httpsOrigin, StringComparison.Ordinal))
        {
            return publisherUri[httpsOrigin.Length..];
        }

        throw new AssertFailedException($"'{publisherUri}' is not a Cellar-origin IRI this test fixture recognizes.");
    }

    /// <summary>
    /// D1-06c-EU defect nine's own fold-in five (REVIEW_RESULT
    /// lex-event-20260904T153119262Z-e51c74bf8710495fbd972b2706509922): this refusal "lost its only
    /// driver" when <see cref="AClassified404DocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun"/>
    /// was rewritten to assert <see cref="CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed"/>
    /// once fix one mapped a classified 404 away from this whole-run refusal -- that 404 scenario is
    /// what used to drive this member (see that test's own remarks). Restores a real driver using a
    /// shape fix one and this defect's own fold-ins explicitly leave whole-run: a redirect loop, which
    /// <see cref="RoutedHttpEvidence"/>'s own validators refuse to represent as anything but
    /// <see cref="HttpRouteIncompleteReason.RedirectLoop"/>.
    /// </summary>
    [TestMethod]
    public async Task ARedirectLoopDocumentFetchRefusesTheWholeRunAsDocumentFetchOutcomeNotRepresentable()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        // hop 0 (the real minted address) redirects to a real, distinct, admitted-host detour path;
        // hop 1 (the detour) redirects straight back to hop 0's own URI -- an unambiguous loop by the
        // "must stop before sending a repeated route URI" rule, never a fabricated status code.
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            documentFetchResponse: request =>
                request.RequestUri!.AbsoluteUri.EndsWith("/defect9-loop-detour", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(
                        request, HttpStatusCode.SeeOther, [], location: address.ResourceUri)
                    : EuAcquisitionTestFixture.BinaryResponse(
                        request, HttpStatusCode.SeeOther, [],
                        location: "https://publications.europa.eu/resource/cellar/defect9-loop-detour"));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (outcomes, ladderResults, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(9101),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.IsNull(outcomes);
        Assert.IsNotNull(refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.DocumentFetchOutcomeNotRepresentable, refusal!.Code);
        StringAssert.Contains(refusal.Detail, "RedirectLoop");
    }

    /// <summary>
    /// A record set written under NO enforced floor is still written, and the class it observed is
    /// recorded rather than costing the run its records.
    /// </summary>
    /// <remarks>
    /// A real single-seed run, identical to
    /// <see cref="AFullRunOverOneSeedWithNoDiscoveredStatesDeliversWithRealMeasuredCounts"/>, makes
    /// several floored custody writes -- family delivery evidence, then the scope manifest, then
    /// (D1-06c-EU fix two) the corpus/6 record set as this run's own proven literal last step; the one
    /// Minted row's own body axis is TypedQuarantine (see
    /// <see cref="EuQueryExecutionAdapter.RunDocumentAcquisitionAsync"/>'s own remarks), so defect
    /// nine's own gate attempts no document fetch and there is no body write among them. This test
    /// does not hardcode a guess at the exact total: it runs the scenario once against a plain
    /// enforcing store to discover the real count, then again unenforcing exactly that last call,
    /// which fix two's own "literal last step" guarantee makes the record-set write whatever the real
    /// total turns out to be.
    /// </remarks>
    [TestMethod]
    public async Task ARecordSetWriteWhoseFloorIsUnenforcedStillDeliversAndRecordsTheWeakerClass()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        const string expressionIri = "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000941.0001.01/DOC_1";
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness", EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, watermarkLexical)),
        };

        async Task<EuQueryExecutionResult> RunOnceAsync(EuAcquisitionTestFixture.EuInMemoryCustodyStore store)
        {
            var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
            var executor = new EuRepeatedEnumerationExecutor(
                store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
            var adapter = new EuQueryExecutionAdapter(store, executor);

            var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
            var censusRequest = new EuCensusPartitionRunRequest(
                censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(941));
            var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
            var pRequest = new EuObjectFactsPartitionRunRequest(
                pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
                EuAcquisitionTestFixture.BuildRendererSource(942));
            var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
            var xRequest = new EuObjectFactsPartitionRunRequest(
                xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
                EuAcquisitionTestFixture.BuildRendererSource(943));
            var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
            var wRequest = new EuObjectFactsPartitionRunRequest(
                wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
                EuAcquisitionTestFixture.BuildRendererSource(944));

            var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
            var mRequest = new EuObjectFactsPartitionRunRequest(
                mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
                EuAcquisitionTestFixture.BuildRendererSource(1044));

            return await adapter.RunAsync(
                [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
                [
                    (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                    (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                    (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                    (mRequest, EuAcquisitionTestFixture.SourceWitness()),
                ],
                EuAcquisitionTestFixture.BuildRendererSource(945),
                EuAcquisitionTestFixture.SourceWitness(),
                EuAcquisitionTestFixture.BuildRendererSource(1945),
                EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
                new PermissiveEvidenceResolver(CompleteEnumerationRef),
                CancellationToken.None);
        }

        // Pass one, fully enforcing: discover the real total number of custody writes a real run
        // makes. Never hardcoded: family delivery itself writes to custody before the scope manifest
        // ever does, so guessing the ordinal is fragile against upstream change.
        var discoveryStore = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var firstResult = await RunOnceAsync(discoveryStore);
        Assert.IsNull(firstResult.Refusal, $"code={firstResult.Refusal?.Code} detail={firstResult.Refusal?.Detail}");
        var totalCustodyWrites = discoveryStore.CreateCallCount;
        Assert.IsTrue(totalCustodyWrites > 0);

        // Pass two, the identical scenario: unenforce exactly the last custody write. Fix two's own
        // proven "WriteAsync is this run's literal last step" guarantee is what makes that last write
        // the corpus/6 record set's, whatever the real total turned out to be.
        var result = await RunOnceAsync(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceCallOrdinal: totalCustodyWrites));

        // RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c. This used to refuse the
        // whole run at its literal last step, so a store publishing no enforcement threw away every
        // record the run had just built. The set is written and the class is recorded.
        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.CorpusRecordSet);
        Assert.IsNotNull(result.CorpusRecordSetRef);

        // GATE TWO, the scope manifest's own custody write, targeted BY ITS OWN DIGEST. Pass one
        // above ran fully enforcing, so it hands back the exact manifest this scenario writes; that
        // digest is then the one write unenforced here. Discovered rather than guessed, for the same
        // reason the ordinal above is discovered: a hardcoded digest would rot the moment the
        // manifest's canonical bytes changed for any unrelated reason.
        //
        // This test exists because the mutation that restores gate two's floor check SURVIVED the
        // first sweep. Unenforcing a session artifact does not reach it and unenforcing the last
        // write is the record set's, so nothing drove the manifest write specifically and its
        // record-and-continue behaviour was asserted nowhere.
        // Targeted by ORDINAL, discovered from pass one, because the manifest's own digest is not
        // stable across runs: it embeds a fresh urn:uuid, so its bytes differ every time while its
        // position in the write order does not. Discovering the ordinal by matching pass one's own
        // manifest receipt against pass one's own write order keeps this a measurement rather than a
        // guess. What proves the right write was hit is the class assertion below: the manifest's own
        // receipt comes back RetainedUnenforced, which only the targeted write can produce.
        var manifestOrdinal = discoveryStore.WrittenDigestsInOrder
            .Select(static (digest, index) => (Digest: digest, Ordinal: index + 1))
            .Single(entry => string.Equals(
                entry.Digest,
                firstResult.ScopeManifestReceipt!.Reference.ContentSha256,
                StringComparison.Ordinal))
            .Ordinal;
        Assert.AreNotEqual(
            totalCustodyWrites,
            manifestOrdinal,
            "the manifest must not be the last write, or this would be the record set's gate again.");

        var manifestUnenforced = await RunOnceAsync(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceCallOrdinal: manifestOrdinal));

        Assert.IsNull(
            manifestUnenforced.Refusal,
            "an unenforced scope manifest was written correctly and must not be thrown away: " +
            $"code={manifestUnenforced.Refusal?.Code} detail={manifestUnenforced.Refusal?.Detail}");
        Assert.IsNotNull(manifestUnenforced.ScopeManifestReceipt);
        Assert.AreEqual(
            CustodyMembership.RetainedUnenforced,
            CustodyMembershipClassifier.Classify(manifestUnenforced.ScopeManifestReceipt!),
            "the manifest's observed class must be recorded on the receipt the run carries out.");
        Assert.IsNotNull(manifestUnenforced.CorpusRecordSet);

        // GATE ONE, the executor's own bootstrap floor, driven through a real RunAsync. The FIRST
        // custody write a run makes is a session bootstrap artifact, before any product request, and
        // that is exactly the membership the executor used to refuse on: RULING
        // lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c. Unenforcing it used to end
        // the whole run at productRequestCount 0, so none of the later gates was even reachable.
        //
        // Only that one write is unenforced, deliberately. Unenforcing EVERY write changes the bytes
        // of every retained delivery artifact, so the evidence refs this run binds against stop
        // resolving and the run refuses for an unrelated fixture reason ("the retained SPARQL
        // evidence tuple does not bind"). That is a property of the scripted resolver, not of the
        // gates, and the store-wide condition is what the live canary on FileSystemCustodyStore
        // exercises instead.
        var everythingUnenforced = await RunOnceAsync(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceCallOrdinal: 1));

        Assert.IsNull(
            everythingUnenforced.Refusal,
            "a store that enforces nothing must still deliver: " +
            $"code={everythingUnenforced.Refusal?.Code} detail={everythingUnenforced.Refusal?.Detail} " +
            "outcomes=[" + string.Join("; ", everythingUnenforced.FamilyOutcomes.Select(
                o => $"{o.Kind}/{o.ExecutorRefusal?.Code}/{o.ExecutorRefusal?.CoreRefusalDetail}/{o.ProofRefusal}")) + "]");
        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, everythingUnenforced.Completion);
        Assert.IsNotNull(everythingUnenforced.CorpusRecordSet);
        Assert.IsNotNull(everythingUnenforced.ScopeManifestReceipt);

        // Every family proves, and each outcome RECORDS a class rather than leaving one unstated.
        Assert.IsTrue(everythingUnenforced.FamilyOutcomes.Count > 0);
        foreach (var outcome in everythingUnenforced.FamilyOutcomes)
        {
            Assert.AreEqual(
                EuFamilyEnumerationOutcomeKind.Proven,
                outcome.Kind,
                $"family {outcome.FamilyKey} did not prove: {outcome.ExecutorRefusal?.Code} {outcome.ProofRefusal}");
            Assert.IsNotNull(
                outcome.RetainedFloor,
                "a proven family must say which of the three its run was.");
        }

        // Floored, and that is the honest answer here rather than a weakness in the test: the one
        // unenforced write is a SESSION BOOTSTRAP artifact, and a delivery receipt's floor is taken
        // over the artifacts that delivery itself names. What this test proves is that the executor
        // no longer refuses the run over that artifact. That an unenforced DELIVERY member lowers
        // the proof's own class is held one layer down, where it is directly observable, by
        // RepeatedEnumerationDeliveryReceiptTests
        // .TheReceiptNamesEveryUnenforcedDigestAndStillMintsAProofCarryingThatClass.
        Assert.AreEqual(
            CustodyMembership.Floored,
            everythingUnenforced.FamilyOutcomes[0].RetainedFloor);
        // No assertion on DocumentAcquisitionOutcomesByOrdinal here on purpose. A refused result
        // passes null for it unconditionally, so asserting null on this path cannot fail and would
        // be evidence of nothing (REVIEW_RESULT lex-event-20260904T165317709Z-8282f67ac5234a68a5fa108a76840dfe
        // item 3 caught exactly that assertion here, and the freeze packet had cited it as proof).
        // Defect nine's gate is proven through a real RunAsync on the delivered path instead, by
        // AFullRunOverOneSeedWithNoDiscoveredStatesDeliversWithRealMeasuredCounts's own
        // Assert.HasCount(0, ...), which fails the moment a quarantined row is fetched again.
    }

    /// <summary>
    /// D1-06c-EU defect nine's own fold-in five: <see cref="EuDocumentFetchRefusal.WrongAcceptToken"/>
    /// already has a real driving test at the route level
    /// (<see cref="EuDocumentFetchReachabilityTests.GdprWrongAcceptTokenReachabilityMatchesTheRealObserved400"/>)
    /// and the other two named shapes already have adapter-level coverage
    /// (<see cref="AClassified404DocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun"/>,
    /// <see cref="ARedirectTargetOriginNotAdmittedDocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun"/>),
    /// but nothing drove <see cref="CorpusAcquisitionRefusalReason.WrongAcceptToken"/> through the
    /// adapter's own <c>TryMapDocumentFetchToCorpusAcquisitionRefusal</c> arm until this test.
    /// </summary>
    [TestMethod]
    public async Task AClassified400WrongAcceptTokenDocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        // The real retained GDPR wrong-token 400 canary
        // EuDocumentFetchReachabilityTests.GdprWrongAcceptTokenReachabilityMatchesTheRealObserved400
        // already establishes and re-hashes on every run.
        var real400Body = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-wrong-token-400-body.bin"));
        Assert.AreEqual(171, real400Body.Length, "must be exactly the retained canary's own byte length.");
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            documentFetchResponse: request =>
                EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.BadRequest, real400Body));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (outcomes, ladderResults, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(9201),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        var outcome = outcomes[0];
        Assert.IsNull(outcome.Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.WrongAcceptToken, outcome.Refusal);
    }

    /// <summary>
    /// D1-06c-EU defect nine's own fold-in one (REVIEW_RESULT
    /// lex-event-20260904T165317709Z-8282f67ac5234a68a5fa108a76840dfe item 1): a document fetch that
    /// completes for real at a terminal status this route has no reviewed reading for is this one
    /// object's own <see cref="CorpusAcquisitionRefusalReason.UnexpectedPublisherStatus"/> cause, not
    /// a whole-run refusal. The mapping arm existed from the previous head and was claimed driven in
    /// that head's freeze packet; it was not, and disabling it failed nothing. This test is that
    /// missing driver.
    /// </summary>
    /// <remarks>
    /// Unlike every other document-fetch fixture in this file, the 503 here is a SHAPE fixture, not a
    /// retained canary: the office was never observed answering 503 for a document fetch, so there is
    /// no real body or digest to reproduce and none is invented. What the test proves is exactly the
    /// mapping, that a completed response at an unreviewed terminal status reaches this arm and
    /// becomes this object's own typed cause; it deliberately claims nothing about what a real
    /// publisher 503 body would contain. An empty body is used for the same reason, so no fabricated
    /// bytes can be mistaken for an observation.
    /// </remarks>
    [TestMethod]
    public async Task ADocumentFetchAtAnUnreviewedTerminalStatusBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal),
            documentFetchResponse: request =>
                EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.ServiceUnavailable, []));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (outcomes, ladderResults, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(9301),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        var outcome = outcomes[0];
        Assert.IsNull(outcome.Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.UnexpectedPublisherStatus, outcome.Refusal);
    }

    /// <summary>
    /// D1-06c-EU defect nine's own fold-in three (REVIEW_RESULT
    /// lex-event-20260904T153119262Z-e51c74bf8710495fbd972b2706509922): a robots-bootstrap refusal
    /// (<see cref="EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused"/>, already driven once through
    /// the executor by <see cref="EuRepeatedEnumerationExecutorTests.ARobotsDisallowForEveryAgentRefusesTheDocumentFetchAttemptAsRobotsBootstrapRefused"/>)
    /// is this one object's own <see cref="CorpusAcquisitionRefusalReason.RobotsDisallowed"/> cause
    /// through the adapter, not a whole-run refusal.
    /// </summary>
    [TestMethod]
    public async Task ARobotsBootstrapRefusedDocumentFetchBecomesAPerObjectRefusalRatherThanRefusingTheWholeRun()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var scopeProfile = EuScopeProfile.BuildBinding();
        var (input, address) = BuildAcceptedBodyReductionInput(rootIri, scopeProfile);
        var manifest = ScopeReducer.Reduce(
            scopeProfile,
            [CompleteEnumerationRef],
            [input.ObjectRef],
            [input],
            new PermissiveEvidenceResolver(CompleteEnumerationRef)).Manifest;
        var mintedAddressesByObjectRef = new Dictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>>
        {
            [input.ObjectRef] = new[] { address },
        };

        var handler = new DocumentFetchRobotsDenyingHandler();
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (outcomes, ladderResults, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            mintedAddressesByObjectRef,
            EuAcquisitionTestFixture.BuildRendererSource(9301),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            CancellationToken.None);

        Assert.IsNull(refusal, refusal?.Detail);
        Assert.IsNotNull(outcomes);
        Assert.HasCount(1, outcomes!);
        var outcome = outcomes[0];
        Assert.IsNull(outcome.Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.RobotsDisallowed, outcome.Refusal);
    }

    /// <summary>
    /// Answers the EU robots route with an unconditional <c>Disallow: /</c> for every agent, mirroring
    /// <c>EuRepeatedEnumerationExecutorTests.DocumentFetchRobotsDenyingHandler</c> exactly (same two
    /// hosts, same 301-then-Disallow shape); throws if it ever receives a request past robots.txt, as
    /// its own correctness guard, since this test's whole point is that the product GET is never
    /// reached.
    /// </summary>
    private sealed class DocumentFetchRobotsDenyingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "publications.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                var body = System.Text.Encoding.UTF8.GetBytes("moved");
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                content.Headers.TryAddWithoutValidation(
                    "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
                {
                    Version = HttpVersion.Version11, RequestMessage = request, Content = content,
                };
                response.Headers.Location = new Uri("https://op.europa.eu/robots.txt");
                return Task.FromResult(response);
            }

            if (request.RequestUri.Host == "op.europa.eu" && request.RequestUri.AbsolutePath == "/robots.txt")
            {
                var body = System.Text.Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /\n");
                var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                content.Headers.TryAddWithoutValidation(
                    "Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Version = HttpVersion.Version11, RequestMessage = request, Content = content,
                });
            }

            throw new InvalidOperationException(
                $"Unexpected request to {request.RequestUri} -- this test's whole point is that " +
                "robots denies the document-fetch attempt before any product request is ever sent.");
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(108));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(124));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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
        // D1-05d adds family M's own six delivered rows (see the sibling full-run test).
        CollectionAssert.AreEqual(new long[] { 1, 1, 2, 6, 39 }, byRows);
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(134));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri, otherRootIri],
            EuAcquisitionTestFixture.BuildRendererSource(144));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(804));

        Assert.AreEqual(0, handler.OccurrenceCountFor("Witness"), "no witness request should have been sent before the run.");

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(814));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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
            // D1-05d: family M, the office's own manifestation listing for this run's root.
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
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

        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(824));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
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


    // ================================================================================================
    // D1-05d: the format axis, end to end. RULING
    // lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262.
    //
    // Every publisher fact these tests script was observed live on 2026-09-04 under User-Agent
    // Lex/0.1 against publications.europa.eu, on allowed paths only. CELEX 32003L0088, the Working
    // Time Directive, is an Appendix A seed. Its own family-M listing names six types (fmx4, html,
    // pdf, pdfa1a, print, xhtml); of the two this route can address, the office answers 404 to
    // Accept: application/xhtml+xml with the datastream-absent body and 200 to Accept: text/html at
    // 37,616 bytes, digest 0d23ad4953be900de8a614fea4022aa46086e0bdc2fdfd6d0fde0cd84429e4b6. That is
    // both the fall-through arm and the pre-2004 Held proof in one real object.
    // ================================================================================================

    /// <summary>
    /// The exact 404 body the office returns for a listed-but-unservable type, reconstructed from the
    /// one Cellar identifier it names. Checked against the retained digests by
    /// <see cref="TheRetainedDatastreamAbsent404BodyIsReproducedByteExactly"/>, so this is a pinned
    /// observation and not a paraphrase.
    /// </summary>
    private static byte[] DatastreamAbsent404Body(string cellarKey) => System.Text.Encoding.UTF8.GetBytes(
        "None of the requests returned successfully a redirection. The following exception was " +
        "thrown: [cellar identifier cellar:" + cellarKey +
        " does not hold a content datastream of the requested type]");

    [TestMethod]
    public void TheRetainedDatastreamAbsent404BodyIsReproducedByteExactly()
    {
        // Three separate retained observations, each reconstructed from its own Cellar identifier and
        // checked against the digest that observation actually carried. Two are this lane's own live
        // probes; the third (32006L0112) is the digest of the file PROBE_RESULT
        // lex-event-20260904T174922051Z-9b8f01162e384f1a90204a57ba7c6967 retained, so the
        // reconstruction is checked against a body a different session wrote down.
        foreach (var (cellarKey, digest) in new (string, string)[]
                 {
                     ("050dd964-4f94-4c61-ab50-89217a0d90e2",
                      "af7411942c4affead28128a23643efc5bfae06a0ec665be85f6241a486a90dfd"),
                     ("3db0a06f-cae9-433d-a229-dde3e68d6dc7",
                      "110e6c443de6074c0b8ffa1209a2319d99009be220a182f7208ce9c9e4ffb394"),
                     ("ded2ee9c-f30e-4ed1-ab74-b2b7d7a7a6b6",
                      "c2a9aa144b7d652376be1a824c607aa32d919c45c15ccf73245c73555d3df3f8"),
                 })
        {
            var body = DatastreamAbsent404Body(cellarKey);
            Assert.HasCount(214, body, cellarKey);
            Assert.AreEqual(
                digest,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(body)),
                cellarKey);
        }
    }

    /// <summary>
    /// The slice's whole point. A real pre-2004 Appendix A act whose office lists xhtml and html: the
    /// ladder attempts xhtml first (the address the manifest row carries), the office answers the
    /// datastream-absent 404, the run falls through to text/html within the same run, and the record
    /// is Held. Before D1-05d this object recorded NotHeld, because no format disposition existed at
    /// all and the body axis could never be accepted.
    /// </summary>
    [TestMethod]
    public async Task ARealPre2004ActRecordsHeldThroughTextHtmlWhereItRecordedNotHeld()
    {
        var (result, handler, store) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes,
            WorkingTimeLadderResponse);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");

        // Two GETs, not one: the ladder really did fall through rather than stopping at its first
        // candidate. Both attempts went through the same routed session and are retained.
        Assert.AreEqual(2, handler.DocumentFetchCount, "the ladder must attempt xhtml, then html.");
        CollectionAssert.AreEqual(
            new[] { "application/xhtml+xml", "text/html" },
            handler.DocumentFetchAcceptTokens.ToArray(),
            "the attempts must be in the closed ladder order, one exact Accept token each.");

        // The record is HELD, and it holds the bytes text/html served.
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.HasCount(1, result.DocumentAcquisitionOutcomesByOrdinal!);
        var outcome = result.DocumentAcquisitionOutcomesByOrdinal!.Values.Single();
        Assert.IsNotNull(outcome.Receipt, "the object must record Held, not PendingAcquisition.");
        Assert.IsNull(outcome.Refusal);
        Assert.AreEqual(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(WorkingTimeHtmlBody)),
            outcome.Receipt!.Reference.ContentSha256,
            "the held body must be the bytes text/html served, never the 404 body xhtml answered.");

        // The run NAMES the format it actually holds, which the manifest row's own single address
        // cannot: that address is still xhtml, the first attempt.
        Assert.IsNotNull(result.DocumentLadderResultsByOrdinal);
        var ladder = result.DocumentLadderResultsByOrdinal!.Values.Single();
        CollectionAssert.AreEqual(
            new[] { EuManifestationMediaType.XhtmlXml, EuManifestationMediaType.TextHtml },
            ladder.Attempted.ToArray());
        Assert.AreEqual(EuManifestationMediaType.TextHtml, ladder.Served);

        // The RULING's "the manifest row keeps ONE fetch address, the first candidate, no schema
        // bump": the row still says application/xhtml+xml, the FIRST ATTEMPT, even though this run
        // holds text/html. Reading that address back as the held format would misreport every
        // fall-through object, which is exactly why the ladder result exists beside it.
        var rowAddress = await ReopenSingleRowFetchAddressAsync(result, store);
        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, rowAddress.Status);
        Assert.AreEqual("application/xhtml+xml", rowAddress.AcceptMediaType);
        Assert.AreEqual("eng", rowAddress.AcceptLanguage);

        // The scope ruling asks for Held on the RECORD, not only on this run's own outcome map and
        // manifest row, so the reopened corpus/6 record set is where this slice's central claim is
        // finally checked: before D1-05d this object's record was NotHeld.
        Assert.IsNotNull(result.CorpusRecordSet);
        var record = result.CorpusRecordSet!.Set.Records.Single();
        Assert.AreEqual(CorpusBodyRecordKind.Held, record.Body.Kind);
    }

    /// <summary>
    /// Same act, same ladder, but the office serves neither listed candidate. The object records
    /// PendingAcquisition with RequestedRepresentationNotServed and the tried types, and no new
    /// vocabulary member was added to say it.
    /// </summary>
    [TestMethod]
    public async Task WhenEveryListedCandidate404sTheObjectRecordsRequestedRepresentationNotServed()
    {
        var (result, handler, store) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes,
            request => EuAcquisitionTestFixture.BinaryResponse(
                request,
                System.Net.HttpStatusCode.NotFound,
                DatastreamAbsent404Body(WorkingTimeCellarKey)));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(3, handler.DocumentFetchCount, "every listed candidate must be attempted.");

        var outcome = result.DocumentAcquisitionOutcomesByOrdinal!.Values.Single();
        Assert.IsNull(outcome.Receipt);
        Assert.AreEqual(CorpusAcquisitionRefusalReason.RequestedRepresentationNotServed, outcome.Refusal);

        // The tried types travel with the refusal: that is the RULING's "the tried types in evidence".
        // Three of them, because this Work's real listing carries pdf as well, and the ruled ladder's
        // fourth rung became addressable with RULING
        // lex-event-20260904T185339315Z-87d1510eccdc42a5947c41d2d8580744.
        var ladder = result.DocumentLadderResultsByOrdinal!.Values.Single();
        CollectionAssert.AreEqual(
            new[]
            {
                EuManifestationMediaType.XhtmlXml, EuManifestationMediaType.TextHtml,
                EuManifestationMediaType.ApplicationPdf,
            },
            ladder.Attempted.ToArray());
        Assert.IsNull(ladder.Served, "nothing was served, so nothing may be named as served.");
    }

    /// <summary>
    /// Typed absence keeps its one meaning: the office lists NOTHING. Family M returns its explicit
    /// absence row, no format observation is minted, the body axis is a typed gap, and no GET is sent
    /// at all. Deliberately NOT the same outcome as a ladder that ran out of candidates.
    /// </summary>
    [TestMethod]
    public async Task AnOfficeThatListsNothingRecordsTypedAbsenceAndSendsNoDocumentFetch()
    {
        var (result, handler, store) = await RunWorkingTimeDirectiveAsync(
            null,
            request => EuAcquisitionTestFixture.BinaryResponse(
                request, System.Net.HttpStatusCode.OK, [1, 2, 3]));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(
            0,
            handler.DocumentFetchCount,
            "an office that lists nothing must produce no fetch attempt at all.");
        Assert.HasCount(0, result.DocumentAcquisitionOutcomesByOrdinal!);
        Assert.HasCount(0, result.DocumentLadderResultsByOrdinal!);
    }

    /// <summary>
    /// A listing whose only addressable wording format is print reaches never-ingest, so no fetch is
    /// attempted either -- but for a different, permanent reason than the typed absence above.
    /// </summary>
    [TestMethod]
    public async Task AListingOfferingOnlyPrintSendsNoDocumentFetchEither()
    {
        var (result, handler, store) = await RunWorkingTimeDirectiveAsync(
            ["print"],
            request => EuAcquisitionTestFixture.BinaryResponse(
                request, System.Net.HttpStatusCode.OK, [1, 2, 3]));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(0, handler.DocumentFetchCount, "no digital body can ever be read off paper.");
        Assert.HasCount(0, result.DocumentAcquisitionOutcomesByOrdinal!);
    }

    /// <summary>
    /// The first candidate serving stops the ladder: a run must never keep asking for
    /// representations after it already holds one.
    /// </summary>
    [TestMethod]
    public async Task WhenTheFirstCandidateServesTheLadderStopsThereAndNamesIt()
    {
        var (result, handler, store) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes,
            request => EuAcquisitionTestFixture.BinaryResponse(
                request,
                System.Net.HttpStatusCode.OK,
                WorkingTimeHtmlBody,
                "application/xhtml+xml;charset=UTF-8"));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(1, handler.DocumentFetchCount, "the second candidate must never be asked for.");
        CollectionAssert.AreEqual(
            new[] { "application/xhtml+xml" }, handler.DocumentFetchAcceptTokens.ToArray());

        var ladder = result.DocumentLadderResultsByOrdinal!.Values.Single();
        CollectionAssert.AreEqual(
            new[] { EuManifestationMediaType.XhtmlXml }, ladder.Attempted.ToArray());
        Assert.AreEqual(EuManifestationMediaType.XhtmlXml, ladder.Served);
    }

    /// <summary>
    /// A 400 is not a 404, and the ladder must not treat it as one. An invalid Accept token answers
    /// 400 "Illegal accept header" on this channel (retained probe
    /// lex-event-20260904T130647372Z-1d98471443364a779feba8c3a524cf69), which is a defect in the
    /// request rather than a fact about the publisher's holdings, so falling through would hide it
    /// behind a second attempt that looks like a publisher answer.
    /// </summary>
    [TestMethod]
    public async Task A400EndsTheLadderAtOnceRatherThanFallingThrough()
    {
        var (result, handler, store) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes,
            request => EuAcquisitionTestFixture.BinaryResponse(
                request,
                System.Net.HttpStatusCode.BadRequest,
                System.Text.Encoding.UTF8.GetBytes("Illegal accept header")));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(
            1, handler.DocumentFetchCount, "a 400 must stop this object's ladder, not fall through.");

        var outcome = result.DocumentAcquisitionOutcomesByOrdinal!.Values.Single();
        Assert.AreEqual(CorpusAcquisitionRefusalReason.WrongAcceptToken, outcome.Refusal);
        Assert.IsNull(result.DocumentLadderResultsByOrdinal!.Values.Single().Served);
    }

    /// <summary>
    /// The ruled ladder's FOURTH rung, driven end to end. RULING
    /// lex-event-20260904T185339315Z-87d1510eccdc42a5947c41d2d8580744 admitted application/pdf as
    /// this route's tenth media type, so a Work whose listed wording formats are all unservable now
    /// falls all the way through to PDF and records Held rather than PendingAcquisition.
    /// </summary>
    /// <remarks>
    /// What is observed and what is constructed, stated separately. Every individual response shape
    /// here was observed live on 2026-09-04: the datastream-absent 404 for a listed-but-unservable
    /// type (byte-exact, see TheRetainedDatastreamAbsent404BodyIsReproducedByteExactly), and a 200
    /// to a bare Accept: application/pdf, seen on four separate works of which the largest,
    /// 32006L0112, is retained at 486,142 bytes with digest
    /// f73bd86fde543c4d36677b971890c30bf6750fe2f9c4dab166fb75176ec5be8a and the %PDF-1.4 magic. The
    /// COMBINATION is constructed: no act probed answers 404 to both xhtml and html while serving
    /// pdf, because in all five an earlier rung served. So this drives the rung with real response
    /// shapes rather than replaying one real object, and says so rather than implying otherwise.
    /// </remarks>
    [TestMethod]
    public async Task WhenBothWordingRungsAreUnservableTheLadderFallsAllTheWayThroughToPdf()
    {
        var (result, handler, _) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes,
            request => request.Headers.Accept.ToString() == "application/pdf"
                ? EuAcquisitionTestFixture.BinaryResponse(
                    request, System.Net.HttpStatusCode.OK, PdfBody, "application/pdf;charset=UTF-8")
                : EuAcquisitionTestFixture.BinaryResponse(
                    request,
                    System.Net.HttpStatusCode.NotFound,
                    DatastreamAbsent404Body(WorkingTimeCellarKey)));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(3, handler.DocumentFetchCount, "all three addressable rungs must be tried.");
        CollectionAssert.AreEqual(
            new[] { "application/xhtml+xml", "text/html", "application/pdf" },
            handler.DocumentFetchAcceptTokens.ToArray(),
            "the attempts must follow the ruled order XHTML, html, PDF/A, PDF.");

        var outcome = result.DocumentAcquisitionOutcomesByOrdinal!.Values.Single();
        Assert.IsNotNull(outcome.Receipt, "the fourth rung serving must record Held.");
        Assert.AreEqual(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(PdfBody)),
            outcome.Receipt!.Reference.ContentSha256);

        var ladder = result.DocumentLadderResultsByOrdinal!.Values.Single();
        Assert.AreEqual(EuManifestationMediaType.ApplicationPdf, ladder.Served);
    }

    /// <summary>
    /// Stand-in bytes for a served PDF body, carrying the real %PDF-1.4 header the retained
    /// observation begins with. Deliberately not the real 486,142 bytes: this repository does not
    /// commit publisher bodies as fixtures, and what this proves is which representation was fetched
    /// and held. The real 200's own observed facts are pinned in
    /// <see cref="EuManifestationMediaType.ApplicationPdf"/>'s own remarks.
    /// </summary>
    private static readonly byte[] PdfBody =
        System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\n% served-through-application-pdf\n");

    /// <summary>
    /// Fix one for REVIEW_RESULT lex-event-20260904T192428840Z-a6a8ebd26c58436aafd109a55303c12e
    /// defect one: a minted format disposition names family M's OWN delivery evidence, and not
    /// family P's.
    /// </summary>
    /// <remarks>
    /// This is asserted here rather than in the contracts test because only the adapter chooses
    /// which family's proof to hand the listing decode, and it used to hand it P's while M's own
    /// proof went unused. What this test checks, exactly: the format selector and the language
    /// selector of the same row cite DIFFERENT delivery evidence. That is the discriminating fact,
    /// because under the defect both cited family P's one ref and were equal. It does not separately
    /// name family M's ref, which the adapter does not publish anywhere a test can read; the
    /// contracts-level
    /// <c>EuCellarObjectDecodeTests.TheDecodeMintsAFormatObservationFromFamilyMsRowsAndNamesFamilyMsOwnEvidence</c>
    /// holds that half, against two refs built from deliberately different labels.
    /// </remarks>
    [TestMethod]
    public async Task AMintedFormatDispositionNamesFamilyMsOwnDeliveryEvidenceAndNotFamilyPs()
    {
        var (result, _, store) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes,
            WorkingTimeLadderResponse);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");

        // The manifest publishes each selector's evidence as an ordinal into its own ordered
        // artifact list, so both refs are read back out of the written manifest rather than assumed.
        var manifest = await ReopenManifestAsync(result, store);
        var selectors = manifest.Rows.Single().Selectors;

        SourceArtifactRef EvidenceFor(string canonicalValue)
        {
            var selector = selectors.Single(
                candidate => candidate.CanonicalValues.Count == 1 &&
                    candidate.CanonicalValues[0] == canonicalValue);
            Assert.IsNotNull(
                selector.EvidenceArtifactOrdinal, $"the '{canonicalValue}' selector cites no evidence.");
            return manifest.OrderedEvidenceArtifacts[selector.EvidenceArtifactOrdinal!.Value];
        }

        // "xhtml" is the format selector's value for this Work, "eng" the language selector's. The
        // language axis is read from families P and X, the format axis from family M, so the two
        // must cite DIFFERENT delivery evidence. Before this fix the adapter stamped family P's ref
        // on the format observation too, and these two were the same artifact.
        var formatEvidence = EvidenceFor("xhtml");
        var languageEvidence = EvidenceFor("ENG");

        Assert.AreNotEqual(
            languageEvidence,
            formatEvidence,
            "the format axis must name family M's own delivery evidence, not the sibling families'.");

        // Every OTHER selector on the row cites the sibling families' one artifact, so the format
        // selector is alone in citing family M's. This is what makes the inequality above a fact
        // about which proof the adapter passed, rather than about which two selectors were picked.
        foreach (var other in selectors.Where(
            candidate => candidate.EvidenceArtifactOrdinal is not null &&
                !(candidate.CanonicalValues.Count == 1 && candidate.CanonicalValues[0] == "xhtml")))
        {
            Assert.AreEqual(
                languageEvidence,
                manifest.OrderedEvidenceArtifacts[other.EvidenceArtifactOrdinal!.Value],
                $"selector [{string.Join(',', other.CanonicalValues)}] should rest on the sibling " +
                "families' proof.");
        }

        // The manifest really did carry two distinct artifacts, so nothing above is an artefact of
        // one of them being absent.
        Assert.HasCount(2, manifest.OrderedEvidenceArtifacts);
        CollectionAssert.Contains(manifest.OrderedEvidenceArtifacts.ToArray(), formatEvidence);
        CollectionAssert.Contains(manifest.OrderedEvidenceArtifacts.ToArray(), languageEvidence);
    }

    /// <summary>
    /// A Work listing xhtml PLUS a manifestation type this vocabulary does not know is HELD, end to
    /// end, through the real decode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OWNER RULING lex-event-20260904T205636383Z-e92b888b62c24df29fe3f8c1be5016f0: if a law can be
    /// legitimately ingested, it is ingested, and unknown is recorded and never a reason. This
    /// condition has shed two illegitimate refusals. It refused the whole seed's RUN until
    /// REVIEW_RESULT lex-event-20260904T192428840Z-a6a8ebd26c58436aafd109a55303c12e, so the day the
    /// office listed a new manifestation type anywhere in its catalogue every EU run would have
    /// refused; it then quarantined the one WORK, so this exact listing recorded NotHeld with no
    /// fetch attempted at all, while the office was serving the body over text/html the whole time.
    /// One odd token in a listing must not cost us a law we can serve.
    /// </para>
    /// <para>
    /// The listing is the real six-token band listing with one invented extra token standing in for
    /// that future type, so what changes between this test and
    /// <see cref="ARealPre2004ActRecordsHeldThroughTextHtmlWhereItRecordedNotHeld"/> is exactly the
    /// unknown token and nothing else. Both must end Held, on the same two attempts, with the same
    /// held bytes.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task AWorkListingXhtmlPlusAnUnknownTypeIsStillHeld()
    {
        var withFutureType = EuAcquisitionTestFixture.RealBandListedTypes
            .Concat(["epub3"])
            .ToArray();

        var (result, handler, store) = await RunWorkingTimeDirectiveAsync(
            withFutureType, WorkingTimeLadderResponse);

        Assert.IsNull(
            result.Refusal,
            "one unknown publisher token must never refuse the run: " +
            $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, result.Completion);

        // The known types still earn their ladder: the unknown token is ignored for it, not fatal
        // to it. Before the ruling this count was zero.
        Assert.AreEqual(2, handler.DocumentFetchCount, "the ladder must attempt xhtml, then html.");
        CollectionAssert.AreEqual(
            new[] { "application/xhtml+xml", "text/html" },
            handler.DocumentFetchAcceptTokens.ToArray(),
            "the attempts must be in the closed ladder order, one exact Accept token each.");

        // The body is HELD, with a real receipt over the bytes text/html served.
        Assert.HasCount(1, result.DocumentAcquisitionOutcomesByOrdinal!);
        var outcome = result.DocumentAcquisitionOutcomesByOrdinal!.Values.Single();
        Assert.IsNotNull(outcome.Receipt, "the Work must record Held, not PendingAcquisition.");
        Assert.IsNull(outcome.Refusal);
        Assert.AreEqual(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(WorkingTimeHtmlBody)),
            outcome.Receipt!.Reference.ContentSha256);

        var record = result.CorpusRecordSet!.Set.Records.Single();
        Assert.AreEqual(CorpusBodyRecordKind.Held, record.Body.Kind);

        // And the row carries the first candidate's address, exactly as a listing with no unknown
        // token does. Before the ruling it was NotMinted.
        var rowAddress = await ReopenSingleRowFetchAddressAsync(result, store);
        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, rowAddress.Status);
        Assert.AreEqual("application/xhtml+xml", rowAddress.AcceptMediaType);
    }

    /// <summary>
    /// GATE TWO's genuine failure: a scope manifest the store accepts and then cannot reproduce at
    /// its own digest is NOT retained, and the run refuses saying so.
    /// </summary>
    /// <remarks>
    /// The inverse mutation the design ruling requires for a re-conditioned gate, and it was missing:
    /// <see cref="EuQueryExecutionRefusal.ScopeManifestNotRetained"/> had ZERO test references, so an
    /// edit turning this genuine hold failure into a continue would have passed green. The store
    /// accepts the write and drops the bytes, which is what CustodyHold's own reopen exists to catch:
    /// "we stored it under a weaker guarantee" and "we failed to store it" must stay different facts,
    /// and only the second refuses. Targeted BY ORDINAL because the manifest embeds a fresh urn:uuid
    /// and its digest is not stable across runs.
    /// </remarks>
    [TestMethod]
    public async Task AScopeManifestTheStoreCannotReproduceRefusesAsNotRetained()
    {
        var (result, manifestOrdinal) = await RunSingleSeedLosingWriteAsync(
            static (store, firstResult) => OrdinalOf(store, firstResult.ScopeManifestReceipt!.Reference.ContentSha256));

        Assert.IsNotNull(result.Refusal, $"ordinal {manifestOrdinal} must refuse, not deliver.");
        Assert.AreEqual(EuQueryExecutionRefusal.ScopeManifestNotRetained, result.Refusal!.Code);
        StringAssert.Contains(
            result.Refusal.Detail,
            "could not reproduce those exact",
            "the refusal must carry the hold's own failure detail, not a restatement.");
        Assert.IsNull(
            result.ScopeManifestReceipt,
            "and no receipt is reported for a manifest this run could not retain.");
    }

    /// <summary>
    /// GATE THREE's genuine failure: a corpus record set the store accepts and then cannot reproduce
    /// is NOT retained, and the adapter refuses with its own code carrying the writer's detail.
    /// </summary>
    /// <remarks>
    /// The second missing inverse mutation.
    /// <see cref="EuQueryExecutionRefusal.RecordSetNotRetained"/> also had zero test references, and
    /// the doc on <see cref="ARecordSetWriteWhoseFloorIsUnenforcedStillDeliversAndRecordsTheWeakerClass"/>
    /// claimed a named test drove it, which was false. The record set is this run's LITERAL LAST
    /// custody write, which is what makes the ordinal discoverable without guessing.
    /// </remarks>
    [TestMethod]
    public async Task ARecordSetTheStoreCannotReproduceRefusesAsNotRetained()
    {
        var (result, _) = await RunSingleSeedLosingWriteAsync(
            static (store, _) => store.CreateCallCount);

        Assert.IsNotNull(result.Refusal, "a record set that cannot be retained must refuse the run.");
        Assert.AreEqual(EuQueryExecutionRefusal.RecordSetNotRetained, result.Refusal!.Code);
        StringAssert.Contains(result.Refusal.Detail, "could not reproduce those exact");
        Assert.IsNull(result.CorpusRecordSet, "and no set is reported for one this run could not retain.");
        Assert.IsNull(result.CorpusRecordSetRef);
    }

    /// <summary>
    /// The write ordinal of a digest, from a fully enforcing discovery pass's own write order.
    /// </summary>
    private static int OrdinalOf(EuAcquisitionTestFixture.EuInMemoryCustodyStore store, string digest) =>
        store.WrittenDigestsInOrder
            .Select(static (written, index) => (Digest: written, Ordinal: index + 1))
            .Single(entry => string.Equals(entry.Digest, digest, StringComparison.Ordinal))
            .Ordinal;

    /// <summary>
    /// One single-seed run twice: once fully enforcing to discover which write ordinal the caller
    /// wants, then again with that one write's bytes dropped after a successful create. Extracted so
    /// the manifest and record-set hold failures are the same scenario differing only in the ordinal.
    /// </summary>
    private static async Task<(EuQueryExecutionResult Result, int Ordinal)> RunSingleSeedLosingWriteAsync(
        Func<EuAcquisitionTestFixture.EuInMemoryCustodyStore, EuQueryExecutionResult, int> chooseOrdinal)
    {
        var discoveryStore = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (first, _, _) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes, WorkingTimeLadderResponse, discoveryStore);
        Assert.IsNull(first.Refusal, $"code={first.Refusal?.Code} detail={first.Refusal?.Detail}");

        var ordinal = chooseOrdinal(discoveryStore, first);
        Assert.IsTrue(ordinal > 0);

        var (result, _, _) = await RunWorkingTimeDirectiveAsync(
            EuAcquisitionTestFixture.RealBandListedTypes,
            WorkingTimeLadderResponse,
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(
                loseBytesAfterWriteCallOrdinal: ordinal));
        return (result, ordinal);
    }

    /// <summary>
    /// The narrowed floor at the adapter level: a listing of print plus an unknown type still fetches
    /// nothing, and still does not reach never-ingest.
    /// </summary>
    /// <remarks>
    /// This is the other side of the ruling, and it is why the amendment is a narrowing rather than a
    /// removal. There is genuinely nothing to fetch here: the only type the office named that this
    /// vocabulary knows is paper. Naming print would assert a PERMANENT exclusion, which an unread
    /// token does not license, so the observation names
    /// <c>EuManifestationFormat.NoneAdmitted</c> and the body axis is a typed gap pending a reviewed
    /// profile. Compare <see cref="AListingOfferingOnlyPrintSendsNoDocumentFetchEither"/>, which
    /// fetches nothing for the permanent reason.
    /// </remarks>
    [TestMethod]
    public async Task AListingOfPrintPlusAnUnknownTypeFetchesNothingWithoutClaimingNeverIngest()
    {
        var (result, handler, _) = await RunWorkingTimeDirectiveAsync(
            ["print", "epub3"],
            request => EuAcquisitionTestFixture.BinaryResponse(
                request, System.Net.HttpStatusCode.OK, [1, 2, 3]));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, result.Completion);
        Assert.AreEqual(
            0, handler.DocumentFetchCount, "there is no candidate to address, so nothing is asked for.");
        Assert.HasCount(0, result.DocumentAcquisitionOutcomesByOrdinal!);

        var record = result.CorpusRecordSet!.Set.Records.Single();
        Assert.AreEqual(CorpusBodyRecordKind.NotHeld, record.Body.Kind);
    }

    private const string WorkingTimeCelex = "32003L0088";
    private const string CellarResourceOrigin = "http://publications.europa.eu/resource/cellar/";

    private static string WorkingTimeCellarKey =>
        EuAppendixASeedMap.SeedsInCelexOrder.Single(seed => seed.Celex == WorkingTimeCelex)
            .WorkRoot[CellarResourceOrigin.Length..];

    /// <summary>
    /// The office's own ladder answers for 32003L0088, keyed on the exact Accept token: 404 with the
    /// real datastream-absent body for <c>application/xhtml+xml</c>, 200 for <c>text/html</c>.
    /// </summary>
    private static HttpResponseMessage WorkingTimeLadderResponse(HttpRequestMessage request) =>
        request.Headers.Accept.ToString() == "text/html"
            ? EuAcquisitionTestFixture.BinaryResponse(
                request, System.Net.HttpStatusCode.OK, WorkingTimeHtmlBody, "text/html;charset=UTF-8")
            : EuAcquisitionTestFixture.BinaryResponse(
                request, System.Net.HttpStatusCode.NotFound, DatastreamAbsent404Body(WorkingTimeCellarKey));

    /// <summary>
    /// Stand-in bytes for the served HTML body. Deliberately NOT the real 37,616 bytes of law text:
    /// this repository does not commit publisher body text as a test fixture, and what these tests
    /// prove is which representation was fetched and held, not what the act says. The real 200's own
    /// observed facts (status, content type, 37,616 bytes, digest
    /// 0d23ad4953be900de8a614fea4022aa46086e0bdc2fdfd6d0fde0cd84429e4b6) are recorded in
    /// <see cref="EuManifestationMediaType.TextHtml"/>'s own remarks instead.
    /// </summary>
    private static readonly byte[] WorkingTimeHtmlBody =
        System.Text.Encoding.UTF8.GetBytes("<html><body>served-through-text-html</body></html>");

    /// <summary>
    /// One full <see cref="EuQueryExecutionAdapter.RunAsync"/> over the Working Time Directive seed,
    /// with a real observed English Expression (so the language axis is a body candidate) and a
    /// family-M listing the caller chooses. A null <paramref name="listedTypes"/> scripts family M's
    /// own explicit absence row instead.
    /// </summary>
    private static async Task<(
        EuQueryExecutionResult Result,
        EuAcquisitionTestFixture.ClassifyingHandler Handler,
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store)>
        RunWorkingTimeDirectiveAsync(
            IReadOnlyList<string>? listedTypes,
            Func<HttpRequestMessage, HttpResponseMessage> documentFetchResponse,
            EuAcquisitionTestFixture.EuInMemoryCustodyStore? custodyStore = null)
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder.Single(entry => entry.Celex == WorkingTimeCelex);
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");
        var expressionIri = seed.WorkRoot + ".0001.01/DOC_1";
        const string watermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                PredicateIri: predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.ResourceLegalType
                    ? EuAcquisitionTestFixture.DirectiveResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes);
        var xRows = EuAcquisitionTestFixture.EnglishExpressionFactRows(rootIri, expressionIri);
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, watermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Count, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["M"] = listedTypes is null
                ? EuAcquisitionTestFixture.ManifestationAbsenceScriptFor(rootIri)
                : EuAcquisitionTestFixture.ManifestationScriptFor(rootIri, listedTypes),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, watermarkLexical)),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts, documentFetchResponse);
        var store = custodyStore ?? new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(1501));

        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var pRequest = new EuObjectFactsPartitionRunRequest(
            pPlan, pPlanId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(1502));
        var (xPlan, xPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var xRequest = new EuObjectFactsPartitionRunRequest(
            xPlan, xPlanId, EuObjectFactsQuerySet.ExpressionFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(1503));
        var (wPlan, wPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var wRequest = new EuObjectFactsPartitionRunRequest(
            wPlan, wPlanId, EuObjectFactsQuerySet.RootWatermark, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(1504));
        var (mPlan, mPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var mRequest = new EuObjectFactsPartitionRunRequest(
            mPlan, mPlanId, EuObjectFactsQuerySet.ManifestationFacts, [rootIri],
            EuAcquisitionTestFixture.BuildRendererSource(1505));

        var result = await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            [
                (pRequest, EuAcquisitionTestFixture.SourceWitness()),
                (xRequest, EuAcquisitionTestFixture.SourceWitness()),
                (wRequest, EuAcquisitionTestFixture.SourceWitness()),
                (mRequest, EuAcquisitionTestFixture.SourceWitness()),
            ],
            EuAcquisitionTestFixture.BuildRendererSource(1509),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(2509),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);

        return (result, handler, store);
    }

    /// <summary>
    /// Reopens the manifest this run actually wrote and hands back its one row's own fetch address,
    /// so a test can check what the ROW carries rather than only what the ladder attempted. The two
    /// are deliberately different facts once a ladder falls through.
    /// </summary>
    private static async Task<ScopeManifest> ReopenManifestAsync(
        EuQueryExecutionResult result, EuAcquisitionTestFixture.EuInMemoryCustodyStore store)
    {
        var bytes = await Lex.V3.Contracts.Custody.CustodyRestore.ReadByDigestCheckedAsync(
            store, result.ScopeManifestReceipt!.Reference.ContentSha256, CancellationToken.None);
        var manifestRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}", result.ScopeManifestCanonicalSha256!);
        var manifest = EuScopeManifestBindingProof.TryOpenAsEuManifest(
            manifestRef, bytes.Span, new PermissiveEvidenceResolver(CompleteEnumerationRef), out var refusal);
        Assert.IsNotNull(manifest, $"the written manifest did not reopen: {refusal}.");
        return manifest!;
    }

    private static async Task<ScopeManifestFetchAddress> ReopenSingleRowFetchAddressAsync(
        EuQueryExecutionResult result, EuAcquisitionTestFixture.EuInMemoryCustodyStore store)
    {
        var bytes = await Lex.V3.Contracts.Custody.CustodyRestore.ReadByDigestCheckedAsync(
            store, result.ScopeManifestReceipt!.Reference.ContentSha256, CancellationToken.None);
        var manifestRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}", result.ScopeManifestCanonicalSha256!);
        var manifest = EuScopeManifestBindingProof.TryOpenAsEuManifest(
            manifestRef, bytes.Span, new PermissiveEvidenceResolver(CompleteEnumerationRef), out var refusal);
        Assert.IsNotNull(manifest, $"the written manifest did not reopen: {refusal}.");
        return manifest!.Rows.Single().FetchAddress;
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

        // D1-05d: MintFetchAddress now returns the object's own ORDERED ladder alongside the manifest
        // projection, and takes the format disposition family M minted, because the candidate set
        // and its order are that listing's answer rather than this method's own constant. The
        // listing below is the real six-token one 32003L0088 returns live; the ladder it produces is
        // xhtml, html, pdf, in the ruled order, and the manifest row carries the FIRST of them.
        var listingRef = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000f2", new string('c', 64));
        var listedDisposition = EuManifestationListingDecode.Observe(
            [
                EuManifestationFormat.Formex4, EuManifestationFormat.Html, EuManifestationFormat.Pdf,
                EuManifestationFormat.PdfA1a, EuManifestationFormat.Print, EuManifestationFormat.Xhtml,
            ],
            listingRef);
        var disposition = new EuFormatDisposition(
            listedDisposition.Format, listedDisposition.Admission, listedDisposition.ReasonCode,
            listedDisposition.EvidenceRef, listedDisposition.OrderedCandidates);

        var mintedResult = (System.Runtime.CompilerServices.ITuple)method.Invoke(
            null, [cellarObject, disposition])!;
        var mintedFetchAddress = (ScopeManifestFetchAddress)mintedResult[0]!;
        var mintedLadder = (IReadOnlyList<EuDocumentFetchAddress>)mintedResult[1]!;
        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, mintedFetchAddress.Status);
        Assert.AreEqual(EuDocumentFetchAddress.AdmittedHost, mintedFetchAddress.Host);
        Assert.AreEqual("cellar/" + canonicalKey, mintedFetchAddress.ResourcePath);
        Assert.AreEqual("application/xhtml+xml", mintedFetchAddress.AcceptMediaType);
        Assert.AreEqual("eng", mintedFetchAddress.AcceptLanguage);
        Assert.HasCount(3, mintedLadder);
        Assert.AreEqual("cellar", mintedLadder[0].PsName);
        Assert.AreEqual(canonicalKey, mintedLadder[0].PsId);
        Assert.AreEqual(EuManifestationMediaType.XhtmlXml, mintedLadder[0].MediaType);
        Assert.AreEqual(EuDocumentLanguage.Eng, mintedLadder[0].Language);
        Assert.AreEqual(EuManifestationMediaType.TextHtml, mintedLadder[1].MediaType);
        Assert.AreEqual("text/html", mintedLadder[1].Accept);
        Assert.AreEqual(EuManifestationMediaType.ApplicationPdf, mintedLadder[2].MediaType);
        Assert.AreEqual("application/pdf", mintedLadder[2].Accept);
        Assert.AreEqual(mintedFetchAddress.AcceptMediaType, mintedLadder[0].Accept,
            "the manifest row's single address must be the ladder's FIRST candidate, not any other.");

        // An object with no listing at all mints nothing: no listed wording format means no fetch
        // this route could name, and a fabricated default address would claim otherwise.
        var noListingResult = (System.Runtime.CompilerServices.ITuple)method.Invoke(
            null, [cellarObject, null])!;
        Assert.AreEqual(
            ScopeManifestFetchAddressStatus.NotMinted,
            ((ScopeManifestFetchAddress)noListingResult[0]!).Status);
        Assert.HasCount(0, (IReadOnlyList<EuDocumentFetchAddress>)noListingResult[1]!);

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
        var notMintedResult = (System.Runtime.CompilerServices.ITuple)method.Invoke(
            null, [nonCellarObject, disposition])!;
        var notMinted = (ScopeManifestFetchAddress)notMintedResult[0]!;
        Assert.AreEqual(ScopeManifestFetchAddressStatus.NotMinted, notMinted.Status);
        Assert.AreEqual(
            ScopeManifestFetchAddressAbsenceReason.NoPublisherRouteYet,
            notMinted.NotMintedReason);
        Assert.HasCount(0, (IReadOnlyList<EuDocumentFetchAddress>)notMintedResult[1]!);
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
